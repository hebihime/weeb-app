using System.Text.RegularExpressions;

namespace Svac.Identity.Auth;

/// <summary>
/// Messy-input quarantine at the door (SLICE_S3_CONTRACT.md §8, L23): "strict parse at the door: ...
/// RFC-lite email + confusable rejection ... per-field validation Problem, never a 500." Deliberately
/// conservative: ASCII-only local+domain part (rejects homoglyph/confusable Unicode lookalikes wholesale
/// rather than maintaining a denylist the way HandleRules does for handles — an email address has no
/// display surface that needs Unicode, so ASCII-only is the narrowest defensible shape), lowercase-folded
/// for canonical lookup (the email column/index is `lower(email)`, SLICE_S3_CONTRACT.md §2).
/// </summary>
public static class EmailInput
{
    private const int MaxLength = 254; // RFC 5321 4.5.3.1.3

    private static readonly Regex ShapeRegex = new(
        @"^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)+$",
        RegexOptions.Compiled);

    /// <summary>True + the canonical lowercase form if <paramref name="raw"/> is a well-formed, ASCII-only email address.</summary>
    public static bool TryNormalize(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxLength)
        {
            return false;
        }

        if (!ShapeRegex.IsMatch(trimmed))
        {
            return false;
        }

        normalized = trimmed.ToLowerInvariant();
        return true;
    }
}
