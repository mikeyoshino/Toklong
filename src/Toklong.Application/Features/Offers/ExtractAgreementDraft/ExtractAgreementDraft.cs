using MediatR;
using Toklong.Application.Abstractions;

namespace Toklong.Application.Features.Offers.ExtractAgreementDraft;

public sealed record ExtractAgreementDraftCommand(
    string ChatText,
    IReadOnlyList<ListingImageInput> Images,
    string SafetyIdentifier) : IRequest<AgreementDraftExtraction>;

public sealed class ExtractAgreementDraftHandler(
    IAgreementDraftExtractionService extractor)
    : IRequestHandler<
        ExtractAgreementDraftCommand,
        AgreementDraftExtraction>
{
    public const int MaximumImages = 3;
    public const int MaximumImageBytes = 6 * 1024 * 1024;
    public const int MaximumTotalImageBytes = 8 * 1024 * 1024;
    public const int MaximumChatTextLength = 8000;

    public async Task<AgreementDraftExtraction> Handle(
        ExtractAgreementDraftCommand request,
        CancellationToken cancellationToken)
    {
        var chatText = request.ChatText.Trim();
        if (chatText.Length > MaximumChatTextLength)
            throw new ArgumentException(
                "ข้อความแชตต้องมีความยาวไม่เกิน 8,000 ตัวอักษร");
        if (request.Images.Count > MaximumImages)
            throw new ArgumentException(
                $"เลือกรูปได้ไม่เกิน {MaximumImages} รูป");
        if (chatText.Length == 0 && request.Images.Count == 0)
            throw new ArgumentException(
                "เพิ่มรูปหรือวางข้อความแชตก่อนให้ AI ช่วยกรอก");
        if (request.Images.Any(image =>
                image.Content.Length == 0 ||
                image.Content.Length > MaximumImageBytes ||
                !IsSupportedImage(image)))
            throw new ArgumentException(
                "รองรับเฉพาะรูป JPG, PNG หรือ WebP ขนาดไม่เกิน 6 MB ต่อรูป");
        if (request.Images.Sum(image => (long)image.Content.Length) >
            MaximumTotalImageBytes)
            throw new ArgumentException(
                "รูปทั้งหมดมีขนาดรวมเกิน 8 MB");
        if (string.IsNullOrWhiteSpace(request.SafetyIdentifier) ||
            request.SafetyIdentifier.Length > 64)
            throw new ArgumentException(
                "ไม่สามารถระบุผู้ใช้งานสำหรับการวิเคราะห์ได้");

        return await extractor.ExtractAsync(
            chatText,
            request.Images,
            request.SafetyIdentifier,
            cancellationToken);
    }

    public static bool IsSupportedImage(ListingImageInput image)
    {
        var bytes = image.Content;
        var isJpeg = bytes.Length >= 3 &&
                     bytes[0] == 0xff &&
                     bytes[1] == 0xd8 &&
                     bytes[2] == 0xff;
        var isPng = bytes.Length >= 8 &&
                    bytes.AsSpan(0, 8).SequenceEqual(
                        new byte[]
                        {
                            0x89, 0x50, 0x4e, 0x47,
                            0x0d, 0x0a, 0x1a, 0x0a
                        });
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
