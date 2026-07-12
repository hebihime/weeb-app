using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Svac.Identity.Persistence;

/// <summary>
/// Design-time-only factory so `dotnet ef migrations add` / `dotnet ef database update` can construct an
/// IdentityDbContext without a running host's DI container (mirrors
/// Svac.DomainCore.Persistence.CoreDbContextFactory exactly). Never used at runtime — the real host wires
/// IdentityDbContext through AddIdentityModule with its own configured connection string.
/// </summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SVAC_DESIGNTIME_CONNECTION_STRING")
            ?? "Host=localhost;Port=5433;Database=svac;Username=svac;Password=svac_dev_only";

        var optionsBuilder = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString);
        return new IdentityDbContext(optionsBuilder.Options);
    }
}
