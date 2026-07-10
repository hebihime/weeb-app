using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Svac.DomainCore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalSeqAndWidenPseudonymizeTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "global_seq",
                schema: "core",
                table: "events_reputation",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<long>(
                name: "global_seq",
                schema: "core",
                table: "events_ledger",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<long>(
                name: "global_seq",
                schema: "core",
                table: "events_heatmap_provenance",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<long>(
                name: "global_seq",
                schema: "core",
                table: "events_consent",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<long>(
                name: "global_seq",
                schema: "core",
                table: "events_behavioral",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<long>(
                name: "global_seq",
                schema: "core",
                table: "events_audit",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.CreateIndex(
                name: "IX_events_reputation_global_seq",
                schema: "core",
                table: "events_reputation",
                column: "global_seq");

            migrationBuilder.CreateIndex(
                name: "IX_events_ledger_global_seq",
                schema: "core",
                table: "events_ledger",
                column: "global_seq");

            migrationBuilder.CreateIndex(
                name: "IX_events_heatmap_provenance_global_seq",
                schema: "core",
                table: "events_heatmap_provenance",
                column: "global_seq");

            migrationBuilder.CreateIndex(
                name: "IX_events_consent_global_seq",
                schema: "core",
                table: "events_consent",
                column: "global_seq");

            migrationBuilder.CreateIndex(
                name: "IX_events_behavioral_global_seq",
                schema: "core",
                table: "events_behavioral",
                column: "global_seq");

            migrationBuilder.CreateIndex(
                name: "IX_events_audit_global_seq",
                schema: "core",
                table: "events_audit",
                column: "global_seq");

            // --- SECURITY_REVIEW_S1.md Purge-F2 = MinorProt-F3 (subject re-key) / PII-F3 (OQ-1 basis
            // override): widens the pseudonymize transition core.enforce_append_only() permits. The
            // InitialCore version pinned stream_id and lawful_basis to OLD in every UPDATE except the
            // tombstone transition, which structurally made "pseudonymize subject (irreversible re-key)"
            // impossible — the DDL-designated "subject scope" column (stream_id, §2) could never actually
            // be re-keyed, and the OQ-1-mandated survivor basis ('legal_obligation') could never be
            // written. Re-creating the SAME function (the six existing triggers already point at this
            // name, so they need no changes) with a widened pseudonymize branch: actor_ref, stream_id,
            // and/or lawful_basis may now change together; every other column (including the new
            // global_seq) is still pinned to OLD, exhaustively, exactly as before.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION core.enforce_append_only() RETURNS trigger AS $$
                BEGIN
                    IF (TG_OP = 'DELETE') THEN
                        IF (current_setting('core.purge_delete_authorized', true) IS DISTINCT FROM 'on') THEN
                            RAISE EXCEPTION 'core.%: DELETE is never permitted on an append-only table outside the purge-pipeline session-local carve-out (SLICE_S1_CONTRACT.md §2/§6)', TG_TABLE_NAME;
                        END IF;
                        -- authorized: the ONE caller (PurgePipeline) sets this session-local GUC via
                        -- SET LOCAL, inside its own transaction, immediately before this statement —
                        -- never reachable from any application/consumer write path.
                    ELSIF (TG_OP = 'UPDATE') THEN
                        IF (OLD.tombstone = true) THEN
                            RAISE EXCEPTION 'core.%: row % is already tombstoned — no further mutation is sanctioned', TG_TABLE_NAME, OLD.event_id;
                        ELSIF (NEW.tombstone = true AND NEW.payload IS NULL
                               AND NEW.actor_ref IS NOT DISTINCT FROM OLD.actor_ref
                               AND NEW.stream_id IS NOT DISTINCT FROM OLD.stream_id
                               AND NEW.lawful_basis IS NOT DISTINCT FROM OLD.lawful_basis) THEN
                            NULL; -- the tombstone transition: permitted.
                        ELSIF (NEW.tombstone IS DISTINCT FROM true
                               AND NEW.payload IS NOT DISTINCT FROM OLD.payload
                               AND (NEW.actor_ref IS DISTINCT FROM OLD.actor_ref
                                    OR NEW.stream_id IS DISTINCT FROM OLD.stream_id
                                    OR NEW.lawful_basis IS DISTINCT FROM OLD.lawful_basis)
                               AND NEW.event_id IS NOT DISTINCT FROM OLD.event_id
                               AND NEW.seq IS NOT DISTINCT FROM OLD.seq
                               AND NEW.event_type IS NOT DISTINCT FROM OLD.event_type
                               AND NEW.reversal_of IS NOT DISTINCT FROM OLD.reversal_of
                               AND NEW.region IS NOT DISTINCT FROM OLD.region
                               AND NEW.occurred_at IS NOT DISTINCT FROM OLD.occurred_at
                               AND NEW.recorded_at IS NOT DISTINCT FROM OLD.recorded_at
                               AND NEW.global_seq IS NOT DISTINCT FROM OLD.global_seq) THEN
                            NULL; -- the pseudonymize transition (WIDENED): actor_ref, stream_id, and/or
                                   -- lawful_basis may change together; every other column pinned to OLD.
                        ELSE
                            RAISE EXCEPTION 'core.%: UPDATE permits only the (tombstone=false->true, payload->NULL) transition or the (actor_ref/stream_id/lawful_basis re-key) pseudonymize transition, no other column change (SLICE_S1_CONTRACT.md §2/§6)', TG_TABLE_NAME;
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse the hand-appended trigger widening first, mirroring InitialCore.Down()'s own
            // convention (raw SQL reversed before the columns it referenced are dropped) — restores the
            // narrower, pre-fix pseudonymize transition. Never invoked by the startup migration service
            // (BUILD.md §8); kept correct for the lint's sake, same as InitialCore.Down().
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION core.enforce_append_only() RETURNS trigger AS $$
                BEGIN
                    IF (TG_OP = 'DELETE') THEN
                        IF (current_setting('core.purge_delete_authorized', true) IS DISTINCT FROM 'on') THEN
                            RAISE EXCEPTION 'core.%: DELETE is never permitted on an append-only table outside the purge-pipeline session-local carve-out (SLICE_S1_CONTRACT.md §2/§6)', TG_TABLE_NAME;
                        END IF;
                    ELSIF (TG_OP = 'UPDATE') THEN
                        IF (OLD.tombstone = true) THEN
                            RAISE EXCEPTION 'core.%: row % is already tombstoned — no further mutation is sanctioned', TG_TABLE_NAME, OLD.event_id;
                        ELSIF (NEW.tombstone = true AND NEW.payload IS NULL
                               AND NEW.actor_ref IS NOT DISTINCT FROM OLD.actor_ref) THEN
                            NULL;
                        ELSIF (NEW.tombstone IS DISTINCT FROM true
                               AND NEW.payload IS NOT DISTINCT FROM OLD.payload
                               AND NEW.actor_ref IS DISTINCT FROM OLD.actor_ref
                               AND NEW.event_id IS NOT DISTINCT FROM OLD.event_id
                               AND NEW.stream_id IS NOT DISTINCT FROM OLD.stream_id
                               AND NEW.seq IS NOT DISTINCT FROM OLD.seq
                               AND NEW.event_type IS NOT DISTINCT FROM OLD.event_type
                               AND NEW.reversal_of IS NOT DISTINCT FROM OLD.reversal_of
                               AND NEW.region IS NOT DISTINCT FROM OLD.region
                               AND NEW.lawful_basis IS NOT DISTINCT FROM OLD.lawful_basis
                               AND NEW.occurred_at IS NOT DISTINCT FROM OLD.occurred_at
                               AND NEW.recorded_at IS NOT DISTINCT FROM OLD.recorded_at) THEN
                            NULL;
                        ELSE
                            RAISE EXCEPTION 'core.%: UPDATE permits only the (tombstone=false->true, payload->NULL) transition or the (actor_ref re-key) pseudonymize transition, no other column change (SLICE_S1_CONTRACT.md §2/§6)', TG_TABLE_NAME;
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            // -- destructive: reversible Down() only, never invoked by the startup migration service
            // (BUILD.md §8) — the DropColumn calls below undo THIS migration's own AddColumn, same
            // destructive-verb-check.mjs marker convention InitialCore.Down() uses for its raw SQL DROPs.
            migrationBuilder.DropIndex(
                name: "IX_events_reputation_global_seq",
                schema: "core",
                table: "events_reputation");

            migrationBuilder.DropIndex(
                name: "IX_events_ledger_global_seq",
                schema: "core",
                table: "events_ledger");

            migrationBuilder.DropIndex(
                name: "IX_events_heatmap_provenance_global_seq",
                schema: "core",
                table: "events_heatmap_provenance");

            migrationBuilder.DropIndex(
                name: "IX_events_consent_global_seq",
                schema: "core",
                table: "events_consent");

            migrationBuilder.DropIndex(
                name: "IX_events_behavioral_global_seq",
                schema: "core",
                table: "events_behavioral");

            migrationBuilder.DropIndex(
                name: "IX_events_audit_global_seq",
                schema: "core",
                table: "events_audit");

            migrationBuilder.DropColumn(
                name: "global_seq",
                schema: "core",
                table: "events_reputation");

            migrationBuilder.DropColumn(
                name: "global_seq",
                schema: "core",
                table: "events_ledger");

            migrationBuilder.DropColumn(
                name: "global_seq",
                schema: "core",
                table: "events_heatmap_provenance");

            migrationBuilder.DropColumn(
                name: "global_seq",
                schema: "core",
                table: "events_consent");

            migrationBuilder.DropColumn(
                name: "global_seq",
                schema: "core",
                table: "events_behavioral");

            migrationBuilder.DropColumn(
                name: "global_seq",
                schema: "core",
                table: "events_audit");
        }
    }
}
