using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Persistence;

namespace Svac.DomainCore.Config;

/// <summary>One manifest row, deserialized from a module's additive config manifest file (SLICE_S1_CONTRACT.md §4).</summary>
public sealed record ConfigManifestEntry(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] JsonElement Value,
    [property: JsonPropertyName("requiresReason")] bool RequiresReason,
    [property: JsonPropertyName("consumer")] string Consumer);

/// <summary>Deserialization shape of a manifest file's top-level object.</summary>
public sealed record ConfigManifestFile([property: JsonPropertyName("entries")] IReadOnlyList<ConfigManifestEntry> Entries);

/// <summary>
/// Seeds 9A config rows from additive manifest files (SLICE_S1_CONTRACT.md §1b/§4: "S5 seeds the v0
/// batch as data through S1's manifest format, not code"). Idempotent: re-running on an already-seeded
/// key is a no-op, so union-merging a second module's manifest file never clobbers this one's rows.
/// A config key with no registered consumer fails a lint (§4) — enforced separately in
/// Svac.Tests.Architecture by reading ConfigManifestEntry.Consumer off every loaded manifest.
/// </summary>
public sealed class ConfigSeedLoader(CoreDbContext db, Svac.DomainCore.Contracts.Streams.IEventStore eventStore)
{
    public async Task<int> SeedFromFile(string manifestPath, RequestContext ctx, CancellationToken ct = default)
    {
        var json = await File.ReadAllTextAsync(manifestPath, ct);
        var manifest = JsonSerializer.Deserialize<ConfigManifestFile>(json)
            ?? throw new InvalidOperationException($"config manifest \"{manifestPath}\" deserialized to null.");

        var seeded = 0;
        foreach (var entry in manifest.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Consumer))
            {
                throw new InvalidOperationException(
                    $"config manifest \"{manifestPath}\" declares key \"{entry.Key}\" with no consumer — a config key with no registered consumer fails the lint (§4).");
            }

            var already = await db.ConfigEntries.SingleOrDefaultAsync(e => e.Key == entry.Key, ct);
            if (already is not null)
            {
                continue; // idempotent: seeding never clobbers an existing row (e.g. an ops-desk edit).
            }

            var now = DateTimeOffset.UtcNow;
            db.ConfigEntries.Add(new ConfigEntryEntity
            {
                Key = entry.Key,
                Type = entry.Type,
                ValueJson = entry.Value.GetRawText(),
                Scope = entry.Scope,
                Gate = null,
                BoundsJson = null,
                RequiresReason = entry.RequiresReason,
                UpdatedAt = now,
                UpdatedBy = ctx.Actor.ToString(),
            });

            var payload = JsonSerializer.Serialize(new { key = entry.Key, value = entry.Value, seeded = true });
            await eventStore.Append(StreamType.Audit, streamId: entry.Key, eventType: "config.seeded", payloadJson: payload, ctx, ExpectedVersion.AnyVersion, ct);
            seeded++;
        }

        return seeded;
    }
}
