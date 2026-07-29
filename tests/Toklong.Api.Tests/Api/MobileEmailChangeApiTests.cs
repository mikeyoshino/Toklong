using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Toklong.Api.Security;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Authentication;
using Toklong.Domain.Buyers;
using Toklong.Domain.Sellers;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Api.Tests.Api;

public sealed class MobileEmailChangeApiTests
    : IClassFixture<MobileApiFactory>
{
    private readonly MobileApiFactory factory;
    private static int accountSequence;

    public MobileEmailChangeApiTests(MobileApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Every_email_change_route_requires_authentication()
    {
        using var client = CreateClient();

        await AssertEmailChangeRoutesRejectedAsync(
            client,
            HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Seller_only_account_cannot_use_email_change_routes()
    {
        using var seller = await AuthenticatedSellerAsync();

        await AssertEmailChangeRoutesRejectedAsync(
            seller.Client,
            HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Direct_update_route_no_longer_bypasses_verification()
    {
        using var buyer = await AuthenticatedBuyerAsync();

        using var response = await buyer.Client.PutAsJsonAsync(
            "/api/mobile/me/email",
            new { Email = "bypass@example.com" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            buyer.Email,
            await CurrentEmailAsync(buyer.Client));
    }

    [Fact]
    public async Task Email_stays_old_until_correct_code_is_verified()
    {
        using var buyer = await AuthenticatedBuyerAsync();
        const string newEmail = "new@example.com";

        using var requested = await RequestAsync(
            buyer.Client,
            newEmail);
        requested.EnsureSuccessStatusCode();
        var challenge = await requested.Content
            .ReadFromJsonAsync<EmailChangeResponse>();
        Assert.NotNull(challenge);
        Assert.Equal(5, challenge.RemainingAttempts);
        Assert.NotEqual(newEmail, challenge.MaskedEmail);
        Assert.Equal(
            buyer.Email,
            await CurrentEmailAsync(buyer.Client));

        using var verified = await VerifyAsync(
            buyer.Client,
            challenge.ChallengeId,
            "123456");
        verified.EnsureSuccessStatusCode();
        var completion = await verified.Content
            .ReadFromJsonAsync<VerifiedEmailChangeResponse>();

        Assert.NotNull(completion);
        Assert.Equal(newEmail, completion.Email);
        Assert.Equal(
            newEmail,
            await CurrentEmailAsync(buyer.Client));
    }

    [Fact]
    public async Task Pending_responses_and_logs_redact_code_and_full_email()
    {
        using var buyer = await AuthenticatedBuyerAsync();
        var pendingEmail =
            $"private-{Guid.NewGuid():N}@example.com";
        var logCount = factory.LogMessages.Count;

        using var requested = await RequestAsync(
            buyer.Client,
            pendingEmail);
        requested.EnsureSuccessStatusCode();
        using var pending = await buyer.Client.GetAsync(
            "/api/mobile/me/email-change");
        pending.EnsureSuccessStatusCode();
        var payload =
            await requested.Content.ReadAsStringAsync() +
            await pending.Content.ReadAsStringAsync();

        Assert.DoesNotContain("123456", payload);
        Assert.DoesNotContain(pendingEmail, payload);
        Assert.DoesNotContain(
            "pendingEmail",
            payload,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "codeDigest",
            payload,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            factory.LogMessages.Skip(logCount),
            message =>
                message.Contains(
                    "123456",
                    StringComparison.Ordinal) ||
                message.Contains(
                    pendingEmail,
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Pending_change_resumes_in_a_new_authenticated_client()
    {
        using var buyer = await AuthenticatedBuyerAsync();
        using var requested = await RequestAsync(
            buyer.Client,
            "resume@example.com");
        requested.EnsureSuccessStatusCode();
        var expected = await requested.Content
            .ReadFromJsonAsync<EmailChangeResponse>();
        Assert.NotNull(expected);

        using var resumedClient = CreateClient(buyer.AccessToken);
        using var pending = await resumedClient.GetAsync(
            "/api/mobile/me/email-change");
        pending.EnsureSuccessStatusCode();
        var actual = await pending.Content
            .ReadFromJsonAsync<EmailChangeResponse>();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Request_and_verification_exact_replays_are_idempotent()
    {
        using var buyer = await AuthenticatedBuyerAsync();
        var pendingEmail =
            $"replay-{Guid.NewGuid():N}@example.com";
        var requestKey = NewKey();

        using var firstRequest = await RequestAsync(
            buyer.Client,
            pendingEmail,
            requestKey);
        firstRequest.EnsureSuccessStatusCode();
        var firstChallenge = await firstRequest.Content
            .ReadFromJsonAsync<EmailChangeResponse>();
        Assert.NotNull(firstChallenge);
        using var requestReplay = await RequestAsync(
            buyer.Client,
            pendingEmail,
            requestKey);
        requestReplay.EnsureSuccessStatusCode();
        var replayedChallenge = await requestReplay.Content
            .ReadFromJsonAsync<EmailChangeResponse>();
        Assert.Equal(firstChallenge, replayedChallenge);

        var inbox = factory.Services
            .GetRequiredService<IDevelopmentEmailInbox>();
        Assert.Single(
            inbox.Messages,
            message => string.Equals(
                message.Recipient,
                pendingEmail,
                StringComparison.OrdinalIgnoreCase));

        var verificationKey = NewKey();
        using var firstVerification = await VerifyAsync(
            buyer.Client,
            firstChallenge.ChallengeId,
            "123456",
            verificationKey);
        firstVerification.EnsureSuccessStatusCode();
        var firstCompletion = await firstVerification.Content
            .ReadFromJsonAsync<VerifiedEmailChangeResponse>();
        using var verificationReplay = await VerifyAsync(
            buyer.Client,
            firstChallenge.ChallengeId,
            "123456",
            verificationKey);
        verificationReplay.EnsureSuccessStatusCode();
        var replayedCompletion = await verificationReplay.Content
            .ReadFromJsonAsync<VerifiedEmailChangeResponse>();

        Assert.Equal(firstCompletion, replayedCompletion);
        await using var scope =
            factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        Assert.Single(
            await database.BuyerEmailChangeChallenges
                .Where(item => item.BuyerId == buyer.BuyerId)
                .ToListAsync());
        Assert.Single(
            await database.BuyerEmailVerificationAttempts
                .Where(item => item.BuyerId == buyer.BuyerId)
                .ToListAsync());
    }

    [Fact]
    public async Task Resend_enforces_sixty_seconds_and_invalidates_old_challenge()
    {
        using var buyer = await AuthenticatedBuyerAsync();
        using var requested = await RequestAsync(
            buyer.Client,
            "resent@example.com");
        requested.EnsureSuccessStatusCode();
        var original = await requested.Content
            .ReadFromJsonAsync<EmailChangeResponse>();
        Assert.NotNull(original);

        using var tooSoon = await ResendAsync(
            buyer.Client,
            original.ChallengeId);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            tooSoon.StatusCode);

        await SetChallengeTimingAsync(
            original.ChallengeId,
            resendAvailableAt:
                DateTimeOffset.UtcNow.AddSeconds(-1));
        using var resent = await ResendAsync(
            buyer.Client,
            original.ChallengeId);
        resent.EnsureSuccessStatusCode();
        var replacement = await resent.Content
            .ReadFromJsonAsync<EmailChangeResponse>();
        Assert.NotNull(replacement);
        Assert.NotEqual(
            original.ChallengeId,
            replacement.ChallengeId);

        using var oldChallenge = await VerifyAsync(
            buyer.Client,
            original.ChallengeId,
            "123456");
        Assert.Equal(
            HttpStatusCode.BadRequest,
            oldChallenge.StatusCode);
        using var replacementChallenge = await VerifyAsync(
            buyer.Client,
            replacement.ChallengeId,
            "123456");
        replacementChallenge.EnsureSuccessStatusCode();
        Assert.Equal(
            "resent@example.com",
            await CurrentEmailAsync(buyer.Client));
    }

    [Fact]
    public async Task Five_incorrect_attempts_lock_the_challenge()
    {
        using var buyer = await AuthenticatedBuyerAsync();
        using var requested = await RequestAsync(
            buyer.Client,
            "locked@example.com");
        requested.EnsureSuccessStatusCode();
        var challenge = await requested.Content
            .ReadFromJsonAsync<EmailChangeResponse>();
        Assert.NotNull(challenge);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var incorrect = await VerifyAsync(
                buyer.Client,
                challenge.ChallengeId,
                "000000");
            Assert.Equal(
                HttpStatusCode.BadRequest,
                incorrect.StatusCode);
        }

        using var correctAfterLock = await VerifyAsync(
            buyer.Client,
            challenge.ChallengeId,
            "123456");
        Assert.Equal(
            HttpStatusCode.BadRequest,
            correctAfterLock.StatusCode);
        Assert.Equal(
            buyer.Email,
            await CurrentEmailAsync(buyer.Client));

        await using var scope =
            factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var stored = await database.BuyerEmailChangeChallenges
            .SingleAsync(item =>
                item.Id == challenge.ChallengeId);
        Assert.Equal(BuyerEmailChangeStatus.Locked, stored.Status);
        Assert.Equal(0, stored.RemainingAttempts);
        Assert.Equal(5, stored.IncorrectAttempts);
    }

    [Fact]
    public async Task Challenge_expires_exactly_after_ten_minutes()
    {
        using var buyer = await AuthenticatedBuyerAsync();
        using var requested = await RequestAsync(
            buyer.Client,
            "expired@example.com");
        requested.EnsureSuccessStatusCode();
        var challenge = await requested.Content
            .ReadFromJsonAsync<EmailChangeResponse>();
        Assert.NotNull(challenge);

        await using (var scope =
                     factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider
                .GetRequiredService<ToklongDbContext>();
            var stored = await database.BuyerEmailChangeChallenges
                .SingleAsync(item =>
                    item.Id == challenge.ChallengeId);
            Assert.Equal(
                TimeSpan.FromMinutes(10),
                stored.ExpiresAt - stored.CreatedAt);
        }

        await SetChallengeTimingAsync(
            challenge.ChallengeId,
            expiresAt: DateTimeOffset.UtcNow.AddSeconds(-1));
        using var expired = await VerifyAsync(
            buyer.Client,
            challenge.ChallengeId,
            "123456");

        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Equal(
            buyer.Email,
            await CurrentEmailAsync(buyer.Client));
        await using var verifyScope =
            factory.Services.CreateAsyncScope();
        var verifyDatabase = verifyScope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        Assert.Equal(
            BuyerEmailChangeStatus.Expired,
            (await verifyDatabase.BuyerEmailChangeChallenges
                .SingleAsync(item =>
                    item.Id == challenge.ChallengeId)).Status);
    }

    [Fact]
    public async Task Another_buyer_cannot_read_resend_or_verify_challenge()
    {
        using var owner = await AuthenticatedBuyerAsync();
        using var other = await AuthenticatedBuyerAsync();
        using var requested = await RequestAsync(
            owner.Client,
            "owner-only@example.com");
        requested.EnsureSuccessStatusCode();
        var challenge = await requested.Content
            .ReadFromJsonAsync<EmailChangeResponse>();
        Assert.NotNull(challenge);

        using var pending = await other.Client.GetAsync(
            "/api/mobile/me/email-change");
        Assert.Equal(
            HttpStatusCode.NoContent,
            pending.StatusCode);

        using var resend = await ResendAsync(
            other.Client,
            challenge.ChallengeId);
        Assert.Equal(HttpStatusCode.Forbidden, resend.StatusCode);
        using var verify = await VerifyAsync(
            other.Client,
            challenge.ChallengeId,
            "123456");
        Assert.Equal(HttpStatusCode.Forbidden, verify.StatusCode);
        Assert.Equal(
            owner.Email,
            await CurrentEmailAsync(owner.Client));
        Assert.Equal(
            other.Email,
            await CurrentEmailAsync(other.Client));
    }

    [Fact]
    public async Task Verify_rate_limit_is_partitioned_by_buyer_and_network_digest()
    {
        using var firstBuyer = await AuthenticatedBuyerAsync();
        using var secondBuyer = await AuthenticatedBuyerAsync();
        var missingChallenge = Guid.NewGuid();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var response = await VerifyAsync(
                firstBuyer.Client,
                missingChallenge,
                "000000");
            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);
        }

        using var limited = await VerifyAsync(
            firstBuyer.Client,
            missingChallenge,
            "000000");
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            limited.StatusCode);

        using var independent = await VerifyAsync(
            secondBuyer.Client,
            missingChallenge,
            "000000");
        Assert.Equal(
            HttpStatusCode.NotFound,
            independent.StatusCode);
    }

    [Fact]
    public async Task Request_and_resend_share_a_buyer_and_network_rate_limit()
    {
        using var buyer = await AuthenticatedBuyerAsync();
        using var firstNetwork = CreateClient(buyer.AccessToken);
        firstNetwork.DefaultRequestHeaders.Add(
            "X-Test-Remote-Address",
            "192.0.2.10");

        using var requested = await RequestAsync(
            firstNetwork,
            "request-limit@example.com");
        requested.EnsureSuccessStatusCode();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var allowed = await ResendAsync(
                firstNetwork,
                Guid.NewGuid());
            Assert.NotEqual(
                HttpStatusCode.TooManyRequests,
                allowed.StatusCode);
        }

        using var limited = await ResendAsync(
            firstNetwork,
            Guid.NewGuid());
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            limited.StatusCode);

        using var secondNetwork = CreateClient(buyer.AccessToken);
        secondNetwork.DefaultRequestHeaders.Add(
            "X-Test-Remote-Address",
            "198.51.100.20");
        using var independent = await RequestAsync(
            secondNetwork,
            "other-network@example.com");

        independent.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Paid_transaction_and_original_provider_contact_stay_unchanged()
    {
        using var buyer = await AuthenticatedBuyerAsync();
        var transactionId =
            await SeedAcceptedDigitalTransactionAsync(buyer);

        using var paymentSheet = await buyer.Client.PostAsJsonAsync(
            $"/api/mobile/transactions/{transactionId}/payment-sheet",
            new { AcceptedTerms = true });
        paymentSheet.EnsureSuccessStatusCode();
        var payment = await paymentSheet.Content
            .ReadFromJsonAsync<PaymentSheetResponse>();
        Assert.NotNull(payment);
        Assert.Equal(buyer.Email, payment.ReceiptEmail);

        string snapshotHash;
        string paymentReference;
        await using (var scope =
                     factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider
                .GetRequiredService<ToklongDbContext>();
            var stored = await database.Transactions
                .Include(item => item.AgreementAcceptances)
                .Include(item => item.AuditEvents)
                .Include(item => item.ExternalEvents)
                .SingleAsync(item => item.Id == transactionId);
            stored.ConfirmStripePayment(
                $"evt-email-{transactionId:N}",
                stored.PaymentReference!,
                stored.BuyerTotalSatang,
                stored.Currency,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                scope.ServiceProvider.GetRequiredService<
                    TransactionTransitionService>());
            await database.SaveChangesAsync();
            Assert.Equal(
                TransactionState.PaidAwaitingDigitalDelivery,
                stored.State);
            snapshotHash = stored.ProductSnapshotHash!;
            paymentReference = stored.PaymentReference!;
        }

        using var requested = await RequestAsync(
            buyer.Client,
            "future-contact@example.com");
        requested.EnsureSuccessStatusCode();
        var challenge = await requested.Content
            .ReadFromJsonAsync<EmailChangeResponse>();
        Assert.NotNull(challenge);
        using var verified = await VerifyAsync(
            buyer.Client,
            challenge.ChallengeId,
            "123456");
        verified.EnsureSuccessStatusCode();

        await using var verifyScope =
            factory.Services.CreateAsyncScope();
        var verifyDatabase = verifyScope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var unchanged = await verifyDatabase.Transactions
            .SingleAsync(item => item.Id == transactionId);
        Assert.Equal(
            TransactionState.PaidAwaitingDigitalDelivery,
            unchanged.State);
        Assert.Equal(snapshotHash, unchanged.ProductSnapshotHash);
        Assert.Equal(paymentReference, unchanged.PaymentReference);
        Assert.Equal(buyer.PhoneNumber, unchanged.BuyerContact);
        var providerRequest = Assert.Single(
            factory.PaymentIntentRequests,
            request => request.TransactionId == transactionId);
        Assert.Equal(buyer.Email, providerRequest.ReceiptEmail);
    }

    private async Task<BuyerSession> AuthenticatedBuyerAsync()
    {
        var sequence = Interlocked.Increment(
            ref accountSequence);
        var phoneNumber = $"+669{sequence:D8}";
        var email = $"buyer-{sequence}@example.com";
        var fullName = $"ผู้ซื้อ ทดสอบ{sequence}";
        await using var scope =
            factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var buyer = BuyerAccount.Create(
            phoneNumber,
            fullName,
            email,
            DateTimeOffset.UtcNow);
        database.Buyers.Add(buyer);
        await database.SaveChangesAsync();
        var tokens = scope.ServiceProvider
            .GetRequiredService<MobileSessionTokenService>();
        var session = await tokens.CreateAsync(
            new MobileSessionProfile(
                buyer.Id,
                null,
                buyer.PhoneNumber,
                buyer.FullName),
            default);
        return new BuyerSession(
            buyer.Id,
            buyer.FullName,
            buyer.PhoneNumber,
            buyer.Email!,
            session.AccessToken,
            CreateClient(session.AccessToken));
    }

    private async Task<SellerSession> AuthenticatedSellerAsync()
    {
        var sequence = Interlocked.Increment(
            ref accountSequence);
        var phoneNumber = $"+668{sequence:D8}";
        await using var scope =
            factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var seller = SellerAccount.Create(
            phoneNumber,
            DateTimeOffset.UtcNow,
            $"ผู้ขาย ทดสอบ{sequence}");
        database.Sellers.Add(seller);
        await database.SaveChangesAsync();
        var tokens = scope.ServiceProvider
            .GetRequiredService<MobileSessionTokenService>();
        var session = await tokens.CreateAsync(
            new MobileSessionProfile(
                null,
                seller.Id,
                seller.PhoneNumber,
                seller.DisplayName),
            default);
        return new SellerSession(
            CreateClient(session.AccessToken));
    }

    private async Task<Guid> SeedAcceptedDigitalTransactionAsync(
        BuyerSession buyer)
    {
        await using var scope =
            factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var transitions = scope.ServiceProvider
            .GetRequiredService<TransactionTransitionService>();
        var now = DateTimeOffset.UtcNow.AddMinutes(-2);
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            buyer.BuyerId,
            buyer.FullName,
            buyer.PhoneNumber,
            "+66855555555",
            FulfillmentType.DigitalHandoff,
            "สิทธิ์ดิจิทัลที่โอนได้",
            "สิทธิ์ดิจิทัลตามรายละเอียดที่ตกลงกัน",
            ConditionCode.UsedGood,
            "ไม่มีตำหนิที่ผู้ซื้อระบุ",
            null,
            100_000,
            "terms-v1",
            now,
            transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "+66855555555",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            now.AddMinutes(1),
            transitions,
            buyerProtectionFeeSatang: 5_900,
            platformFeeSatang: 0,
            sellerExpectedNetSatang: 100_000,
            feePolicyVersion: "buyer-protection-v2");
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        return transaction.Id;
    }

    private async Task SetChallengeTimingAsync(
        Guid challengeId,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? resendAvailableAt = null)
    {
        await using var scope =
            factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var challenge = await database.BuyerEmailChangeChallenges
            .SingleAsync(item => item.Id == challengeId);
        if (expiresAt.HasValue)
            database.Entry(challenge)
                .Property(item => item.ExpiresAt)
                .CurrentValue = expiresAt.Value;
        if (resendAvailableAt.HasValue)
            database.Entry(challenge)
                .Property(item => item.ResendAvailableAt)
                .CurrentValue = resendAvailableAt.Value;
        await database.SaveChangesAsync();
    }

    private HttpClient CreateClient(string? accessToken = null)
    {
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        if (accessToken is not null)
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken);
        return client;
    }

    private static async Task AssertEmailChangeRoutesRejectedAsync(
        HttpClient client,
        HttpStatusCode expectedStatus)
    {
        foreach (var spec in EmailChangeRouteRequests())
        {
            using var request = new HttpRequestMessage(
                spec.Method,
                spec.Path);
            if (spec.Body is not null)
                request.Content = JsonContent.Create(spec.Body);

            using var response = await client.SendAsync(request);

            Assert.Equal(expectedStatus, response.StatusCode);
        }
    }

    private static RequestSpec[] EmailChangeRouteRequests()
    {
        var challengeId = Guid.NewGuid();
        return
        [
            new RequestSpec(
                HttpMethod.Get,
                "/api/mobile/me/email-change",
                null),
            new RequestSpec(
                HttpMethod.Post,
                "/api/mobile/me/email-change",
                new
                {
                    Email = "new@example.com",
                    IdempotencyKey = NewKey()
                }),
            new RequestSpec(
                HttpMethod.Post,
                $"/api/mobile/me/email-change/{challengeId}/resend",
                new { IdempotencyKey = NewKey() }),
            new RequestSpec(
                HttpMethod.Post,
                $"/api/mobile/me/email-change/{challengeId}/verify",
                new
                {
                    Code = "123456",
                    IdempotencyKey = NewKey()
                })
        ];
    }

    private static Task<HttpResponseMessage> RequestAsync(
        HttpClient client,
        string email,
        string? idempotencyKey = null) =>
        client.PostAsJsonAsync(
            "/api/mobile/me/email-change",
            new
            {
                Email = email,
                IdempotencyKey = idempotencyKey ?? NewKey()
            });

    private static Task<HttpResponseMessage> ResendAsync(
        HttpClient client,
        Guid challengeId,
        string? idempotencyKey = null) =>
        client.PostAsJsonAsync(
            $"/api/mobile/me/email-change/{challengeId}/resend",
            new
            {
                IdempotencyKey = idempotencyKey ?? NewKey()
            });

    private static Task<HttpResponseMessage> VerifyAsync(
        HttpClient client,
        Guid challengeId,
        string code,
        string? idempotencyKey = null) =>
        client.PostAsJsonAsync(
            $"/api/mobile/me/email-change/{challengeId}/verify",
            new
            {
                Code = code,
                IdempotencyKey = idempotencyKey ?? NewKey()
            });

    private static async Task<string?> CurrentEmailAsync(
        HttpClient client)
    {
        using var profileResponse = await client.GetAsync(
            "/api/mobile/me");
        profileResponse.EnsureSuccessStatusCode();
        var profile = await profileResponse.Content
            .ReadFromJsonAsync<ProfileResponse>();
        return profile?.Email;
    }

    private static string NewKey() =>
        Guid.NewGuid().ToString("N");

    private sealed record RequestSpec(
        HttpMethod Method,
        string Path,
        object? Body);

    private sealed record EmailChangeResponse(
        Guid ChallengeId,
        string MaskedEmail,
        DateTimeOffset ExpiresAt,
        DateTimeOffset ResendAvailableAt,
        int RemainingAttempts);

    private sealed record VerifiedEmailChangeResponse(
        string Email,
        DateTimeOffset CompletedAt);

    private sealed record ProfileResponse(string? Email);

    private sealed record PaymentSheetResponse(
        string ReceiptEmail);

    private sealed record BuyerSession(
        Guid BuyerId,
        string FullName,
        string PhoneNumber,
        string Email,
        string AccessToken,
        HttpClient Client) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    private sealed record SellerSession(
        HttpClient Client) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }
}
