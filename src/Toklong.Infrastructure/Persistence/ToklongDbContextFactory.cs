using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Toklong.Infrastructure.Persistence;

public sealed class ToklongDbContextFactory : IDesignTimeDbContextFactory<ToklongDbContext>
{
    public ToklongDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=toklong;Username=toklong;Password=toklong_dev")
            .Options;
        return new ToklongDbContext(options);
    }
}
