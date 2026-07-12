namespace Svac.Identity.Contracts;

/// <summary>
/// <c>GET /v1/me/export/{exportId}</c> response shape (SLICE_S3_CONTRACT.md §1c). <see cref="State"/> is
/// one of <c>"pending"|"ready"|"delivered"|"expired"|"failed"</c> (mirrors <c>identity.export_jobs</c>'s
/// DDL CHECK, §2) — a string here rather than an enum so the wire shape matches the DDL CHECK verbatim
/// with nothing to keep in sync (the S3 BUILD implementation is free to model this as an internal enum
/// and serialize it down to these exact strings). <see cref="ExpiresAt"/> is set once the job reaches
/// "ready".
/// </summary>
public sealed record ExportStatus(string State, DateTimeOffset? ExpiresAt);
