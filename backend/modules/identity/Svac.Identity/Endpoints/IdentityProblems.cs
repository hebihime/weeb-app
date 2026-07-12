using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Api;

namespace Svac.Identity.Endpoints;

/// <summary>Renders the shared <see cref="Problem"/> shape (SLICE_S3_CONTRACT.md §1c, token law 2 — never localized prose) — the SAME wire shape <see cref="Svac.DomainCore.Hosting.PolicyResults.Standard"/> renders for a policy deny, so identity's own validation/business-rule failures never invent a second Problem shape.</summary>
public static class IdentityProblems
{
    public static IResult Of(string messageKey, int statusCode, RequestContext ctx, string? title = null) =>
        Results.Json(
            new Problem(Type: "about:blank", Title: title ?? "Request failed", Status: statusCode, MessageKey: messageKey, CorrelationId: ctx.CorrelationId),
            statusCode: statusCode);
}
