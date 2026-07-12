using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Svac.AdminHost.ConfigRegistry;

/// <summary>
/// Mints/verifies the founder-scope confirm-with-reason interstitial's opaque <c>confirmToken</c>
/// (SLICE_S5_CONTRACT.md §4/§10.3: "explicit re-confirm"). Deliberately STATELESS — no server-side
/// session/DB row backs the propose→confirm round trip (S5 ships zero session table, §2), so the token
/// itself must carry everything needed to detect tampering: it is an <see cref="IDataProtector"/>-sealed
/// envelope over the EXACT (key, newValueJson, reason) triple the propose step minted it for, verified
/// byte-for-byte against whatever the confirm POST resubmits. <see cref="IDataProtectionProvider"/> is
/// already registered host-wide (<c>StaffAuthServiceCollectionExtensions.AddStaffAuth</c>'s
/// <c>AddDataProtection()</c> call, persisted via <c>CoreDbXmlRepository</c>) — this type adds a second,
/// independently-purposed protector off the SAME provider, never a new key ring.
/// </summary>
public sealed class ConfigConfirmToken(IDataProtectionProvider dataProtectionProvider)
{
    private const string Purpose = "Svac.AdminHost.ConfigRegistry.ConfirmInterstitial.v1";
    private const char FieldSeparator = '\u0001'; // never legally present in a JSON value or a staff-typed reason.

    private IDataProtector Protector => dataProtectionProvider.CreateProtector(Purpose);

    public string Mint(string key, string newValueJson, string reason) =>
        Protector.Protect(string.Join(FieldSeparator, key, newValueJson, reason));

    /// <summary>True iff <paramref name="token"/> decrypts AND its sealed (key, newValueJson, reason)
    /// triple matches the confirm POST's own fields exactly — a tampered hidden field (a different key,
    /// a smuggled-in different value, or a swapped reason) fails closed, never silently "confirms"
    /// something other than what the interstitial actually showed the operator.</summary>
    public bool TryVerify(string token, string key, string newValueJson, string reason)
    {
        string plaintext;
        try
        {
            plaintext = Protector.Unprotect(token);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false; // not even base64/urlsafe-shaped -- a hand-tampered or missing field, never a crash.
        }

        var parts = plaintext.Split(FieldSeparator);
        return parts.Length == 3
            && string.Equals(parts[0], key, StringComparison.Ordinal)
            && string.Equals(parts[1], newValueJson, StringComparison.Ordinal)
            && string.Equals(parts[2], reason, StringComparison.Ordinal);
    }
}
