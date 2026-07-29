using Toklong.Domain.Buyers;
using Toklong.Domain.Common;

namespace Toklong.Domain.Tests.Buyers;

public sealed class BuyerEmailVerificationAttemptTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Records_only_normalized_replay_provenance_and_outcome()
    {
        var attempt = new BuyerEmailVerificationAttempt(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"  {Guid.NewGuid():N}  ",
            new string('A', 64),
            BuyerEmailVerificationAttemptOutcome.Incorrect,
            4,
            Now,
            null);

        Assert.Equal(32, attempt.IdempotencyKey.Length);
        Assert.Equal(new string('a', 64), attempt.SubmittedDigest);
        Assert.Equal(
            BuyerEmailVerificationAttemptOutcome.Incorrect,
            attempt.Outcome);
        Assert.Equal(4, attempt.RemainingAttempts);
        Assert.Null(attempt.CompletedAt);
    }

    [Fact]
    public void Verified_attempt_requires_completion_time()
    {
        Assert.Throws<DomainException>(() =>
            new BuyerEmailVerificationAttempt(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid().ToString("N"),
                new string('a', 64),
                BuyerEmailVerificationAttemptOutcome.Verified,
                5,
                Now,
                null));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void Remaining_attempts_must_be_bounded(int remainingAttempts)
    {
        Assert.Throws<DomainException>(() =>
            new BuyerEmailVerificationAttempt(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid().ToString("N"),
                new string('a', 64),
                BuyerEmailVerificationAttemptOutcome.Incorrect,
                remainingAttempts,
                Now,
                null));
    }
}
