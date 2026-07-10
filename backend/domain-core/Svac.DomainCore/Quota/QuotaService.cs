using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Quota;
using Svac.DomainCore.Deterministic;
using Svac.DomainCore.Persistence;

namespace Svac.DomainCore.Quota;

/// <summary>
/// The 10A quota verb over Postgres, NOT Redis (SLICE_S1_CONTRACT.md §1b, §2, §5): transactional with
/// the guarded action, one moving part fewer. Consume is a single atomic UPSERT ... WHERE consumed &lt;
/// cap, idempotent-under-race by construction — two concurrent callers racing the same window can never
/// both succeed past the cap. Zero live quota keys exist at S1 (§5): the base cap is resolved via a 9A
/// config convention key (`quota.&lt;quotaKey&gt;.cap`) that the FIRST real consuming slice (S14+)
/// registers; calling Consume for a key with no such config entry is a caller bug at this substrate
/// phase and throws rather than silently defaulting to unbounded or zero.
/// </summary>
public sealed class QuotaService(CoreDbContext db, IConfigRegistry configRegistry, IEnumerable<ICapModifier> capModifiers) : IQuotaService
{
    public async Task<QuotaResult> Consume(ActorRef actor, string quotaKey, QuotaContext context, CancellationToken ct = default)
    {
        var baseCap = await configRegistry.GetValue<int>($"quota.{quotaKey}.cap", ct);
        var cap = capModifiers.Aggregate(baseCap, (running, modifier) => modifier.Modify(running, actor));

        var windowKey = ConDayWindow.WindowKey(quotaKey, context.ResetSpec, context.TimeZone, context.ConDayCutoff, context.Now);
        var resetsAt = ConDayWindow.NextResetAt(context.ResetSpec, context.TimeZone, context.ConDayCutoff, context.Now);
        var actorRefText = actor.ToString();

        // Atomic UPSERT ... WHERE consumed < cap (§2 quota_counters comment): ON CONFLICT DO UPDATE with
        // a WHERE clause on the conflict target row so two racing callers against the same window never
        // both observe "room available" — exactly one of them wins the increment past the cap boundary.
        //
        // Concurrency-F3 (SECURITY_REVIEW_S1.md): the INSERT branch alone had no cap guard — cap=0 (an
        // ops kill-switch, a perfectly valid 9A value) still granted the FIRST Consume of every window
        // because ON CONFLICT's WHERE only ever applies once a row already exists. `INSERT ... SELECT
        // ... WHERE {cap} > 0` makes the INSERT source produce zero rows when the cap itself is zero, so
        // there is nothing for ON CONFLICT to even consider — the row-producing predicate and the
        // row-updating predicate now agree on one boundary instead of the insert path silently having none.
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO core.quota_counters (actor_ref, quota_key, window_key, consumed, updated_at)
            SELECT {actorRefText}, {quotaKey}, {windowKey}, 1, now()
            WHERE {cap} > 0
            ON CONFLICT (actor_ref, quota_key, window_key)
            DO UPDATE SET consumed = core.quota_counters.consumed + 1, updated_at = now()
            WHERE core.quota_counters.consumed < {cap}
            """, ct);

        if (rows == 0)
        {
            return new QuotaResult.Limited(new LimitReached(quotaKey, MessageKeys.LimitReachedGeneric, resetsAt, PremiumExtends: false));
        }

        var consumedNow = await db.QuotaCounters
            .Where(q => q.ActorRef == actorRefText && q.QuotaKey == quotaKey && q.WindowKey == windowKey)
            .Select(q => q.Consumed)
            .SingleAsync(ct);

        // No Math.Max mask (Concurrency-F3): with the cap>0 guard above, `rows == 1` is only ever reached
        // when the row-producing/updating predicate already proved consumedNow <= cap, so cap - consumedNow
        // can never go negative here — a defensive clamp would have hidden exactly the overshoot this fix removes.
        return new QuotaResult.Ok(new Consumed(cap - consumedNow, resetsAt));
    }
}
