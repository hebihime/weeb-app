using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Region;

namespace Svac.DomainCore.Region;

/// <summary>
/// DevSeams deterministic region resolver (SLICE_S1_CONTRACT.md §1b, §9): every actor resolves to the
/// same fixed region, clearly not a real geo-inference — real resolution is account-declared (S10) >
/// signup capture (S3) > edge-inferred (S9). Falls back to <see cref="RegionCode.Unknown"/> for anonymous
/// actors, matching the `privacy.region_fallback` posture (unknown region gets the strictest treatment).
/// </summary>
[DevSeamsOnly]
public sealed class DevSeamsRegionResolver : IRegionResolver
{
    public Task<(RegionCode Region, RegionSource Source)> Resolve(ActorRef actor, CancellationToken ct = default) =>
        actor.Kind == ActorKind.System
            ? Task.FromResult((RegionCode.Unknown, RegionSource.System))
            : Task.FromResult((RegionCode.Unknown, RegionSource.EdgeInferred));
}
