namespace Svac.Identity.Persistence;

/// <summary>
/// identity.accounts (SLICE_S3_CONTRACT.md §2). Email-verified-FIRST flow: <see cref="EmailVerifiedAt"/>
/// is NOT NULL — no account row exists before the email is proven owned. No age/year column exists;
/// <see cref="BirthdateEnc"/> is the ONLY place a birthdate ever lands, and age derives on read via
/// AgeMath, never a stored value.
/// </summary>
public sealed class AccountEntity
{
    public required string AccountId { get; set; } // usr_ ULID
    public required string Handle { get; set; } // canonical NFKC-folded lowercase (HandleRules)
    public string? Email { get; set; } // plaintext by ruling (§12 item 6); NULLed at tombstone
    public DateTimeOffset EmailVerifiedAt { get; set; }
    public required byte[] BirthdateEnc { get; set; } // IFieldEncryptor purpose 'birthdate'
    public DateTimeOffset AttestedAdultAt { get; set; }
    public required string TermsVersion { get; set; }
    public required string FandomTag { get; set; } // free text at S3
    public string? AvatarRef { get; set; } // NULL until S10/S11 (dark ruling, §0)
    public required string Locale { get; set; }
    public string AccountState { get; set; } = "active"; // CHECK active|suspended|banned|deleted
    public string IrlAccessState { get; set; } = "active"; // CHECK active|suspended; writer-less at S3
    public DateTimeOffset StateChangedAt { get; set; }
    public DateTimeOffset? DeletionRequestedAt { get; set; }
    public DateTimeOffset? DeletionEffectiveAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActiveAt { get; set; } // write-throttled >=1h delta; never in a response DTO
    public DateTimeOffset? TombstonedAt { get; set; } // set only by the deletion pipeline (Pass 2)
    public required string Region { get; set; }
    public required string RegionSource { get; set; }
    public required string LawfulBasis { get; set; }
}

/// <summary>
/// identity.email_challenges (SLICE_S3_CONTRACT.md §2). ONE machine: signup | login | email_change.
/// NO birthdate column BY DESIGN (minor no-store posture, §1g) — there is no table a refused minor's
/// birthdate could land in.
/// </summary>
public sealed class EmailChallengeEntity
{
    public required string ChallengeId { get; set; } // chl_ ULID
    public required string Purpose { get; set; } // CHECK signup|login|email_change
    public required string EmailLower { get; set; }
    public string? AccountId { get; set; } // login/email_change carry it from issuance; signup rows get it STAMPED at consumption (PII-5, SECURITY_REVIEW_S3.md) so the purge's account_id branch always reaches them
    public required byte[] CodeHash { get; set; } // HMAC(IFieldKeyVault named secret, code)
    public int Attempts { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; } // signup confirm step
    public byte[]? VerifiedTokenHash { get; set; } // single-use complete ticket, hash only
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string Locale { get; set; }
    public required string Region { get; set; }
    public required string LawfulBasis { get; set; }
}

/// <summary>identity.sessions (SLICE_S3_CONTRACT.md §2). Opaque, server-side, DB-backed, revocable.</summary>
public sealed class SessionEntity
{
    public required string SessionId { get; set; } // ses_ ULID
    public required string AccountId { get; set; }
    public string? DeviceId { get; set; }
    public required byte[] AccessTokenHash { get; set; } // SHA-256; plaintext never stored
    public required string RefreshFamilyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; } // write-coalesced >=60s
    public DateTimeOffset AccessExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; } // logout|user_revoked|rotation_reuse|state_cascade|expired|cap_evicted
    public required string Region { get; set; }
    public required string LawfulBasis { get; set; }
}

