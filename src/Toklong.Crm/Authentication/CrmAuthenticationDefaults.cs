namespace Toklong.Crm.Authentication;

public static class CrmAuthenticationDefaults
{
    public const string CookieScheme = "CrmCookie";
    public const string OpenIdConnectScheme = "CrmEntra";
    public const string UserIdClaim = "toklong_crm_user_id";
    public const string RoleClaim = "toklong_crm_role";
    public const string AuthenticatedAtClaim =
        "toklong_crm_authenticated_at";
}

public static class CrmRoles
{
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";
}

public static class CrmPolicies
{
    public const string DisputeReader = "DisputeReader";
    public const string DisputeReviewer = "DisputeReviewer";
    public const string DisputeResolver = "DisputeResolver";
    public const string CrmAccountAdministrator =
        "CrmAccountAdministrator";
}
