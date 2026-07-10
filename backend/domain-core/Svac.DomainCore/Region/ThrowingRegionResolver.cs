using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Region;

namespace Svac.DomainCore.Region;

/// <summary>
/// The PROD default (SLICE_S1_CONTRACT.md §1b, §9, L18 fail-closed): real region resolution is
/// account-declared (S10) &gt; signup capture (S3) &gt; edge-inferred (S9) — none of which exist yet.
/// A production host with DevSeams off has no real backend to resolve region from, so it throws rather
/// than silently stamping every actor with a fabricated region.
/// </summary>
public sealed class ThrowingRegionResolver : IRegionResolver
{
    public Task<(RegionCode Region, RegionSource Source)> Resolve(ActorRef actor, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "IRegionResolver has no real backend configured (SLICE_S1_CONTRACT.md §9) — signup capture " +
            "(S3) / account-declared (S10) / edge-inferred (S9) resolution does not exist yet. This throw " +
            "is deliberate fail-closed behavior in a non-DevSeams environment, not a bug.");
}
