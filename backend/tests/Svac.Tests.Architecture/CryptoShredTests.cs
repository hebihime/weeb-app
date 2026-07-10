using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.FieldEncryption;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// The crypto-shred proof (SLICE_S1_CONTRACT.md §6): "CryptoShred is a VERB a store declares per class
/// ... tested: protect -&gt; shred -&gt; unprotect fails -&gt; PurgeReport emitted." This file proves the
/// protect/shred/unprotect-fails half over the real AesFieldEncryptor + DevKeyringFieldKeyVault pair
/// (pure in-memory, no Postgres needed — data_protection_keys/field_key_refs never hold key material,
/// §2: "no key material ever in Postgres"); PurgeCompletenessTests.cs proves the PurgeReport-emitted half
/// through the real pipeline against a real Postgres.
/// </summary>
public sealed class CryptoShredTests
{
    [Fact]
    public async Task Protect_ThenUnprotect_RoundTrips_BeforeAnyShred()
    {
        var encryptor = BuildEncryptor();
        var protected_ = await encryptor.Protect(FieldEncryptionPurpose.SpecialCategory, new SubjectScope("usr_fixture_roundtrip"), "sensitive-plaintext");

        var recovered = await encryptor.Unprotect(FieldEncryptionPurpose.SpecialCategory, protected_);

        Assert.Equal("sensitive-plaintext", recovered);
    }

    [Fact]
    public async Task Protect_Shred_Unprotect_Fails_TheCryptoShredContractHolds()
    {
        var encryptor = BuildEncryptor();
        var protectedData = await encryptor.Protect(FieldEncryptionPurpose.Birthdate, new SubjectScope("usr_fixture_shred_target"), "2001-01-01");

        await encryptor.Shred(FieldEncryptionPurpose.Birthdate, new SubjectScope("usr_fixture_shred_target"));

        // The crypto-shred contract: a subsequent Unprotect must throw — the plaintext is unrecoverable,
        // not merely inaccessible through some higher-level API. This is what makes Shred a REAL purge
        // verb rather than a soft-delete flag.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => encryptor.Unprotect(FieldEncryptionPurpose.Birthdate, protectedData));
    }

    [Fact]
    public async Task Shred_IsIdempotent_ShreddingTwiceNeverThrows()
    {
        // A purge pipeline re-run (retry after a partial failure, or a second purge class hitting the
        // same purpose) must never crash on an already-shredded key — CryptoShred is deliberate and
        // repeatable, not a one-shot landmine.
        var encryptor = BuildEncryptor();
        await encryptor.Protect(FieldEncryptionPurpose.VerificationAudit, new SubjectScope("usr_fixture"), "id-doc-reference");

        await encryptor.Shred(FieldEncryptionPurpose.VerificationAudit, new SubjectScope("usr_fixture"));
        await encryptor.Shred(FieldEncryptionPurpose.VerificationAudit, new SubjectScope("usr_fixture")); // must not throw
    }

    [Fact]
    public async Task Shred_OnAPurposeNeverProtected_NeverThrows()
    {
        // §6 pipeline comment: "Purpose never had key material for this subject — not every subject has
        // every purpose protected." A purge run must be able to Shred every enumerated purpose
        // unconditionally without needing to know in advance which ones this subject actually used.
        var encryptor = BuildEncryptor();
        await encryptor.Shred(FieldEncryptionPurpose.IdentityExclusionFilters, new SubjectScope("usr_never_protected"));
    }

    [Fact]
    public async Task EachPurpose_UsesAnIndependentKey_ShreddingOneNeverBreaksAnother()
    {
        // Purpose-bound keys (§1b: "one key never serves two purposes, so a shred of one purpose can
        // never leak protection on another"). This is the isolation half of that invariant.
        var encryptor = BuildEncryptor();
        var special = await encryptor.Protect(FieldEncryptionPurpose.SpecialCategory, new SubjectScope("usr_fixture"), "special-category-data");
        var birthdate = await encryptor.Protect(FieldEncryptionPurpose.Birthdate, new SubjectScope("usr_fixture"), "1999-12-31");

        await encryptor.Shred(FieldEncryptionPurpose.SpecialCategory, new SubjectScope("usr_fixture"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => encryptor.Unprotect(FieldEncryptionPurpose.SpecialCategory, special));
        // The Birthdate purpose's key was never touched — it must still decrypt cleanly.
        Assert.Equal("1999-12-31", await encryptor.Unprotect(FieldEncryptionPurpose.Birthdate, birthdate));
    }

    private static AesFieldEncryptor BuildEncryptor() => new(new DevKeyringFieldKeyVault());
}
