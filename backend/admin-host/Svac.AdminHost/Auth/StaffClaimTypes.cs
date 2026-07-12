using System.Security.Claims;

namespace Svac.AdminHost.Auth;

/// <summary>
/// The ONE claim contract the cookie ticket carries (SLICE_S5_CONTRACT.md §1b/§8 seam 4: "one claim
/// contract + one MFA policy"). Deliberately NOT a role claim — roles are read fresh from the grants
/// table on every operation (§0/§1d: "NEVER Entra claims"), so the cookie only needs enough to re-identify
/// the row + detect a stale stamp between revalidations.
/// </summary>
public static class StaffClaimTypes
{
    /// <summary>The resolved <c>stf_</c> ULID (never the raw Entra <c>oid</c> — that lives only as the
    /// admin.staff_accounts.external_subject lookup key, one hop upstream of the cookie).</summary>
    public const string StaffId = ClaimTypes.NameIdentifier;

    /// <summary>The security_stamp read at sign-in time; compared against the CURRENT row on every
    /// revalidation (SLICE_S5_CONTRACT.md §1b law 1: "cookie validation + revalidation ... re-checks
    /// stamp + status").</summary>
    public const string SecurityStamp = "svac_security_stamp";

    /// <summary>The staff account's declared region (L21) — read once at sign-in so RequestContextMiddleware's
    /// IBearerAuthenticator can supply it without a DB hit on every request.</summary>
    public const string Region = "svac_region";
}

/// <summary>
/// The INCOMING Entra claim shape a real MFA-satisfied sign-in carries (SLICE_S5_CONTRACT.md §1b: "the
/// staff authorization policy requires an MFA-satisfied claim (amr contains mfa / auth-context acr);
/// absence ⇒ sign-in refused"). <see cref="DevSeamsStaffTransport"/>'s fixtures set <see
/// cref="Domain.Auth.StaffExternalClaims.HasMfaClaim"/> directly with the SAME meaning these two claims
/// carry in prod — this type documents what OnTokenValidated inspects to compute that same boolean.
/// </summary>
public static class EntraClaimTypes
{
    /// <summary>Authentication Methods References (OIDC standard claim) — MFA-satisfied iff it contains "mfa".</summary>
    public const string Amr = "amr";

    /// <summary>Authentication Context Class Reference (Entra Conditional Access) — an alternate MFA signal some tenants emit instead of/alongside amr.</summary>
    public const string Acr = "acr";

    /// <summary>
    /// SECURITY_REVIEW_S5.md S5-02: <paramref name="configuredAcrValues"/> is the exact set of
    /// authentication-context-class-reference values the Conditional Access MFA policy for the staff
    /// group actually emits (SVAC_ENTRA_MFA_ACR_VALUES, wired by <c>StaffAuthEntraConfig</c> —
    /// <c>Julien-executed action</c> per SLICE_S5_CONTRACT.md §11: created alongside the Entra tenant/app
    /// registration/CA policy itself, since the values live in the SAME Entra admin center screen). Before
    /// this fix, ANY non-empty <c>acr</c> claim satisfied MFA — a plain <c>pwd</c>/<c>acr:1</c> single-
    /// factor sign-in that merely happens to carry an authentication-context claim (many do, unrelated to
    /// MFA) would have passed. Matching against the configured value(s) means an unconfigured (empty) set
    /// contributes NOTHING — <c>acr</c> alone can never satisfy MFA until Julien's tenant-specific value(s)
    /// are set, so this is strictly narrower than before, never a fail-open widening.
    /// </summary>
    public static bool HasMfaClaim(IEnumerable<System.Security.Claims.Claim> claims, IReadOnlySet<string> configuredAcrValues)
    {
        var list = claims as IList<System.Security.Claims.Claim> ?? claims.ToList();
        var hasAmrMfa = list.Any(c => c.Type == Amr && c.Value.Contains("mfa", StringComparison.OrdinalIgnoreCase));
        var hasConfiguredAcr = configuredAcrValues.Count > 0
            && list.Any(c => c.Type == Acr && configuredAcrValues.Contains(c.Value));
        return hasAmrMfa || hasConfiguredAcr;
    }
}
