using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Svac.AdminHost.Auth;
using Svac.AdminHost.Domain.Execution;
using Svac.AdminHost.Domain.Persistence;
using Svac.AdminHost.Domain.Policy;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Hosting;

namespace Svac.AdminHost.Staff;

/// <summary>
/// The Staff & Roles desk's SSR form-post surface (SLICE_S5_CONTRACT.md §0/§1c/§8 seam 3, Pass B):
/// provision/deactivate/reactivate/role_grant/role_revoke as REAL HTTP endpoints (static SSR + enhanced
/// form posts, §1a) — never Blazor interactive dispatch. Every mutation routes THROUGH <see
/// cref="IAdminActionExecutor"/> with a REAL <see cref="TargetRef"/> (never a placeholder) — this file
/// itself NEVER calls a mutating domain-contract member directly; every DB write happens inside the
/// <c>work</c> delegate the executor invokes only after Authorize/reason/four-eyes all pass
/// (arch-gated: see <c>NoMutationOutsideExecutorArchTests</c> in Svac.Tests.Architecture).
///
/// All five endpoints carry <c>admin.host.transport</c> (Staff+Anonymous, DenyAsAbsence, §3) as their
/// OUTER, transport-level policy mapping — exactly like <see cref="StaffAuthEndpointExtensions"/>'s own
/// sign-in/out endpoints and the Razor Components mapping in Program.cs. The REAL, role-gated Authorize
/// call for EACH specific admin.staff.* action happens INSIDE the executor, never at this outer layer —
/// an anonymous or non-SuperAdmin POST reaching a handler here is refused cleanly by
/// <see cref="RequireStaffActor"/> before the executor is ever invoked (never an unhandled 500 from the
/// executor's own ArgumentException guard, which exists for programmer-error callers, not real HTTP
/// traffic).
/// </summary>
public static class StaffRolesEndpointExtensions
{
    private const string StaffRoute = "/staff";

    public static WebApplication MapStaffRolesEndpoints(this WebApplication app)
    {
        // Routes match backend/e2e/admin-host.e2e.mjs's own wire-contract comment verbatim (role is a
        // ROUTE segment on revoke, never a hidden form field — "POST /staff/<id>/revoke/<role>").
        app.MapPost("/staff/provision", HandleProvision).RequirePolicyAction("admin.host.transport");
        app.MapPost("/staff/{staffId}/deactivate", HandleDeactivate).RequirePolicyAction("admin.host.transport");
        app.MapPost("/staff/{staffId}/reactivate", HandleReactivate).RequirePolicyAction("admin.host.transport");
        app.MapPost("/staff/{staffId}/grant", HandleRoleGrant).RequirePolicyAction("admin.host.transport");
        app.MapPost("/staff/{staffId}/revoke/{role}", HandleRoleRevoke).RequirePolicyAction("admin.host.transport");
        return app;
    }

