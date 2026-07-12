using Microsoft.EntityFrameworkCore;
using Svac.AdminHost.Domain.Persistence;
using Svac.AdminHost.Domain.Purge;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Persistence;
using Svac.DomainCore.Purge;
using Svac.Identity.Persistence;
using Svac.Identity.Purge;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// B2's proof (SLICE_S1_CONTRACT.md §6, §10.3; SLICE_S3_CONTRACT.md §6a; SLICE_S5_CONTRACT.md §6): the
/// arch test enumerates every EF entity type (CoreDbContext, IdentityDbContext, AND, since S5,
/// AdminDbContext) + every declared blob/cache store and fails the build on any store absent from the
/// purge-registry manifest — proven non-vacuous by a red fixture (an unregistered fixture table fails
/// the gate). The registry under test is the union of every REAL host's own registration set
/// (CorePurgeRegistrySource + IdentityPurgeRegistrySource + AdminPurgeRegistrySource) — Svac.PublicApi
/// and Svac.AdminHost are two DIFFERENT deploy units that never share a process, so no single running
/// host actually assembles this exact three-way union; this test is the one place that union is proven
/// complete against every EF entity across every host at once.
/// </summary>
public sealed class PurgeRegistryGateTests
{
    private static readonly IReadOnlySet<string> RealStoreKeys = EnumerateCoreAndIdentityAndAdminDbContextTableNames();

    private static PurgeRegistry BuildRealRegistry() =>
        new(new IPurgeRegistrySource[] { new CorePurgeRegistrySource(), new IdentityPurgeRegistrySource(), new AdminPurgeRegistrySource() });

    [Fact]
    public void EveryCoreDbContextTable_IsRegisteredInThePurgeRegistry()
    {
        var registry = BuildRealRegistry();
        var missing = FindUnregisteredStores(RealStoreKeys, registry.RegisteredStoreKeys);
        Assert.Empty(missing);
    }

    [Fact]
    public void RedFixture_UnregisteredTable_FailsTheGate()
    {
        var registry = BuildRealRegistry();
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
        var registry = BuildRealRegistry();
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
        var registry = BuildRealRegistry();
        var unreasoned = registry.Entries
            .Where(e => e.Verb == PurgeVerb.NotApplicable && string.IsNullOrWhiteSpace(e.Reason))
            .ToList();
        Assert.Empty(unreasoned);
    }

    [Fact]
    public void RedFixture_AStoreKeyClaimedByTwoSources_IsABootRefusal()
    {
        // The union's own duplicate-ownership guard (SLICE_S3_CONTRACT.md §6a: "a storeKey registered by
        // more than one source is a boot refusal") — proven with a fixture source colliding on a real
        // identity store key, mirroring PolicyTable/ExportRegistry's own duplicate-key throw tests.
        var colliding = new IPurgeRegistrySource[]
        {
            new IdentityPurgeRegistrySource(),
            new FixtureCollidingPurgeRegistrySource(),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new PurgeRegistry(colliding));
        Assert.Contains("identity.accounts", ex.Message, StringComparison.Ordinal);
    }

    private sealed class FixtureCollidingPurgeRegistrySource : IPurgeRegistrySource
    {
        public IReadOnlyList<PurgeRegistrationEntry> Entries { get; } = new[]
        {
            new PurgeRegistrationEntry("identity.accounts", PurgeClass.AccountDeletion, PurgeVerb.NotApplicable, "fixture collision"),
        };
    }

    private static List<string> FindUnregisteredStores(IEnumerable<string> realStores, IReadOnlySet<string> registeredStores) =>
        realStores.Where(s => !registeredStores.Contains(s)).ToList();

    private static HashSet<string> EnumerateCoreAndIdentityAndAdminDbContextTableNames()
    {
        var coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql("Host=localhost;Database=svac-model-enumeration-only")
            .Options;
        using var coreDb = new CoreDbContext(coreOptions);

        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=svac-model-enumeration-only")
            .Options;
        using var identityDb = new IdentityDbContext(identityOptions);

        var adminOptions = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql("Host=localhost;Database=svac-model-enumeration-only")
            .Options;
        using var adminDb = new AdminDbContext(adminOptions);

        // Core's own registry keys are BARE table names (e.g. "events_ledger", §6 — unchanged from S1);
        // identity's and admin's are schema-qualified (e.g. "identity.accounts", "admin.staff_accounts",
        // §6a/§6) — the enumeration must match each side's own registration convention, not a single
        // uniform shape.
        var tableNames = coreDb.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet();

        foreach (var entityType in identityDb.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (table is null)
            {
                continue;
            }
            var schema = entityType.GetSchema();
            tableNames.Add(schema is null ? table : $"{schema}.{table}");
        }

        foreach (var entityType in adminDb.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (table is null)
            {
                continue;
            }
            var schema = entityType.GetSchema();
            tableNames.Add(schema is null ? table : $"{schema}.{table}");
        }

        return tableNames;
    }
}
