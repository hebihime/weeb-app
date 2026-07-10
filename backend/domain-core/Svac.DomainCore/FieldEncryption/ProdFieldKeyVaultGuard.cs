namespace Svac.DomainCore.FieldEncryption;

/// <summary>
/// Prod-without-Vault THROWS at startup (L18 fail-closed, SLICE_S1_CONTRACT.md §1b/§9): Azure Key Vault
/// has no subscription yet (OQ-3 pending). Call this once during host startup, before the first request
/// is served, so a misconfigured production deploy fails loudly at boot rather than serving traffic with
/// no real key-material backend.
///
/// Trust-F1 (SECURITY_REVIEW_S1.md): fail-closed means ALLOWLIST the one safe environment
/// (Development), never BLOCKLIST the one unsafe one (Production). A boolean `isProduction` collapses
/// every non-Production environment (Staging, QA, Preview, ...) into "safe", so DevSeams — and the fake
/// money door + hardcoded dev-keyring crypto it wires — boots clean anywhere that is not literally
/// Production. The guard now takes the environment NAME and allowlists exactly "Development".
/// </summary>
public static class ProdFieldKeyVaultGuard
{
    /// <param name="environmentName">The hosting environment's EnvironmentName (e.g. ASPNETCORE_ENVIRONMENT).</param>
    /// <param name="devSeamsEnabled">The DevSeams environment flag (never a 9A entry, §1b/§12.16).</param>
    /// <param name="keyVaultEndpointConfigured">True once real Key Vault wiring exists (post-OQ-3).</param>
    public static void Enforce(string environmentName, bool devSeamsEnabled, bool keyVaultEndpointConfigured)
    {
        var isDevelopment = string.Equals(environmentName, Microsoft.Extensions.Hosting.Environments.Development, StringComparison.OrdinalIgnoreCase);

        if (!isDevelopment && devSeamsEnabled)
        {
            throw new InvalidOperationException(
                $"DevSeams is enabled in environment \"{environmentName}\" — only \"Development\" may ever enable it " +
                "(SLICE_S1_CONTRACT.md §1b: \"a runtime-tunable that swaps fake payment/crypto backends from the ops " +
                "desk must be structurally impossible\"). Allowlisting Development (not blocklisting Production) means " +
                "Staging/QA/Preview/Production all fail closed here, never just the one name a boolean happened to check.");
        }

        if (!isDevelopment && !keyVaultEndpointConfigured)
        {
            throw new InvalidOperationException(
                $"Startup in environment \"{environmentName}\" with no Key Vault endpoint configured and DevSeams " +
                "disabled — IFieldKeyVault has no real backend to resolve (L18 fail-closed). Configure Key Vault " +
                "(post-OQ-3) before deploying to any environment other than Development.");
        }
    }
}
