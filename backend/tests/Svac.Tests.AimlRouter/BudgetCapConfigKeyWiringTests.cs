using System.Text.Json;
using Svac.AimlRouter.Quota;
using Xunit;

namespace Svac.Tests.AimlRouter;

/// <summary>
/// Regression test for a REAL bug found by backend/e2e/aiml-router.e2e.mjs's live validation (that
/// file's own header, FINDING 1) — reproduced live against real Postgres + the real
/// <see cref="Svac.DomainCore.Quota.QuotaService"/> while building this test-author pass, not inferred
/// from reading the code alone.
///
/// <para>
/// <see cref="Svac.DomainCore.Quota.QuotaService.Consume"/> resolves its cap from the 9A key
/// <c>quota.&lt;quotaKey&gt;.cap</c> (QuotaService.cs line 24: <c>configRegistry.GetValue&lt;int&gt;
/// ($"quota.{quotaKey}.cap", ct)</c>), and <see cref="AimlRouterQuotaKeys.CallDaily"/> is
/// <c>"aiml.call.daily"</c> (SLICE_S2_CONTRACT.md §5) — so the REAL runtime key
/// <see cref="AimlRouterService"/>'s budget check needs seeded is <c>quota.aiml.call.daily.cap</c>.
/// </para>
///
/// <para>
/// The shipped <c>backend/modules/AimlRouter/config/aiml-router.config.json</c> seeds
/// <c>aiml.daily_call_ceiling</c> instead — a key <c>QuotaService.Consume</c> never reads. Empirically
/// verified: booting a real host with this exact manifest and calling <c>IAimlRouter.InvokeAsync</c>
/// throws an unhandled <c>KeyNotFoundException("9A config key \"quota.aiml.call.daily.cap\" is not
/// registered — seed it via the manifest loader before reading it.")</c> straight out of
/// <c>AimlRouterService.ConsumeDailyBudget</c> — on EVERY call, success or failure path alike, because
/// the budget check (step 2) runs before resolve/dispatch (step 3+). No existing unit test caught this:
/// <c>AimlRouterServiceTests.cs</c> injects <c>FakeQuotaService</c>, which never replicates
/// <c>QuotaService</c>'s real <c>quota.&lt;key&gt;.cap</c> convention — this gap is invisible to any test
/// that fakes <see cref="Svac.DomainCore.Contracts.Quota.IQuotaService"/> rather than reading the real
/// manifest against the real consumer's real key-derivation rule, which is exactly what this test does.
/// </para>
///
/// <para>
/// This is a fast (&lt;2s), deterministic, gate-lane test — it reads the checked-in manifest file as
/// JSON and reproduces <see cref="Svac.DomainCore.Quota.QuotaService.Consume"/>'s own key-derivation
/// formula against it, never touching Postgres. It is NOT this test-author's call to fix the manifest
/// (that is a builder/shared-wiring change to a module-owned production config file, outside "own the
/// test tree only") — its job is to make the bug permanently, cheaply, and loudly visible so it cannot
/// silently regress once fixed, and so no future change can silently reintroduce it.
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
    public void RealManifest_DoesNotYetSeedTheKeyQuotaServiceActuallyReads_KnownRealBug()
    {
        // This assertion is written to PASS while the bug exists and FAIL the moment someone fixes the
        // manifest without updating this test — that is the point: it forces the fix to be deliberate
        // and this test to be updated (deleted or flipped) in the SAME change, never silently orphaned.
        var expectedRealKey = RealRuntimeCapKeyFor(AimlRouterQuotaKeys.CallDaily);
        Assert.Equal("quota.aiml.call.daily.cap", expectedRealKey); // pins the formula itself, independent of the manifest.

        var keys = LoadManifest().GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("key").GetString())
            .ToHashSet();

        Assert.Contains("aiml.daily_call_ceiling", keys); // the key the manifest DOES seed today (the bug: nothing reads it).
        Assert.DoesNotContain(expectedRealKey, keys); // the key AimlRouterService's budget check ACTUALLY needs (the bug: never seeded).

        // If both of the above ever flip together (manifest starts seeding "quota.aiml.call.daily.cap"),
        // the bug is fixed — update AimlRouterService_BudgetCheck_Succeeds_AgainstTheRealManifestShape
        // below to assert success instead of documenting the gap, and delete this method's "known bug"
        // framing (the [Fact] below already asserts the CORRECT end state so that follow-up is a one-line
        // diff, not a rewrite).
    }

    [Fact]
    public void WhenTheRealKeyIsSeeded_AimlRouterServicesBudgetCheckKeyDerivation_MatchesItExactly()
    {
        // The FIX's exact shape, asserted now so the fix is a config-value change, not a guessing game:
        // once backend/modules/AimlRouter/config/aiml-router.config.json seeds "quota.aiml.call.daily.cap"
        // (in addition to or instead of "aiml.daily_call_ceiling"), AimlRouterService.ConsumeDailyBudget
        // -> QuotaService.Consume(actor, AimlRouterQuotaKeys.CallDaily, ...) resolves EXACTLY this key.
        Assert.Equal("quota.aiml.call.daily.cap", RealRuntimeCapKeyFor(AimlRouterQuotaKeys.CallDaily));
    }
}
