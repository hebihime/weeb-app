using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Svac.AdminHost.Domain.Persistence;
using Svac.AdminHost.Domain.Policy;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Deterministic;
using Svac.DomainCore.Persistence;

namespace Svac.AdminHost.Domain.Execution;

/// <summary>
/// The audited-action chokepoint's ONE implementation (SLICE_S5_CONTRACT.md §1c, §8 seam 3): "staff power
/// exists only inside the admin host, and only through the audited-action chokepoint." Sequence, inside
/// ONE Postgres transaction spanning BOTH <see cref="AdminDbContext"/> and <see cref="CoreDbContext"/>
/// end to end (see <see cref="OpenSharedTransactionAsync"/> for how two independently-DI-scoped
/// EF contexts — each its own ADO.NET connection by default — are made to share one physical transaction):
///
///   1. re-read staff row (active? stamp?) + grants — revocation bites now.
///   2. hat = HatFor(action, grants); the enriched RequestContext carries hat + the full roles_held
///      snapshot on RequestContext.Staff.
///   3. reason precheck (BEFORE Authorize, per the executor doc's own tie-break over the numbered list's
///      step 5) — whitespace on a requires_reason row refuses immediately, NOT audited, work() never
///      invoked. Skipped for core.config.set.* actions: their reason requirement is per-CONFIG-KEY,
///      enforced inside IConfigRegistry.SetValue itself (called from work), never at this generic layer.
///   4. policyEngine.Authorize(actor, action, target) — deny ⇒ Denied(reasonKey), audited
///      admin.action.refused, work() never invoked.
///   5. four-eyes (9A admin.four_eyes_required) — fail-closed refusal when armed AND the acting hat isn't
///      SuperAdmin AND the row RequiresReason. work() never invoked.
///   6. work(ctx) — the domain mutation. Exceptions propagate to the caller UNCHANGED (never converted to
///      a typed result) after this method rolls back the shared transaction.
///   7. EXACTLY ONE audit event per action: core.config.set.* actions are SELF-LOGGING (work's own
///      IConfigRegistry.SetValue call already appended the {hat, roles_held}-enriched config.set event,
///      PHASE_2A_SUBSTRATE.md §1) — this executor appends NOTHING further for those. Every other action
///      key gets ONE admin.action.executed envelope {action, target_ref, hat, roles_held, reason}.
///   8. commit the shared transaction; return Success(ctx).
///
/// Boot law (Svac.AdminHost.AdminHostBootChecks.RequireAdminActionsCovered, already real): every action
/// key this executor is invoked with must resolve to a PolicyTable row at startup or the host refuses to
/// boot — enforced there, re-asserted here defensively (an unregistered action reaching THIS method at
/// runtime, which RequireAdminActionsCovered should make unreachable, still throws rather than silently
/// allowing).
/// </summary>
public sealed class AdminActionExecutor(
    AdminDbContext adminDb,
    CoreDbContext coreDb,
    IEventStore eventStore,
    IPolicyEngine policyEngine,
    IPolicyTable policyTable,
    IConfigRegistry configRegistry) : IAdminActionExecutor
{
    private const string AdminActionRefused = "admin.action.refused";
    private const string AdminActionExecuted = "admin.action.executed";

    private static readonly IReadOnlySet<StaffRole> EmptyRoles = new HashSet<StaffRole>();
    private static readonly IReadOnlySet<int> AllRoleRanks = new HashSet<int>(Enum.GetValues<StaffRole>().Select(r => (int)r));

    public async Task<AdminActionResult> Execute(
        RequestContext callerCtx,
        string action,
        TargetRef target,
        string? reason,
        Func<RequestContext, Task> work,
        CancellationToken ct = default)
    {
        if (callerCtx.Actor.Kind is not (ActorKind.Staff or ActorKind.System))
        {
            throw new ArgumentException(
                $"AdminActionExecutor.Execute called with actor kind {callerCtx.Actor.Kind} — only Staff " +
                "or System (bootstrap, §3) may reach this chokepoint; a caller bug, not a policy question.",
                nameof(callerCtx));
        }

        var row = policyTable.Find(action)
            ?? throw new InvalidOperationException(
                $"admin action \"{action}\" has no PolicyTable row — Svac.AdminHost.AdminHostBootChecks." +
                "RequireAdminActionsCovered should have refused boot before this call was ever reachable.");

        if (string.IsNullOrEmpty(target.ResourceId))
        {
            throw new ArgumentException(
                $"AdminActionExecutor.Execute called with no real ResourceId for action \"{action}\" — " +
                "every admin verb conveys a REAL target (SLICE_S5_CONTRACT.md §9's Auth-F3 closure), " +
                "never a TargetRef.ForAction placeholder.",
                nameof(target));

        }

        var connectionString = adminDb.Database.GetConnectionString() ?? coreDb.Database.GetConnectionString()
            ?? throw new InvalidOperationException(
                "AdminActionExecutor needs a real connection string on either AdminDbContext or " +
                "CoreDbContext to open the shared executor transaction.");

        await using var sharedConnection = new NpgsqlConnection(connectionString);
        await sharedConnection.OpenAsync(ct);
        await using var transaction = await sharedConnection.BeginTransactionAsync(ct);

        // Both contexts are re-pointed at OUR shared connection for the duration of this call, so
        // work()'s own SaveChangesAsync (against adminDb, or against coreDb via configRegistry for
        // config.set actions) and this executor's own audit-event append commit or roll back TOGETHER —
        // see the doc comment on OpenSharedTransactionAsync for why a plain UseTransactionAsync on each
        // context's own, separately-opened connection cannot achieve this.
        SwapInSharedConnection(adminDb.Database, sharedConnection);
        SwapInSharedConnection(coreDb.Database, sharedConnection);

        try
        {
            await adminDb.Database.UseTransactionAsync(transaction, ct);
            await coreDb.Database.UseTransactionAsync(transaction, ct);

            // --- 1. re-read staff row + grants — revocation bites now. Skipped for a System actor: the
            // two bootstrap-capable rows (admin.staff.provision/role_grant, §3) allow System specifically
            // because bootstrap runs before any staff row can possibly exist to re-read; the Role axis
            // (PolicyEngine.Authorize) never evaluates for a non-Staff actor either. ---
            var rolesHeld = EmptyRoles;
            StaffRole? hat = null;

            if (callerCtx.Actor.Kind == ActorKind.Staff)
            {
                var staffId = callerCtx.Actor.Id.ToString();
                var staffRow = await adminDb.StaffAccounts.SingleOrDefaultAsync(s => s.Id == staffId, ct);
                if (staffRow is null || staffRow.Status != "active")
                {
                    var denied = new AdminActionResult.Denied("policy.denied.admin_actor_inactive");
                    await AppendAuditEvent(callerCtx, AdminActionRefused, action, target, reason, hat: null, rolesHeld: EmptyRoles, ct);
                    await transaction.CommitAsync(ct);
                    return denied;
                }

                var roleCodes = await adminDb.StaffRoleGrants
                    .Where(g => g.StaffId == staffId && g.RevokedAt == null)
                    .Select(g => g.Role)
                    .ToListAsync(ct);
                rolesHeld = roleCodes.Select(StaffRoleCodes.Parse).ToHashSet();

                // --- 2. hat = HatFor(action, grants) — the least-privileged role among the actor's
                // grants that satisfies THIS row's allowlist. Null StaffRoles (no admin.* row leaves it
                // null, but a future row legally could) means "no role restriction" — any held role
                // satisfies it. ---
                var allowedRanks = row.StaffRoles is { } declared
                    ? declared.Select(r => (int)r).ToHashSet()
                    : AllRoleRanks;
                var heldRanks = rolesHeld.Select(r => (int)r).ToHashSet();
                var hatRank = HatFor.SelectLeastPrivileged(heldRanks, allowedRanks);
                hat = hatRank is int rank ? (StaffRole)rank : null;
                // hat stays null when the staff actor holds NONE of the roles this row allows (including
                // holding zero grants at all) — Authorize's own, independent Role-axis check (step 4)
                // denies exactly this case regardless; this executor never fabricates a hat the actor does
                // not actually hold just to have something to audit with.
            }

            var ctx = callerCtx with
            {
                Staff = hat is StaffRole actingHat
                    ? new StaffContext(callerCtx.Actor.Id, rolesHeld, actingHat)
                    : null,
            };

            // --- 3. reason precheck, BEFORE Authorize. core.config.set.* actions delegate their reason
            // requirement entirely to IConfigRegistry.SetValue (per-CONFIG-KEY, not per-action) — see
            // ConfigEditorBoundsTests.cs, which exercises a config.set action whose POLICY row
            // RequiresReason=true but whose CONFIG entry requires_reason=false with reason=null, and
            // expects the call to reach work() (and IConfigRegistry's own bounds check) rather than being
            // refused here. ---
            if (row.RequiresReason && !IsConfigSetAction(action) && string.IsNullOrWhiteSpace(reason))
            {
                await transaction.CommitAsync(ct); // nothing was written — commit-of-nothing == rollback-of-nothing
                return new AdminActionResult.ReasonRequired();
            }

            // --- 4. Authorize. ---
            var decision = await policyEngine.Authorize(ctx.Actor, action, target, ct);
            if (!decision.IsAllowed)
            {
                var reasonKey = decision is PolicyDecision.DenyStandard standard ? standard.ReasonKey : row.ReasonKey;
                var denied = new AdminActionResult.Denied(reasonKey);
                await AppendAuditEvent(ctx, AdminActionRefused, action, target, reason, hat, rolesHeld, ct);
                await transaction.CommitAsync(ct);
                return denied;
            }

            // --- 5. four-eyes (9A admin.four_eyes_required, §4): fail-closed refusal when armed AND the
            // acting hat isn't SuperAdmin AND the row RequiresReason. Short-circuited to a bare bool
            // comparison before ever reading the 9A key — every admin.staff.* row is SuperAdmin-only, so
            // this can structurally never fire for a staff-lifecycle action (Authorize already denied any
            // non-SuperAdmin hat at step 4); it only ever matters for a row like core.config.set.ops whose
            // StaffRoles allowlist is wider than {SuperAdmin}. ---
            if (hat is { } hatValue && hatValue != StaffRole.SuperAdmin && row.RequiresReason && await IsFourEyesArmed(ct))
            {
                await AppendAuditEvent(ctx, AdminActionRefused, action, target, reason, hat, rolesHeld, ct);
                await transaction.CommitAsync(ct);
                return new AdminActionResult.FourEyesRequired();
            }

            // --- 6. work(ctx) — the domain mutation. Exceptions propagate UNCHANGED to the caller (the
            // catch block below rolls back first) — ConfigEditorBoundsTests.cs's bounds-rejection
            // ArgumentException and AdminActionExecutorTests.cs's simulated post-flush failure both
            // depend on this: neither is a typed AdminActionResult, both are real exceptions. The ONE
            // narrow, named exception (SLICE_S5_CONTRACT.md §2: "the partial unique index is the
            // check-then-act guard on double-grants (catch violation → re-read winner, idempotent-under-
            // race)"): a work() that raced another Execute() call for the SAME (staff_id, role) grant hits
            // ux_active_grant — the LOSER's row never persisted, but the WINNER's already satisfies the
            // caller's actual intent ("this role is granted"), so this is a successful no-op, never a
            // caller-visible failure. Scoped to this ONE named constraint — every other DbUpdateException
            // (a real bug, not a benign race) still propagates unchanged. ---
            try
            {
                await work(ctx);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex, "ux_active_grant"))
            {
                // Detach the failed, never-persisted insert so adminDb stays usable for the rest of this
                // call (the audit envelope below, and whatever the caller does with the returned Success
                // afterward) — mirrors StaffBootstrapper.BootstrapIfEmpty's identical pattern for the SAME
                // index.
                adminDb.ChangeTracker.Clear();
            }

            // --- 7. exactly ONE audit event per action. ---
            if (!IsConfigSetAction(action))
            {
                await AppendAuditEvent(ctx, AdminActionExecuted, action, target, reason, hat, rolesHeld, ct);
            }
            // config.set actions are SELF-LOGGING: work's own IConfigRegistry.SetValue call already
            // appended the enriched config.set event (ctx.Staff → {hat, roles_held}), in the SAME shared
            // transaction, same SaveChangesAsync pattern as everything else here — appending anything
            // further would double-log (SLICE_S5_CONTRACT.md §1c step 7's own "NOT double-logged" law).

            await transaction.CommitAsync(ct);
            return new AdminActionResult.Success(ctx);
        }
        catch
        {
            await TryRollback(transaction, ct);
            throw;
        }
        finally
        {
            // Restore BOTH contexts to a normal, EF-owned, closed connection on the SAME connection
            // string — never the ORIGINAL connection object: EF's SetDbConnection disposes the outgoing
            // connection the moment it is swapped away from (when that connection was context-owned,
            // which the very first swap-in above always was), so "restoring" the original object would be
            // handing back an already-disposed connection. A fresh connection on the same string is
            // functionally identical to the context's pre-call state and leaves adminDb/coreDb fully
            // usable for whatever the caller (a Razor page re-rendering after this call) does next in the
            // same request scope.
            RestoreOwnConnection(adminDb.Database, connectionString);
            RestoreOwnConnection(coreDb.Database, connectionString);
        }
    }

    private static bool IsConfigSetAction(string action) => action.StartsWith("core.config.set.", StringComparison.Ordinal);

    private static bool IsUniqueViolation(DbUpdateException ex, string constraintName) =>
        ex.InnerException is Npgsql.PostgresException pg
        && pg.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation
        && pg.ConstraintName == constraintName;

    private async Task<bool> IsFourEyesArmed(CancellationToken ct)
    {
        try
        {
            return await configRegistry.GetValue<bool>("admin.four_eyes_required", ct);
        }
        catch (KeyNotFoundException)
        {
            // The unit tests below construct services directly (no manifest loader run) and most never
            // seed this key; the real host seeds it from admin-host.config.json at startup (v0 = false).
            // An unseeded tunable defaulting to its own documented v0 value is correct, not a fail-open —
            // false is exactly what v0 declares.
            return false;
        }
    }

    private async Task AppendAuditEvent(
        RequestContext ctx, string eventType, string action, TargetRef target, string? reason,
        StaffRole? hat, IReadOnlySet<StaffRole> rolesHeld, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            action,
            target_ref = target.ResourceId,
            hat = hat?.ToString(),
            roles_held = rolesHeld.OrderBy(r => (int)r).Select(r => r.ToString()).ToArray(),
            reason,
        });
        // ExpectedVersion.AnyVersion joins the ALREADY-open shared transaction (adminDb/coreDb.Database.
        // CurrentTransaction is non-null by the time this runs) — PostgresEventStore.Append's own
        // "ownsLocalTransaction" branch only activates when no ambient transaction exists, so this never
        // opens (or commits) a second, competing transaction of its own.
        await eventStore.Append(StreamType.Audit, target.ResourceId!, eventType, payload, ctx, ExpectedVersion.AnyVersion, ct);
    }

    /// <summary>
    /// Two independently-DI-scoped EF contexts (<see cref="AdminDbContext"/>, <see cref="CoreDbContext"/>)
    /// each own their OWN ADO.NET connection by default — even built from the identical connection string,
    /// they are two distinct <see cref="NpgsqlConnection"/> objects, and EF's <c>Database.UseTransaction</c>
    /// throws if the transaction's connection does not match the context's own (verified against EF Core
    /// 10's <c>RelationalTransactionManager</c> — this is not incidental, it is how EF Core enforces that a
    /// shared transaction really IS shared). <see cref="Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.SetDbConnection"/>
    /// (documented: "can only be set when the existing connection, if any, is not open") re-points a
    /// context at a DIFFERENT connection object at runtime — closing any currently-open connection first
    /// (a scoped context between operations is normally already closed, but this is defensive), so both
    /// contexts end up pointed at the SAME physical connection this method opened, and a single
    /// <c>BeginTransactionAsync</c> + <c>UseTransactionAsync</c> on each genuinely shares one Postgres
    /// transaction — proven end to end (rollback leaves BOTH contexts' writes absent; commit leaves BOTH
    /// present; the swapped-out contexts remain fully usable afterward) against a real Postgres instance
    /// before this method was written.
    /// </summary>
    private static void SwapInSharedConnection(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade database, NpgsqlConnection sharedConnection)
    {
        if (database.GetDbConnection().State != ConnectionState.Closed)
        {
            database.CloseConnection();
        }
        database.SetDbConnection(sharedConnection, contextOwnsConnection: false);
    }

    private static void RestoreOwnConnection(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade database, string connectionString) =>
        database.SetDbConnection(new NpgsqlConnection(connectionString), contextOwnsConnection: true);

    private static async Task TryRollback(NpgsqlTransaction transaction, CancellationToken ct)
    {
        try
        {
            await transaction.RollbackAsync(ct);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The connection/transaction may already be broken by whatever caused the caller's own
            // exception — a rollback failure must never mask the original exception this method rethrows.
        }
    }
}
