using System.Net;
using System.Text.RegularExpressions;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class AccountNameChangeOperationStateTests
{
    [Fact]
    public void Old_request_completion_cannot_clear_replacement_after_field_change()
    {
        var session = new AuthenticatedSessionBoundary();
        var state = new AccountNameChangeOperationState(session);
        var oldLease = state.BeginRequest("ชื่อเดิม", "นามสกุล");
        var replacement = state.BeginRequest("ชื่อใหม่", "นามสกุล");

        state.RecordRequestSuccess(oldLease);

        var retry = state.BeginRequest("ชื่อใหม่", "นามสกุล");
        AssertValidKey(oldLease.IdempotencyKey);
        Assert.NotEqual(oldLease.IdempotencyKey, replacement.IdempotencyKey);
        Assert.Equal(replacement.IdempotencyKey, retry.IdempotencyKey);
    }

    [Fact]
    public void Old_session_failure_cannot_clear_replacement_after_reset()
    {
        var session = new AuthenticatedSessionBoundary();
        var state = new AccountNameChangeOperationState(session);
        var oldLease = state.BeginVerification(Guid.NewGuid(), "123456");

        session.Reset();
        var replacementChallenge = Guid.NewGuid();
        var replacement = state.BeginVerification(replacementChallenge, "123456");
        state.RecordVerificationFailure(oldLease, AuthoritativeFailure());

        var retry = state.BeginVerification(
            replacementChallenge,
            "123456");
        Assert.NotEqual(oldLease.IdempotencyKey, replacement.IdempotencyKey);
        Assert.Equal(replacement.IdempotencyKey, retry.IdempotencyKey);
    }

    [Fact]
    public void Old_resend_completion_cannot_clear_replacement_after_source_change()
    {
        var state = new AccountNameChangeOperationState(
            new AuthenticatedSessionBoundary());
        var oldLease = state.BeginResend(Guid.NewGuid());
        var replacementChallenge = Guid.NewGuid();
        var replacement = state.BeginResend(replacementChallenge);

        state.RecordResendSuccess(oldLease);

        Assert.Equal(
            replacement.IdempotencyKey,
            state.BeginResend(replacementChallenge).IdempotencyKey);
    }

    [Fact]
    public void Old_verification_failure_cannot_clear_replacement_after_code_change()
    {
        var state = new AccountNameChangeOperationState(
            new AuthenticatedSessionBoundary());
        var challenge = Guid.NewGuid();
        var oldLease = state.BeginVerification(challenge, "123456");
        var replacement = state.BeginVerification(challenge, "654321");

        state.RecordVerificationFailure(oldLease, AuthoritativeFailure());

        Assert.Equal(
            replacement.IdempotencyKey,
            state.BeginVerification(challenge, "654321").IdempotencyKey);
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public void Only_explicit_ambiguous_failures_retain_the_same_key(
        Exception failure,
        bool shouldReuse)
    {
        var state = new AccountNameChangeOperationState(
            new AuthenticatedSessionBoundary());
        var lease = state.BeginRequest("ชื่อ", "นามสกุล");

        state.RecordRequestFailure(lease, failure);

        var retry = state.BeginRequest("ชื่อ", "นามสกุล");
        Assert.Equal(shouldReuse, lease.IdempotencyKey == retry.IdempotencyKey);
    }

    [Fact]
    public void Operation_types_and_association_changes_have_isolated_leases()
    {
        var state = new AccountNameChangeOperationState(
            new AuthenticatedSessionBoundary());
        var firstChallenge = Guid.NewGuid();
        var replacementChallenge = Guid.NewGuid();
        var request = state.BeginRequest("  ชื่อ  ", "นามสกุล");
        var requestRetry = state.BeginRequest("ชื่อ", "นามสกุล");
        var resend = state.BeginResend(firstChallenge);
        var verification = state.BeginVerification(firstChallenge, " 123456 ");
        var resendReplacement = state.BeginResend(replacementChallenge);
        var verificationCodeChange = state.BeginVerification(firstChallenge, "654321");

        AssertValidKey(request.IdempotencyKey);
        Assert.Equal(request.IdempotencyKey, requestRetry.IdempotencyKey);
        Assert.NotEqual(request.IdempotencyKey, resend.IdempotencyKey);
        Assert.NotEqual(request.IdempotencyKey, verification.IdempotencyKey);
        Assert.NotEqual(resend.IdempotencyKey, verification.IdempotencyKey);
        Assert.NotEqual(resend.IdempotencyKey, resendReplacement.IdempotencyKey);
        Assert.NotEqual(verification.IdempotencyKey, verificationCodeChange.IdempotencyKey);
    }

    public static IEnumerable<object[]> FailureCases()
    {
        yield return [new HttpRequestException(), true];
        yield return [new TimeoutException(), true];
        yield return [new OperationCanceledException(), true];
        yield return [new TaskCanceledException(), true];
        yield return [OutcomeUnknown(), true];
        yield return [new InvalidOperationException(), false];
        yield return [new UnauthorizedAccessException(), false];
        yield return [new MobileApiRequestException(HttpStatusCode.BadRequest, "detail", null), false];
        yield return [new MobileApiRequestException(HttpStatusCode.BadRequest, "detail", null, "name_change_future_code"), false];
        yield return [AuthoritativeFailure(), false];
    }

    private static MobileApiRequestException OutcomeUnknown() =>
        new(
            HttpStatusCode.ServiceUnavailable,
            "never use response text",
            TimeSpan.FromSeconds(5),
            "name_change_provider_outcome_unknown");

    private static MobileApiRequestException AuthoritativeFailure() =>
        new(
            HttpStatusCode.UnprocessableEntity,
            "never use response text",
            null,
            "name_change_invalid_request");

    private static void AssertValidKey(string value)
    {
        Assert.Matches(new Regex("^[0-9a-f]{32}$"), value);
        Assert.True(Guid.TryParseExact(value, "N", out _));
    }
}
