using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Svac.AdminHost.Domain.Persistence;

/// <summary>
/// Design-time-only factory so `dotnet ef migrations add` / `dotnet ef database update` can construct an
/// AdminDbContext without a running host's DI container (mirrors Svac.DomainCore.Persistence.
/// CoreDbContextFactory / Svac.Identity.Persistence.IdentityDbContextFactory exactly). Never used at
/// runtime — the real host wires AdminDbContext through AddAdminHostModule with its own configured
/// connection string.
/// </summary>
public sealed class AdminDbContextFactory : IDesignTimeDbContextFactory<AdminDbContext>
{
    public AdminDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SVAC_DESIGNTIME_CONNECTION_STRING")
            ?? "Host=localhost;Port=5433;Database=svac;Username=svac;Password=svac_dev_only";

        var optionsBuilder = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(connectionString);
        return new AdminDbContext(optionsBuilder.Options);
    }
}
