using System.Security.Cryptography;
using System.Text;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Ids;

namespace Svac.Identity.Auth;

/// <summary>
/// The `identity.email.send.daily` synthetic quota actor (SLICE_S3_CONTRACT.md §5): "actor =
/// eml_&lt;HMAC(vault-named-secret, email_lower)&gt; ... the anonymous-flow quota keys on the HMAC'd
/// PROTECTED RESOURCE — the victim's mailbox — never a raw email in quota_counters, never caller-scoped,
/// so an attacker rotating IPs still cannot mail-bomb one address."
///
/// Mints a DETERMINISTIC <see cref="ActorRef"/> for a given mailbox without adding a new member to
/// <see cref="IdPrefixes"/>'s closed set: <see cref="OpaqueId.New"/> (unlike <see cref="OpaqueId.Parse"/>)
/// never validates its prefix against the closed set — it exists purely to mint a fresh id from a
/// caller-supplied clock + randomness source. Seeding a <see cref="Random"/> from the keyed-HMAC digest
/// and pairing it with a FIXED reference timestamp makes <see cref="OpaqueId.New"/>'s otherwise-random
/// ULID body fully deterministic per mailbox, so quota_counters accumulates correctly across repeated
/// calls for the SAME email without a schema-level prefix change. <see cref="ActorKind.System"/> is the
/// correct kind (this is the SYSTEM protecting a mailbox, not a user acting), so the existing
/// ActorPrefixConsistencyArchTests bijection (keyed on <see cref="IdPrefixes.ActorKindForPrefix"/>, which
/// this id's prefix is deliberately absent from) is unaffected.
/// </summary>
public static class EmailQuotaActor
{
    /// <summary>The IFieldKeyVault named-secret door this synthetic actor's HMAC hashes under — distinct from the code and verified-token secrets.</summary>
    public const string NamedSecretKey = "identity.email_quota_actor_hmac";

    private static readonly DateTimeOffset FixedReferenceInstant = DateTimeOffset.UnixEpoch;

    public static async Task<ActorRef> ForMailbox(IFieldKeyVault vault, string emailLower, CancellationToken ct)
    {
        var key = await vault.GetNamedSecret(NamedSecretKey, ct);
        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(emailLower));

        // Any 4 bytes of a keyed HMAC are a fine deterministic PRNG seed for this NON-cryptographic
        // bucketing purpose — the secrecy already comes from the HMAC step, not from Random's PRNG
        // quality; Random(int) is deterministic within one running .NET build, which is all a same-process
        // quota bucket needs.
        var seed = BitConverter.ToInt32(mac, 0);
        var deterministicRandom = new Random(seed);

        var id = OpaqueId.New(IdPrefixes.System, FixedReferenceInstant, deterministicRandom);
        return new ActorRef(id, ActorKind.System);
    }
}
