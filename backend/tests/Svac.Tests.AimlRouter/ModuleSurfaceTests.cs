using System.Reflection;
using Svac.DomainCore.Contracts.Purge;
using Xunit;

namespace Svac.Tests.AimlRouter;

/// <summary>
/// SLICE_S2_CONTRACT.md §2/§6: "No AimlDbContext exists" and "Zero new [13A] registrations — a design
/// theorem, not an omission." N/A-per-contract, verified rather than assumed: a red fixture (an
/// AimlRouter-owned DbContext or PurgeRegistration attribute) would fail these tests.
/// </summary>
public sealed class ModuleSurfaceTests
{
    private static readonly Assembly[] ModuleAssemblies =
    {
        typeof(Svac.AimlRouter.Contracts.IAimlRouter).Assembly,       // Svac.AimlRouter.Contracts
        typeof(Svac.AimlRouter.AimlRouterService).Assembly,           // Svac.AimlRouter
    };

    [Fact]
    public void NoModuleAssembly_DeclaresAnEfDbContext()
    {
        // Checked by base-type FULL-NAME WALK, deliberately WITHOUT a Microsoft.EntityFrameworkCore
        // package reference in this test project: referencing that package here would make THIS
        // project's own .csproj match ef-gate.sh's `grep -l 'Microsoft.EntityFrameworkCore'` discovery
        // (build/scripts/ef-gate.sh's FOUND_PROJECTS), which then tries `dotnet ef migrations
        // has-pending-model-changes` against a project with no DbContext and misreports it as model
        // drift — a real false-positive hit while building this test, fixed by never taking the
        // dependency instead of teaching the gate script about test-only exceptions.
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
    public void Svac_AimlRouter_Assembly_DoesNotReferenceEntityFrameworkCore()
    {
        // Belt-and-suspenders on §2's "no tables, no migration, no EF entity, no DbContext": the
        // assembly should not even carry the reference, not merely lack a DbContext subclass today.
        var referencesEf = typeof(Svac.AimlRouter.AimlRouterService).Assembly.GetReferencedAssemblies()
            .Any(r => r.Name?.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) == true);

        Assert.False(referencesEf, "Svac.AimlRouter must never reference EF Core (SLICE_S2_CONTRACT.md §2: the router persists nothing).");
    }
}
