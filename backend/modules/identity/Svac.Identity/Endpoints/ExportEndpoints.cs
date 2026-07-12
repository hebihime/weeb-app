using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Contracts.Quota;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Deterministic;
using Svac.DomainCore.Hosting;
using Svac.Identity.Config;
using Svac.Identity.Contracts;
using Svac.Identity.Export;
using Svac.Identity.Persistence;

namespace Svac.Identity.Endpoints;

/// <summary>
/// The three export endpoints (SLICE_S3_CONTRACT.md §1c/§6b): `POST /v1/me/export` (single-active-job
/// idempotent), `GET /v1/me/export/{exportId}` (status), `GET /v1/me/export/{exportId}/download` (streams
/// the zip THROUGH this authed, policy-gated endpoint — no public SAS URL). All three bind
/// <see cref="PolicyTargetBinding.SelfAccount"/>/<see cref="PolicyTargetBinding.FromRoute"/> per §3b;
/// the two resource-scoped reads ride the registered `export` <see cref="IResourceOwnershipResolver"/>
/// (<see cref="Svac.Identity.Policy.ExportOwnershipResolver"/>) — a foreign or unknown exportId denies as
/// absence, byte-identical, exactly like the session/device Auth-F3 exemplars.
///
/// AUTH-1 (SECURITY_REVIEW_S3.md): the two reads ALSO fold <c>AccountId == caller</c> directly into their
/// own queries (GetMeExport here; <see cref="Svac.Identity.Export.IExportArtifactStore.GetReadyZipAsync"/>
/// for the download) — defense-in-depth so a future refactor that drops <c>RequirePolicyAction</c> still
/// cannot leak a foreign account's export, not just a latent single point of failure on the policy layer.
/// </summary>
public static class ExportEndpoints
{
    public static void MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/me/export", PostMeExport)
            .WithName("PostMeExport")
            .RequirePolicyAction("identity.export.request", PolicyTargetBinding.SelfAccount)
            .Produces<ExportRequested>(StatusCodes.Status202Accepted)
            .Produces<LimitReached>(StatusCodes.Status429TooManyRequests);

