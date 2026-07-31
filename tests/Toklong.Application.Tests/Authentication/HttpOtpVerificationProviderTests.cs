using System.Net;
using System.Text;
using System.Text.Json;
using Toklong.Application.Abstractions;
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
            OtpPurpose.MobileAuthentication,
            Guid.NewGuid().ToString("N"),
            default);
        var phone = await provider.VerifyAsync(
            challenge.ChallengeId,
            "123456",
            OtpPurpose.MobileAuthentication,
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
            OtpPurpose.MobileAuthentication,
            Guid.NewGuid().ToString("N"),
            default);

        var result = await provider.VerifyAsync(
            $"{challenge.ChallengeId}x",
            "123456",
            OtpPurpose.MobileAuthentication,
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
        var provider = CreateCertifiedNameChangeProvider(handler);
        const string requestKey =
            "11111111222233334444555555555555";

        var result = await provider.RequestAsync(
            "081-234-5678",
            OtpPurpose.AccountNameChange,
            requestKey,
            default);

        Assert.NotEqual("challenge_0123456789", result.ChallengeId);
        Assert.DoesNotContain(
            "challenge_0123456789",
            result.ChallengeId,
            StringComparison.Ordinal);
        Assert.Null(result.DevelopmentCode);
        using var sent = JsonDocument.Parse(handler.LastBody);
        Assert.Equal(
            "+66812345678",
            sent.RootElement.GetProperty("phoneNumber").GetString());
        Assert.Equal(
            "AccountNameChange",
            sent.RootElement.GetProperty("purpose").GetString());
        Assert.Equal(
            requestKey,
            sent.RootElement.GetProperty("providerRequestKey").GetString());
        Assert.Equal(
            600,
            sent.RootElement.GetProperty("codeLifetimeSeconds").GetInt32());
        Assert.Equal(
            "secret-test-signing-key-at-least-32-bytes",
            handler.LastRequest!.Headers
                .GetValues("X-Api-Key")
                .Single());
    }

    [Fact]
    public async Task Verify_normalizes_only_a_provider_verified_phone()
    {
        var handler = new SequenceStubHandler(
            (
                HttpStatusCode.OK,
                """
                {
                  "challengeId": "challenge_0123456789",
                  "maskedPhoneNumber": "081-***-5678"
                }
                """),
            (
                HttpStatusCode.OK,
                """
                {
                  "verified": true,
                  "phoneNumber": "0812345678"
                }
                """));
        var provider = CreateCertifiedNameChangeProvider(handler);
        var challenge = await provider.RequestAsync(
            "0812345678",
            OtpPurpose.AccountNameChange,
            "22222222333344445555666666666666",
            default);

        var phone = await provider.VerifyAsync(
            challenge.ChallengeId,
            "123456",
            OtpPurpose.AccountNameChange,
            default);

        Assert.Equal("+66812345678", phone);
        using var sent = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.Equal(
            "AccountNameChange",
            sent.RootElement.GetProperty("purpose").GetString());
    }

    [Fact]
    public async Task Verify_rejects_non_ascii_digits_without_provider_call()
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
            "๑๒๓๔๕๖",
            OtpPurpose.MobileAuthentication,
            default);

        Assert.Null(phone);
        Assert.Null(handler.LastRequest);
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
            provider.RequestAsync(
                "0812345678",
                OtpPurpose.MobileAuthentication,
                Guid.NewGuid().ToString("N"),
                default));

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

    [Fact]
    public async Task ThaiBulkSms_challenge_cannot_be_verified_for_another_purpose()
    {
        var handler = new SequenceStubHandler(
            (
                HttpStatusCode.OK,
                """
                {
                  "status": "success",
                  "token": "provider-token-purpose-test",
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
            "0812345678",
            OtpPurpose.MobileAuthentication,
            Guid.NewGuid().ToString("N"),
            default);

        var wrongPurpose = await provider.VerifyAsync(
            challenge.ChallengeId,
            "123456",
            OtpPurpose.AccountNameChange,
            default);
        var correctPurpose = await provider.VerifyAsync(
            challenge.ChallengeId,
            "123456",
            OtpPurpose.MobileAuthentication,
            default);

        Assert.Null(wrongPurpose);
        Assert.Equal("+66812345678", correctPurpose);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Http_challenge_rejects_wrong_purpose_without_provider_call()
    {
        var handler = new SequenceStubHandler(
            (
                HttpStatusCode.OK,
                """
                {
                  "challengeId": "provider_challenge_0123456789",
                  "maskedPhoneNumber": "081-***-5678"
                }
                """));
        var provider = CreateCertifiedNameChangeProvider(handler);
        var challenge = await provider.RequestAsync(
            "0812345678",
            OtpPurpose.AccountNameChange,
            "11111111222233334444555555555555",
            default);

        var result = await provider.VerifyAsync(
            challenge.ChallengeId,
            "123456",
            OtpPurpose.MobileAuthentication,
            default);

        Assert.Null(result);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain(
            "provider_challenge_0123456789",
            challenge.ChallengeId,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_challenge_rejects_tampering_without_provider_call()
    {
        var handler = new SequenceStubHandler(
            (
                HttpStatusCode.OK,
                """
                {
                  "challengeId": "provider_challenge_0123456789",
                  "maskedPhoneNumber": "081-***-5678"
                }
                """));
        var provider = CreateCertifiedNameChangeProvider(handler);
        var challenge = await provider.RequestAsync(
            "0812345678",
            OtpPurpose.AccountNameChange,
            "33333333444455556666777777777777",
            default);

        var result = await provider.VerifyAsync(
            $"{challenge.ChallengeId}x",
            "123456",
            OtpPurpose.AccountNameChange,
            default);

        Assert.Null(result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Http_request_uses_caller_stable_key_and_lookup_recovers_lost_response()
    {
        const string requestKey =
            "aaaaaaaa11111111bbbbbbbb22222222";
        var handler = new LostResponseThenLookupHandler(requestKey);
        var provider = CreateCertifiedNameChangeProvider(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.RequestAsync(
                "0812345678",
                OtpPurpose.AccountNameChange,
                requestKey,
                default));
        var recovered = await provider.LookupAsync(
            requestKey,
            "0812345678",
            OtpPurpose.AccountNameChange,
            default);

        Assert.NotNull(recovered);
        Assert.Equal(requestKey, handler.RequestIdempotencyKey);
        Assert.Equal(requestKey, handler.LookupRequestKey);
        Assert.Equal(requestKey, recovered.ProviderRequestKey);
        Assert.Equal(
            OtpPurpose.AccountNameChange,
            recovered.Purpose);
        Assert.Equal("+66812345678", recovered.PhoneNumber);
        Assert.Equal(
            TimeSpan.FromMinutes(10),
            recovered.ExpiresAt - recovered.AcceptedAt);
        Assert.DoesNotContain(
            "provider_challenge_0123456789",
            recovered.Challenge.ChallengeId,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_lookup_rejects_a_wrong_original_purpose()
    {
        var now = DateTimeOffset.UtcNow;

        await AssertLookupRejectedAsync(
            "MobileAuthentication",
            "aaaaaaaa11111111bbbbbbbb22222222",
            now.AddMinutes(-2),
            now.AddMinutes(8));
    }

    [Fact]
    public async Task Http_lookup_rejects_a_mismatched_original_request_key()
    {
        var now = DateTimeOffset.UtcNow;

        await AssertLookupRejectedAsync(
            "AccountNameChange",
            "cccccccc33333333dddddddd44444444",
            now.AddMinutes(-2),
            now.AddMinutes(8));
    }

    [Fact]
    public async Task Http_lookup_rejects_provider_expired_evidence()
    {
        var now = DateTimeOffset.UtcNow;

        await AssertLookupRejectedAsync(
            "AccountNameChange",
            "aaaaaaaa11111111bbbbbbbb22222222",
            now.AddMinutes(-11),
            now.AddMinutes(-1));
    }

    [Fact]
    public async Task ThaiBulkSms_blocks_account_name_change_but_preserves_mobile_authentication()
    {
        var handler = new SequenceStubHandler(
            (
                HttpStatusCode.OK,
                """
                {
                  "status": "success",
                  "token": "provider-token-auth-only",
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

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.RequestAsync(
                "0812345678",
                OtpPurpose.AccountNameChange,
                Guid.NewGuid().ToString("N"),
                default));
        var authentication = await provider.RequestAsync(
            "0812345678",
            OtpPurpose.MobileAuthentication,
            Guid.NewGuid().ToString("N"),
            default);

        Assert.NotNull(authentication);
        Assert.Single(handler.Requests);
        Assert.False(provider.Capabilities.SupportsAccountNameChange);
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

    private static HttpOtpVerificationProvider
        CreateCertifiedNameChangeProvider(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            new OtpProviderOptions
            {
                Provider = "Http",
                BaseUrl = "https://otp.example.test/",
                ApiKey =
                    "secret-test-signing-key-at-least-32-bytes",
                AccountNameChangeEnabled = true,
                AccountNameChangeCertificationReference =
                    "cert-account-name-001",
                AccountNameChangeCodeLifetimeSeconds = 600
            });

    private static async Task AssertLookupRejectedAsync(
        string originalPurpose,
        string originalRequestKey,
        DateTimeOffset acceptedAt,
        DateTimeOffset expiresAt)
    {
        const string requestedKey =
            "aaaaaaaa11111111bbbbbbbb22222222";
        var response = JsonSerializer.Serialize(new
        {
            challengeId = "provider_challenge_0123456789",
            maskedPhoneNumber = "081-***-5678",
            phoneNumber = "0812345678",
            providerRequestKey = originalRequestKey,
            purpose = originalPurpose,
            acceptedAt,
            expiresAt
        });
        var handler = new SequenceStubHandler(
            (HttpStatusCode.OK, response));
        var provider =
            CreateCertifiedNameChangeProvider(handler);

        var result = await provider.LookupAsync(
            requestedKey,
            "0812345678",
            OtpPurpose.AccountNameChange,
            default);

        Assert.Null(result);
        Assert.Single(handler.Requests);
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

    private sealed class LostResponseThenLookupHandler(
        string expectedRequestKey) : HttpMessageHandler
    {
        public string? RequestIdempotencyKey { get; private set; }
        public string? LookupRequestKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                RequestIdempotencyKey = request.Headers
                    .GetValues("Idempotency-Key")
                    .Single();
                Assert.Equal(
                    expectedRequestKey,
                    RequestIdempotencyKey);
                _ = await request.Content!.ReadAsStringAsync(
                    cancellationToken);
                throw new HttpRequestException(
                    "response lost after provider acceptance");
            }

            LookupRequestKey = request.RequestUri!
                .Segments[^1]
                .TrimEnd('/');
            var acceptedAt =
                DateTimeOffset.UtcNow.AddMinutes(-1);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""
                    {
                      "challengeId": "provider_challenge_0123456789",
                      "maskedPhoneNumber": "081-***-5678",
                      "phoneNumber": "0812345678",
                      "providerRequestKey": "{{expectedRequestKey}}",
                      "purpose": "AccountNameChange",
                      "acceptedAt": "{{acceptedAt:O}}",
                      "expiresAt": "{{acceptedAt.AddMinutes(10):O}}"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
