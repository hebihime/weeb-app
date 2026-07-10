namespace Svac.DomainCore.Contracts.Streams;

/// <summary>
/// A projection consuming a replayed stream (SLICE_S1_CONTRACT.md §1b, §8 clause 7). CanHandle decides
/// whether Apply runs for a given event type (contract pseudocode names this Handles(); renamed for
/// CA1716 — "Handles" collides with a VB.NET reserved keyword and this repo's analyzers treat that as a
/// build-breaking warning). IEventStore.Replay advances the watermark regardless, so a foreign event on
/// a shared stream is a structural no-op, never a silently-stuck consumer.
/// </summary>
public interface IProjection
{
    public string ConsumerId { get; }
    public StreamType Stream { get; }
    public bool CanHandle(string eventType);
    public Task Apply(RecordedEvent recordedEvent, CancellationToken ct = default);
}
