using Microsoft.EntityFrameworkCore;

namespace Svac.AdminHost.Domain.Persistence;

/// <summary>
/// The schema `admin` DbContext (SLICE_S5_CONTRACT.md §2), owned SOLELY by this type — mirrors
/// Svac.DomainCore.Persistence.CoreDbContext / Svac.Identity.Persistence.IdentityDbContext exactly.
/// EXACTLY two tables, per the contract's own "that is the entire schema" line: no sessions, no
/// search-audit table, no audit-view table, no config mirror, no dashboard cache — every other admin
/// read is a contract read (IConfigRegistry/IAuditReader/IPurgeRunReader), never a second EF mapping.
/// </summary>
public sealed class AdminDbContext(DbContextOptions<AdminDbContext> options) : DbContext(options)
{
    public DbSet<StaffAccountEntity> StaffAccounts => Set<StaffAccountEntity>();
    public DbSet<StaffRoleGrantEntity> StaffRoleGrants => Set<StaffRoleGrantEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("admin");

        modelBuilder.Entity<StaffAccountEntity>(b =>
        {
            b.ToTable("staff_accounts", "admin", t =>
                t.HasCheckConstraint("ck_staff_accounts_status", "status IN ('active','deactivated')"));
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.ExternalSubject).HasColumnName("external_subject");
            b.Property(e => e.Email).HasColumnName("email");
            b.Property(e => e.DisplayName).HasColumnName("display_name");
            b.Property(e => e.Status).HasColumnName("status");
            b.Property(e => e.SecurityStamp).HasColumnName("security_stamp");
            b.Property(e => e.Region).HasColumnName("region");
            b.Property(e => e.LawfulBasis).HasColumnName("lawful_basis");
            b.Property(e => e.CreatedAt).HasColumnName("created_at");
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            b.Property(e => e.DeactivatedAt).HasColumnName("deactivated_at");

            b.HasIndex(e => e.ExternalSubject).IsUnique().HasDatabaseName("ux_staff_accounts_external_subject");
            b.HasIndex(e => e.Email).IsUnique().HasDatabaseName("ux_staff_accounts_email");
        });

        modelBuilder.Entity<StaffRoleGrantEntity>(b =>
        {
            b.ToTable("staff_role_grants", "admin", t =>
                t.HasCheckConstraint(
                    "ck_staff_role_grants_role",
                    "role IN ('super_admin','safety_agent','content_moderator','venue_con_ops','economy_ops','analyst')"));
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.StaffId).HasColumnName("staff_id");
            b.Property(e => e.Role).HasColumnName("role");
            b.Property(e => e.GrantedBy).HasColumnName("granted_by");
            b.Property(e => e.GrantReason).HasColumnName("grant_reason");
            b.Property(e => e.GrantedAt).HasColumnName("granted_at");
            b.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            b.Property(e => e.RevokedBy).HasColumnName("revoked_by");
            b.Property(e => e.RevokeReason).HasColumnName("revoke_reason");
            b.Property(e => e.Region).HasColumnName("region");
            b.Property(e => e.LawfulBasis).HasColumnName("lawful_basis");

            b.HasOne<StaffAccountEntity>().WithMany().HasForeignKey(e => e.StaffId).HasPrincipalKey(e => e.Id).OnDelete(DeleteBehavior.Restrict);

            // §2: "the check-then-act guard on double-grants (catch violation -> re-read winner,
            // idempotent-under-race tested)" — the guard itself is real DDL now; the catch/re-read
            // caller (AdminActionExecutor.ProvisionGrant) is Phase 2.
            b.HasIndex(e => new { e.StaffId, e.Role }).IsUnique().HasFilter("revoked_at IS NULL").HasDatabaseName("ux_active_grant");
        });
    }
}
