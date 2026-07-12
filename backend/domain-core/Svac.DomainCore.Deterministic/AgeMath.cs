namespace Svac.DomainCore.Deterministic;

/// <summary>
/// Pure age arithmetic (PHASE_2A_SUBSTRATE.md §3, SLICE_S3_CONTRACT.md §1g/§12 item 7). The 18/13 floors
/// are CODE CONSTANTS here — no 9A config key may ever match <c>age|floor|minor</c> (a desk tunable that
/// could lower a child-safety floor must be structurally impossible, same logic as S1's DevSeams-not-in-
/// 9A ruling). Zero wall-clock reads: every function takes "as of" explicitly. A future birthdate (one
/// that is later than the "as of" date) is an INVALID INPUT, never a verdict — the minor-protection
/// posture never derives a false floor-pass from a malformed date.
/// </summary>
public static class AgeMath
{
    /// <summary>The server-authoritative adult floor (SLICE_S3_CONTRACT.md §12 item 7). A code constant, never configurable.</summary>
    public const int AdultFloorYears = 18;

    /// <summary>The COPPA hard floor (SLICE_S3_CONTRACT.md §0: "the server-authoritative 18+ floor and under-13 COPPA hard floor"). A code constant, never configurable.</summary>
    public const int CoppaFloorYears = 13;

    /// <summary>
    /// Whole years elapsed from <paramref name="birthdate"/> through <paramref name="asOf"/> (inclusive
    /// of the birthday itself — a person is already the new age ON their birthday, not the day after).
    /// Feb-29 birthdates observe their birthday on Mar-1 in a non-leap year (never Feb-28) — the rule
    /// this library pins, distinct from <see cref="DateOnly.AddYears"/>'s default clamp-to-Feb-28.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="asOf"/> precedes <paramref name="birthdate"/> — a future birthdate relative to "as of" is a malformed input, never a valid age of zero or negative.</exception>
    public static int AgeYears(DateOnly birthdate, DateOnly asOf)
    {
        if (asOf < birthdate)
        {
            throw new ArgumentException(
                $"asOf ({asOf}) precedes birthdate ({birthdate}) — a future birthdate is an invalid input, never a verdict.",
                nameof(asOf));
        }

        var age = asOf.Year - birthdate.Year;
        var birthdayObservedThisYear = BirthdayObservedIn(birthdate, asOf.Year);
        if (asOf < birthdayObservedThisYear)
        {
            age--;
        }

        return age;
    }

    /// <summary>True iff the subject has reached <paramref name="years"/> of age as of <paramref name="asOf"/> — the birthday itself already passes.</summary>
    public static bool IsAtLeast(DateOnly birthdate, int years, DateOnly asOf) => AgeYears(birthdate, asOf) >= years;

    /// <summary>The exact calendar date the birthday is OBSERVED in a given year, applying the Feb-29 -> Mar-1 rule for non-leap years.</summary>
    private static DateOnly BirthdayObservedIn(DateOnly birthdate, int year)
    {
        if (birthdate.Month == 2 && birthdate.Day == 29 && !DateTime.IsLeapYear(year))
        {
            return new DateOnly(year, 3, 1);
        }
        return new DateOnly(year, birthdate.Month, birthdate.Day);
    }
}
