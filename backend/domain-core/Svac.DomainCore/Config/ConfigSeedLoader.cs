using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Persistence;

namespace Svac.DomainCore.Config;

/// <summary>
/// One manifest row, deserialized from a module's additive config manifest file (SLICE_S1_CONTRACT.md §4).
///
/// <see cref="Bounds"/> (OPS-3, SECURITY_REVIEW_S3.md): optional, a JSON 2-element array
/// <c>[min, max]</c> naming the key's declared numeric bounds — seeded verbatim into
/// <c>ConfigEntryEntity.BoundsJson</c> so <c>ConfigBounds.ValidateAsync</c>'s generic bounds check
/// (every key, not a hardcoded switch) has real data to read on the actual <c>ConfigRegistry.SetValue</c>
/// write path. Absent for a key means "no bounds rule" — SetValue proceeds unchanged, same as before this
/// fix.
/// </summary>
public sealed record ConfigManifestEntry(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] JsonElement Value,
    [property: JsonPropertyName("requiresReason")] bool RequiresReason,
    [property: JsonPropertyName("consumer")] string Consumer,
    [property: JsonPropertyName("bounds")] JsonElement? Bounds = null);

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
            var staged = new ConfigEntryEntity
            {
                Key = entry.Key,
                Type = entry.Type,
                ValueJson = entry.Value.GetRawText(),
                Scope = entry.Scope,
                Gate = null,
                // OPS-3 (SECURITY_REVIEW_S3.md): seed the manifest's declared bounds verbatim (null when
                // the manifest carries none for this key) — this is the data ConfigBounds.ValidateAsync's
                // generic check reads on every real SetValue call, not just the 3 hardcoded AimlRouter keys.
                BoundsJson = entry.Bounds.HasValue && entry.Bounds.Value.ValueKind != JsonValueKind.Undefined
                    ? entry.Bounds.Value.GetRawText()
                    : null,
                RequiresReason = entry.RequiresReason,
                UpdatedAt = now,
                UpdatedBy = ctx.Actor.ToString(),
            };
            db.ConfigEntries.Add(staged);

            var payload = JsonSerializer.Serialize(new { key = entry.Key, value = entry.Value, seeded = true });
            try
            {
                // The config row and its audit event ride ONE SaveChanges inside Append (same-tx outbox),
                // so both commit or neither does.
                await eventStore.Append(StreamType.Audit, streamId: entry.Key, eventType: "config.seeded", payloadJson: payload, ctx, ExpectedVersion.AnyVersion, ct);
                seeded++;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Concurrency-F6 (SECURITY_REVIEW_S1.md): two hosts booting concurrently both saw "key
                // absent" and staged the same config_entries PK; the loser's SaveChanges (inside Append)
                // hits 23505. That IS the idempotent outcome the doc-comment promises — the winner's row
                // AND its single audit event stand (both rolled back atomically for the loser, so no
                // orphan audit event). Detach our doomed staged row and treat the key as already-seeded;
                // a raced boot must never crash. Mirrors PostgresEventStore.Append's own seq-race catch.
                db.Entry(staged).State = EntityState.Detached;
                if (!await db.ConfigEntries.AnyAsync(e => e.Key == entry.Key, ct))
                {
                    throw; // some OTHER unique violation — the key is still absent; never swallow it.
                }
            }
        }

        return seeded;
    }

    /// <summary>
    /// True if <paramref name="ex"/> is (or wraps) Postgres' 23505 unique-violation. EF Core's default
    /// execution strategy can re-wrap the provider exception, so the real PostgresException may be the
    /// direct exception or its InnerException — checks both (same shape as PostgresEventStore).
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        (ex.InnerException as Npgsql.PostgresException ?? ex.InnerException?.InnerException as Npgsql.PostgresException)?.SqlState
            == Npgsql.PostgresErrorCodes.UniqueViolation;
}
