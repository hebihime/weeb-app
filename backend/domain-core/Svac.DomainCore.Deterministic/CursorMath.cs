using System.Globalization;
using System.Text;

namespace Svac.DomainCore.Deterministic;

/// <summary>
/// Pure opaque-cursor codec (PHASE_2A_SUBSTRATE.md §3: "cursor/pagination math -- pure encode/decode for
/// CursorPage"). v0 encodes a non-negative row offset as base64url text — opaque to the client (never a
/// client-decodable ordering key beyond "how many rows came before"), reversible, and stable across
/// processes since it carries no server-local state. A future cursor shape (composite keyset pagination)
/// is a versioned change to THIS codec, never a second parallel cursor format (SLICE_S1_CONTRACT.md §1c:
/// "CursorPage" is pinned once so no later slice invents a second shape).
/// </summary>
public static class CursorMath
{
    /// <summary>Encodes a non-negative offset into an opaque, URL-safe cursor string.</summary>
    public static string Encode(long offset)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "cursor offset must not be negative.");
        }

        var bytes = Encoding.UTF8.GetBytes(offset.ToString(CultureInfo.InvariantCulture));
        return ToBase64Url(bytes);
    }

    /// <summary>Decodes a previously-encoded cursor back into its offset. Throws <see cref="FormatException"/> on any malformed input.</summary>
    public static long Decode(string cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            throw new FormatException("cursor must not be null or empty.");
        }

        byte[] bytes;
        try
        {
            bytes = FromBase64Url(cursor);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FormatException($"\"{cursor}\" is not a well-formed cursor.", ex);
        }

        var text = Encoding.UTF8.GetString(bytes);
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var offset))
        {
            throw new FormatException($"\"{cursor}\" does not decode to a non-negative integer offset.");
        }

        return offset;
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string cursor)
    {
        var padded = cursor.Replace('-', '+').Replace('_', '/');
        var remainder = padded.Length % 4;
        if (remainder == 2)
        {
            padded += "==";
        }
        else if (remainder == 3)
        {
            padded += "=";
        }
        else if (remainder != 0)
        {
            throw new FormatException($"\"{cursor}\" is not a well-formed base64url cursor.");
        }
        return Convert.FromBase64String(padded);
    }
}
