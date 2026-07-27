using System.Net;
using System.Text;
using System.Text.Json;
using Toklong.Application.Common;
using Toklong.Infrastructure.Services;

namespace Toklong.Application.Tests.Authentication;

public sealed class HttpOtpVerificationProviderTests
{
    [Fact]
    public async Task ThaiBulkSms_uses_form_api_and_returns_phone_only_after_verification()
    {
        var handler = new SequenceStubHandler(
            (
                HttpStatusCode.OK,
                """
                {
                  "status": "success",
                  "token": "provider-token-0123456789",
                  "refno": "ABC12"
                }
                """),
            (
                HttpStatusCode.OK,
                """
                {
                  "status": "success",
                  "message": "Code is correct."
                }
                """));
        var provider = new ThaiBulkSmsOtpVerificationProvider(
            new HttpClient(handler),
            new OtpProviderOptions
            {
                Provider = "ThaiBulkSms",
                BaseUrl = "https://otp.thaibulksms.test/",
                ApiKey = "key-test",
                ApiSecret = "secret-test-at-least-16"
            });

        var challenge = await provider.RequestAsync(
            "081-234-5678",
            default);
        var phone = await provider.VerifyAsync(
            challenge.ChallengeId,
            "123456",
            default);

        Assert.Equal("081-***-5678", challenge.MaskedPhoneNumber);
        Assert.Null(challenge.DevelopmentCode);
        Assert.Equal("+66812345678", phone);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(
            "/v2/otp/request",
            handler.Requests[0].Uri.AbsolutePath);
        Assert.Contains(
            "msisdn=%2B66812345678",
            handler.Requests[0].Body,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "provider-token-0123456789",
            challenge.ChallengeId,
            StringComparison.Ordinal);
        Assert.Equal(
            "/v2/otp/verify",
            handler.Requests[1].Uri.AbsolutePath);
        Assert.Contains(
            "token=provider-token-0123456789",
            handler.Requests[1].Body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThaiBulkSms_rejects_a_tampered_challenge_without_provider_call()
    {
        var handler = new SequenceStubHandler(
            (
                HttpStatusCode.OK,
                """
                {
                  "status": "success",
                  "token": "provider-token-0123456789",
                  "refno": "ABC12"
                }
                """));
        var provider = new ThaiBulkSmsOtpVerificationProvider(
            new HttpClient(handler),
            new OtpProviderOptions
            {
                Provider = "ThaiBulkSms",
                BaseUrl = "https://otp.thaibulksms.test/",
                ApiKey = "key-test",
                ApiSecret = "secret-test-at-least-16"
            });
        var challenge = await provider.RequestAsync(
            "0812345678",
            default);

        var result = await provider.VerifyAsync(
            $"{challenge.ChallengeId}x",
            "123456",
            default);

        Assert.Null(result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Request_never_returns_a_development_code()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """
            {
              "challengeId": "challenge_0123456789",
              "maskedPhoneNumber": "081-***-5678"
            }
            """);
        var provider = CreateProvider(handler);

        var result = await provider.RequestAsync(
            "081-234-5678",
            default);

        Assert.Equal("challenge_0123456789", result.ChallengeId);
        Assert.Null(result.DevelopmentCode);
        using var sent = JsonDocument.Parse(handler.LastBody);
        Assert.Equal(
            "+66812345678",
            sent.RootElement.GetProperty("phoneNumber").GetString());
        Assert.Equal(
            "secret-test",
            handler.LastRequest!.Headers
                .GetValues("X-Api-Key")
                .Single());
    }

    [Fact]
    public async Task Verify_normalizes_only_a_provider_verified_phone()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """
            {
              "verified": true,
              "phoneNumber": "0812345678"
            }
            """);
        var provider = CreateProvider(handler);

        var phone = await provider.VerifyAsync(
            "challenge_0123456789",
            "123456",
            default);

        Assert.Equal("+66812345678", phone);
    }

    [Fact]
    public async Task Rate_limit_is_exposed_as_a_safe_cooldown()
    {
        var handler = new StubHandler(
            HttpStatusCode.TooManyRequests,
            "{}",
            retryAfterSeconds: 30);
        var provider = CreateProvider(handler);

        var exception = await Assert.ThrowsAsync<
            RequestCooldownException>(() =>
            provider.RequestAsync("0812345678", default));

        Assert.True(exception.RetryAfter >= TimeSpan.FromSeconds(29));
    }

    [Fact]
    public void Non_https_provider_url_is_rejected()
    {
        var options = new OtpProviderOptions
        {
            Provider = "Http",
            BaseUrl = "http://otp.example.test",
            ApiKey = "secret-test"
        };

        Assert.Throws<InvalidOperationException>(
            options.GetValidatedBaseUri);
    }

    private static HttpOtpVerificationProvider CreateProvider(
        HttpMessageHandler handler)
    {
        var options = new OtpProviderOptions
        {
            Provider = "Http",
            BaseUrl = "https://otp.example.test/",
            ApiKey = "secret-test"
        };
        return new HttpOtpVerificationProvider(
            new HttpClient(handler),
            options);
    }

    private sealed class StubHandler(
        HttpStatusCode status,
        string response,
        int? retryAfterSeconds = null) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string LastBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(
                    cancellationToken);
            var result = new HttpResponseMessage(status)
            {
                Content = new StringContent(
                    response,
                    Encoding.UTF8,
                    "application/json")
            };
            if (retryAfterSeconds.HasValue)
                result.Headers.RetryAfter =
                    new System.Net.Http.Headers.RetryConditionHeaderValue(
                        TimeSpan.FromSeconds(retryAfterSeconds.Value));
            return result;
        }
    }

    private sealed class SequenceStubHandler(
        params (HttpStatusCode Status, string Body)[] responses)
        : HttpMessageHandler
    {
        private int _index;
        public List<(Uri Uri, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(
                    cancellationToken);
            Requests.Add((request.RequestUri!, body));
            var response = responses[_index++];
            return new HttpResponseMessage(response.Status)
            {
                Content = new StringContent(
                    response.Body,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
