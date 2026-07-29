namespace Toklong.Mobile.Core;

public enum EmailChangeErrorTarget
{
    EmailInput,
    CodeInput,
    ResendAction,
    VerificationAction,
    NewRequestAction,
    AccountReturnAction
}

public sealed record EmailChangeErrorNotice(
    EmailChangeErrorTarget Target,
    string Message);
