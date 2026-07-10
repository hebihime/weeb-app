using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Persistence;

namespace Svac.DomainCore.Config;

/// <summary>
/// The 9A typed config seam over Postgres (SLICE_S1_CONTRACT.md §1b, §4, §2). GetValue is typed;
/// SetValue appends an Audit-stream event in the SAME transaction as the write — proven by test at S1,
/// rendered by the ops desk at S5.
/// </summary>
public sealed class ConfigRegistry(CoreDbContext db, Svac.DomainCore.Contracts.Streams.IEventStore eventStore) : IConfigRegistry
{
    public async Task<T> GetValue<T>(string key, CancellationToken ct = default)
    {
        var row = await db.ConfigEntries.SingleOrDefaultAsync(e => e.Key == key, ct)
            ?? throw new KeyNotFoundException($"9A config key \"{key}\" is not registered — seed it via the manifest loader before reading it.");
        var value = JsonSerializer.Deserialize<T>(row.ValueJson)
            ?? throw new InvalidOperationException($"9A config key \"{key}\" stored value could not deserialize to {typeof(T).Name}.");
        return value;
    }

    public async Task SetValue<T>(string key, T value, string reason, ActorRef actor, RequestContext ctx, CancellationToken ct = default)
    {
        var row = await db.ConfigEntries.SingleOrDefaultAsync(e => e.Key == key, ct)
            ?? throw new KeyNotFoundException($"9A config key \"{key}\" is not registered — a Set may only update an already-declared key.");

        if (row.RequiresReason && string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException($"9A config key \"{key}\" requires a reason on every Set.", nameof(reason));
        }

        var valueJson = JsonSerializer.Serialize(value);
        // PII-S2-F1/TRUST-BREAK-1 (SECURITY_REVIEW_S2.md): set-time bounds validation — throws BEFORE the
        // tracked row is mutated, so a rejected Set leaves the stored value (and the audit stream) byte-
        // for-byte untouched. Every OTHER 9A key has no bounds rule registered and passes through unchanged.
        await ConfigBounds.ValidateAsync(key, valueJson, db, ct);

        row.ValueJson = valueJson;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedBy = actor.ToString();

        var payload = JsonSerializer.Serialize(new { key, value, reason, actor = actor.ToString() });
        // Same tx: the config-row mutation above is tracked but not yet saved; IEventStore.Append's own
        // SaveChangesAsync call commits BOTH the config row mutation and the audit event together in one
        // implicit EF transaction (§4: "every config change ... appends an Audit-stream event in the
        // same tx"). Do not call SaveChangesAsync a second time here — that would split the tx in two.
        await eventStore.Append(StreamType.Audit, streamId: key, eventType: "config.set", payloadJson: payload, ctx, ExpectedVersion.AnyVersion, ct);
    }
}