/// <summary>identity.refresh_tokens (SLICE_S3_CONTRACT.md §2). Single-use, family-linked; reuse detection is the theft alarm.</summary>
public sealed class RefreshTokenEntity
{
    public required string Id { get; set; }
    public required string SessionId { get; set; }
    public required byte[] TokenHash { get; set; }
    public required string FamilyId { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public string? SupersededBy { get; set; } // reuse detection: consumed presented again -> revoke family
    public required string Region { get; set; }
    public required string LawfulBasis { get; set; }
}

/// <summary>identity.devices (SLICE_S3_CONTRACT.md §2). Server-minted device id; push-token STORE only, delivery is S4.</summary>
public sealed class DeviceEntity
{
    public required string DeviceId { get; set; } // dev_ ULID, server-minted
    public required string AccountId { get; set; }
    public required string Platform { get; set; } // CHECK ios|android|web
    public string? PushToken { get; set; }
    public DateTimeOffset? PushTokenUpdatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public required string Region { get; set; }
    public required string LawfulBasis { get; set; }
}

/// <summary>identity.push_category_consents (SLICE_S3_CONTRACT.md §2). PROJECTION of events_consent; rebuildable. Category 8 UNREPRESENTABLE, not just immutable.</summary>
public sealed class PushCategoryConsentEntity
{
    public required string AccountId { get; set; }
    public short Category { get; set; } // CHECK BETWEEN 1 AND 9 AND <> 8
    public bool Enabled { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public required string Region { get; set; }
    public required string LawfulBasis { get; set; }
}

/// <summary>identity.consent_current (SLICE_S3_CONTRACT.md §2). Rebuildable projection over events_consent.</summary>
public sealed class ConsentCurrentEntity
{
    public required string AccountId { get; set; }
    public required string ConsentKind { get; set; }
    public required string Version { get; set; }
    public required string Status { get; set; } // CHECK granted|revoked
    public required string Surface { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
    public required string Region { get; set; }
    public required string LawfulBasis { get; set; }
}

/// <summary>identity.handle_history (SLICE_S3_CONTRACT.md §2). Moderation trail + cooldown source. No consumer read path in the contract assembly (moderation-visible only, structural).</summary>
public sealed class HandleHistoryEntity
{
    public required string Id { get; set; }
    public required string AccountId { get; set; }
    public required string OldHandle { get; set; }
    public required string NewHandle { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public required string Region { get; set; }
    public required string LawfulBasis { get; set; }
}

/// <summary>identity.reserved_handles (SLICE_S3_CONTRACT.md §2). Non-PII; seeded from a checked-in manifest.</summary>
public sealed class ReservedHandleEntity
{
    public required string Handle { get; set; }
    public required string Reason { get; set; }
}

/// <summary>identity.retired_handles (SLICE_S3_CONTRACT.md §2, OQ-2). Subject-severed at write — pseudonymous by construction.</summary>
public sealed class RetiredHandleEntity
{
    public required string Handle { get; set; }
    public DateTimeOffset RetiredAt { get; set; }
}

/// <summary>
/// identity.export_jobs (SLICE_S3_CONTRACT.md §2). Schema created at Pass 1 per the contract; the export
/// PIPELINE (worker, IExportContributor wiring, artifact population) is Pass 2 — no code in this build
/// writes a row into this table.
/// </summary>
public sealed class ExportJobEntity
{
    public required string ExportId { get; set; } // exp_ ULID
    public required string AccountId { get; set; }
    public required string State { get; set; } // CHECK pending|ready|delivered|expired|failed
    public byte[]? Artifact { get; set; }
    public string? ManifestJson { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public required string Region { get; set; }
    public required string LawfulBasis { get; set; }
}

/// <summary>
/// identity.deletion_jobs (SLICE_S3_CONTRACT.md §2). Schema created at Pass 1 per the contract; the
/// deletion PIPELINE (worker, purge orchestration) is Pass 2 — no code in this build writes a row into
/// this table.
/// </summary>
public sealed class DeletionJobEntity
{
    public required string DeletionId { get; set; } // del_ ULID
    public required string AccountId { get; set; } // pseudonymized post-run
    public required string State { get; set; } // CHECK scheduled|canceled|executing|held|complete
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset ScheduledFor { get; set; }
    public DateTimeOffset? ExecutingSince { get; set; } // CONC-2 lease (SECURITY_REVIEW_S3.md): stamped by the guarded CAS claim; a job stuck 'executing' past the lease window is re-swept as retryable
    public bool ExportOffered { get; set; } = true;
    public int? CustodyHoldsFound { get; set; }
    public string? CustodyHoldRefsJson { get; set; }
    public DateTimeOffset? ExecutedAt { get; set; }
    public string? PurgeRunIdsJson { get; set; }
    public required string Region { get; set; }
    public required string LawfulBasis { get; set; }
}

/// <summary>
/// identity.ban_evasion_refs (SLICE_S3_CONTRACT.md §11 OQ-3, RATIFIED §13: retain a salted-HMAC email ref
/// (+ push-token hash) so a banned account's deletion cannot be undone by delete-then-re-signup).
/// <see cref="HmacEmail"/> IS the row's key — a keyed HMAC of the lowercased email (never the raw
/// address), computed via IFieldKeyVault's named-secret door (MinorProt-F4 precedent: a keyed re-key,
/// never an unsalted hash). Schema created at Pass 1 per the contract's item 1; this store's ONLY writer
/// is the deletion pipeline's ban-evasion path (Pass 2 — a banned account's deletion does not exist as a
/// reachable flow until the deletion worker lands), so this table is intentionally unpopulated and
/// unconsulted at Pass 1. lawful_basis is always 'legitimate_interest' by the OQ-3 ruling.
/// </summary>
public sealed class BanEvasionRefEntity
{
    public required string HmacEmail { get; set; }
    public byte[]? PushTokenHash { get; set; }
    public DateTimeOffset BannedAt { get; set; }
    public required string Region { get; set; }
    public required string LawfulBasis { get; set; }
}
