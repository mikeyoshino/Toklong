using System.Reflection;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class AccountEmailChangeAnalyticsTests
{
    [Fact]
    public void Analytics_constructors_accept_only_coarse_values()
    {
        var publicFactories = typeof(AccountEmailChangeAnalytics)
            .GetMethods(
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(
            publicFactories.SelectMany(
                method => method.GetParameters()),
            parameter =>
                parameter.ParameterType == typeof(string));

        var events = new[]
        {
            AccountEmailChangeAnalytics.Started(),
            AccountEmailChangeAnalytics.CodeResent(),
            AccountEmailChangeAnalytics.Verified(),
            AccountEmailChangeAnalytics.Failed(
                AccountEmailChangeFailureReason.Invalid),
            AccountEmailChangeAnalytics.Failed(
                AccountEmailChangeFailureReason.Expired),
            AccountEmailChangeAnalytics.Failed(
                AccountEmailChangeFailureReason.Locked),
            AccountEmailChangeAnalytics.Failed(
                AccountEmailChangeFailureReason.Network),
            AccountEmailChangeAnalytics.Failed(
                AccountEmailChangeFailureReason.Sender)
        };

        Assert.Equal(
            [
                "account_email_change_started",
                "account_email_change_code_resent",
                "account_email_change_verified",
                "account_email_change_failed",
                "account_email_change_failed",
                "account_email_change_failed",
                "account_email_change_failed",
                "account_email_change_failed"
            ],
            events.Select(value => value.Name));
        Assert.All(
            events.Take(3),
            value => Assert.Empty(value.Properties));
        Assert.Equal(
            [
                "invalid",
                "expired",
                "locked",
                "network",
                "sender"
            ],
            events.Skip(3)
                .Select(value =>
                    value.Properties["reason"]));
        Assert.All(
            events.SelectMany(value => value.Properties),
            property =>
            {
                Assert.DoesNotContain(
                    "@",
                    property.Value);
                Assert.DoesNotContain(
                    "123456",
                    property.Value);
                Assert.DoesNotContain(
                    "exception",
                    property.Value);
                Assert.DoesNotContain(
                    "phone",
                    property.Key);
                Assert.DoesNotContain(
                    "email",
                    property.Key);
                Assert.DoesNotContain(
                    "code",
                    property.Key);
            });
    }
}
