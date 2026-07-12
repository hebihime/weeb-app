using System.Security.Cryptography;
using System.Text;
using Svac.DomainCore.Contracts.FieldEncryption;

namespace Svac.Identity.Deletion;

/// <summary>
/// The ONE keyed-HMAC construction OQ-3's ban-evasion ref uses (SLICE_S3_CONTRACT.md §11/§13, RATIFIED
/// (a)): a KEYED re-key, never an unsalted hash (MinorProt-F4 precedent) — a candidate email cannot be
/// confirmed against <c>identity.ban_evasion_refs</c> without the vault-held secret. Shared between the
/// WRITER (<see cref="AccountLifecycle.RequestDeletion"/>, on a banned account's deletion) and the
/// CONSULT (<see cref="Svac.Identity.Auth.SignupCompletionService"/>, refusing re-registration) so both
/// sides derive the identical reference for the same email.
/// </summary>
public static class BanEvasionRefs
{
    public const string HmacKeyName = "identity-ban-evasion-hmac-v1";

    public static async Task<string> ComputeHmacRef(IFieldKeyVault keyVault, string valueLower, CancellationToken ct) =>
        Convert.ToHexString(await ComputeHmacBytes(keyVault, valueLower, ct));

    public static async Task<byte[]> ComputeHmacBytes(IFieldKeyVault keyVault, string value, CancellationToken ct)
    {
        var key = await keyVault.GetNamedSecret(HmacKeyName, ct);
        return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));
    }
}
