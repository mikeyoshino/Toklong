namespace Toklong.Application.Common;

public sealed class NotFoundException(string message) : Exception(message);

public sealed class ForbiddenException(string message) : Exception(message);

public sealed class RequestCooldownException(
    string message,
    TimeSpan retryAfter,
    string code = "request_cooldown") : Exception(message)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
    public string Code { get; } = string.IsNullOrWhiteSpace(code)
        ? "request_cooldown"
        : code.Trim();
}

public sealed record CommandResult(bool Success, string? Error = null)
{
    public static CommandResult Ok() => new(true);
    public static CommandResult Fail(string error) => new(false, error);
}
