namespace Svac.AdminHost.Auth;

/// <summary>
/// Prod-without-Entra THROWS at startup (SLICE_S5_CONTRACT.md §1b: "any non-Development boot without
/// complete Entra config throws" — the <c>ProdFieldKeyVaultGuard</c> family, S2's <c>ValidateOnBuild</c>
/// lesson applied here: a fail-closed guard must fire from a plain method called directly during
/// Program.cs's startup sequence, never from inside a lazily-resolved DI factory lambda, because
/// <c>ValidateOnBuild</c> only proves constructor-graph RESOLVABILITY — it never invokes a factory body).
/// Call this once, before <c>app.Build()</c>'s services are exercised by real traffic, exactly where
/// <c>ProdFieldKeyVaultGuard.Enforce</c> is called in this same Program.cs.
///
/// Trust-F1 (SECURITY_REVIEW_S1.md): allowlist the one safe environment (Development) BY NAME, never
/// blocklist the one unsafe one (Production) — Staging/QA/Preview all fail closed here too.
/// </summary>
public static class ProdStaffAuthGuard
{
    /// <param name="environmentName">The hosting environment's EnvironmentName (e.g. ASPNETCORE_ENVIRONMENT).</param>
    /// <param name="entraAuthorityConfigured">True iff a non-empty Entra authority URL is configured.</param>
    /// <param name="entraClientIdConfigured">True iff a non-empty Entra client id is configured.</param>
    /// <param name="entraClientSecretConfigured">True iff a client credential resolved from the Key Vault seam (2A) — never a repo secret.</param>
    public static void Enforce(
        string environmentName,
        bool entraAuthorityConfigured,
        bool entraClientIdConfigured,
        bool entraClientSecretConfigured)
    {
        var isDevelopment = string.Equals(environmentName, Microsoft.Extensions.Hosting.Environments.Development, StringComparison.OrdinalIgnoreCase);
        if (isDevelopment)
        {
            return; // Development boots on DevSeams fixtures alone — Entra config is a seam-now dependency (OQ-3).
        }

        var missing = new List<string>();
        if (!entraAuthorityConfigured)
        {
            missing.Add("Entra authority");
        }
        if (!entraClientIdConfigured)
        {
            missing.Add("Entra client id");
        }
        if (!entraClientSecretConfigured)
        {
            missing.Add("Entra client secret (Key Vault seam, 2A)");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Startup in environment \"{environmentName}\" with incomplete Entra config: missing " +
                $"{string.Join(", ", missing)}. SLICE_S5_CONTRACT.md §1b: \"any non-Development boot without " +
                "complete Entra config throws\" — Development is allowlisted BY NAME (Trust-F1), so " +
                "Staging/QA/Production all fail closed here, never just the one name a boolean would have checked.");
        }
    }
}
