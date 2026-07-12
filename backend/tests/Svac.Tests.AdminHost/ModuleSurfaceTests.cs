using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts.Purge;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// SLICE_S5_CONTRACT.md §0/§1c: "Zero parallel audit store — proven, not claimed": AdminDbContext's
/// model enumerates to EXACTLY the two schema-`admin` entities — no `*audit*`/`*session*`/`*log*`/
/// `*search*` table exists. Mirrors Svac.Tests.Identity/Svac.Tests.AimlRouter's ModuleSurfaceTests.cs
/// shape exactly.
/// </summary>
public sealed class ModuleSurfaceTests
{
    private static readonly Assembly DomainAssembly = typeof(Svac.AdminHost.Domain.Persistence.AdminDbContext).Assembly;

    [Fact]
    public void ExactlyOneDbContext_ExistsInTheDomainAssembly_AndItIsAdminDbContext()
    {
        var dbContexts = DomainAssembly.GetTypes().Where(IsEfDbContextSubclass).Select(t => t.FullName).ToArray();
        var single = Assert.Single(dbContexts);
        Assert.Equal("Svac.AdminHost.Domain.Persistence.AdminDbContext", single);
    }

    [Fact]
    public void AdminDbContext_MapsExactlyTwoEntityTypes_NoParallelAuditOrSessionOrSearchStore()
    {
        using var db = new Svac.AdminHost.Domain.Persistence.AdminDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Svac.AdminHost.Domain.Persistence.AdminDbContext>()
                .UseNpgsql("Host=localhost;Database=svac-model-enumeration-only")
                .Options);

        var tableNames = db.Model.GetEntityTypes().Select(t => t.GetTableName()).Where(n => n is not null).Select(n => n!).ToArray();

        Assert.Equal(2, tableNames.Length);
        Assert.Contains("staff_accounts", tableNames);
        Assert.Contains("staff_role_grants", tableNames);

        var forbiddenPatterns = new[] { "audit", "session", "log", "search" };
        foreach (var table in tableNames)
        {
            foreach (var pattern in forbiddenPatterns)
            {
                Assert.DoesNotContain(pattern, table, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void NoAdminType_CarriesTheLegacyPurgeRegistrationAttribute()
    {
        // The admin host registers its 13A slice via IPurgeRegistrySource (AdminPurgeRegistrySource),
        // never the legacy attribute-based mechanism — mirrors Svac.Tests.Identity's own absence proof.
        var offenders = DomainAssembly.GetTypes()
            .Where(t => t.GetCustomAttributes<PurgeRegistrationAttribute>().Any())
            .Select(t => t.FullName)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static bool IsEfDbContextSubclass(Type type)
    {
        for (var cur = type.BaseType; cur is not null; cur = cur.BaseType)
        {
            if (cur.FullName == "Microsoft.EntityFrameworkCore.DbContext")
            {
                return true;
            }
        }
        return false;
    }
}
