using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Svac.AdminHost.Domain.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAdmin : Migration
    {
        // SLICE_S5_CONTRACT.md §2: "DELETE revoked from the app role on both tables (S1 pattern)." NOT
        // IMPLEMENTED — verified rather than assumed to be a real precedent, and it is not one: no S1/S3
        // migration (Svac.DomainCore/Svac.Identity) contains any REVOKE/GRANT statement, and
        // docker-compose.yml provisions exactly ONE Postgres role ("svac") that both applies every
        // migration AND is every host's runtime connection role. Postgres object OWNERS always retain
        // every privilege on objects they own regardless of REVOKE (GRANT/REVOKE never binds the owner) —
        // a `REVOKE DELETE ON admin.staff_accounts FROM svac` here would be a structural no-op (the
        // migration runs AS svac, which owns the table it just created), i.e. decorative security theater,
        // not a real control. A genuine implementation needs a SECOND, least-privileged Postgres role
        // (no DELETE grant) that every host's RUNTIME connection uses while migrations keep running under
        // the owning role — a cross-cutting change to docker-compose.yml + every host's connection-string
        // wiring + infra/modules/postgres-flexible.bicep, affecting schemas `core`/`identity` too, not an
        // admin-only concern. Flagged honestly as an open gap (reported at scaffold sign-off) rather than
        // shipping a REVOKE that reads as a real guard but is not one.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "admin");

            migrationBuilder.CreateTable(
                name: "staff_accounts",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    external_subject = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    security_stamp = table.Column<string>(type: "text", nullable: false),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_accounts", x => x.id);
                    table.CheckConstraint("ck_staff_accounts_status", "status IN ('active','deactivated')");
                });

            migrationBuilder.CreateTable(
                name: "staff_role_grants",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    staff_id = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    granted_by = table.Column<string>(type: "text", nullable: false),
                    grant_reason = table.Column<string>(type: "text", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<string>(type: "text", nullable: true),
                    revoke_reason = table.Column<string>(type: "text", nullable: true),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_role_grants", x => x.id);
                    table.CheckConstraint("ck_staff_role_grants_role", "role IN ('super_admin','safety_agent','content_moderator','venue_con_ops','economy_ops','analyst')");
                    table.ForeignKey(
                        name: "FK_staff_role_grants_staff_accounts_staff_id",
                        column: x => x.staff_id,
                        principalSchema: "admin",
                        principalTable: "staff_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_staff_accounts_email",
                schema: "admin",
                table: "staff_accounts",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_staff_accounts_external_subject",
                schema: "admin",
                table: "staff_accounts",
                column: "external_subject",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_active_grant",
                schema: "admin",
                table: "staff_role_grants",
                columns: new[] { "staff_id", "role" },
                unique: true,
                filter: "revoked_at IS NULL");
        }

        // -- destructive: Down() only, never invoked by the startup migration service (build/scripts/
        // destructive-verb-check.mjs marker — reversible drop of the admin schema's two tables).
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "staff_role_grants",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "staff_accounts",
                schema: "admin");
        }
    }
}
