using Microsoft.EntityFrameworkCore;
using Npgsql;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Deterministic;
using Svac.Identity.Config;
using Svac.Identity.Persistence;

namespace Svac.Identity.Auth;

/// <summary>Closed outcome union for POST /v1/me/handle (SLICE_S3_CONTRACT.md §1c/§3b/§5).</summary>
public abstract record HandleChangeOutcome
{
    public sealed record ChangedResult(string NewHandle) : HandleChangeOutcome;

    /// <summary>Requested handle already equals the account's current one — a harmless no-op, never a cooldown/history write.</summary>
    public sealed record NoOpResult(string Handle) : HandleChangeOutcome;

    /// <summary>The identity-local cooldown check (§5: NOT a 10A key) still serializes as THE one LimitReached shape.</summary>
    public sealed record CooldownResult(LimitReached LimitReached) : HandleChangeOutcome;

    public sealed record InvalidResult : HandleChangeOutcome;

    /// <summary>Reserved, retired, and taken all collapse to this ONE outcome (§1c: "reserved/retired/taken = one handle.taken").</summary>
    public sealed record TakenResult : HandleChangeOutcome;

    public static readonly HandleChangeOutcome Invalid = new InvalidResult();
    public static readonly HandleChangeOutcome Taken = new TakenResult();
}

/// <summary>
/// POST /v1/me/handle (SLICE_S3_CONTRACT.md §1c/§2/§3b/§5/§8). Cooldown = `identity.handle_history`
/// max(changed_at) + `identity.handle.cooldown_days`, an identity-local deterministic check (never a 10A
/// key — widening S1's calendar-reset quota union for one rolling-window consumer is the substrate
/// mutation vanilla-by-default forbids); the deny still renders THE one LimitReached component. The
/// mutation itself is atomic across schema `identity` (accounts.handle + handle_history insert) and
/// schema `core` (the `identity.handle_changed` audit event, §8) via <see cref="IdentityAtomicScope"/>,
/// the same cross-schema-tx seam <see cref="SignupCompletionService"/> uses.
/// </summary>
public sealed class HandleChangeService(IdentityDbContext db, IConfigRegistry config)
{
    public async Task<HandleChangeOutcome> Change(string accountId, string? handleRaw, RequestContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handleRaw))
        {
            return HandleChangeOutcome.Invalid;
        }

        var validation = HandleRules.Validate(handleRaw);
        if (!validation.IsValid)
        {
            return HandleChangeOutcome.Invalid;
        }
        var canonical = validation.Canonical!;

        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("IdentityDbContext has no connection string configured.");
        await using var scope = await IdentityAtomicScope.OpenAsync(connectionString, ct);

        var account = await scope.Db.Accounts.SingleAsync(a => a.AccountId == accountId, ct);
        if (account.Handle == canonical)
        {
            await scope.RollbackAsync(ct);
            return new HandleChangeOutcome.NoOpResult(canonical);
        }

        var now = DateTimeOffset.UtcNow;
        var cooldownDays = await config.GetValue<int>(IdentityConfigKeys.HandleCooldownDays, ct);
        var lastChangedAt = await scope.Db.HandleHistory
            .Where(h => h.AccountId == accountId)
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => (DateTimeOffset?)h.ChangedAt)
            .FirstOrDefaultAsync(ct);

        if (lastChangedAt is not null)
        {
            var eligibleAt = lastChangedAt.Value.AddDays(cooldownDays);
            if (now < eligibleAt)
            {
                await scope.RollbackAsync(ct);
                return new HandleChangeOutcome.CooldownResult(new LimitReached(
                    QuotaKey: "identity.handle.change",
                    MessageKey: MessageKeys.LimitReachedGeneric,
                    ResetsAt: eligibleAt,
                    PremiumExtends: false));
            }
        }

        var reservedOrRetired = await scope.Db.ReservedHandles.AnyAsync(h => h.Handle == canonical, ct)
            || await scope.Db.RetiredHandles.AnyAsync(h => h.Handle == canonical, ct);
        if (reservedOrRetired)
        {
            await scope.RollbackAsync(ct);
            return HandleChangeOutcome.Taken;
        }

        var oldHandle = account.Handle;
        account.Handle = canonical;
        scope.Db.HandleHistory.Add(new HandleHistoryEntity
        {
            Id = OpaqueId.New(IdPrefixes.Event, now, Random.Shared).ToString(),
            AccountId = accountId,
            OldHandle = oldHandle,
            NewHandle = canonical,
            ChangedAt = now,
            Region = ctx.Region.ToString(),
            LawfulBasis = LawfulBasisResolver.Resolve(ctx.LawfulBasisVariant.Key, "identity.handle_history", "handle.changed", ctx.Region.ToString()),
        });

        try
        {
            await scope.Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException dbEx) when (IsUniqueViolation(dbEx, "ux_accounts_handle"))
        {
            await scope.RollbackAsync(ct);
            return HandleChangeOutcome.Taken;
        }

        await scope.Events.Append(StreamType.Audit, accountId, "identity.handle_changed", "{}", ctx, ExpectedVersion.AnyVersion, ct);
        await scope.CommitAsync(ct);

        return new HandleChangeOutcome.ChangedResult(canonical);
    }

    private static bool IsUniqueViolation(DbUpdateException dbEx, string constraintName) =>
        dbEx.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation && pg.ConstraintName == constraintName;
}
