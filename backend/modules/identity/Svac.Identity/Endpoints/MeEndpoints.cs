using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Deterministic;
using Svac.DomainCore.Hosting;
using Svac.Identity.Contracts;
using Svac.Identity.Persistence;

namespace Svac.Identity.Endpoints;

/// <summary>
/// A MINIMAL `GET /v1/me` (SLICE_S3_CONTRACT.md task scope: "leave the rest of /v1/me/* to the next
/// pass") — needed here only so the live E2E can assert a minted session actually works. The full
/// AccountSelf shape already exists in Svac.Identity.Contracts; `identity.me.read`'s SelfOnly binding
/// (§3b) is the same policy row the next pass's fuller read builds on, unmapped-endpoint-wise unchanged.
/// </summary>
public static class MeEndpoints
{
    public static void MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/me", GetMe)
            .WithName("GetMe")
            .RequirePolicyAction("identity.me.read", PolicyTargetBinding.SelfAccount)
            .Produces<AccountSelf>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> GetMe(
        [FromServices] IdentityDbContext db,
        [FromServices] IFieldEncryptor fieldEncryptor,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        var accountId = requestContext.Current.Actor.Id.ToString();
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.AccountId == accountId, ct);
        if (account is null)
        {
            // The policy chokepoint already proved a live session for a User actor exists — this is
            // defensive only (e.g. a tombstoned row mid-purge) and renders absence, never a leak.
            return Results.NotFound();
        }

        var birthdateText = await fieldEncryptor.Unprotect(FieldEncryptionPurpose.Birthdate, account.BirthdateEnc, ct);
        var birthdate = DateOnly.Parse(birthdateText, CultureInfo.InvariantCulture);
        var ageYears = AgeMath.AgeYears(birthdate, DateOnly.FromDateTime(DateTime.UtcNow));

        var self = new AccountSelf(
            OpaqueId.Parse(account.AccountId),
            account.Handle,
            account.Email ?? string.Empty,
            ageYears,
            account.Locale,
            account.FandomTag,
            account.CreatedAt,
            account.DeletionEffectiveAt);

        return Results.Ok(self);
    }
}
