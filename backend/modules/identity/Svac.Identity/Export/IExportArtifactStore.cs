using Microsoft.EntityFrameworkCore;
using Svac.Identity.Persistence;

namespace Svac.Identity.Export;

/// <summary>
/// The read/write door onto the export artifact itself (SLICE_S3_CONTRACT.md §6b/§12 item 5: "export
/// artifact = Postgres bytea behind IExportArtifactStore; promotion to blob is one DI swap later").
/// Module-internal — no other module, and no other part of identity, reaches
/// <c>identity.export_jobs.artifact</c> directly.
/// </summary>
public interface IExportArtifactStore
{
    /// <summary>Persists the built zip + manifest onto the job row and flips its state to "ready".</summary>
    public Task MarkReadyAsync(string exportId, byte[] zipBytes, string manifestJson, DateTimeOffset readyAt, DateTimeOffset expiresAt, CancellationToken ct = default);

    /// <summary>Marks the job "failed" — the artifact never wrote (SLICE_S3_CONTRACT.md §1c ExportStatus state).</summary>
    public Task MarkFailedAsync(string exportId, CancellationToken ct = default);

    /// <summary>Fetches the zip bytes for a READY, non-expired job. Null on any other state/expiry — the caller renders absence, never a distinguishing reason.</summary>
    public Task<byte[]?> GetReadyZipAsync(string exportId, CancellationToken ct = default);
}

/// <summary>Postgres-bytea-backed <see cref="IExportArtifactStore"/> (SLICE_S3_CONTRACT.md §12 item 5) — the ratified artifact tier for a kilobytes-of-JSON export.</summary>
public sealed class PostgresExportArtifactStore(IdentityDbContext db) : IExportArtifactStore
{
    public async Task MarkReadyAsync(string exportId, byte[] zipBytes, string manifestJson, DateTimeOffset readyAt, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        await db.ExportJobs
            .Where(j => j.ExportId == exportId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.State, "ready")
                .SetProperty(j => j.Artifact, zipBytes)
                .SetProperty(j => j.ManifestJson, manifestJson)
                .SetProperty(j => j.ReadyAt, readyAt)
                .SetProperty(j => j.ExpiresAt, expiresAt), ct);
    }

    public async Task MarkFailedAsync(string exportId, CancellationToken ct = default)
    {
        await db.ExportJobs
            .Where(j => j.ExportId == exportId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, "failed"), ct);
    }

    public async Task<byte[]?> GetReadyZipAsync(string exportId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var row = await db.ExportJobs
            .Where(j => j.ExportId == exportId && j.State == "ready" && (j.ExpiresAt == null || j.ExpiresAt > now))
            .Select(j => j.Artifact)
            .SingleOrDefaultAsync(ct);
        return row;
    }
}
