using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Security;
using Toklong.TestSupport;

namespace Toklong.Application.Tests.Shipping;

public sealed class CounterQrPersistenceTests : IDisposable
{
    private readonly string _keysPath = Path.Combine(
        Path.GetTempPath(),
        $"toklong-counter-qr-{Guid.NewGuid():N}");

    [Fact]
    public void Api_and_worker_with_same_key_directory_round_trip_without_plaintext()
    {
        Directory.CreateDirectory(_keysPath);
        var api = new CounterQrArtifactProtector(_keysPath);
        var worker = new CounterQrArtifactProtector(_keysPath);
        var plaintext = CounterQrTestPng.Create();

        var protectedArtifact = api.Protect(
            new CounterQrArtifact(plaintext, "image/png"));

        Assert.DoesNotContain(
            Convert.ToBase64String(plaintext),
            Convert.ToBase64String(protectedArtifact.Ciphertext));
        var restored = worker.Unprotect(protectedArtifact);
        Assert.Equal("image/png", restored.ContentType);
        Assert.Equal(plaintext, restored.Content);
        Assert.Equal(64, protectedArtifact.Sha256.Length);
        Assert.Equal("v1", protectedArtifact.ProtectionVersion);
    }

    [Fact]
    public void Signature_only_png_is_rejected_before_persistence()
    {
        var protector = new CounterQrArtifactProtector(_keysPath);
        var signatureOnly = new byte[]
        {
            137, 80, 78, 71, 13, 10, 26, 10,
            1, 2, 3, 4, 5, 6, 7, 8
        };

        Assert.Throws<ArgumentException>(() =>
            protector.Protect(
                new CounterQrArtifact(
                    signatureOnly,
                    "image/png")));
    }

    [Fact]
    public void Png_with_decoded_data_beyond_declared_dimensions_is_rejected()
    {
        var protector = new CounterQrArtifactProtector(_keysPath);
        var bomb = CounterQrPngFixture.Create(
            width: 64,
            height: 64,
            decodedLength: 8 * 1024 * 1024);

        Assert.InRange(bomb.Length, 1, 2 * 1024 * 1024);
        Assert.Throws<ArgumentException>(() =>
            protector.Protect(
                new CounterQrArtifact(bomb, "image/png")));
    }

    [Fact]
    public async Task Ready_resource_metadata_round_trips_through_ef()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var shipment = ConfirmedShipment(Guid.NewGuid());
        var resource = shipment.QueueCounterQr(Now);
        resource.Claim("worker-a", Now, TimeSpan.FromMinutes(1));
        resource.RecordReady(
            CounterQrRepresentation.ProviderPng,
            Enumerable.Repeat((byte)7, 64).ToArray(),
            "v1",
            new string('a', 64),
            new string('b', 64),
            Now.AddMinutes(5),
            Now,
            "worker-a");

        await using (var database = new ToklongDbContext(options))
        {
            database.ManagedShipments.Add(shipment);
            await database.SaveChangesAsync();
        }

        await using (var database = new ToklongDbContext(options))
        {
            var restored = await database.CounterQrResources.SingleAsync();
            Assert.Equal(CounterQrResourceStatus.Ready, restored.Status);
            Assert.Equal(CounterQrRepresentation.ProviderPng, restored.Representation);
            Assert.Equal(new string('a', 64), restored.ArtifactSha256);
            Assert.Equal(new string('b', 64), restored.ProviderResourceDigest);
            Assert.Equal(64, restored.ProtectedArtifact!.Length);
        }
    }

    [Fact]
    public void Live_lease_prevents_a_second_claim()
    {
        var resource = ConfirmedShipment(Guid.NewGuid())
            .QueueCounterQr(Now);
        resource.Claim("worker-a", Now, TimeSpan.FromMinutes(1));

        Assert.Throws<Toklong.Domain.Common.DomainException>(() =>
            resource.Claim(
                "worker-b",
                Now.AddSeconds(1),
                TimeSpan.FromMinutes(1)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_keysPath))
            Directory.Delete(_keysPath, recursive: true);
    }

    private static readonly DateTimeOffset Now =
        new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private static ManagedShipment ConfirmedShipment(Guid transactionId)
    {
        var shipment = ManagedShipment.CreateOutbound(
            transactionId,
            new ManagedShipmentDraft(
                "Development",
                "origin-ref",
                "destination-ref",
                "สินค้า",
                1000,
                10,
                10,
                10,
                "THP",
                "DEV",
                "Development Counter",
                5000,
                0,
                0,
                null,
                "quote-ref",
                Now.AddMinutes(20)),
            Now);
        shipment.RecordReservation(
            "purchase-ref",
            "provider-track",
            "courier-track",
            Now);
        shipment.RecordConfirmation(
            "courier-track",
            "confirmed",
            Now);
        return shipment;
    }

    private static class CounterQrPngFixture
    {
        private static readonly byte[] Signature =
            [137, 80, 78, 71, 13, 10, 26, 10];

        public static byte[] Create(
            int width,
            int height,
            int decodedLength)
        {
            using var output = new MemoryStream();
            output.Write(Signature);
            Span<byte> ihdr = stackalloc byte[13];
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt32BigEndian(ihdr[..4], (uint)width);
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt32BigEndian(ihdr.Slice(4, 4), (uint)height);
            ihdr[8] = 8;
            ihdr[9] = 0;
            WriteChunk(output, "IHDR", ihdr);
            using var compressed = new MemoryStream();
            using (var zlib = new System.IO.Compression.ZLibStream(
                       compressed,
                       System.IO.Compression.CompressionLevel.SmallestSize,
                       leaveOpen: true))
                zlib.Write(new byte[decodedLength]);
            WriteChunk(output, "IDAT", compressed.ToArray());
            WriteChunk(output, "IEND", []);
            return output.ToArray();
        }

        private static void WriteChunk(
            Stream output,
            string type,
            ReadOnlySpan<byte> data)
        {
            Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt32BigEndian(length, (uint)data.Length);
            output.Write(length);
            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            output.Write(typeBytes);
            output.Write(data);
            Span<byte> crc = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                crc,
                Crc32(typeBytes, data));
            output.Write(crc);
        }

        private static uint Crc32(
            ReadOnlySpan<byte> type,
            ReadOnlySpan<byte> data)
        {
            var crc = uint.MaxValue;
            foreach (var value in type)
                crc = Update(crc, value);
            foreach (var value in data)
                crc = Update(crc, value);
            return ~crc;
        }

        private static uint Update(uint crc, byte value)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0
                    ? (crc >> 1) ^ 0xedb88320u
                    : crc >> 1;
            return crc;
        }
    }
}
