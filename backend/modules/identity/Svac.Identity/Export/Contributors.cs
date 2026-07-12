using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts.Export;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Ledger;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Persistence;
using Svac.Identity.Persistence;

namespace Svac.Identity.Export;

// Thirteen IExportContributor implementations (SLICE_S3_CONTRACT.md §6b): every identity table that
// holds the subject's data, PLUS the five S1 stores S3 is the first real consumer of. Each writes ONE
// (path, schema-versioned JSON) entry to the sink and returns ExportDisposition.Contributes — the
// per-call mirror of the store's registered ExportRegistryState.Contributes cell in
// IdentityExportRegistrySource. Schema version 1 everywhere at this pass (no prior shape to version
// against yet).

/// <summary>identity.accounts (SLICE_S3_CONTRACT.md §6b) — birthdate is unprotected and included here (Art. 15 entitles the subject to their OWN data; this is the export artifact, never a `/v1/me` response — the birthdate-in-response arch scan targets C# response DTOs, not this runtime JSON payload).</summary>
public sealed class AccountExportContributor(IdentityDbContext db, IFieldEncryptor fieldEncryptor) : IExportContributor
{
    public string StoreKey => "identity.accounts";

    public async Task<ExportDisposition> ContributeAsync(SubjectRef subject, IExportSink sink, CancellationToken ct = default)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.AccountId == subject.ResourceId, ct);

        object payload;
        if (account is null)
        {
            payload = new { };
        }
        else
        {
            var birthdate = await fieldEncryptor.Unprotect(FieldEncryptionPurpose.Birthdate, account.BirthdateEnc, ct);
            payload = new
            {
                accountId = account.AccountId,
                handle = account.Handle,
                email = account.Email,
                emailVerifiedAt = account.EmailVerifiedAt,
                birthdate,
                attestedAdultAt = account.AttestedAdultAt,
                termsVersion = account.TermsVersion,
                fandomTag = account.FandomTag,
                avatarRef = account.AvatarRef,
                locale = account.Locale,
                accountState = account.AccountState,
                irlAccessState = account.IrlAccessState,
                createdAt = account.CreatedAt,
                lastActiveAt = account.LastActiveAt,
                region = account.Region,
                regionSource = account.RegionSource,
                lawfulBasis = account.LawfulBasis,
            };
        }

        await sink.WriteAsync(StoreKey, schemaVersion: 1, ExportJson.Serialize(payload), ct);
        return ExportDisposition.Contributes;
    }
}

/// <summary>identity.sessions (SLICE_S3_CONTRACT.md §6b) — token hashes excluded (secret material, never exported); metadata only.</summary>
public sealed class SessionsExportContributor(IdentityDbContext db) : IExportContributor
{
    public string StoreKey => "identity.sessions";

    public async Task<ExportDisposition> ContributeAsync(SubjectRef subject, IExportSink sink, CancellationToken ct = default)
    {
        var rows = await db.Sessions
            .Where(s => s.AccountId == subject.ResourceId)
            .Select(s => new
            {
                sessionId = s.SessionId,
                deviceId = s.DeviceId,
                createdAt = s.CreatedAt,
                lastSeenAt = s.LastSeenAt,
                accessExpiresAt = s.AccessExpiresAt,
                revokedAt = s.RevokedAt,
                revokeReason = s.RevokeReason,
                region = s.Region,
                lawfulBasis = s.LawfulBasis,
            })
            .ToListAsync(ct);

        await sink.WriteAsync(StoreKey, schemaVersion: 1, ExportJson.Serialize(rows), ct);
        return ExportDisposition.Contributes;
    }
}

/// <summary>identity.devices (SLICE_S3_CONTRACT.md §6b) — the subject's own registered push token is included (their own device data, not a third party's).</summary>
public sealed class DevicesExportContributor(IdentityDbContext db) : IExportContributor
{
    public string StoreKey => "identity.devices";

