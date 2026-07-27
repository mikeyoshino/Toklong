using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Services;

public sealed class DisputeEvidenceStoreOptions
{
    public const string StoragePathKey =
        "DisputeEvidence:StoragePath";
    public const string DevelopmentKeyPathKey =
        "DisputeEvidence:DevelopmentKeyPath";
    public const string EncryptionKeyKey =
        "DisputeEvidence:EncryptionKeyBase64";
    public const string EncryptionKeyFileKey =
        "DisputeEvidence:EncryptionKeyFile";

    public static void ValidateConfiguration(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (environment.IsDevelopment() ||
            environment.IsEnvironment("Testing"))
            return;
        var encoded = ReadConfiguredKey(configuration);
        byte[] key;
        try
        {
            key = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            key = [];
        }
        if (key.Length != 32)
            throw new InvalidOperationException(
                "Production dispute evidence requires a 32-byte base64 encryption key or absolute secret file.");
        var storagePath = configuration[StoragePathKey];
        if (string.IsNullOrWhiteSpace(storagePath) ||
            !Path.IsPathFullyQualified(storagePath))
            throw new InvalidOperationException(
                "Production DisputeEvidence:StoragePath must be an absolute persistent path.");
    }

    internal static string ReadConfiguredKey(
        IConfiguration configuration)
    {
        var encoded = configuration[EncryptionKeyKey];
        if (!string.IsNullOrWhiteSpace(encoded))
            return encoded.Trim();
        var keyFile = configuration[EncryptionKeyFileKey];
        if (string.IsNullOrWhiteSpace(keyFile))
            return "";
        if (!Path.IsPathFullyQualified(keyFile) ||
            !File.Exists(keyFile))
            throw new InvalidOperationException(
                "DisputeEvidence:EncryptionKeyFile must be an existing absolute secret file.");
        return File.ReadAllText(keyFile).Trim();
    }
}

