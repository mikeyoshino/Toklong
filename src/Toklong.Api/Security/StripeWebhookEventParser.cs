using Stripe;

namespace Toklong.Api.Security;

public sealed class StripeWebhookEventParser
{
    public Event Parse(
        string payload,
        string signature,
        string webhookSecret) =>
        EventUtility.ConstructEvent(
            payload,
            signature,
            webhookSecret);
}
