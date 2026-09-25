using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace OpenQuest.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "asset_type",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    icon = table.Column<string>(type: "text", nullable: false),
                    attribute_schema = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_type", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_source",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    adapter_key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    city = table.Column<string>(type: "text", nullable: false),
                    source_url = table.Column<string>(type: "text", nullable: false),
                    license = table.Column<string>(type: "text", nullable: false),
                    attribution = table.Column<string>(type: "text", nullable: false),
                    config = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_source", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "task_type",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    config_schema = table.Column<string>(type: "jsonb", nullable: false),
                    result_schema = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_type", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    display_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    total_points = table.Column<int>(type: "integer", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "asset",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "text", nullable: false),
                    geom = table.Column<Point>(type: "geography (point)", nullable: false),
                    attributes = table.Column<string>(type: "jsonb", nullable: false),
                    raw = table.Column<string>(type: "jsonb", nullable: false),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_asset_type_asset_type_id",
                        column: x => x.asset_type_id,
                        principalTable: "asset_type",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asset_data_source_data_source_id",
                        column: x => x.data_source_id,
                        principalTable: "data_source",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sync_run",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    assets_created = table.Column<int>(type: "integer", nullable: false),
                    assets_updated = table.Column<int>(type: "integer", nullable: false),
                    assets_removed = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_run", x => x.id);
                    table.ForeignKey(
                        name: "fk_sync_run_data_source_data_source_id",
                        column: x => x.data_source_id,
                        principalTable: "data_source",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asset_type_task_type",
                columns: table => new
                {
                    asset_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_type_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_type_task_type", x => new { x.asset_type_id, x.task_type_id });
                    table.ForeignKey(
                        name: "fk_asset_type_task_type_asset_type_asset_type_id",
                        column: x => x.asset_type_id,
                        principalTable: "asset_type",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asset_type_task_type_task_type_task_type_id",
                        column: x => x.task_type_id,
                        principalTable: "task_type",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "export_run",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    format = table.Column<string>(type: "text", nullable: false),
                    storage_key = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    change_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_export_run", x => x.id);
                    table.ForeignKey(
                        name: "fk_export_run_data_source_data_source_id",
                        column: x => x.data_source_id,
                        principalTable: "data_source",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_export_run_user_created_by",
                        column: x => x.created_by,
                        principalTable: "user",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "quest_campaign",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    asset_filter = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quest_campaign", x => x.id);
                    table.ForeignKey(
                        name: "fk_quest_campaign_user_created_by",
                        column: x => x.created_by,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_recovery_code",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "text", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_recovery_code", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_recovery_code_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quest",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    task_config = table.Column<string>(type: "jsonb", nullable: false),
                    max_completions = table.Column<int>(type: "integer", nullable: false),
                    slots_taken = table.Column<int>(type: "integer", nullable: false),
                    reward_points = table.Column<int>(type: "integer", nullable: false),
                    geofence_radius_m = table.Column<int>(type: "integer", nullable: false),
                    claim_ttl_minutes = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quest", x => x.id);
                    table.CheckConstraint("ck_quest_slots", "slots_taken >= 0 AND max_completions >= 1");
                    table.ForeignKey(
                        name: "fk_quest_asset_asset_id",
                        column: x => x.asset_id,
                        principalTable: "asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_quest_quest_campaign_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "quest_campaign",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_quest_task_type_task_type_id",
                        column: x => x.task_type_id,
                        principalTable: "task_type",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_quest_user_created_by",
                        column: x => x.created_by,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "claim",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_claim", x => x.id);
                    table.ForeignKey(
                        name: "fk_claim_quest_quest_id",
                        column: x => x.quest_id,
                        principalTable: "quest",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_claim_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "submission",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    location = table.Column<Point>(type: "geography (point)", nullable: false),
                    distance_m = table.Column<double>(type: "double precision", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "text", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submission", x => x.id);
                    table.ForeignKey(
                        name: "fk_submission_claim_claim_id",
                        column: x => x.claim_id,
                        principalTable: "claim",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_submission_user_reviewed_by",
                        column: x => x.reviewed_by,
                        principalTable: "user",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "attribute_change",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    old_value = table.Column<string>(type: "jsonb", nullable: true),
                    new_value = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    export_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attribute_change", x => x.id);
                    table.ForeignKey(
                        name: "fk_attribute_change_asset_asset_id",
                        column: x => x.asset_id,
                        principalTable: "asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attribute_change_export_runs_export_run_id",
                        column: x => x.export_run_id,
                        principalTable: "export_run",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_attribute_change_submission_submission_id",
                        column: x => x.submission_id,
                        principalTable: "submission",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    mime_type = table.Column<string>(type: "text", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    phash = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media", x => x.id);
                    table.ForeignKey(
                        name: "fk_media_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "submission",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_asset_type_id_status",
                table: "asset",
                columns: new[] { "asset_type_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_asset_attributes",
                table: "asset",
                column: "attributes")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_asset_data_source_id_external_id",
                table: "asset",
                columns: new[] { "data_source_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_geom",
                table: "asset",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_asset_type_key",
                table: "asset_type",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_type_task_type_task_type_id",
                table: "asset_type_task_type",
                column: "task_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_attribute_change_asset_id",
                table: "attribute_change",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_attribute_change_export_run_id",
                table: "attribute_change",
                column: "export_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_attribute_change_status",
                table: "attribute_change",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_attribute_change_submission_id",
                table: "attribute_change",
                column: "submission_id");

            migrationBuilder.CreateIndex(
                name: "ix_claim_expires_at",
                table: "claim",
                column: "expires_at",
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_claim_quest_id_status",
                table: "claim",
                columns: new[] { "quest_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_claim_quest_id_user_id",
                table: "claim",
                columns: new[] { "quest_id", "user_id" },
                unique: true,
                filter: "status IN ('active','submitted')");

            migrationBuilder.CreateIndex(
                name: "ix_claim_user_id_status",
                table: "claim",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_data_source_key",
                table: "data_source",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_export_run_created_by",
                table: "export_run",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_export_run_data_source_id",
                table: "export_run",
                column: "data_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_sha256",
                table: "media",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "ix_media_submission_id",
                table: "media",
                column: "submission_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_available_at_occurred_at",
                table: "outbox_message",
                columns: new[] { "available_at", "occurred_at" },
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_quest_asset_id",
                table: "quest",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_quest_campaign_id",
                table: "quest",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_quest_created_by",
                table: "quest",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_quest_status_asset_id",
                table: "quest",
                columns: new[] { "status", "asset_id" });

            migrationBuilder.CreateIndex(
                name: "ix_quest_task_type_id",
                table: "quest",
                column: "task_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_quest_campaign_created_by",
                table: "quest_campaign",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_submission_claim_id",
                table: "submission",
                column: "claim_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_submission_reviewed_by",
                table: "submission",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "ix_submission_status_submitted_at",
                table: "submission",
                columns: new[] { "status", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_run_data_source_id_started_at",
                table: "sync_run",
                columns: new[] { "data_source_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_type_key",
                table: "task_type",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_username",
                table: "user",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_recovery_code_user_id",
                table: "user_recovery_code",
                column: "user_id");

            // Event-driven delivery: PostgreSQL delivers NOTIFY to listeners only when the inserting transaction commits,
            // so the outbox processor wakes up exactly when a new event became visible (no polling).
            migrationBuilder.Sql(@"
                CREATE FUNCTION notify_outbox() RETURNS trigger AS $$
                BEGIN
                    PERFORM pg_notify('outbox', NEW.id::text);
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;");
            migrationBuilder.Sql(@"
                CREATE TRIGGER outbox_message_notify AFTER INSERT ON outbox_message
                FOR EACH ROW EXECUTE FUNCTION notify_outbox();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS outbox_message_notify ON outbox_message;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS notify_outbox();");
            migrationBuilder.DropTable(
                name: "asset_type_task_type");

            migrationBuilder.DropTable(
                name: "attribute_change");

            migrationBuilder.DropTable(
                name: "media");

            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropTable(
                name: "sync_run");

            migrationBuilder.DropTable(
                name: "user_recovery_code");

            migrationBuilder.DropTable(
                name: "export_run");

            migrationBuilder.DropTable(
                name: "submission");

            migrationBuilder.DropTable(
                name: "claim");

            migrationBuilder.DropTable(
                name: "quest");

            migrationBuilder.DropTable(
                name: "asset");

            migrationBuilder.DropTable(
                name: "quest_campaign");

            migrationBuilder.DropTable(
                name: "task_type");

            migrationBuilder.DropTable(
                name: "asset_type");

            migrationBuilder.DropTable(
                name: "data_source");

            migrationBuilder.DropTable(
                name: "user");
        }
    }
}
