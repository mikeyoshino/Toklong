using Microsoft.Extensions.Logging;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class LoggingMobileAnalytics(
    ILogger<LoggingMobileAnalytics> logger) : IMobileAnalytics
{
    public void Track(MobileAnalyticsEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        logger.LogInformation(
            "Mobile analytics {EventName} {@Properties}",
            value.Name,
            value.Properties);
    }
}
