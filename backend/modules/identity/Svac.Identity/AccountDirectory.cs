using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Deterministic;
using Svac.Identity.Contracts;
using Svac.Identity.Persistence;

namespace Svac.Identity;

/// <summary>
/// The real <see cref="IAccountDirectory"/> (SLICE_S3_CONTRACT.md §1a) — "S10's privacy matrix consumes
/// this, never identity's tables." Replaces the Phase-1 <c>AccountDirectoryStub</c>. <see
/// cref="GetAgeYears"/> decrypts <c>birthdate_enc</c> on every call via <see cref="IFieldEncryptor"/> and
/// derives via <see cref="AgeMath"/> — the raw birthdate never leaves this method.
/// </summary>
internal sealed class AccountDirectory(IdentityDbContext db, IFieldEncryptor fieldEncryptor) : IAccountDirectory
{
    public async Task<AccountState?> GetState(OpaqueId accountId, CancellationToken ct = default)
    {
        var id = accountId.ToString();
        var stateText = await db.Accounts.Where(a => a.AccountId == id).Select(a => a.AccountState).SingleOrDefaultAsync(ct);
        return stateText is null ? null : ParseState(stateText);
    }

    public async Task<string?> GetHandle(OpaqueId accountId, CancellationToken ct = default)
    {
        var id = accountId.ToString();
        return await db.Accounts
            .Where(a => a.AccountId == id && a.TombstonedAt == null)
            .Select(a => a.Handle)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<int?> GetAgeYears(OpaqueId accountId, CancellationToken ct = default)
    {
        var id = accountId.ToString();
        var enc = await db.Accounts.Where(a => a.AccountId == id).Select(a => a.BirthdateEnc).SingleOrDefaultAsync(ct);
        if (enc is null)
        {
            return null;
        }

        var birthdateText = await fieldEncryptor.Unprotect(FieldEncryptionPurpose.Birthdate, enc, ct);
        var birthdate = DateOnly.Parse(birthdateText, System.Globalization.CultureInfo.InvariantCulture);
        return AgeMath.AgeYears(birthdate, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    private static AccountState ParseState(string text) => text switch
    {
        "active" => AccountState.Active,
        "suspended" => AccountState.Suspended,
        "banned" => AccountState.Banned,
        "deleted" => AccountState.Deleted,
        _ => throw new InvalidOperationException($"unrecognized account_state \"{text}\"."),
    };
}
