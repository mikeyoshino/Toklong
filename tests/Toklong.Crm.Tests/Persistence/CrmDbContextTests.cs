using Microsoft.EntityFrameworkCore;
using Toklong.Crm.Authentication;
using Toklong.Crm.Persistence;

namespace Toklong.Crm.Tests.Persistence;

public sealed class CrmDbContextTests
{
    [Fact]
    public void Every_crm_entity_maps_to_crm_schema()
    {
        using var database = Database();

        Assert.All(
            database.Model.GetEntityTypes(),
            entity => Assert.Equal(
                "crm",
                entity.GetSchema()));
    }

    [Fact]
    public async Task Initial_roles_are_seeded()
    {
        await using var database = Database();
        await database.Database.EnsureCreatedAsync();

        var roles = await database.Roles
            .OrderBy(role => role.Name)
            .Select(role => role.Name)
            .ToArrayAsync();

        Assert.Equal(
            [CrmRoles.Admin, CrmRoles.SuperAdmin],
            roles);
    }

    [Fact]
    public async Task Authentication_events_are_append_only()
    {
        await using var database = Database();
        var authEvent = CrmAuthEvent.Create(
            null,
            "sign_in.denied_unknown_user",
            new string('a', 64),
            "test-correlation",
            DateTimeOffset.UtcNow);
        database.AuthEvents.Add(authEvent);
        await database.SaveChangesAsync();

        database.Entry(authEvent).State =
            EntityState.Modified;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => database.SaveChangesAsync());
    }

    private static CrmDbContext Database()
    {
        var options =
            new DbContextOptionsBuilder<CrmDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString("N"))
                .Options;
        return new CrmDbContext(options);
    }
}
