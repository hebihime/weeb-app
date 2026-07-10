using Svac.DomainCore.Contracts.Ids;

namespace Svac.DomainCore.Contracts.Region;

/// <summary>
/// Seam-now region resolution (SLICE_S1_CONTRACT.md §1b, §9): account-declared (S10) > signup capture
/// (S3) > edge-inferred fallback, with RegionSource provenance recorded so upgrading resolution never
/// rewrites history. DevSeams supplies a deterministic impl for local dev.
/// </summary>
public interface IRegionResolver
{
    public Task<(RegionCode Region, RegionSource Source)> Resolve(ActorRef actor, CancellationToken ct = default);
}
