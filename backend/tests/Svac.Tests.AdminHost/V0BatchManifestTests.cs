using System.Text.Json;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// SLICE_S5_CONTRACT.md §4/§10.2: "v0-batch manifest completeness vs a checked-in transcription of
/// eng-review §5 (a dropped row = a test failure)" + "pending_consumer_slice lint honest both
/// directions." Reads the REAL committed manifest files directly as JSON (never through
/// Svac.DomainCore.Config.ConfigManifestEntry, so this test compiles and is meaningfully RED today,
/// before Pass C adds that record's additive <c>pending_consumer_slice</c> property) and compares them,
/// key-for-key, against <see cref="V0BatchTranscription"/> and <see cref="AdminHostTunablesTranscription"/>
/// below -- both hand-transcribed from SLICE_S5_CONTRACT.md §4's two tables and checked into THIS file,
/// so a future edit to either table is a reviewable diff here too, never a silent drift.
/// </summary>
public sealed class V0BatchManifestTests
{
    private sealed record ExpectedEntry(string Key, string Scope, string Type, string ValueJson, string? PendingConsumerSlice);

    // SLICE_S5_CONTRACT.md §4, first table ("Admin-host tunables") -- every one of these five keys has a
    // REAL S5 consumer by the time Pass A/B/C ship (cookie lifetime, revalidation interval, the §5 quota
    // cap, the executor's four-eyes step, the 13A retention verb) -- none is ever pending.
    private static readonly ExpectedEntry[] AdminHostTunablesTranscription =
    {
        new("admin.session_lifetime_hours", "ops", "int", "8", null),
        new("admin.session_revalidate_seconds", "ops", "int", "300", null),
        new("admin.user_search_daily_cap", "ops", "int", "500", null),
        new("admin.four_eyes_required", "founder", "bool", "false", null),
        new("admin.staff_pii_retention_years", "founder", "int", "6", null),
    };

    // SLICE_S5_CONTRACT.md §4, second table (the eng-review §5 v0 batch) -- 36 keys, transcribed verbatim.
    private static readonly ExpectedEntry[] V0BatchTranscription =
    {
        new("verification.age_gate_challenge_threshold", "founder", "int", "21", "S18"),
        new("verification.reverify_deadline_days", "founder", "int", "7", "S18"),
        new("integrity.minor_report_rate_limit", "founder", "json", """{"count":3,"window_days":30}""", "S12"),
        new("integrity.l4_reporter_tenure", "founder", "json", """{"days":30,"requires_photo_verified":true,"fallback_days":90}""", "S12"),
        new("match.swipe_cap_free_daily", "founder", "int", "100", "S14"),
        new("premium.price_usd_monthly", "founder", "number", "9.99", "S23"),
        new("premium.grace_days", "founder", "int", "16", "S23"),
        new("romantic.superlike_budget", "founder", "json", """{"free":1,"premium":3}""", "S20"),
        new("romantic.pending_ttl", "founder", "string", "\"disabled\"", "S20"),
        new("nakama.daily_budget", "founder", "int", "3", "S34"),
        new("match.reciprocity_signal_budget", "ops", "int", "30", "S14"),
        new("battle.freemium_limit", "founder", "json", """{"free":5,"premium":20}""", "S22"),
        new("invite.combined_budget_r8", "founder", "json", """{"free":5,"premium":25}""", "S28"),
        new("crew.captain_invite_daily", "founder", "int", "10", "S27"),
        new("premium.dm_baseline_daily", "founder", "int", "5", "S23"),
        new("economy.gifting_daily", "founder", "int", "5", "S35"),
        new("economy.trade_status", "set", "json", """{"free":true,"new_account_cooldown_days":7,"min_level":5}""", "S35"),
        new("ads.sponsored_card_ratio", "founder", "json", """{"operating_one_in":25,"structural_ceiling_one_in":15}""", "S30"),
        new("characters.ai_card_frequency_cap_one_in", "founder", "int", "20", "S25"),
        new("quest.party_cap", "set", "int", "8", "S33"),
        new("crew.squad_cap", "set", "int", "11", "S27"),
        new("heatmap.cell_provenance_days", "set", "int", "365", "S32"),
        new("heatmap.cell_history_months", "set", "int", "12", "S32"),
        new("heatmap.residential_geohash_floor", "ops", "int", "5", "S32"),
        new("nakama.radius_km", "ops", "int", "25", "S34"),
        new("nakama.presence_ttl_days", "ops", "int", "14", "S34"),
        new("nakama.per_pair_cap", "ops", "json", """{"count":3,"window_days":30}""", "S34"),
        new("battle.modal_ttl_seconds", "set", "int", "90", "S22"),
        new("battle.resume_window", "set", "string", "\"same_con_day\"", "S22"),
        new("match.category_density_floor", "ops", "int", "30", "S19"),
        new("match.category_density_resume", "ops", "int", "40", "S19"),
        new("match.pass_reserve", "set", "json", """{"hours":48,"alt":"next_con_day","max":2}""", "S14"),
        new("match.exposure_floor", "ops", "json", """{"eligible_min_serve_per_active_con_day":1,"watch_tier_per_days":2,"restricted_exempt":true}""", "S19"),
        new("quest.spot_check_rates", "set", "json", """{"general":0.05,"sponsored":0.10}""", "S30"),
        new("quest.kill_bonus_threshold", "set", "number", "0.5", "S33"),
        new("economy.svac_weekly_cap", "set", "int", "50", "S35"),
    };

