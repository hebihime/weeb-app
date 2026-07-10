using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Con;

namespace Svac.DomainCore.Con;

/// <summary>
/// DevSeams placeholder con-timezone/cutoff resolver (SLICE_S1_CONTRACT.md §1b, §5, §9). Returns a
/// fixed, clearly-labeled UTC/04:00 placeholder for ANY conId — "No fake con data ever": this is an
/// explicit, obviously-not-real placeholder, never dressed up as a plausible con timezone. S8 supplies
/// real con values from the con registry.
/// </summary>
[DevSeamsOnly]
public sealed class DevSeamsConDayResolver : IConDayResolver
{
    public Task<(TimeZoneInfo TimeZone, TimeOnly Cutoff)> ResolveForCon(string conId, CancellationToken ct = default) =>
        Task.FromResult((TimeZoneInfo.Utc, new TimeOnly(4, 0)));
}