    public async Task<ExportDisposition> ContributeAsync(SubjectRef subject, IExportSink sink, CancellationToken ct = default)
    {
        var rows = await db.Devices
            .Where(d => d.AccountId == subject.ResourceId)
            .Select(d => new
            {
                deviceId = d.DeviceId,
                platform = d.Platform,
                pushToken = d.PushToken,
                pushTokenUpdatedAt = d.PushTokenUpdatedAt,
                createdAt = d.CreatedAt,
                lastSeenAt = d.LastSeenAt,
                revokedAt = d.RevokedAt,
                region = d.Region,
                lawfulBasis = d.LawfulBasis,
            })
            .ToListAsync(ct);

        await sink.WriteAsync(StoreKey, schemaVersion: 1, ExportJson.Serialize(rows), ct);
        return ExportDisposition.Contributes;
    }
}

/// <summary>identity.push_category_consents (SLICE_S3_CONTRACT.md §6b) — the rebuildable per-category projection.</summary>
public sealed class PushCategoryConsentsExportContributor(IdentityDbContext db) : IExportContributor
{
    public string StoreKey => "identity.push_category_consents";

    public async Task<ExportDisposition> ContributeAsync(SubjectRef subject, IExportSink sink, CancellationToken ct = default)
    {
        var rows = await db.PushCategoryConsents
            .Where(p => p.AccountId == subject.ResourceId)
            .Select(p => new
            {
                category = p.Category,
                enabled = p.Enabled,
                updatedAt = p.UpdatedAt,
                region = p.Region,
                lawfulBasis = p.LawfulBasis,
            })
            .ToListAsync(ct);

        await sink.WriteAsync(StoreKey, schemaVersion: 1, ExportJson.Serialize(rows), ct);
        return ExportDisposition.Contributes;
    }
}

/// <summary>identity.consent_current (SLICE_S3_CONTRACT.md §6b) — the rebuildable consent-kind projection (the "preference_answers always in export" structural form's own consent leg).</summary>
public sealed class ConsentCurrentExportContributor(IdentityDbContext db) : IExportContributor
{
    public string StoreKey => "identity.consent_current";

    public async Task<ExportDisposition> ContributeAsync(SubjectRef subject, IExportSink sink, CancellationToken ct = default)
    {
        var rows = await db.ConsentCurrent
            .Where(c => c.AccountId == subject.ResourceId)
            .Select(c => new
            {
                consentKind = c.ConsentKind,
                version = c.Version,
                status = c.Status,
                surface = c.Surface,
                decidedAt = c.DecidedAt,
                region = c.Region,
                lawfulBasis = c.LawfulBasis,
            })
            .ToListAsync(ct);

        await sink.WriteAsync(StoreKey, schemaVersion: 1, ExportJson.Serialize(rows), ct);
        return ExportDisposition.Contributes;
    }
}

/// <summary>identity.handle_history (SLICE_S3_CONTRACT.md §6b) — no consumer READ path exists in the contract assembly (moderation-visible only), but Art. 15 export is a DIFFERENT door than the product API and is not exempted by that structural absence.</summary>
public sealed class HandleHistoryExportContributor(IdentityDbContext db) : IExportContributor
{
    public string StoreKey => "identity.handle_history";

    public async Task<ExportDisposition> ContributeAsync(SubjectRef subject, IExportSink sink, CancellationToken ct = default)
    {
        var rows = await db.HandleHistory
            .Where(h => h.AccountId == subject.ResourceId)
            .OrderBy(h => h.ChangedAt)
            .Select(h => new
            {
                oldHandle = h.OldHandle,
                newHandle = h.NewHandle,
                changedAt = h.ChangedAt,
                region = h.Region,
                lawfulBasis = h.LawfulBasis,
            })
            .ToListAsync(ct);

        await sink.WriteAsync(StoreKey, schemaVersion: 1, ExportJson.Serialize(rows), ct);
        return ExportDisposition.Contributes;
    }
}

