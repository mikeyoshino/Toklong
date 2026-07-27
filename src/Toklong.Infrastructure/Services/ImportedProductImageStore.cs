using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Services;

public sealed class ImportedProductImageStore(
    IHostEnvironment environment,
    IConfiguration configuration)
    : IImportedProductImageStore
{
    public const string RequestPath = "/media/product-imports";
    public const string StorageFolder = "App_Data/product-imports";
    public const string StoragePathConfigurationKey =
        "ProductImages:StoragePath";

    private readonly string storagePath = PersistentStoragePath.Resolve(
        environment,
        configuration,
        StoragePathConfigurationKey,
        StorageFolder);

    public static string ResolveStoragePath(
        IHostEnvironment environment,
        IConfiguration configuration) =>
        PersistentStoragePath.Resolve(
            environment,
            configuration,
            StoragePathConfigurationKey,
            StorageFolder);

    public async Task<string> SaveAsync(
        ListingImageInput image,
        CancellationToken cancellationToken)
    {
        var normalized = await ProductImageProcessor.NormalizeAsync(image, cancellationToken);
        Directory.CreateDirectory(storagePath);
        var fileName = $"{RandomNumberGenerator.GetHexString(24).ToLowerInvariant()}.jpg";
        await File.WriteAllBytesAsync(
            Path.Combine(storagePath, fileName),
            normalized,
            cancellationToken);
        return $"{RequestPath}/{fileName}";
    }

    public Task DeleteAsync(
        string fileReference,
        CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(
                fileReference,
                UriKind.Absolute,
                out var absolute))
            fileReference =
                Uri.UnescapeDataString(
                    absolute.AbsolutePath);
        var prefix = $"{RequestPath}/";
        if (!fileReference.StartsWith(
                prefix,
                StringComparison.Ordinal))
            return Task.CompletedTask;
        var fileName = fileReference[prefix.Length..];
        if (fileName.Length == 0 ||
            fileName != Path.GetFileName(fileName))
            return Task.CompletedTask;
        cancellationToken
            .ThrowIfCancellationRequested();
        var candidate = Path.GetFullPath(
            Path.Combine(storagePath, fileName));
        var root = Path.GetFullPath(storagePath) +
                   Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(
                root,
                StringComparison.Ordinal))
            return Task.CompletedTask;
        if (File.Exists(candidate))
            File.Delete(candidate);
        return Task.CompletedTask;
    }
}
