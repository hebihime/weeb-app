using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Email;
using Svac.DomainCore.Contracts.Export;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Contracts.Streams;
using Svac.Identity.Config;
using Svac.Identity.Persistence;

namespace Svac.Identity.Export;

/// <summary>
/// The export orchestrator (SLICE_S3_CONTRACT.md §6b: "the export worker/orchestrator builds ONE zip =
/// per-store JSON files + manifest.json"). No background job queue exists in this monolith yet (Pass 2b's
/// deletion worker is the first slice that adds one) — this build runs the SAME orchestration logic
/// synchronously from inside the POST /v1/me/export request (SignupCompletionService's own "the atomic
/// write happens inline" precedent) rather than adding polling infrastructure for a kilobytes-of-JSON job
/// that completes in milliseconds. The class boundary (a standalone worker, called by the endpoint) is
/// exactly what a future async dispatch would wrap in a hosted-service loop — nothing here changes shape
/// when that day comes, only WHO calls <see cref="RunAsync"/> and when.
/// </summary>
public sealed class ExportWorker(
    IEnumerable<IExportContributor> contributors,
    IExportRegistry registry,
    IdentityDbContext db,
    IExportArtifactStore artifactStore,
    IConfigRegistry config,
    IEventStore eventStore,
    IEmailSender emailSender)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyModel = new Dictionary<string, string>();

    public async Task RunAsync(string exportId, string accountId, DateTimeOffset requestedAt, RequestContext ctx, CancellationToken ct)
    {
        try
        {
            await Execute(exportId, accountId, requestedAt, ctx, ct);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The job row is the receipt (§2 DDL CHECK already declares "failed" as a first-class
            // state) — a worker exception must never leave a job silently "pending" forever, and must
            // never crash the request that kicked it off (the job was already Accepted at 202).
            await artifactStore.MarkFailedAsync(exportId, ct);
        }
    }

    private async Task Execute(string exportId, string accountId, DateTimeOffset requestedAt, RequestContext ctx, CancellationToken ct)
    {
        var subject = new SubjectRef("account", accountId);
        var sink = new InMemoryExportSink();
        var contributorsByKey = contributors.ToDictionary(c => c.StoreKey);

        var contributeEntries = registry.Entries.Where(e => e.State == ExportRegistryState.Contributes).ToList();
        foreach (var entry in contributeEntries)
        {
            if (!contributorsByKey.TryGetValue(entry.StoreKey, out var contributor))
            {
                // The export⋈purge cross-gate + Svac.Tests.Identity's export-completeness suite both
                // prove this can never happen at build/test time; this throw is the runtime backstop —
                // never a silent under-export.
                throw new InvalidOperationException(
                    $"export-registry declares Contributes for \"{entry.StoreKey}\" but no IExportContributor is registered — completeness violated at runtime.");
            }

            await contributor.ContributeAsync(subject, sink, ct);
        }

        var statutoryDeadlineDays = await config.GetValue<int>(IdentityConfigKeys.ExportStatutoryDeadlineDays, ct);
        var ttlHours = await config.GetValue<int>(IdentityConfigKeys.ExportLinkTtlHours, ct);
        var readyAt = DateTimeOffset.UtcNow;

        var manifestJson = BuildManifest(sink.Entries, registry.Entries, requestedAt, readyAt, statutoryDeadlineDays);
        var zipBytes = BuildZip(sink.Entries, manifestJson);

        await artifactStore.MarkReadyAsync(exportId, zipBytes, manifestJson, readyAt, readyAt.AddHours(ttlHours), ct);

        await eventStore.Append(StreamType.Audit, accountId, "identity.export_ready", "{}", ctx, ExpectedVersion.AnyVersion, ct);
        await eventStore.Append(StreamType.Behavioral, accountId, "identity.export_ready", "{}", ctx, ExpectedVersion.AnyVersion, ct);

        var account = await db.Accounts.Where(a => a.AccountId == accountId).Select(a => new { a.Email, a.Locale }).SingleOrDefaultAsync(ct);
        if (account?.Email is not null)
        {
            await emailSender.SendAsync(new EmailMessage(account.Email, "email.export_ready", account.Locale, EmptyModel), ctx, ct);
        }
    }

    private static string BuildManifest(
        IReadOnlyList<ExportSinkEntry> sinkEntries,
        IReadOnlyList<ExportRegistrationEntry> registryEntries,
        DateTimeOffset requestedAt,
        DateTimeOffset generatedAt,
        int statutoryDeadlineDays)
    {
        var stores = sinkEntries.Select(e => new
        {
            storeKey = e.StoreKey,
            schemaVersion = e.SchemaVersion,
            rowCount = ComputeRowCount(e.JsonPayload),
        }).ToList();

        var dispositions = registryEntries
            .Where(e => e.State != ExportRegistryState.Contributes)
            .Select(e => new { storeKey = e.StoreKey, state = e.State.ToString(), reason = e.Reason })
            .ToList();

        var manifest = new
        {
            generatedAt,
            requestedAt,
            // A real consumer for identity.export.statutory_deadline_days (§4): the Art. 12(3) clock the
            // manifest itself carries, so the receipt states its own SLA rather than requiring a desk to
            // recompute it later.
            statutoryDeadlineAt = requestedAt.AddDays(statutoryDeadlineDays),
            stores,
            dispositions,
        };

        return ExportJson.Serialize(manifest);
    }

    private static int ComputeRowCount(string jsonPayload)
    {
        using var doc = JsonDocument.Parse(jsonPayload);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.GetArrayLength();
        }
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
            {
                return entries.GetArrayLength();
            }
            return root.EnumerateObject().Any() ? 1 : 0;
        }
        return 1;
    }

    private static byte[] BuildZip(IReadOnlyList<ExportSinkEntry> sinkEntries, string manifestJson)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in sinkEntries)
            {
                var zipEntry = archive.CreateEntry($"{entry.StoreKey}.json", CompressionLevel.Optimal);
                using var entryStream = zipEntry.Open();
                var bytes = Encoding.UTF8.GetBytes(entry.JsonPayload);
                entryStream.Write(bytes, 0, bytes.Length);
            }

            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using var manifestStream = manifestEntry.Open();
            var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
            manifestStream.Write(manifestBytes, 0, manifestBytes.Length);
        }

        return memory.ToArray();
    }
}
