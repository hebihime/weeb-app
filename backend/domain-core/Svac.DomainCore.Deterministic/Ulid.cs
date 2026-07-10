namespace Svac.DomainCore.Deterministic;

/// <summary>
/// Pure ULID codec (SLICE_S1_CONTRACT.md §1b: "Typed opaque ids: prefixed ULIDs"). ULID = 48-bit
/// millisecond timestamp + 80-bit randomness, Crockford base32 encoded to a fixed 26 characters,
/// lexicographically sortable by creation time. No wall-clock read inside — every function that needs
/// "now" or "randomness" takes it as an explicit parameter, so the codec itself stays a pure function
/// arch-tested to have zero IO references.
/// </summary>
public static class Ulid
{
    private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int EncodedLength = 26;
    private const long MaxTimestamp = (1L << 48) - 1;

    /// <summary>Encodes a timestamp + 80 bits of randomness into a 26-char Crockford base32 ULID body.</summary>
    /// <param name="timestamp">Milliseconds since Unix epoch. Caller supplies "now" — never read here.</param>
    /// <param name="randomness">Exactly 10 bytes (80 bits) of caller-supplied randomness.</param>
    public static string Encode(long timestamp, ReadOnlySpan<byte> randomness)
    {
        if (timestamp < 0 || timestamp > MaxTimestamp)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp), timestamp, "ULID timestamp must fit in 48 bits.");
        }
        if (randomness.Length != 10)
        {
            throw new ArgumentException("ULID randomness must be exactly 10 bytes (80 bits).", nameof(randomness));
        }

        Span<byte> bytes = stackalloc byte[16];
        for (var i = 0; i < 6; i++)
        {
            bytes[5 - i] = (byte)((timestamp >> (i * 8)) & 0xFF);
        }
        randomness.CopyTo(bytes[6..]);

        return EncodeBase32(bytes);
    }

    /// <summary>Prepends a stable prefix + underscore to an encoded ULID body (e.g. "usr_01H...").</summary>
    public static string WithPrefix(string prefix, string ulidBody) => $"{prefix}_{ulidBody}";

    /// <summary>Splits a prefixed opaque id back into (prefix, ulidBody). Throws on malformed input.</summary>
    public static (string Prefix, string Body) SplitPrefixed(string prefixedId)
    {
        var idx = prefixedId.IndexOf('_');
        if (idx <= 0 || idx == prefixedId.Length - 1)
        {
            throw new FormatException($"\"{prefixedId}\" is not a prefixed opaque id (expected \"<prefix>_<ulid>\").");
        }
        var prefix = prefixedId[..idx];
        var body = prefixedId[(idx + 1)..];
        if (body.Length != EncodedLength || !IsValidCrockford(body))
        {
            throw new FormatException($"\"{prefixedId}\" does not carry a well-formed 26-char ULID body.");
        }
        return (prefix, body);
    }

    private static bool IsValidCrockford(string body)
    {
        foreach (var c in body)
        {
            if (CrockfordAlphabet.IndexOf(char.ToUpperInvariant(c)) < 0)
            {
                return false;
            }
        }
        return true;
    }

    private static string EncodeBase32(ReadOnlySpan<byte> bytes)
    {
        // 16 bytes = 128 bits -> 26 Crockford base32 characters (5 bits each, last char uses 3 bits).
        Span<char> output = stackalloc char[EncodedLength];
        var bitBuffer = 0;
        var bitCount = 0;
        var outIndex = 0;

        foreach (var b in bytes)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;
            while (bitCount >= 5)
            {
                bitCount -= 5;
                output[outIndex++] = CrockfordAlphabet[(bitBuffer >> bitCount) & 0x1F];
            }
        }
        if (bitCount > 0)
        {
            output[outIndex++] = CrockfordAlphabet[(bitBuffer << (5 - bitCount)) & 0x1F];
        }

        return new string(output);
    }
}