/// <summary>identity.export_jobs (SLICE_S3_CONTRACT.md §6b) — job METADATA only; the artifact bytea/manifest of a PRIOR export is deliberately excluded (an export containing a copy of itself is not the point of this receipt).</summary>
public sealed class ExportJobsExportContributor(IdentityDbContext db) : IExportContributor
{
    public string StoreKey => "identity.export_jobs";

    public async Task<ExportDisposition> ContributeAsync(SubjectRef subject, IExportSink sink, CancellationToken ct = default)
    {
        var rows = await db.ExportJobs
            .Where(e => e.AccountId == subject.ResourceId)
            .Select(e => new
            {
                exportId = e.ExportId,
                state = e.State,
                requestedAt = e.RequestedAt,
                readyAt = e.ReadyAt,
                expiresAt = e.ExpiresAt,
                region = e.Region,
                lawfulBasis = e.LawfulBasis,
            })
            .ToListAsync(ct);

        await sink.WriteAsync(StoreKey, schemaVersion: 1, ExportJson.Serialize(rows), ct);
        return ExportDisposition.Contributes;
    }
}

/// <summary>identity.deletion_jobs (SLICE_S3_CONTRACT.md §6b) — "as applicable": the pipeline is Pass 2b, so this yields zero rows today; the contributor exists now so a future deletion-job row is exported without a second registration.</summary>
public sealed class DeletionJobsExportContributor(IdentityDbContext db) : IExportContributor
{
    public string StoreKey => "identity.deletion_jobs";

    public async Task<ExportDisposition> ContributeAsync(SubjectRef subject, IExportSink sink, CancellationToken ct = default)
    {
        var rows = await db.DeletionJobs
            .Where(d => d.AccountId == subject.ResourceId)
            .Select(d => new
            {
                deletionId = d.DeletionId,
                state = d.State,
                requestedAt = d.RequestedAt,
                scheduledFor = d.ScheduledFor,
                exportOffered = d.ExportOffered,
                custodyHoldsFound = d.CustodyHoldsFound,
                executedAt = d.ExecutedAt,
                region = d.Region,
                lawfulBasis = d.LawfulBasis,
            })
            .ToListAsync(ct);

        await sink.WriteAsync(StoreKey, schemaVersion: 1, ExportJson.Serialize(rows), ct);
        return ExportDisposition.Contributes;
    }
}

/// <summary>ledger_entries (+ ILedger.BalanceOf) (SLICE_S3_CONTRACT.md §6b) — the derived balance rides alongside the raw entries so the export never needs a second read of core.ledger_balances (registered NotExportable, see CoreExportRegistrySource).</summary>
public sealed class LedgerEntriesExportContributor(CoreDbContext coreDb, ILedger ledger) : IExportContributor
{
    public string StoreKey => "ledger_entries";

    public async Task<ExportDisposition> ContributeAsync(SubjectRef subject, IExportSink sink, CancellationToken ct = default)
    {
        var entries = await coreDb.LedgerEntries
            .Where(e => e.UserId == subject.ResourceId)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new
            {
                id = e.Id,
                eventType = e.EventType,
                points = e.Points,
                xp = e.Xp,
                svac = e.Svac,
                questId = e.QuestId,
                crewId = e.CrewId,
                reversalOf = e.ReversalOf,
                createdAt = e.CreatedAt,
                region = e.Region,
                lawfulBasis = e.LawfulBasis,
            })
            .ToListAsync(ct);

        var balance = await ledger.BalanceOf(subject.ResourceId, ct);

        var payload = new
        {
            balance = new { points = balance.Points, xp = balance.Xp, svac = balance.Svac },
            entries,
        };

        await sink.WriteAsync(StoreKey, schemaVersion: 1, ExportJson.Serialize(payload), ct);
        return ExportDisposition.Contributes;
    }
}

