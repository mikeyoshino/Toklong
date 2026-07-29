using Toklong.Domain.Buyers;
using Toklong.Domain.Common;

namespace Toklong.Domain.Tests.Buyers;

public sealed class BuyerEmailChangeChallengeTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid BuyerId = Guid.NewGuid();
    private const string CorrectDigest =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string WrongDigest =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Challenge_uses_approved_expiry_and_resend_windows()
    {
        var challenge = NewChallenge();

        Assert.Equal(Now.AddMinutes(10), challenge.ExpiresAt);
        Assert.Equal(Now.AddSeconds(60), challenge.ResendAvailableAt);
        Assert.Equal(BuyerEmailChangeStatus.PendingSend, challenge.Status);
        Assert.Null(challenge.SourceChallengeId);
    }

    [Fact]
    public void Resend_replacement_records_a_distinct_source_challenge()
    {
        var replacementId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();

        var replacement = BuyerEmailChangeChallenge.Create(
            replacementId,
            BuyerId,
            "next@example.com",
            "ne••@example.com",
            CorrectDigest,
            NewRequestKey(),
            Now,
            sourceId);

        Assert.Equal(sourceId, replacement.SourceChallengeId);
        Assert.Throws<DomainException>(() =>
            BuyerEmailChangeChallenge.Create(
                Guid.NewGuid(),
                BuyerId,
                "next@example.com",
                "ne••@example.com",
                CorrectDigest,
                NewRequestKey(),
                Now,
                Guid.Empty));
        Assert.Throws<DomainException>(() =>
            BuyerEmailChangeChallenge.Create(
                replacementId,
                BuyerId,
                "next@example.com",
                "ne••@example.com",
                CorrectDigest,
                NewRequestKey(),
                Now,
                replacementId));
    }

    [Fact]
    public void Create_rejects_invalid_identity_digest_and_request_key()
    {
        Assert.Throws<DomainException>(() => BuyerEmailChangeChallenge.Create(
            Guid.NewGuid(),
            Guid.Empty,
            "next@example.com",
            "n***@example.com",
            CorrectDigest,
            NewRequestKey(),
            Now));
        Assert.Throws<DomainException>(() => BuyerEmailChangeChallenge.Create(
            Guid.NewGuid(),
            BuyerId,
            "next@example.com",
            "n***@example.com",
            "not-a-digest",
            NewRequestKey(),
            Now));
        Assert.Throws<DomainException>(() => BuyerEmailChangeChallenge.Create(
            Guid.NewGuid(),
            BuyerId,
            "next@example.com",
            "n***@example.com",
            CorrectDigest,
            "not-a-key",
            Now));
    }

    [Fact]
    public void Send_acceptance_is_allowed_only_from_pending_send()
    {
        var challenge = NewChallenge();

        challenge.MarkSendAccepted(Now.AddSeconds(1));

        Assert.Equal(BuyerEmailChangeStatus.Active, challenge.Status);
        Assert.Throws<DomainException>(() =>
            challenge.MarkSendAccepted(Now.AddSeconds(2)));
    }

    [Fact]
    public void Resend_is_not_available_until_sixty_seconds_after_creation()
    {
        var challenge = ActiveChallenge();

        Assert.Throws<DomainException>(() =>
            challenge.EnsureCanResend(Now.AddSeconds(59)));

        challenge.EnsureCanResend(Now.AddSeconds(60));
    }

    [Fact]
    public void Resend_is_not_available_after_challenge_expiry()
    {
        Assert.Throws<DomainException>(() =>
            ActiveChallenge().EnsureCanResend(Now.AddMinutes(10)));
    }

    [Fact]
    public void Fifth_wrong_digest_locks_the_challenge()
    {
        var challenge = ActiveChallenge();

        for (var attempt = 1; attempt <= 4; attempt++)
            Assert.Equal(
                BuyerEmailVerificationOutcome.Incorrect,
                challenge.Verify(WrongDigest, NewRequestKey(), Now));

        Assert.Equal(
            BuyerEmailVerificationOutcome.Locked,
            challenge.Verify(WrongDigest, NewRequestKey(), Now));
        Assert.Equal(BuyerEmailChangeStatus.Locked, challenge.Status);
        Assert.Equal(0, challenge.RemainingAttempts);
    }

    [Fact]
    public void Equivalent_hex_digest_verifies_using_decoded_bytes()
    {
        var challenge = ActiveChallenge();

        var outcome = challenge.Verify(CorrectDigest.ToUpperInvariant(), NewRequestKey(), Now);

        Assert.Equal(BuyerEmailVerificationOutcome.Verified, outcome);
        Assert.Equal(BuyerEmailChangeStatus.Verified, challenge.Status);
    }

    [Fact]
    public void Exact_completion_replay_is_idempotent()
    {
        var challenge = ActiveChallenge();
        var key = NewRequestKey();

        Assert.Equal(
            BuyerEmailVerificationOutcome.Verified,
            challenge.Verify(CorrectDigest, key, Now));
        Assert.Equal(
            BuyerEmailVerificationOutcome.ExactReplay,
            challenge.Verify(CorrectDigest, key, Now.AddSeconds(1)));
    }

    [Fact]
    public void Verify_rejects_invalid_digest_and_idempotency_key()
    {
        var challenge = ActiveChallenge();

        Assert.Throws<DomainException>(() =>
            challenge.Verify("too-short", NewRequestKey(), Now));
        Assert.Throws<DomainException>(() =>
            challenge.Verify(CorrectDigest, "not-a-key", Now));
    }

    [Fact]
    public void Superseded_expired_and_send_failed_challenges_cannot_verify()
    {
        Assert.Throws<DomainException>(() =>
            SupersededChallenge().Verify(CorrectDigest, NewRequestKey(), Now));
        Assert.Throws<DomainException>(() =>
            ActiveChallenge().Verify(CorrectDigest, NewRequestKey(), Now.AddMinutes(10)));
        Assert.Throws<DomainException>(() =>
            SendFailedChallenge().Verify(CorrectDigest, NewRequestKey(), Now));
    }

    [Fact]
    public void Terminal_challenges_cannot_be_failed_or_superseded()
    {
        var active = ActiveChallenge();

        Assert.Throws<DomainException>(() => active.MarkSendFailed(Now));
        active.Supersede(Now);
        Assert.Throws<DomainException>(() => active.Supersede(Now));
        Assert.Throws<DomainException>(() =>
            SendFailedChallenge().Supersede(Now));
    }

    [Fact]
    public void Activation_changes_only_a_new_normalized_verified_email()
    {
        var buyer = BuyerAccount.Create(
            "+66812345678",
            "Buyer Example",
            "current@example.com",
            Now);

        buyer.ActivateVerifiedEmail("  next@example.com ");

        Assert.Equal("next@example.com", buyer.Email);
        Assert.Throws<DomainException>(() =>
            buyer.ActivateVerifiedEmail("NEXT@example.com"));
    }

    [Fact]
    public void Audit_event_stores_only_masked_and_hashed_destination_evidence()
    {
        var audit = new BuyerEmailChangeAuditEvent(
            BuyerId,
            Guid.NewGuid(),
            "account.email_change_requested",
            CorrectDigest,
            "n***@example.com",
            Now,
            "accepted");

        Assert.Equal(BuyerId, audit.BuyerId);
        Assert.Equal(CorrectDigest, audit.DestinationHash);
        Assert.Equal("n***@example.com", audit.MaskedDestination);
        var bulletMaskedAudit = new BuyerEmailChangeAuditEvent(
            BuyerId,
            Guid.NewGuid(),
            "account.email_change_requested",
            CorrectDigest,
            "ne••@example.com",
            Now,
            "accepted");
        Assert.Equal("ne••@example.com", bulletMaskedAudit.MaskedDestination);
        Assert.Throws<DomainException>(() => new BuyerEmailChangeAuditEvent(
            BuyerId,
            Guid.NewGuid(),
            "account.email_change_requested",
            "not-a-hash",
            "n***@example.com",
            Now,
            "accepted"));
        Assert.Throws<DomainException>(() => new BuyerEmailChangeAuditEvent(
            BuyerId,
            Guid.NewGuid(),
            "account.email_change_requested",
            CorrectDigest,
            "next@example.com*",
            Now,
            "accepted"));
        Assert.Throws<DomainException>(() => new BuyerEmailChangeAuditEvent(
            BuyerId,
            Guid.NewGuid(),
            "account.email_change_requested",
            CorrectDigest,
            "n***@next@example.com",
            Now,
            "accepted"));
    }

    [Fact]
    public void Challenge_rejects_a_full_pending_email_with_an_appended_mask_character()
    {
        Assert.Throws<DomainException>(() => BuyerEmailChangeChallenge.Create(
            Guid.NewGuid(),
            BuyerId,
            "next@example.com",
            "next@example.com*",
            CorrectDigest,
            NewRequestKey(),
            Now));
    }

    [Fact]
    public void Challenge_mask_must_match_its_pending_email()
    {
        Assert.Throws<DomainException>(() => BuyerEmailChangeChallenge.Create(
            Guid.NewGuid(),
            BuyerId,
            "next@example.com",
            "n***@next@example.com",
            CorrectDigest,
            NewRequestKey(),
            Now));
        Assert.Throws<DomainException>(() => BuyerEmailChangeChallenge.Create(
            Guid.NewGuid(),
            BuyerId,
            "next@example.com",
            "n***@other.example",
            CorrectDigest,
            NewRequestKey(),
            Now));
        Assert.Throws<DomainException>(() => BuyerEmailChangeChallenge.Create(
            Guid.NewGuid(),
            BuyerId,
            "next@example.com",
            "x***@example.com",
            CorrectDigest,
            NewRequestKey(),
            Now));
    }

    [Theory]
    [InlineData("a@example.com", "a••@example.com")]
    [InlineData("ab@example.com", "ab••@example.com")]
    public void Challenge_mask_cannot_reveal_every_real_local_character(
        string pendingEmail,
        string maskedEmail)
    {
        Assert.Throws<DomainException>(() =>
            BuyerEmailChangeChallenge.Create(
                Guid.NewGuid(),
                BuyerId,
                pendingEmail,
                maskedEmail,
                CorrectDigest,
                NewRequestKey(),
                Now));
    }

    [Theory]
    [InlineData("a@example.com", "••@example.com")]
    [InlineData("ab@example.com", "a••@example.com")]
    public void Challenge_mask_hides_at_least_one_real_local_character(
        string pendingEmail,
        string maskedEmail)
    {
        var challenge = BuyerEmailChangeChallenge.Create(
            Guid.NewGuid(),
            BuyerId,
            pendingEmail,
            maskedEmail,
            CorrectDigest,
            NewRequestKey(),
            Now);
        var audit = new BuyerEmailChangeAuditEvent(
            BuyerId,
            challenge.Id,
            "account.email_change_requested",
            CorrectDigest,
            maskedEmail,
            Now,
            "accepted");

        Assert.Equal(maskedEmail, challenge.MaskedPendingEmail);
        Assert.Equal(maskedEmail, audit.MaskedDestination);
    }

    private static BuyerEmailChangeChallenge NewChallenge() =>
        BuyerEmailChangeChallenge.Create(
            Guid.NewGuid(),
            BuyerId,
            "next@example.com",
            "n***@example.com",
            CorrectDigest,
            NewRequestKey(),
            Now);

    private static BuyerEmailChangeChallenge ActiveChallenge()
    {
        var challenge = NewChallenge();
        challenge.MarkSendAccepted(Now);
        return challenge;
    }

    private static BuyerEmailChangeChallenge SupersededChallenge()
    {
        var challenge = ActiveChallenge();
        challenge.Supersede(Now);
        return challenge;
    }

    private static BuyerEmailChangeChallenge SendFailedChallenge()
    {
        var challenge = NewChallenge();
        challenge.MarkSendFailed(Now);
        return challenge;
    }

    private static string NewRequestKey() => Guid.NewGuid().ToString("N");
}
