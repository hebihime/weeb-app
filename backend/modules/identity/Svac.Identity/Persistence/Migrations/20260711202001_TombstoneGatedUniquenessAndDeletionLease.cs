using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Svac.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TombstoneGatedUniquenessAndDeletionLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_accounts_email",
                schema: "identity",
                table: "accounts");

            migrationBuilder.DropIndex(
                name: "ux_accounts_handle",
                schema: "identity",
                table: "accounts");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "executing_since",
                schema: "identity",
                table: "deletion_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_accounts_email",
                schema: "identity",
                table: "accounts",
                column: "email",
                unique: true,
                filter: "tombstoned_at IS NULL AND email IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_accounts_handle",
                schema: "identity",
                table: "accounts",
                column: "handle",
                unique: true,
                filter: "tombstoned_at IS NULL");
        }

        // -- destructive: Down() rolls back executing_since (DropColumn below) — the FORWARD (Up)
        // migration only ADDS this column; DropColumn is reachable only via an explicit `ef database
        // update <prior-migration>` rollback, never a normal forward deploy.
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_accounts_email",
                schema: "identity",
                table: "accounts");

            migrationBuilder.DropIndex(
                name: "ux_accounts_handle",
                schema: "identity",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "executing_since",
                schema: "identity",
                table: "deletion_jobs");

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
        }
    }
}
