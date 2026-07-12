using Svac.DomainCore.Contracts.Ids;

namespace Svac.Identity.Contracts;

/// <summary>
/// Response shape for every session-minting route (SLICE_S3_CONTRACT.md §1c: <c>POST /v1/signup/complete</c>,
/// <c>POST /v1/auth/session</c>, <c>POST /v1/auth/refresh</c>). <see cref="AccessToken"/>/<see cref="RefreshToken"/>
/// are the plaintext, one-time-visible <c>sst_</c>/<c>srt_</c> tokens — never persisted in plaintext
/// server-side (§1b: hash-stored only).
/// </summary>
public sealed record SessionCreated(string AccessToken, DateTimeOffset AccessExpiresAt, string RefreshToken, OpaqueId AccountId);
