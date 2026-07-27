using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Toklong.Crm.Authentication;

namespace Toklong.Crm.Tests.Authentication;

public sealed class WorkforceIdentityOptionsTests
{
    [Fact]
    public void Production_requires_configured_single_tenant_oidc()
    {
        var options = new WorkforceIdentityOptions();

        var exception = Assert.Throws<
            InvalidOperationException>(
            () => options.Validate(
                Environment("Production")));

        Assert.Contains(
            "single-tenant Entra",
            exception.Message);
    }

    [Fact]
    public void Free_read_only_configuration_passes_production_gate()
    {
        var options = Configured(
            financialActionsEnabled: false,
            conditionalAccessApproved: false);

        options.Validate(Environment("Production"));
    }

    [Fact]
    public void Financial_actions_require_conditional_access_approval()
    {
        var options = Configured(
            financialActionsEnabled: true,
            conditionalAccessApproved: false);

        var exception = Assert.Throws<
            InvalidOperationException>(
            () => options.Validate(
                Environment("Production")));

        Assert.Contains(
            "Conditional Access",
            exception.Message);
    }

    private static WorkforceIdentityOptions Configured(
        bool financialActionsEnabled,
        bool conditionalAccessApproved) =>
        new()
        {
            Enabled = true,
            TenantId =
                "b301f1d0-f83d-4279-9c0a-b4715b44622b",
            ClientId =
                "bb2bd652-1e8a-4c16-b252-dd8818ed5341",
            ClientSecret = "test-secret-not-production",
            FinancialActionsEnabled =
                financialActionsEnabled,
            ConditionalAccessApproved =
                conditionalAccessApproved
        };

    private static IHostEnvironment Environment(
        string name) =>
        new TestEnvironment
        {
            EnvironmentName = name
        };

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";
        public string ApplicationName { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
