using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Svac.AdminHost.Auth;
using Svac.AdminHost.Domain.Execution;
using Svac.AdminHost.Domain.I18n;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Hosting;

namespace Svac.AdminHost.ConfigRegistry;

/// <summary>
/// The Config Registry desk's SSR form-post surface (SLICE_S5_CONTRACT.md §0/§1c/§4/§8 seam 3, Pass C) —
/// THE LEDGER HEADLINE's mutation half. Every edit routes THROUGH <see cref="IAdminActionExecutor"/> on
/// the EXISTING <c>core.config.set.founder</c>/<c>core.config.set.ops</c> policy rows (§3/§4: "Config
/// edits add ZERO new rows") — this file itself NEVER calls <see cref="IConfigRegistry.SetValue{T}"/>
/// directly; every write happens inside the executor's own <c>work</c> delegate, exactly like <see
/// cref="Svac.AdminHost.Staff.StaffRolesEndpointExtensions"/>.
///
/// Two-phase flow for a founder-scope key (§4/§10.3's confirm-with-reason interstitial):
///   1. POST /config/{key}/edit — runs the REAL <see cref="IAdminActionExecutor"/> chokepoint (Authorize
///      + four-eyes) with a NO-OP <c>work</c> delegate. A denial is real and audited (admin.action.
///      refused) exactly as if the edit had been attempted for real; on success NOTHING is written (the
///      no-op work commits an empty transaction) and this handler renders the interstitial — the typed
///      old→new diff, a fresh antiforgery token, and an opaque <see cref="ConfigConfirmToken"/> sealing
///      the EXACT (key, newValueJson, reason) triple shown, so confirm cannot silently commit anything
///      other than what the operator actually saw.
///   2. POST /config/{key}/confirm — re-verifies the sealed triple, then runs the SAME chokepoint again
///      with the REAL <c>work</c> (IConfigRegistry.SetValue) — the ONLY point either write path ever
///      touches the config table.
/// An ops-scope key skips the interstitial entirely: one POST /config/{key}/edit call, REAL work, done —
/// §4's "ops-scope edits take an inline mandatory-reason field" (no second round trip).
///
/// A set-scope key is refused defensively at BOTH endpoints even though the editor renders no form for
/// one (§4: "the editor refuses scope='set'") — never trusting the UI's own omission as the only guard.
/// </summary>
public static class ConfigRegistryEndpointExtensions
{
    private const string ConfigRoute = "/config";
    private const string FounderAction = "core.config.set.founder";
    private const string OpsAction = "core.config.set.ops";

    public static WebApplication MapConfigRegistryEndpoints(this WebApplication app)
    {
        app.MapPost("/config/{key}/edit", HandlePropose).RequirePolicyAction("admin.host.transport");
        app.MapPost("/config/{key}/confirm", HandleConfirm).RequirePolicyAction("admin.host.transport");
        return app;
    }

