using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Toklong.Crm.Persistence;

namespace Toklong.Crm.Authentication;

public sealed class CrmOpenIdConnectEvents(
    IDbContextFactory<CrmDbContext> databaseFactory,
    WorkforceIdentityOptions workforce,
    TimeProvider timeProvider)
    : OpenIdConnectEvents
{
    public override async Task TokenValidated(
        TokenValidatedContext context)
    {
        var tenantId =
            context.Principal?.FindFirstValue("tid")?.Trim();
        var objectId =
            context.Principal?.FindFirstValue("oid")?.Trim();
        var correlationId =
            context.HttpContext.TraceIdentifier;

        if (!Guid.TryParse(tenantId, out var parsedTenantId) ||
            !Guid.TryParse(objectId, out var parsedObjectId) ||
            !string.Equals(
                parsedTenantId.ToString("D"),
                Guid.Parse(workforce.TenantId).ToString("D"),
                StringComparison.OrdinalIgnoreCase))
        {
            context.Fail(
                "บัญชีนี้ไม่ได้อยู่ใน workforce tenant ที่อนุญาต");
            return;
        }

        var normalizedTenantId =
            parsedTenantId.ToString("D");
        var normalizedObjectId =
            parsedObjectId.ToString("D");
        var subjectHash = CrmSubjectReference.Hash(
            normalizedTenantId,
            normalizedObjectId);
        var now = timeProvider.GetUtcNow();

        await using var database =
            await databaseFactory.CreateDbContextAsync(
                context.HttpContext.RequestAborted);
        var user = await database.Users
            .SingleOrDefaultAsync(
                item =>
                    item.EntraTenantId == normalizedTenantId &&
                    item.EntraObjectId == normalizedObjectId,
                context.HttpContext.RequestAborted);

        if (user is null || !user.IsActive)
        {
            database.AuthEvents.Add(
                CrmAuthEvent.Create(
                    user?.Id,
                    user is null
                        ? "sign_in.denied_unknown_user"
                        : "sign_in.denied_disabled_user",
                    subjectHash,
                    correlationId,
                    now));
            await database.SaveChangesAsync(
                context.HttpContext.RequestAborted);
            context.Fail("บัญชีนี้ยังไม่ได้รับสิทธิ์เข้า TOKLONG CRM");
            return;
        }

        var roles = await (
                from userRole in database.UserRoles
                join role in database.Roles
                    on userRole.RoleId equals role.Id
                where userRole.UserId == user.Id
                select role.Name)
            .ToArrayAsync(
                context.HttpContext.RequestAborted);
        if (roles.Length == 0)
        {
            database.AuthEvents.Add(
                CrmAuthEvent.Create(
                    user.Id,
                    "sign_in.denied_no_role",
                    subjectHash,
                    correlationId,
                    now));
            await database.SaveChangesAsync(
                context.HttpContext.RequestAborted);
            context.Fail(
                "บัญชีนี้ไม่มีบทบาท TOKLONG CRM ที่ใช้งานได้");
            return;
        }

        if (context.Principal?.Identity is not ClaimsIdentity identity)
        {
            context.Fail("ไม่สามารถสร้าง CRM identity ได้");
            return;
        }

        identity.AddClaim(
            new Claim(
                CrmAuthenticationDefaults.UserIdClaim,
                user.Id.ToString("N")));
        identity.AddClaim(
            new Claim(
                CrmAuthenticationDefaults.AuthenticatedAtClaim,
                now.ToUnixTimeSeconds().ToString(
                    System.Globalization.CultureInfo
                        .InvariantCulture)));
        foreach (var role in roles)
            identity.AddClaim(
                new Claim(
                    CrmAuthenticationDefaults.RoleClaim,
                    role));

        database.AuthEvents.Add(
            CrmAuthEvent.Create(
                user.Id,
                "sign_in.succeeded",
                subjectHash,
                correlationId,
                now));
        await database.SaveChangesAsync(
            context.HttpContext.RequestAborted);
    }

    public override Task RemoteFailure(
        RemoteFailureContext context)
    {
        context.HandleResponse();
        context.Response.Redirect(
            "/access-denied?reason=authentication");
        return Task.CompletedTask;
    }
}
