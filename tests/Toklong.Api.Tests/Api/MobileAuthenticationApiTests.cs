using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Toklong.Api.Security;
using Toklong.Application.Abstractions;
using Toklong.Application.Pricing;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Payments;
using Toklong.Infrastructure.Pricing;
using Toklong.Infrastructure.Security;
using Toklong.Infrastructure.Services;

namespace Toklong.Api.Tests.Api;

public sealed class MobileAuthenticationApiTests
    : IClassFixture<MobileApiFactory>
{
    private readonly MobileApiFactory factory;

    public MobileAuthenticationApiTests(MobileApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Sign_up_refresh_rotation_and_logout_are_enforced()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        using var otpResponse = await client.PostAsJsonAsync(
            "/api/mobile/auth/otp/request",
            new
            {
                PhoneNumber = "0812345678",
                Mode = "SignUp",
                FullName = "ผู้ซื้อ ทดสอบ",
                Email = "buyer@example.com"
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
                FullName = "ผู้ซื้อ ทดสอบ",
                Email = "buyer@example.com"
            });
        Assert.True(
            verifyResponse.IsSuccessStatusCode,
            await verifyResponse.Content.ReadAsStringAsync());
        var issued = await verifyResponse.Content
            .ReadFromJsonAsync<SessionResponse>();
        Assert.NotNull(issued);
        Assert.NotEmpty(issued.AccessToken);
        Assert.NotEmpty(issued.RefreshToken);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tokens = scope.ServiceProvider
                .GetRequiredService<MobileSessionTokenService>();
            Assert.NotNull(await tokens.ValidateAccessAsync(
                issued.AccessToken,
                default));
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                issued.AccessToken);
        using var profileResponse = await client.GetAsync(
            "/api/mobile/me");
        profileResponse.EnsureSuccessStatusCode();
        Assert.True(profileResponse.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "nosniff",
            profileResponse.Headers.GetValues("X-Content-Type-Options")
                .Single());
        var profile = await profileResponse.Content
            .ReadFromJsonAsync<ProfileResponse>();
        Assert.NotNull(profile);
        Assert.Equal("buyer@example.com", profile.Email);

        using var invalidEmailResponse = await client.PutAsJsonAsync(
            "/api/mobile/me/email",
            new { Email = "not-an-email" });
        Assert.Equal(
            HttpStatusCode.BadRequest,
            invalidEmailResponse.StatusCode);
        var invalidEmailProblem = await invalidEmailResponse.Content
            .ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(invalidEmailProblem);
        Assert.Equal(
            "กรุณากรอกอีเมลให้ถูกต้อง",
            invalidEmailProblem.Detail);

        using var updateEmailResponse = await client.PutAsJsonAsync(
            "/api/mobile/me/email",
            new { Email = "updated-buyer@example.com" });
        updateEmailResponse.EnsureSuccessStatusCode();
        using var updatedProfileResponse = await client.GetAsync(
            "/api/mobile/me");
        updatedProfileResponse.EnsureSuccessStatusCode();
        var updatedProfile = await updatedProfileResponse.Content
            .ReadFromJsonAsync<ProfileResponse>();
        Assert.NotNull(updatedProfile);
        Assert.Equal("updated-buyer@example.com", updatedProfile.Email);

        client.DefaultRequestHeaders.Authorization = null;
        using var refreshResponse = await client.PostAsJsonAsync(
            "/api/mobile/auth/refresh",
            new { issued.RefreshToken });
        refreshResponse.EnsureSuccessStatusCode();
        var rotated = await refreshResponse.Content
            .ReadFromJsonAsync<SessionResponse>();
        Assert.NotNull(rotated);
        Assert.NotEqual(
            issued.RefreshToken,
            rotated.RefreshToken);

        using var replayResponse = await client.PostAsJsonAsync(
            "/api/mobile/auth/refresh",
            new { issued.RefreshToken });
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            replayResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                rotated.AccessToken);
        using var logoutResponse = await client.PostAsync(
            "/api/mobile/auth/logout",
            null);
        Assert.Equal(
            HttpStatusCode.NoContent,
            logoutResponse.StatusCode);

        using var rejectedProfile = await client.GetAsync(
            "/api/mobile/me");
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            rejectedProfile.StatusCode);
    }

    [Theory]
    [InlineData("abc0812345678")]
    [InlineData("0212345678")]
    [InlineData("0712345678")]
    [InlineData("+14155552671")]
    public async Task Otp_request_rejects_non_thai_mobile_numbers(
        string phoneNumber)
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        using var response = await client.PostAsJsonAsync(
            "/api/mobile/auth/otp/request",
            new
            {
                PhoneNumber = phoneNumber,
                Mode = "SignIn"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record OtpResponse(
        string ChallengeId,
        string MaskedPhoneNumber,
        string? DevelopmentCode);

    private sealed record SessionResponse(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt);

    private sealed record ProfileResponse(string? Email);
}

public sealed class MobileApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Database:ApplyMigrations"] = "false",
                    ["Stripe:Enabled"] = "true",
                    ["Stripe:LiveMode"] = "false",
                    ["Stripe:WebhookSecret"] =
                        "whsec_integration_test",
                    ["BuyerProtectionFee:Enabled"] = "true",
                    ["BuyerProtectionFee:PolicyVersion"] =
                        "buyer-protection-v2",
                    ["ShippingQuotes:Provider"] =
                        "Development",
                    ["Reconciliation:SigningSecret"] =
                        "integration-reconciliation-secret",
                    ["DataProtection:KeysPath"] =
                        Path.Combine(
                            Path.GetTempPath(),
                            $"toklong-api-tests-{Guid.NewGuid():N}")
                }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ToklongDbContext>();
            services.RemoveAll<
                DbContextOptions<ToklongDbContext>>();
            services.RemoveAll<
                IDbContextOptionsConfiguration<ToklongDbContext>>();
            services.RemoveAll<IOtpVerificationProvider>();
            services.RemoveAll<IShippingQuoteProvider>();
            services.RemoveAll<IShipmentProvider>();
            services.RemoveAll<IPaymentIntentProvider>();
            services.RemoveAll<IPaymentFeePolicy>();
            services.RemoveAll<BuyerProtectionFeeOptions>();
            services.RemoveAll<
                DevelopmentShippingQuoteProvider>();
            services.RemoveAll<StripePaymentOptions>();
            services.RemoveAll<ReconciliationOptions>();
            var databaseName = Guid.NewGuid().ToString("N");
            services.AddDbContext<ToklongDbContext>(options =>
                options.UseInMemoryDatabase(
                    databaseName));
            services.AddSingleton<
                IOtpVerificationProvider,
                TestOtpVerificationProvider>();
            services.AddSingleton<
                DevelopmentShippingQuoteProvider>();
            services.AddSingleton<IShippingQuoteProvider>(
                provider => provider.GetRequiredService<
                    DevelopmentShippingQuoteProvider>());
            services.AddSingleton<IShipmentProvider>(
                provider => provider.GetRequiredService<
                    DevelopmentShippingQuoteProvider>());
            services.AddSingleton<
                IPaymentIntentProvider,
                TestPaymentIntentProvider>();
            services.AddSingleton(new BuyerProtectionFeeOptions
            {
                Enabled = true,
                PolicyVersion = "buyer-protection-v2"
            });
            services.AddSingleton<
                IPaymentFeePolicy,
                ConfiguredBuyerProtectionFeePolicy>();
            services.AddSingleton(new StripePaymentOptions
            {
                Enabled = true,
                LiveMode = false,
                WebhookSecret = "whsec_integration_test"
            });
            services.AddSingleton(new ReconciliationOptions
            {
                SigningSecret =
                    "integration-reconciliation-secret"
            });
        });
    }

    private sealed class TestOtpVerificationProvider
        : IOtpVerificationProvider
    {
        public Task<OtpChallenge> RequestAsync(
            string phoneNumber,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new OtpChallenge(
                    "challenge-test",
                    "081-***-5678",
                    "123456"));

        public Task<string?> VerifyAsync(
            string challengeId,
            string code,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(
                challengeId == "challenge-test" &&
                code == "123456"
                    ? "+66812345678"
                    : null);
    }

    private sealed class TestPaymentIntentProvider
        : IPaymentIntentProvider
    {
        public Task<PaymentIntentPreparation> PrepareAsync(
            Guid transactionId,
            long amountSatang,
            string currency,
            FulfillmentType fulfillmentType,
            string receiptEmail,
            string? existingProviderReference,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new PaymentIntentPreparation(
                    existingProviderReference ??
                    $"pi_local_{transactionId:N}",
                    $"pi_local_{transactionId:N}_secret_test",
                    "pk_test_local"));
    }
}
