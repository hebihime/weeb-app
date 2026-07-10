using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Policy;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// B1's deny-mode proof (SLICE_S1_CONTRACT.md §3, §10.3): a mapped POST with a deny-row actor is denied
/// with the row's declared mode; an allowed actor kind is allowed.
/// </summary>
public sealed class PolicyEngineTests
{
    private readonly PolicyEngine _engine = new(new PolicyTable());

    [Fact]
    public void SystemActor_LedgerAppend_IsAllowed()
    {
        var systemActor = new ActorRef(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);
        var decision = _engine.Authorize(systemActor, "core.ledger.append", TargetRef.ForAction("core.ledger.append"));
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void UserActor_LedgerAppend_IsDeniedAsAbsence_NeverAnObservableDenyStandard()
    {
        // SilentRej-L1/L2 (SECURITY_REVIEW_S1.md): a CONSUMER actor (User/Anonymous) denied by
        // omission from a row's ActorKinds must always render as absence, never the row's own declared
        // DenyStandard — DenyStandard is legal only for staff/partner actor kinds (§1b). This test used
        // to enshrine the leaky behavior (hence its old name, "...IsDeniedStandard"); PolicyEngine's
        // consumer-denial coercion is what SilentRejectionLeakLensTests proves fixes it.
        var userActor = new ActorRef(OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared), ActorKind.User);
        var decision = _engine.Authorize(userActor, "core.ledger.append", TargetRef.ForAction("core.ledger.append"));
        Assert.IsType<PolicyDecision.DenyAsAbsence>(decision);
    }

    [Fact]
    public void AnonymousActor_QuotaConsume_IsAllowed_AxisEvaluationHappensInsideIQuotaService()
    {
        // The policy row for core.quota.consume allows every actor kind (§3: "internal chokepoint") —
        // the actual cap/window deny decision is IQuotaService.Consume's job, not the 4A row's.
        var anon = new ActorRef(OpaqueId.New(IdPrefixes.Anonymous, DateTimeOffset.UtcNow, Random.Shared), ActorKind.Anonymous);
        var decision = _engine.Authorize(anon, "core.quota.consume", TargetRef.ForAction("core.quota.consume"));
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void UnmappedAction_FailsClosed_NeverAllows()
    {
        var systemActor = new ActorRef(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);
        var decision = _engine.Authorize(systemActor, "core.totally.unregistered.action", TargetRef.ForAction("x"));
        var deny = Assert.IsType<PolicyDecision.DenyStandard>(decision);
        Assert.Equal("policy.denied.unmapped_action", deny.ReasonKey);
    }

    [Fact]
    public void PolicyTable_IsNonEmpty_ExercisedByRealInternalConsumers()
    {
        // §3: "Zero consumer mutation endpoints → zero consumer entries ... The substrate's own internal
        // verbs get real rows so the engine is exercised by real consumers, not vacuously."
        Assert.NotEmpty(new PolicyTable().Entries);
    }

    [Fact]
    public void DenyAsLimitRows_AlwaysCarryAQuotaKeyOrAreExplicitlyDynamic()
    {
        foreach (var entry in new PolicyTable().Entries.Where(e => e.DenyMode == PolicyDenyMode.DenyAsLimit))
        {
            Assert.True(entry.DynamicQuotaKey || !string.IsNullOrEmpty(entry.QuotaKeyForLimit));
        }
    }

    // --- The generated action×axis matrix suite (§3: "CI generates the action×axis matrix suite FROM
    // this same table") + the consumer-DenyStandard-on-read rule (§3, §8), proven non-vacuous by a red
    // fixture: a deliberately bad table row (a consumer actor mapped to DenyStandard) must be caught by
    // the same checker the real table passes. ---

    [Fact]
    public void RealPolicyTable_HasNoConsumerActorMappedToDenyStandard()
    {
        var violations = FindConsumerDenyStandardViolations(new PolicyTable().Entries);
        Assert.Empty(violations);
    }

    [Fact]
    public void RedFixture_ConsumerActorMappedToDenyStandard_IsDetected()
    {
        var badRow = new PolicyTableEntry(
            Action: "fixture.bad.row",
            ActorKinds: new HashSet<ActorKind> { ActorKind.User }, // consumer actor kind
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyStandard, // illegal combination for a consumer actor
            RequiresReason: false,
            ReasonKey: "fixture.reason");

        var violations = FindConsumerDenyStandardViolations(new[] { badRow });
        Assert.Single(violations);
        Assert.Equal("fixture.bad.row", violations[0]);
    }

    [Fact]
    public void RedFixture_ReadPathDenyStandardRow_ExcludingAConsumerKind_IsDetected()
    {
        // SilentRej-L2's exact shape (SilentRejectionLeakLensTests.StaffOnlyDenyStandardReadRow_...): a
        // staff-only READ row denies a consumer by OMISSION, not by explicit mapping — the ORIGINAL guard
        // (which only ever inspected ActorKinds for an explicitly-listed consumer kind) was structurally
        // blind to this. IsReadPath=true is what arms the broadened half of the guard below.
        var readRow = new PolicyTableEntry(
            Action: "fixture.bad.read.row",
            ActorKinds: new HashSet<ActorKind> { ActorKind.Staff },
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: false,
            ReasonKey: "fixture.reason",
            IsReadPath: true);

        var violations = FindConsumerDenyStandardViolations(new[] { readRow });
        Assert.Single(violations);
        Assert.Equal("fixture.bad.read.row", violations[0]);
    }

    [Fact]
    public void RealPolicyTable_HasNoReadPathRowsYet_TheBroadenedGuardIsVacuousUntilOneLands()
    {
        // S1 ships zero consumer-reachable reads (§0) — every real row today is IsReadPath=false, so the
        // broadened half of the guard cannot yet fire against the shipped table. This pins that fact
        // explicitly rather than leaving it as an unstated assumption behind
        // RealPolicyTable_HasNoConsumerActorMappedToDenyStandard staying green.
        Assert.DoesNotContain(new PolicyTable().Entries, e => e.IsReadPath);
    }

    /// <summary>
    /// Contract-lint invariant 4 made source-level (§3): "an arch test fails any policy entry mapping a
    /// consumer actor to DenyStandard on a read path." No policy row at S1 represents a read path (every
    /// row is a mutation-class verb — config.set, ledger.append/reverse, event.tombstone, purge.execute,
    /// quota.consume); the check still runs unconditionally against every row's actor-kind × deny-mode
    /// pair so it activates the moment a future slice adds a read-path row.
    /// </summary>
    private static readonly HashSet<ActorKind> ConsumerActorKinds = new() { ActorKind.User, ActorKind.Anonymous };

    private static List<string> FindConsumerDenyStandardViolations(IEnumerable<PolicyTableEntry> entries) =>
        entries
            .Where(e => e.DenyMode == PolicyDenyMode.DenyStandard &&
                (e.ActorKinds.Any(k => ConsumerActorKinds.Contains(k)) // explicit: a consumer kind IS listed yet the row still declares DenyStandard — a self-contradictory row (a listed kind is always Allowed, never denied at all), always wrong regardless of read/write.
                 || (e.IsReadPath && ConsumerActorKinds.Any(k => !e.ActorKinds.Contains(k))))) // SilentRej-L2 broadening: on a READ path specifically, a consumer kind excluded by OMISSION would hit DenyStandard the moment PolicyEngine's own consumer-denial coercion were ever bypassed — the static lint's defense-in-depth mirror of that runtime fix.
            .Select(e => e.Action)
            .ToList();
}
