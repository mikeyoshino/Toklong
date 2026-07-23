using MediatR;
using Toklong.Application.Abstractions;

namespace Toklong.Application.Features.Sales.AnalyzeListingImages;

public sealed record AnalyzeListingImagesCommand(
    IReadOnlyList<ListingImageInput> Images) : IRequest<AnalyzedListingDraft>;

public sealed class AnalyzeListingImagesHandler(
    IListingImageAnalysisService analyzer,
    IImportedProductImageStore imageStore)
    : IRequestHandler<AnalyzeListingImagesCommand, AnalyzedListingDraft>
{
    public const int MaximumImages = 4;
    public const int MaximumImageBytes = 8 * 1024 * 1024;
    public const int MaximumTotalBytes = 20 * 1024 * 1024;

    public async Task<AnalyzedListingDraft> Handle(
        AnalyzeListingImagesCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Images.Count is < 1 or > MaximumImages)
            throw new ArgumentException($"กรุณาเลือกรูป 1–{MaximumImages} รูป");
        if (request.Images.Any(image =>
                image.Content.Length == 0 ||
                image.Content.Length > MaximumImageBytes ||
                !IsSupportedImage(image)))
            throw new ArgumentException("รองรับเฉพาะรูป JPG, PNG หรือ WebP ขนาดไม่เกิน 8 MB ต่อรูป");
        if (request.Images.Sum(image => (long)image.Content.Length) > MaximumTotalBytes)
            throw new ArgumentException("รูปทั้งหมดมีขนาดรวมเกิน 20 MB");

        var analysis = await analyzer.AnalyzeAsync(request.Images, cancellationToken);
        var photoPath = await imageStore.SaveAsync(request.Images[0], cancellationToken);

        return new AnalyzedListingDraft(
            analysis.ProductName,
            analysis.Description,
            analysis.KnownDefects,
            analysis.PriceBaht,
            analysis.Category,
            analysis.Condition,
            photoPath,
            analysis.Confidence,
            analysis.ExtractedFields);
    }

    public static bool IsSupportedImage(ListingImageInput image)
    {
        var bytes = image.Content;
        var isJpeg = bytes.Length >= 3 &&
                     bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff;
        var isPng = bytes.Length >= 8 &&
                    bytes.AsSpan(0, 8).SequenceEqual(
                        new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
        var isWebP = bytes.Length >= 12 &&
                     bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                     bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8);
        return image.ContentType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => isJpeg,
            "image/png" => isPng,
            "image/webp" => isWebP,
            _ => false
        };
    }
}
