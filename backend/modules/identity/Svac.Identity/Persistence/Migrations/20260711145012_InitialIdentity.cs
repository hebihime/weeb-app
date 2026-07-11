using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Svac.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "identity",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "text", nullable: false),
                    handle = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: true),
                    email_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    birthdate_enc = table.Column<byte[]>(type: "bytea", nullable: false),
                    attested_adult_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    terms_version = table.Column<string>(type: "text", nullable: false),
                    fandom_tag = table.Column<string>(type: "text", nullable: false),
                    avatar_ref = table.Column<string>(type: "text", nullable: true),
                    locale = table.Column<string>(type: "text", nullable: false),
                    account_state = table.Column<string>(type: "text", nullable: false, defaultValue: "active"),
                    irl_access_state = table.Column<string>(type: "text", nullable: false, defaultValue: "active"),
                    state_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deletion_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deletion_effective_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_active_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tombstoned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    region = table.Column<string>(type: "text", nullable: false),
                    region_source = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.account_id);
                    table.CheckConstraint("ck_accounts_state", "account_state IN ('active','suspended','banned','deleted')");
                });

            migrationBuilder.CreateTable(
                name: "ban_evasion_refs",
                schema: "identity",
                columns: table => new
                {
                    hmac_email = table.Column<string>(type: "text", nullable: false),
                    push_token_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    banned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ban_evasion_refs", x => x.hmac_email);
                });

            migrationBuilder.CreateTable(
                name: "consent_current",
                schema: "identity",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "text", nullable: false),
                    consent_kind = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    surface = table.Column<string>(type: "text", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consent_current", x => new { x.account_id, x.consent_kind });
                    table.CheckConstraint("ck_consent_current_status", "status IN ('granted','revoked')");
                });

            migrationBuilder.CreateTable(
                name: "deletion_jobs",
                schema: "identity",
                columns: table => new
                {
                    deletion_id = table.Column<string>(type: "text", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    scheduled_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    export_offered = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    custody_holds_found = table.Column<int>(type: "integer", nullable: true),
                    custody_hold_refs = table.Column<string>(type: "jsonb", nullable: true),
                    executed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    purge_run_ids = table.Column<string>(type: "jsonb", nullable: true),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deletion_jobs", x => x.deletion_id);
                    table.CheckConstraint("ck_deletion_jobs_state", "state IN ('scheduled','canceled','executing','held','complete')");
                });

            migrationBuilder.CreateTable(
                name: "devices",
                schema: "identity",
                columns: table => new
                {
                    device_id = table.Column<string>(type: "text", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    platform = table.Column<string>(type: "text", nullable: false),
                    push_token = table.Column<string>(type: "text", nullable: true),
                    push_token_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devices", x => x.device_id);
                    table.CheckConstraint("ck_devices_platform", "platform IN ('ios','android','web')");
                });

            migrationBuilder.CreateTable(
                name: "email_challenges",
                schema: "identity",
                columns: table => new
                {
                    challenge_id = table.Column<string>(type: "text", nullable: false),
                    purpose = table.Column<string>(type: "text", nullable: false),
                    email_lower = table.Column<string>(type: "text", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: true),
                    code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verified_token_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    locale = table.Column<string>(type: "text", nullable: false),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_challenges", x => x.challenge_id);
                    table.CheckConstraint("ck_email_challenges_purpose", "purpose IN ('signup','login','email_change')");
                });

            migrationBuilder.CreateTable(
                name: "export_jobs",
                schema: "identity",
                columns: table => new
                {
                    export_id = table.Column<string>(type: "text", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    artifact = table.Column<byte[]>(type: "bytea", nullable: true),
                    manifest = table.Column<string>(type: "jsonb", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ready_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_jobs", x => x.export_id);
                    table.CheckConstraint("ck_export_jobs_state", "state IN ('pending','ready','delivered','expired','failed')");
                });

            migrationBuilder.CreateTable(
                name: "handle_history",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    old_handle = table.Column<string>(type: "text", nullable: false),
                    new_handle = table.Column<string>(type: "text", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_handle_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "push_category_consents",
                schema: "identity",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<short>(type: "smallint", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_category_consents", x => new { x.account_id, x.category });
                    table.CheckConstraint("ck_push_category_consents_category", "category BETWEEN 1 AND 9 AND category <> 8");
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<string>(type: "text", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    family_id = table.Column<string>(type: "text", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_by = table.Column<string>(type: "text", nullable: true),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reserved_handles",
                schema: "identity",
                columns: table => new
                {
                    handle = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reserved_handles", x => x.handle);
                });

            migrationBuilder.CreateTable(
                name: "retired_handles",
                schema: "identity",
                columns: table => new
                {
                    handle = table.Column<string>(type: "text", nullable: false),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retired_handles", x => x.handle);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "identity",
                columns: table => new
                {
                    session_id = table.Column<string>(type: "text", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false),
                    device_id = table.Column<string>(type: "text", nullable: true),
                    access_token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    refresh_family_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    access_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoke_reason = table.Column<string>(type: "text", nullable: true),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.session_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_deletion_due",
                schema: "identity",
                table: "accounts",
                column: "deletion_effective_at",
                filter: "deletion_effective_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_accounts_email",
                schema: "identity",
                table: "accounts",
                column: "email",
                unique: true,
                filter: "account_state <> 'deleted' AND email IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_accounts_handle",
                schema: "identity",
                table: "accounts",
                column: "handle",
                unique: true,
                filter: "account_state <> 'deleted'");

            migrationBuilder.CreateIndex(
                name: "ix_deletion_jobs_account",
                schema: "identity",
                table: "deletion_jobs",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_devices_account",
                schema: "identity",
                table: "devices",
                column: "account_id",
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_email_challenges_account",
                schema: "identity",
                table: "email_challenges",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_challenges_email_lower",
                schema: "identity",
                table: "email_challenges",
                column: "email_lower");

            migrationBuilder.CreateIndex(
                name: "ux_export_active",
                schema: "identity",
                table: "export_jobs",
                column: "account_id",
                unique: true,
                filter: "state IN ('pending','ready')");

            migrationBuilder.CreateIndex(
                name: "ix_handle_history_account",
                schema: "identity",
                table: "handle_history",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_family",
                schema: "identity",
                table: "refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ux_refresh_tokens_token_hash",
                schema: "identity",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_account",
                schema: "identity",
                table: "sessions",
                column: "account_id",
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_sessions_access_token_hash",
                schema: "identity",
                table: "sessions",
                column: "access_token_hash",
                unique: true);
        }

        /// <inheritdoc />
        // -- destructive: every DropTable below is the reversible Down() half of this migration only —
        // never invoked by IdentityMigrationHostedService (which only ever calls MigrateAsync forward);
        // mirrors Svac.DomainCore's own InitialCore migration's identical marker/reasoning.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounts",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "ban_evasion_refs",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "consent_current",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "deletion_jobs",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "devices",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "email_challenges",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "export_jobs",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "handle_history",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "push_category_consents",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "reserved_handles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "retired_handles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "identity");
        }
    }
}
