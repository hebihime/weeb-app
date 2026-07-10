using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Deterministic;

namespace Svac.DomainCore.Contracts.Quota;

/// <summary>Context a Consume call resolves its window against (SLICE_S1_CONTRACT.md §5).</summary>
public sealed record QuotaContext(ResetSpec ResetSpec, TimeZoneInfo TimeZone, TimeOnly ConDayCutoff, DateTimeOffset Now);

/// <summary>The one closed deny shape's domain-side result — token law 4, identical rendering everywhere.</summary>
public sealed record Consumed(int Remaining, DateTimeOffset ResetsAt);

/// <summary>
/// A cap modifier in the pipeline: base cap (9A) x ICapModifier[]. Premium / reputation-tier / mode
/// modifiers are identity functions at S1 (seams; real impls land S23/S16/S19).
/// </summary>
public interface ICapModifier
{
    public int Modify(int baseCap, ActorRef actor);
}

/// <summary>
/// The 10A quota verb (SLICE_S1_CONTRACT.md §1b, §5). One verb, one deny shape. No live keys exist at
/// S1 — this is the mechanism every later slice's key resolves against.
/// </summary>
public interface IQuotaService
{
    public Task<QuotaResult> Consume(ActorRef actor, string quotaKey, QuotaContext context, CancellationToken ct = default);
}

/// <summary>Closed result union: Consumed | LimitReached (SLICE_S1_CONTRACT.md §1b).</summary>
public abstract record QuotaResult
{
    public sealed record Ok(Consumed Consumed) : QuotaResult;
    public sealed record Limited(LimitReached LimitReached) : QuotaResult;
}
