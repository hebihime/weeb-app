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

// --- /v1/me/* wire DTOs (SLICE_S3_CONTRACT.md §1c) — same "internal transport shape, never a cross-
// module contract" rule as the block above; AccountSelf/SessionSummary/SessionCreated (the shapes OTHER
// modules or the OpenAPI "New components" list name) live in Svac.Identity.Contracts instead.

public sealed record SettingsUpdateRequest(string? Locale);

public sealed record HandleChangeRequest(string? Handle);

public sealed record EmailChangeRequest(string? Email);

public sealed record DeviceRegisterRequest(string? Platform, string? PushToken);

public sealed record DeviceRegistered(string DeviceId);

/// <summary>One row of `GET /v1/me/push-consents` (SLICE_S3_CONTRACT.md §1c) — categories 1-7,9 only; category 8 never appears (absence law, §0/§12 item 10).</summary>
public sealed record PushConsentRow(int Category, bool Enabled);

public sealed record PushConsentSetRequest(bool? Enabled);

/// <summary>`POST /v1/me/export` 202 response (SLICE_S3_CONTRACT.md §1c) — duplicate active request returns the SAME job, idempotent.</summary>
public sealed record ExportRequested(string ExportId);
