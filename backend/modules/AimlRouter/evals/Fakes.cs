using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Quota;
using Svac.DomainCore.Contracts.Streams;

namespace Svac.AimlRouter.Evals;

/// <summary>
/// Minimal in-memory fakes for this project's own <see cref="AimlRouterService"/> wiring
/// (<c>FailoverUnderRealBackend_...</c>) — deliberately NOT a project reference to
/// <c>backend/tests/Svac.Tests.AimlRouter</c> (that project is the gate lane's own tree; this eval
/// project stays independent per SLICE_PLAYBOOK.md's "independent test + eval suites" rule). Substrate
/// dependencies (config/quota/event-store) stay fake here — the point of this project's failover drill is
/// proving the PROVIDER-side mechanics against the real local CLI, not re-proving substrate wiring the
/// gate lane already covers.
/// </summary>
internal sealed class FakeConfigRegistry : IConfigRegistry
{
    private readonly Dictionary<string, object> _values = new();

    public FakeConfigRegistry With<T>(string key, T value)
    {
        _values[key] = value!;
        return this;
    }

    public Task<T> GetValue<T>(string key, CancellationToken ct = default) =>
        _values.TryGetValue(key, out var v)
            ? Task.FromResult((T)v)
            : throw new KeyNotFoundException($"FakeConfigRegistry has no seeded value for \"{key}\".");

    public Task SetValue<T>(string key, T value, string reason, ActorRef actor, RequestContext ctx, CancellationToken ct = default)
    {
        _values[key] = value!;
        return Task.CompletedTask;
    }
}

internal sealed class FakeQuotaService : IQuotaService
{
    public Task<QuotaResult> Consume(ActorRef actor, string quotaKey, QuotaContext context, CancellationToken ct = default) =>
        Task.FromResult<QuotaResult>(new QuotaResult.Ok(new Consumed(Remaining: 9999, ResetsAt: context.Now.AddDays(1))));
}

internal sealed class FakeEventStore : IEventStore
{
    public Task<RecordedEvent> Append(StreamType stream, string streamId, string eventType, string? payloadJson, RequestContext ctx, ExpectedVersion expectedVersion, CancellationToken ct = default) =>
        Task.FromResult(new RecordedEvent(
            EventId: "evt_fake", StreamId: streamId, Seq: 1, EventType: eventType, PayloadJson: payloadJson,
            ReversalOf: null, Tombstone: false, ActorRef: ctx.Actor.ToString(), Region: ctx.Region.ToString(),
            LawfulBasis: ctx.LawfulBasisVariant.Key, OccurredAt: DateTimeOffset.UtcNow, RecordedAt: DateTimeOffset.UtcNow));

    public Task<RecordedEvent> Reverse(StreamType stream, string eventId, string reason, RequestContext ctx, CancellationToken ct = default) =>
        throw new NotSupportedException("FakeEventStore.Reverse is not exercised by this eval.");

    public Task Tombstone(StreamType stream, string eventId, string purgeClass, RequestContext ctx, CancellationToken ct = default) =>
        throw new NotSupportedException("FakeEventStore.Tombstone is not exercised by this eval.");

#pragma warning disable CS1998 // no await needed in a synchronous fake enumerable
    public async IAsyncEnumerable<RecordedEvent> ReadStream(StreamType stream, string streamId, long fromSeq = 0, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield break;
    }
#pragma warning restore CS1998

    public Task Replay(StreamType stream, string consumerId, IProjection projection, CancellationToken ct = default) =>
        throw new NotSupportedException("FakeEventStore.Replay is not exercised by this eval.");
}