        app.MapGet("/v1/me/export/{exportId}", GetMeExport)
            .WithName("GetMeExport")
            .RequirePolicyAction("identity.export.read", PolicyTargetBinding.FromRoute("exportId", "export"))
            .Produces<ExportStatus>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/v1/me/export/{exportId}/download", GetMeExportDownload)
            .WithName("GetMeExportDownload")
            .RequirePolicyAction("identity.export.download", PolicyTargetBinding.FromRoute("exportId", "export"))
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .Produces(StatusCodes.Status404NotFound);
    }

    // ------------------------------------------------------------------------------------------------
    // POST /v1/me/export — duplicate active request returns the SAME job (single-active enforced by the
    // partial unique index ux_export_active, §2); race-proof via 23505 catch + re-read winner, the same
    // "check-then-act" idiom SignupCompletionService's handle-uniqueness path already uses.
    // ------------------------------------------------------------------------------------------------
    private static async Task<IResult> PostMeExport(
        [FromServices] IdentityDbContext db,
        [FromServices] ExportWorker worker,
        [FromServices] IQuotaService quotaService,
        [FromServices] IEventStore eventStore,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        var ctx = requestContext.Current;
        var accountId = ctx.Actor.Id.ToString();

        // Lazy expiry (§4: identity.export.link_ttl_hours "download expiry + sweep") — no background 13A
        // sweep worker exists yet (same deferred posture as identity.email_challenge.retention_hours,
        // whose own doc comment names this exact pattern: "Pass 1 ships the config + registered retention
        // rule, the sweep loop lands with the first 13A worker"). Sweeping THIS account's own stale row
        // inline on the request path means an expired job can never permanently block a fresh request via
        // ux_export_active — the partial unique index only excludes 'pending'/'ready' rows.
        await ExpireStaleReadyJob(db, accountId, ct);

        var existingActive = await db.ExportJobs
            .Where(e => e.AccountId == accountId && (e.State == "pending" || e.State == "ready"))
            .Select(e => e.ExportId)
            .FirstOrDefaultAsync(ct);
        if (existingActive is not null)
        {
            return Results.Json(new ExportRequested(existingActive), statusCode: StatusCodes.Status202Accepted);
        }

        var quotaContext = new QuotaContext(
            new ResetSpec(ResetCadence.Daily, WindowLocality.ConLocal),
            TimeZoneInfo.Utc,
            TimeOnly.MinValue,
            DateTimeOffset.UtcNow);
        var quotaResult = await quotaService.Consume(ctx.Actor, IdentityQuotaKeys.ExportRequestDaily, quotaContext, ct);
        if (quotaResult is QuotaResult.Limited limited)
        {
            return PolicyResults.LimitReached(limited.LimitReached);
        }

        var now = DateTimeOffset.UtcNow;
        var exportId = OpaqueId.New(IdPrefixes.Export, now, Random.Shared).ToString();

        var jobEntity = new ExportJobEntity
        {
            ExportId = exportId,
            AccountId = accountId,
            State = "pending",
            RequestedAt = now,
            Region = ctx.Region.ToString(),
            LawfulBasis = LawfulBasisResolver.Resolve(ctx.LawfulBasisVariant.Key, "identity.export_jobs", "export.requested", ctx.Region.ToString()),
        };
        db.ExportJobs.Add(jobEntity);

        string winnerExportId;
        try
        {
            await db.SaveChangesAsync(ct);
            winnerExportId = exportId;
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException pg
            && pg.SqlState == PostgresErrorCodes.UniqueViolation && pg.ConstraintName == "ux_export_active")
        {
            // Vanishingly rare race: two concurrent POSTs both saw no active job. The loser's retry
            // resolves to the WINNER's job id — the wire contract's own idempotency promise.
            db.Entry(jobEntity).State = EntityState.Detached;

            winnerExportId = await db.ExportJobs
                .Where(e => e.AccountId == accountId && (e.State == "pending" || e.State == "ready"))
                .Select(e => e.ExportId)
                .FirstAsync(ct);
        }

        if (winnerExportId == exportId)
        {
            await eventStore.Append(StreamType.Behavioral, accountId, "identity.export_requested", "{}", ctx, ExpectedVersion.AnyVersion, ct);
            await worker.RunAsync(exportId, accountId, now, ctx, ct);
        }

        return Results.Json(new ExportRequested(winnerExportId), statusCode: StatusCodes.Status202Accepted);
    }

    // ------------------------------------------------------------------------------------------------
    // GET /v1/me/export/{exportId} — pending -> ready(expiresAt) -> delivered/expired/failed.
    // ------------------------------------------------------------------------------------------------
    private static async Task<IResult> GetMeExport(
        [FromRoute] string exportId,
        [FromServices] IdentityDbContext db,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        // AUTH-1 (SECURITY_REVIEW_S3.md): defense-in-depth ownership fold — the export status/download
        // queries scoped ONLY by the policy filter+resolver, unlike DeleteMeSession/DeleteMeDevice's own
        // `&& AccountId == accountId` re-scope. Folding `&& AccountId == accountId` here means a future
        // refactor that drops `.RequirePolicyAction(...)` still cannot leak a foreign export's status —
        // the query itself, not just the 4A chokepoint, proves ownership.
        var accountId = requestContext.Current.Actor.Id.ToString();

        // Same lazy-expiry sweep as POST, scoped to this one row so a status read never lies about a
        // job that is, in fact, past its TTL.
        var now = DateTimeOffset.UtcNow;
        // PII-7 (SECURITY_REVIEW_S3.md): null the artifact + manifest in the SAME expiry UPDATE — leaving
        // the bytea in place after "expired" flips means an unbounded-lifetime PII blob outlives its own
        // download window with nothing anywhere sweeping it. The artifact is encrypted at rest (see
        // PostgresExportArtifactStore), but a job that will never be read again should not keep it either.
        await db.ExportJobs
            .Where(e => e.ExportId == exportId && e.AccountId == accountId && e.State == "ready" && e.ExpiresAt != null && e.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.State, "expired")
                .SetProperty(e => e.Artifact, (byte[]?)null)
                .SetProperty(e => e.ManifestJson, (string?)null), ct);

        var job = await db.ExportJobs
            .Where(e => e.ExportId == exportId && e.AccountId == accountId)
            .Select(e => new { e.State, e.ExpiresAt })
            .SingleOrDefaultAsync(ct);

        // The policy chokepoint already proved ownership for a live row — this is defensive absence only.
        if (job is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new ExportStatus(job.State, job.ExpiresAt));
    }

    private static async Task ExpireStaleReadyJob(IdentityDbContext db, string accountId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        // PII-7 (SECURITY_REVIEW_S3.md): see GetMeExport's identical fix below — null the artifact +
        // manifest in the SAME expiry UPDATE.
        await db.ExportJobs
            .Where(e => e.AccountId == accountId && e.State == "ready" && e.ExpiresAt != null && e.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.State, "expired")
                .SetProperty(e => e.Artifact, (byte[]?)null)
                .SetProperty(e => e.ManifestJson, (string?)null), ct);
    }

    // ------------------------------------------------------------------------------------------------
    // GET /v1/me/export/{exportId}/download — streams the zip THROUGH this authed endpoint (no public
    // SAS URL, §1c); download audited (3A). Not-ready/expired renders the SAME absence shape as a
    // foreign/unknown exportId — never a distinguishing reason.
    // ------------------------------------------------------------------------------------------------
    private static async Task<IResult> GetMeExportDownload(
        [FromRoute] string exportId,
        [FromServices] IExportArtifactStore artifactStore,
        [FromServices] IEventStore eventStore,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        var ctx = requestContext.Current;
        // AUTH-1 (SECURITY_REVIEW_S3.md): same defense-in-depth fold as GetMeExport above —
        // GetReadyZipAsync's own query now requires AccountId == the caller, independent of the policy layer.
        var zip = await artifactStore.GetReadyZipAsync(exportId, ctx.Actor.Id.ToString(), ct);
        if (zip is null)
        {
            return Results.NotFound();
        }

        await eventStore.Append(StreamType.Audit, ctx.Actor.Id.ToString(), "identity.export_downloaded", "{}", ctx, ExpectedVersion.AnyVersion, ct);

        return Results.Bytes(zip, contentType: "application/zip", fileDownloadName: $"{exportId}.zip");
    }
}
