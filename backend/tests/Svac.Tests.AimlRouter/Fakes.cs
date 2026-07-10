using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Quota;
using Svac.DomainCore.Contracts.Streams;

namespace Svac.Tests.AimlRouter;

/// <summary>In-memory fakes for the gate lane (SLICE_S2_CONTRACT.md §1a: "deterministic, faked/seed providers, &lt;2s") — never Postgres, never a real CLI/API.</summary>
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
    public bool AlwaysLimited { get; set; }

    public Task<QuotaResult> Consume(ActorRef actor, string quotaKey, QuotaContext context, CancellationToken ct = default) =>
        Task.FromResult<QuotaResult>(AlwaysLimited
            ? new QuotaResult.Limited(new Svac.DomainCore.Contracts.Api.LimitReached(quotaKey, "limit_reached.generic", context.Now.AddDays(1), PremiumExtends: false))
            : new QuotaResult.Ok(new Consumed(Remaining: 9999, ResetsAt: context.Now.AddDays(1))));
}

internal sealed class FakeEventStore : IEventStore
{
    public List<(StreamType Stream, string StreamId, string EventType, string? PayloadJson)> Appended { get; } = new();

    public Task<RecordedEvent> Append(StreamType stream, string streamId, string eventType, string? payloadJson, RequestContext ctx, ExpectedVersion expectedVersion, CancellationToken ct = default)
    {
        Appended.Add((stream, streamId, eventType, payloadJson));
        var recorded = new RecordedEvent(
            EventId: $"evt_fake{Appended.Count:D26}"[..30],
            StreamId: streamId,
            Seq: Appended.Count,
            EventType: eventType,
            PayloadJson: payloadJson,
            ReversalOf: null,
            Tombstone: false,
            ActorRef: ctx.Actor.ToString(),
            Region: ctx.Region.ToString(),
            LawfulBasis: ctx.LawfulBasisVariant.Key,
            OccurredAt: DateTimeOffset.UtcNow,
            RecordedAt: DateTimeOffset.UtcNow);
        return Task.FromResult(recorded);
    }

    public Task<RecordedEvent> Reverse(StreamType stream, string eventId, string reason, RequestContext ctx, CancellationToken ct = default) =>
        throw new NotSupportedException("FakeEventStore.Reverse is not exercised by the AimlRouter gate lane.");

    public Task Tombstone(StreamType stream, string eventId, string purgeClass, RequestContext ctx, CancellationToken ct = default) =>
        throw new NotSupportedException("FakeEventStore.Tombstone is not exercised by the AimlRouter gate lane.");

#pragma warning disable CS1998 // no await needed in a synchronous fake enumerable
    public async IAsyncEnumerable<RecordedEvent> ReadStream(StreamType stream, string streamId, long fromSeq = 0, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var e in Appended.Where(a => a.StreamId == streamId))
        {
            yield return new RecordedEvent(
                EventId: "evt_fake", StreamId: e.StreamId, Seq: 0, EventType: e.EventType, PayloadJson: e.PayloadJson,
                ReversalOf: null, Tombstone: false, ActorRef: "System:sys_fake", Region: "ZZ", LawfulBasis: "n/a",
                OccurredAt: DateTimeOffset.UtcNow, RecordedAt: DateTimeOffset.UtcNow);
        }
    }
#pragma warning restore CS1998

    public Task Replay(StreamType stream, string consumerId, IProjection projection, CancellationToken ct = default) =>
        throw new NotSupportedException("FakeEventStore.Replay is not exercised by the AimlRouter gate lane.");
}
