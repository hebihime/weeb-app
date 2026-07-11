using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts.FieldEncryption;
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
    public Task MarkReadyAsync(string exportId, string accountId, byte[] zipBytes, string manifestJson, DateTimeOffset readyAt, DateTimeOffset expiresAt, CancellationToken ct = default);

    /// <summary>Marks the job "failed" — the artifact never wrote (SLICE_S3_CONTRACT.md §1c ExportStatus state).</summary>
    public Task MarkFailedAsync(string exportId, CancellationToken ct = default);

    /// <summary>
    /// Fetches the zip bytes for a READY, non-expired job OWNED by <paramref name="accountId"/> (AUTH-1,
    /// SECURITY_REVIEW_S3.md: folded defense-in-depth ownership predicate — the query itself denies a
    /// foreign exportId even if the policy chokepoint filter is ever dropped by a future refactor). Null
    /// on any other state/expiry/ownership mismatch — the caller renders absence, never a distinguishing
    /// reason.
    /// </summary>
    public Task<byte[]?> GetReadyZipAsync(string exportId, string accountId, CancellationToken ct = default);
}

/// <summary>
/// Postgres-bytea-backed <see cref="IExportArtifactStore"/> (SLICE_S3_CONTRACT.md §12 item 5) — the
/// ratified artifact tier for a kilobytes-of-JSON export.
///
/// PII-7 (SECURITY_REVIEW_S3.md): the artifact bytea is encrypted under the SUBJECT'S OWN
/// (<see cref="FieldEncryptionPurpose.ExportArtifact"/>, accountId) key before it is written — the
/// artifact zip itself contains the subject's decrypted birthdate (AccountExportContributor's own,
/// deliberate Art. 15 posture), so storing it plaintext defeated the birthdate field-encryption tier for
/// every subject who ever exported: a DB dump would yield a decrypted birthdate regardless of the
/// accounts.birthdate_enc column's own protection. Encrypting under the subject's own key means
/// crypto-shred (already run per-purpose, per-subject on every erasure class) retroactively kills any
/// artifact residue too — the download path decrypts on read, so the export still WORKS exactly as
/// before for a live, unshredded subject.
/// </summary>
public sealed class PostgresExportArtifactStore(IdentityDbContext db, IFieldEncryptor fieldEncryptor) : IExportArtifactStore
{
    public async Task MarkReadyAsync(string exportId, string accountId, byte[] zipBytes, string manifestJson, DateTimeOffset readyAt, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        // Protect() takes a string plaintext; the zip is arbitrary binary, so it travels as base64 inside
        // the encrypted envelope (the envelope itself is already a self-describing binary blob — this is
        // just what goes INSIDE it here, not a second layer of encoding on top of ciphertext).
        var encryptedArtifact = await fieldEncryptor.Protect(
            FieldEncryptionPurpose.ExportArtifact, new SubjectScope(accountId), Convert.ToBase64String(zipBytes), ct);

        await db.ExportJobs
            .Where(j => j.ExportId == exportId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.State, "ready")
                .SetProperty(j => j.Artifact, encryptedArtifact)
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

    public async Task<byte[]?> GetReadyZipAsync(string exportId, string accountId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var encryptedArtifact = await db.ExportJobs
            .Where(j => j.ExportId == exportId && j.AccountId == accountId && j.State == "ready" && (j.ExpiresAt == null || j.ExpiresAt > now))
            .Select(j => j.Artifact)
            .SingleOrDefaultAsync(ct);
        if (encryptedArtifact is null)
        {
            return null;
        }
        var base64Zip = await fieldEncryptor.Unprotect(FieldEncryptionPurpose.ExportArtifact, encryptedArtifact, ct);
        return Convert.FromBase64String(base64Zip);
    }
}
