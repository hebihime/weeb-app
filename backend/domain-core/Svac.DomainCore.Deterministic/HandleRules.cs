using System.Globalization;
using System.Text.RegularExpressions;

namespace Svac.DomainCore.Deterministic;

/// <summary>The outcome of canonicalizing + validating a candidate handle (PHASE_2A_SUBSTRATE.md §3, SLICE_S3_CONTRACT.md §2 "canonical NFKC-folded lowercase").</summary>
public sealed record HandleValidationResult(bool IsValid, string? Canonical, string? ReasonKey)
{
    public static HandleValidationResult Valid(string canonical) => new(true, canonical, null);
    public static HandleValidationResult Invalid(string reasonKey) => new(false, null, reasonKey);
}

/// <summary>
/// Pure handle canonicalization + validation (PHASE_2A_SUBSTRATE.md §3, SLICE_S3_CONTRACT.md §2: "handle
/// text NOT NULL -- canonical NFKC-folded lowercase"). NFKC folding collapses visually/semantically
/// equivalent Unicode encodings (full-width digits, compatibility ligatures, etc.) to one canonical form
/// BEFORE the charset lock runs, so two differently-encoded inputs that render identically always
/// canonicalize to the same stored handle (SLICE_S3_CONTRACT.md §2 uniqueness index relies on this).
/// Confusable rejection is a conservative denylist of common Cyrillic/Greek homoglyphs of Latin letters —
/// impersonation-defense, not a full Unicode confusables-table implementation (that upgrade is additive,
/// never a breaking change to the charset lock itself).
/// </summary>
public static class HandleRules
{
    public const int MinLength = 3;
    public const int MaxLength = 20;

    /// <summary>The only characters a canonicalized handle may contain (SLICE_S3_CONTRACT.md §2: display-inert, public by definition).</summary>
    private static readonly Regex AllowedCharset = new("^[a-z0-9_]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A conservative homoglyph denylist: Cyrillic/Greek code points that render indistinguishably from
    /// Latin a/e/o/p/c/x/i/j/s/y/b/k/m/h/t/v/n on common UI fonts — exactly the impersonation vector a
    /// handle namespace with no password/email disambiguation (SLICE_S3_CONTRACT.md §1b) must close.
    /// </summary>
    private static readonly HashSet<int> ConfusableCodePoints = new()
    {
        0x0430, 0x0435, 0x043E, 0x0440, 0x0441, 0x0445, // Cyrillic а е о р с х
        0x0456, 0x0458, 0x0455, 0x0443, 0x0432, 0x043A, // Cyrillic і ј ѕ у в к
        0x0501, 0x043D, 0x0442, // Cyrillic ԁ н т
        0x03BF, 0x03B1, 0x03B5, // Greek ο α ε
    };

    /// <summary>NFKC-fold then lowercase-fold (invariant culture — a handle's canonical form never varies by locale).</summary>
    public static string Canonicalize(string raw) => raw.Normalize(System.Text.NormalizationForm.FormKC).ToLower(CultureInfo.InvariantCulture);

    /// <summary>Canonicalizes then validates length + charset + confusable-freedom, in that order (a mis-encoded confusable must fold before the charset lock ever inspects it).</summary>
    public static HandleValidationResult Validate(string raw)
    {
        var canonical = Canonicalize(raw);

        if (canonical.Length < MinLength || canonical.Length > MaxLength)
        {
            return HandleValidationResult.Invalid("handle.invalid_length");
        }

        if (ContainsConfusable(canonical))
        {
            return HandleValidationResult.Invalid("handle.confusable_rejected");
        }

        if (!AllowedCharset.IsMatch(canonical))
        {
            return HandleValidationResult.Invalid("handle.invalid_charset");
        }

        return HandleValidationResult.Valid(canonical);
    }

    private static bool ContainsConfusable(string canonical)
    {
        foreach (var rune in canonical.EnumerateRunes())
        {
            if (ConfusableCodePoints.Contains(rune.Value))
            {
                return true;
            }
        }
        return false;
    }
}
