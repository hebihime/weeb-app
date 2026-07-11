using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts.Ids;
using Svac.Identity.Persistence;

namespace Svac.Identity.Auth;

/// <summary>Everything <see cref="SessionCreated"/>-minting needed to mint a fresh opaque session (SLICE_S3_CONTRACT.md §1b/§1c) — shared by signup/complete, auth/session, auth/refresh.</summary>
public sealed record IssuedSession(string AccessToken, DateTimeOffset AccessExpiresAt, string RefreshToken, string SessionId, string RefreshFamilyId);

public static class SessionIssuance
{
    /// <summary>
    /// Mints a session + its first refresh token against <paramref name="db"/> (participates in whatever
    /// ambient transaction <paramref name="db"/> already has, if any — callers inside <see
    /// cref="IdentityAtomicScope"/> pass its <c>Db</c>). Active-session cap (SLICE_S3_CONTRACT.md §1b):
    /// evicts the oldest live session(s) past <paramref name="maxActivePerAccount"/> BEFORE minting the
    /// new one, reason `cap_evicted`.
    /// </summary>
    public static async Task<IssuedSession> IssueAsync(
        IdentityDbContext db,
        string accountId,
        string? deviceId,
        string region,
        string lawfulBasis,
        int accessTtlMinutes,
        int refreshTtlDays,
        int maxActivePerAccount,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var activeCount = await db.Sessions.CountAsync(s => s.AccountId == accountId && s.RevokedAt == null, ct);
        if (activeCount >= maxActivePerAccount)
        {
            var toEvictCount = activeCount - maxActivePerAccount + 1;
            var oldest = await db.Sessions
                .Where(s => s.AccountId == accountId && s.RevokedAt == null)
                .OrderBy(s => s.CreatedAt)
                .Take(toEvictCount)
                .ToListAsync(ct);
            foreach (var evicted in oldest)
            {
                evicted.RevokedAt = now;
                evicted.RevokeReason = "cap_evicted";
            }
        }

        var sessionId = OpaqueId.New(IdPrefixes.Session, now, Random.Shared).ToString();
        var familyId = OpaqueId.New(IdPrefixes.Session, now, Random.Shared).ToString();
        var accessToken = SessionTokens.NewAccessToken();
        var refreshToken = SessionTokens.NewRefreshToken();
        var accessExpiresAt = now.AddMinutes(accessTtlMinutes);

        db.Sessions.Add(new Svac.Identity.Persistence.SessionEntity
        {
            SessionId = sessionId,
            AccountId = accountId,
            DeviceId = deviceId,
            AccessTokenHash = SessionTokens.HashAccessToken(accessToken),
            RefreshFamilyId = familyId,
            CreatedAt = now,
            LastSeenAt = now,
            AccessExpiresAt = accessExpiresAt,
            Region = region,
            LawfulBasis = lawfulBasis,
        });

        db.RefreshTokens.Add(new Svac.Identity.Persistence.RefreshTokenEntity
        {
            Id = OpaqueId.New(IdPrefixes.Session, now, Random.Shared).ToString(),
            SessionId = sessionId,
            TokenHash = SessionTokens.HashRefreshToken(refreshToken),
            FamilyId = familyId,
            IssuedAt = now,
            ExpiresAt = now.AddDays(refreshTtlDays),
            Region = region,
            LawfulBasis = lawfulBasis,
        });

        await db.SaveChangesAsync(ct);

        return new IssuedSession(accessToken, accessExpiresAt, refreshToken, sessionId, familyId);
    }
}
