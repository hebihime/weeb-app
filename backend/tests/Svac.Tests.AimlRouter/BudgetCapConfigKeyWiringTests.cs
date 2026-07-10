using System.Text.Json;
using Svac.AimlRouter.Quota;
using Xunit;

namespace Svac.Tests.AimlRouter;

/// <summary>
/// Regression test for a REAL bug found by backend/e2e/aiml-router.e2e.mjs's live validation (that
/// file's own header, FINDING 1) — reproduced live against real Postgres + the real
/// <see cref="Svac.DomainCore.Quota.QuotaService"/> while building this test-author pass, not inferred
/// from reading the code alone. FIXED in the Build phase (this file's own docstring told the builder
/// exactly how, and instructed the builder to flip this test in the SAME change — done here): the
/// manifest now seeds <c>quota.aiml.call.daily.cap</c> directly (see
/// <see cref="Svac.AimlRouter.Config.AimlRouterConfigKeys.DailyCallCeiling"/>'s own updated doc comment),
/// renamed from the contract §4 table's literal <c>aiml.daily_call_ceiling</c>, which nothing ever read.
///
/// <para>
/// <see cref="Svac.DomainCore.Quota.QuotaService.Consume"/> resolves its cap from the 9A key
/// <c>quota.&lt;quotaKey&gt;.cap</c> (QuotaService.cs line 24: <c>configRegistry.GetValue&lt;int&gt;
/// ($"quota.{quotaKey}.cap", ct)</c>), and <see cref="AimlRouterQuotaKeys.CallDaily"/> is
/// <c>"aiml.call.daily"</c> (SLICE_S2_CONTRACT.md §5) — so the REAL runtime key
/// <see cref="AimlRouterService"/>'s budget check needs seeded is <c>quota.aiml.call.daily.cap</c>.
/// Before the fix, the shipped manifest seeded <c>aiml.daily_call_ceiling</c> instead — a key
/// <c>QuotaService.Consume</c> never reads. Empirically verified while this bug was live: booting a real
/// host with that manifest and calling <c>IAimlRouter.InvokeAsync</c> threw an unhandled
/// <c>KeyNotFoundException("9A config key \"quota.aiml.call.daily.cap\" is not registered — seed it via
/// the manifest loader before reading it.")</c> on EVERY call, success or failure path alike, because the
/// budget check (step 2) runs before resolve/dispatch (step 3+). No existing unit test caught it at the
/// time: <c>AimlRouterServiceTests.cs</c> injects <c>FakeQuotaService</c>, which never replicates
/// <c>QuotaService</c>'s real <c>quota.&lt;key&gt;.cap</c> convention — invisible to any test that fakes
/// <see cref="Svac.DomainCore.Contracts.Quota.IQuotaService"/> rather than reading the real manifest
/// against the real consumer's real key-derivation rule, which is exactly what this test does.
/// </para>
///
/// <para>
/// This is a fast (&lt;2s), deterministic, gate-lane test — it reads the checked-in manifest file as
/// JSON and reproduces <see cref="Svac.DomainCore.Quota.QuotaService.Consume"/>'s own key-derivation
/// formula against it, never touching Postgres. Kept permanently as a regression guard: any future edit
/// that renames the manifest key away from the real runtime formula, or renames
/// <see cref="AimlRouterQuotaKeys.CallDaily"/> without updating the manifest to match, fails this test
/// immediately instead of silently reintroducing the KeyNotFoundException.
/// </para>
/// </summary>
public sealed class BudgetCapConfigKeyWiringTests
{
    /// <summary>The exact formula QuotaService.cs:24 applies to every quotaKey it is ever called with.</summary>
    private static string RealRuntimeCapKeyFor(string quotaKey) => $"quota.{quotaKey}.cap";

    private static JsonElement LoadManifest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Config", "aiml-router.config.json");
        Assert.True(File.Exists(path), $"expected the 9A manifest at {path} (copied via the test project's own Content item).");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    [Fact]
    public void RealManifest_NowSeedsTheKeyQuotaServiceActuallyReads_BugFixed()
    {
        // Flipped from the pre-fix "KnownRealBug" framing per this file's own instructions: the manifest
        // key and QuotaService.Consume's own key-derivation formula must now agree exactly.
        var expectedRealKey = RealRuntimeCapKeyFor(AimlRouterQuotaKeys.CallDaily);
        Assert.Equal("quota.aiml.call.daily.cap", expectedRealKey); // pins the formula itself, independent of the manifest.

        var keys = LoadManifest().GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("key").GetString())
            .ToHashSet();

        Assert.Contains(expectedRealKey, keys); // the key AimlRouterService's budget check ACTUALLY needs — now seeded.
        Assert.DoesNotContain("aiml.daily_call_ceiling", keys); // the dead pre-fix spelling must not silently come back.
    }

    [Fact]
    public void WhenTheRealKeyIsSeeded_AimlRouterServicesBudgetCheckKeyDerivation_MatchesItExactly()
    {
        // The fix's exact shape: backend/modules/AimlRouter/config/aiml-router.config.json seeds
        // "quota.aiml.call.daily.cap", so AimlRouterService.ConsumeDailyBudget ->
        // QuotaService.Consume(actor, AimlRouterQuotaKeys.CallDaily, ...) resolves EXACTLY this key.
        Assert.Equal("quota.aiml.call.daily.cap", RealRuntimeCapKeyFor(AimlRouterQuotaKeys.CallDaily));
    }

    [Fact]
    public void RealManifest_SeededValue_MatchesContractSection5V0Ceiling()
    {
        // SLICE_S2_CONTRACT.md §5 / §4: v0 value is 10000 regardless of which key spelling carries it.
        var entry = LoadManifest().GetProperty("entries").EnumerateArray()
            .First(e => e.GetProperty("key").GetString() == RealRuntimeCapKeyFor(AimlRouterQuotaKeys.CallDaily));
        Assert.Equal(10000, entry.GetProperty("value").GetInt32());
        Assert.Equal("ops", entry.GetProperty("scope").GetString());
    }
}
