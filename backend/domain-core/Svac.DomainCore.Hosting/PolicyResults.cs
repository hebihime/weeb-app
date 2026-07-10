using Microsoft.AspNetCore.Http;
using Svac.DomainCore.Contracts.Api;

namespace Svac.DomainCore.Hosting;

/// <summary>
/// Serialization for the two shared deny shapes (SLICE_S1_CONTRACT.md §1c): Problem (RFC 9457, never
/// localized prose — token law 2) and LimitReached (the one 429 deny shape — 10A / DR-7.3). One factory
/// so every host renders both identically; no second deny shape ever gets invented ad hoc per endpoint.
/// </summary>
public static class PolicyResults
{
    public static IResult NotFoundAbsence() => Results.NotFound();

    public static IResult LimitReached(Svac.DomainCore.Contracts.Api.LimitReached limitReached) =>
        Results.Json(limitReached, statusCode: StatusCodes.Status429TooManyRequests);

    public static IResult Standard(string reasonKey, string correlationId, int statusCode = StatusCodes.Status403Forbidden) =>
        Results.Json(
            new Problem(
                Type: "about:blank",
                Title: "Request denied",
                Status: statusCode,
                MessageKey: reasonKey,
                CorrelationId: correlationId),
            statusCode: statusCode);
}
