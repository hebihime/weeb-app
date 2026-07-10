using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Svac.DomainCore.Persistence;

/// <summary>
/// Design-time-only factory so `dotnet ef migrations add` / `dotnet ef database update` can construct a
/// CoreDbContext without a running host's DI container. The connection string here is never used at
/// runtime — every real host wires CoreDbContext through AddDomainCore with its own configured
/// connection string; this factory only exists to satisfy EF's tooling contract.
/// </summary>
public sealed class CoreDbContextFactory : IDesignTimeDbContextFactory<CoreDbContext>
{
    public CoreDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SVAC_DESIGNTIME_CONNECTION_STRING")
            ?? "Host=localhost;Port=5433;Database=svac;Username=svac;Password=svac_dev_only";

        var optionsBuilder = new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(connectionString);
        return new CoreDbContext(optionsBuilder.Options);
    }
}
