using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Toklong.Crm.Accounts;
using Toklong.Crm.Authentication;
using Toklong.Crm.Persistence;

namespace Toklong.Crm.Tests.Accounts;

public sealed class CrmAccountGovernanceTests
{
    [Fact]
    public async Task Super_admin_grant_requires_a_second_super_admin()
    {
        await using var database = Database();
        await database.Database.EnsureCreatedAsync();
        var first = AddUser(database, true);
        var second = AddUser(database, true);
        var target = AddUser(database, false);
        await database.SaveChangesAsync();
        var operations = new CrmAccountOperations(
            database,
            TimeProvider.System);

        await operations.RequestSuperAdminAsync(
            target.Id,
            Principal(first.Id),
            CancellationToken.None);
        var request = await database.RoleChangeRequests
            .SingleAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => operations.ApproveSuperAdminAsync(
                request.Id,
                Principal(first.Id),
                CancellationToken.None));

        await operations.ApproveSuperAdminAsync(
            request.Id,
            Principal(second.Id),
            CancellationToken.None);

        Assert.True(await database.UserRoles.AnyAsync(
            item =>
                item.UserId == target.Id &&
                item.RoleId == CrmRoleIds.SuperAdmin));
    }

    [Fact]
    public async Task Disabling_an_admin_revokes_every_session()
    {
        await using var database = Database();
        await database.Database.EnsureCreatedAsync();
        var first = AddUser(database, true);
        _ = AddUser(database, true);
        var target = AddUser(database, false);
        database.Sessions.Add(
            CrmSession.Create(
                target.Id,
                new string('a', 64),
                [1, 2, 3],
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1)));
        await database.SaveChangesAsync();
        var operations = new CrmAccountOperations(
            database,
            TimeProvider.System);

        await operations.DisableAsync(
            target.Id,
            Principal(first.Id),
            CancellationToken.None);

        Assert.False(target.IsActive);
        Assert.All(
            database.Sessions.Where(
                item => item.UserId == target.Id),
            item => Assert.NotNull(item.RevokedAt));

        await operations.ReactivateAsync(
            target.Id,
            Principal(first.Id),
            CancellationToken.None);

        Assert.True(target.IsActive);
        Assert.All(
            database.Sessions.Where(
                item => item.UserId == target.Id),
            item => Assert.NotNull(item.RevokedAt));
    }

    private static CrmUser AddUser(
        CrmDbContext database,
        bool superAdmin)
    {
        var user = CrmUser.Create(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            $"{Guid.NewGuid():N}@example.com",
            "ผู้ดูแลทดสอบ",
            null,
            DateTimeOffset.UtcNow);
        database.Users.Add(user);
        database.UserRoles.Add(
            CrmUserRole.Assign(
                user.Id,
                CrmRoleIds.Admin,
                null,
                DateTimeOffset.UtcNow));
        if (superAdmin)
            database.UserRoles.Add(
                CrmUserRole.Assign(
                    user.Id,
                    CrmRoleIds.SuperAdmin,
                    null,
                    DateTimeOffset.UtcNow));
        return user;
    }

    private static ClaimsPrincipal Principal(Guid userId) =>
        new(new ClaimsIdentity(
            [
                new Claim(
                    CrmAuthenticationDefaults.UserIdClaim,
                    userId.ToString("N"))
            ],
            "test"));

    private static CrmDbContext Database()
    {
        var options =
            new DbContextOptionsBuilder<CrmDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
        return new CrmDbContext(options);
    }
}
