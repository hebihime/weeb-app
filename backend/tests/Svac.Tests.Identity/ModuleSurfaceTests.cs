using System.Reflection;
using Svac.DomainCore.Contracts.Purge;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// SLICE_S3_CONTRACT.md Pass 1 (BUILD phase) flip of the Phase-1 scaffold's absence proofs. Phase 1's own
/// doc comment named this exactly: "verified rather than assumed, so a Phase-2 change landing an EF
/// DbContext ... is caught immediately" — that tripwire fired the moment IdentityDbContext landed; this
/// is the deliberate, documented flip to the BUILD-phase shape, never a silent weakening. The PurgeRegistration
/// absence proof is UNCHANGED and still real: no identity store is purge-registered at Pass 1 (§0 DO-NOT
/// list — export/deletion pipelines are Pass 2, and PurgeRegistryGateTests.cs's own store enumeration is
/// scoped to CoreDbContext only, so identity's tables are correctly outside that gate's reach too).
/// </summary>
public sealed class ModuleSurfaceTests
{
    private static readonly Assembly[] ModuleAssemblies =
    {
        typeof(Svac.Identity.Contracts.IAccountLifecycle).Assembly, // Svac.Identity.Contracts
        typeof(Svac.Identity.DependencyInjection.IdentityServiceCollectionExtensions).Assembly, // Svac.Identity
    };

    [Fact]
    public void OnlyOneModuleAssembly_DeclaresAnEfDbContext_AndItIsTheContractsAssemblyThatDoesNot()
    {
        // Checked by base-type FULL-NAME WALK, deliberately without an EF Core package reference in THIS
        // test project — see Svac.Tests.Identity.csproj's own comment for why.
        var contractsAssembly = typeof(Svac.Identity.Contracts.IAccountLifecycle).Assembly;
        var contractsOffenders = contractsAssembly.GetTypes().Where(IsEfDbContextSubclass).Select(t => t.FullName).ToArray();
        Assert.Empty(contractsOffenders); // the PUBLIC contract assembly stays EF-free, always.

        var internalAssembly = typeof(Svac.Identity.DependencyInjection.IdentityServiceCollectionExtensions).Assembly;
        var internalDbContexts = internalAssembly.GetTypes().Where(IsEfDbContextSubclass).Select(t => t.FullName).ToArray();
        // BUILD phase: exactly one DbContext now exists — IdentityDbContext, schema `identity`.
        var single = Assert.Single(internalDbContexts);
        Assert.Equal("Svac.Identity.Persistence.IdentityDbContext", single);
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
    public void ContractsAssembly_StillDoesNotReferenceEntityFrameworkCore()
    {
        // The PUBLIC contract assembly (the only one later modules may reference, §1a) must stay EF-free
        // forever, even though the INTERNAL Svac.Identity assembly now legitimately references EF Core
        // for IdentityDbContext (BUILD phase, SLICE_S3_CONTRACT.md §1a's literal reference list).
        var referencesEf = typeof(Svac.Identity.Contracts.IAccountLifecycle).Assembly
            .GetReferencedAssemblies()
            .Any(r => r.Name?.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) == true);

        Assert.False(referencesEf, "Svac.Identity.Contracts must never reference EF Core (1A module-boundary rule — it is the ONLY assembly a future module may reference).");
    }
}
