using Microsoft.EntityFrameworkCore;
using Toklong.Crm.Authentication;

namespace Toklong.Crm.Persistence;

public static class CrmBootstrapper
{
    public static async Task BootstrapInitialSuperAdminAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<CrmDbContext>();
        if (await database.Users.AnyAsync(cancellationToken))
            throw new InvalidOperationException(
                "Initial CRM bootstrap is allowed only before any CRM user exists.");

        var options = CrmBootstrapOptions.From(configuration);
        var now = scope.ServiceProvider
            .GetRequiredService<TimeProvider>()
            .GetUtcNow();
        var user = CrmUser.Create(
            options.EntraTenantId,
            options.EntraObjectId,
            options.Email,
            options.DisplayName,
            null,
            now);
        database.Users.Add(user);
        database.UserRoles.Add(
            CrmUserRole.Assign(
                user.Id,
                CrmRoleIds.SuperAdmin,
                null,
                now));
        database.AuthEvents.Add(
            CrmAuthEvent.Create(
                user.Id,
                "account.bootstrap_super_admin",
                CrmSubjectReference.Hash(
                    options.EntraTenantId,
                    options.EntraObjectId),
                $"crm-bootstrap:{user.Id:N}",
                now));
        await database.SaveChangesAsync(cancellationToken);
    }

    public static async Task BootstrapSecondSuperAdminAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<CrmDbContext>();
        var activeSuperAdminCount = await (
                from activeUser in database.Users
                join role in database.UserRoles
                    on activeUser.Id equals role.UserId
                where activeUser.Status == CrmUserStatus.Active &&
                      role.RoleId == CrmRoleIds.SuperAdmin
                select activeUser.Id)
            .Distinct()
            .CountAsync(cancellationToken);
        if (activeSuperAdminCount != 1)
            throw new InvalidOperationException(
                "Second SuperAdmin ceremony requires exactly one active SuperAdmin.");

        var options = CrmBootstrapOptions.From(configuration);
        if (!Guid.TryParse(
                options.EntraTenantId,
                out var tenantId) ||
            !Guid.TryParse(
                options.EntraObjectId,
                out var objectId))
            throw new InvalidOperationException(
                "CrmBootstrap Entra identifiers are invalid.");
        var normalizedTenantId = tenantId.ToString("D");
        var normalizedObjectId = objectId.ToString("D");
        var user = await database.Users.SingleOrDefaultAsync(
            item =>
                item.EntraTenantId == normalizedTenantId &&
                item.EntraObjectId == normalizedObjectId,
            cancellationToken);
        if (user is null || !user.IsActive)
            throw new InvalidOperationException(
                "Second SuperAdmin must already be an active CRM Admin.");
        if (await database.UserRoles.AnyAsync(
                item =>
                    item.UserId == user.Id &&
                    item.RoleId == CrmRoleIds.SuperAdmin,
                cancellationToken))
            throw new InvalidOperationException(
                "Account is already a SuperAdmin.");

        var now = scope.ServiceProvider
            .GetRequiredService<TimeProvider>()
            .GetUtcNow();
        database.UserRoles.Add(
            CrmUserRole.Assign(
                user.Id,
                CrmRoleIds.SuperAdmin,
                null,
                now));
        database.AuthEvents.Add(
            CrmAuthEvent.Create(
                user.Id,
                "account.bootstrap_second_super_admin",
                CrmSubjectReference.Hash(
                    normalizedTenantId,
                    normalizedObjectId),
                $"crm-bootstrap-second:{user.Id:N}",
                now));
        await database.SaveChangesAsync(cancellationToken);
    }
}
