using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Sales.AnalyzeListingImages;

namespace Toklong.Application.Features.Sales.SaveListingPhoto;

public sealed record SaveListingPhotoCommand(
    ListingImageInput Image) : IRequest<string>;

public sealed class SaveListingPhotoHandler(
    IImportedProductImageStore imageStore)
    : IRequestHandler<SaveListingPhotoCommand, string>
{
    public async Task<string> Handle(
        SaveListingPhotoCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Image.Content.Length == 0 ||
            request.Image.Content.Length >
            AnalyzeListingImagesHandler.MaximumImageBytes ||
            !AnalyzeListingImagesHandler.IsSupportedImage(request.Image))
        {
            throw new ArgumentException(
                "รองรับเฉพาะรูป JPG, PNG หรือ WebP ขนาดไม่เกิน 8 MB");
        }

        return await imageStore.SaveAsync(request.Image, cancellationToken);
    }
}
