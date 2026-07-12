using System.Text.Json;
using Svac.AdminHost.Domain.Search;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// Pass D's own version of <c>backend/tests/Svac.Tests.AimlRouter/BudgetCapConfigKeyWiringTests.cs</c> —
/// the SAME class of dead-tunable bug, caught and fixed during THIS pass's own quota wiring rather than
/// shipped dead. SLICE_S5_CONTRACT.md §4's literal table spelling for the user-search quota cap is
/// <c>admin.user_search_daily_cap</c>; <see cref="Svac.DomainCore.Quota.QuotaService.Consume"/> resolves
/// its cap from the 9A key <c>quota.&lt;quotaKey&gt;.cap</c> (QuotaService.cs:24), and
/// <see cref="AdminQuotaKeys.UserSearchDaily"/> is <c>"admin.user_search.daily"</c> (SLICE_S5_CONTRACT.md
/// §5) — so the REAL runtime key <see cref="UserSearchExecutionService"/>'s quota check needs seeded is
/// <c>quota.admin.user_search.daily.cap</c>, never the contract table's literal spelling. This is a fast
/// (&lt;2s), deterministic, gate-lane test — it reads the checked-in manifest as JSON and reproduces
/// QuotaService.Consume's own key-derivation formula against it, never touching Postgres.
/// </summary>
public sealed class UserSearchQuotaCapConfigKeyWiringTests
{
    /// <summary>The exact formula QuotaService.cs:24 applies to every quotaKey it is ever called with.</summary>
    private static string RealRuntimeCapKeyFor(string quotaKey) => $"quota.{quotaKey}.cap";

    // Reads the checked-in manifest directly from the repo tree — mirrors V0BatchManifestTests.cs's own
    // RepoRoot()-relative loading (never AppContext.BaseDirectory content-copy plumbing, which this test
    // project's own .csproj does not set up for backend/admin-host/Svac.AdminHost/config/*.json).
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "docker-compose.yml")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("could not locate repo root from " + AppContext.BaseDirectory);
    }

    private static JsonElement LoadManifest()
    {
        var path = Path.Combine(RepoRoot(), "backend", "admin-host", "Svac.AdminHost", "config", "admin-host.config.json");
        Assert.True(File.Exists(path), $"expected the 9A manifest at {path}.");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    [Fact]
    public void RealManifest_SeedsTheKeyQuotaServiceActuallyReads_NeverTheContractsLiteralSpelling()
    {
        var expectedRealKey = RealRuntimeCapKeyFor(AdminQuotaKeys.UserSearchDaily);
        Assert.Equal("quota.admin.user_search.daily.cap", expectedRealKey); // pins the formula itself, independent of the manifest.

        var keys = LoadManifest().GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("key").GetString())
            .ToHashSet();

        Assert.Contains(expectedRealKey, keys); // the key UserSearchExecutionService's Consume call ACTUALLY needs.
        Assert.DoesNotContain("admin.user_search_daily_cap", keys); // the dead, contract-literal spelling must not silently come back.
    }

    [Fact]
    public void WhenTheRealKeyIsSeeded_UserSearchExecutionServicesQuotaCheck_MatchesItExactly()
    {
        Assert.Equal("quota.admin.user_search.daily.cap", RealRuntimeCapKeyFor(AdminQuotaKeys.UserSearchDaily));
    }

    [Fact]
    public void RealManifest_SeededValue_MatchesContractSection5V0Ceiling()
    {
        // SLICE_S5_CONTRACT.md §5 / §4: v0 value is 500 regardless of which key spelling carries it.
        var entry = LoadManifest().GetProperty("entries").EnumerateArray()
            .First(e => e.GetProperty("key").GetString() == RealRuntimeCapKeyFor(AdminQuotaKeys.UserSearchDaily));
        Assert.Equal(500, entry.GetProperty("value").GetInt32());
        Assert.Equal("ops", entry.GetProperty("scope").GetString());
    }
}
