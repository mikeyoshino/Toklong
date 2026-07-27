using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SkiaSharp;
using Toklong.Application.Abstractions;
using Toklong.Infrastructure.Services;

namespace Toklong.Application.Tests.Security;

public sealed class EncryptedDisputeEvidenceStoreTests
{
    [Fact]
    public void Production_configuration_accepts_key_from_absolute_secret_file()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var keyFile = Path.Combine(root, "evidence-key");
            File.WriteAllText(
                keyFile,
                Convert.ToBase64String(
                    RandomNumberGenerator.GetBytes(32)));
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["DisputeEvidence:StoragePath"] =
                            Path.Combine(root, "evidence"),
                        ["DisputeEvidence:EncryptionKeyFile"] =
                            keyFile
                    })
                .Build();

            DisputeEvidenceStoreOptions.ValidateConfiguration(
                configuration,
                new TestEnvironment(root)
                {
                    EnvironmentName = "Production"
                });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Production_configuration_rejects_relative_key_file()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["DisputeEvidence:StoragePath"] =
                            Path.Combine(root, "evidence"),
                        ["DisputeEvidence:EncryptionKeyFile"] =
                            "evidence-key"
                    })
                .Build();

            Assert.Throws<InvalidOperationException>(() =>
                DisputeEvidenceStoreOptions.ValidateConfiguration(
                    configuration,
                    new TestEnvironment(root)
                    {
                        EnvironmentName = "Production"
                    }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Production_configuration_rejects_missing_key_file()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["DisputeEvidence:StoragePath"] =
                            Path.Combine(root, "evidence"),
                        ["DisputeEvidence:EncryptionKeyFile"] =
                            Path.Combine(root, "missing-key")
                    })
                .Build();

            Assert.Throws<InvalidOperationException>(() =>
                DisputeEvidenceStoreOptions.ValidateConfiguration(
                    configuration,
                    new TestEnvironment(root)
                    {
                        EnvironmentName = "Production"
                    }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Stored_file_is_encrypted_and_round_trips_as_normalized_jpeg()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var store = CreateStore(root);

            var saved = await store.SaveImageAsync(
                new DisputeEvidenceFileInput(
                    "evidence.png",
                    "image/png",
                    CreatePng()),
                default);
            var fileName = saved.StorageReference["evidence:".Length..];
            var protectedBytes = await File.ReadAllBytesAsync(
                Path.Combine(root, fileName));
            var opened = await store.ReadAsync(
                saved.StorageReference,
                default);

            Assert.NotEqual(0xff, protectedBytes[0]);
            Assert.Equal("image/jpeg", opened.ContentType);
            Assert.Equal(0xff, opened.Content[0]);
            Assert.Equal(0xd8, opened.Content[1]);
            Assert.Equal(
                saved.Sha256,
                Convert.ToHexString(
                        SHA256.HashData(opened.Content))
                    .ToLowerInvariant());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Authentication_tag_detects_ciphertext_tampering()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var store = CreateStore(root);
            var saved = await store.SaveImageAsync(
                new DisputeEvidenceFileInput(
                    "evidence.png",
                    "image/png",
                    CreatePng()),
                default);
            var path = Path.Combine(
                root,
                saved.StorageReference["evidence:".Length..]);
            var bytes = await File.ReadAllBytesAsync(path);
            bytes[^1] ^= 0xff;
            await File.WriteAllBytesAsync(path, bytes);

            await Assert.ThrowsAnyAsync<CryptographicException>(() =>
                store.ReadAsync(saved.StorageReference, default));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static EncryptedDisputeEvidenceStore CreateStore(
        string root)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["DisputeEvidence:StoragePath"] = root,
                    ["DisputeEvidence:EncryptionKeyBase64"] =
                        Convert.ToBase64String(
                            Enumerable.Range(1, 32)
                                .Select(value => (byte)value)
                                .ToArray())
                })
            .Build();
        return new EncryptedDisputeEvidenceStore(
            new TestEnvironment(root),
            configuration);
    }

    private static byte[] CreatePng()
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(
            SKEncodedImageFormat.Png,
            100);
        return data.ToArray();
    }

    private static string NewTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"toklong-evidence-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestEnvironment(string contentRoot)
        : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Toklong.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
