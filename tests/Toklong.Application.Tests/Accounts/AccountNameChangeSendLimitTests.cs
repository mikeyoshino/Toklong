using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Features.Accounts.NameChanges;
using Toklong.Domain.Accounts;
using Toklong.Domain.Buyers;
using Toklong.Domain.Sellers;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Accounts;

public sealed class AccountNameChangeSendLimitTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 31, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sixth_accepted_send_in_rolling_day_is_rejected_until_oldest_expires()
    {
        await using var scenario = await Scenario.CreateAsync();
        var oldest = Now.AddHours(-23);
        for (var index = 0; index < 5; index++)
            await scenario.AddAcceptedChallengeAsync(
                oldest.AddHours(index));

        var exception = await Assert.ThrowsAsync<RequestCooldownException>(() =>
            scenario.Handler().Handle(
                scenario.Command(),
                default));

        Assert.Equal(oldest.AddHours(24) - Now, exception.RetryAfter);
        Assert.Equal(0, scenario.Provider.RequestCount);
        Assert.Equal(
            5,
            scenario.Database.AccountNameChangeChallenges.Count());
    }

    [Fact]
    public async Task Send_at_exact_rolling_day_boundary_is_allowed()
    {
        await using var scenario = await Scenario.CreateAsync();
        await scenario.AddAcceptedChallengeAsync(Now.AddHours(-24));
        for (var index = 1; index < 5; index++)
            await scenario.AddAcceptedChallengeAsync(
                Now.AddHours(-23 + index));

        var result = await scenario.Handler().Handle(
            scenario.Command(),
            default);

        Assert.NotEqual(Guid.Empty, result.ChallengeId);
        Assert.Equal(1, scenario.Provider.RequestCount);
    }

    [Fact]
    public async Task Buyer_accepted_history_blocks_same_phone_seller_only_subject_in_a_new_scope()
    {
        await using var scenario = await Scenario.CreateAsync();
        var oldest = Now.AddHours(-23);
        for (var index = 0; index < 5; index++)
            await scenario.AddAcceptedChallengeAsync(
                oldest.AddHours(index));
        var sellerSubject = new AccountNameChangeSubject(
            null,
            scenario.Seller.Id,
            Guid.NewGuid(),
            scenario.Seller.PhoneNumber);

        var exception = await Assert.ThrowsAsync<RequestCooldownException>(() =>
            scenario.Handler().Handle(
                scenario.Command() with { Subject = sellerSubject },
                default));

        Assert.Equal(oldest.AddHours(24) - Now, exception.RetryAfter);
        Assert.Equal(0, scenario.Provider.RequestCount);
        Assert.All(
            scenario.Database.AccountNameChangeChallenges,
            challenge =>
            {
                Assert.Equal(scenario.Buyer.Id, challenge.BuyerId);
                Assert.Null(challenge.SellerId);
                Assert.Equal(
                    AccountNameChangeStatus.Superseded,
                    challenge.Status);
            });
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private Scenario(
            ToklongDbContext database,
            BuyerAccount buyer,
            SellerAccount seller,
            AccountNameChangeSubject subject,
            MutableClock clock,
            CountingOtpProvider provider)
        {
            Database = database;
            Buyer = buyer;
            Seller = seller;
            Subject = subject;
            Clock = clock;
            Provider = provider;
        }

        public ToklongDbContext Database { get; }
        public BuyerAccount Buyer { get; }
        public SellerAccount Seller { get; }
        public AccountNameChangeSubject Subject { get; }
        public MutableClock Clock { get; }
        public CountingOtpProvider Provider { get; }

        public static async Task<Scenario> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            var database = new ToklongDbContext(options);
            var buyer = BuyerAccount.Create(
                "+66921031202",
                AccountName.Create("สมชาย", "ใจดี"),
                "buyer@example.com",
                Now.AddYears(-1));
            var seller = SellerAccount.Create(
                buyer.PhoneNumber,
                Now.AddYears(-1),
                AccountName.Create("สมชาย", "ใจดี"));
            database.AddRange(buyer, seller);
            await database.SaveChangesAsync();
            var subject = new AccountNameChangeSubject(
                buyer.Id,
                null,
                Guid.NewGuid(),
                buyer.PhoneNumber);
            var clock = new MutableClock();
            return new(
                database,
                buyer,
                seller,
                subject,
                clock,
                new CountingOtpProvider());
        }

        public async Task AddAcceptedChallengeAsync(DateTimeOffset acceptedAt)
        {
            var challenge = AccountNameChangeChallenge.Create(
                Guid.NewGuid(),
                Buyer.Id,
                null,
                Subject.SessionId,
                Subject.PhoneNumber,
                "0••-•••-1202",
                AccountName.Create("สมศักดิ์", "ใจดี"),
                Guid.NewGuid().ToString("N"),
                acceptedAt);
            challenge.MarkSendAccepted(
                $"provider-{challenge.Id:N}",
                acceptedAt);
            challenge.Supersede(acceptedAt.AddSeconds(61));
            Database.AccountNameChangeChallenges.Add(challenge);
            await Database.SaveChangesAsync();
        }

        public RequestAccountNameChangeCommand Command() =>
            new(
                Subject,
                "สมศักดิ์",
                "ใจดี",
                Guid.NewGuid().ToString("N"));

        public RequestAccountNameChangeHandler Handler() =>
            new(
                new BuyerRepository(Database),
                new SellerRepository(Database),
                new AccountNameChangeRepository(Database),
                Provider,
                Database,
                Clock);

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
    }

    private sealed class CountingOtpProvider : IOtpVerificationProvider
    {
        public OtpProviderCapabilities Capabilities { get; } =
            new(true, TimeSpan.FromMinutes(10), true);
        public int RequestCount { get; private set; }

        public Task<OtpChallenge> RequestAsync(
            string phoneNumber,
            OtpPurpose purpose,
            string providerRequestKey,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(
                new OtpChallenge(
                    $"provider-{providerRequestKey}",
                    "0••-•••-1202",
                    null));
        }

        public Task<string?> VerifyAsync(
            string challengeId,
            string code,
            OtpPurpose purpose,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }
}