    private static async Task<IResult> HandleProvision(
        HttpContext httpContext,
        IAdminActionExecutor executor,
        IStaffContextProvider contextProvider,
        IAntiforgery antiforgery,
        AdminDbContext adminDb,
        CancellationToken ct)
    {
        var callerCtx = contextProvider.ForStaffOperation();
        if (RequireStaffActor(callerCtx) is { } refusal)
        {
            return refusal;
        }

        // SECURITY_REVIEW_S5.md S5-11 — every staff mutation POST validates its antiforgery token for
        // real, before any mutation/executor call (AntiforgeryGate's own doc comment).
        if (!await AntiforgeryGate.IsValid(antiforgery, httpContext))
        {
            return RedirectToStaffPage("admin.staff_roles.notice.error", isError: true);
        }

        // Field names match backend/e2e/admin-host.e2e.mjs's own wire-contract comment verbatim
        // (externalSubject/displayName, camelCase — the script's postForm literally sends these names).
        var form = httpContext.Request.Form;
        var externalSubject = form["externalSubject"].ToString().Trim();
        var email = form["email"].ToString().Trim();
        var displayName = form["displayName"].ToString().Trim();
        var region = form["region"].ToString().Trim();
        var reason = form["reason"].ToString();

        if (IsAnyMissing(externalSubject, email, displayName, region))
        {
            return RedirectToStaffPage("admin.staff_roles.notice.error", isError: true);
        }

        var newStaffId = OpaqueId.New(IdPrefixes.Staff, DateTimeOffset.UtcNow, Random.Shared).ToString();
        var target = new TargetRef("staff_account", newStaffId);

        try
        {
            var result = await executor.Execute(callerCtx, "admin.staff.provision", target, reason, async _ =>
            {
                var now = DateTimeOffset.UtcNow;
                adminDb.StaffAccounts.Add(new StaffAccountEntity
                {
                    Id = newStaffId,
                    ExternalSubject = externalSubject,
                    Email = email,
                    DisplayName = displayName,
                    Status = "active",
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    Region = region,
                    LawfulBasis = "contract",
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                await adminDb.SaveChangesAsync(ct);
            }, ct: ct);

            return ResultToRedirect(result, "admin.staff_roles.notice.provisioned");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // ux_staff_accounts_email / ux_staff_accounts_external_subject — a real user error (this
            // person is already provisioned), never a bug; a friendly redirect, not an unhandled 500.
            return RedirectToStaffPage("admin.staff_roles.notice.duplicate", isError: true);
        }
    }

    private static async Task<IResult> HandleDeactivate(
        string staffId,
        HttpContext httpContext,
        IAdminActionExecutor executor,
        IStaffContextProvider contextProvider,
        IAntiforgery antiforgery,
        AdminDbContext adminDb,
        CancellationToken ct)
    {
        var callerCtx = contextProvider.ForStaffOperation();
        if (RequireStaffActor(callerCtx) is { } refusal)
        {
            return refusal;
        }

        if (!await AntiforgeryGate.IsValid(antiforgery, httpContext))
        {
            return RedirectToStaffPage("admin.staff_roles.notice.error", isError: true);
        }

        var reason = httpContext.Request.Form["reason"].ToString();
        var target = new TargetRef("staff_account", staffId);

        var result = await executor.Execute(callerCtx, "admin.staff.deactivate", target, reason, async _ =>
        {
            var row = await adminDb.StaffAccounts.SingleAsync(s => s.Id == staffId, ct);
            row.Status = "deactivated";
            row.DeactivatedAt = DateTimeOffset.UtcNow;
            // §2: "security_stamp ... bumped on deactivate/grant/revoke; kills live sessions" — a
            // deactivated operator's live cookie fails OnValidatePrincipal's stamp compare at the next
            // revalidation (SLICE_S5_CONTRACT.md §1b law 1), and the executor's own step-1 re-read denies
            // their very next admin action regardless (§1b law 2, both legs composed).
            row.SecurityStamp = Guid.NewGuid().ToString("N");
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await adminDb.SaveChangesAsync(ct);
        }, ct: ct);

        return ResultToRedirect(result, "admin.staff_roles.notice.deactivated");
    }

    private static async Task<IResult> HandleReactivate(
        string staffId,
        HttpContext httpContext,
        IAdminActionExecutor executor,
        IStaffContextProvider contextProvider,
        IAntiforgery antiforgery,
        AdminDbContext adminDb,
        CancellationToken ct)
    {
        var callerCtx = contextProvider.ForStaffOperation();
        if (RequireStaffActor(callerCtx) is { } refusal)
        {
            return refusal;
        }

        if (!await AntiforgeryGate.IsValid(antiforgery, httpContext))
        {
            return RedirectToStaffPage("admin.staff_roles.notice.error", isError: true);
        }

        var reason = httpContext.Request.Form["reason"].ToString();
        var target = new TargetRef("staff_account", staffId);

        var result = await executor.Execute(callerCtx, "admin.staff.reactivate", target, reason, async _ =>
        {
            var row = await adminDb.StaffAccounts.SingleAsync(s => s.Id == staffId, ct);
            row.Status = "active";
            row.DeactivatedAt = null;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await adminDb.SaveChangesAsync(ct);
        }, ct: ct);

        return ResultToRedirect(result, "admin.staff_roles.notice.reactivated");
    }

    private static async Task<IResult> HandleRoleGrant(
        string staffId,
        HttpContext httpContext,
        IAdminActionExecutor executor,
        IStaffContextProvider contextProvider,
        IAntiforgery antiforgery,
        AdminDbContext adminDb,
        CancellationToken ct)
    {
        var callerCtx = contextProvider.ForStaffOperation();
        if (RequireStaffActor(callerCtx) is { } refusal)
        {
            return refusal;
        }

        if (!await AntiforgeryGate.IsValid(antiforgery, httpContext))
        {
            return RedirectToStaffPage("admin.staff_roles.notice.error", isError: true);
        }

        var form = httpContext.Request.Form;
        var reason = form["reason"].ToString();
        StaffRole role;
        try
        {
            role = StaffRoleCodes.Parse(form["role"].ToString());
        }
        catch (ArgumentOutOfRangeException)
        {
            return RedirectToStaffPage("admin.staff_roles.notice.error", isError: true);
        }

        var target = new TargetRef("staff_account", staffId);

        var result = await executor.Execute(callerCtx, "admin.staff.role_grant", target, reason, async ctx =>
        {
            var staffRow = await adminDb.StaffAccounts.SingleAsync(s => s.Id == staffId, ct);
            adminDb.StaffRoleGrants.Add(new StaffRoleGrantEntity
            {
                Id = OpaqueId.New(IdPrefixes.StaffRoleGrant, DateTimeOffset.UtcNow, Random.Shared).ToString(),
                StaffId = staffId,
                Role = StaffRoleCodes.ToCode(role),
                GrantedBy = ctx.Actor.Id.ToString(),
                GrantReason = reason!, // executor's own reason-precheck already refused a whitespace reason before work() ever ran
                GrantedAt = DateTimeOffset.UtcNow,
                Region = staffRow.Region,
                LawfulBasis = staffRow.LawfulBasis,
            });
            // §2: security_stamp bumped on grant — a fresh hat is live at the granted staff member's very
            // next action (no re-sign-in required), symmetric with the deactivate/revoke legs.
            staffRow.SecurityStamp = Guid.NewGuid().ToString("N");
            staffRow.UpdatedAt = DateTimeOffset.UtcNow;
            await adminDb.SaveChangesAsync(ct);
        }, ct: ct);

        return ResultToRedirect(result, "admin.staff_roles.notice.granted");
    }

    private static async Task<IResult> HandleRoleRevoke(
        string staffId,
        string role,
        HttpContext httpContext,
        IAdminActionExecutor executor,
        IStaffContextProvider contextProvider,
        IAntiforgery antiforgery,
        AdminDbContext adminDb,
        CancellationToken ct)
    {
        var callerCtx = contextProvider.ForStaffOperation();
        if (RequireStaffActor(callerCtx) is { } refusal)
        {
            return refusal;
        }

        if (!await AntiforgeryGate.IsValid(antiforgery, httpContext))
        {
            return RedirectToStaffPage("admin.staff_roles.notice.error", isError: true);
        }

        // "role" arrives as a ROUTE segment (backend/e2e/admin-host.e2e.mjs: "POST /staff/<id>/revoke/
        // <role>") — the snake_case StaffRoleCodes value verbatim (e.g. "economy_ops"); StaffRoleCodes.
        // Parse/ToCode round-trips it to validate it is a REAL role before touching the DB.
        string roleCode;
        try
        {
            roleCode = StaffRoleCodes.ToCode(StaffRoleCodes.Parse(role));
        }
        catch (ArgumentOutOfRangeException)
        {
            return RedirectToStaffPage("admin.staff_roles.notice.error", isError: true);
        }

        var reason = httpContext.Request.Form["reason"].ToString();
        var target = new TargetRef("staff_account", staffId);

        var result = await executor.Execute(callerCtx, "admin.staff.role_revoke", target, reason, async ctx =>
        {
            var grant = await adminDb.StaffRoleGrants.SingleOrDefaultAsync(
                g => g.StaffId == staffId && g.Role == roleCode && g.RevokedAt == null, ct);
            if (grant is null)
            {
                return; // already revoked (or never granted) — idempotent no-op, still a real audited attempt.
            }

            grant.RevokedAt = DateTimeOffset.UtcNow;
            grant.RevokedBy = ctx.Actor.Id.ToString();
            grant.RevokeReason = reason;

            var staffRow = await adminDb.StaffAccounts.SingleAsync(s => s.Id == staffId, ct);
            staffRow.SecurityStamp = Guid.NewGuid().ToString("N"); // §2: bumped on revoke
            staffRow.UpdatedAt = DateTimeOffset.UtcNow;
            await adminDb.SaveChangesAsync(ct);
        }, affectedRoleCode: roleCode, ct: ct); // SECURITY_REVIEW_S5.md S5-03: the last-active-SuperAdmin lockout guard needs the SPECIFIC role being revoked, not just the target staff id.

        return ResultToRedirect(result, "admin.staff_roles.notice.revoked");
    }

    /// <summary>
    /// The transport-level gate every handler runs BEFORE ever touching the executor: <c>admin.host.
    /// transport</c> (this endpoint's own [PolicyAction]) allows Staff AND Anonymous through the HTTP
    /// filter — this second check is what turns "reached the handler" into "is actually a staff member,"
    /// cleanly (a real redirect), never letting a non-staff POST reach AdminActionExecutor.Execute's own
    /// ArgumentException guard (which exists for programmer-error callers, not real HTTP traffic).
    /// </summary>
    private static IResult? RequireStaffActor(Svac.DomainCore.Contracts.RequestContext callerCtx) =>
        callerCtx.Actor.Kind == ActorKind.Staff ? null : Results.Redirect("/?refused=staff_only");

    private static bool IsAnyMissing(params string[] values) => values.Any(string.IsNullOrWhiteSpace);

    private static IResult RedirectToStaffPage(string noticeKey, bool isError, string? detail = null)
    {
        var url = $"{StaffRoute}?notice={Uri.EscapeDataString(noticeKey)}&error={(isError ? "1" : "0")}";
        if (!string.IsNullOrEmpty(detail))
        {
            url += $"&detail={Uri.EscapeDataString(detail)}";
        }
        return Results.Redirect(url);
    }

    private static IResult ResultToRedirect(AdminActionResult result, string successNoticeKey) => result switch
    {
        AdminActionResult.Success => RedirectToStaffPage(successNoticeKey, isError: false),
        AdminActionResult.ReasonRequired => RedirectToStaffPage("admin.staff_roles.notice.reason_required", isError: true),
        AdminActionResult.FourEyesRequired => RedirectToStaffPage("admin.staff_roles.notice.four_eyes_required", isError: true),
        AdminActionResult.Denied denied => RedirectToStaffPage("admin.staff_roles.notice.refused_prefix", isError: true, detail: denied.ReasonKey),
        _ => RedirectToStaffPage("admin.staff_roles.notice.error", isError: true),
    };

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        (ex.InnerException as Npgsql.PostgresException ?? ex.InnerException?.InnerException as Npgsql.PostgresException)?.SqlState
            == Npgsql.PostgresErrorCodes.UniqueViolation;
}
