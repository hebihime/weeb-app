using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Deterministic;
using Svac.DomainCore.FieldEncryption;
using Svac.DomainCore.Persistence;

namespace Svac.DomainCore.Purge;

/// <summary>
/// The ONE executor of purge verbs (SLICE_S1_CONTRACT.md §1b, §6): iterates the compiled registry for
/// the requested class, runs the registered verb per store, emits an Audit-stream event + a purge_run
/// row per run. Subject-scoped stores (the six event tables, ledger_entries, ledger_balances,
/// quota_counters, the key-material pair) get a real per-verb action; stores whose registry cells are
/// NotApplicable for every class that reaches this pipeline in practice (config_entries, purge_runs,
/// projection_checkpoints) report zero rows without mutation, matching their registration.
/// </summary>
public sealed class PurgePipeline(
    CoreDbContext db,
    Svac.DomainCore.Contracts.Streams.IEventStore eventStore,
    IPurgeRegistry registry,
    IFieldEncryptor fieldEncryptor,
    IPolicyEngine policyEngine,
    IFieldKeyVault keyVault,
    IEnumerable<IPurgeStoreExecutor>? storeExecutors = null)
    : IPurgePipeline
{
    private static readonly Dictionary<string, StreamType> EventStoreKeys = CoreDbContext.StreamTables
        .ToDictionary(kv => kv.Value, kv => kv.Key);

    /// <summary>
    /// The extraction seam (SLICE_S3_CONTRACT.md §6c): every registered store OUTSIDE the six native
    /// event streams / ledger pair / quota_counters / crypto-shred pair routes here by storeKey. Empty at
    /// S1/S2 (no registrants) — identity is the first real registrant (§6a), and this dictionary is built
    /// once per pipeline instance from whatever <see cref="IPurgeStoreExecutor"/>s DI supplies, exactly
    /// like <see cref="Svac.DomainCore.Policy.PolicyEngine"/>'s own ownership-resolver dictionary.
    /// </summary>
    private readonly Dictionary<string, IPurgeStoreExecutor> _storeExecutorsByKey =
        (storeExecutors ?? Enumerable.Empty<IPurgeStoreExecutor>()).ToDictionary(e => e.StoreKey);

    /// <summary>Never destroyed by Shred(purpose, subjectScope) — a separate namespace from the per-(purpose, subject) crypto-shred keys.</summary>
    private const string PseudonymizeHmacKeyName = "purge-pseudonymize-hmac-v1";

    /// <summary>
    /// Purge-F4 (SECURITY_REVIEW_S1.md): RetentionExpiry is an AGE-gated class by the registry's own
    /// declared reason ("statutory retention period governs ... hard delete once the age threshold is
    /// reached", PurgeRegistry.cs) — nothing in the shipped pipeline ever filtered by age, so every
    /// RetentionExpiry run deleted a subject's rows on that stream in full, including one recorded
    /// milliseconds ago. A stream with no entry here has no age gate: events_behavioral and
    /// events_heatmap_provenance's registry reasons explicitly defer the WINDOW LENGTH (not the
    /// mechanism) to the Metrics & Ops desk (S5) / cell_history_months config — landing a real,
    /// config-driven value for those is that slice's job, not a silent zero-day floor invented here.
    /// events_audit's window is statutory and unconditional at S1, so it gets a real, conservative floor
    /// now rather than shipping with none at all.
    /// </summary>
    private static readonly Dictionary<StreamType, TimeSpan> RetentionMinimumAge = new()
    {
        [StreamType.Audit] = TimeSpan.FromDays(365 * 7),
    };

    public async Task<IReadOnlyList<PurgeReport>> Run(PurgeClass purgeClass, SubjectRef subject, ActorRef actor, RequestContext ctx, IReadOnlySet<string>? heldStoreKeys = null, CancellationToken ct = default)
    {
        var decision = await policyEngine.Authorize(actor, "core.purge.execute", TargetRef.ForAction("core.purge.execute"), ct);
        if (!decision.IsAllowed)
        {
            throw new UnauthorizedAccessException($"4A denied \"core.purge.execute\" for actor {actor} — {decision.GetType().Name}.");
        }

        // PII-F4 (SECURITY_REVIEW_S1.md): "System-actor writes inherit the SUBJECT's region (a purge run
        // on a German user's data is EU-scoped work)" (§1b) — the shipped pipeline stamped its purge.run
        // audit events with the CALLER's ctx.Region (ZZ for the system scheduler) and had no path to the
        // subject's own region at all. Resolved here from whatever the subject's own already-recorded
        // rows say (a subject with no rows anywhere — nothing yet written, or a NotApplicable-only class
        // run — falls back to the caller's own region, matching prior behavior in that case).
        var subjectRegion = await ResolveSubjectRegion(subject, ct);
        var auditCtx = ctx with { Region = subjectRegion };

        var hmacKey = await keyVault.GetNamedSecret(PseudonymizeHmacKeyName, ct);
        // Purge-F5 (SECURITY_REVIEW_S1.md): purge_runs is registered as "non-PII operational metadata"
        // (PurgeRegistry.cs) exempt from every subject-scoped purge class — a receipt that permanently
        // retains the erased subject's RAW id falsifies that registration. The same keyed pseudonym used
        // to re-key events_consent below is reused here, so the receipt still correlates to the
        // pseudonymized survivor record for anyone holding the HMAC key, while an outside reader of
        // purge_runs alone can never recover the raw subject id from it.
        var pseudonymizedSubjectRef = $"{subject.ResourceType}:{PseudonymizeRef(subject.ResourceId, purgeClass, hmacKey)}";

        var reports = new List<PurgeReport>();
        // First-occurrence (registration) order, never the unordered RegisteredStoreKeys set (SLICE_S3_
        // CONTRACT.md §6a): a store's purge can depend on ANOTHER store's row still being live (identity's
        // email_challenges-by-email S2 scar reads the account's still-live email before accounts' own
        // Tombstone nulls it) — the registry's declared order is now load-bearing, not incidental.
        var orderedStoreKeys = registry.Entries.Select(e => e.StoreKey).Distinct().ToList();
        foreach (var storeKey in orderedStoreKeys)
        {
            var entry = registry.EntriesFor(storeKey).SingleOrDefault(e => e.PurgeClass == purgeClass);
            if (entry is null)
            {
                continue; // registry gap for this (store, class) pair — the arch test's completeness suite catches this, not a silent skip here.
            }

            var isHeld = heldStoreKeys is { Count: > 0 } && heldStoreKeys.Contains(storeKey);

            var runId = MintRunId(DateTimeOffset.UtcNow);
            var startedAt = DateTimeOffset.UtcNow;
            var rowsAffected = isHeld || entry.Verb == PurgeVerb.NotApplicable
                ? 0
                : await ExecuteVerb(storeKey, entry.Verb, subject, purgeClass, hmacKey, ct);
            var completedAt = DateTimeOffset.UtcNow;

            db.PurgeRuns.Add(new PurgeRunEntity
            {
                Id = runId,
                PurgeClass = purgeClass.ToString(),
                SubjectRef = pseudonymizedSubjectRef,
                StoreKey = storeKey,
                RowsAffected = rowsAffected,
                StartedAt = startedAt,
                CompletedAt = completedAt,
                // ER-14 (SLICE_S3_CONTRACT.md §2 Phase P step 1): a held store's receipt records the verb
                // as "Held" with the documented basis, never silently running (or silently skipping with
                // no evidence at all) the registry's own declared verb.
                EvidenceJson = isHeld
                    ? JsonSerializer.Serialize(new { verb = "Held", reason = entry.Reason, custodyHold = true })
                    : JsonSerializer.Serialize(new { verb = entry.Verb.ToString(), reason = entry.Reason }),
            });

            var auditPayload = JsonSerializer.Serialize(new { runId, storeKey, purgeClass = purgeClass.ToString(), verb = isHeld ? "Held" : entry.Verb.ToString(), rowsAffected });
            await eventStore.Append(StreamType.Audit, streamId: runId, eventType: "purge.run", payloadJson: auditPayload, auditCtx, ExpectedVersion.AnyVersion, ct);

            reports.Add(new PurgeReport(runId, storeKey, purgeClass, rowsAffected, startedAt, completedAt));
        }

        return reports;
    }

    private async Task<int> ExecuteVerb(string storeKey, PurgeVerb verb, SubjectRef subject, PurgeClass purgeClass, byte[] hmacKey, CancellationToken ct)
    {
        if (EventStoreKeys.TryGetValue(storeKey, out var stream))
        {
            return await ExecuteOnEventStream(stream, verb, subject, purgeClass, hmacKey, ct);
        }

        return storeKey switch
        {
            "ledger_entries" => await ExecuteOnLedgerEntries(verb, subject, ct),
            "ledger_balances" => await ExecuteOnLedgerBalances(verb, subject, ct),
            "quota_counters" => await ExecuteOnQuotaCounters(verb, subject, ct),
            "data_protection_keys" or "field_key_refs" => await ExecuteCryptoShred(storeKey, verb, subject, ct),
            // Every OTHER registered store (SLICE_S3_CONTRACT.md §6c extraction seam): a DI-registered
            // IPurgeStoreExecutor for this exact key, if one exists. config_entries (NotApplicable by
            // construction), purge_runs/projection_checkpoints (operational metadata, never subject-
            // scoped) have no executor registered and no reachable non-NotApplicable cell in practice, so
            // the fallback 0 below preserves S1's original byte-identical behavior for them.
            _ => _storeExecutorsByKey.TryGetValue(storeKey, out var executor)
                ? await executor.ExecuteAsync(verb, purgeClass, subject, hmacKey, ct)
                : 0,
        };
    }

    private async Task<int> ExecuteOnEventStream(StreamType stream, PurgeVerb verb, SubjectRef subject, PurgeClass purgeClass, byte[] hmacKey, CancellationToken ct)
    {
        var table = db.EventsFor(stream);
        var rows = await table.Where(e => e.StreamId == subject.ResourceId).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return 0;
        }

        // Purge-F4: age-gate a RetentionExpiry run wherever a floor is registered for this stream (see
        // RetentionMinimumAge's doc comment). Rows younger than the floor are left standing entirely.
        if (purgeClass == PurgeClass.RetentionExpiry && RetentionMinimumAge.TryGetValue(stream, out var minAge))
        {
            var cutoff = DateTimeOffset.UtcNow - minAge;
            rows = rows.Where(r => r.RecordedAt <= cutoff).ToList();
            if (rows.Count == 0)
            {
                return 0;
            }
        }

        switch (verb)
        {
            case PurgeVerb.Tombstone:
                foreach (var row in rows.Where(r => !r.Tombstone))
                {
                    row.PayloadJson = null;
                    row.Tombstone = true;
                }
                await db.SaveChangesAsync(ct);
                return rows.Count;
            case PurgeVerb.Delete:
                // Real physical DELETE (SLICE_S1_CONTRACT.md §6: events_behavioral for every class,
                // events_reputation's MinorPurge, events_audit's RetentionExpiry,
                // events_heatmap_provenance's StatutoryErasure/MinorPurge/RetentionExpiry — a real hard
                // delete, deliberately distinct from Tombstone). core.enforce_append_only() rejects
                // every DELETE by default; the ONE narrow, session-local carve-out (migration
                // 20260710085111_InitialCore.Up, §2 comment) requires this exact GUC, set here and
                // nowhere else, inside its own explicit transaction so `SET LOCAL` covers the delete
                // that follows it and nothing else ever sees the flag on.
                await using (var tx = await db.Database.BeginTransactionAsync(ct))
                {
                    await db.Database.ExecuteSqlRawAsync("SET LOCAL core.purge_delete_authorized = 'on'", ct);
                    table.RemoveRange(rows);
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                return rows.Count;
            case PurgeVerb.Pseudonymize:
                // Purge-F2 = MinorProt-F3 (SECURITY_REVIEW_S1.md): "pseudonymize subject" must re-key the
                // SUBJECT — the DDL-designated "subject scope" column is stream_id (§2), not actor_ref.
                // The shipped code re-keyed only actor_ref, leaving stream_id (and therefore the primary
                // means of finding "this subject's rows") untouched and trivially linkable. Both columns
                // now re-key through the SAME keyed-HMAC construction (MinorProt-F4: no unsalted hash —
                // an adversary holding a candidate id cannot recompute this without the vault-held key).
                // OQ-1's ratified posture (§15, PII-F3) also flips the survivor's lawful_basis to the
                // defensible-record basis; Pseudonymize is registered ONLY for events_consent, so this is
                // never reached by any other stream's verb.
                var subjectPseudonym = PseudonymizeRef(subject.ResourceId, purgeClass, hmacKey);
                foreach (var row in rows)
                {
                    row.ActorRef = PseudonymizeRef(row.ActorRef, purgeClass, hmacKey);
                    row.StreamId = subjectPseudonym;
                    row.LawfulBasis = "legal_obligation";
                }
                await db.SaveChangesAsync(ct);
                return rows.Count;
            default:
                return 0;
        }
    }

    private async Task<int> ExecuteOnLedgerEntries(PurgeVerb verb, SubjectRef subject, CancellationToken ct)
    {
        if (verb != PurgeVerb.Tombstone)
        {
            return 0;
        }

        var rows = await db.LedgerEntries.Where(e => e.UserId == subject.ResourceId).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return 0;
        }

        // "Tombstone refs (entries survive as tombstones, user refs severed; balances rebuilt by
        // Replay)" — ledger_entries.user_id is NOT NULL (§2), so severing means re-pointing at a
        // stable redacted sentinel rather than nulling it. The row and its point/xp/svac magnitudes
        // survive for reconciliation; only the subject linkage is cut.
        var sentinel = $"{IdPrefixes.User}_REDACTED0000000000000000000";
        foreach (var row in rows)
        {
            row.UserId = sentinel;
        }
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    private async Task<int> ExecuteOnLedgerBalances(PurgeVerb verb, SubjectRef subject, CancellationToken ct)
    {
        if (verb != PurgeVerb.Tombstone)
        {
            return 0;
        }

        // Purge-F1 = MinorProt-F2 (SECURITY_REVIEW_S1.md): the registry declares Tombstone here
        // ("balances rebuilt by Replay", PurgeRegistry.cs) but the shipped ExecuteVerb had no case for
        // ledger_balances at all — the derivative projection row, keyed by the RAW subject id, survived
        // every purge class while the receipt still reported success. Unlike the six event tables,
        // ledger_balances carries no tombstone/payload columns of its own — it is a rebuildable
        // PROJECTION (§2), so retiring the row keyed by the OLD raw id IS the tombstone here; a future
        // Replay over ledger_entries' own severing (ExecuteOnLedgerEntries, same purge run) rebuilds the
        // balance fresh under the new sentinel id.
        return await db.LedgerBalances.Where(b => b.UserId == subject.ResourceId).ExecuteDeleteAsync(ct);
    }

    private async Task<int> ExecuteOnQuotaCounters(PurgeVerb verb, SubjectRef subject, CancellationToken ct)
    {
        if (verb != PurgeVerb.Delete)
        {
            return 0;
        }

        // quota_counters.actor_ref is written as ActorRef.ToString() ("{Kind}:{Id}", e.g.
        // "User:usr_xxx" — see QuotaService.Consume), never the bare opaque id SubjectRef.ResourceId
        // carries alone. Match on the ":"-delimited suffix so this finds the row regardless of which
        // ActorKind originally consumed the quota, instead of silently deleting zero rows on a
        // literal-equality mismatch.
        var suffix = $":{subject.ResourceId}";
        return await db.QuotaCounters.Where(q => q.ActorRef.EndsWith(suffix)).ExecuteDeleteAsync(ct);
    }

    private async Task<int> ExecuteCryptoShred(string storeKey, PurgeVerb verb, SubjectRef subject, CancellationToken ct)
    {
        if (verb != PurgeVerb.CryptoShred)
        {
            return 0;
        }

        // CRITICAL dedupe: PII-F1 = Purge-F3 = MinorProt-F1 (SECURITY_REVIEW_S1.md). AesFieldEncryptor
        // now wraps every subject's DEK under a key named by (purpose, subject) — Shred here is exercised
        // exactly the way it always was (every purpose, best-effort per purpose), but each call now
        // destroys ONLY this one subject's key, never the whole population's.
        var scope = new SubjectScope(subject.ResourceId);
        foreach (var purpose in Enum.GetValues<FieldEncryptionPurpose>())
        {
            try
            {
                await fieldEncryptor.Shred(purpose, scope, ct);
            }
            catch (InvalidOperationException)
            {
                // Purpose never had key material for this subject — not every subject has every purpose protected.
            }
        }

        if (storeKey != "field_key_refs")
        {
            // data_protection_keys is the ASP.NET Data Protection key ring (auth cookies / antiforgery
            // tokens) — never subject-scoped rows; there is nothing per-subject to touch in this store.
            // CryptoShred's real effect for it is the in-memory/Vault key destruction just performed
            // above. rows_affected honestly reports 0 here rather than a purpose-count masquerading as a
            // row count (SECURITY_REVIEW_S1.md: "make rows_affected count rows, not purposes").
            return 0;
        }

        // field_key_refs (SECURITY_REVIEW_S1.md: "cover the registered stores' own rows"): retire any
        // ref row naming one of this subject's (purpose, subject) vault keys. Empty today (nothing seeds
        // field_key_refs rows yet at S1), so this returns 0 honestly until a real seeding path lands —
        // never inflated by counting purposes shredded instead of rows actually retired.
        var keyNames = Enum.GetValues<FieldEncryptionPurpose>()
            .Select(p => AesFieldEncryptor.KeyName(p, scope))
            .ToHashSet();
        var refs = await db.FieldKeyRefs.Where(r => keyNames.Contains(r.VaultKeyName) && r.RetiredAt == null).ToListAsync(ct);
        foreach (var r in refs)
        {
            r.RetiredAt = DateTimeOffset.UtcNow;
        }
        if (refs.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        return refs.Count;
    }

    /// <summary>
    /// PII-F4: the subject's own region, resolved from whatever they already have recorded on any of the
    /// six event streams (the only region-bearing rows this substrate has at S1 — there is no separate
    /// profile/region store yet). Returns RegionCode.Unknown if the subject has no rows anywhere.
    /// </summary>
    private async Task<RegionCode> ResolveSubjectRegion(SubjectRef subject, CancellationToken ct)
    {
        foreach (var stream in EventStoreKeys.Values)
        {
            var regionText = await db.EventsFor(stream)
                .Where(e => e.StreamId == subject.ResourceId)
                .OrderBy(e => e.GlobalSeq)
                .Select(e => e.Region)
                .FirstOrDefaultAsync(ct);
            if (regionText is not null && regionText != RegionCode.Unknown.ToString())
            {
                var parts = regionText.Split('-', 2);
                return new RegionCode(parts[0], parts.Length > 1 ? parts[1] : null);
            }
        }
        return RegionCode.Unknown;
    }

    /// <summary>
    /// MinorProt-F4 (SECURITY_REVIEW_S1.md): a KEYED re-key (HMAC-SHA256 under a vault-held secret),
    /// never an unsalted hash — the shipped SHA256(purgeClass + original) construction let anyone holding
    /// a candidate id (from another stream's tombstoned rows, a log, a backup) recompute the pseudonym
    /// with zero secrets and confirm linkage. Timestamp zeroed in the ULID encoding: the randomness slot
    /// is entirely supplied by the keyed hash, so the result is deterministic given (purgeClass,
    /// original, key) — required so re-running the SAME purge idempotently re-derives the SAME pseudonym.
    /// </summary>
    private static string PseudonymizeRef(string original, PurgeClass purgeClass, byte[] hmacKey) =>
        PurgePseudonymizer.Pseudonymize(original, purgeClass, hmacKey);

    private static string MintRunId(DateTimeOffset now)
    {
        var randomness = new byte[10];
        RandomNumberGenerator.Fill(randomness);
        var body = Ulid.Encode(now.ToUnixTimeMilliseconds(), randomness);
        return Ulid.WithPrefix(IdPrefixes.PurgeRun, body);
    }
}
