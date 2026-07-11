namespace Svac.DomainCore.Contracts.Consent;

/// <summary>
/// The nine push-notification categories (SLICE_S3_CONTRACT.md §1c/§1b) minus category 8. Category 8 is
/// deliberately UNREPRESENTABLE — no enum member names it — so "never mutable" is a type fact enforced by
/// the compiler, not a runtime check a future call site could forget.
/// </summary>
public enum PushCategoryValue
{
    Category1 = 1,
    Category2 = 2,
    Category3 = 3,
    Category4 = 4,
    Category5 = 5,
    Category6 = 6,
    Category7 = 7,
    Category9 = 9,
}

/// <summary>
/// CLOSED consent taxonomy (PHASE_2A_SUBSTRATE.md §2, SLICE_S3_CONTRACT.md §1b). Declared now with ZERO
/// real writers at S1/S2 (Marketing stays declared-unwritten even at S3 per DR-7.1 — "nothing may honor a
/// flag no surface writes"); S3's real writers are AgeAttestation18Plus/TermsAcceptance (signup) and
/// PushCategory (per-category consent changes).
/// </summary>
public abstract record ConsentKind
{
    public sealed record AgeAttestation18PlusKind : ConsentKind;

    public sealed record TermsAcceptanceKind : ConsentKind;

    public sealed record PushCategoryKind(PushCategoryValue Category) : ConsentKind;

    public sealed record IrlAccessKind : ConsentKind;

    public sealed record BackgroundLocationKind : ConsentKind;

    public sealed record SpecialCategoryIdentityKind : ConsentKind;

    public sealed record IdentityVerificationKind : ConsentKind;

    public sealed record MarketingKind : ConsentKind;

    public static readonly ConsentKind AgeAttestation18Plus = new AgeAttestation18PlusKind();
    public static readonly ConsentKind TermsAcceptance = new TermsAcceptanceKind();
    public static ConsentKind PushCategory(PushCategoryValue category) => new PushCategoryKind(category);
    public static readonly ConsentKind IrlAccess = new IrlAccessKind();
    public static readonly ConsentKind BackgroundLocation = new BackgroundLocationKind();
    public static readonly ConsentKind SpecialCategoryIdentity = new SpecialCategoryIdentityKind();
    public static readonly ConsentKind IdentityVerification = new IdentityVerificationKind();
    public static readonly ConsentKind Marketing = new MarketingKind();
}
