using System.Text.Json.Serialization;
using Svac.DomainCore.Deterministic;

namespace Svac.DomainCore.Contracts.Ids;

/// <summary>
/// A typed, prefixed-ULID opaque id (SLICE_S1_CONTRACT.md §1b, §12.11): no raw Guid/uuid ever crosses a
/// module boundary or the API. The prefix is load-bearing, not decorative — 4A axis checks and ER-6
/// absence rules key off it, so it is enforceable by type, not lookup.
/// </summary>
[JsonConverter(typeof(OpaqueIdJsonConverter))]
public readonly record struct OpaqueId
{
    public string Prefix { get; }
    public string Value { get; }

    private OpaqueId(string prefix, string value)
    {
        Prefix = prefix;
        Value = value;
    }

    /// <summary>Mints a fresh id with the given prefix. Caller supplies the clock and the randomness source.</summary>
    public static OpaqueId New(string prefix, DateTimeOffset now, Random random)
    {
        var bytes = new byte[10];
        random.NextBytes(bytes);
        var body = Ulid.Encode(now.ToUnixTimeMilliseconds(), bytes);
        var full = Ulid.WithPrefix(prefix, body);
        return new OpaqueId(prefix, full);
    }

    /// <summary>Parses a previously-minted id, validating its ULID body shape AND that the prefix is a
    /// member of the closed IdPrefixes set (Auth-F1, SECURITY_REVIEW_S1.md) — the prefix is load-bearing
    /// (4A axis checks, ER-6 absence rules key off it), so an id carrying an unregistered prefix must
    /// never silently parse as if it were legitimate.</summary>
    public static OpaqueId Parse(string value)
    {
        var (prefix, _) = Ulid.SplitPrefixed(value);
        if (!IdPrefixes.All.Contains(prefix))
        {
            throw new FormatException(
                $"\"{value}\" carries prefix \"{prefix}\", which is not a member of the closed IdPrefixes " +
                "set (SLICE_S1_CONTRACT.md §1b/§12.11) — extend IdPrefixes via a versioned contract change, never silently.");
        }
        return new OpaqueId(prefix, value);
    }

    /// <summary>True if <paramref name="value"/> is a well-formed prefixed opaque id (any prefix).</summary>
    public static bool TryParse(string? value, out OpaqueId id)
    {
        id = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }
        try
        {
            id = Parse(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public override string ToString() => Value;
}

/// <summary>Canonical id prefixes (SLICE_S1_CONTRACT.md §1b line 36). Closed set, extend only via contract change.</summary>
public static class IdPrefixes
{
    public const string User = "usr";
    public const string Staff = "stf";
    public const string Partner = "prt";
    public const string System = "sys";
    /// <summary>The unauthenticated caller (Auth-F1, SECURITY_REVIEW_S1.md): the lowest-privilege actor
    /// gets its OWN prefix, never System's — a prefix is load-bearing (4A axis checks, ER-6 absence
    /// rules key off it), so minting an anonymous actor under "sys" reads an unauthenticated caller as
    /// the system actor to any code that trusts the prefix, which the contract explicitly sanctions.</summary>
    public const string Anonymous = "anon";
    public const string Event = "evt";
    public const string Ledger = "led";
    /// <summary>A purge-pipeline run receipt id (PurgePipeline.MintRunId) — formalized here so it is a
    /// registered member of the closed set instead of a bare literal at the mint site.</summary>
    public const string PurgeRun = "run";
    /// <summary>An irreversibly re-keyed subject reference (PurgePipeline.PseudonymizeRef) — formalized
    /// here so it is a registered member of the closed set instead of a bare literal at the mint site.</summary>
    public const string Pseudonym = "pseudo";

    // --- Phase-2a additive (PHASE_2A_SUBSTRATE.md §2) — RESOURCE id prefixes (sessions/devices/challenges/
    // export/deletion jobs, staff role grants), never actor-kind prefixes. Deliberately absent from
    // ActorKindForPrefix below: ActorPrefixConsistencyArchTests's bijection proof enumerates only the
    // fixed, closed set of MINTABLE ActorKinds (User/Staff/Partner/System/Anonymous) and is unaffected by
    // additions to All that carry no corresponding ActorKind.
    /// <summary>[S3] An identity session id.</summary>
    public const string Session = "ses";
    /// <summary>[S3] An identity device/push-token registration id.</summary>
    public const string Device = "dev";
    /// <summary>[S3] An identity email-challenge (signup/login/email-change) id.</summary>
    public const string Challenge = "chl";
    /// <summary>[S3] An identity export-job id.</summary>
    public const string Export = "exp";
    /// <summary>[S3] An identity deletion-job id.</summary>
    public const string Deletion = "del";
    /// <summary>[S5] An admin staff-role-grant id.</summary>
    public const string StaffRoleGrant = "srg";

    /// <summary>The closed set of every prefix ever minted (SLICE_S1_CONTRACT.md §1b/§12.11).
    /// OpaqueId.Parse validates against exactly this set. Extend only via a versioned contract change.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        User, Staff, Partner, System, Anonymous, Event, Ledger, PurgeRun, Pseudonym,
        Session, Device, Challenge, Export, Deletion, StaffRoleGrant,
    };

    /// <summary>The subset of prefixes that identify an ACTOR, mapped to the ActorKind each one carries
    /// (Auth-F1 Kind-vs-prefix cross-check, SECURITY_REVIEW_S1.md). Every ActorKind that can be minted
    /// from an OpaqueId at S1 has exactly one canonical prefix here.</summary>
    public static readonly IReadOnlyDictionary<string, Svac.DomainCore.Contracts.Ids.ActorKind> ActorKindForPrefix =
        new Dictionary<string, Svac.DomainCore.Contracts.Ids.ActorKind>
        {
            [User] = Svac.DomainCore.Contracts.Ids.ActorKind.User,
            [Staff] = Svac.DomainCore.Contracts.Ids.ActorKind.Staff,
            [Partner] = Svac.DomainCore.Contracts.Ids.ActorKind.Partner,
            [System] = Svac.DomainCore.Contracts.Ids.ActorKind.System,
            [Anonymous] = Svac.DomainCore.Contracts.Ids.ActorKind.Anonymous,
        };
}
