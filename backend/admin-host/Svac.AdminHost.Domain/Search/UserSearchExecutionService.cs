using System.Text.Json;
using Svac.AdminHost.Domain.Execution;
using Svac.AdminHost.Domain.Policy;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Quota;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Deterministic;

namespace Svac.AdminHost.Domain.Search;

/// <summary>The User Search desk's ONE closed outcome (mirrors <see cref="AdminActionResult"/>'s own shape so callers pattern-match, never string-sniff).</summary>
public abstract record UserSearchExecutionOutcome
{
    /// <summary>A completed search — <paramref name="Page"/> may legitimately hold zero rows (a real empty result OR <see cref="EmptyUserSearchSource"/>'s honest-dark state — <see cref="UserSearchPage.SourceLive"/> tells them apart).</summary>
    public sealed record Rendered(UserSearchPage Page) : UserSearchExecutionOutcome;

    /// <summary>10A `admin.user_search.daily` capped THIS staff actor for the window — the search source was never called (never enumerate past the cap). Render the portal's ONE LimitReached shape (§5).</summary>
    public sealed record QuotaLimited(LimitReached Limit) : UserSearchExecutionOutcome;

    /// <summary>4A denied (Analyst, or a staff actor with no qualifying grant) — the standard staff deny, itself audited by the executor's own refusal path (§3/§0).</summary>
    public sealed record AccessDenied(string ReasonKey) : UserSearchExecutionOutcome;

    /// <summary>A `requires_reason` refusal or four-eyes gate — structurally unreachable for this action (RequiresReason=false, §3), kept only so this union stays exhaustive against every <see cref="AdminActionResult"/> leg.</summary>
    public sealed record Unexpected(string Detail) : UserSearchExecutionOutcome;
}