    private static async Task<IResult> HandlePropose(
        string key,
        HttpContext httpContext,
        IAdminActionExecutor executor,
        IStaffContextProvider contextProvider,
        IConfigRegistry configRegistry,
        IAntiforgery antiforgery,
        ConfigConfirmToken confirmToken,
        AdminStringCatalog strings,
        CancellationToken ct)
    {
        var callerCtx = contextProvider.ForStaffOperation();
        if (RequireStaffActor(callerCtx) is { } refusal)
        {
            return refusal;
        }

        var decodedKey = Uri.UnescapeDataString(key);
        var form = httpContext.Request.Form;
        var rawNewValue = form["newValue"].ToString();
        var reason = form["reason"].ToString();

        var entries = await configRegistry.ListEntries(ct);
        var entry = entries.FirstOrDefault(e => string.Equals(e.Key, decodedKey, StringComparison.Ordinal));
        if (entry is null || entry.Scope == "set")
        {
            // §4: "the editor refuses scope='set'" — defense in depth against a hand-crafted POST to a
            // key the UI never rendered a form for, and against an unknown key entirely.
            return RedirectToConfigPage("admin.config.notice.error", errorKind: "denied", detail: null);
        }

        if (entry.RequiresReason && string.IsNullOrWhiteSpace(reason))
        {
            // A cheap, honest UX guard: ConfigRegistry.SetValue enforces this too (§4), but there is no
            // reason to walk a whole founder-scope Authorize round trip (or commit an ops edit) for a
            // reason this KEY already declares mandatory.
            return RedirectToConfigPage("admin.config.notice.reason_required", errorKind: "denied", detail: null);
        }

        string newValueJson;
        try
        {
            newValueJson = BuildValueJson(entry.Type, rawNewValue);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return RedirectToConfigPage("admin.config.notice.bounds_prefix", errorKind: "bounds", detail: ex.Message);
        }

        var target = new TargetRef("config_entry", decodedKey);

        if (entry.Scope == "ops")
        {
            return await CommitEdit(executor, configRegistry, callerCtx, OpsAction, target, decodedKey, newValueJson, reason, ct);
        }

        // founder-scope: dry-run the REAL chokepoint (Authorize/four-eyes, no-op work) before ever
        // showing the interstitial — a non-qualifying hat is denied HERE, exactly like a real edit
        // attempt, never silently shown a confirm form it could never actually commit.
        AdminActionResult dryRun;
        try
        {
            dryRun = await executor.Execute(callerCtx, FounderAction, target, reason, _ => Task.CompletedTask, ct);
        }
        catch (ArgumentException ex)
        {
            return RedirectToConfigPage("admin.config.notice.bounds_prefix", errorKind: "bounds", detail: ex.Message);
        }

        if (dryRun is not AdminActionResult.Success)
        {
            return MapRefusalToRedirect(dryRun);
        }

        var tokenSet = antiforgery.GetAndStoreTokens(httpContext);
        var sealedToken = confirmToken.Mint(decodedKey, newValueJson, reason);
        var html = RenderInterstitial(strings, decodedKey, entry, newValueJson, reason, sealedToken, tokenSet);
        return Results.Content(html, "text/html");
    }

    private static async Task<IResult> HandleConfirm(
        string key,
        HttpContext httpContext,
        IAdminActionExecutor executor,
        IStaffContextProvider contextProvider,
        IConfigRegistry configRegistry,
        ConfigConfirmToken confirmToken,
        CancellationToken ct)
    {
        var callerCtx = contextProvider.ForStaffOperation();
        if (RequireStaffActor(callerCtx) is { } refusal)
        {
            return refusal;
        }

        var decodedKey = Uri.UnescapeDataString(key);
        var form = httpContext.Request.Form;
        var newValueJson = form["newValue"].ToString();
        var reason = form["reason"].ToString();
        var token = form["confirmToken"].ToString();

        if (string.IsNullOrEmpty(token) || !confirmToken.TryVerify(token, decodedKey, newValueJson, reason))
        {
            // A tampered/expired/missing confirmToken never commits — fail closed, never trust the
            // resubmitted fields on their own (SLICE_S5_CONTRACT.md §4: "explicit re-confirm").
            return RedirectToConfigPage("admin.config.notice.error", errorKind: "denied", detail: null);
        }

        var target = new TargetRef("config_entry", decodedKey);
        return await CommitEdit(executor, configRegistry, callerCtx, FounderAction, target, decodedKey, newValueJson, reason, ct);
    }

    /// <summary>The ONE place either write path (ops-direct or founder-confirm) actually calls <see
    /// cref="IConfigRegistry.SetValue{T}"/> — always from inside the executor's own <c>work</c> delegate,
    /// so <c>AdminActionChokepointArchTests</c>'s scan never flags this file.</summary>
    private static async Task<IResult> CommitEdit(
        IAdminActionExecutor executor, IConfigRegistry configRegistry, RequestContext callerCtx,
        string action, TargetRef target, string key, string newValueJson, string reason, CancellationToken ct)
    {
        try
        {
            var result = await executor.Execute(callerCtx, action, target, reason, ctx =>
            {
                var value = JsonSerializer.Deserialize<JsonElement>(newValueJson);
                return configRegistry.SetValue(key, value, reason, ctx.Actor, ctx);
            }, ct);

            return result switch
            {
                AdminActionResult.Success => RedirectToConfigPage("admin.config.notice.saved", errorKind: null, detail: null),
                _ => MapRefusalToRedirect(result),
            };
        }
        catch (ArgumentException ex)
        {
            // ConfigBounds' OWN set-time rejection (bounds OR the per-key requires_reason check) surfaces
            // VERBATIM (§4: "never a second bounds implementation") — a rejected Set has already left
            // the stored value + audit stream byte-identical (ConfigRegistry.SetValue's own guarantee).
            return RedirectToConfigPage("admin.config.notice.bounds_prefix", errorKind: "bounds", detail: ex.Message);
        }
    }

