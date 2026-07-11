using System.Reflection;
using Svac.DomainCore.Contracts.Purge;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// SLICE_S3_CONTRACT.md §0 Phase-1 DO-NOT list: "NO IdentityDbContext, NO EF entities, NO migration, NO
/// schema `identity`, NO DDL". Mirrors backend/tests/Svac.Tests.AimlRouter/ModuleSurfaceTests.cs's own
/// red-fixture-provable absence proof — verified rather than assumed, so a Phase-2 change landing an EF
/// DbContext or a PurgeRegistration attribute ahead of the real DDL work is caught immediately.
/// </summary>
public sealed class ModuleSurfaceTests
{
    private static readonly Assembly[] ModuleAssemblies =
    {
        typeof(Svac.Identity.Contracts.IAccountLifecycle).Assembly, // Svac.Identity.Contracts
        typeof(Svac.Identity.DependencyInjection.IdentityServiceCollectionExtensions).Assembly, // Svac.Identity
    };

    [Fact]
    public void NoModuleAssembly_DeclaresAnEfDbContext()
    {
        // Checked by base-type FULL-NAME WALK, deliberately without an EF Core package reference in this
        // test project — see Svac.Tests.Identity.csproj's own comment for why.
        var offenders = ModuleAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(IsEfDbContextSubclass)
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

    [Fact]
    public void NoModuleType_CarriesAPurgeRegistrationAttribute()
    {
        var offenders = ModuleAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.GetCustomAttributes<PurgeRegistrationAttribute>().Any())
            .Select(t => t.FullName)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Svac_Identity_Assembly_DoesNotReferenceEntityFrameworkCore()
    {
        var referencesEf = typeof(Svac.Identity.DependencyInjection.IdentityServiceCollectionExtensions).Assembly
            .GetReferencedAssemblies()
            .Any(r => r.Name?.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) == true);

        Assert.False(referencesEf, "Svac.Identity must not reference EF Core at Phase 1 (SLICE_S3_CONTRACT.md §0: no IdentityDbContext yet).");
    }
}
