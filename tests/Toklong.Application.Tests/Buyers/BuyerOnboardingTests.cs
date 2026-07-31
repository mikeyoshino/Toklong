using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Buyers;
using Toklong.Domain.Buyers;
using Toklong.Domain.Common;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Buyers;

public sealed class BuyerOnboardingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Buyer_profile_requires_first_and_last_name()
    {
        Assert.Throws<DomainException>(() =>
            BuyerAccount.Create(
                "+66812345678",
                "สมชาย",
                "buyer@example.com",
                Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("buyer @example.com")]
    public void Buyer_profile_requires_a_valid_payment_contact_email(
        string email)
    {
        Assert.Throws<DomainException>(() =>
            BuyerAccount.Create(
                "+66812345678",
                "สมชาย ใจดี",
                email,
                Now));
    }

    [Fact]
    public async Task Registration_creates_buyer_profile_with_full_name()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ToklongDbContext(options);
        var handler = new RegisterBuyerHandler(
            new SuccessfulOtpProvider("+66812345678"),
            new BuyerRepository(db),
            db,
            new FixedClock(Now));

        var profile = await handler.Handle(
            new RegisterBuyerCommand(
                "challenge",
                "123456",
                "สมชาย ใจดี",
                "buyer@example.com"),
            default);

        Assert.Equal("สมชาย ใจดี", profile.FullName);
        Assert.Equal("buyer@example.com", profile.Email);
        Assert.Equal("+66812345678", profile.PhoneNumber);
        Assert.Single(db.Buyers);
    }

    [Fact]
    public async Task Sign_in_uses_saved_name_without_asking_for_or_overwriting_it()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ToklongDbContext(options);
        var buyer = BuyerAccount.Create(
            "+66812345678",
            "สมชาย ใจดี",
            "buyer@example.com",
            Now);
        db.Buyers.Add(buyer);
        await db.SaveChangesAsync();
        var handler = new VerifyBuyerOtpHandler(
            new SuccessfulOtpProvider("+66812345678"),
            new BuyerRepository(db),
            db,
            new FixedClock(Now.AddDays(1)));

        var profile = await handler.Handle(
            new VerifyBuyerOtpCommand("challenge", "123456"),
            default);

        Assert.Equal("สมชาย ใจดี", profile.FullName);
        Assert.Equal(Now.AddDays(1), profile.PhoneVerifiedAt);
        Assert.Single(db.Buyers);
    }

    [Fact]
    public async Task Sign_in_rejects_a_phone_without_an_account()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ToklongDbContext(options);
        var handler = new VerifyBuyerOtpHandler(
            new SuccessfulOtpProvider("+66812345678"),
            new BuyerRepository(db),
            db,
            new FixedClock(Now));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(
                new VerifyBuyerOtpCommand("challenge", "123456"),
                default));

        Assert.Contains("สมัครสมาชิก", exception.Message);
        Assert.Empty(db.Buyers);
    }

    [Fact]
    public async Task Registration_rejects_an_existing_phone()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ToklongDbContext(options);
        db.Buyers.Add(BuyerAccount.Create(
            "+66812345678",
            "สมชาย ใจดี",
            "buyer@example.com",
            Now));
        await db.SaveChangesAsync();
        var handler = new RegisterBuyerHandler(
            new SuccessfulOtpProvider("+66812345678"),
            new BuyerRepository(db),
            db,
            new FixedClock(Now));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(
                new RegisterBuyerCommand(
                    "challenge",
                    "123456",
                    "ชื่อ ใหม่",
                    "buyer@example.com"),
                default));

        Assert.Contains("เข้าสู่ระบบ", exception.Message);
        Assert.Single(db.Buyers);
    }

    private sealed class SuccessfulOtpProvider(string phone)
        : IOtpVerificationProvider
    {
        public Task<OtpChallenge> RequestAsync(
            string phoneNumber,
            OtpPurpose purpose,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> VerifyAsync(
            string challengeId,
            string code,
            OtpPurpose purpose,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(
                purpose == OtpPurpose.MobileAuthentication
                    ? phone
                    : null);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
