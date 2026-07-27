using Toklong.Application.Abstractions;
using Toklong.Application.Features.Offers.ExtractAgreementDraft;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Services;

namespace Toklong.Application.Tests.Offers;

public sealed class AgreementDraftExtractionTests
{
    [Fact]
    public async Task Handler_requires_text_or_image()
    {
        var handler = new ExtractAgreementDraftHandler(
            new StubExtractor());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(
                new ExtractAgreementDraftCommand(
                    "",
                    [],
                    "stable-test-user"),
                CancellationToken.None));

        Assert.Contains("เพิ่มรูป", exception.Message);
    }

    [Fact]
    public async Task Handler_rejects_spoofed_image_before_ai_call()
    {
        var extractor = new StubExtractor();
        var handler = new ExtractAgreementDraftHandler(extractor);
        var fake = new ListingImageInput(
            "chat.jpg",
            "image/jpeg",
            "not an image"u8.ToArray());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(
                new ExtractAgreementDraftCommand(
                    "",
                    [fake],
                    "stable-test-user"),
                CancellationToken.None));

        Assert.Equal(0, extractor.CallCount);
    }

    [Fact]
    public async Task Handler_passes_trimmed_text_without_storing_image()
    {
        var extractor = new StubExtractor();
        var handler = new ExtractAgreementDraftHandler(extractor);

        var result = await handler.Handle(
            new ExtractAgreementDraftCommand(
                "  กล้อง ราคา 4,500 บาท  ",
                [],
                "stable-test-user"),
            CancellationToken.None);

        Assert.Equal("กล้อง ราคา 4,500 บาท", extractor.ChatText);
        Assert.Equal("กล้อง", result.ProductName);
        Assert.Equal(1, extractor.CallCount);
    }

    [Fact]
    public void Ai_response_is_allow_listed_and_normalized()
    {
        const string json = """
            {
              "sellerPhoneNumber": "+66 81-234-5678",
              "productName": " กล้อง ",
              "description": "พร้อมเลนส์",
              "knownDefects": "มีรอย",
              "priceBaht": 4500.555,
              "condition": "Unknown",
              "confidence": "unexpected",
              "extractedFields": ["ชื่อสินค้า", "ราคา", "ราคา", ""]
            }
            """;

        var result =
            OpenAiAgreementDraftExtractionService.ParseResponse(json);

        Assert.Equal("0812345678", result.SellerPhoneNumber);
        Assert.Equal("กล้อง", result.ProductName);
        Assert.Equal(4500.56m, result.PriceBaht);
        Assert.Null(result.Condition);
        Assert.Equal("low", result.Confidence);
        Assert.Equal(["ชื่อสินค้า", "ราคา"], result.ExtractedFields);
    }

    private sealed class StubExtractor
        : IAgreementDraftExtractionService
    {
        public int CallCount { get; private set; }
        public string ChatText { get; private set; } = "";

        public Task<AgreementDraftExtraction> ExtractAsync(
            string chatText,
            IReadOnlyList<ListingImageInput> images,
            string safetyIdentifier,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ChatText = chatText;
            return Task.FromResult(new AgreementDraftExtraction(
                "",
                "กล้อง",
                "",
                "",
                null,
                null,
                "medium",
                ["ชื่อสินค้า"]));
        }
    }
}
