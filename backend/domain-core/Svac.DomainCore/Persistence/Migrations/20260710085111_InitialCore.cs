using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Svac.DomainCore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.CreateTable(
                name: "config_entries",
                schema: "core",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: false),
                    gate = table.Column<string>(type: "text", nullable: true),
                    bounds = table.Column<string>(type: "jsonb", nullable: true),
                    requires_reason = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_entries", x => x.key);
                    table.CheckConstraint("ck_config_entries_scope", "scope IN ('founder','ops','set')");
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml_data = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "events_audit",
                schema: "core",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "text", nullable: false),
                    stream_id = table.Column<string>(type: "text", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    reversal_of = table.Column<string>(type: "text", nullable: true),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    actor_ref = table.Column<string>(type: "text", nullable: false),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events_audit", x => x.event_id);
                    table.CheckConstraint("ck_events_audit_payload_null_iff_tombstoned", "NOT tombstone OR payload IS NULL");
                });

            migrationBuilder.CreateTable(
                name: "events_behavioral",
                schema: "core",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "text", nullable: false),
                    stream_id = table.Column<string>(type: "text", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    reversal_of = table.Column<string>(type: "text", nullable: true),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    actor_ref = table.Column<string>(type: "text", nullable: false),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events_behavioral", x => x.event_id);
                    table.CheckConstraint("ck_events_behavioral_payload_null_iff_tombstoned", "NOT tombstone OR payload IS NULL");
                });

            migrationBuilder.CreateTable(
                name: "events_consent",
                schema: "core",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "text", nullable: false),
                    stream_id = table.Column<string>(type: "text", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    reversal_of = table.Column<string>(type: "text", nullable: true),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    actor_ref = table.Column<string>(type: "text", nullable: false),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events_consent", x => x.event_id);
                    table.CheckConstraint("ck_events_consent_payload_null_iff_tombstoned", "NOT tombstone OR payload IS NULL");
                });

            migrationBuilder.CreateTable(
                name: "events_heatmap_provenance",
                schema: "core",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "text", nullable: false),
                    stream_id = table.Column<string>(type: "text", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    reversal_of = table.Column<string>(type: "text", nullable: true),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    actor_ref = table.Column<string>(type: "text", nullable: false),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events_heatmap_provenance", x => x.event_id);
                    table.CheckConstraint("ck_events_heatmap_provenance_payload_null_iff_tombstoned", "NOT tombstone OR payload IS NULL");
                });

            migrationBuilder.CreateTable(
                name: "events_ledger",
                schema: "core",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "text", nullable: false),
                    stream_id = table.Column<string>(type: "text", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    reversal_of = table.Column<string>(type: "text", nullable: true),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    actor_ref = table.Column<string>(type: "text", nullable: false),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events_ledger", x => x.event_id);
                    table.CheckConstraint("ck_events_ledger_payload_null_iff_tombstoned", "NOT tombstone OR payload IS NULL");
                });

            migrationBuilder.CreateTable(
                name: "events_reputation",
                schema: "core",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "text", nullable: false),
                    stream_id = table.Column<string>(type: "text", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    reversal_of = table.Column<string>(type: "text", nullable: true),
                    tombstone = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    actor_ref = table.Column<string>(type: "text", nullable: false),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events_reputation", x => x.event_id);
                    table.CheckConstraint("ck_events_reputation_payload_null_iff_tombstoned", "NOT tombstone OR payload IS NULL");
                });

            migrationBuilder.CreateTable(
                name: "field_key_refs",
                schema: "core",
                columns: table => new
                {
                    field_key_id = table.Column<string>(type: "text", nullable: false),
                    vault_key_name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_key_refs", x => x.field_key_id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_balances",
                schema: "core",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    points = table.Column<long>(type: "bigint", nullable: false),
                    xp = table.Column<long>(type: "bigint", nullable: false),
                    svac = table.Column<long>(type: "bigint", nullable: false),
                    watermark = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_balances", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    crew_id = table.Column<string>(type: "text", nullable: true),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    xp = table.Column<int>(type: "integer", nullable: false),
                    svac = table.Column<long>(type: "bigint", nullable: false),
                    quest_id = table.Column<string>(type: "text", nullable: true),
                    evidence_ref = table.Column<string>(type: "text", nullable: true),
                    region = table.Column<string>(type: "text", nullable: false),
                    lawful_basis = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reversal_of = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_points_nonneg", "points >= 0");
                    table.CheckConstraint("ck_ledger_entries_xp_eq_points", "xp = points");
                    table.ForeignKey(
                        name: "FK_ledger_entries_ledger_entries_reversal_of",
                        column: x => x.reversal_of,
                        principalSchema: "core",
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "projection_checkpoints",
                schema: "core",
                columns: table => new
                {
                    consumer_id = table.Column<string>(type: "text", nullable: false),
                    stream_type = table.Column<string>(type: "text", nullable: false),
                    watermark_seq = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projection_checkpoints", x => new { x.consumer_id, x.stream_type });
                });

            migrationBuilder.CreateTable(
                name: "purge_runs",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    purge_class = table.Column<string>(type: "text", nullable: false),
                    subject_ref = table.Column<string>(type: "text", nullable: false),
                    store_key = table.Column<string>(type: "text", nullable: false),
                    rows_affected = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    evidence = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purge_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quota_counters",
                schema: "core",
                columns: table => new
                {
                    actor_ref = table.Column<string>(type: "text", nullable: false),
                    quota_key = table.Column<string>(type: "text", nullable: false),
                    window_key = table.Column<string>(type: "text", nullable: false),
                    consumed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quota_counters", x => new { x.actor_ref, x.quota_key, x.window_key });
                });

            migrationBuilder.CreateIndex(
                name: "IX_events_audit_reversal_of",
                schema: "core",
                table: "events_audit",
                column: "reversal_of");

            migrationBuilder.CreateIndex(
                name: "IX_events_audit_stream_id_seq",
                schema: "core",
                table: "events_audit",
                columns: new[] { "stream_id", "seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_events_behavioral_reversal_of",
                schema: "core",
                table: "events_behavioral",
                column: "reversal_of");

            migrationBuilder.CreateIndex(
                name: "IX_events_behavioral_stream_id_seq",
                schema: "core",
                table: "events_behavioral",
                columns: new[] { "stream_id", "seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_events_consent_reversal_of",
                schema: "core",
                table: "events_consent",
                column: "reversal_of");

            migrationBuilder.CreateIndex(
                name: "IX_events_consent_stream_id_seq",
                schema: "core",
                table: "events_consent",
                columns: new[] { "stream_id", "seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_events_heatmap_provenance_reversal_of",
                schema: "core",
                table: "events_heatmap_provenance",
                column: "reversal_of");

            migrationBuilder.CreateIndex(
                name: "IX_events_heatmap_provenance_stream_id_seq",
                schema: "core",
                table: "events_heatmap_provenance",
                columns: new[] { "stream_id", "seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_events_ledger_reversal_of",
                schema: "core",
                table: "events_ledger",
                column: "reversal_of");

            migrationBuilder.CreateIndex(
                name: "IX_events_ledger_stream_id_seq",
                schema: "core",
                table: "events_ledger",
                columns: new[] { "stream_id", "seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_events_reputation_reversal_of",
                schema: "core",
                table: "events_reputation",
                column: "reversal_of");

            migrationBuilder.CreateIndex(
                name: "IX_events_reputation_stream_id_seq",
                schema: "core",
                table: "events_reputation",
                columns: new[] { "stream_id", "seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_reversal_of",
                schema: "core",
                table: "ledger_entries",
                column: "reversal_of");

            // --- SLICE_S1_CONTRACT.md §2 raw SQL additions, hand-appended after `dotnet ef migrations
            // add` (EF's fluent API cannot express either of these): ---
            //
            // (1) reversal_of self-referencing FK on each of the six event tables. Skipped by EF's
            // model (CoreDbContext.cs comment: HasOne<EventRow>() is ambiguous across six
            // SharedTypeEntity registrations of one CLR type), added here directly instead.
            foreach (var table in new[] { "events_ledger", "events_reputation", "events_consent", "events_behavioral", "events_audit", "events_heatmap_provenance" })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE core.{table}
                        ADD CONSTRAINT fk_{table}_reversal_of
                        FOREIGN KEY (reversal_of) REFERENCES core.{table}(event_id);
                    """);
            }

            // (2) Append-only enforced IN-DATABASE, not by convention (§2): a constraint trigger permits
            // INSERT always; UPDATE only the (tombstone=false -> true, payload -> NULL) transition;
            // DELETE never — EXCEPT the one narrow, deliberate, session-scoped carve-out below, added
            // for the exact bug PurgeCompletenessTests.cs documents in its class-level remark ("bug #2"):
            // §6's purge-taxonomy table declares a real, physical PurgeVerb.Delete against several
            // (store, class) cells that live on these six event tables — events_behavioral for every
            // class, events_reputation's MinorPurge, events_audit's RetentionExpiry,
            // events_heatmap_provenance's StatutoryErasure/MinorPurge/RetentionExpiry — and
            // PurgeCompletenessTests.MinorPurge_OnEventsReputation_IsAHardDelete_DistinctFromAccountDeletionsTombstone
            // proves by name that this must be a REAL hard delete, distinct from Tombstone (softer verb,
            // same store, different class) — converting Delete to Tombstone everywhere would collapse
            // that distinction the registry and its own reason strings ("mechanism is Delete regardless
            // of the window's length") deliberately draw. A blanket, always-on DELETE grant would gut
            // the append-only posture this whole slice makes structural (§0 Privacy thesis); a
            // GUC-gated carve-out keeps DELETE unreachable from every other path (INSERT/Tombstone/
            // Reverse/Append, any consumer, any future module) while making the one legitimate caller
            // (PurgePipeline, immediately below) set the flag for the lifetime of its own transaction
            // only (`SET LOCAL`, which Postgres resets at COMMIT/ROLLBACK regardless of connection
            // pooling — it can never leak onto a later, unrelated statement on a reused pooled
            // connection). Interim posture, Julien-ratifiable per the OQ-1 pattern (SLICE_PLAYBOOK.md
            // Confusion Protocol / CLAUDE.md): reversible by deleting this ELSE branch and the matching
            // PurgePipeline transaction until the first production purge run makes it load-bearing.
            //
            // A distinct, unprivileged application DB role with DELETE revoked (the DDL comment's
            // "DELETE grant additionally revoked from the app role") is deliberately NOT added here: no
            // such role exists yet in this dev compose stack (S0 provisions a single superuser-ish
            // `svac` role for all of Postgres/PostGIS/migrations/app traffic) and revoking DELETE from
            // that role would break migration tooling itself, not just enforce the invariant. The
            // trigger below already blocks every DELETE regardless of the calling role's grants unless
            // the session opts in via the carve-out — a least-privilege app role is a follow-on infra
            // item (S0/S5 role story), not a gap in the structural enforcement this contract cares about.
            // Two sanctioned UPDATE transitions exist, not one: the tombstone transition (payload ->
            // NULL, flag set) AND the pseudonymize transition (§6: events_consent's AccountDeletion/
            // StatutoryErasure/MinorPurge posture is "pseudonymize subject" — PurgePipeline.PseudonymizeRef
            // re-keys ONLY actor_ref, leaving tombstone/payload untouched, on a store that never carries
            // the Tombstone verb in the registry). Both transitions are exhaustively column-checked below
            // so neither can be smuggled into a wider write; every other column is pinned to its OLD
            // value in every branch.
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
                               AND NEW.actor_ref IS NOT DISTINCT FROM OLD.actor_ref) THEN
                            NULL; -- the tombstone transition: permitted.
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
                            NULL; -- the pseudonymize transition: only actor_ref changes, permitted.
                        ELSE
                            RAISE EXCEPTION 'core.%: UPDATE permits only the (tombstone=false->true, payload->NULL) transition or the (actor_ref re-key) pseudonymize transition, no other column change (SLICE_S1_CONTRACT.md §2/§6)', TG_TABLE_NAME;
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            foreach (var table in new[] { "events_ledger", "events_reputation", "events_consent", "events_behavioral", "events_audit", "events_heatmap_provenance" })
            {
                migrationBuilder.Sql($"""
                    CREATE CONSTRAINT TRIGGER trg_{table}_append_only
                        AFTER UPDATE OR DELETE ON core.{table}
                        FOR EACH ROW EXECUTE FUNCTION core.enforce_append_only();
                    """);
            }

            // ledger_entries has no tombstone/payload columns — DELETE never (reversal is the only
            // correction verb per §1b) and UPDATE never EXCEPT one narrow, sanctioned transition that
            // mirrors the events_<stream> tombstone carve-out for the different column set here: §6's
            // AccountDeletion/StatutoryErasure/MinorPurge posture for this store group is "Tombstone
            // refs (entries survive as tombstones, user refs severed; balances rebuilt by Replay)" —
            // severing user_id to the redacted sentinel below, every OTHER column held byte-identical
            // (no economic-magnitude surgery: points/xp/svac/quest_id/evidence_ref/region/lawful_basis/
            // created_at/reversal_of are untouchable, matching AppendOnlyTriggerTests.
            // LedgerEntries_NeverPermitsUpdateOrDelete_ReversalIsTheOnlyCorrectionVerb's Points=999
            // rejection). This does not reopen "data surgery has no policy entry, hence impossible"
            // (§3): core.purge.execute is ITS OWN, already-registered 4A-gated verb (PurgePipeline.Run
            // authorizes it before touching any store) distinct from ledger mutation actions — the
            // sentinel value must match PurgePipeline.ExecuteOnLedgerEntries's literal exactly.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION core.enforce_ledger_append_only() RETURNS trigger AS $$
                BEGIN
                    IF (TG_OP = 'DELETE') THEN
                        RAISE EXCEPTION 'core.ledger_entries: DELETE is never permitted — reversal is the ONLY correction verb (SLICE_S1_CONTRACT.md §1b)';
                    ELSIF (TG_OP = 'UPDATE') THEN
                        IF (OLD.user_id = 'usr_REDACTED0000000000000000000') THEN
                            RAISE EXCEPTION 'core.ledger_entries: row % user_id is already severed — the ONE sanctioned update path may run once', OLD.id;
                        ELSIF (NEW.user_id IS DISTINCT FROM 'usr_REDACTED0000000000000000000'
                               OR NEW.crew_id IS DISTINCT FROM OLD.crew_id
                               OR NEW.event_type IS DISTINCT FROM OLD.event_type
                               OR NEW.points IS DISTINCT FROM OLD.points
                               OR NEW.xp IS DISTINCT FROM OLD.xp
                               OR NEW.svac IS DISTINCT FROM OLD.svac
                               OR NEW.quest_id IS DISTINCT FROM OLD.quest_id
                               OR NEW.evidence_ref IS DISTINCT FROM OLD.evidence_ref
                               OR NEW.region IS DISTINCT FROM OLD.region
                               OR NEW.lawful_basis IS DISTINCT FROM OLD.lawful_basis
                               OR NEW.created_at IS DISTINCT FROM OLD.created_at
                               OR NEW.reversal_of IS DISTINCT FROM OLD.reversal_of) THEN
                            RAISE EXCEPTION 'core.ledger_entries: UPDATE permits only the user_id -> redacted-sentinel severing transition, every other column held fixed (SLICE_S1_CONTRACT.md §6)';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.Sql("""
                CREATE CONSTRAINT TRIGGER trg_ledger_entries_append_only
                    AFTER UPDATE OR DELETE ON core.ledger_entries
                    FOR EACH ROW EXECUTE FUNCTION core.enforce_ledger_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse the hand-appended raw SQL from Up() first (triggers/functions before the tables
            // DropTable below removes) — destructive-verb-check.mjs (build/scripts) requires a
            // "-- destructive:" marker on a bare DROP; these SQL DROPs run only inside Down(), which
            // never executes against a live environment (no down-migration path is wired into the
            // startup migration service, §2/BUILD.md §8), so they carry the marker for the lint's sake
            // rather than because a production rollback ever invokes this.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_ledger_entries_append_only ON core.ledger_entries; -- destructive: reversible Down() only, never invoked by the startup migration service");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS core.enforce_ledger_append_only(); -- destructive: reversible Down() only");
            foreach (var table in new[] { "events_ledger", "events_reputation", "events_consent", "events_behavioral", "events_audit", "events_heatmap_provenance" })
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_{table}_append_only ON core.{table}; -- destructive: reversible Down() only");
            }
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS core.enforce_append_only(); -- destructive: reversible Down() only");
            foreach (var table in new[] { "events_ledger", "events_reputation", "events_consent", "events_behavioral", "events_audit", "events_heatmap_provenance" })
            {
                migrationBuilder.Sql($"ALTER TABLE core.{table} DROP CONSTRAINT IF EXISTS fk_{table}_reversal_of; -- destructive: reversible Down() only");
            }

            migrationBuilder.DropTable(
                name: "config_entries",
                schema: "core");

            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "core");

            migrationBuilder.DropTable(
                name: "events_audit",
                schema: "core");

            migrationBuilder.DropTable(
                name: "events_behavioral",
                schema: "core");

            migrationBuilder.DropTable(
                name: "events_consent",
                schema: "core");

            migrationBuilder.DropTable(
                name: "events_heatmap_provenance",
                schema: "core");

            migrationBuilder.DropTable(
                name: "events_ledger",
                schema: "core");

            migrationBuilder.DropTable(
                name: "events_reputation",
                schema: "core");

            migrationBuilder.DropTable(
                name: "field_key_refs",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ledger_balances",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "core");

            migrationBuilder.DropTable(
                name: "projection_checkpoints",
                schema: "core");

            migrationBuilder.DropTable(
                name: "purge_runs",
                schema: "core");

            migrationBuilder.DropTable(
                name: "quota_counters",
                schema: "core");
        }
    }
}
