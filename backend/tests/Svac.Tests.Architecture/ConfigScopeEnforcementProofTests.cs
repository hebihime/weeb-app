using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Config;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// OPS-4 (SECURITY_REVIEW_S3.md, MED, DEFERRED — carried to S5 with the admin config desk):
/// <c>ConfigRegistry.SetValue</c> checks <c>RequiresReason</c> and bounds (OPS-3, now generalized) but
/// never compares the caller's authority against <c>row.Scope</c> ("founder"/"ops"/"set"). There is no
/// ops-vs-founder gate at the substrate — a founder-scope key (e.g. <c>identity.deletion.grace_days</c>,
/// the GDPR grace-window clock; <c>identity.handle.retirement_days</c>; <c>identity.export.
/// statutory_deadline_days</c>) is, at the registry layer, writable by an ops-scope actor or ANY actor at
/// all, since scope enforcement is entirely deferred to the not-yet-built S5 desk / 4A policy layer.
///
/// This Skip-annotated proof calls <c>SetValue</c> directly against a founder-scoped key with a plain
/// Staff actor (no founder credential exists anywhere in the type system today) and asserts it should be
/// REJECTED — which currently fails (SetValue has no scope check at all, so it silently succeeds). Fixed
/// shape (S5): enforce <c>row.Scope</c> against the actor's granted scope inside SetValue (deny + audit on
/// mismatch), so the guarantee is substrate-level, not desk-level.
/// </summary>
public sealed class ConfigScopeEnforcementProofTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:16-3.4")
        .Build();

    private string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private CoreDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(ConnectionString).Options);

    [Fact(Skip = "deferred: SECURITY_REVIEW_S3.md OPS-4 (ConfigRegistry.SetValue enforces no scope — a founder-scope key is writable by any actor that reaches SetValue) -> carried to S5 (admin config desk)")]
    public async Task SetValue_OnAFounderScopedKey_ByAnOrdinaryStaffActor_ShouldBeRejected()
    {
        using var db = NewDb();
        var eventStore = new PostgresEventStore(db);
        var loader = new ConfigSeedLoader(db, eventStore);
        var registry = new ConfigRegistry(db, eventStore);

        var systemActor = new ActorRef(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);
        var seedCtx = RequestContext.System(systemActor, "ops-4-proof-seed");
        await loader.SeedFromFile(RealIdentityManifestPath(), seedCtx);

        // identity.deletion.grace_days is scope:"founder" per identity.config.json — no ActorKind in this
        // codebase distinguishes "founder" from any other Staff/System caller, so this is the strongest
        // available stand-in for "an ops-authority actor, not a founder."
        var ordinaryStaffActor = new ActorRef(OpaqueId.New(IdPrefixes.Staff, DateTimeOffset.UtcNow, Random.Shared), ActorKind.Staff);
        var ctx = RequestContext.System(ordinaryStaffActor, "ops-4-proof-write");

        // Documents the gap: today this SUCCEEDS — SetValue never reads row.Scope at all. The fixed shape
        // (S5) rejects a founder-scope write from a non-founder actor.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.SetValue(
            "identity.deletion.grace_days", 30, "ops-4-proof: attempting a founder-scope edit as ops", ordinaryStaffActor, ctx));
    }

    private static string RealIdentityManifestPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "docker-compose.yml")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        if (dir is null)
        {
            throw new InvalidOperationException("could not locate repo root from " + AppContext.BaseDirectory);
        }
        return Path.Combine(dir, "backend", "modules", "identity", "config", "identity.config.json");
    }
}
