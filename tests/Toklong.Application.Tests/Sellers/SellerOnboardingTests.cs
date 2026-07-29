using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Toklong.Application.Common;
using Microsoft.EntityFrameworkCore;
using Toklong.Domain.Common;
using Toklong.Domain.Sellers;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Services;

namespace Toklong.Application.Tests.Sellers;

public sealed class SellerOnboardingTests
{
    [Theory]
    [InlineData("0812345678", "+66812345678")]
    [InlineData("+66 81 234 5678", "+66812345678")]
    public void Thai_phone_is_normalized_to_e164(
        string input,
        string expected)
    {
        Assert.Equal(
            expected,
            DevelopmentOtpVerificationProvider.NormalizeThaiPhone(input));
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("021234567")]
    [InlineData("0712345678")]
    [InlineData("+14155552671")]
    [InlineData("abc0812345678")]
    [InlineData("08123x5678")]
    public void Unsupported_phone_is_rejected(string input)
    {
        Assert.Throws<ArgumentException>(() =>
            DevelopmentOtpVerificationProvider.NormalizeThaiPhone(input));
    }

    [Fact]
    public async Task Development_otp_is_one_time_and_wrong_code_does_not_verify()
    {
        var provider = new DevelopmentOtpVerificationProvider(
            new TestEnvironment(Environments.Development));
        var phone = $"089{Random.Shared.Next(10_000_000):D7}";
        var challenge = await provider.RequestAsync(phone, CancellationToken.None);
        var wrongCode = challenge.DevelopmentCode == "000000"
            ? "999999"
            : "000000";

        Assert.NotNull(challenge.DevelopmentCode);
        Assert.Null(await provider.VerifyAsync(
            challenge.ChallengeId, wrongCode, CancellationToken.None));
        Assert.Equal(
            DevelopmentOtpVerificationProvider.NormalizeThaiPhone(phone),
            await provider.VerifyAsync(
                challenge.ChallengeId,
                challenge.DevelopmentCode!,
                CancellationToken.None));
        Assert.Null(await provider.VerifyAsync(
            challenge.ChallengeId,
            challenge.DevelopmentCode!,
            CancellationToken.None));
    }

    [Fact]
    public async Task Production_never_falls_back_to_development_otp()
    {
        var provider = new DevelopmentOtpVerificationProvider(
            new TestEnvironment(Environments.Production));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.RequestAsync("0812345678", CancellationToken.None));
    }

    [Fact]
    public async Task Development_otp_resend_reports_a_retryable_cooldown()
    {
        var provider = new DevelopmentOtpVerificationProvider(
            new TestEnvironment(Environments.Development));
        var phone = $"086{Random.Shared.Next(10_000_000):D7}";

        await provider.RequestAsync(phone, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<RequestCooldownException>(() =>
            provider.RequestAsync(phone, CancellationToken.None));

        Assert.InRange(
            exception.RetryAfter,
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromSeconds(60));
        Assert.Contains("ก่อนขอรหัสใหม่", exception.Message);
    }

    [Fact]
    public void Seller_can_reuse_and_update_owned_payout_account()
    {
        var now = new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);
        var seller = SellerAccount.Create("+66812345678", now);
        var account = seller.SavePayoutAccount(
            null, "KBANK", "สมชาย ใจดี", "1234567890", now);

        seller.SavePayoutAccount(
            account.Id, "SCB", "สมชาย ใจดี", "", now.AddMinutes(1));

        Assert.Single(seller.PayoutAccounts);
        Assert.Equal("SCB", account.BankCode);
        Assert.Equal("1234567890", account.AccountNumber);
        Assert.Equal("•••• ••7890", account.MaskedNumber);
    }

    [Fact]
    public void Seller_profile_uses_registered_full_name_instead_of_phone()
    {
        var seller = SellerAccount.Create(
            "+66812345678",
            DateTimeOffset.UtcNow,
            "สมชาย   ใจดี");

        Assert.Equal("สมชาย ใจดี", seller.DisplayName);

        seller.UpdateDisplayName("สมชาย ใจเย็น");

        Assert.Equal("สมชาย ใจเย็น", seller.DisplayName);
    }

    [Fact]
    public void Seller_cannot_update_an_account_outside_their_profile()
    {
        var seller = SellerAccount.Create(
            "+66812345678",
            DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() =>
            seller.SavePayoutAccount(
                Guid.NewGuid(),
                "KBANK",
                "สมชาย ใจดี",
                "1234567890",
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Repository_persists_new_payout_account_for_existing_seller()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var seller = SellerAccount.Create("+66876543210", DateTimeOffset.UtcNow);
        await using (var setup = new ToklongDbContext(options))
        {
            setup.Sellers.Add(seller);
            await setup.SaveChangesAsync();
        }

        await using (var db = new ToklongDbContext(options))
        {
            var repository = new SellerRepository(db);
            var loaded = await repository.GetByIdAsync(
                seller.Id, CancellationToken.None);
            var account = loaded!.SavePayoutAccount(
                null, "KBANK", "ผู้ขาย ทดสอบ", "1234567890",
                DateTimeOffset.UtcNow);
            await repository.AddPayoutAccountAsync(account, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        await using (var verification = new ToklongDbContext(options))
        {
            Assert.Single(verification.SellerPayoutAccounts);
        }
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Toklong.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
