namespace Toklong.Application.Features.Accounts.NameChanges;

public abstract class AccountNameChangeFlowException(
    string message) : Exception(message);

public enum AccountNameInputField
{
    FirstName,
    LastName
}

public sealed class AccountNameChangeInputException(
    AccountNameInputField field,
    string message) : AccountNameChangeFlowException(message)
{
    public AccountNameInputField Field { get; } = field;
}

public sealed class AccountNameChangeUnchangedNameException()
    : AccountNameChangeFlowException("ชื่อนี้เป็นชื่อปัจจุบันของคุณแล้ว");

public sealed class AccountNameChangeInvalidIdempotencyException()
    : AccountNameChangeFlowException("รหัสคำขอไม่ถูกต้อง");

public sealed class AccountNameChangeIdempotencyConflictException()
    : AccountNameChangeFlowException("คำขอนี้ไม่ตรงกับข้อมูลเดิม");

public enum AccountNameVerificationFailure
{
    Incorrect,
    Locked,
    Expired,
    Inactive,
    MalformedCode,
    NonExactReplay
}

public sealed class AccountNameChangeVerificationException(
    AccountNameVerificationFailure failure,
    int? remainingAttempts = null)
    : AccountNameChangeFlowException("การยืนยันชื่อไม่สำเร็จ")
{
    public AccountNameVerificationFailure Failure { get; } = failure;
    public int? RemainingAttempts { get; } = remainingAttempts;
}

public sealed class AccountNameChangeProviderUnavailableException()
    : AccountNameChangeFlowException("บริการยืนยันชื่อยังไม่พร้อมใช้งาน");

public sealed class AccountNameChangeProviderOutcomeUnknownException()
    : AccountNameChangeFlowException("กำลังตรวจสอบผลการยืนยัน");

public sealed class AccountNameChangeProviderThrottleException(
    TimeSpan retryAfter)
    : AccountNameChangeFlowException("กรุณารอก่อนขอรหัสยืนยันอีกครั้ง")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