/// <summary>events_ledger (SLICE_S3_CONTRACT.md §6b) — the append-only ledger event stream, read via the substrate's own IEventStore door (never a cross-module join).</summary>
public sealed class EventsLedgerExportContributor(IEventStore eventStore) : IExportContributor
{
    public string StoreKey => "events_ledger";

    public async Task<ExportDisposition> ContributeAsync(SubjectRef subject, IExportSink sink, CancellationToken ct = default)
    {
        await ExportEventStream.WriteStream(eventStore, StreamType.Ledger, subject.ResourceId, StoreKey, sink, redactStaffActors: false, ct);
        return ExportDisposition.Contributes;
    }
}

/// <summary>events_consent (SLICE_S3_CONTRACT.md §6b) — the append-only consent event stream (the ledger's own consent leg; the projection lives separately as identity.consent_current).</summary>
public sealed class EventsConsentExportContributor(IEventStore eventStore) : IExportContributor
{
    public string StoreKey => "events_consent";

    public async Task<ExportDisposition> ContributeAsync(SubjectRef subject, IExportSink sink, CancellationToken ct = default)
    {
        await ExportEventStream.WriteStream(eventStore, StreamType.Consent, subject.ResourceId, StoreKey, sink, redactStaffActors: false, ct);
        return ExportDisposition.Contributes;
    }
}

/// <summary>events_behavioral (SLICE_S3_CONTRACT.md §6b) — the subject's own funnel/telemetry events.</summary>
public sealed class EventsBehavioralExportContributor(IEventStore eventStore) : IExportContributor
{
    public string StoreKey => "events_behavioral";

    public async Task<ExportDisposition> ContributeAsync(SubjectRef subject, IExportSink sink, CancellationToken ct = default)
    {
        await ExportEventStream.WriteStream(eventStore, StreamType.Behavioral, subject.ResourceId, StoreKey, sink, redactStaffActors: false, ct);
        return ExportDisposition.Contributes;
    }
}

/// <summary>events_audit, the subject's OWN rows only (SLICE_S3_CONTRACT.md §6b) — staff-actor identities redacted with a declared reason: "Art. 15 covers the subject's data, not staff identities."</summary>
public sealed class EventsAuditExportContributor(IEventStore eventStore) : IExportContributor
{
    public string StoreKey => "events_audit";

    public async Task<ExportDisposition> ContributeAsync(SubjectRef subject, IExportSink sink, CancellationToken ct = default)
    {
        await ExportEventStream.WriteStream(eventStore, StreamType.Audit, subject.ResourceId, StoreKey, sink, redactStaffActors: true, ct);
        return ExportDisposition.Contributes;
    }
}

/// <summary>Shared event-stream-to-sink writer for the four events_* contributors above (SLICE_S3_CONTRACT.md §6b).</summary>
internal static class ExportEventStream
{
    public static async Task WriteStream(IEventStore eventStore, StreamType stream, string streamId, string storeKey, IExportSink sink, bool redactStaffActors, CancellationToken ct)
    {
        var rows = new List<object>();
        await foreach (var e in eventStore.ReadStream(stream, streamId, ct: ct))
        {
            var isStaffActor = e.ActorRef.StartsWith("Staff:", StringComparison.Ordinal);
            var redacted = redactStaffActors && isStaffActor;

            rows.Add(new
            {
                eventId = e.EventId,
                eventType = e.EventType,
                payload = redacted ? null : ExportJson.ParsePayload(e.PayloadJson),
                actorRef = redacted ? "[redacted: staff actor — Art. 15 covers the subject's own data, not staff identities]" : e.ActorRef,
                tombstone = e.Tombstone,
                region = e.Region,
                lawfulBasis = e.LawfulBasis,
                occurredAt = e.OccurredAt,
                recordedAt = e.RecordedAt,
            });
        }

        await sink.WriteAsync(storeKey, schemaVersion: 1, ExportJson.Serialize(rows), ct);
    }
}
