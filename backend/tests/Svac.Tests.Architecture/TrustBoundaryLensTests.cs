using System.Reflection;
using Svac.DomainCore.FieldEncryption;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// ADVERSARIAL trust-boundary lens (money-doors fail closed · never-pay-to-rank · DTO trust absence).
/// Every [Fact] here asserts the SECURE behavior S1's own contract / BUILD.md gate promises, and FAILS
/// against the shipped S1 code — each one demonstrates a hole, not a hypothetical.
///
/// Run just this file:
///   dotnet test backend/tests/Svac.Tests.Architecture \
///     --filter "FullyQualifiedName~TrustBoundaryLensTests"
/// </summary>
public sealed class TrustBoundaryLensTests
{
    // -----------------------------------------------------------------------------------------------
    // BREAK 1 — MONEY-DOOR NOT FAIL-CLOSED: DevSeams is permitted in EVERY non-Production environment,
    // not just Development.
    //
    // ProdFieldKeyVaultGuard.Enforce (backend/domain-core/Svac.DomainCore/FieldEncryption/
    // ProdFieldKeyVaultGuard.cs:14) takes a BOOLEAN `isProduction`, and Program.cs:37-40 feeds it
    // `builder.Environment.IsProduction()`. That boolean erases the difference between Development and
    // Staging/QA/Preview. The guard only throws when `isProduction && devSeamsEnabled` — so a Staging
    // deploy (ASPNETCORE_ENVIRONMENT=Staging → IsProduction()==false) with SVAC_DEVSEAMS_ENABLED=true
    // boots clean and DI (DomainCoreServiceCollectionExtensions.cs:60-66) registers:
    //   • DevSeamsPaymentService — Charge() ALWAYS returns Succeeded:true (the money door wide open), and
    //   • DevKeyringFieldKeyVault — special-category crypto keyed off the hardcoded, in-repo seed
    //     "svac-dev-keyring-NOT-FOR-PRODUCTION-USE" (DevKeyringFieldKeyVault.cs:18).
    //
    // Fail-closed means ALLOWLIST the one safe environment (Development), not BLOCKLIST the one unsafe
    // one (Production). The guard does the latter, so every environment that is neither is a fake-money,
    // fake-crypto surface. Input → wrong result: Enforce(isProduction:false, devSeamsEnabled:true) →
    // returns normally (host boots) where the secure result is "throw".
    // -----------------------------------------------------------------------------------------------
    [Fact]
    public void MoneyDoor_DevSeamsOutsideDevelopment_MustFailClosed()
    {
        // Exactly what a Staging/QA/Preview host produces at startup: not Production, DevSeams flag on,
        // no real Key Vault wired (OQ-3 still pending).
        var ex = Record.Exception(() => ProdFieldKeyVaultGuard.Enforce(
            environmentName: "Staging",
            devSeamsEnabled: true,
            keyVaultEndpointConfigured: false));

        Assert.NotNull(ex); // FAILS today: Enforce returns normally, so Staging boots the fake payment +
                            // fake-crypto seams. The guard cannot even see it is Staging — it is handed a
                            // bool that collapsed Development and Staging into one "not production".
    }

    // -----------------------------------------------------------------------------------------------
    // BREAK 2 — NEVER-PAY-TO-RANK (L19) HAS ZERO STRUCTURAL ENFORCEMENT AT S1.
    //
    // BUILD.md §8.1 (THE HARDENED GATE, line 321) lists "never-pay-to-rank" among the arch tests that
    // must be green before ANY slice — S1 included — is green. BUILD.md:383 defines L19 with enforcement
    // "arch test". SLICE_S1_CONTRACT.md §8 promises: "L19 rank-by-attestation / on-platform-REAL |
    // arch-rule slots land now, binding future types by name." 15A honored the identical "slot lands now,
    // vacuously green" promise with ProviderSdkArchTest. L19 shipped NOTHING — no rule, no red fixture,
    // no name-binding scan. Svac.Tests.Architecture.csproj's own header (lines 6-8) enumerates every
    // enforced rule and L19 is absent from that list.
    //
    // The invariant is real and enforceable (Fact `RedFixture...` below proves a pay-to-rank ordering key
    // is detectable). Its absence means the first future deck ORDER BY that sorts on a paid signal ships
    // with no gate firing. This Fact scans the arch assembly for the promised slot and finds none.
    // -----------------------------------------------------------------------------------------------
    [Fact]
    public void NeverPayToRank_TheL19ArchRuleSlotPromisedForS1_IsMissing()
    {
        var archTypes = typeof(ProviderSdkArchTest).Assembly.GetTypes();

        var l19Rules = archTypes.Where(t =>
                t.Name.Contains("PayToRank", StringComparison.OrdinalIgnoreCase)
                || t.Name.Contains("NeverPayToRank", StringComparison.OrdinalIgnoreCase)
                || t.Name.Contains("RankByAttestation", StringComparison.OrdinalIgnoreCase)
                || t.Name.Contains("Attestation", StringComparison.OrdinalIgnoreCase)
                || t.Name.Contains("L19", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name)
            .ToList();

        Assert.True(
            l19Rules.Count > 0,
            "No L19 / never-pay-to-rank arch rule exists in Svac.Tests.Architecture, but BUILD.md §8.1 " +
            "requires it green for S1 and SLICE_S1_CONTRACT.md §8 promises the slot lands at S1. 15A got " +
            "ProviderSdkArchTest; L19 got nothing.");
    }

    // Companion proof the L19 invariant is substantive, not a filename check: the scan a real L19 rule
    // would run (forbid a ranking/ordering key that reads a purchasable signal) DOES flag the fixture.
    // This Fact PASSES — it exists so BREAK 2 cannot be dismissed as "there is nothing to enforce yet."
    [Fact]
    public void RedFixture_PayToRankOrderingKey_IsDetectableByAnL19Scan()
    {
        // Field-name patterns that make a ranking input pay-to-rank / tier-boosted rather than
        // attestation-gated or deterministic (BUILD.md:383).
        string[] paidSignalPatterns =
        {
            "premium", "tier", "purchase", "boost", "paid", "subscription", "iap", "payment",
        };

        var offendingFields = typeof(FixtureDeckRankingKey)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => paidSignalPatterns.Any(pat =>
                p.Name.Contains(pat, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToList();

        Assert.Contains("PremiumBoost", offendingFields);
    }

    // A future deck-ranking sort key that orders by a purchased boost — the exact L19 violation. It
    // compiles and lives in the backend with ZERO gate failure today because no L19 rule scans for it.
    // Never referenced outside this test.
    private sealed record FixtureDeckRankingKey(string CardId, int AttestationScore, int PremiumBoost);
}
