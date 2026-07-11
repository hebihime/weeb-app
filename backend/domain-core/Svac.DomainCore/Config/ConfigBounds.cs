using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Persistence;

namespace Svac.DomainCore.Config;

/// <summary>
/// The §4 set-time bounds mechanism (SLICE_S2_CONTRACT.md §4; SECURITY_REVIEW_S2.md PII-S2-F1 /
/// TRUST-BREAK-1): "an unlawful or non-Claude-default policy cannot be *saved*, not merely not-served."
///
/// Generic JSON-shape rules keyed by 9A key NAME, never a typed reference to a module's own record types
/// (domain-core must never depend on a module — 1A: AimlRouter already depends on domain-core, so the
/// reverse reference would be circular). This mirrors the same discipline <c>LawfulBasisResolver</c>
/// already applies to stream names ("Ledger", "Consent", ...) without referencing the modules that own
/// those streams — a small, hardcoded, cross-cutting code table is the correct shape for a rule that by
/// definition spans every module's config keys, exactly like the 4A policy table and the 9A config
/// registry itself are both declarative, centrally-owned tables.
///
/// Every rule here throws <see cref="ArgumentException"/> (never mutates anything) — <c>ConfigRegistry.
/// SetValue</c> calls this BEFORE touching the tracked config row, so a rejected Set leaves the stored
/// value byte-for-byte unchanged and appends no audit event.
/// </summary>
internal static class ConfigBounds
{
    private const string ProviderAllowlistKey = "aiml.provider_allowlist";
    private const string RoutingPolicyKey = "aiml.routing_policy";
    private const string InvokeTimeoutSecondsKey = "aiml.invoke_timeout_seconds";

    public static async Task ValidateAsync(string key, string valueJson, string? boundsJson, CoreDbContext db, CancellationToken ct)
    {
        switch (key)
        {
            case ProviderAllowlistKey:
                ValidateProviderAllowlist(valueJson);
                break;
            case RoutingPolicyKey:
                await ValidateRoutingPolicy(valueJson, db, ct);
                break;
            case InvokeTimeoutSecondsKey:
                ValidateInvokeTimeoutSeconds(valueJson);
                break;
            default:
                break; // these 3 AimlRouter keys carry cross-key/shape rules too specific for the generic check below.
        }

        // OPS-3 (SECURITY_REVIEW_S3.md): every OTHER 9A key's declared numeric bounds (row.BoundsJson,
        // seeded from the manifest's own "bounds" field — SLICE_S3_CONTRACT.md §4) are now enforced HERE,
        // on the real ConfigRegistry.SetValue write path, not just inside the DevSeams grace-days
        // diagnostic endpoint. Absent BoundsJson (the overwhelming majority of keys today) is a no-op,
        // identical to today's behavior.
        ValidateGenericNumericBounds(key, valueJson, boundsJson);
    }

    /// <summary>
    /// A generic <c>[min, max]</c> inclusive-range check over any key whose manifest row declares
    /// <c>"bounds": [min, max]</c> (OPS-3, SECURITY_REVIEW_S3.md) — e.g. <c>identity.export.daily_cap</c>
    /// (floor 1: "no ops edit can zero a legal right") and <c>identity.deletion.grace_days</c> ([0,30]:
    /// "keeps the whole pipeline inside GDPR's one-month clock"). Only applies when BOTH the declared
    /// bounds AND the value being set are JSON numbers — a non-numeric key with a bounds row (none exist
    /// today) or a bounds-less key both fall through unchanged, exactly like every rule above.
    /// </summary>
    private static void ValidateGenericNumericBounds(string key, string valueJson, string? boundsJson)
    {
        if (string.IsNullOrWhiteSpace(boundsJson))
        {
            return;
        }

        using var boundsDoc = JsonDocument.Parse(boundsJson);
        if (boundsDoc.RootElement.ValueKind != JsonValueKind.Array || boundsDoc.RootElement.GetArrayLength() != 2)
        {
            return;
        }

        var min = boundsDoc.RootElement[0].GetDouble();
        var max = boundsDoc.RootElement[1].GetDouble();

        using var valueDoc = JsonDocument.Parse(valueJson);
        if (valueDoc.RootElement.ValueKind != JsonValueKind.Number)
        {
            return;
        }

        var value = valueDoc.RootElement.GetDouble();
        if (value < min || value > max)
        {
            throw new ArgumentException(
                $"9A bounds: {key}={value} is outside the declared bounds [{min},{max}] (SLICE_S3_CONTRACT.md §4).",
                nameof(valueJson));
        }
    }

