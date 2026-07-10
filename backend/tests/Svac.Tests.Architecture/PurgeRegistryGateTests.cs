using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Persistence;
using Svac.DomainCore.Purge;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// B2's proof (SLICE_S1_CONTRACT.md §6, §10.3): the arch test enumerates every EF entity type + every
/// declared blob/cache store and fails the build on any store absent from the purge-registry manifest —
/// proven non-vacuous by a red fixture (an unregistered fixture table fails the gate).
/// </summary>
public sealed class PurgeRegistryGateTests
{
    private static readonly IReadOnlySet<string> RealStoreKeys = EnumerateCoreDbContextTableNames();

    [Fact]
    public void EveryCoreDbContextTable_IsRegisteredInThePurgeRegistry()
    {
        var registry = new PurgeRegistry();
        var missing = FindUnregisteredStores(RealStoreKeys, registry.RegisteredStoreKeys);
        Assert.Empty(missing);
    }

    [Fact]
    public void RedFixture_UnregisteredTable_FailsTheGate()
    {
        var registry = new PurgeRegistry();
        var fixtureStores = new HashSet<string>(RealStoreKeys) { "fixture_unregistered_table" };

        var missing = FindUnregisteredStores(fixtureStores, registry.RegisteredStoreKeys);
        Assert.Single(missing);
        Assert.Equal("fixture_unregistered_table", missing[0]);
    }

    [Fact]
    public void EveryRegisteredStore_CoversEveryPurgeClass()
    {
        // The registry's own completeness: every (storeKey, purgeClass) pair must have exactly one
        // entry — a gap here is a silent purge-class hole, not caught by the store-presence check above.
        var registry = new PurgeRegistry();
        var allClasses = Enum.GetValues<PurgeClass>();
        var gaps = new List<string>();

        foreach (var storeKey in registry.RegisteredStoreKeys)
        {
            var coveredClasses = registry.EntriesFor(storeKey).Select(e => e.PurgeClass).ToHashSet();
            foreach (var purgeClass in allClasses)
            {
                if (!coveredClasses.Contains(purgeClass))
                {
                    gaps.Add($"{storeKey}/{purgeClass}");
                }
            }
        }

        Assert.Empty(gaps);
    }

    [Fact]
    public void EveryNotApplicableEntry_CarriesAStatedReason()
    {
        var registry = new PurgeRegistry();
        var unreasoned = registry.Entries
            .Where(e => e.Verb == PurgeVerb.NotApplicable && string.IsNullOrWhiteSpace(e.Reason))
            .ToList();
        Assert.Empty(unreasoned);
    }

    private static List<string> FindUnregisteredStores(IEnumerable<string> realStores, IReadOnlySet<string> registeredStores) =>
        realStores.Where(s => !registeredStores.Contains(s)).ToList();

    private static HashSet<string> EnumerateCoreDbContextTableNames()
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql("Host=localhost;Database=svac-model-enumeration-only")
            .Options;
        using var db = new CoreDbContext(options);
        var tableNames = db.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet();
        return tableNames;
    }
}
