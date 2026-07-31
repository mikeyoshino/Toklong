using Toklong.Domain.Accounts;
using Toklong.Domain.Common;

namespace Toklong.Domain.Tests.Accounts;

public sealed class AccountNameChangeChallengeTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_starts_pending_send_without_provider_reference()
    {
        var challenge = NewChallenge();

        Assert.Equal(AccountNameChangeStatus.PendingSend, challenge.Status);
        Assert.Null(challenge.ProviderChallengeId);
        Assert.Null(challenge.SendAcceptedAt);
        Assert.Equal("สมศักดิ์", challenge.PendingFirstName);
        Assert.Equal("ใจดี", challenge.PendingLastName);
        Assert.Equal(5, challenge.RemainingAttempts);
        Assert.Equal(0, challenge.Version);
    }

    [Fact]
    public void Accepted_send_activates_ten_minute_code_and_sixty_second_resend()
    {
        var challenge = NewChallenge();
        var acceptedAt = Now.AddSeconds(1);

        challenge.MarkSendAccepted("provider-challenge", acceptedAt);

        Assert.Equal(AccountNameChangeStatus.Active, challenge.Status);
        Assert.Equal("provider-challenge", challenge.ProviderChallengeId);
        Assert.Equal(acceptedAt, challenge.SendAcceptedAt);
        Assert.Equal(acceptedAt.AddMinutes(10), challenge.ExpiresAt);
        Assert.Equal(acceptedAt.AddSeconds(60), challenge.ResendAvailableAt);
        Assert.Equal(1, challenge.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Accepted_send_rejects_empty_provider_challenge_id(string value)
    {
        var challenge = NewChallenge();

        Assert.Throws<DomainException>(
            () => challenge.MarkSendAccepted(value, Now));
        Assert.Equal(AccountNameChangeStatus.PendingSend, challenge.Status);
    }

    [Fact]
    public void Verification_at_expiry_closes_challenge_without_accepting_code()
    {
        var challenge = ActiveChallenge();

        var outcome = challenge.RecordVerification(
            Key(),
            providerAccepted: true,
            challenge.ExpiresAt!.Value);

        Assert.Equal(AccountNameVerificationOutcome.Expired, outcome);
        Assert.Equal(AccountNameChangeStatus.Expired, challenge.Status);
        Assert.Null(challenge.VerifiedAt);
    }

    [Fact]
    public void Expire_closes_active_challenge_at_exact_boundary()
    {
        var challenge = ActiveChallenge();
        var expiresAt = challenge.ExpiresAt!.Value;

        Assert.Throws<DomainException>(
            () => challenge.Expire(expiresAt.AddTicks(-1)));

        challenge.Expire(expiresAt);

        Assert.Equal(AccountNameChangeStatus.Expired, challenge.Status);
        Assert.Equal(2, challenge.Version);
    }

    [Fact]
    public void Resend_is_rejected_until_sixty_seconds_after_accepted_send()
    {
        var challenge = ActiveChallenge();

        Assert.Throws<DomainException>(
            () => challenge.EnsureCanResend(
                challenge.ResendAvailableAt!.Value.AddTicks(-1)));

        challenge.EnsureCanResend(challenge.ResendAvailableAt!.Value);
    }

    [Fact]
    public void Fifth_incorrect_verification_locks_challenge()
    {
        var challenge = ActiveChallenge();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var outcome = challenge.RecordVerification(
                Key(),
                providerAccepted: false,
                Now.AddMinutes(1).AddSeconds(attempt));

            Assert.Equal(
                attempt == 5
                    ? AccountNameVerificationOutcome.Locked
                    : AccountNameVerificationOutcome.Incorrect,
                outcome);
            Assert.Equal(5 - attempt, challenge.RemainingAttempts);
        }

        Assert.Equal(AccountNameChangeStatus.Locked, challenge.Status);
        Assert.Equal(Now.AddMinutes(1).AddSeconds(5), challenge.LockedAt);
    }

    [Fact]
    public void Successful_verification_allows_only_an_exact_replay()
    {
        var challenge = ActiveChallenge();
        var key = Key();
        var verifiedAt = Now.AddMinutes(1);

        var first = challenge.RecordVerification(
            key,
            providerAccepted: true,
            verifiedAt);
        var replay = challenge.RecordVerification(
            key,
            providerAccepted: true,
            verifiedAt.AddSeconds(5));

        Assert.Equal(AccountNameVerificationOutcome.Verified, first);
        Assert.Equal(AccountNameVerificationOutcome.ExactReplay, replay);
        Assert.Equal(AccountNameChangeStatus.Verified, challenge.Status);
        Assert.Equal(verifiedAt, challenge.VerifiedAt);
        Assert.Throws<DomainException>(
            () => challenge.RecordVerification(
                Key(),
                providerAccepted: true,
                verifiedAt.AddSeconds(6)));
    }

    [Fact]
    public void Supersede_closes_an_active_challenge()
    {
        var challenge = ActiveChallenge();
        var supersededAt = Now.AddMinutes(2);

        challenge.Supersede(supersededAt);

        Assert.Equal(AccountNameChangeStatus.Superseded, challenge.Status);
        Assert.Equal(supersededAt, challenge.SupersededAt);
        Assert.Throws<DomainException>(
            () => challenge.RecordVerification(
                Key(),
                providerAccepted: true,
                supersededAt));
    }

    [Fact]
    public void Send_failure_is_terminal_and_never_activates_code()
    {
        var challenge = NewChallenge();
        var failedAt = Now.AddSeconds(2);

        challenge.MarkSendFailed(
            failedAt,
            "otp_provider_cooldown",
            "กรุณารออีก 37 วินาที",
            TimeSpan.FromSeconds(37));

        Assert.Equal(AccountNameChangeStatus.SendFailed, challenge.Status);
        Assert.Equal(failedAt, challenge.SendFailedAt);
        Assert.Equal(
            "otp_provider_cooldown",
            challenge.SendFailureCode);
        Assert.Equal(
            "กรุณารออีก 37 วินาที",
            challenge.SendFailureMessage);
        Assert.Equal(
            TimeSpan.FromSeconds(37).Ticks,
            challenge.SendFailureRetryAfterTicks);
        Assert.Null(challenge.ProviderChallengeId);
        Assert.Null(challenge.ExpiresAt);
        Assert.Throws<DomainException>(
            () => challenge.MarkSendAccepted(
                "late-provider-result",
                failedAt.AddSeconds(1)));
    }

    [Theory]
    [InlineData("OTP CODE", "กรุณารอ")]
    [InlineData("otp_provider_cooldown", "")]
    public void Send_failure_rejects_unbounded_or_unsafe_evidence(
        string code,
        string message)
    {
        var challenge = NewChallenge();

        Assert.Throws<DomainException>(() =>
            challenge.MarkSendFailed(
                Now,
                code,
                message,
                TimeSpan.FromSeconds(30)));

        Assert.Equal(
            AccountNameChangeStatus.PendingSend,
            challenge.Status);
    }

    [Fact]
    public void Resend_persists_source_kind_and_content_fingerprint()
    {
        var source = ActiveChallenge();
        var replacement = AccountNameChangeChallenge.Create(
            Guid.NewGuid(),
            source.BuyerId,
            source.SellerId,
            source.SessionId,
            source.PhoneNumber,
            source.MaskedPhoneNumber,
            AccountName.Create(
                source.PendingFirstName,
                source.PendingLastName),
            Key(),
            Now.AddMinutes(2),
            source.Id);
        var anotherSource = AccountNameChangeChallenge.Create(
            Guid.NewGuid(),
            source.BuyerId,
            source.SellerId,
            source.SessionId,
            source.PhoneNumber,
            source.MaskedPhoneNumber,
            AccountName.Create(
                source.PendingFirstName,
                source.PendingLastName),
            replacement.RequestIdempotencyKey,
            Now.AddMinutes(2),
            Guid.NewGuid());

        Assert.Equal(
            AccountNameChangeOperationKind.Resend,
            replacement.OperationKind);
        Assert.Equal(source.Id, replacement.SourceChallengeId);
        Assert.Equal(64, replacement.OperationFingerprint.Length);
        Assert.NotEqual(
            replacement.OperationFingerprint,
            anotherSource.OperationFingerprint);
        Assert.Equal(
            replacement.RequestIdempotencyKey,
            replacement.ProviderRequestKey);
    }

    [Fact]
    public void Initial_request_has_distinct_persisted_operation_provenance()
    {
        var challenge = NewChallenge();

        Assert.Equal(
            AccountNameChangeOperationKind.InitialRequest,
            challenge.OperationKind);
        Assert.Null(challenge.SourceChallengeId);
        Assert.Equal(64, challenge.OperationFingerprint.Length);
    }

    [Fact]
    public void Exact_resend_replay_requires_the_same_key_source_and_name()
    {
        var source = ActiveChallenge();
        var name = AccountName.Create("สมศักดิ์", "ใจดี");
        var key = Key();
        var replacement = AccountNameChangeChallenge.Create(
            Guid.NewGuid(),
            source.BuyerId,
            source.SellerId,
            source.SessionId,
            source.PhoneNumber,
            source.MaskedPhoneNumber,
            name,
            key,
            Now.AddMinutes(2),
            source.Id);

        replacement.EnsureExactOperationReplay(
            key,
            source.Id,
            name);
    }

    [Fact]
    public void Replay_rejects_the_same_key_for_a_different_source()
    {
        var source = ActiveChallenge();
        var name = AccountName.Create("สมศักดิ์", "ใจดี");
        var key = Key();
        var replacement = AccountNameChangeChallenge.Create(
            Guid.NewGuid(),
            source.BuyerId,
            source.SellerId,
            source.SessionId,
            source.PhoneNumber,
            source.MaskedPhoneNumber,
            name,
            key,
            Now.AddMinutes(2),
            source.Id);

        Assert.Throws<DomainException>(() =>
            replacement.EnsureExactOperationReplay(
                key,
                Guid.NewGuid(),
                name));
    }

    [Fact]
    public void Replay_rejects_a_different_key_for_the_same_source()
    {
        var source = ActiveChallenge();
        var name = AccountName.Create("สมศักดิ์", "ใจดี");
        var replacement = AccountNameChangeChallenge.Create(
            Guid.NewGuid(),
            source.BuyerId,
            source.SellerId,
            source.SessionId,
            source.PhoneNumber,
            source.MaskedPhoneNumber,
            name,
            Key(),
            Now.AddMinutes(2),
            source.Id);

        Assert.Throws<DomainException>(() =>
            replacement.EnsureExactOperationReplay(
                Key(),
                source.Id,
                name));
    }

    private static AccountNameChangeChallenge ActiveChallenge()
    {
        var challenge = NewChallenge();
        challenge.MarkSendAccepted("provider-challenge", Now.AddSeconds(1));
        return challenge;
    }

    private static AccountNameChangeChallenge NewChallenge() =>
        AccountNameChangeChallenge.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "+66812345678",
            "081-•••-5678",
            AccountName.Create("สมศักดิ์", "ใจดี"),
            Key(),
            Now);

    private static string Key() => Guid.NewGuid().ToString("N");

}
