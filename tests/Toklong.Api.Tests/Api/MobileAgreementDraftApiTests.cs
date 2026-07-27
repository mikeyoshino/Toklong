using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;

namespace Toklong.Api.Tests.Api;

public sealed class MobileAgreementDraftApiTests
    : IClassFixture<MobileApiFactory>
{
    private readonly MobileApiFactory factory;

    public MobileAgreementDraftApiTests(MobileApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Extraction_requires_authentication()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("กล้องราคา 4500"), "chatText");

        using var response = await client.PostAsync(
            "/api/mobile/offers/extract-draft",
            content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_buyer_can_extract_draft_from_text()
    {
        var extractor = new StubExtractor();
        using var customizedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<
                    IAgreementDraftExtractionService>();
                services.AddSingleton<
                    IAgreementDraftExtractionService>(extractor);
            }));
        using var client = customizedFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        var accessToken = await SignUpAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        using var content = new MultipartFormDataContent();
        content.Add(
            new StringContent("กล้องมือสอง ราคา 4,500 บาท"),
            "chatText");

        using var response = await client.PostAsync(
            "/api/mobile/offers/extract-draft",
            content);

        Assert.True(
            response.IsSuccessStatusCode,
            await response.Content.ReadAsStringAsync());
        var draft = await response.Content
            .ReadFromJsonAsync<AgreementDraftExtraction>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    Converters =
                    {
                        new JsonStringEnumConverter()
                    }
                });
        Assert.NotNull(draft);
        Assert.Equal("กล้องมือสอง", draft.ProductName);
        Assert.Equal(4500m, draft.PriceBaht);
        Assert.Equal(
            "กล้องมือสอง ราคา 4,500 บาท",
            extractor.ChatText);
        Assert.Equal(64, extractor.SafetyIdentifier.Length);
    }

    private static async Task<string> SignUpAsync(HttpClient client)
    {
        using var otpResponse = await client.PostAsJsonAsync(
            "/api/mobile/auth/otp/request",
            new
            {
                PhoneNumber = "0865550011",
                Mode = "SignUp",
                FullName = "ผู้ซื้อ เอไอ",
                Email = "ai-buyer@example.com"
            });
        otpResponse.EnsureSuccessStatusCode();
        var challenge = await otpResponse.Content
            .ReadFromJsonAsync<OtpResponse>();
        Assert.NotNull(challenge);

        using var verifyResponse = await client.PostAsJsonAsync(
            "/api/mobile/auth/otp/verify",
            new
            {
                challenge.ChallengeId,
                Code = "123456",
                Mode = "SignUp",
                FullName = "ผู้ซื้อ เอไอ",
                Email = "ai-buyer@example.com"
            });
        verifyResponse.EnsureSuccessStatusCode();
        var session = await verifyResponse.Content
            .ReadFromJsonAsync<SessionResponse>();
        Assert.NotNull(session);
        return session.AccessToken;
    }

    private sealed record OtpResponse(
        string ChallengeId,
        string MaskedPhoneNumber,
        string? DevelopmentCode);

    private sealed record SessionResponse(string AccessToken);

    private sealed class StubExtractor
        : IAgreementDraftExtractionService
    {
        public string ChatText { get; private set; } = "";
        public string SafetyIdentifier { get; private set; } = "";

        public Task<AgreementDraftExtraction> ExtractAsync(
            string chatText,
            IReadOnlyList<ListingImageInput> images,
            string safetyIdentifier,
            CancellationToken cancellationToken)
        {
            ChatText = chatText;
            SafetyIdentifier = safetyIdentifier;
            return Task.FromResult(new AgreementDraftExtraction(
                "",
                "กล้องมือสอง",
                "",
                "",
                4500m,
                ConditionCode.UsedGood,
                "high",
                ["ชื่อสินค้า", "ราคา"]));
        }
    }
}
