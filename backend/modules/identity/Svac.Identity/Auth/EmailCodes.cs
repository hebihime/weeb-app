using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Svac.DomainCore.Contracts.FieldEncryption;

namespace Svac.Identity.Auth;

/// <summary>
/// The 6-digit email-challenge code (SLICE_S3_CONTRACT.md §1b): "keyed HMAC codes (IFieldKeyVault named
/// secret) ... a 6-digit space must not be offline-brutable." Keyed (not a bare SHA-256) so the
/// email_challenges table alone, even fully exfiltrated, cannot be offline-brute-forced against the
/// 10^6 code space without the vault-held named secret.
/// </summary>
public static class EmailCodes
{
    /// <summary>The IFieldKeyVault named-secret door this code family hashes under — distinct from the verified-token and email-quota-actor secrets (separate key material per purpose, standard crypto hygiene).</summary>
    public const string NamedSecretKey = "identity.email_code_hmac";

    public static string NewCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    public static async Task<byte[]> Hash(IFieldKeyVault vault, string code, CancellationToken ct)
    {
        var key = await vault.GetNamedSecret(NamedSecretKey, ct);
        return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(code));
    }

    public static bool FixedTimeEquals(byte[] a, byte[] b) => CryptographicOperations.FixedTimeEquals(a, b);
}
