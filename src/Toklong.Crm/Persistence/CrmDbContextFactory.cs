using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Toklong.Crm.Persistence;

public sealed class CrmDbContextFactory
    : IDesignTimeDbContextFactory<CrmDbContext>
{
    public CrmDbContext CreateDbContext(string[] args)
    {
        var options =
            new DbContextOptionsBuilder<CrmDbContext>()
                .UseNpgsql(
                    "Host=localhost;Port=5432;Database=toklong;Username=toklong;Password=toklong_dev",
                    npgsql => npgsql.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        "crm"))
                .Options;
        return new CrmDbContext(options);
    }
}
