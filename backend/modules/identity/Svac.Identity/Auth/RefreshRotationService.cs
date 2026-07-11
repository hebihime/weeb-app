using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Email;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Deterministic;
using Svac.Identity.Config;
using Svac.Identity.Persistence;

namespace Svac.Identity.Auth;

/// <summary>Closed outcome union for POST /v1/auth/refresh (SLICE_S3_CONTRACT.md §1b/§1c). Every failure — unknown, expired, OR reused — renders the SAME generic Problem: "a thief learns nothing."</summary>
public abstract record RefreshOutcome
{
    public sealed record RotatedResult(IssuedSession Session, string AccountId) : RefreshOutcome;

    public sealed record FailedResult : RefreshOutcome;

    public static readonly RefreshOutcome Failed = new FailedResult();
}

/// <summary>
/// Single-use, family-linked refresh rotation (SLICE_S3_CONTRACT.md §1b): "presenting an already-consumed
/// refresh token revokes the entire family + session, appends a 3A audit event, and queues the category-8
/// security notice" — rotation converts token theft from an invisible condition into a detectable event.
/// </summary>
public sealed class RefreshRotationService(IdentityDbContext db, IConfigRegistry config, IEmailSender emailSender)
{
    public async Task<RefreshOutcome> Rotate(string presentedToken, RequestContext ctx, CancellationToken ct)
    {
        var presentedHash = SessionTokens.HashRefreshToken(presentedToken);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var row = await db.RefreshTokens
            .FromSqlInterpolated($"SELECT * FROM identity.refresh_tokens WHERE token_hash = {presentedHash} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

        if (row is null)
        {
            await tx.RollbackAsync(ct);
            return RefreshOutcome.Failed;
        }

        var now = DateTimeOffset.UtcNow;

        if (row.ConsumedAt is not null)
        {
            // REUSE — the theft alarm. Revoke the whole family (the one session this family was ever
            // bound to) + append the audit event + queue the security mail, all in the SAME tx as the
            // revoke via the DI-scoped IEventStore (both IdentityDbContext row mutation and the audit
            // Append below share this ambient transaction — Append only calls SaveChangesAsync, no
            // internal BeginTransaction, so it composes cleanly here unlike Replay).
            var session = await db.Sessions.SingleOrDefaultAsync(s => s.SessionId == row.SessionId, ct);
            string? notifyEmail = null;
            if (session is not null && session.RevokedAt is null)
            {
                session.RevokedAt = now;
                session.RevokeReason = "rotation_reuse";
                notifyEmail = await db.Accounts.Where(a => a.AccountId == session.AccountId).Select(a => a.Email).FirstOrDefaultAsync(ct);

                var eventStore = new EventStoreOverSharedContext(db);
                await eventStore.AppendAudit(session.AccountId, "identity.session_family_revoked", ctx, ct);
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            if (notifyEmail is not null)
            {
                await emailSender.SendAsync(new EmailMessage(notifyEmail, "email.sessions_revoked", ctx.Locale, EmptyModel), ctx, ct);
            }

            return RefreshOutcome.Failed;
        }

        if (row.ExpiresAt <= now)
        {
            await tx.RollbackAsync(ct);
            return RefreshOutcome.Failed;
        }

        var liveSession = await db.Sessions.SingleOrDefaultAsync(s => s.SessionId == row.SessionId, ct);
        if (liveSession is null || liveSession.RevokedAt is not null)
        {
            await tx.RollbackAsync(ct);
            return RefreshOutcome.Failed;
        }

        var accessTtlMinutes = await config.GetValue<int>(IdentityConfigKeys.SessionAccessTtlMinutes, ct);
        var refreshTtlDays = await config.GetValue<int>(IdentityConfigKeys.SessionRefreshTtlDays, ct);

        var newAccessToken = SessionTokens.NewAccessToken();
        var newRefreshToken = SessionTokens.NewRefreshToken();
        var newAccessExpiresAt = now.AddMinutes(accessTtlMinutes);

        liveSession.AccessTokenHash = SessionTokens.HashAccessToken(newAccessToken);
        liveSession.AccessExpiresAt = newAccessExpiresAt;
        liveSession.LastSeenAt = now;

        var newRefreshRowId = OpaqueId.New(IdPrefixes.Session, now, Random.Shared).ToString();
        db.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = newRefreshRowId,
            SessionId = liveSession.SessionId,
            TokenHash = SessionTokens.HashRefreshToken(newRefreshToken),
            FamilyId = row.FamilyId,
            IssuedAt = now,
            ExpiresAt = now.AddDays(refreshTtlDays),
            Region = row.Region,
            LawfulBasis = row.LawfulBasis,
        });

        row.ConsumedAt = now;
        row.SupersededBy = newRefreshRowId;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var issued = new IssuedSession(newAccessToken, newAccessExpiresAt, newRefreshToken, liveSession.SessionId, row.FamilyId);
        return new RefreshOutcome.RotatedResult(issued, liveSession.AccountId);
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyModel = new Dictionary<string, string>();

    /// <summary>Appends the ONE audit event this rotation needs, over the SAME ambient IdentityDbContext transaction's underlying connection — deliberately NOT the full cross-schema IdentityAtomicScope (that helper opens a brand-new connection, which would NOT share this method's already-open `db`-ambient transaction). PostgresEventStore itself only needs a CoreDbContext; a fresh one bound to `db`'s OWN connection/transaction gives the same one-tx guarantee without a second physical connection.</summary>
    private sealed class EventStoreOverSharedContext(IdentityDbContext identityDb)
    {
        public async Task AppendAudit(string accountId, string eventType, RequestContext ctx, CancellationToken ct)
        {
            var coreDb = new Svac.DomainCore.Persistence.CoreDbContext(
                new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Svac.DomainCore.Persistence.CoreDbContext>()
                    .UseNpgsql(identityDb.Database.GetDbConnection())
                    .Options);
            var currentTx = identityDb.Database.CurrentTransaction?.GetDbTransaction()
                ?? throw new InvalidOperationException("AppendAudit requires an ambient transaction on identityDb.");
            await coreDb.Database.UseTransactionAsync(currentTx, ct);

            var store = new Svac.DomainCore.EventStore.PostgresEventStore(coreDb);
            await store.Append(StreamType.Audit, accountId, eventType, "{}", ctx, ExpectedVersion.AnyVersion, ct);
            await coreDb.DisposeAsync();
        }
    }
}
