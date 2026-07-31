using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Accounts.NameChanges;
using Toklong.Application.Features.Offers.CreateBuyerOffer;
using Toklong.Application.Features.Offers.RespondToBuyerOffer;
using Toklong.Domain.Accounts;
using Toklong.Domain.Authentication;
using Toklong.Domain.Buyers;
using Toklong.Domain.Common;
using Toklong.Domain.Sellers;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;
using Toklong.Application.Tests.TestSupport;
using Toklong.Infrastructure.Pricing;
using Toklong.Infrastructure.Services;

namespace Toklong.Application.Tests.Accounts;

public sealed class AccountNameTransactionSnapshotTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 31, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Verification_never_modifies_existing_party_snapshots_or_hashes()
    {
        await using var scenario = await Scenario.CreateAsync();
        var transaction = scenario.ExistingTransaction;
        var originalBuyerName = transaction.BuyerDisplayName;
        var originalSellerName = transaction.SellerDisplayName;
        var originalAgreementJson = transaction.AgreementCoreSnapshotJson;
        var originalAgreementHash =
            transaction.AgreementCoreSnapshotHash;
        var originalTermsJson = transaction.TermsSnapshotJson;
        var originalTermsHash = transaction.TermsSnapshotHash;
        var originalAuditCount = transaction.AuditEvents.Count;
        var sellerTransaction = scenario.ExistingSellerTransaction;
        var originalSubjectSellerName =
            sellerTransaction.SellerDisplayName;
        var originalSellerAgreementJson =
            sellerTransaction.AgreementCoreSnapshotJson;
        var originalSellerAgreementHash =
            sellerTransaction.AgreementCoreSnapshotHash;

        await scenario.VerifyHandler().Handle(
            scenario.VerifyCommand(),
            default);

        Assert.Equal(originalBuyerName, transaction.BuyerDisplayName);
        Assert.Equal(originalSellerName, transaction.SellerDisplayName);
        Assert.Equal(
            originalAgreementJson,
            transaction.AgreementCoreSnapshotJson);
        Assert.Equal(
            originalAgreementHash,
            transaction.AgreementCoreSnapshotHash);
        Assert.Equal(originalTermsJson, transaction.TermsSnapshotJson);
        Assert.Equal(originalTermsHash, transaction.TermsSnapshotHash);
        Assert.Equal(originalAuditCount, transaction.AuditEvents.Count);
        Assert.Equal(
            originalSubjectSellerName,
            sellerTransaction.SellerDisplayName);
        Assert.Equal(
            originalSellerAgreementJson,
            sellerTransaction.AgreementCoreSnapshotJson);
        Assert.Equal(
            originalSellerAgreementHash,
            sellerTransaction.AgreementCoreSnapshotHash);
        Assert.DoesNotContain(
            scenario.Database.Entry(transaction).Properties,
            property => property.IsModified);
        Assert.DoesNotContain(
            scenario.Database.Entry(sellerTransaction).Properties,
            property => property.IsModified);
    }

    [Fact]
    public async Task Offer_created_after_verification_captures_the_new_buyer_name()
    {
        await using var scenario = await Scenario.CreateAsync();
        await scenario.VerifyHandler().Handle(
            scenario.VerifyCommand(),
            default);

        var later = await new CreateBuyerOfferHandler(
            new TransactionRepository(scenario.Database),
            new BuyerRepository(scenario.Database),
            new BundledThaiAddressCatalog(),
            scenario.Database,
            new FixedClock(),
            new ConfiguredBuyerProtectionFeePolicy(
                new BuyerProtectionFeeOptions
                {
                    Enabled = true,
                    PolicyVersion = "buyer-protection-test-v2"
                }),
            new TransactionTransitionService()).Handle(
            new CreateBuyerOfferCommand(
                scenario.Buyer.Id,
                "+66822222222",
                FulfillmentType.DigitalHandoff,
                "สิทธิ์ดิจิทัลที่โอนได้",
                "สิทธิ์ดิจิทัลที่ผู้ขายมีสิทธิ์โอน",
                ConditionCode.UsedGood,
                "ไม่มีตำหนิที่ผู้ซื้อระบุ",
                null,
                100_000,
                false,
                null,
                false),
            default);

        Assert.Equal("สมศักดิ์ ใจดี", later.BuyerDisplayName);
        Assert.Equal(
            "สมชาย ใจดี",
            scenario.ExistingTransaction.BuyerDisplayName);
        Assert.NotEqual(
            scenario.ExistingTransaction.Id,
            later.Id);
    }

    [Fact]
    public async Task Seller_acceptance_after_verification_captures_the_new_seller_name()
    {
        await using var scenario = await Scenario.CreateAsync();
        await scenario.VerifyHandler().Handle(
            scenario.VerifyCommand(),
            default);
        var payout = scenario.SellerRole.SavePayoutAccount(
            null,
            "KBANK",
            "สมศักดิ์ ใจดี",
            "1234567890",
            Now);
        await new SellerRepository(scenario.Database)
            .AddPayoutAccountAsync(payout, default);
        await scenario.Database.SaveChangesAsync();
        var transitions = new TransactionTransitionService();
        var feePolicy = new ConfiguredBuyerProtectionFeePolicy(
            new BuyerProtectionFeeOptions
            {
                Enabled = true,
                PolicyVersion = "buyer-protection-test-v2"
            });
        var offer = await new CreateBuyerOfferHandler(
            new TransactionRepository(scenario.Database),
            new BuyerRepository(scenario.Database),
            new BundledThaiAddressCatalog(),
            scenario.Database,
            new FixedClock(),
            feePolicy,
            transitions).Handle(
            new CreateBuyerOfferCommand(
                scenario.OtherBuyer.Id,
                scenario.Buyer.PhoneNumber,
                FulfillmentType.DigitalHandoff,
                "สิทธิ์ดิจิทัลที่โอนได้",
                "สิทธิ์ดิจิทัลที่ผู้ขายมีสิทธิ์โอน",
                ConditionCode.UsedGood,
                "ไม่มีตำหนิที่ผู้ซื้อระบุ",
                null,
                100_000,
                false,
                null,
                false),
            default);

        var accepted = await new AcceptBuyerOfferHandler(
            new TransactionRepository(scenario.Database),
            new SellerRepository(scenario.Database),
            feePolicy,
            new UnusedShippingQuoteProvider(),
            new BundledThaiAddressCatalog(),
            scenario.Database,
            new FixedClock(),
            transitions).Handle(
            new AcceptBuyerOfferCommand(
                offer.PublicToken,
                scenario.SellerRole.Id,
                payout.Id,
                true,
                true),
            default);

        Assert.Equal("สมศักดิ์ ใจดี", accepted.SellerDisplayName);
        Assert.Equal(
            "สมชาย ใจดี",
            scenario.ExistingSellerTransaction.SellerDisplayName);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private Scenario(
            ToklongDbContext database,
            BuyerAccount buyer,
            BuyerAccount otherBuyer,
            SellerAccount sellerRole,
            MobileSession session,
            AccountNameChangeChallenge challenge,
            SaleTransaction existingTransaction,
            SaleTransaction existingSellerTransaction)
        {
            Database = database;
            Buyer = buyer;
            OtherBuyer = otherBuyer;
            SellerRole = sellerRole;
            Session = session;
            Challenge = challenge;
            ExistingTransaction = existingTransaction;
            ExistingSellerTransaction = existingSellerTransaction;
        }

        public ToklongDbContext Database { get; }
        public BuyerAccount Buyer { get; }
        public BuyerAccount OtherBuyer { get; }
        public SellerAccount SellerRole { get; }
        public MobileSession Session { get; }
        public AccountNameChangeChallenge Challenge { get; }
        public SaleTransaction ExistingTransaction { get; }
        public SaleTransaction ExistingSellerTransaction { get; }

        public static async Task<Scenario> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            var database = new ToklongDbContext(options);
            var phone = "+66921031202";
            var oldName = AccountName.Create("สมชาย", "ใจดี");
            var buyer = BuyerAccount.Create(
                phone,
                oldName,
                "buyer@example.com",
                Now.AddYears(-1));
            var sellerRole = SellerAccount.Create(
                phone,
                Now.AddYears(-1),
                oldName);
            var transactionSeller = SellerAccount.Create(
                "+66822222222",
                Now.AddYears(-1),
                AccountName.Create("ผู้ขาย", "เดิม"));
            var otherBuyer = BuyerAccount.Create(
                "+66833333333",
                AccountName.Create("ผู้ซื้อ", "อื่น"),
                "other-buyer@example.com",
                Now.AddYears(-1));
            var session = MobileSession.Create(
                buyer.Id,
                sellerRole.Id,
                oldName.DisplayName,
                phone,
                Hash("refresh-snapshot"),
                Now.AddDays(-1),
                Now.AddDays(30));
            var challenge = AccountNameChangeChallenge.Create(
                Guid.NewGuid(),
                buyer.Id,
                sellerRole.Id,
                session.Id,
                phone,
                "0••-•••-1202",
                AccountName.Create("สมศักดิ์", "ใจดี"),
                Guid.NewGuid().ToString("N"),
                Now.AddMinutes(-1));
            challenge.MarkSendAccepted(
                "provider-name-change",
                Now.AddMinutes(-1));
            var transitions = new TransactionTransitionService();
            var transaction = TestTransactionFactory.CreateBuyerOffer(
                buyer.Id,
                oldName.DisplayName,
                phone,
                transactionSeller.PhoneNumber,
                FulfillmentType.DigitalHandoff,
                "สิทธิ์ดิจิทัลที่โอนได้",
                "สิทธิ์ดิจิทัลที่ผู้ขายมีสิทธิ์โอน",
                ConditionCode.UsedGood,
                "ไม่มีตำหนิที่ผู้ซื้อระบุ",
                null,
                100_000,
                "mvp-th-2026-07",
                Now.AddHours(-1),
                transitions);
            transaction.AcceptBuyerOffer(
                transactionSeller.Id,
                transactionSeller.DisplayName,
                transactionSeller.PhoneNumber,
                "KBANK",
                "ผู้ขาย เดิม",
                "1234567890",
                true,
                Now.AddMinutes(-30),
                transitions,
                buyerProtectionFeeSatang: 5_900,
                feePolicyVersion: "buyer-protection-test-v2");
            var sellerTransaction = TestTransactionFactory.CreateBuyerOffer(
                otherBuyer.Id,
                otherBuyer.FullName,
                otherBuyer.PhoneNumber,
                phone,
                FulfillmentType.DigitalHandoff,
                "ไลเซนส์ที่โอนได้",
                "ไลเซนส์ดิจิทัลที่ผู้ขายมีสิทธิ์โอน",
                ConditionCode.UsedGood,
                "ไม่มีตำหนิที่ผู้ซื้อระบุ",
                null,
                100_000,
                "mvp-th-2026-07",
                Now.AddHours(-1),
                transitions);
            sellerTransaction.AcceptBuyerOffer(
                sellerRole.Id,
                oldName.DisplayName,
                phone,
                "KBANK",
                oldName.DisplayName,
                "1234567890",
                true,
                Now.AddMinutes(-30),
                transitions,
                buyerProtectionFeeSatang: 5_900,
                feePolicyVersion: "buyer-protection-test-v2");
            database.AddRange(
                buyer,
                sellerRole,
                transactionSeller,
                otherBuyer,
                session,
                challenge,
                transaction,
                sellerTransaction);
            await database.SaveChangesAsync();
            return new(
                database,
                buyer,
                otherBuyer,
                sellerRole,
                session,
                challenge,
                transaction,
                sellerTransaction);
        }

        public VerifyAccountNameChangeCommand VerifyCommand() =>
            new(
                new AccountNameChangeSubject(
                    Buyer.Id,
                    SellerRole.Id,
                    Session.Id,
                    Buyer.PhoneNumber),
                Challenge.Id,
                "123456",
                Guid.NewGuid().ToString("N"));

        public VerifyAccountNameChangeHandler VerifyHandler() =>
            new(
                new BuyerRepository(Database),
                new SellerRepository(Database),
                new MobileSessionRepository(Database),
                new AccountNameChangeRepository(Database),
                new AcceptingProvider(Buyer.PhoneNumber),
                new DeterministicSecurity(),
                new DeterministicAccountNameAuditEvidenceWriter(),
                Database,
                new FixedClock(),
                new ImmediateAccountPhoneTransactionManager());

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class AcceptingProvider(string phoneNumber)
        : IOtpVerificationProvider
    {
        public OtpProviderCapabilities Capabilities { get; } =
            new(true, TimeSpan.FromMinutes(10), true)
            {
                SupportsVerificationLookup = true
            };

        public Task<OtpChallenge> RequestAsync(
            string phoneNumber,
            OtpPurpose purpose,
            string providerRequestKey,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> VerifyAsync(
            string challengeId,
            string code,
            OtpPurpose purpose,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(phoneNumber);

        public Task<OtpProviderVerificationEvidence>
            VerifyIdempotentlyAsync(
                string challengeId,
                string code,
                OtpPurpose purpose,
                string verificationRequestKey,
                CancellationToken cancellationToken) =>
            Task.FromResult(
                new OtpProviderVerificationEvidence(
                    verificationRequestKey,
                    challengeId,
                    purpose,
                    phoneNumber,
                    OtpProviderVerificationOutcome.Verified,
                    Now.AddSeconds(-1),
                    Now));
    }

    private sealed class DeterministicSecurity
        : IAccountNameVerificationSecurity
    {
        public string Digest(Guid challengeId, string code) =>
            Hash($"account-name:{challengeId:N}:{code}");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class UnusedShippingQuoteProvider
        : IShippingQuoteProvider
    {
        public Task<IReadOnlyList<ShippingQuoteOption>> GetQuotesAsync(
            ShippingQuoteRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShippingQuoteOption> ValidateQuoteAsync(
            ShippingQuoteRequest request,
            string quoteReference,
            long disclosedFeeSatang,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