    private static IResult MapRefusalToRedirect(AdminActionResult result) => result switch
    {
        AdminActionResult.ReasonRequired => RedirectToConfigPage("admin.config.notice.reason_required", errorKind: "denied", detail: null),
        AdminActionResult.FourEyesRequired => RedirectToConfigPage("admin.config.notice.four_eyes_required", errorKind: "denied", detail: null),
        AdminActionResult.Denied denied => RedirectToConfigPage("admin.config.notice.denied_prefix", errorKind: "denied", detail: denied.ReasonKey),
        _ => RedirectToConfigPage("admin.config.notice.error", errorKind: "denied", detail: null),
    };

    /// <summary>
    /// Type-aware raw-JSON builder for the editor's plain-text <c>newValue</c> form field (SLICE_S5_
    /// CONTRACT.md §4's five 9A types: int/number/bool/json/string). Throws <see cref="FormatException"/>
    /// (int/number/bool) or <see cref="JsonException"/> (json) on a malformed input — both caught by the
    /// caller and rendered exactly like a bounds rejection (a value the registry could never have
    /// accepted anyway). The RESULT is always valid, self-contained JSON text — never re-parsed against
    /// the manifest's bounds here (§4: "the editor never pre-validates with a second bounds
    /// implementation"); <c>ConfigBounds.ValidateAsync</c>, inside the real <c>SetValue</c> call, is the
    /// ONLY bounds authority.
    /// </summary>
    private static string BuildValueJson(string type, string rawInput) => type switch
    {
        "int" => long.TryParse(rawInput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
            ? l.ToString(CultureInfo.InvariantCulture)
            : throw new FormatException($"\"{rawInput}\" is not a valid integer."),
        "number" => double.TryParse(rawInput.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d.ToString(CultureInfo.InvariantCulture)
            : throw new FormatException($"\"{rawInput}\" is not a valid number."),
        "bool" => bool.TryParse(rawInput.Trim(), out var b)
            ? (b ? "true" : "false")
            : throw new FormatException($"\"{rawInput}\" is not a valid boolean (true/false)."),
        "json" => ParseJsonRawText(rawInput),
        "string" => JsonSerializer.Serialize(rawInput),
        _ => throw new FormatException($"unknown 9A type \"{type}\" — cannot build a value for it."),
    };

    private static string ParseJsonRawText(string rawInput)
    {
        using var doc = JsonDocument.Parse(rawInput); // throws JsonException on malformed input
        return doc.RootElement.GetRawText();
    }

    /// <summary>
    /// Hand-built HTML for the founder-scope confirm-with-reason interstitial (DESIGN.md neutral modal,
    /// admin.css's existing token classes — zero new CSS mechanism). Rendered directly from a minimal-API
    /// handler (never a Blazor <c>@page</c> route) because the wire contract requires the PROPOSE POST
    /// itself to answer 200 with the interstitial body, never a redirect (backend/e2e/admin-host.e2e.mjs:
    /// "the interstitial, not an immediate commit"). Every user-facing string still flows through <see
    /// cref="AdminStringCatalog"/> — the SAME keyed-string discipline as every Razor page — even though
    /// <c>tools/i18n-lint</c>'s mechanical hardcoded-literal scan only walks <c>*.razor</c> files (the
    /// SAME acknowledged escape valve <c>StaffRoles.razor.cs</c>'s own doc comment records for code-
    /// behind files); this file honors the RULE, not merely the lint.
    /// </summary>
    private static string RenderInterstitial(
        AdminStringCatalog strings, string key, ConfigEntryView entry, string newValueJson, string reason,
        string sealedToken, AntiforgeryTokenSet tokenSet)
    {
        var urlKey = Uri.EscapeDataString(key);
        var directionWarning = DirectionAwareWarning(strings, key, entry.ValueJson, newValueJson);

        return $"""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>{Enc(strings["admin.config.confirm.title"])}</title>
                <link rel="stylesheet" href="css/admin.css" />
            </head>
            <body>
                <div class="admin-shell">
                    <main class="admin-shell__main">
                        <div class="admin-card" data-testid="config-confirm-modal">
                            <h1>{Enc(strings["admin.config.confirm.title"])}</h1>
                            <p>{Enc(strings["admin.config.confirm.body"])} <code>{Enc(key)}</code></p>
                            {directionWarning}
                            <dl class="admin-confirm-diff">
                                <dt>{Enc(strings["admin.config.confirm.old_label"])}</dt>
                                <dd data-testid="config-confirm-old-value">{Enc(entry.ValueJson)}</dd>
                                <dt>{Enc(strings["admin.config.confirm.new_label"])}</dt>
                                <dd data-testid="config-confirm-new-value">{Enc(newValueJson)}</dd>
                                <dt>{Enc(strings["admin.config.confirm.reason_label"])}</dt>
                                <dd>{Enc(reason)}</dd>
                            </dl>
                            <form data-testid="config-confirm-form" method="post" action="/config/{urlKey}/confirm" class="admin-form">
                                <input type="hidden" name="{Enc(tokenSet.FormFieldName)}" value="{Enc(tokenSet.RequestToken ?? string.Empty)}" />
                                <input type="hidden" name="newValue" value="{Enc(newValueJson)}" />
                                <input type="hidden" name="reason" value="{Enc(reason)}" />
                                <input type="hidden" name="confirmToken" value="{Enc(sealedToken)}" />
                                <button type="submit" class="admin-button admin-button--danger">{Enc(strings["admin.config.confirm.action.confirm"])}</button>
                            </form>
                            <a class="admin-button admin-button--ghost" href="/config">{Enc(strings["admin.config.confirm.action.cancel"])}</a>
                        </div>
                    </main>
                </div>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Direction-aware callout (SLICE_S5_CONTRACT.md §4: "LOWERING verification.age_gate_challenge_
    /// threshold additionally trips the interstitial" — this key is founder-scope, so it ALREADY always
    /// walks the interstitial above; this renders the extra, direction-specific warning the spec calls
    /// out by name, so lowering the age-gate threshold is never visually indistinguishable from any other
    /// routine founder edit).
    /// </summary>
    private static string DirectionAwareWarning(AdminStringCatalog strings, string key, string oldValueJson, string newValueJson)
    {
        const string AgeGateKey = "verification.age_gate_challenge_threshold";
        if (!string.Equals(key, AgeGateKey, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (!long.TryParse(oldValueJson.Trim(), out var oldValue) || !long.TryParse(newValueJson.Trim(), out var newValue) || newValue >= oldValue)
        {
            return string.Empty;
        }

        return $"""<p class="admin-badge admin-badge--danger" data-testid="config-confirm-direction-warning">{Enc(strings["admin.config.confirm.age_gate_lowering_warning"])}</p>""";
    }

    private static string Enc(string value) => WebUtility.HtmlEncode(value);

    private static IResult? RequireStaffActor(RequestContext callerCtx) =>
        callerCtx.Actor.Kind == ActorKind.Staff ? null : Results.Redirect("/?refused=staff_only");

    private static IResult RedirectToConfigPage(string noticeKey, string? errorKind, string? detail)
    {
        var url = $"{ConfigRoute}?notice={Uri.EscapeDataString(noticeKey)}";
        if (errorKind is not null)
        {
            url += $"&errorKind={Uri.EscapeDataString(errorKind)}";
        }
        if (!string.IsNullOrEmpty(detail))
        {
            url += $"&detail={Uri.EscapeDataString(detail)}";
        }
        return Results.Redirect(url);
    }
}
