using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography.X509Certificates;
using SkiaSharp;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Security;

public sealed class CounterQrArtifactProtector :
    ICounterQrArtifactProtector
{
    public const string ApplicationName = "Toklong.CounterQr";
    public const string Purpose =
        "Toklong.ShipmentCounterQr.v1";
    public const string CurrentVersion = "v1";
    private const int MaximumArtifactBytes = 2 * 1024 * 1024;
    private const int MinimumDimension = 64;
    private const int MaximumDimension = 1024;
    private const int MaximumDecodedBytes =
        4 * 1024 * 1024 + 1024;
    private static readonly byte[] PngSignature =
        [137, 80, 78, 71, 13, 10, 26, 10];
    private readonly IDataProtector protector;

    public CounterQrArtifactProtector(
        string keysPath,
        X509Certificate2? keyEncryptionCertificate = null)
    {
        if (string.IsNullOrWhiteSpace(keysPath))
            throw new ArgumentException(
                "Counter QR key path is required.",
                nameof(keysPath));
        var fullPath = Path.GetFullPath(keysPath);
        Directory.CreateDirectory(fullPath);
        var provider = DataProtectionProvider.Create(
            new DirectoryInfo(fullPath),
            options =>
            {
                options.SetApplicationName(ApplicationName);
                if (keyEncryptionCertificate is not null)
                    options.ProtectKeysWithCertificate(
                        keyEncryptionCertificate);
            });
        protector = provider.CreateProtector(Purpose);
    }

    internal CounterQrArtifactProtector(
        IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        protector = provider.CreateProtector(Purpose);
    }

    public ProtectedCounterQrArtifact Protect(
        CounterQrArtifact artifact)
    {
        Validate(artifact);
        var envelope = new byte[artifact.Content.Length + 1];
        envelope[0] = 1;
        artifact.Content.CopyTo(envelope, 1);
        return new ProtectedCounterQrArtifact(
            protector.Protect(envelope),
            CurrentVersion,
            Convert.ToHexString(
                    SHA256.HashData(artifact.Content))
                .ToLowerInvariant());
    }

    public CounterQrArtifact Unprotect(
        ProtectedCounterQrArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (!string.Equals(
                artifact.ProtectionVersion,
                CurrentVersion,
                StringComparison.Ordinal) ||
            artifact.Ciphertext is null ||
            artifact.Ciphertext.Length == 0)
            throw new CryptographicException(
                "Unsupported Counter QR protection envelope.");
        var envelope = protector.Unprotect(
            artifact.Ciphertext);
        if (envelope.Length is < 2 or
                > MaximumArtifactBytes + 1 ||
            envelope[0] != 1)
            throw new CryptographicException(
                "Invalid Counter QR protection envelope.");
        var content = envelope[1..];
        var sha256 = Convert.ToHexString(
                SHA256.HashData(content))
            .ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(sha256),
                Convert.FromHexString(artifact.Sha256)))
            throw new CryptographicException(
                "Counter QR artifact integrity check failed.");
        var restored = new CounterQrArtifact(
            content,
            "image/png");
        Validate(restored);
        return restored;
    }

    private static void Validate(CounterQrArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.Content is null ||
            artifact.Content.Length is < 57 or
                > MaximumArtifactBytes ||
            !string.Equals(
                artifact.ContentType,
                "image/png",
                StringComparison.OrdinalIgnoreCase) ||
            !artifact.Content.AsSpan(0, 8).SequenceEqual(
                PngSignature))
            throw new ArgumentException(
                "Counter QR artifact must be a bounded PNG.",
                nameof(artifact));
        try
        {
            ValidatePng(artifact.Content);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
                IOException or
                OverflowException or
                ArgumentOutOfRangeException)
        {
            throw new ArgumentException(
                "Counter QR artifact must be a valid bounded PNG.",
                nameof(artifact),
                exception);
        }
    }

    private static void ValidatePng(byte[] content)
    {
        var span = content.AsSpan();
        var offset = PngSignature.Length;
        var sawHeader = false;
        var sawImageData = false;
        var sawEnd = false;
        var width = 0;
        var height = 0;
        var rowBytes = 0;
        var expectedDecodedBytes = 0;
        using var compressed = new MemoryStream();

        while (offset < span.Length)
        {
            if (span.Length - offset < 12)
                throw new InvalidDataException("Truncated PNG chunk.");
            var length = checked((int)BinaryPrimitives
                .ReadUInt32BigEndian(span.Slice(offset, 4)));
            if (length > MaximumArtifactBytes ||
                length > span.Length - offset - 12)
                throw new InvalidDataException("Invalid PNG chunk length.");
            var type = span.Slice(offset + 4, 4);
            var data = span.Slice(offset + 8, length);
            var storedCrc = BinaryPrimitives.ReadUInt32BigEndian(
                span.Slice(offset + 8 + length, 4));
            if (storedCrc != Crc32(type, data))
                throw new InvalidDataException("Invalid PNG chunk CRC.");
            var chunkType = Encoding.ASCII.GetString(type);

            switch (chunkType)
            {
                case "IHDR":
                    if (sawHeader || offset != PngSignature.Length ||
                        length != 13)
                        throw new InvalidDataException(
                            "Invalid PNG header.");
                    width = checked((int)BinaryPrimitives
                        .ReadUInt32BigEndian(data[..4]));
                    height = checked((int)BinaryPrimitives
                        .ReadUInt32BigEndian(data.Slice(4, 4)));
                    var bitDepth = data[8];
                    var colorType = data[9];
                    if (width is < MinimumDimension or > MaximumDimension ||
                        height is < MinimumDimension or > MaximumDimension ||
                        width != height ||
                        data[10] != 0 ||
                        data[11] != 0 ||
                        data[12] != 0 ||
                        !ValidColorFormat(colorType, bitDepth))
                        throw new InvalidDataException(
                            "Unsupported PNG dimensions or format.");
                    var samples = colorType switch
                    {
                        0 or 3 => 1,
                        2 => 3,
                        4 => 2,
                        6 => 4,
                        _ => throw new InvalidDataException(
                            "Unsupported PNG color type.")
                    };
                    rowBytes = checked((width * samples * bitDepth + 7) / 8);
                    expectedDecodedBytes = checked(
                        (rowBytes + 1) * height);
                    if (expectedDecodedBytes > MaximumDecodedBytes)
                        throw new InvalidDataException(
                            "Decoded PNG is too large.");
                    sawHeader = true;
                    break;
                case "IDAT":
                    if (!sawHeader || sawEnd)
                        throw new InvalidDataException(
                            "PNG image data is out of order.");
                    compressed.Write(data);
                    sawImageData = true;
                    break;
                case "IEND":
                    if (!sawHeader || !sawImageData || sawEnd ||
                        length != 0 ||
                        offset + 12 != span.Length)
                        throw new InvalidDataException(
                            "Invalid PNG end chunk.");
                    sawEnd = true;
                    break;
                default:
                    if ((type[0] & 0x20) == 0 &&
                        chunkType != "PLTE")
                        throw new InvalidDataException(
                            "Unknown critical PNG chunk.");
                    break;
            }

            offset = checked(offset + 12 + length);
        }

        if (!sawEnd || expectedDecodedBytes <= 0)
            throw new InvalidDataException("Incomplete PNG.");

        compressed.Position = 0;
        var decoded = new byte[expectedDecodedBytes];
        try
        {
            using (var zlib = new ZLibStream(
                       compressed,
                       CompressionMode.Decompress,
                       leaveOpen: true))
            {
                zlib.ReadExactly(decoded);
                if (zlib.ReadByte() != -1)
                    throw new InvalidDataException(
                        "PNG expands beyond its declared dimensions.");
            }
            for (var row = 0; row < height; row++)
            {
                var filter = decoded[row * (rowBytes + 1)];
                if (filter > 4)
                    throw new InvalidDataException(
                        "Invalid PNG scanline filter.");
            }

            using var dataCopy = SKData.CreateCopy(content);
            using var codec = SKCodec.Create(dataCopy)
                ?? throw new InvalidDataException("PNG cannot be decoded.");
            if (codec.EncodedFormat != SKEncodedImageFormat.Png ||
                codec.Info.Width != width ||
                codec.Info.Height != height)
                throw new InvalidDataException(
                    "Decoded PNG metadata changed.");
            using var bitmap = new SKBitmap(codec.Info);
            var result = codec.GetPixels(
                bitmap.Info,
                bitmap.GetPixels());
            if (result != SKCodecResult.Success)
                throw new InvalidDataException("PNG pixel decode failed.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }

    private static bool ValidColorFormat(byte colorType, byte bitDepth) =>
        colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            2 or 4 or 6 => bitDepth is 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            _ => false
        };

    private static uint Crc32(
        ReadOnlySpan<byte> type,
        ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in type)
            crc = UpdateCrc32(crc, value);
        foreach (var value in data)
            crc = UpdateCrc32(crc, value);
        return ~crc;
    }

    private static uint UpdateCrc32(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
            crc = (crc & 1) != 0
                ? (crc >> 1) ^ 0xedb88320u
                : crc >> 1;
        return crc;
    }
}
