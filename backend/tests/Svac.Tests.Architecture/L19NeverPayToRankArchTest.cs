using Svac.DomainCore.Contracts;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// L19 never-pay-to-rank (SLICE_S1_CONTRACT.md §8: "arch-rule slots land now, binding future types by
/// name" — BUILD.md §8.1 line 321, §8.383). Mirrors ProviderSdkArchTest's 15A slot exactly: a name-bound
/// scan over every backend assembly for a ranking/ordering-shaped type, failing the build if any such
/// type reads a purchasable/paid signal. Vacuously green today (S1 ships zero ranking/deck types) —
/// arms the instant S25's character 18+ metric gate or any future deck-ranking sort key lands.
/// </summary>
public sealed class L19NeverPayToRankArchTest
{
    private static readonly string[] ForbiddenPaidSignalPatterns =
    {
        "premium", "tier", "purchase", "boost", "paid", "subscription", "iap", "payment",
    };

    private static readonly string[] RankingOrderingTypeNameMarkers =
    {
        "Ranking", "RankBy", "Deck", "SortKey", "Ordering", "OrderBy",
    };

    [Fact]
    public void NoRankingOrOrderingType_ReadsAPurchasableSignal()
    {
        var assembliesToScan = new[]
        {
            typeof(RequestContext).Assembly, // Svac.DomainCore.Contracts
            typeof(Svac.DomainCore.Deterministic.Ulid).Assembly,
            typeof(Svac.DomainCore.Persistence.CoreDbContext).Assembly, // Svac.DomainCore
            typeof(Svac.DomainCore.Hosting.PolicyResults).Assembly,
        };

        var violations = new List<string>();
        foreach (var assembly in assembliesToScan)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (!RankingOrderingTypeNameMarkers.Any(marker => type.Name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                violations.AddRange(ScanTypeMembers(type));
            }
        }

        Assert.Empty(violations);
    }

    /// <summary>Companion proof the rule is substantive, not a filename check — mirrors TrustBoundaryLensTests.RedFixture_PayToRankOrderingKey_IsDetectableByAnL19Scan against THIS shipped scan.</summary>
    [Fact]
    public void RedFixture_PayToRankOrderingKey_IsDetectableByThisScan()
    {
        var violations = ScanTypeMembers(typeof(FixtureDeckRankingKey));

        Assert.Contains(violations, v => v.Contains("PremiumBoost", StringComparison.Ordinal));
    }

    private static List<string> ScanTypeMembers(Type type)
    {
        var violations = new List<string>();
        foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (ForbiddenPaidSignalPatterns.Any(pattern => property.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            {
                violations.Add($"{type.Name}.{property.Name}");
            }
        }
        return violations;
    }

    // A future deck-ranking sort key that orders by a purchased boost — the exact L19 violation. Never
    // referenced outside this test.
    private sealed record FixtureDeckRankingKey(string CardId, int AttestationScore, int PremiumBoost);
}
