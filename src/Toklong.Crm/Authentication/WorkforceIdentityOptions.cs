namespace Toklong.Crm.Authentication;

public sealed class WorkforceIdentityOptions
{
    public const string SectionName = "WorkforceIdentity";

    public bool Enabled { get; init; }
    public string TenantId { get; init; } = "";
    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string CallbackPath { get; init; } = "/signin-oidc";
    public bool FinancialActionsEnabled { get; init; }
    public bool ConditionalAccessApproved { get; init; }

    public bool IsConfigured =>
        Enabled &&
        Guid.TryParse(TenantId, out _) &&
        Guid.TryParse(ClientId, out _) &&
        !string.IsNullOrWhiteSpace(ClientSecret) &&
        CallbackPath.StartsWith(
            "/",
            StringComparison.Ordinal);

    public static WorkforceIdentityOptions From(
        IConfiguration configuration) =>
        configuration
            .GetSection(SectionName)
            .Get<WorkforceIdentityOptions>() ??
        new WorkforceIdentityOptions();

    public void Validate(IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && !IsConfigured)
            throw new InvalidOperationException(
                "Production CRM requires a configured single-tenant Entra OIDC application.");

        if (FinancialActionsEnabled &&
            !ConditionalAccessApproved)
            throw new InvalidOperationException(
                "CRM financial actions require the approved Entra P1 Conditional Access production gate.");
    }
}
