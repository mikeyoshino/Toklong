using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Toklong.Crm.Authentication;
using Toklong.Crm.Persistence;

namespace Toklong.Crm.Accounts;

public sealed record CrmAccountListItem(
    Guid Id,
    string Email,
    string DisplayName,
    string EntraObjectId,
    CrmUserStatus Status,
    IReadOnlyList<string> Roles);

public sealed class CrmAccountOperations(
    CrmDbContext database,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<CrmAccountListItem>>
        ListAsync(
            ClaimsPrincipal principal,
            CancellationToken cancellationToken)
    {
        _ = await RequireSuperAdminAsync(
            principal,
            cancellationToken);
        var users = await database.Users
            .AsNoTracking()
            .OrderBy(item => item.DisplayName)
            .ToListAsync(cancellationToken);
        var roles = await (
                from userRole in database.UserRoles
                join role in database.Roles
                    on userRole.RoleId equals role.Id
                select new
                {
                    userRole.UserId,
                    role.Name
                })
            .ToListAsync(cancellationToken);
        return users.Select(user =>
            new CrmAccountListItem(
                user.Id,
                user.Email,
                user.DisplayName,
                user.EntraObjectId,
                user.Status,
                roles.Where(item => item.UserId == user.Id)
                    .Select(item => item.Name)
                    .Order()
                    .ToList()))
            .ToList();
    }

    public async Task<Guid> CreateAdminAsync(
        string tenantId,
        string objectId,
        string email,
        string displayName,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = await RequireSuperAdminAsync(
            principal,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        var user = CrmUser.Create(
            tenantId,
            objectId,
            email,
            displayName,
            actor.Id,
            now);
        database.Users.Add(user);
        database.UserRoles.Add(
            CrmUserRole.Assign(
                user.Id,
                CrmRoleIds.Admin,
                actor.Id,
                now));
        AddEvent(
            user.Id,
            actor.Id,
            "account.admin_created",
            new { user.Email, user.EntraObjectId });
        await database.SaveChangesAsync(cancellationToken);
        return user.Id;
    }

    public async Task RequestSuperAdminAsync(
        Guid targetUserId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = await RequireSuperAdminAsync(
            principal,
            cancellationToken);
        var target = await ActiveUserAsync(
            targetUserId,
            cancellationToken);
        if (await HasRoleAsync(
                target.Id,
                CrmRoleIds.SuperAdmin,
                cancellationToken))
            throw new InvalidOperationException(
                "บัญชีนี้เป็น Super Admin อยู่แล้ว");
        if (await database.RoleChangeRequests.AnyAsync(
                item =>
                    item.TargetUserId == target.Id &&
                    item.RoleId == CrmRoleIds.SuperAdmin &&
                    item.Status ==
                        CrmRoleChangeRequestStatus
                            .PendingApproval,
                cancellationToken))
            throw new InvalidOperationException(
                "มีคำขอเพิ่ม Super Admin ที่รออนุมัติอยู่แล้ว");
        var request =
            CrmRoleChangeRequest.CreateSuperAdminGrant(
                target.Id,
                actor.Id,
                timeProvider.GetUtcNow());
        database.RoleChangeRequests.Add(request);
        AddEvent(
            target.Id,
            actor.Id,
            "account.super_admin_requested",
            new { requestId = request.Id });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task ApproveSuperAdminAsync(
        Guid requestId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = await RequireSuperAdminAsync(
            principal,
            cancellationToken);
        var request = await database.RoleChangeRequests
            .SingleOrDefaultAsync(
                item => item.Id == requestId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "ไม่พบคำขอ");
        _ = await ActiveUserAsync(
            request.TargetUserId,
            cancellationToken);
        if (request.RequestedByUserId == actor.Id)
        {
            AddEvent(
                request.TargetUserId,
                actor.Id,
                "account.super_admin_self_approval_denied",
                new { requestId });
            await database.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException(
                "ผู้ขอเพิ่ม Super Admin ห้ามอนุมัติเอง");
        }
        request.Approve(actor.Id, timeProvider.GetUtcNow());
        if (!await HasRoleAsync(
                request.TargetUserId,
                CrmRoleIds.SuperAdmin,
                cancellationToken))
            database.UserRoles.Add(
                CrmUserRole.Assign(
                    request.TargetUserId,
                    CrmRoleIds.SuperAdmin,
                    actor.Id,
                    timeProvider.GetUtcNow()));
        AddEvent(
            request.TargetUserId,
            actor.Id,
            "account.super_admin_approved",
            new
            {
                requestId,
                requestedBy = request.RequestedByUserId
            });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task DisableAsync(
        Guid targetUserId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = await RequireSuperAdminAsync(
            principal,
            cancellationToken);
        if (actor.Id == targetUserId)
        {
            AddEvent(
                targetUserId,
                actor.Id,
                "account.self_disable_denied",
                new { });
            await database.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException(
                "ห้ามปิดบัญชีของตนเอง");
        }
        var target = await ActiveUserAsync(
            targetUserId,
            cancellationToken);
        if (await HasRoleAsync(
                target.Id,
                CrmRoleIds.SuperAdmin,
                cancellationToken))
        {
            var activeSuperAdmins = await (
                    from user in database.Users
                    join role in database.UserRoles
                        on user.Id equals role.UserId
                    where user.Status == CrmUserStatus.Active &&
                          role.RoleId == CrmRoleIds.SuperAdmin
                    select user.Id)
                .Distinct()
                .CountAsync(cancellationToken);
            if (activeSuperAdmins <= 2)
            {
                AddEvent(
                    target.Id,
                    actor.Id,
                    "account.disable_denied_minimum_super_admins",
                    new { activeSuperAdmins });
                await database.SaveChangesAsync(
                    cancellationToken);
                throw new InvalidOperationException(
                    "ต้องเหลือ Super Admin ที่ใช้งานได้อย่างน้อย 2 คน");
            }
        }

        var now = timeProvider.GetUtcNow();
        target.Disable(actor.Id, now);
        var sessions = await database.Sessions
            .Where(item =>
                item.UserId == target.Id &&
                item.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
            session.Revoke(now);
        AddEvent(
            target.Id,
            actor.Id,
            "account.disabled",
            new { revokedSessions = sessions.Count });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task ReactivateAsync(
        Guid targetUserId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = await RequireSuperAdminAsync(
            principal,
            cancellationToken);
        var target = await database.Users.SingleOrDefaultAsync(
            item => item.Id == targetUserId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "ไม่พบบัญชี");
        if (target.IsActive)
            return;
        target.Reactivate();
        AddEvent(
            target.Id,
            actor.Id,
            "account.reactivated",
            new { });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CrmRoleChangeRequest>>
        PendingRequestsAsync(
            ClaimsPrincipal principal,
            CancellationToken cancellationToken)
    {
        _ = await RequireSuperAdminAsync(
            principal,
            cancellationToken);
        return await database.RoleChangeRequests
            .AsNoTracking()
            .Where(item =>
                item.Status ==
                CrmRoleChangeRequestStatus.PendingApproval)
            .OrderBy(item => item.RequestedAt)
            .ToListAsync(cancellationToken);
    }

    private async Task<CrmUser> RequireSuperAdminAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var rawId = principal.FindFirstValue(
            CrmAuthenticationDefaults.UserIdClaim);
        if (!Guid.TryParse(rawId, out var userId))
            throw new UnauthorizedAccessException();
        var user = await ActiveUserAsync(
            userId,
            cancellationToken);
        if (!await HasRoleAsync(
                user.Id,
                CrmRoleIds.SuperAdmin,
                cancellationToken))
            throw new UnauthorizedAccessException(
                "ต้องใช้สิทธิ์ Super Admin");
        return user;
    }

    private async Task<CrmUser> ActiveUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await database.Users.SingleOrDefaultAsync(
            item => item.Id == userId,
            cancellationToken);
        return user is { IsActive: true }
            ? user
            : throw new InvalidOperationException(
                "ไม่พบบัญชีที่ใช้งานได้");
    }

    private Task<bool> HasRoleAsync(
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken) =>
        database.UserRoles.AnyAsync(
            item =>
                item.UserId == userId &&
                item.RoleId == roleId,
            cancellationToken);

    private void AddEvent(
        Guid targetUserId,
        Guid actorUserId,
        string name,
        object metadata) =>
        database.AccountEvents.Add(
            CrmAccountEvent.Create(
                targetUserId,
                actorUserId,
                name,
                JsonSerializer.Serialize(metadata),
                timeProvider.GetUtcNow()));
}
