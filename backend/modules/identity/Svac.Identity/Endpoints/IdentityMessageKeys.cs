namespace Svac.Identity.Endpoints;

/// <summary>The identity-specific server-emitted message keys (SLICE_S3_CONTRACT.md §1d), registered ×4 locales in contracts/message-keys.json.</summary>
public static class IdentityMessageKeys
{
    /// <summary>ONE wire shape for BOTH the 18-and-13 minor floors (§1g/§0) — never distinguishes which floor tripped.</summary>
    public const string SignupRefusedAgeFloor = "signup.refused_age_floor";
    public const string HandleTaken = "handle.taken";
    public const string HandleInvalid = "handle.invalid";
    /// <summary>Reused across the whole challenge/verifiedToken credential surface — invalid, expired, exhausted, or already-consumed all render this ONE Problem (§1c).</summary>
    public const string AuthCodeInvalid = "auth.code_invalid";
}
