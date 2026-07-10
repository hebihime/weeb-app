using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Behavioral;
using Svac.DomainCore.Contracts.Streams;

namespace Svac.DomainCore.Behavioral;

/// <summary>
/// The one door onto the behavioral stream (SLICE_S1_CONTRACT.md §1b), implemented directly over the 3A
/// substrate — Emit is just an Append onto StreamType.Behavioral keyed by the calling actor. "Verified
/// received" is proven by reading the row back and asserting the consumer watermark advanced
/// (backend/e2e/substrate.e2e.mjs), never emit-only. Named without a "Stream" suffix (CA1711 — the
/// contract's IBehavioralStream interface name is suppressed for the same reason at its declaration;
/// this concrete class has no such contract-text obligation, so it simply avoids the collision).
/// </summary>
public sealed class SubstrateBehavioralEmitter(Svac.DomainCore.Contracts.Streams.IEventStore eventStore) : IBehavioralStream
{
    public Task Emit(string eventName, string payloadJson, RequestContext ctx, CancellationToken ct = default) =>
        eventStore.Append(StreamType.Behavioral, streamId: ctx.Actor.Id.Value, eventType: eventName, payloadJson, ctx, ExpectedVersion.AnyVersion, ct);
}
