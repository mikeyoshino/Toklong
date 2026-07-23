using Microsoft.EntityFrameworkCore;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Web.Extensions;

public static class DatabaseMigrationExtensions
{
    public static async Task ApplyDatabaseMigrationsAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ToklongDbContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("DatabaseMigration");

        var pendingMigrations = (await database.Database
            .GetPendingMigrationsAsync(cancellationToken))
            .ToArray();

        if (pendingMigrations.Length == 0)
        {
            logger.LogInformation("PostgreSQL schema is already up to date");
            return;
        }

        logger.LogInformation(
            "Applying {Count} EF Core migration(s): {Migrations}",
            pendingMigrations.Length,
            string.Join(", ", pendingMigrations));

        await database.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("EF Core migrations applied successfully");
    }
}
