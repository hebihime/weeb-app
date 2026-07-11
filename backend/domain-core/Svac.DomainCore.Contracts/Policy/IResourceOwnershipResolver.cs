using Svac.DomainCore.Contracts.Ids;

namespace Svac.DomainCore.Contracts.Policy;

/// <summary>
/// Resolves a resource id's owning actor for a <see cref="TargetRule.OwnedResourceRule"/> row (PHASE_2A_
/// SUBSTRATE.md §1, SLICE_S3_CONTRACT.md §3a). Registered per resource type by the owning module (e.g.
/// identity registers session/device/export) — zero registered at S1/S2, so <see cref="PolicyEngine"/>'s
/// OwnedResource axis is a structural no-op until a real resolver lands.
///
/// An unknown resource id resolving to null is deliberate: "unknown id ⇒ owner null ⇒ deny-as-absence —
/// nonexistent and foreign are ONE branch" (SLICE_S3_CONTRACT.md §3a), discharging the SilentRej-L4
/// timing-channel finding structurally, not by convention.
/// </summary>
public interface IResourceOwnershipResolver
{
    /// <summary>The resource type this resolver answers for (matches a <see cref="TargetRule.OwnedResourceRule.ResourceType"/> value).</summary>
    public string ResourceType { get; }

    /// <summary>Returns the resource's owning actor id, or null if the id is unknown/nonexistent.</summary>
    public Task<OpaqueId?> OwnerOf(string resourceId, CancellationToken ct = default);
}