    /// <summary>Every real BUILD.md §7 ledger slice id a v0-batch key may legally point at
    /// (SLICE_S5_CONTRACT.md §4's "pending consumer" column) -- transcribed from BUILD.md's own ledger
    /// table so a typo'd or invented slice id fails the lint exactly like a totally dead key would.</summary>
    private static readonly IReadOnlySet<string> RealLedgerSlices = new HashSet<string>
    {
        "S12", "S14", "S18", "S19", "S20", "S22", "S23", "S25", "S27", "S28", "S30", "S32", "S33", "S34", "S35",
    };

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "docker-compose.yml")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("could not locate repo root from " + AppContext.BaseDirectory);
    }

    private static JsonElement[] LoadRealEntries(string relativeManifestPath)
    {
        var path = Path.Combine(RepoRoot(), relativeManifestPath);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("entries").EnumerateArray().Select(e => e.Clone()).ToArray();
    }

    [Fact]
    public void AdminHostConfigJson_SeedsExactlyTheFiveHostTunables_NoneDropped_NonePhantom()
    {
        var real = LoadRealEntries("backend/admin-host/Svac.AdminHost/config/admin-host.config.json");
        AssertManifestMatchesTranscription(real, AdminHostTunablesTranscription);
    }

    [Fact]
    public void V0BatchConfigJson_SeedsExactlyTheThirtySixEngReviewKeys_NoneDropped_NonePhantom()
    {
        var real = LoadRealEntries("backend/admin-host/Svac.AdminHost/config/v0-batch.config.json");
        AssertManifestMatchesTranscription(real, V0BatchTranscription);
    }

    [Fact]
    public void V0BatchConfigJson_NeverDuplicates_CoreConDayCutoff()
    {
        // §4: "core.con_day_cutoff already S1-seeded -- never duplicated (union-merge, one truth)."
        var real = LoadRealEntries("backend/admin-host/Svac.AdminHost/config/v0-batch.config.json");
        Assert.DoesNotContain(real, e => e.GetProperty("key").GetString() == "core.con_day_cutoff");
        var hostTunables = LoadRealEntries("backend/admin-host/Svac.AdminHost/config/admin-host.config.json");
        Assert.DoesNotContain(hostTunables, e => e.GetProperty("key").GetString() == "core.con_day_cutoff");
    }

    [Fact]
    public void V0BatchConfigJson_NeverSeeds_TheThreeDeliberatelyExcludedKeys()
    {
        // §4: "Deliberately NOT seeded ... nakama_rep_floor ... epsilon-budget-per-principal ... annual Premium."
        var real = LoadRealEntries("backend/admin-host/Svac.AdminHost/config/v0-batch.config.json");
        var keys = real.Select(e => e.GetProperty("key").GetString()).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(keys, k => k != null && k.Contains("nakama_rep_floor", StringComparison.Ordinal));
        Assert.DoesNotContain(keys, k => k != null && k.Contains("epsilon", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(keys, k => k != null && k.Contains("annual", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertManifestMatchesTranscription(JsonElement[] real, ExpectedEntry[] expected)
    {
        var byKey = real.ToDictionary(e => e.GetProperty("key").GetString()!, e => e, StringComparer.Ordinal);

        var missing = expected.Where(e => !byKey.ContainsKey(e.Key)).Select(e => e.Key).ToArray();
        Assert.True(missing.Length == 0, $"manifest is missing key(s) present in the checked-in transcription: {string.Join(", ", missing)}");

        var expectedKeys = expected.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var phantom = byKey.Keys.Where(k => !expectedKeys.Contains(k)).ToArray();
        Assert.True(phantom.Length == 0, $"manifest seeds key(s) absent from the checked-in transcription (verify against SLICE_S5_CONTRACT.md §4 before adding): {string.Join(", ", phantom)}");

        foreach (var exp in expected)
        {
            var actual = byKey[exp.Key];
            Assert.Equal(exp.Scope, actual.GetProperty("scope").GetString());
            Assert.Equal(exp.Type, actual.GetProperty("type").GetString());
            Assert.True(
                JsonValuesEqual(JsonDocument.Parse(exp.ValueJson).RootElement, actual.GetProperty("value")),
                $"key \"{exp.Key}\": expected value {exp.ValueJson}, manifest has {actual.GetProperty("value").GetRawText()}");

            var pending = actual.TryGetProperty("pending_consumer_slice", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            Assert.Equal(exp.PendingConsumerSlice, pending);
        }
    }

    private static bool JsonValuesEqual(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var aProps = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var bProps = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                return aProps.Count == bProps.Count && aProps.All(kv => bProps.TryGetValue(kv.Key, out var bv) && JsonValuesEqual(kv.Value, bv));
            case JsonValueKind.Array:
                var aItems = a.EnumerateArray().ToArray();
                var bItems = b.EnumerateArray().ToArray();
                return aItems.Length == bItems.Length && aItems.Zip(bItems).All(pair => JsonValuesEqual(pair.First, pair.Second));
            case JsonValueKind.Number:
                return a.GetDouble() == b.GetDouble();
            case JsonValueKind.String:
                return a.GetString() == b.GetString();
            default:
                return a.GetRawText() == b.GetRawText();
        }
    }

    // -------------------- pending_consumer_slice lint, honest both directions --------------------
    //
    // INTERFACE-SKETCH (SLICE_S5_CONTRACT.md §4 judge synthesis §12.7; PHASE_2A_SUBSTRATE.md §6: this
    // field + its CI wiring is shared node tooling under tools/, Pass C's own deliverable -- NOT
    // implemented by the test-author). The tests below pin the pure DECISION function's contract (the
    // "honest both directions" half a single manifest file can prove); the repo-wide "a slice marked
    // DONE in BUILD.md with no registered consumer claiming the key" CI wiring is Pass C's tools/ script,
    // which this decision function is intended to back:
    //
    //   namespace Svac.AdminHost.Domain.Config
    //   {
    //       public static class PendingConsumerSliceLint
    //       {
    //           public sealed record Entry(string Key, string Consumer, string? PendingConsumerSlice);
    //
    //           // A key passes iff EITHER its own manifest `consumer` field is non-empty (a REAL,
    //           // already-shipped consumer), OR `pendingConsumerSlice` names a member of
    //           // `knownLedgerSlices` (a real BUILD.md §7 row) -- P2's "desk_rendered" satisfaction mode
    //           // is REJECTED (§12.7): the desk renders every key regardless, so rendering can never be
    //           // what makes a key legal. Returns the violation messages; empty = the manifest passes.
    //           public static IReadOnlyList<string> Validate(IReadOnlyList<Entry> entries, IReadOnlySet<string> knownLedgerSlices);
    //       }
    //   }

    [Fact]
    public void PendingConsumerSliceLint_ARowNamingARealNotYetDoneSlice_Passes()
    {
        var entries = new[] { new Svac.AdminHost.Domain.Config.PendingConsumerSliceLint.Entry("verification.age_gate_challenge_threshold", "", "S18") };
        Assert.Empty(Svac.AdminHost.Domain.Config.PendingConsumerSliceLint.Validate(entries, RealLedgerSlices));
    }

    [Fact]
    public void PendingConsumerSliceLint_ARowWithNeitherARealConsumerNorAPendingSlice_Fails()
    {
        var entries = new[] { new Svac.AdminHost.Domain.Config.PendingConsumerSliceLint.Entry("test.totally_dead_key", "", null) };
        var violations = Svac.AdminHost.Domain.Config.PendingConsumerSliceLint.Validate(entries, RealLedgerSlices);
        Assert.Contains(violations, v => v.Contains("test.totally_dead_key", StringComparison.Ordinal));
    }

    [Fact]
    public void PendingConsumerSliceLint_ARowNamingAnInventedSliceId_Fails_ATypoIsNotAFreePass()
    {
        var entries = new[] { new Svac.AdminHost.Domain.Config.PendingConsumerSliceLint.Entry("test.typo_slice_key", "", "S999") };
        var violations = Svac.AdminHost.Domain.Config.PendingConsumerSliceLint.Validate(entries, RealLedgerSlices);
        Assert.Contains(violations, v => v.Contains("test.typo_slice_key", StringComparison.Ordinal));
    }

    [Fact]
    public void PendingConsumerSliceLint_ARowWithARealConsumer_PassesEvenWithNoPendingSliceNamed()
    {
        var entries = new[] { new Svac.AdminHost.Domain.Config.PendingConsumerSliceLint.Entry("admin.session_lifetime_hours", "cookie ticket lifetime (AddStaffAuth)", null) };
        Assert.Empty(Svac.AdminHost.Domain.Config.PendingConsumerSliceLint.Validate(entries, RealLedgerSlices));
    }

    [Fact]
    public void PendingConsumerSliceLint_TheRealV0BatchManifest_HasZeroViolations()
    {
        var real = LoadRealEntries("backend/admin-host/Svac.AdminHost/config/v0-batch.config.json");
        var entries = real.Select(e => new Svac.AdminHost.Domain.Config.PendingConsumerSliceLint.Entry(
            e.GetProperty("key").GetString()!,
            e.TryGetProperty("consumer", out var c) ? c.GetString() ?? "" : "",
            e.TryGetProperty("pending_consumer_slice", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null)).ToArray();

        Assert.Empty(Svac.AdminHost.Domain.Config.PendingConsumerSliceLint.Validate(entries, RealLedgerSlices));
    }
}
