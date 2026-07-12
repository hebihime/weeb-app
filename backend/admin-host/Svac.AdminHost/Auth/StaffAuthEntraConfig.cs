namespace Svac.AdminHost.Auth;

/// <summary>
/// Prod/Staging Entra config (SLICE_S5_CONTRACT.md §1b: "Authority/client-id from config; client
/// credential from Key Vault via the S0-reserved path — no staff-auth secret in the repo (2A)").
/// <see cref="ClientSecret"/> is resolved by Program.cs from the Key Vault seam (2A) — this record never
/// touches a file the repo commits.
/// </summary>
public sealed record StaffAuthEntraConfig(string? Authority, string? ClientId, string? ClientSecret)
{
    /// <summary>
    /// SECURITY_REVIEW_S5.md S5-02: the exact <c>acr</c> value(s) the staff group's Conditional Access
    /// MFA policy actually emits (SVAC_ENTRA_MFA_ACR_VALUES, comma-separated) — see
    /// <see cref="EntraClaimTypes.HasMfaClaim"/>'s own doc comment. Defaults to empty, meaning the
    /// unconfigured <c>acr</c> signal contributes nothing to the MFA decision (relies on <c>amr</c>
    /// alone) rather than the old fail-open "any non-empty acr" behavior.
    /// </summary>
    public IReadOnlySet<string> AcrValues { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public bool AuthorityConfigured => !string.IsNullOrWhiteSpace(Authority);
    public bool ClientIdConfigured => !string.IsNullOrWhiteSpace(ClientId);
    public bool ClientSecretConfigured => !string.IsNullOrWhiteSpace(ClientSecret);
    public bool IsComplete => AuthorityConfigured && ClientIdConfigured && ClientSecretConfigured;
}
