using SkiaSharp;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Services;

internal static class ProductImageProcessor
{
    private const int MaximumDimension = 1600;
    private const long MaximumDecodedPixels = 24_000_000;

    public static Task<byte[]> NormalizeAsync(
        ListingImageInput input,
        CancellationToken cancellationToken)
    {
        using (var metadataSource =
               new MemoryStream(input.Content, writable: false))
        using (var codec = SKCodec.Create(metadataSource))
        {
            if (codec is null ||
                codec.Info.Width < 1 ||
                codec.Info.Height < 1 ||
                (long)codec.Info.Width *
                codec.Info.Height >
                MaximumDecodedPixels)
                throw new InvalidOperationException(
                    "ขนาดมิติของรูปสูงเกินไป");
        }
        using var source =
            new MemoryStream(input.Content, writable: false);
        using var original = SKBitmap.Decode(source)
            ?? throw new InvalidOperationException("ไม่สามารถอ่านไฟล์รูปนี้ได้");
        var scale = Math.Min(
            1d,
            Math.Min(
                MaximumDimension / (double)original.Width,
                MaximumDimension / (double)original.Height));
        var width = Math.Max(1, (int)Math.Round(original.Width * scale));
        var height = Math.Max(1, (int)Math.Round(original.Height * scale));
        using var surface = SKSurface.Create(new SKImageInfo(
            width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        if (surface is null)
            throw new InvalidOperationException("ไม่สามารถเตรียมรูปสินค้าได้");

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        using (var paint = new SKPaint { IsAntialias = true })
        {
            canvas.DrawBitmap(
                original,
                new SKRect(0, 0, width, height),
                paint);
        }
        canvas.Flush();
        using var normalized = surface.Snapshot();
        using var data = normalized.Encode(SKEncodedImageFormat.Jpeg, 84)
            ?? throw new InvalidOperationException("ไม่สามารถบันทึกรูปสินค้าได้");
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(data.ToArray());
    }
}
