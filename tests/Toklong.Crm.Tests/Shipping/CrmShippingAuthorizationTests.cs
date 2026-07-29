using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Toklong.Crm.Authentication;
using Toklong.Crm.Persistence;
using Toklong.Crm.Shipping;

namespace Toklong.Crm.Tests.Shipping;

public sealed class CrmShippingAuthorizationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Admin_without_step_up_role_cannot_resolve_shipping()
    {
        await using var database = Database();
        var actor = await ActorAsync(
            database,
            CrmRoleIds.Admin);
        var operations = new CrmShippingOperations(
            database,
            null!,
            null!,
            new FixedTimeProvider());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => operations.ResolveAdjustmentAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "absorbed-by-platform",
                Principal(actor.Id, Now),
                default));
    }

    [Fact]
    public async Task Stale_super_admin_session_cannot_authorize_retry()
    {
        await using var database = Database();
        var actor = await ActorAsync(
            database,
            CrmRoleIds.SuperAdmin);
        var operations = new CrmShippingOperations(
            database,
            null!,
            null!,
            new FixedTimeProvider());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => operations.AuthorizeShippingOperationRetryAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "ตรวจผลเดิมแล้ว",
                "SHIPPOP-LOOKUP-001",
                Principal(
                    actor.Id,
                    Now.AddMinutes(-31)),
                default));
    }

    private static async Task<CrmUser> ActorAsync(
        CrmDbContext database,
        Guid roleId)
    {
        await database.Database.EnsureCreatedAsync();
        var actor = CrmUser.Create(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            "shipping-reviewer@example.com",
            "Shipping Reviewer",
            null,
            Now);
        database.Users.Add(actor);
        database.UserRoles.Add(
            CrmUserRole.Assign(
                actor.Id,
                roleId,
                null,
                Now));
        await database.SaveChangesAsync();
        return actor;
    }

    private static ClaimsPrincipal Principal(
        Guid actorId,
        DateTimeOffset authenticatedAt) =>
        new(
            new ClaimsIdentity(
            [
                new Claim(
                    CrmAuthenticationDefaults.UserIdClaim,
                    actorId.ToString()),
                new Claim(
                    CrmAuthenticationDefaults.AuthenticatedAtClaim,
                    authenticatedAt.ToUnixTimeSeconds().ToString())
            ],
            "test"));

    private static CrmDbContext Database() =>
        new(
            new DbContextOptionsBuilder<CrmDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString("N"))
                .Options);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
