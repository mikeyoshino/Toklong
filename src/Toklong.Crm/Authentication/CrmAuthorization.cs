using Microsoft.AspNetCore.Authorization;

namespace Toklong.Crm.Authentication;

public static class CrmAuthorization
{
    public static void Configure(
        AuthorizationOptions options)
    {
        options.FallbackPolicy =
            new AuthorizationPolicyBuilder(
                    CrmAuthenticationDefaults.CookieScheme)
                .RequireAuthenticatedUser()
                .Build();
        options.AddPolicy(
            CrmPolicies.DisputeReader,
            policy => policy.RequireClaim(
                CrmAuthenticationDefaults.RoleClaim,
                CrmRoles.Admin,
                CrmRoles.SuperAdmin));
        options.AddPolicy(
            CrmPolicies.DisputeReviewer,
            policy => policy.RequireClaim(
                CrmAuthenticationDefaults.RoleClaim,
                CrmRoles.Admin,
                CrmRoles.SuperAdmin));
        options.AddPolicy(
            CrmPolicies.DisputeResolver,
            policy => policy.RequireClaim(
                CrmAuthenticationDefaults.RoleClaim,
                CrmRoles.SuperAdmin));
        options.AddPolicy(
            CrmPolicies.CrmAccountAdministrator,
            policy => policy.RequireClaim(
                CrmAuthenticationDefaults.RoleClaim,
                CrmRoles.SuperAdmin));
    }
}
