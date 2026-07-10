using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Ledger;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Deterministic;
using Svac.DomainCore.Persistence;

namespace Svac.DomainCore.Ledger;

/// <summary>
/// The ledger seam, quest-ready day one (SLICE_S1_CONTRACT.md §1b, §2). Every Append/Reverse is
/// authorized through the 4A chokepoint FIRST (core.ledger.append / core.ledger.reverse) so the policy
/// engine is exercised by a real consumer, not vacuously (§3). xp = points and the sink_purchase-only
/// negative-svac rule are validated in Deterministic.LedgerMath before they ever reach the CHECK
/// constraint — a bug surfaces as a clear .NET exception, not an opaque Postgres constraint violation.
/// </summary>
public sealed class LedgerService(CoreDbContext db, Svac.DomainCore.Contracts.Streams.IEventStore eventStore, IPolicyEngine policyEngine) : ILedger
{
    public async Task<string> Append(LedgerEntry entry, ActorRef actor, RequestContext ctx, CancellationToken ct = default)
    {
        Authorize("core.ledger.append", actor);

        var isSinkPurchase = entry.EventType == "sink_purchase";
        var movement = new LedgerMovement(entry.Points, entry.Xp, entry.Svac, isSinkPurchase);
        LedgerMath.ValidateMovement(movement); // points>=0, xp==points, svac<0 only on sink_purchase — before any row is staged.

        var id = MintId(DateTimeOffset.UtcNow);
        db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = id,
            UserId = entry.UserId,
            CrewId = entry.CrewId,
            EventType = entry.EventType,
            Points = entry.Points,
            Xp = entry.Xp,
            Svac = entry.Svac,
            QuestId = entry.QuestId,
            EvidenceRef = entry.EvidenceRef,
            Region = ctx.Region.ToString(),
            LawfulBasis = ctx.LawfulBasisVariant.Key,
            CreatedAt = DateTimeOffset.UtcNow,
            ReversalOf = null,
        });

        // Concurrency-F2 (SECURITY_REVIEW_S1.md): one explicit transaction wraps the whole write. The
        // ledger entry is staged (not yet flushed) and the event append runs FIRST — its own
        // SaveChangesAsync flushes the staged entry insert together with the event row — so the balance
        // row is never touched, and no lock on it is ever held, until the very last statement before
        // commit. UpsertBalanceAtomic below is a single atomic increment (no prior read), so whatever the
        // row's committed value is at the moment it finally acquires the lock, the increment lands on
        // top of THAT value — there is no read-then-write window for a second writer to land inside.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payload = JsonSerializer.Serialize(new { id, entry.UserId, entry.EventType, entry.Points, entry.Xp, entry.Svac });
        await eventStore.Append(StreamType.Ledger, streamId: entry.UserId, eventType: entry.EventType, payloadJson: payload, ctx, ExpectedVersion.AnyVersion, ct);
        await UpsertBalanceAtomic(entry.UserId, movement, isReversal: false, ct);
        await tx.CommitAsync(ct);

        return id;
    }

    public async Task<string> Reverse(string entryId, ActorRef actor, string reason, RequestContext ctx, CancellationToken ct = default)
    {
        Authorize("core.ledger.reverse", actor);

        var original = await db.LedgerEntries.SingleOrDefaultAsync(e => e.Id == entryId, ct)
            ?? throw new InvalidOperationException($"ledger entry \"{entryId}\" not found — reversal is the ONLY correction verb, so it must reference a real prior entry.");

        // The ORIGINAL positive movement (never a negated one) is what LedgerMath.Reverse folds OUT of
        // the running balance via UpsertBalanceAtomic's isReversal branch below.
        var originalMovement = new LedgerMovement(original.Points, original.Xp, original.Svac, original.Svac < 0);

        var reversalId = MintId(DateTimeOffset.UtcNow);
        db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = reversalId,
            UserId = original.UserId,
            CrewId = original.CrewId,
            EventType = $"{original.EventType}.reversed",
            // Functional break fix (SECURITY_REVIEW_S1.md, "Reverse can never succeed"): the reversal ROW
            // mirrors the original's POSITIVE magnitudes rather than negating them — a negated Points
            // always violated ck_ledger_entries_points_nonneg (CoreDbContext.cs: "points >= 0"), so every
            // Reverse call threw and the correction verb could never complete. `ReversalOf` (non-null
            // here) IS the reversal-direction signal: any future raw-SQL reconciliation over
            // ledger_entries must subtract rows where ReversalOf is set rather than blindly summing
            // Points — LedgerMath.Reverse (via UpsertBalanceAtomic below) already does exactly that for
            // the live balance projection, using the ORIGINAL row's own values, never this row's.
            // Svac carries no nonneg constraint, so negating it stays safe AND reads naturally as a
            // credit: reversing a sink_purchase's -100 spend shows +100 here.
            Points = original.Points,
            Xp = original.Xp,
            Svac = -original.Svac,
            QuestId = original.QuestId,
            EvidenceRef = original.EvidenceRef,
            Region = ctx.Region.ToString(),
            LawfulBasis = ctx.LawfulBasisVariant.Key,
            CreatedAt = DateTimeOffset.UtcNow,
            ReversalOf = original.Id,
        });

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payload = JsonSerializer.Serialize(new { reversalId, original.Id, reason, actor = actor.ToString() });
        await eventStore.Append(StreamType.Ledger, streamId: original.UserId, eventType: "ledger.reversed", payloadJson: payload, ctx, ExpectedVersion.AnyVersion, ct);
        await UpsertBalanceAtomic(original.UserId, originalMovement, isReversal: true, ct);
        await tx.CommitAsync(ct);

        return reversalId;
    }

    public async Task<LedgerBalance> BalanceOf(string userId, CancellationToken ct = default)
    {
        // AsNoTracking (Concurrency-F2 follow-on): UpsertBalanceAtomic writes via raw SQL, which bypasses
        // EF's change tracker entirely. Without AsNoTracking, a SECOND BalanceOf call in the same
        // DbContext scope as an earlier one would hit EF's identity map and get back the FIRST call's now
        // stale tracked LedgerBalanceEntity instance instead of the fresh row the raw SQL just wrote —
        // the read would silently ignore its own Append/Reverse. A pure projection read has no business
        // being tracked in the first place.
        var balance = await db.LedgerBalances.AsNoTracking().SingleOrDefaultAsync(b => b.UserId == userId, ct);
        return balance is null
            ? new LedgerBalance(0, 0, 0)
            : new LedgerBalance(balance.Points, balance.Xp, balance.Svac);
    }

    private void Authorize(string action, ActorRef actor)
    {
        var decision = policyEngine.Authorize(actor, action, TargetRef.ForAction(action));
        if (!decision.IsAllowed)
        {
            throw new UnauthorizedAccessException($"4A denied \"{action}\" for actor {actor} — {decision.GetType().Name}.");
        }
    }

    /// <summary>
    /// Atomically increments (or, isReversal, decrements) the balance projection's running totals with a
    /// single INSERT ... ON CONFLICT DO UPDATE SET points = points + delta statement — never a
    /// read-then-write (Concurrency-F2, SECURITY_REVIEW_S1.md). Executes immediately (raw SQL is never
    /// "staged" the way EF change-tracking is), so callers must run this inside their own explicit
    /// transaction alongside the ledger entry + event writes it must commit atomically with.
    /// </summary>
    private async Task UpsertBalanceAtomic(string userId, LedgerMovement movement, bool isReversal, CancellationToken ct)
    {
        long deltaPoints = isReversal ? -movement.Points : movement.Points;
        long deltaXp = isReversal ? -movement.Xp : movement.Xp;
        var deltaSvac = isReversal ? -movement.Svac : movement.Svac;

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO core.ledger_balances (user_id, points, xp, svac, watermark, updated_at)
            VALUES ({userId}, {deltaPoints}, {deltaXp}, {deltaSvac}, 0, now())
            ON CONFLICT (user_id) DO UPDATE SET
                points = core.ledger_balances.points + {deltaPoints},
                xp = core.ledger_balances.xp + {deltaXp},
                svac = core.ledger_balances.svac + {deltaSvac},
                updated_at = now()
            """, ct);
    }

    private static string MintId(DateTimeOffset now)
    {
        var randomness = new byte[10];
        RandomNumberGenerator.Fill(randomness);
        var body = Ulid.Encode(now.ToUnixTimeMilliseconds(), randomness);
        return Ulid.WithPrefix(IdPrefixes.Ledger, body);
    }
}