public sealed class EncryptedDisputeEvidenceStore
    : IDisputeEvidenceStore
{
    private const string ReferencePrefix = "evidence:";
    private const byte FormatVersion = 1;
    private readonly string storagePath;
    private readonly byte[] key;

    public EncryptedDisputeEvidenceStore(
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        DisputeEvidenceStoreOptions.ValidateConfiguration(
            configuration,
            environment);
        storagePath = PersistentStoragePath.Resolve(
            environment,
            configuration,
            DisputeEvidenceStoreOptions.StoragePathKey,
            "../App_Data/dispute-evidence");
        key = ResolveKey(environment, configuration);
    }

    public async Task<StoredDisputeEvidenceFile> SaveImageAsync(
        DisputeEvidenceFileInput input,
        CancellationToken cancellationToken)
    {
        if (input.Content.Length is < 1 or > 6_000_000)
            throw new ArgumentException(
                "รูปหลักฐานต้องมีขนาดไม่เกิน 6 MB");
        if (!input.ContentType.StartsWith(
                "image/",
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "รองรับเฉพาะไฟล์รูปภาพ");
        byte[] normalized;
        try
        {
            normalized =
                await ProductImageProcessor.NormalizeAsync(
                    new ListingImageInput(
                        input.FileName,
                        input.ContentType,
                        input.Content),
                    cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new ArgumentException(
                "ไม่สามารถอ่านรูปหลักฐานนี้ได้",
                nameof(input),
                exception);
        }
        if (normalized.LongLength > 8_000_000)
            throw new ArgumentException(
                "รูปหลักฐานหลังแปลงมีขนาดใหญ่เกินไป");
        var sha256 = Convert.ToHexString(
                SHA256.HashData(normalized))
            .ToLowerInvariant();
        var protectedContent = Encrypt(normalized);
        Directory.CreateDirectory(storagePath);
        var fileName =
            $"{RandomNumberGenerator.GetHexString(24).ToLowerInvariant()}.bin";
        var destination = Path.Combine(storagePath, fileName);
        var temporary =
            $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(
                temporary,
                protectedContent,
                cancellationToken);
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
        return new StoredDisputeEvidenceFile(
            $"{ReferencePrefix}{fileName}",
            "image/jpeg",
            normalized.LongLength,
            sha256);
    }

    public async Task<DisputeEvidenceFileContent> ReadAsync(
        string storageReference,
        CancellationToken cancellationToken)
    {
        var path = ResolveReference(storageReference);
        var protectedContent = await File.ReadAllBytesAsync(
            path,
            cancellationToken);
        return new DisputeEvidenceFileContent(
            Decrypt(protectedContent),
            "image/jpeg");
    }

    public Task DeleteAsync(
        string storageReference,
        CancellationToken cancellationToken)
    {
        if (!TryResolveReference(
                storageReference,
                out var path))
            return Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private byte[] Encrypt(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(
            nonce,
            plaintext,
            ciphertext,
            tag,
            new byte[] { FormatVersion });
        var output = new byte[
            1 + nonce.Length + tag.Length + ciphertext.Length];
        output[0] = FormatVersion;
        nonce.CopyTo(output, 1);
        tag.CopyTo(output, 13);
        ciphertext.CopyTo(output, 29);
        return output;
    }

    private byte[] Decrypt(byte[] protectedContent)
    {
        if (protectedContent.Length < 30 ||
            protectedContent[0] != FormatVersion)
            throw new CryptographicException(
                "Evidence file format is invalid.");
        var nonce = protectedContent.AsSpan(1, 12);
        var tag = protectedContent.AsSpan(13, 16);
        var ciphertext = protectedContent.AsSpan(29);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(
            nonce,
            ciphertext,
            tag,
            plaintext,
            new byte[] { FormatVersion });
        return plaintext;
    }

    private string ResolveReference(string storageReference) =>
        TryResolveReference(storageReference, out var path) &&
        File.Exists(path)
            ? path
            : throw new FileNotFoundException(
                "ไม่พบไฟล์หลักฐาน");

    private bool TryResolveReference(
        string storageReference,
        out string path)
    {
        path = "";
        if (!storageReference.StartsWith(
                ReferencePrefix,
                StringComparison.Ordinal))
            return false;
        var fileName =
            storageReference[ReferencePrefix.Length..];
        if (fileName.Length == 0 ||
            fileName != Path.GetFileName(fileName) ||
            !fileName.EndsWith(
                ".bin",
                StringComparison.Ordinal))
            return false;
        var root = Path.GetFullPath(storagePath) +
                   Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(
            Path.Combine(storagePath, fileName));
        if (!candidate.StartsWith(
                root,
                StringComparison.Ordinal))
            return false;
        path = candidate;
        return true;
    }

    private static byte[] ResolveKey(
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        var encoded =
            DisputeEvidenceStoreOptions.ReadConfiguredKey(
                configuration);
        if (!string.IsNullOrWhiteSpace(encoded))
        {
            var configured = Convert.FromBase64String(encoded);
            return configured.Length == 32
                ? configured
                : throw new InvalidOperationException(
                    "Dispute evidence encryption key must be exactly 32 bytes.");
        }
        if (!environment.IsDevelopment() &&
            !environment.IsEnvironment("Testing"))
            throw new InvalidOperationException(
                "Dispute evidence encryption key is required.");
        var keyPath = PersistentStoragePath.Resolve(
            environment,
            configuration,
            DisputeEvidenceStoreOptions.DevelopmentKeyPathKey,
            "../App_Data/dispute-evidence-keys/master.key");
        Directory.CreateDirectory(
            Path.GetDirectoryName(keyPath)!);
        if (!File.Exists(keyPath))
        {
            try
            {
                using var stream = new FileStream(
                    keyPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                stream.Write(
                    RandomNumberGenerator.GetBytes(32));
            }
            catch (IOException) when (File.Exists(keyPath))
            {
                // Another process created the shared development key.
            }
        }
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                keyPath,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite);
        var key = File.ReadAllBytes(keyPath);
        return key.Length == 32
            ? key
            : throw new InvalidOperationException(
                "Development dispute evidence key is invalid.");
    }
}
