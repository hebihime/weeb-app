using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Hosting;
using Svac.Identity.Contracts;
using Svac.Identity.Persistence;

namespace Svac.Identity.Deletion;

/// <summary>
/// The three deletion endpoints (SLICE_S3_CONTRACT.md §1c/§2): `POST /v1/me/deletion` (request, Phase L),
/// `DELETE /v1/me/deletion` (cancel, grace-only), `GET /v1/me/deletion` (status). All three bind <see
/// cref="PolicyTargetBinding.SelfAccount"/> per §3b; the deletion.request/.cancel/.read policy rows
/// already ship in <c>IdentityPolicyTableSource</c> (Pass 2b's schema-only scaffold anticipated them).
/// </summary>
public static class DeletionEndpoints
{
    public static void MapDeletionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/me/deletion", PostDeletion)
            .WithName("PostMeDeletion")
            .RequirePolicyAction("identity.deletion.request", PolicyTargetBinding.SelfAccount)
            .Produces<DeletionRequested>(StatusCodes.Status200OK);

        app.MapDelete("/v1/me/deletion", DeleteDeletion)
            .WithName("DeleteMeDeletion")
            .RequirePolicyAction("identity.deletion.cancel", PolicyTargetBinding.SelfAccount)
            .Produces(StatusCodes.Status204NoContent);

        app.MapGet("/v1/me/deletion", GetDeletion)
            .WithName("GetMeDeletion")
            .RequirePolicyAction("identity.deletion.read", PolicyTargetBinding.SelfAccount)
            .Produces<DeletionStatus>(StatusCodes.Status200OK);
    }

    // ------------------------------------------------------------------------------------------------
    // POST /v1/me/deletion — export-offered-first IS in the response contract (SLICE_S3_CONTRACT.md
    // §1c/§2): {effectiveAt, exportOffered: true}. Grace clock starts now.
    // ------------------------------------------------------------------------------------------------
    private static async Task<IResult> PostDeletion(
        [FromServices] IAccountLifecycle lifecycle,
        [FromServices] IdentityDbContext db,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        var ctx = requestContext.Current;
        await lifecycle.RequestDeletion(ctx.Actor.Id, ctx, ct);

        var accountId = ctx.Actor.Id.ToString();
        var effectiveAt = await db.Accounts.Where(a => a.AccountId == accountId).Select(a => a.DeletionEffectiveAt).SingleAsync(ct);

        return Results.Ok(new DeletionRequested(effectiveAt!.Value, ExportOffered: true));
    }

    // ------------------------------------------------------------------------------------------------
    // DELETE /v1/me/deletion — cancel during grace; idempotent under race (AccountLifecycle's own
    // state-guarded UPDATE) — always 204, whether this call won the race or a concurrent one already had.
    // ------------------------------------------------------------------------------------------------
    private static async Task<IResult> DeleteDeletion(
        [FromServices] IAccountLifecycle lifecycle,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        var ctx = requestContext.Current;
        await lifecycle.CancelDeletion(ctx.Actor.Id, ctx, ct);
        return Results.NoContent();
    }

    // ------------------------------------------------------------------------------------------------
    // GET /v1/me/deletion — {state, scheduledFor?}, mirrors identity.deletion_jobs' own state CHECK
    // (scheduled|canceled|executing|held|complete). Reads the MOST RECENT job row for this account (a
    // cancel-then-re-request sequence leaves an older canceled row behind); "none requested yet" renders
    // state="none" rather than 404 — GET is always reachable at `any` accountState (§3b).
    // ------------------------------------------------------------------------------------------------
    private static async Task<IResult> GetDeletion(
        [FromServices] IdentityDbContext db,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        var accountId = requestContext.Current.Actor.Id.ToString();
        var latest = await db.DeletionJobs
            .Where(d => d.AccountId == accountId)
            .OrderByDescending(d => d.RequestedAt)
            .Select(d => new { d.State, d.ScheduledFor })
            .FirstOrDefaultAsync(ct);

        return Results.Ok(latest is null
            ? new DeletionStatus("none", null)
            : new DeletionStatus(latest.State, latest.ScheduledFor));
    }
}

/// <summary>`POST /v1/me/deletion` 200 response (SLICE_S3_CONTRACT.md §1c) — export-offered-first is in the response contract.</summary>
public sealed record DeletionRequested(DateTimeOffset EffectiveAt, bool ExportOffered);
