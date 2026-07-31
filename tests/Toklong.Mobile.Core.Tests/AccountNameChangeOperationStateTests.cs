using System.Net;
using System.Text.RegularExpressions;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class AccountNameChangeOperationStateTests
{
    [Fact]
    public void Request_key_is_valid_stable_for_normalized_fields_and_rotates_after_authoritative_outcomes()
    {
        var state = new AccountNameChangeOperationState(
            new AuthenticatedSessionBoundary());

        var first = state.GetRequestKey("  ชื่อ  ", "นามสกุล");
        var normalized = state.GetRequestKey("ชื่อ", "นามสกุล");
        state.RecordRequestFailure(new HttpRequestException());
        var afterNetwork = state.GetRequestKey("ชื่อ", "นามสกุล");
        state.RecordRequestFailure(OutcomeUnknown());
        var afterUnknown = state.GetRequestKey("ชื่อ", "นามสกุล");
        state.RecordRequestFailure(new InvalidOperationException("response was incomplete"));
        var afterIncompleteResponse = state.GetRequestKey("ชื่อ", "นามสกุล");
        state.RecordRequestFailure(AuthoritativeFailure());
        var afterAuthoritativeFailure = state.GetRequestKey("ชื่อ", "นามสกุล");
        state.RecordRequestSuccess();
        var afterSuccess = state.GetRequestKey("ชื่อ", "นามสกุล");
        var afterFieldChange = state.GetRequestKey("ชื่อใหม่", "นามสกุล");

        AssertValidKey(first);
        Assert.Equal(first, normalized);
        Assert.Equal(first, afterNetwork);
        Assert.Equal(first, afterUnknown);
        Assert.Equal(first, afterIncompleteResponse);
        Assert.NotEqual(first, afterAuthoritativeFailure);
        Assert.NotEqual(afterAuthoritativeFailure, afterSuccess);
        Assert.NotEqual(afterSuccess, afterFieldChange);
    }

    [Fact]
    public void Resend_and_verification_keys_are_isolated_and_rotate_for_their_own_associated_input()
    {
        var state = new AccountNameChangeOperationState(
            new AuthenticatedSessionBoundary());
        var firstChallenge = Guid.Parse("b1f9f3e3-1817-4677-82dd-86687f4e20a4");
        var replacementChallenge = Guid.Parse("e4299eb1-fd1a-44a9-95b9-d5996fa7cf10");

        var request = state.GetRequestKey("ชื่อ", "นามสกุล");
        var resend = state.GetResendKey(firstChallenge);
        var verify = state.GetVerificationKey(firstChallenge, " 123456 ");
        state.RecordResendFailure(new HttpRequestException());
        var resendAfterNetwork = state.GetResendKey(firstChallenge);
        state.RecordVerificationFailure(OutcomeUnknown());
        var verifyAfterUnknown = state.GetVerificationKey(firstChallenge, "123456");
        var resendAfterChallengeChange = state.GetResendKey(replacementChallenge);
        var verifyAfterCodeChange = state.GetVerificationKey(firstChallenge, "654321");
        var verifyAfterChallengeChange = state.GetVerificationKey(replacementChallenge, "654321");
        state.RecordVerificationSuccess();
        var verifyAfterSuccess = state.GetVerificationKey(replacementChallenge, "654321");

        AssertValidKey(resend);
        AssertValidKey(verify);
        Assert.NotEqual(request, resend);
        Assert.NotEqual(request, verify);
        Assert.NotEqual(resend, verify);
        Assert.Equal(resend, resendAfterNetwork);
        Assert.Equal(verify, verifyAfterUnknown);
        Assert.NotEqual(resend, resendAfterChallengeChange);
        Assert.NotEqual(verify, verifyAfterCodeChange);
        Assert.NotEqual(verifyAfterCodeChange, verifyAfterChallengeChange);
        Assert.NotEqual(verifyAfterChallengeChange, verifyAfterSuccess);
    }

    [Fact]
    public void Session_reset_discards_all_in_flight_operation_keys()
    {
        var session = new AuthenticatedSessionBoundary();
        var state = new AccountNameChangeOperationState(session);
        var challenge = Guid.NewGuid();
        var request = state.GetRequestKey("ชื่อ", "นามสกุล");
        var resend = state.GetResendKey(challenge);
        var verify = state.GetVerificationKey(challenge, "123456");

        session.Reset();

        Assert.NotEqual(request, state.GetRequestKey("ชื่อ", "นามสกุล"));
        Assert.NotEqual(resend, state.GetResendKey(challenge));
        Assert.NotEqual(verify, state.GetVerificationKey(challenge, "123456"));
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