/// <summary>
/// The audited-execute path around <see cref="IUserSearchSource"/> (SLICE_S5_CONTRACT.md §0/§8 seam 6/
/// §9): "EVERY query (even empty) is audited admin.user_search.executed {query_class,query_term,hat}
/// stream_id=staff ref AND quota-consumed via 10A admin.user_search.daily... The execute path runs
/// THROUGH the audited flow (auth→4A→quota→audit→render)." Routes through
/// <see cref="IAdminActionExecutor"/> (auth re-read + hat computation + 4A Authorize, §1c) with the
/// TARGET being the ACTING STAFF's own account (§3's stream_id law extended to a non-staff-lifecycle,
/// read-path action: "staff-lifecycle events key by the staff ref" — a search names no single subject,
/// so it is keyed exactly like one). Quota-consume and the self-logging audit event both happen INSIDE
/// the executor's own `work` delegate — nested inside the call to its own Execute method, so
/// <c>AdminActionChokepointArchTests</c>'s text scan never flags this file's own
/// <c>eventStore.Append</c> call (it is textually blanked before that scan's regex ever runs). [NOTE:
/// this doc comment deliberately never spells the literal substring dot-Execute-openparen — that scan's
/// own paren-balancer keys off the FIRST such substring in the file, so writing it in prose ahead of the
/// real call corrupts the balance for every occurrence after it. Found live authoring this file.]
/// </summary>
public sealed class UserSearchExecutionService(
    IAdminActionExecutor executor,
    IUserSearchSource searchSource,
    IQuotaService quotaService,
    IEventStore eventStore)
{
    private const string Action = "admin.user_search.execute";

    /// <summary>The typed-enum entry point (unchanged call shape) — a real, already-parsed query class.</summary>
    public Task<UserSearchExecutionOutcome> Execute(RequestContext callerCtx, UserSearchQueryClass queryClass, string term, string? cursor, CancellationToken ct = default) =>
        ExecuteCore(callerCtx, queryClass.ToString(), term, cursor, ct);

    /// <summary>
    /// SECURITY_REVIEW_S5.md S5-14 fix — the RAW entry point UserSearch.razor.cs now calls for EVERY
    /// submitted attempt, including an explicitly empty <paramref name="term"/> or an unparseable
    /// <paramref name="queryClassRaw"/>. The empty/invalid decision lives HERE (a typed outcome derived
    /// inside the SAME audited-execute flow), never before it — so §0's "EVERY query (even empty) is
    /// audited ... and quota-consumed" holds for this shape too, not only for an already-valid typed call.
    /// </summary>
    public Task<UserSearchExecutionOutcome> Execute(RequestContext callerCtx, string? queryClassRaw, string? term, string? cursor, CancellationToken ct = default) =>
        ExecuteCore(callerCtx, queryClassRaw, term, cursor, ct);

    private async Task<UserSearchExecutionOutcome> ExecuteCore(RequestContext callerCtx, string? queryClassRaw, string? term, string? cursor, CancellationToken ct)
    {
        var normalizedTerm = term?.Trim() ?? string.Empty;
        var hasValidQueryClass = Enum.TryParse<UserSearchQueryClass>(queryClassRaw, ignoreCase: false, out var queryClass);
        var canSearch = hasValidQueryClass && !string.IsNullOrWhiteSpace(normalizedTerm);

        UserSearchPage? page = null;
        LimitReached? limited = null;

        var target = new TargetRef("staff_account", callerCtx.Actor.Id.ToString());
        var result = await executor.Execute(callerCtx, Action, target, reason: null, work: async workCtx =>
        {
            // --- quota (10A admin.user_search.daily, §5) — BEFORE the actual search, so a capped
            // session never gets to enumerate past its limit. Consumed regardless of outcome: "EVERY
            // query ... is quota-consumed" (§0), the detection value the §5 rationale names holds even
            // for a query the cap itself blocks, an empty term, or an unparseable query class. ---
            var quotaContext = new QuotaContext(
                new ResetSpec(ResetCadence.Daily, WindowLocality.ConLocal),
                TimeZoneInfo.Utc,
                TimeOnly.MinValue, // daily/UTC (mirrors AimlRouterService.ConsumeDailyBudget's own con-local-over-UTC-cutoff convention).
                DateTimeOffset.UtcNow);
            var quotaResult = await quotaService.Consume(workCtx.Actor, AdminQuotaKeys.UserSearchDaily, quotaContext, ct);

            if (quotaResult is QuotaResult.Limited quotaLimited)
            {
                limited = quotaLimited.LimitReached;
            }
            else if (canSearch)
            {
                page = await searchSource.Search(new UserSearchQuery(queryClass, normalizedTerm, cursor), ct);
            }
            else
            {
                // SECURITY_REVIEW_S5.md S5-14: an empty/whitespace term or an unparseable query class is
                // still an audited, quota-consumed attempt — it renders the SAME honest-dark shape
                // EmptyUserSearchSource itself uses (SourceLive:false), never a fabricated row, and the
                // real search source is never called with a query that could never mean anything.
                page = new UserSearchPage(Array.Empty<UserSearchResultRow>(), NextCursor: null, HasMore: false, SourceLive: false);
            }

            // --- self-logging audit event (§0/§9): "EVERY query (even empty) is audited
            // admin.user_search.executed {query_class,query_term,hat} stream_id=staff ref" — appended
            // here (inside `work`) rather than relying on the executor's generic admin.action.executed
            // envelope, per AdminActionExecutor's own extended self-logging exemption (see its doc
            // comment) for this exact action key. query_class logs the RAW attempted value when it fails
            // to parse (never silently substituted) so the audit trail records exactly what was tried. ---
            var hat = workCtx.Staff?.ActingHat.ToString();
            var payload = JsonSerializer.Serialize(new { query_class = hasValidQueryClass ? queryClass.ToString() : (queryClassRaw ?? string.Empty), query_term = normalizedTerm, hat });
            await eventStore.Append(StreamType.Audit, target.ResourceId!, "admin.user_search.executed", payload, workCtx, ExpectedVersion.AnyVersion, ct);
        }, ct: ct);

        return result switch
        {
            AdminActionResult.Success when limited is { } limit => new UserSearchExecutionOutcome.QuotaLimited(limit),
            AdminActionResult.Success when page is { } renderedPage => new UserSearchExecutionOutcome.Rendered(renderedPage),
            AdminActionResult.Success => new UserSearchExecutionOutcome.Unexpected("executor succeeded but neither a page nor a quota limit was captured"),
            AdminActionResult.Denied denied => new UserSearchExecutionOutcome.AccessDenied(denied.ReasonKey),
            AdminActionResult.ReasonRequired => new UserSearchExecutionOutcome.Unexpected("ReasonRequired — unreachable, admin.user_search.execute has RequiresReason=false"),
            AdminActionResult.FourEyesRequired => new UserSearchExecutionOutcome.Unexpected("FourEyesRequired — unreachable, admin.user_search.execute has RequiresReason=false"),
            _ => new UserSearchExecutionOutcome.Unexpected($"unhandled AdminActionResult leg: {result.GetType().Name}"),
        };
    }
}
