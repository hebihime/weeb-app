namespace Svac.DomainCore.Contracts.Api;

/// <summary>GET /health liveness payload (SLICE_S1_CONTRACT.md §1c) — the compose healthcheck target's response shape.</summary>
public sealed record HealthStatus(string Status, DateTimeOffset CheckedAt);

/// <summary>GET /v1/client-config response (SLICE_S1_CONTRACT.md §1c), sourced from i18n/locales.json at boot.</summary>
public sealed record ClientConfigResponse(string ApiVersion, IReadOnlyList<string> Locales, string DefaultLocale);

/// <summary>Pagination shape pinned early (SLICE_S1_CONTRACT.md §1c) so no later slice invents a second cursor shape.</summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);
