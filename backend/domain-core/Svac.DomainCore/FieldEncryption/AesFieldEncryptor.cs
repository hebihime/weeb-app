using System.Security.Cryptography;
using System.Text;
using Svac.DomainCore.Contracts.FieldEncryption;

namespace Svac.DomainCore.FieldEncryption;

/// <summary>
/// Purpose-bound field encryption over AES-GCM, keyed through IFieldKeyVault (SLICE_S1_CONTRACT.md
/// §1b). Ships with the seam wired end-to-end (protect -&gt; shred -&gt; unprotect fails).
///
/// Per-subject key scoping (dedupe: PII-F1 = Purge-F3 = MinorProt-F1, SECURITY_REVIEW_S1.md): the vault
/// key name is keyed by BOTH purpose and subject, never purpose alone. Before this fix, one shared
/// per-purpose key wrapped every subject's DEK, so Shred(purpose, subjectScope) discarded subjectScope
/// entirely and destroyed the whole population's key material for that purpose — one AccountDeletion/
/// StatutoryErasure/MinorPurge run bricked every OTHER subject's special-category/birthdate data. The
/// protected blob is self-describing (it embeds the exact key name it was wrapped under), so Unprotect
/// still needs no subject parameter — only Protect (which must decide which key to wrap under) does.
/// </summary>
public sealed class AesFieldEncryptor(IFieldKeyVault keyVault) : IFieldEncryptor
{
    public async Task<byte[]> Protect(FieldEncryptionPurpose purpose, SubjectScope subjectScope, string plaintext, CancellationToken ct = default)
    {
        var keyName = KeyName(purpose, subjectScope);
        var dek = RandomNumberGenerator.GetBytes(32);
        var wrappedDek = await keyVault.WrapKey(keyName, dek, ct);

        using var aes = new AesGcm(dek, AesGcm.TagByteSizes.MaxSize);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Envelope: [keyName][wrappedDekLength(4)][wrappedDek][nonce][tag][ciphertext] — self-contained
        // blob. keyName is now per-(purpose, subject), so it must travel WITH the blob (BinaryWriter's
        // string overload is length-prefixed) rather than being re-derived from purpose alone — Unprotect
        // needs no subject parameter as a result.
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(keyName);
        writer.Write(wrappedDek.Length);
        writer.Write(wrappedDek);
        writer.Write(nonce);
        writer.Write(tag);
        writer.Write(ciphertext);
        return stream.ToArray();
    }

    public async Task<string> Unprotect(FieldEncryptionPurpose purpose, byte[] protectedData, CancellationToken ct = default)
    {
        using var stream = new MemoryStream(protectedData);
        using var reader = new BinaryReader(stream);
        var keyName = reader.ReadString();
        var wrappedDekLength = reader.ReadInt32();
        var wrappedDek = reader.ReadBytes(wrappedDekLength);
        var nonce = reader.ReadBytes(AesGcm.NonceByteSizes.MaxSize);
        var tag = reader.ReadBytes(AesGcm.TagByteSizes.MaxSize);
        var ciphertext = reader.ReadBytes((int)(stream.Length - stream.Position));

        var dek = await keyVault.UnwrapKey(keyName, wrappedDek, ct);
        using var aes = new AesGcm(dek, AesGcm.TagByteSizes.MaxSize);
        var plaintextBytes = new byte[ciphertext.Length];
        aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);
        return Encoding.UTF8.GetString(plaintextBytes);
    }

    public Task Shred(FieldEncryptionPurpose purpose, SubjectScope subjectScope, CancellationToken ct = default) =>
        keyVault.DestroyKey(KeyName(purpose, subjectScope), ct);

    /// <summary>The per-(purpose, subject) vault key name. Exposed so PurgePipeline can compute the same
    /// name to locate/retire the matching core.field_key_refs row without duplicating this formula.</summary>
    public static string KeyName(FieldEncryptionPurpose purpose, SubjectScope subjectScope) =>
        $"{PurposeKeyName(purpose)}:{subjectScope.SubjectId}";

    private static string PurposeKeyName(FieldEncryptionPurpose purpose) => purpose switch
    {
        FieldEncryptionPurpose.SpecialCategory => "field-enc-special-category-v1",
        FieldEncryptionPurpose.Birthdate => "field-enc-birthdate-v1",
        FieldEncryptionPurpose.VerificationAudit => "field-enc-verification-audit-v1",
        FieldEncryptionPurpose.IdentityExclusionFilters => "field-enc-identity-exclusion-filters-v1",
        FieldEncryptionPurpose.AnimeAnswers => "field-enc-anime-answers-v1",
        FieldEncryptionPurpose.ExportArtifact => "field-enc-export-artifact-v1",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null),
    };
}
