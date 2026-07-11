namespace Svac.DomainCore.Contracts.Api;

/// <summary>GET /health liveness payload (SLICE_S1_CONTRACT.md §1c) — the compose healthcheck target's response shape.</summary>
public sealed record HealthStatus(string Status, DateTimeOffset CheckedAt);

/// <summary>GET /v1/client-config response (SLICE_S1_CONTRACT.md §1c), sourced from i18n/locales.json at boot.</summary>
public sealed record ClientConfigResponse(string ApiVersion, IReadOnlyList<string> Locales, string DefaultLocale);

/// <summary>Pagination shape pinned early (SLICE_S1_CONTRACT.md §1c) so no later slice invents a second cursor shape.</summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);

/// <summary>
/// The REQUEST side of opaque-cursor pagination (PHASE_2A_SUBSTRATE.md §2, SLICE_S5_CONTRACT.md §1d) —
/// the counterpart <see cref="CursorPage{T}"/> already pins for responses. Single definition so
/// <see cref="Svac.DomainCore.Contracts.Audit.IAuditReader"/>/<see
/// cref="Svac.DomainCore.Contracts.Purge.IPurgeRunReader"/> and every later paginated read share it.
/// </summary>
public sealed record CursorPageRequest(string? Cursor = null, int Limit = 50);
