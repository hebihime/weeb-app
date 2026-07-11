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

    /// <summary>
    /// PII-8 (SECURITY_REVIEW_S3.md, RED before the fix / GREEN after): DevKeyringFieldKeyVault's master
    /// keys are RE-DERIVABLE from the fixed dev seed (needed so an in-process restart of the SAME
    /// instance stays usable) — pre-fix, the destroyed-key set was purely in-memory, so a genuine process
    /// restart (a fresh instance) forgot every shredded key and Unprotect happily re-derived it. This
    /// test simulates that restart with two SEPARATE instances pointed at the SAME persisted store path —
    /// destroy on instance 1, "restart" (construct instance 2 fresh), Unprotect must still fail on
    /// instance 2. The default parameterless constructor (every other test in this suite) is UNCHANGED —
    /// purely in-memory, isolated per instance — proven by <see cref="Shred_OnAPurposeNeverProtected_NeverThrows"/>
    /// and friends staying green with zero modification.
    /// </summary>
    [Fact]
    public async Task DestroyedKey_SurvivesASimulatedRestart_WhenBackedByAPersistedStore()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"pii8-fixture-{Guid.NewGuid():N}.destroyed-keys.txt");
        try
        {
            var subject = new SubjectScope($"usr_pii8_{Guid.NewGuid():N}");

            var instance1 = new DevKeyringFieldKeyVault(storePath);
            var encryptor1 = new AesFieldEncryptor(instance1);
            var protectedData = await encryptor1.Protect(FieldEncryptionPurpose.Birthdate, subject, "1990-06-15");
            await encryptor1.Shred(FieldEncryptionPurpose.Birthdate, subject);

            // Sanity: the SAME (live) instance already refuses — this is the pre-fix behavior too.
            await Assert.ThrowsAsync<InvalidOperationException>(() => encryptor1.Unprotect(FieldEncryptionPurpose.Birthdate, protectedData));

            // "Restart" — a BRAND NEW instance, over the SAME persisted store path, never told about
            // instance1's in-memory _destroyed set directly.
            var instance2 = new DevKeyringFieldKeyVault(storePath);
            var encryptor2 = new AesFieldEncryptor(instance2);
            await Assert.ThrowsAsync<InvalidOperationException>(() => encryptor2.Unprotect(FieldEncryptionPurpose.Birthdate, protectedData));
        }
        finally
        {
            File.Delete(storePath);
        }
    }

    // NOTE: the env-var-driven path fallback (SVAC_DEVSEAMS_DESTROYED_KEYS_PATH, used by the real DI
    // registration when no explicit path is passed) is deliberately NOT exercised via a process-wide
    // Environment.SetEnvironmentVariable here — this test assembly runs multiple IAsyncLifetime classes
    // in parallel (several of which construct a DevKeyringFieldKeyVault through AddDomainCore with no
    // explicit path), and mutating process-wide env state would risk exactly the shared-file contention
    // this fix's design note warns against. The explicit-constructor-argument test above exercises the
    // SAME persistence code path (the env var is a one-line `??` fallback ahead of it) with zero shared
    // global state.

    private static AesFieldEncryptor BuildEncryptor() => new(new DevKeyringFieldKeyVault());
}
