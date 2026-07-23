using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Services;

public sealed class ImportedProductImageStore(IHostEnvironment environment)
    : IImportedProductImageStore
{
    public const string RequestPath = "/media/product-imports";
    public const string StorageFolder = "App_Data/product-imports";

    public async Task<string> SaveAsync(
        ListingImageInput image,
        CancellationToken cancellationToken)
    {
        var normalized = await ProductImageProcessor.NormalizeAsync(image, cancellationToken);
        var directory = Path.Combine(environment.ContentRootPath, StorageFolder);
        Directory.CreateDirectory(directory);
        var fileName = $"{RandomNumberGenerator.GetHexString(24).ToLowerInvariant()}.jpg";
        await File.WriteAllBytesAsync(
            Path.Combine(directory, fileName), normalized, cancellationToken);
        return $"{RequestPath}/{fileName}";
    }
}
