using Svac.DomainCore.Contracts.Con;

namespace Svac.DomainCore.Con;

/// <summary>The PROD default (SLICE_S1_CONTRACT.md §1b, §9, L18 fail-closed): the con registry (S8) does not exist yet.</summary>
public sealed class ThrowingConDayResolver : IConDayResolver
{
    public Task<(TimeZoneInfo TimeZone, TimeOnly Cutoff)> ResolveForCon(string conId, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "IConDayResolver has no real backend configured (SLICE_S1_CONTRACT.md §9) — the con registry " +
            "(S8) does not exist yet. This throw is deliberate fail-closed behavior in a non-DevSeams " +
            "environment, not a bug.");
}
