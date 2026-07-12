namespace Svac.AdminHost.Auth;

/// <summary>
/// Prod/Staging Entra config (SLICE_S5_CONTRACT.md §1b: "Authority/client-id from config; client
/// credential from Key Vault via the S0-reserved path — no staff-auth secret in the repo (2A)").
/// <see cref="ClientSecret"/> is resolved by Program.cs from the Key Vault seam (2A) — this record never
/// touches a file the repo commits.
/// </summary>
public sealed record StaffAuthEntraConfig(string? Authority, string? ClientId, string? ClientSecret)
{
    public bool AuthorityConfigured => !string.IsNullOrWhiteSpace(Authority);
    public bool ClientIdConfigured => !string.IsNullOrWhiteSpace(ClientId);
    public bool ClientSecretConfigured => !string.IsNullOrWhiteSpace(ClientSecret);
    public bool IsComplete => AuthorityConfigured && ClientIdConfigured && ClientSecretConfigured;
}
