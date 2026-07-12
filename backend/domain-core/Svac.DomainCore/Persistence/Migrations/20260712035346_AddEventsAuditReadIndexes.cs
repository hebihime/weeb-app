using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Svac.DomainCore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventsAuditReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_events_audit_actor_ref_recorded_at",
                schema: "core",
                table: "events_audit",
                columns: new[] { "actor_ref", "recorded_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_events_audit_event_type_recorded_at",
                schema: "core",
                table: "events_audit",
                columns: new[] { "event_type", "recorded_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_events_audit_actor_ref_recorded_at",
                schema: "core",
                table: "events_audit");

            migrationBuilder.DropIndex(
                name: "IX_events_audit_event_type_recorded_at",
                schema: "core",
                table: "events_audit");
        }
    }
}
