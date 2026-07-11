namespace Svac.Identity.Endpoints;

// Wire DTOs for the signup/* and auth/* routes (SLICE_S3_CONTRACT.md §1c). Internal to Svac.Identity —
// every OTHER module reaches identity only through Svac.Identity.Contracts (SessionCreated, AccountSelf,
// ...); these shapes are the HTTP transport's own concern, never a cross-module contract. Trust-field-free
// by construction (§1c: "all request DTOs trust-field-free"; arch scan pattern account_state|email_verified|
// attested|deletion_|session_ — none of these fields exist below).

public sealed record HandleAvailabilityResponse(bool Available);

public sealed record EmailVerificationRequest(string? Email, string? Locale);

public sealed record ChallengeIssued(string ChallengeId);

public sealed record EmailVerificationConfirmRequest(string? ChallengeId, string? Code);

public sealed record VerifiedTokenIssued(string VerifiedToken);

public sealed record SignupCompleteRequest(string? VerifiedToken, string? Handle, string? Birthdate, string? FandomTag, string? Locale);

public sealed record AuthEmailCodeRequest(string? Email);

public sealed record AuthSessionRequest(string? Email, string? Code);

public sealed record AuthRefreshRequest(string? RefreshToken);
