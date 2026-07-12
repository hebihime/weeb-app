using System.Text.Json;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Consent;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Contracts.Streams;

namespace Svac.Identity.Consent;

/// <summary>
/// Maps the closed <see cref="ConsentKind"/> union to its frozen wire/event `consent_key` string
/// (SLICE_S3_CONTRACT.md §1b payload: <c>{consent_key, version, decision, surface}</c>).
/// </summary>
public static class ConsentKindKeys
{
    public static string KeyFor(ConsentKind kind) => kind switch
    {
        ConsentKind.AgeAttestation18PlusKind => "age_attestation_18_plus",
        ConsentKind.TermsAcceptanceKind => "terms_acceptance",
        ConsentKind.PushCategoryKind push => $"push_category_{(int)push.Category}",
        ConsentKind.IrlAccessKind => "irl_access",
        ConsentKind.BackgroundLocationKind => "background_location",
        ConsentKind.SpecialCategoryIdentityKind => "special_category_identity",
        ConsentKind.IdentityVerificationKind => "identity_verification",
        ConsentKind.MarketingKind => "marketing",
        _ => throw new InvalidOperationException($"unhandled ConsentKind case: {kind.GetType().Name}"),
    };
}

/// <summary>
/// Identity's <see cref="IConsentLedgerWriter"/> implementation (SLICE_S3_CONTRACT.md §1b) — appends ONE
/// `consent.recorded` event to <c>events_consent</c> in the caller's own transaction (the caller's ambient
/// <see cref="IEventStore"/>/CoreDbContext tx — Append stages rather than immediately saving, so it joins
/// whatever SaveChanges the caller's own signup/push-consent handler issues), then drives an immediate
/// Replay pass for both identity-owned projections (<see cref="ConsentCurrentProjection"/>, <see
/// cref="PushCategoryConsentProjection"/>) so the read-back the live E2E performs observes the write
/// without waiting on a separate background worker. The projections are still independently rebuildable
/// (§8 clause 7) — this is a convenience trigger, not the only way they can ever run.
/// </summary>
public sealed class IdentityConsentLedgerWriter(
    IEventStore eventStore,
    ConsentCurrentProjection consentCurrentProjection,
    PushCategoryConsentProjection pushCategoryConsentProjection)
    : IConsentLedgerWriter
{
    public async Task Record(SubjectRef subject, ConsentKind kind, string version, string surface, ConsentDecision decision, RequestContext ctx, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            consent_key = ConsentKindKeys.KeyFor(kind),
            version,
            decision = decision.ToString().ToLowerInvariant(),
            surface,
        });

        await eventStore.Append(StreamType.Consent, streamId: subject.ResourceId, eventType: "consent.recorded", payloadJson: payload, ctx, ExpectedVersion.AnyVersion, ct);

        await eventStore.Replay(StreamType.Consent, consentCurrentProjection.ConsumerId, consentCurrentProjection, ct);
        await eventStore.Replay(StreamType.Consent, pushCategoryConsentProjection.ConsumerId, pushCategoryConsentProjection, ct);
    }
}
