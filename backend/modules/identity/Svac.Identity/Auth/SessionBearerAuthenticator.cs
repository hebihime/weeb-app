using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Hosting;
using Svac.Identity.Persistence;

namespace Svac.Identity.Auth;

/// <summary>
/// The session-backed <see cref="IBearerAuthenticator"/> (SLICE_S3_CONTRACT.md §1b): token hash -> live
/// session row + account join (state, region, locale) -> User <see cref="ActorRef"/> + accountState into
/// RequestContext. Revoked/expired/unknown resolve to null (anonymous) — no 401 oracle at middleware;
/// endpoints requiring User then deny per policy as absence (uniform with a genuinely missing token).
/// </summary>
public sealed class SessionBearerAuthenticator(IdentityDbContext db) : IBearerAuthenticator
{
    private const string BearerPrefix = "Bearer ";

    public async Task<AuthenticatedActor?> Authenticate(HttpContext httpContext, CancellationToken ct = default)
    {
        var header = httpContext.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var token = header[BearerPrefix.Length..].Trim();
        if (token.Length == 0)
        {
            return null;
        }

        var tokenHash = SessionTokens.HashAccessToken(token);
        var now = DateTimeOffset.UtcNow;

        var row = await db.Sessions
            .Where(s => s.AccessTokenHash == tokenHash && s.RevokedAt == null && s.AccessExpiresAt > now)
            .Join(db.Accounts, s => s.AccountId, a => a.AccountId, (s, a) => new { s, a })
            .SingleOrDefaultAsync(ct);

        if (row is null)
        {
            // Unknown/expired/revoked token -> anonymous. No distinct signal ever crosses this boundary
            // (no 401 oracle) — a downstream policy row denies exactly like an absent bearer header.
            return null;
        }

        var actor = new ActorRef(OpaqueId.Parse(row.a.AccountId), ActorKind.User);
        var region = ParseRegion(row.a.Region);

        return new AuthenticatedActor(actor, row.a.AccountState, region, row.a.Locale);
    }

    private static RegionCode ParseRegion(string stored)
    {
        var parts = stored.Split('-', 2);
        return parts.Length == 2 ? new RegionCode(parts[0], parts[1]) : new RegionCode(parts[0], null);
    }
}

/// <summary>
/// Session/refresh token minting + hashing (SLICE_S3_CONTRACT.md §1b): 256-bit random tokens, prefixed
/// `sst_`/`srt_` (greppable in logs/dumps), stored SHA-256 only — plaintext is one-time-visible in the
/// SessionCreated response and never persisted.
/// </summary>
public static class SessionTokens
{
    public const string AccessTokenPrefix = "sst_";
    public const string RefreshTokenPrefix = "srt_";

    // Session/refresh tokens are bearer SECRETS, not ids (unlike OpaqueId's ULID, which only needs
    // uniqueness) — always a CSPRNG, never the caller-supplied System.Random OpaqueId.New() takes.
    public static string NewAccessToken() => AccessTokenPrefix + RandomBase64Url();

    public static string NewRefreshToken() => RefreshTokenPrefix + RandomBase64Url();

    public static byte[] HashAccessToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public static byte[] HashRefreshToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string RandomBase64Url()
    {
        var bytes = RandomNumberGenerator.GetBytes(32); // 256 bits
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
