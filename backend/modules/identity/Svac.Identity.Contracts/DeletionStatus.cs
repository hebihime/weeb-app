namespace Svac.Identity.Contracts;

/// <summary>
/// <c>GET /v1/me/deletion</c> response shape (SLICE_S3_CONTRACT.md §1c) — "the design/06 'deletion
/// scheduled' surface." <see cref="State"/> is one of
/// <c>"scheduled"|"canceled"|"executing"|"held"|"complete"</c> (mirrors <c>identity.deletion_jobs</c>'s
/// DDL CHECK, §2). <see cref="ScheduledFor"/> is the grace-window <c>effective_at</c>.
/// </summary>
public sealed record DeletionStatus(string State, DateTimeOffset? ScheduledFor);
