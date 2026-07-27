using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Toklong.Crm.Authentication;

namespace Toklong.Crm.Tests.Authentication;

public sealed class CrmAuthorizationTests
{
    [Fact]
    public async Task Admin_can_review_but_cannot_resolve_or_manage_accounts()
    {
        await using var services = BuildServices();
        var authorization = services
            .GetRequiredService<IAuthorizationService>();
        var principal = Principal(CrmRoles.Admin);

        Assert.True((await authorization.AuthorizeAsync(
            principal,
            null,
            CrmPolicies.DisputeReviewer)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(
            principal,
            null,
            CrmPolicies.DisputeResolver)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(
            principal,
            null,
            CrmPolicies.CrmAccountAdministrator)).Succeeded);
    }

    [Fact]
    public async Task SuperAdmin_can_resolve_and_manage_accounts()
    {
        await using var services = BuildServices();
        var authorization = services
            .GetRequiredService<IAuthorizationService>();
        var principal = Principal(CrmRoles.SuperAdmin);

        Assert.True((await authorization.AuthorizeAsync(
            principal,
            null,
            CrmPolicies.DisputeResolver)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(
            principal,
            null,
            CrmPolicies.CrmAccountAdministrator)).Succeeded);
    }

    [Fact]
    public async Task Consumer_role_does_not_satisfy_any_crm_policy()
    {
        await using var services = BuildServices();
        var authorization = services
            .GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Role, "Buyer")
            ], "consumer"));

        Assert.False((await authorization.AuthorizeAsync(
            principal,
            null,
            CrmPolicies.DisputeReader)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(
            principal,
            null,
            CrmPolicies.DisputeResolver)).Succeeded);
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(
            CrmAuthorization.Configure);
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal Principal(string role) =>
        new(
            new ClaimsIdentity(
            [
                new Claim(
                    CrmAuthenticationDefaults.RoleClaim,
                    role)
            ], CrmAuthenticationDefaults.CookieScheme));
}
