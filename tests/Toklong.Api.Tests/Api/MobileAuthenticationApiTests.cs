using System.Net;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Toklong.Api.Security;
using Toklong.Api.Services;
using Toklong.Application.Abstractions;
using Toklong.Application.Pricing;
using Toklong.Domain.Authentication;
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
        var installationId = Guid.NewGuid().ToString("N");
        var completionId = Guid.NewGuid().ToString("N");
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
                Mode = "SignUp"
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
                InstallationId = installationId
            });
        Assert.True(
            verifyResponse.IsSuccessStatusCode,
            await verifyResponse.Content.ReadAsStringAsync());
        var verification = await verifyResponse.Content
            .ReadFromJsonAsync<VerificationResponse>();
        Assert.NotNull(verification);
        Assert.Equal(
            "registration_required",
            verification.Outcome);
        Assert.NotNull(verification.Registration);
        Assert.NotEmpty(
            verification.Registration.RegistrationTicket);
        var verificationJson =
            await verifyResponse.Content.ReadAsStringAsync();
        var ticketHash = Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        verification.Registration
                            .RegistrationTicket)))
            .ToLowerInvariant();
        Assert.DoesNotContain(
            ticketHash,
            verificationJson,
            StringComparison.OrdinalIgnoreCase);

        using var wrongInstallation = await CompleteAsync(
            client,
            verification.Registration.RegistrationTicket,
            "terms-mvp-v1",
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"));
        Assert.Equal(
            HttpStatusCode.BadRequest,
            wrongInstallation.StatusCode);

        using var outdatedTerms = await CompleteAsync(
            client,
            verification.Registration.RegistrationTicket,
            "terms-mvp-v0",
            installationId,
            Guid.NewGuid().ToString("N"));
        Assert.Equal(
            HttpStatusCode.BadRequest,
            outdatedTerms.StatusCode);

        using var completeResponse = await CompleteAsync(
            client,
            verification.Registration.RegistrationTicket,
            "terms-mvp-v1",
            installationId,
            completionId);
        Assert.True(
            completeResponse.IsSuccessStatusCode,
            await completeResponse.Content.ReadAsStringAsync());
        var issued = await completeResponse.Content
            .ReadFromJsonAsync<SessionResponse>();
        Assert.NotNull(issued);
        Assert.NotEmpty(issued.AccessToken);
        Assert.NotEmpty(issued.RefreshToken);

        string? buyerId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tokens = scope.ServiceProvider
                .GetRequiredService<MobileSessionTokenService>();
            var principal = await tokens.ValidateAccessAsync(
                issued.AccessToken,
                default);
            Assert.NotNull(principal);
            buyerId = principal.FindFirst(
                MobileAuthenticationDefaults.BuyerIdClaim)?.Value;
            Assert.NotNull(buyerId);
        }

        using var exactReplay = await CompleteAsync(
            client,
            verification.Registration.RegistrationTicket,
            "terms-mvp-v1",
            installationId,
            completionId);
        exactReplay.EnsureSuccessStatusCode();
        var replayedSession = await exactReplay.Content
            .ReadFromJsonAsync<SessionResponse>();
        Assert.NotNull(replayedSession);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tokens = scope.ServiceProvider
                .GetRequiredService<MobileSessionTokenService>();
            var replayedPrincipal =
                await tokens.ValidateAccessAsync(
                    replayedSession.AccessToken,
                    default);
            Assert.Equal(
                buyerId,
                replayedPrincipal?.FindFirst(
                    MobileAuthenticationDefaults.BuyerIdClaim)?.Value);
        }

        using var differentReplay = await CompleteAsync(
            client,
            verification.Registration.RegistrationTicket,
            "terms-mvp-v1",
            installationId,
            Guid.NewGuid().ToString("N"));
        Assert.Equal(
            HttpStatusCode.BadRequest,
            differentReplay.StatusCode);
        var otpHash = Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes("123456")))
            .ToLowerInvariant();
        Assert.DoesNotContain(
            factory.LogMessages,
            message =>
                message.Contains(
                    verification.Registration
                        .RegistrationTicket,
                    StringComparison.Ordinal) ||
                message.Contains(
                    ticketHash,
                    StringComparison.OrdinalIgnoreCase) ||
                message.Contains(
                    otpHash,
                    StringComparison.OrdinalIgnoreCase));

        using var existingOtp = await client.PostAsJsonAsync(
            "/api/mobile/auth/otp/request",
            new
            {
                PhoneNumber = "0812345678",
                Mode = "SignUp"
            });
        existingOtp.EnsureSuccessStatusCode();
        var existingChallenge = await existingOtp.Content
            .ReadFromJsonAsync<OtpResponse>();
        Assert.NotNull(existingChallenge);
        using var existingVerify = await client.PostAsJsonAsync(
            "/api/mobile/auth/otp/verify",
            new
            {
                existingChallenge.ChallengeId,
                Code = "123456",
                Mode = "SignUp",
                InstallationId = installationId
            });
        existingVerify.EnsureSuccessStatusCode();
        var existingOutcome = await existingVerify.Content
            .ReadFromJsonAsync<VerificationResponse>();
        Assert.Equal("session", existingOutcome?.Outcome);
        Assert.NotNull(existingOutcome?.Session);
        Assert.Null(existingOutcome?.Registration);

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

        using var removedEmailUpdateResponse = await client.PutAsJsonAsync(
            "/api/mobile/me/email",
            new { Email = "updated-buyer@example.com" });
        Assert.Equal(
            HttpStatusCode.NotFound,
            removedEmailUpdateResponse.StatusCode);
        using var unchangedProfileResponse = await client.GetAsync(
            "/api/mobile/me");
        unchangedProfileResponse.EnsureSuccessStatusCode();
        var unchangedProfile = await unchangedProfileResponse.Content
            .ReadFromJsonAsync<ProfileResponse>();
        Assert.NotNull(unchangedProfile);
        Assert.Equal("buyer@example.com", unchangedProfile.Email);

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

    [Fact]
    public async Task Sign_up_rejects_legacy_profile_fields_before_otp()
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
                PhoneNumber = "0812345678",
                Mode = "SignUp",
                FullName = "ผู้ซื้อ ทดสอบ",
                Email = "buyer@example.com"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Registration_cleanup_removes_only_rows_past_retention()
    {
        Guid oldId;
        Guid activeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider
                .GetRequiredService<ToklongDbContext>();
            var old = PendingMobileRegistration.Create(
                new string('c', 64),
                "+66822222222",
                Guid.NewGuid().ToString("N"),
                new DateTimeOffset(
                    2020,
                    1,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero),
                new DateTimeOffset(
                    2020,
                    1,
                    1,
                    0,
                    15,
                    0,
                    TimeSpan.Zero));
            var active = PendingMobileRegistration.Create(
                new string('d', 64),
                "+66833333333",
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(15));
            database.PendingMobileRegistrations.AddRange(
                old,
                active);
            await database.SaveChangesAsync();
            oldId = old.Id;
            activeId = active.Id;
        }

        var worker = factory.Services.GetRequiredService<
            PendingRegistrationCleanupWorker>();
        var deleted = await worker.RunOnceAsync(default);

        Assert.True(deleted >= 1);
        await using var verifyScope =
            factory.Services.CreateAsyncScope();
        var verifyDatabase = verifyScope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        Assert.False(await verifyDatabase
            .PendingMobileRegistrations.AnyAsync(
                item => item.Id == oldId));
        Assert.True(await verifyDatabase
            .PendingMobileRegistrations.AnyAsync(
                item => item.Id == activeId));
    }

    private static async Task<HttpResponseMessage> CompleteAsync(
        HttpClient client,
        string registrationTicket,
        string termsVersion,
        string installationId,
        string idempotencyKey)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/mobile/auth/registration/complete")
        {
            Content = JsonContent.Create(new
            {
                RegistrationTicket = registrationTicket,
                FullName = "ผู้ซื้อ ทดสอบ",
                Email = "buyer@example.com",
                TermsVersion = termsVersion,
                InstallationId = installationId
            })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private sealed record OtpResponse(
        string ChallengeId,
        string MaskedPhoneNumber,
        string? DevelopmentCode);

    private sealed record SessionResponse(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt);

    private sealed record VerificationResponse(
        string Outcome,
        SessionResponse? Session,
        RegistrationResponse? Registration);

    private sealed record RegistrationResponse(
        string RegistrationTicket,
        DateTimeOffset ExpiresAt,
        string MaskedPhoneNumber);

    private sealed record ProfileResponse(string? Email);
}

public sealed class MobileApiFactory : WebApplicationFactory<Program>
{
    private readonly RecordingLoggerProvider recordingLogger = new();

    public IReadOnlyCollection<string> LogMessages =>
        recordingLogger.Messages;

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging =>
            logging.AddProvider(recordingLogger));
        builder.UseSetting(
            "RateLimits:OtpRequestPermitLimit",
            "100");
        builder.UseSetting(
            "RateLimits:OtpVerifyPermitLimit",
            "100");
        builder.UseSetting(
            "RateLimits:RegistrationCompletePermitLimit",
            "100");
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

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> messages = new();

        public IReadOnlyCollection<string> Messages =>
            messages.ToArray();

        public ILogger CreateLogger(string categoryName) =>
            new RecordingLogger(messages);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(
            ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull =>
                null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue(formatter(state, exception));
                if (exception is not null)
                    messages.Enqueue(exception.Message);
            }
        }
    }
}
