namespace Svac.DomainCore.Contracts.FieldEncryption;

/// <summary>
/// Closed enum of encryption purposes, seeded now (SLICE_S1_CONTRACT.md §1b). Purposes are purpose-bound
/// — one key never serves two purposes, so a shred of one purpose can never leak protection on another.
/// </summary>
public enum FieldEncryptionPurpose
{
    SpecialCategory,
    Birthdate,
    VerificationAudit,
    IdentityExclusionFilters,
}

/// <summary>Scope a Shred call applies to — usually a subject id, sometimes a whole purpose-key retirement.</summary>
public readonly record struct SubjectScope(string SubjectId);

/// <summary>
/// Purpose-bound field encryption over .NET Data Protection + Key Vault envelope (SLICE_S1_CONTRACT.md
/// §1b). Shred is a first-class verb: crypto-shredding is deliberate, never an accident.
///
/// Protect takes a SubjectScope (dedupe: PII-F1 = Purge-F3 = MinorProt-F1, SECURITY_REVIEW_S1.md): every
/// subject's ciphertext is wrapped under its OWN (purpose, subject) key, never one key shared across the
/// whole purpose population. Shred(purpose, subjectScope) destroys exactly that one key — crypto-shredding
/// one subject's special-category/birthdate data can never brick every other subject's, and a purge run
/// is subject-scoped in fact, not just in the registry's declared intent. Unprotect needs no subject
/// parameter: the protected blob is self-describing (it carries the exact key name it was wrapped under),
/// so a caller need only hold the blob and the purpose to unprotect it, same as before this fix.
/// </summary>
public interface IFieldEncryptor
{
    public Task<byte[]> Protect(FieldEncryptionPurpose purpose, SubjectScope subjectScope, string plaintext, CancellationToken ct = default);
    public Task<string> Unprotect(FieldEncryptionPurpose purpose, byte[] protectedData, CancellationToken ct = default);

    /// <summary>Destroys the key material for (purpose, subjectScope) so a subsequent Unprotect throws.</summary>
    public Task Shred(FieldEncryptionPurpose purpose, SubjectScope subjectScope, CancellationToken ct = default);
}

/// <summary>
/// Vendor seam for the key material backend (SLICE_S1_CONTRACT.md §1b): Key Vault in prod, a local dev
/// keyring under DevSeams. Prod without Vault THROWS at startup (L18 fail-closed).
/// </summary>
public interface IFieldKeyVault
{
    public Task<byte[]> WrapKey(string keyName, byte[] rawKey, CancellationToken ct = default);
    public Task<byte[]> UnwrapKey(string keyName, byte[] wrappedKey, CancellationToken ct = default);

    /// <summary>Destroys the named key server-side; a subsequent UnwrapKey for this name throws.</summary>
    public Task DestroyKey(string keyName, CancellationToken ct = default);

    /// <summary>
    /// Returns a stable, named raw secret for use OUTSIDE the Wrap/Unwrap envelope — e.g. an HMAC key
    /// (MinorProt-F4, SECURITY_REVIEW_S1.md: purge pseudonymization must be a KEYED re-key, never an
    /// unsalted hash, or a candidate id recomputes it with zero secrets). Never destroyed by Shred(purpose,
    /// subjectScope), which only ever names a (purpose, subject) key — this is a separate namespace.
    /// </summary>
    public Task<byte[]> GetNamedSecret(string keyName, CancellationToken ct = default);
}
