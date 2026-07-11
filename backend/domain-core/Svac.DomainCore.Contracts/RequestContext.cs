using Svac.DomainCore.Contracts.Ids;

namespace Svac.DomainCore.Contracts;

/// <summary>ISO-3166-1 country + optional subdivision (SLICE_S1_CONTRACT.md §1b: "always-when-known", a one-way door).</summary>
public readonly record struct RegionCode(string Country, string? Subdivision)
{
    /// <summary>Allowlisted unknown-region sentinel for pure-system rows (§1b: region='ZZ', lawful_basis='n/a').</summary>
    public static readonly RegionCode Unknown = new("ZZ", null);

    public override string ToString() => Subdivision is null ? Country : $"{Country}-{Subdivision}";
}

/// <summary>Provenance of a resolved region, so upgrading resolution never rewrites history (§1b, §9).</summary>
public enum RegionSource
{
    Declared,
    Signup,
    EdgeInferred,
    System,
}

/// <summary>Selects which lawful-basis code-table variant resolves a given write (§1b, config-deployable per L-1).</summary>
public readonly record struct LawfulBasisVariant(string Key)
{
    public static readonly LawfulBasisVariant ConservativeGlobalV0 = new("conservative_global_v0");
}

/// <summary>
/// Built by the Hosting middleware BEFORE any module code runs; flows via IRequestContextAccessor
/// (SLICE_S1_CONTRACT.md §1b). Region is NEVER client-settable (L20). System-actor writes inherit the
/// SUBJECT's region; pure-system rows use Region.Unknown / lawful_basis='n/a'.
/// </summary>
public sealed record RequestContext(
    ActorRef Actor,
    RegionCode Region,
    RegionSource RegionSource,
    LawfulBasisVariant LawfulBasisVariant,
    string Locale,
    string CorrelationId,
    // --- Phase-2a additive (PHASE_2A_SUBSTRATE.md §2/§4) — both null-defaulted, so every S1/S2 caller
    // (none of which set either) constructs a byte-identical RequestContext. ---
    /// <summary>[S3] The caller's account state (active/suspended/banned/deleted), resolved server-side from the session join — NEVER client input. Null until identity's bearer resolver sets it.</summary>
    string? AccountState = null,
    /// <summary>[S5] Which staff hat acted + the full grant snapshot. Null on every non-admin host.</summary>
    StaffContext? Staff = null)
{
    /// <summary>The pure-system context used for scheduler/migration/seed-loader work with no subject.</summary>
    public static RequestContext System(ActorRef systemActor, string correlationId) => new(
        systemActor,
        RegionCode.Unknown,
        Svac.DomainCore.Contracts.RegionSource.System,
        LawfulBasisVariant.ConservativeGlobalV0,
        "en",
        correlationId,
        AccountState: null,
        Staff: null);
}

/// <summary>Ambient accessor for the current request's RequestContext (SLICE_S1_CONTRACT.md §1b). Modules
/// consume this seam, never HttpContext directly (arch-tested).</summary>
public interface IRequestContextAccessor
{
    public RequestContext Current { get; }
}
