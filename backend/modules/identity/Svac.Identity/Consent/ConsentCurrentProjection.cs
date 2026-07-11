using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts.Streams;
using Svac.Identity.Persistence;

namespace Svac.Identity.Consent;

/// <summary>Deserialization shape of a `consent.recorded` event payload (SLICE_S3_CONTRACT.md §1b: frozen <c>{consent_key, version, decision, surface}</c>).</summary>
public sealed record ConsentRecordedPayload(
    [property: JsonPropertyName("consent_key")] string ConsentKey,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("surface")] string Surface);

/// <summary>
/// The <c>identity.consent_current</c> rebuildable projection (SLICE_S3_CONTRACT.md §2/§8) — identity's
/// FIRST 3A stream consumer. CanHandle is narrow ("consent.recorded" only), so any OTHER event type ever
/// appended to the shared <c>events_consent</c> stream is a structural no-op here (§8 clause 7's
/// foreign-event skip), never a silently-stuck consumer.
/// </summary>
public sealed class ConsentCurrentProjection(IdentityDbContext db) : IProjection
{
    public string ConsumerId => "identity.consent_current";
    public StreamType Stream => StreamType.Consent;

    public bool CanHandle(string eventType) => eventType == "consent.recorded";

    public async Task Apply(RecordedEvent recordedEvent, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Deserialize<ConsentRecordedPayload>(recordedEvent.PayloadJson!)
            ?? throw new InvalidOperationException($"consent.recorded event \"{recordedEvent.EventId}\" has an unparseable payload.");

        var accountId = recordedEvent.StreamId;
        var existing = await db.ConsentCurrent.SingleOrDefaultAsync(
            c => c.AccountId == accountId && c.ConsentKind == payload.ConsentKey, ct);

        if (existing is null)
        {
            db.ConsentCurrent.Add(new ConsentCurrentEntity
            {
                AccountId = accountId,
                ConsentKind = payload.ConsentKey,
                Version = payload.Version,
                Status = payload.Decision,
                Surface = payload.Surface,
                DecidedAt = recordedEvent.OccurredAt,
                Region = recordedEvent.Region,
                LawfulBasis = recordedEvent.LawfulBasis,
            });
        }
        else
        {
            existing.Version = payload.Version;
            existing.Status = payload.Decision;
            existing.Surface = payload.Surface;
            existing.DecidedAt = recordedEvent.OccurredAt;
            existing.Region = recordedEvent.Region;
            existing.LawfulBasis = recordedEvent.LawfulBasis;
        }

        await db.SaveChangesAsync(ct);
    }
}
