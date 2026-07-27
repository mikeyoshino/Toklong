using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Sales.AnalyzeListingImages;
using Toklong.Application.Features.Sales.SaveListingPhoto;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Services;

namespace Toklong.Application.Tests.Listings;

public sealed class ListingImageAnalysisTests
{
    [Theory]
    [InlineData("image/jpeg", "FFD8FFE0")]
    [InlineData("image/png", "89504E470D0A1A0A")]
    [InlineData("image/webp", "524946460000000057454250")]
    public void Supported_image_requires_matching_content_signature(
        string contentType,
        string hex)
    {
        var image = new ListingImageInput(
            "product",
            contentType,
            Convert.FromHexString(hex));

        Assert.True(AnalyzeListingImagesHandler.IsSupportedImage(image));
    }

    [Fact]
    public async Task Handler_analyzes_then_stores_first_image_as_draft_photo()
    {
        var image = Jpeg();
        var handler = new AnalyzeListingImagesHandler(
            new StubAnalyzer(),
            new StubStore());

        var result = await handler.Handle(
            new AnalyzeListingImagesCommand([image]),
            CancellationToken.None);

        Assert.Equal("กล้องฟิล์ม", result.ProductName);
        Assert.Equal(4500m, result.PriceBaht);
        Assert.Equal("/media/product-imports/test.jpg", result.ProductPhotoPath);
        Assert.Equal(ConditionCode.UsedGood, result.Condition);
    }

    [Fact]
    public async Task Handler_rejects_content_type_spoofing_before_ai_call()
    {
        var handler = new AnalyzeListingImagesHandler(
            new StubAnalyzer(),
            new StubStore());
        var fake = new ListingImageInput(
            "not-a-photo.jpg",
            "image/jpeg",
            "plain text"u8.ToArray());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(
                new AnalyzeListingImagesCommand([fake]),
                CancellationToken.None));

        Assert.Contains("JPG", exception.Message);
    }

    [Fact]
    public async Task Seller_can_store_an_agreement_photo_without_running_ai()
    {
        var handler = new SaveListingPhotoHandler(new StubStore());

        var path = await handler.Handle(
            new SaveListingPhotoCommand(Jpeg()),
            CancellationToken.None);

        Assert.Equal("/media/product-imports/test.jpg", path);
    }

    [Fact]
    public async Task Agreement_photo_upload_rejects_spoofed_content()
    {
        var handler = new SaveListingPhotoHandler(new StubStore());
        var fake = new ListingImageInput(
            "not-a-photo.jpg",
            "image/jpeg",
            "plain text"u8.ToArray());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(
                new SaveListingPhotoCommand(fake),
                CancellationToken.None));

        Assert.Contains("JPG", exception.Message);
    }

    [Fact]
    public void Ai_response_is_allow_listed_and_limited()
    {
        const string response = """
            {
              "productName": " กล้องฟิล์ม ",
              "description": "รายละเอียดจากภาพ",
              "knownDefects": "",
              "priceBaht": 4500.50,
              "category": "หมวดที่ไม่มี",
              "condition": "Unknown",
              "confidence": "unexpected",
              "extractedFields": ["ชื่อสินค้า", "ราคา", "ราคา", ""]
            }
            """;

        var result = OpenAiListingImageAnalysisService.ParseResponse(response);

        Assert.Equal("กล้องฟิล์ม", result.ProductName);
        Assert.Equal("งานอดิเรกและของใช้", result.Category);
        Assert.Equal(ConditionCode.UsedGood, result.Condition);
        Assert.Equal("low", result.Confidence);
        Assert.Equal(["ชื่อสินค้า", "ราคา"], result.ExtractedFields);
    }

    [Fact]
    public async Task Stored_image_is_reencoded_as_jpeg_without_original_container()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"toklong-image-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var storagePath = Path.Combine(
                root,
                "persistent",
                "product-images");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ProductImages:StoragePath"] = storagePath
                    })
                .Build();
            var store = new ImportedProductImageStore(
                new TestEnvironment(root),
                configuration);
            var png = new ListingImageInput(
                "product.png",
                "image/png",
                Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));

            var path = await store.SaveAsync(png, CancellationToken.None);
            var stored = await File.ReadAllBytesAsync(
                Path.Combine(
                    storagePath,
                    Path.GetFileName(path)));

            Assert.Equal(0xff, stored[0]);
            Assert.Equal(0xd8, stored[1]);
            Assert.EndsWith(".jpg", path);
            await store.DeleteAsync(
                $"https://toklong.example{path}",
                CancellationToken.None);
            Assert.False(File.Exists(
                Path.Combine(
                    storagePath,
                    Path.GetFileName(path))));

            var outsideFile = Path.Combine(
                root,
                "must-not-delete.txt");
            await File.WriteAllTextAsync(
                outsideFile,
                "retained");
            await store.DeleteAsync(
                "/media/product-imports/../must-not-delete.txt",
                CancellationToken.None);
            Assert.True(File.Exists(outsideFile));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ListingImageInput Jpeg() =>
        new("product.jpg", "image/jpeg", [0xff, 0xd8, 0xff, 0xd9]);

    private sealed class StubAnalyzer : IListingImageAnalysisService
    {
        public Task<ListingImageAnalysis> AnalyzeAsync(
            IReadOnlyList<ListingImageInput> images,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ListingImageAnalysis(
                "กล้องฟิล์ม",
                "รายละเอียด",
                "",
                4500m,
                "กล้องและอุปกรณ์",
                ConditionCode.UsedGood,
                "high",
                ["ชื่อสินค้า", "ราคา"]));
    }

    private sealed class StubStore : IImportedProductImageStore
    {
        public Task<string> SaveAsync(
            ListingImageInput image,
            CancellationToken cancellationToken) =>
            Task.FromResult("/media/product-imports/test.jpg");

        public Task DeleteAsync(
            string fileReference,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class TestEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Toklong.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
