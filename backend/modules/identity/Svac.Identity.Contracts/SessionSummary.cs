using Svac.DomainCore.Contracts.Ids;

namespace Svac.Identity.Contracts;

/// <summary>
/// <c>GET /v1/me/sessions</c> row shape (SLICE_S3_CONTRACT.md §1c) — "the honest theft lever."
/// <see cref="Platform"/> is null when unknown (no device bound to the session).
/// </summary>
public sealed record SessionSummary(OpaqueId SessionId, string? Platform, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, bool Current);
