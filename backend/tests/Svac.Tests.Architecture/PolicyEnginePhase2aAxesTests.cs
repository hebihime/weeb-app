using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Policy;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// Proves the three Phase-2a additive axes PolicyEngine ANDs onto the existing actor-kind check
/// (PHASE_2A_SUBSTRATE.md §1): target-ownership (SelfOnly/OwnedResource), accountState, and staff Role.
/// Every S1/S2 row leaves all three at their default — <see cref="PolicyEngineTests"/> already proves
/// those decisions stay byte-identical; THIS file proves the new axes actually fire once a row declares
/// them (fixture rows only — no real S1/S2 row sets any of these).
/// </summary>
public sealed class PolicyEnginePhase2aAxesTests
{
    private static ActorRef User(OpaqueId id) => new(id, ActorKind.User);
    private static OpaqueId NewId(string prefix) => OpaqueId.New(prefix, DateTimeOffset.UtcNow, Random.Shared);

    // --- Axis 1: target-ownership -------------------------------------------------------------------

    [Fact]
    public async Task SelfOnly_TargetMatchesActor_Allows()
    {
        var row = SelfOnlyRow();
        var engine = new PolicyEngine(new SingleRowTable(row));
        var actorId = NewId(IdPrefixes.User);

        var decision = await engine.Authorize(User(actorId), row.Action, new TargetRef("account", actorId.ToString()));

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public async Task SelfOnly_TargetIsAnotherActor_DeniesAsAbsence()
    {
        var row = SelfOnlyRow();
        var engine = new PolicyEngine(new SingleRowTable(row));
        var actorId = NewId(IdPrefixes.User);
        var otherId = NewId(IdPrefixes.User);

        var decision = await engine.Authorize(User(actorId), row.Action, new TargetRef("account", otherId.ToString()));

        Assert.IsType<PolicyDecision.DenyAsAbsence>(decision);
    }

    [Fact]
    public async Task OwnedResource_ResolverConfirmsOwnership_Allows()
    {
        var row = OwnedResourceRow("session");
        var actorId = NewId(IdPrefixes.User);
        var resolver = new FixedOwnershipResolver("session", ("ses_owned", actorId));
        var engine = new PolicyEngine(new SingleRowTable(row), ownershipResolvers: new[] { resolver });

        var decision = await engine.Authorize(User(actorId), row.Action, new TargetRef("session", "ses_owned"));

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public async Task OwnedResource_ResolverReturnsADifferentOwner_DeniesAsAbsence()
    {
        var row = OwnedResourceRow("session");
        var actorId = NewId(IdPrefixes.User);
        var otherOwnerId = NewId(IdPrefixes.User);
        var resolver = new FixedOwnershipResolver("session", ("ses_foreign", otherOwnerId));
        var engine = new PolicyEngine(new SingleRowTable(row), ownershipResolvers: new[] { resolver });

        var decision = await engine.Authorize(User(actorId), row.Action, new TargetRef("session", "ses_foreign"));

        Assert.IsType<PolicyDecision.DenyAsAbsence>(decision);
    }

    [Fact]
    public async Task OwnedResource_UnknownResourceId_ResolverReturnsNull_DeniesAsAbsence_NotFoundAndForeignAreOneBranch()
    {
        var row = OwnedResourceRow("session");
        var actorId = NewId(IdPrefixes.User);
        var resolver = new FixedOwnershipResolver("session"); // no entries — every id resolves to null owner.
        var engine = new PolicyEngine(new SingleRowTable(row), ownershipResolvers: new[] { resolver });

        var decision = await engine.Authorize(User(actorId), row.Action, new TargetRef("session", "ses_nonexistent"));

        Assert.IsType<PolicyDecision.DenyAsAbsence>(decision);
    }

    [Fact]
    public async Task OwnedResource_NoResolverRegisteredForResourceType_DeniesAsAbsence_S1S2DefaultPosture()
    {
        // Zero IResourceOwnershipResolver registered at S1/S2 — the structural no-op state this surgery ships.
        var row = OwnedResourceRow("session");
        var engine = new PolicyEngine(new SingleRowTable(row));
        var actorId = NewId(IdPrefixes.User);

        var decision = await engine.Authorize(User(actorId), row.Action, new TargetRef("session", "ses_anything"));

        Assert.IsType<PolicyDecision.DenyAsAbsence>(decision);
    }

    // --- Axis 2: accountState -------------------------------------------------------------------------

    [Fact]
    public async Task AccountState_ActorStateInAllowedSet_Allows()
    {
        var row = AccountStateRow(new HashSet<string> { "active", "suspended" });
        var accessor = new FixedRequestContextAccessor("active");
        var engine = new PolicyEngine(new SingleRowTable(row), requestContextAccessor: accessor);
        var actorId = NewId(IdPrefixes.User);

        var decision = await engine.Authorize(User(actorId), row.Action, TargetRef.ForAction(row.Action));

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public async Task AccountState_ActorStateOutsideAllowedSet_DeniesAsAbsence()
    {
        var row = AccountStateRow(new HashSet<string> { "active" });
        var accessor = new FixedRequestContextAccessor("banned");
        var engine = new PolicyEngine(new SingleRowTable(row), requestContextAccessor: accessor);
        var actorId = NewId(IdPrefixes.User);

        var decision = await engine.Authorize(User(actorId), row.Action, TargetRef.ForAction(row.Action));

        Assert.IsType<PolicyDecision.DenyAsAbsence>(decision);
    }

    [Fact]
    public async Task AccountState_NoAccessorRegistered_DeniesAsAbsence_FailClosedNeverUnknownPasses()
    {
        var row = AccountStateRow(new HashSet<string> { "active" });
        var engine = new PolicyEngine(new SingleRowTable(row)); // no IRequestContextAccessor supplied.
        var actorId = NewId(IdPrefixes.User);

        var decision = await engine.Authorize(User(actorId), row.Action, TargetRef.ForAction(row.Action));

        Assert.IsType<PolicyDecision.DenyAsAbsence>(decision);
    }

    [Fact]
    public async Task AccountState_NullOnEveryS1S2Row_IsANoOp()
    {
        // The real proof this axis is byte-identical for S1/S2 lives in PolicyEngineTests /
        // SilentRejectionLeakLensTests (real CorePolicyTableSource rows, all AllowedAccountStates=null).
        // Pinned here explicitly: a row that never sets the axis allows a User regardless of account state.
        var row = new PolicyTableEntry(
            Action: "fixture.no.accountstate.axis",
            ActorKinds: new HashSet<ActorKind> { ActorKind.User },
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "fixture.none");
        var engine = new PolicyEngine(new SingleRowTable(row)); // no accessor at all.
        var actorId = NewId(IdPrefixes.User);

        var decision = await engine.Authorize(User(actorId), row.Action, TargetRef.ForAction(row.Action));

        Assert.True(decision.IsAllowed);
    }

    // --- Axis 3: staff Role ----------------------------------------------------------------------------

    [Fact]
    public async Task StaffRole_GrantsOverlapAllowedRoles_Allows()
    {
        var row = StaffRoleRow(new HashSet<StaffRole> { StaffRole.SuperAdmin, StaffRole.EconomyOps });
        var resolver = new FixedStaffRoleResolver(new HashSet<StaffRole> { StaffRole.EconomyOps });
        var engine = new PolicyEngine(new SingleRowTable(row), staffRoleResolver: resolver);
        var staffId = NewId(IdPrefixes.Staff);

        var decision = await engine.Authorize(new ActorRef(staffId, ActorKind.Staff), row.Action, TargetRef.ForAction(row.Action));

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public async Task StaffRole_NoOverlap_DeniesPerRowsDenyMode()
    {
        // S1's core rows are DenyStandard for Staff — the row this fixture uses mirrors that shape (a
        // staff actor is legal to see DenyStandard, §1b).
        var row = StaffRoleRow(new HashSet<StaffRole> { StaffRole.SuperAdmin });
        var resolver = new FixedStaffRoleResolver(new HashSet<StaffRole> { StaffRole.Analyst });
        var engine = new PolicyEngine(new SingleRowTable(row), staffRoleResolver: resolver);
        var staffId = NewId(IdPrefixes.Staff);

        var decision = await engine.Authorize(new ActorRef(staffId, ActorKind.Staff), row.Action, TargetRef.ForAction(row.Action));

        Assert.IsType<PolicyDecision.DenyStandard>(decision);
    }

    [Fact]
    public async Task StaffRole_NoResolverRegistered_DenyAllStaffRoleResolverDefault_FailsClosed()
    {
        var row = StaffRoleRow(new HashSet<StaffRole> { StaffRole.SuperAdmin });
        var engine = new PolicyEngine(new SingleRowTable(row)); // no resolver supplied — DenyAllStaffRoleResolver default.
        var staffId = NewId(IdPrefixes.Staff);

        var decision = await engine.Authorize(new ActorRef(staffId, ActorKind.Staff), row.Action, TargetRef.ForAction(row.Action));

        Assert.IsType<PolicyDecision.DenyStandard>(decision);
    }

    [Fact]
    public async Task StaffRole_RowStructurallyUnwritableBeforeThisSurgery_NowWrittenAndGreen()
    {
        // SLICE_S5_CONTRACT.md §1d: "Red fixture: the test S1 called structurally unwritable — a staff
        // actor without SuperAdmin denied on core.config.set.founder — now written and green." Exercises
        // the REAL CorePolicyTableSource row (typed StaffRoles={SuperAdmin}), not a fixture row.
        var table = new PolicyTable();
        var resolver = new FixedStaffRoleResolver(new HashSet<StaffRole> { StaffRole.EconomyOps });
        var engine = new PolicyEngine(table, staffRoleResolver: resolver);
        var staffId = NewId(IdPrefixes.Staff);

        var decision = await engine.Authorize(new ActorRef(staffId, ActorKind.Staff), "core.config.set.founder", TargetRef.ForAction("core.config.set.founder"));

        Assert.IsType<PolicyDecision.DenyStandard>(decision);
    }

    [Fact]
    public async Task StaffRole_RealCoreRow_SuperAdminGrant_IsAllowed()
    {
        var table = new PolicyTable();
        var resolver = new FixedStaffRoleResolver(new HashSet<StaffRole> { StaffRole.SuperAdmin });
        var engine = new PolicyEngine(table, staffRoleResolver: resolver);
        var staffId = NewId(IdPrefixes.Staff);

        var decision = await engine.Authorize(new ActorRef(staffId, ActorKind.Staff), "core.config.set.founder", TargetRef.ForAction("core.config.set.founder"));

        Assert.True(decision.IsAllowed);
    }

    // --- Boot-refusal: duplicate action key across two IPolicyTableSources -----------------------------

    [Fact]
    public void DuplicateActionKeyAcrossTwoSources_RefusesToBoot()
    {
        var sourceA = new SingleRowSource(new PolicyTableEntry(
            Action: "fixture.duplicate.action",
            ActorKinds: new HashSet<ActorKind> { ActorKind.System },
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: false,
            ReasonKey: "fixture.a"));
        var sourceB = new SingleRowSource(new PolicyTableEntry(
            Action: "fixture.duplicate.action",
            ActorKinds: new HashSet<ActorKind> { ActorKind.Staff },
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: false,
            ReasonKey: "fixture.b"));

        var ex = Assert.Throws<InvalidOperationException>(() => new PolicyTable(new IPolicyTableSource[] { sourceA, sourceB }));
        Assert.Contains("fixture.duplicate.action", ex.Message, StringComparison.Ordinal);
        Assert.Contains("4A boot refusal", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoDuplicateKeys_UnionBuildsCleanly_EntriesFromEverySource()
    {
        var sourceA = new SingleRowSource(new PolicyTableEntry(
            Action: "fixture.a.action",
            ActorKinds: new HashSet<ActorKind> { ActorKind.System },
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: false,
            ReasonKey: "fixture.a"));
        var sourceB = new SingleRowSource(new PolicyTableEntry(
            Action: "fixture.b.action",
            ActorKinds: new HashSet<ActorKind> { ActorKind.Staff },
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: false,
            ReasonKey: "fixture.b"));

        var table = new PolicyTable(new IPolicyTableSource[] { sourceA, sourceB });

        Assert.NotNull(table.Find("fixture.a.action"));
        Assert.NotNull(table.Find("fixture.b.action"));
    }

    [Fact]
    public void WithOnlyCorePolicyTableSourceRegistered_UnionIsByteIdenticalToTheOldTable()
    {
        var unioned = new PolicyTable(new IPolicyTableSource[] { new CorePolicyTableSource() });
        var defaulted = new PolicyTable();

        Assert.Equal(defaulted.Entries.Count, unioned.Entries.Count);
        Assert.Equal(defaulted.Entries.Select(e => e.Action).OrderBy(a => a), unioned.Entries.Select(e => e.Action).OrderBy(a => a));
    }

    // --- helpers -------------------------------------------------------------------------------------

    private static PolicyTableEntry SelfOnlyRow() => new(
        Action: "fixture.self.only",
        ActorKinds: new HashSet<ActorKind> { ActorKind.User },
        Axes: PolicyAxis.None,
        DenyMode: PolicyDenyMode.DenyAsAbsence,
        RequiresReason: false,
        ReasonKey: "fixture.none",
        TargetRule: TargetRule.SelfOnly);

    private static PolicyTableEntry OwnedResourceRow(string resourceType) => new(
        Action: "fixture.owned.resource",
        ActorKinds: new HashSet<ActorKind> { ActorKind.User },
        Axes: PolicyAxis.None,
        DenyMode: PolicyDenyMode.DenyAsAbsence,
        RequiresReason: false,
        ReasonKey: "fixture.none",
        TargetRule: TargetRule.OwnedResource(resourceType));

    private static PolicyTableEntry AccountStateRow(IReadOnlySet<string> allowedStates) => new(
        Action: "fixture.account.state",
        ActorKinds: new HashSet<ActorKind> { ActorKind.User },
        Axes: PolicyAxis.AccountState,
        DenyMode: PolicyDenyMode.DenyAsAbsence,
        RequiresReason: false,
        ReasonKey: "fixture.none",
        AllowedAccountStates: allowedStates);

    private static PolicyTableEntry StaffRoleRow(IReadOnlySet<StaffRole> allowedRoles) => new(
        Action: "fixture.staff.role",
        ActorKinds: new HashSet<ActorKind> { ActorKind.Staff },
        Axes: PolicyAxis.Role,
        DenyMode: PolicyDenyMode.DenyStandard,
        RequiresReason: false,
        ReasonKey: "fixture.staff.denied",
        StaffRoles: allowedRoles);

    private sealed class SingleRowTable(PolicyTableEntry row) : IPolicyTable
    {
        public IReadOnlyList<PolicyTableEntry> Entries { get; } = new[] { row };
        public PolicyTableEntry? Find(string action) => Entries.FirstOrDefault(e => e.Action == action);
    }

    private sealed class SingleRowSource(PolicyTableEntry row) : IPolicyTableSource
    {
        public IReadOnlyList<PolicyTableEntry> Entries { get; } = new[] { row };
    }

    private sealed class FixedOwnershipResolver : IResourceOwnershipResolver
    {
        private readonly Dictionary<string, OpaqueId> _owners;

        public FixedOwnershipResolver(string resourceType, params (string ResourceId, OpaqueId Owner)[] entries)
        {
            ResourceType = resourceType;
            _owners = entries.ToDictionary(e => e.ResourceId, e => e.Owner);
        }

        public string ResourceType { get; }

        public Task<OpaqueId?> OwnerOf(string resourceId, CancellationToken ct = default) =>
            Task.FromResult(_owners.TryGetValue(resourceId, out var owner) ? owner : (OpaqueId?)null);
    }

    private sealed class FixedStaffRoleResolver(IReadOnlySet<StaffRole> grants) : IStaffRoleResolver
    {
        public Task<IReadOnlySet<StaffRole>> GrantsOf(ActorRef staff, CancellationToken ct = default) => Task.FromResult(grants);
    }

    private sealed class FixedRequestContextAccessor(string accountState) : IRequestContextAccessor
    {
        public RequestContext Current { get; } = RequestContext.System(
            new ActorRef(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System),
            "fixture-correlation") with
        { AccountState = accountState };
    }
}
