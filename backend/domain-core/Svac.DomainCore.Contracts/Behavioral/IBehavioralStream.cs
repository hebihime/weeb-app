using System.Diagnostics.CodeAnalysis;

namespace Svac.DomainCore.Contracts.Behavioral;

/// <summary>
/// The one door onto the behavioral stream (SLICE_S1_CONTRACT.md §1b). Every slice's "metric wired at
/// build time" goes through this, so "verified received" is one integration-test pattern forever: emit
/// on a request path, then read the row back and assert the consumer watermark advanced.
/// </summary>
/// <remarks>
/// CA1711 flags type names ending in "Stream" on the assumption of a collision with System.IO.Stream.
/// "Behavioral stream" is this product's own 3A domain vocabulary (SLICE_S1_CONTRACT.md §1b/§2), not an
/// IO stream, and the contract's public-surface pseudocode names this type exactly this — suppressed
/// with the reasoning recorded here rather than renamed away from the ratified contract text.
/// </remarks>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Contract-mandated name (SLICE_S1_CONTRACT.md §1b); 'stream' is this product's 3A domain term, not System.IO.Stream.")]
public interface IBehavioralStream
{
    public Task Emit(string eventName, string payloadJson, RequestContext ctx, CancellationToken ct = default);
}
