namespace Svac.AdminHost.Domain.Persistence;

/// <summary>
/// admin.staff_accounts (SLICE_S5_CONTRACT.md §2) — the schema's ONLY identity row for a staff member.
/// Provisioning/deactivation/reactivation are Phase 2 (AdminActionExecutor); this scaffold ships the
/// mapped shape only, so the migration + the purge-registry completeness gate are real today.
/// </summary>
public sealed class StaffAccountEntity
{
    public required string Id { get; set; } // stf_ ULID (IdPrefixes.Staff, domain-core, already exists)
    public required string ExternalSubject { get; set; } // Entra oid; dev fixtures use 'devseams:*'
    public required string Email { get; set; } // staff PII
    public required string DisplayName { get; set; } // staff PII
    public required string Status { get; set; } // 'active' | 'deactivated'
    public required string SecurityStamp { get; set; } // bumped on deactivate/grant/revoke
    public required string Region { get; set; } // L21: staff are data subjects too
    public required string LawfulBasis { get; set; } // 'contract' via the S1 resolver
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
}

/// <summary>
/// admin.staff_role_grants (SLICE_S5_CONTRACT.md §2) — a role grant is a state transition (granted ->
/// revoked), NEVER a DELETE; the audit chain must always resolve stf_/srg_ ids. Grant/revoke business
/// logic (AdminActionExecutor) is Phase 2 — this scaffold ships the mapped shape + the partial unique
/// index that is the check-then-act double-grant guard.
/// </summary>
public sealed class StaffRoleGrantEntity
{
    public required string Id { get; set; } // srg_ ULID (IdPrefixes.StaffRoleGrant, domain-core, already exists)
    public required string StaffId { get; set; } // FK -> admin.staff_accounts.id
    public required string Role { get; set; } // one of the six StaffRole enum members, snake_case on the wire
    public required string GrantedBy { get; set; } // stf_ or sys_ (bootstrap only)
    public required string GrantReason { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; } // NULL = active grant; the ux_active_grant partial index keys off this
    public string? RevokedBy { get; set; }
    public string? RevokeReason { get; set; }
    public required string Region { get; set; }
    public required string LawfulBasis { get; set; }
}
