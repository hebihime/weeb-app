namespace Svac.AdminHost.Domain.Auth;

/// <summary>
/// A minimal, transport-agnostic projection of what BOTH Entra OIDC and <c>DevSeamsStaffTransport</c> hand
/// the sign-in pipeline (SLICE_S5_CONTRACT.md §1b: "the pipeline after [the transport] is IDENTICAL in dev
/// and prod") — never a ClaimsPrincipal, so <see cref="StaffSignInPipeline"/> stays unit-testable with
/// zero ASP.NET dependency. <paramref name="HasMfaClaim"/> is the caller's own translation of Entra's
/// <c>amr</c>-contains-"mfa" / <c>acr</c> auth-context claim (prod) or a fixture's deterministic flag
/// (dev) — the pipeline itself never parses a raw claim bag.
/// </summary>
public sealed record StaffExternalClaims(string ExternalSubject, bool HasMfaClaim, string Email, string DisplayName);
