using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Toklong.Api.Security;
using Toklong.Application.Features.Authentication;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Api.Tests.Security;

public sealed class MobileSessionTokenServiceTests
{
    [Fact]
    public async Task Refresh_token_is_hashed_rotated_and_replay_is_rejected()
    {
        await using var db = CreateDatabase();
        var repository = new MobileSessionRepository(db);
        var service = new MobileSessionTokenService(
            new EphemeralDataProtectionProvider(),
            TimeProvider.System,
            repository,
            db);
        var buyerId = Guid.NewGuid();

        var issued = await service.CreateAsync(
            new MobileSessionProfile(
                buyerId,
                null,
                "+66812345678",
                "ผู้ซื้อ ทดสอบ"),
            default);
        var stored = Assert.Single(db.MobileSessions);

        Assert.NotEqual(issued.RefreshToken, stored.RefreshTokenHash);
        Assert.Equal(64, stored.RefreshTokenHash.Length);
        Assert.NotNull(await service.ValidateAccessAsync(
            issued.AccessToken,
            default));

        var rotated = await service.RefreshAsync(
            issued.RefreshToken,
            default);

        Assert.NotNull(rotated);
        Assert.NotEqual(issued.RefreshToken, rotated.RefreshToken);
        Assert.Null(await service.RefreshAsync(
            issued.RefreshToken,
            default));
        Assert.NotNull(await service.ValidateAccessAsync(
            rotated.AccessToken,
            default));
    }

    [Fact]
    public async Task Logout_revokes_access_token_immediately()
    {
        await using var db = CreateDatabase();
        var repository = new MobileSessionRepository(db);
        var service = new MobileSessionTokenService(
            new EphemeralDataProtectionProvider(),
            TimeProvider.System,
            repository,
            db);
        var issued = await service.CreateAsync(
            new MobileSessionProfile(
                Guid.NewGuid(),
                null,
                "+66812345678",
                "ผู้ซื้อ ทดสอบ"),
            default);
        var session = Assert.Single(db.MobileSessions);

        await service.RevokeAsync(session.Id, default);

        Assert.Null(await service.ValidateAccessAsync(
            issued.AccessToken,
            default));
        Assert.Null(await service.RefreshAsync(
            issued.RefreshToken,
            default));
    }

    private static ToklongDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ToklongDbContext(options);
    }
}
