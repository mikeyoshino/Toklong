using System.Reflection;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class AccountNameChangeAnalyticsTests
{
    [Fact]
    public void Factories_accept_only_bounded_values_and_emit_no_personal_data()
    {
        var factories = typeof(AccountNameChangeAnalytics).GetMethods(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.DoesNotContain(
            factories.SelectMany(factory => factory.GetParameters()),
            parameter => parameter.ParameterType == typeof(string));

        var events = new[]
        {
            AccountNameChangeAnalytics.Opened(),
            AccountNameChangeAnalytics.Started(),
            AccountNameChangeAnalytics.CodeResent(),
            AccountNameChangeAnalytics.Verified(),
            AccountNameChangeAnalytics.Blocked(AccountNameChangeBlockReason.Cooldown),
            AccountNameChangeAnalytics.Failed(AccountNameChangeFailureReason.Cooldown),
            AccountNameChangeAnalytics.Failed(AccountNameChangeFailureReason.Unchanged),
            AccountNameChangeAnalytics.Failed(AccountNameChangeFailureReason.Invalid),
            AccountNameChangeAnalytics.Failed(AccountNameChangeFailureReason.SendLimit),
            AccountNameChangeAnalytics.Failed(AccountNameChangeFailureReason.Expired),
            AccountNameChangeAnalytics.Failed(AccountNameChangeFailureReason.Locked),
            AccountNameChangeAnalytics.Failed(AccountNameChangeFailureReason.Network),
            AccountNameChangeAnalytics.Failed(AccountNameChangeFailureReason.Provider)
        };

        Assert.Equal(
            [
                "account_name_change_opened",
                "account_name_change_started",
                "account_name_change_code_resent",
                "account_name_change_verified",
                "account_name_change_blocked",
                "account_name_change_failed",
                "account_name_change_failed",
                "account_name_change_failed",
                "account_name_change_failed",
                "account_name_change_failed",
                "account_name_change_failed",
                "account_name_change_failed",
                "account_name_change_failed"
            ],
            events.Select(value => value.Name));
        Assert.Equal("cooldown", events[4].Properties["reason"]);
        Assert.Equal(
            ["cooldown", "unchanged", "invalid", "send_limit", "expired", "locked", "network", "provider"],
            events.Skip(5).Select(value => value.Properties["reason"]));
        Assert.All(events.SelectMany(value => value.Properties), property =>
        {
            Assert.DoesNotContain("name", property.Key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("phone", property.Key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("code", property.Key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("id", property.Key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("message", property.Key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ชื่อ", property.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("081", property.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("123456", property.Value, StringComparison.Ordinal);
        });
    }
}
