using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Domain.Tests.Transactions;

public sealed class BookingAttemptTests
{
    private static readonly Guid TransactionId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ShipmentId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BuyerId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Attempt_moves_from_created_to_calling_to_succeeded()
    {
        var attempt = NewAttempt();

        attempt.Claim(Now.AddSeconds(1));
        attempt.Succeed(
            Success(),
            Now.AddSeconds(2));

        Assert.Equal(
            BookingAttemptStatus.Succeeded,
            attempt.Status);
        Assert.Equal(
            $"checkout:{attempt.Id:N}",
            attempt.ProviderReference);
        Assert.Equal(
            "purchase-1",
            attempt.ProviderPurchaseId);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.Equal(
            Now.AddSeconds(2),
            attempt.CompletedAt);
    }

    [Fact]
    public void Timed_out_attempt_cannot_be_claimed_or_succeeded()
    {
        var attempt = NewAttempt();
        attempt.Claim(Now);
        attempt.TimeOut(
            "shippop-timeout",
            Now.AddSeconds(2));

        Assert.Equal(
            BookingAttemptStatus.TimedOut,
            attempt.Status);
        Assert.Throws<DomainException>(
            () => attempt.Claim(
                Now.AddSeconds(3)));
        Assert.Throws<DomainException>(
            () => attempt.Succeed(
                Success(),
                Now.AddSeconds(3)));
    }

    [Fact]
    public void Definite_failure_cannot_be_reclaimed()
    {
        var attempt = NewAttempt();
        attempt.Claim(Now);
        attempt.Fail(
            "shipping-price-mismatch",
            Now.AddSeconds(1));

        Assert.Equal(
            BookingAttemptStatus.Failed,
            attempt.Status);
        Assert.Equal(
            "shipping-price-mismatch",
            attempt.SafeFailureCode);
        Assert.Throws<DomainException>(
            () => attempt.Claim(
                Now.AddSeconds(2)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-sha256")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Fingerprint_must_be_lowercase_sha256(
        string fingerprint) =>
        Assert.Throws<DomainException>(
            () => BookingAttempt.Create(
                TransactionId,
                ShipmentId,
                BuyerId,
                "checkout-001",
                fingerprint,
                1,
                Now));

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Attempt_number_is_limited_to_three(
        int attemptNumber) =>
        Assert.Throws<DomainException>(
            () => BookingAttempt.Create(
                TransactionId,
                ShipmentId,
                BuyerId,
                "checkout-001",
                new string('a', 64),
                attemptNumber,
                Now));

    [Fact]
    public void Money_must_not_be_negative()
    {
        var attempt = NewAttempt();
        attempt.Claim(Now);

        Assert.Throws<DomainException>(
            () => attempt.Succeed(
                Success() with
                {
                    ShippingFeeSatang = -1
                },
                Now.AddSeconds(1)));
    }

    private static BookingAttempt NewAttempt() =>
        BookingAttempt.Create(
            TransactionId,
            ShipmentId,
            BuyerId,
            "checkout-001",
            new string('a', 64),
            1,
            Now);

    private static BookingAttemptSuccess Success() =>
        new(
            "purchase-1",
            "provider-track-1",
            "courier-track-1",
            5_200,
            600,
            100_000,
            "THB",
            new string('b', 64));
}