    /// <summary>
    /// SLICE_S2_CONTRACT.md §4/§1b: "refuses saving any allowlist entry with special_category_ok: true
    /// until S17 exists" — the second of the "two independent locks" on special-category vendor egress
    /// (the first is the always-registered RefuseAllSpecialCategoryAuthorizer).
    /// </summary>
    private static void ValidateProviderAllowlist(string valueJson)
    {
        using var doc = JsonDocument.Parse(valueJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return; // shape mismatch is a deserialization concern for the reader, not this bounds rule.
        }

        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (entry.TryGetProperty("special_category_ok", out var flag) && flag.ValueKind == JsonValueKind.True)
            {
                var name = entry.TryGetProperty("name", out var n) ? n.GetString() : "<unknown>";
                throw new ArgumentException(
                    $"9A bounds: aiml.provider_allowlist entry \"{name}\" sets special_category_ok:true — " +
                    "refused until S17's consent-ledger-backed IVendorEgressAuthorizer exists " +
                    "(SLICE_S2_CONTRACT.md §1b/§4: the two-independent-locks law).", nameof(valueJson));
            }
        }
    }

    /// <summary>
    /// SLICE_S2_CONTRACT.md §4: "a routing_policy naming a provider absent from the allowlist, or a
    /// model absent from that provider's declared models list ..., or whose default_chain[0] resolves to
    /// family != \"claude\" fails bounds at SetValue." Cross-key: reads the CURRENTLY COMMITTED
    /// aiml.provider_allowlist row to resolve the named provider — the same snapshot Resolver.Resolve
    /// itself reads at call time, so a policy this rule accepts is a policy the resolver can serve.
    /// </summary>
    private static async Task ValidateRoutingPolicy(string valueJson, CoreDbContext db, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(valueJson);
        if (!doc.RootElement.TryGetProperty("default_chain", out var chain)
            || chain.ValueKind != JsonValueKind.Array
            || chain.GetArrayLength() == 0)
        {
            return; // an empty/absent default chain is a routing gap (NoRouteConfigured), not an unlawful default.
        }

        var first = chain[0];
        var provider = first.TryGetProperty("provider", out var p) ? p.GetString() : null;
        var model = first.TryGetProperty("model", out var m) ? m.GetString() : null;

        var allowlistRow = await db.ConfigEntries.SingleOrDefaultAsync(e => e.Key == ProviderAllowlistKey, ct);
        if (allowlistRow is null)
        {
            throw new ArgumentException(
                "9A bounds: aiml.routing_policy cannot be validated — aiml.provider_allowlist is not seeded yet " +
                "(SLICE_S2_CONTRACT.md §4).", nameof(valueJson));
        }

        using var allowlistDoc = JsonDocument.Parse(allowlistRow.ValueJson);
        JsonElement? matched = null;
        foreach (var entry in allowlistDoc.RootElement.EnumerateArray())
        {
            if (entry.TryGetProperty("name", out var nameEl) && string.Equals(nameEl.GetString(), provider, StringComparison.Ordinal))
            {
                matched = entry;
                break;
            }
        }

        if (matched is null)
        {
            throw new ArgumentException(
                $"9A bounds: aiml.routing_policy default_chain[0] names provider \"{provider}\" absent from " +
                "aiml.provider_allowlist (SLICE_S2_CONTRACT.md §4).", nameof(valueJson));
        }

        var family = matched.Value.TryGetProperty("family", out var familyEl) ? familyEl.GetString() : null;
        if (!string.Equals(family, "claude", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"9A bounds: aiml.routing_policy default_chain[0] resolves to family \"{family}\" != \"claude\" " +
                "(CLAUDE.md: Claude is the default provider; SLICE_S2_CONTRACT.md §4/Correction 1).", nameof(valueJson));
        }

        var models = matched.Value.TryGetProperty("models", out var modelsEl)
            ? modelsEl.EnumerateArray().Select(e => e.GetString()).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string?>();
        if (model is null || !models.Contains(model))
        {
            throw new ArgumentException(
                $"9A bounds: aiml.routing_policy default_chain[0] names model \"{model}\" undeclared for " +
                $"provider \"{provider}\" (SLICE_S2_CONTRACT.md §4: \"the allowlist models list must contain " +
                "whatever default_chain[0].model names\").", nameof(valueJson));
        }
    }

    /// <summary>SLICE_S2_CONTRACT.md §4: "bounds [5, 300]" for aiml.invoke_timeout_seconds.</summary>
    private static void ValidateInvokeTimeoutSeconds(string valueJson)
    {
        using var doc = JsonDocument.Parse(valueJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Number)
        {
            return;
        }

        var seconds = doc.RootElement.GetInt32();
        if (seconds < 5 || seconds > 300)
        {
            throw new ArgumentException(
                $"9A bounds: aiml.invoke_timeout_seconds={seconds} is outside the declared bounds [5, 300] " +
                "(SLICE_S2_CONTRACT.md §4).", nameof(valueJson));
        }
    }
}
