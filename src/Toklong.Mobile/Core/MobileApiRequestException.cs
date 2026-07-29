using System.Net;

namespace Toklong.Mobile.Core;

public sealed class MobileApiRequestException(
    HttpStatusCode statusCode,
    string message,
    TimeSpan? retryAfter) : InvalidOperationException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
