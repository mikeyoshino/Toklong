using System.Net;

namespace Toklong.Mobile.Core;

public sealed class MobileApiRequestException(
    HttpStatusCode statusCode,
    string message,
    TimeSpan? retryAfter,
    string? code = null,
    string? field = null,
    int? remainingAttempts = null,
    DateTimeOffset? nextAllowedAt = null) : InvalidOperationException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public string? Code { get; } = code;
    public string? Field { get; } = field;
    public int? RemainingAttempts { get; } = remainingAttempts;
    public DateTimeOffset? NextAllowedAt { get; } = nextAllowedAt;
}
