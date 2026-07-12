using Microsoft.Extensions.DependencyInjection;
using Svac.AdminHost.Auth;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// SECURITY_REVIEW_S5.md S5-04 (fixNow): before this fix, <c>AddStaffAuth</c> ALWAYS persisted the
/// DataProtection key ring's raw XML (cookie/antiforgery signing key material) to
/// <c>core.data_protection_keys</c> in plaintext, regardless of environment. This is the RED->GREEN
/// regression proof at the DI-composition level (mirrors DevSeamsNotInProdDiTests.cs's own
/// "ServiceCollection, no full host boot" convention) — no live Postgres/Key Vault needed: the fail-closed
/// throw and the <c>.ProtectKeysWithAzureKeyVault</c> chaining both happen eagerly during composition,
/// never lazily inside a resolved factory (the exact S2 ValidateOnBuild lesson ProdStaffAuthGuard's own
/// doc comment already names).
/// </summary>
public sealed class DataProtectionKeyVaultGuardTests
{
    private const string FakeConnectionString = "Host=localhost;Database=svac-di-check-only";

    [Fact]
    public void NonDevSeamsBoot_WithNoKeyVaultKeyIdentifier_ThrowsFailClosed_PlaintextPathNeverWired()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddStaffAuth(FakeConnectionString, devSeamsEnabled: false, new StaffAuthEntraConfig(null, null, null)));

        Assert.Contains("S5-04", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonDevSeamsBoot_WithAKeyVaultKeyIdentifierConfigured_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var keyIdentifier = new Uri("https://example-vault.vault.azure.net/keys/svac-admin-dataprotection");

        // No throw == pass. ProtectKeysWithAzureKeyVault's own credential resolution (DefaultAzureCredential)
        // is lazy — constructing it here never touches the network, exactly why this is a fast, deterministic
        // gate test rather than a live-E2E-only proof.
        services.AddStaffAuth(FakeConnectionString, devSeamsEnabled: false, new StaffAuthEntraConfig(null, null, null), keyIdentifier);
    }

    [Fact]
    public void DevSeamsBoot_WithNoKeyVaultKeyIdentifier_DoesNotThrow_ThePlaintextPathIsDevSeamsOnly()
    {
        var services = new ServiceCollection();

        // devSeamsEnabled: true is only ever reachable in Development in a real boot
        // (ProdFieldKeyVaultGuard.Enforce throws first otherwise) — the plaintext CoreDbXmlRepository path
        // stays legal exactly there, never silently elsewhere.
        services.AddStaffAuth(FakeConnectionString, devSeamsEnabled: true, new StaffAuthEntraConfig(null, null, null));
    }
}
