using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts.Streams;
using Svac.Identity.Persistence;

namespace Svac.Identity.Consent;

/// <summary>
/// The <c>identity.push_category_consents</c> rebuildable projection (SLICE_S3_CONTRACT.md §2/§8) —
/// identity's SECOND 3A stream consumer, sharing the same <c>events_consent</c> stream as <see
/// cref="ConsentCurrentProjection"/>. CanHandle narrows further than that sibling: only "consent.recorded"
/// events whose <c>consent_key</c> is a <c>push_category_*</c> key advance this projection's own state —
/// every OTHER consent.recorded event (age attestation, terms acceptance, ...) on the SAME stream is a
/// structural no-op here (the non-vacuous §8 clause-7 foreign-event skip: a real, non-synthetic foreign
/// event, not a fixture-only one).
/// </summary>
public sealed class PushCategoryConsentProjection(IdentityDbContext db) : IProjection
{
    private const string PushCategoryKeyPrefix = "push_category_";

    public string ConsumerId => "identity.push_category_consents";
    public StreamType Stream => StreamType.Consent;

    public bool CanHandle(string eventType) => eventType == "consent.recorded";

    public async Task Apply(RecordedEvent recordedEvent, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Deserialize<ConsentRecordedPayload>(recordedEvent.PayloadJson!)
            ?? throw new InvalidOperationException($"consent.recorded event \"{recordedEvent.EventId}\" has an unparseable payload.");

        if (!payload.ConsentKey.StartsWith(PushCategoryKeyPrefix, StringComparison.Ordinal))
        {
            // Foreign-to-THIS-projection consent kind (age attestation, terms, ...) — the watermark still
            // advanced (IEventStore.Replay's job, not this method's); this projection's own state is
            // untouched, by design (§8 clause 7).
            return;
        }

        var category = short.Parse(payload.ConsentKey[PushCategoryKeyPrefix.Length..], System.Globalization.CultureInfo.InvariantCulture);
        var accountId = recordedEvent.StreamId;
        var enabled = payload.Decision == "granted";

        var existing = await db.PushCategoryConsents.SingleOrDefaultAsync(
            p => p.AccountId == accountId && p.Category == category, ct);

        if (existing is null)
        {
            db.PushCategoryConsents.Add(new PushCategoryConsentEntity
            {
                AccountId = accountId,
                Category = category,
                Enabled = enabled,
                UpdatedAt = recordedEvent.OccurredAt,
                Region = recordedEvent.Region,
                LawfulBasis = recordedEvent.LawfulBasis,
            });
        }
        else
        {
            existing.Enabled = enabled;
            existing.UpdatedAt = recordedEvent.OccurredAt;
            existing.Region = recordedEvent.Region;
            existing.LawfulBasis = recordedEvent.LawfulBasis;
        }

        await db.SaveChangesAsync(ct);
    }
}
