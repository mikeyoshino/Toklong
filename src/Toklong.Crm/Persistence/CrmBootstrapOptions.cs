namespace Toklong.Crm.Persistence;

public sealed class CrmBootstrapOptions
{
    public const string SectionName = "CrmBootstrap";

    public string EntraTenantId { get; init; } = "";
    public string EntraObjectId { get; init; } = "";
    public string Email { get; init; } = "";
    public string DisplayName { get; init; } = "";

    public static CrmBootstrapOptions From(
        IConfiguration configuration) =>
        configuration
            .GetSection(SectionName)
            .Get<CrmBootstrapOptions>() ??
        new CrmBootstrapOptions();
}
