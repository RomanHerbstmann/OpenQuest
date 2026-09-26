using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenQuest.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStaleQuests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asset_activity",
                columns: table => new
                {
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verification_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_activity", x => x.asset_id);
                    table.CheckConstraint("ck_asset_activity_count", "verification_count >= 1");
                    table.ForeignKey(
                        name: "fk_asset_activity_asset_asset_id",
                        column: x => x.asset_id,
                        principalTable: "asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quest_schedule",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    city_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    weekday = table.Column<int>(type: "integer", nullable: false),
                    time_of_day = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    duration_hours = table.Column<int>(type: "integer", nullable: false),
                    task_type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    task_config = table.Column<string>(type: "jsonb", nullable: false),
                    target = table.Column<string>(type: "jsonb", nullable: false),
                    max_completions = table.Column<int>(type: "integer", nullable: false),
                    reward_points = table.Column<int>(type: "integer", nullable: false),
                    geofence_radius_m = table.Column<int>(type: "integer", nullable: true),
                    claim_ttl_minutes = table.Column<int>(type: "integer", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quest_schedule", x => x.id);
                    table.CheckConstraint("ck_quest_schedule_duration", "duration_hours BETWEEN 1 AND 168");
                    table.CheckConstraint("ck_quest_schedule_weekday", "weekday BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "fk_quest_schedule_city_city_id",
                        column: x => x.city_id,
                        principalTable: "city",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_quest_schedule_user_created_by",
                        column: x => x.created_by,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quest_schedule_run",
                columns: table => new
                {
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_key = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    ran_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quests_created = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quest_schedule_run", x => new { x.schedule_id, x.period_key });
                    table.ForeignKey(
                        name: "fk_quest_schedule_run_quest_campaign_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "quest_campaign",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_quest_schedule_run_quest_schedule_schedule_id",
                        column: x => x.schedule_id,
                        principalTable: "quest_schedule",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_activity_last_verified_at",
                table: "asset_activity",
                column: "last_verified_at");

            migrationBuilder.CreateIndex(
                name: "ix_quest_schedule_city_id",
                table: "quest_schedule",
                column: "city_id");

            migrationBuilder.CreateIndex(
                name: "ix_quest_schedule_created_by",
                table: "quest_schedule",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_quest_schedule_name",
                table: "quest_schedule",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quest_schedule_run_campaign_id",
                table: "quest_schedule_run",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_quest_schedule_run_schedule_id_ran_at",
                table: "quest_schedule_run",
                columns: new[] { "schedule_id", "ran_at" });

            // Players verified assets before this table existed: derive the state from the approved submissions.
            migrationBuilder.Sql(@"
                INSERT INTO asset_activity (asset_id, last_verified_at, verification_count)
                SELECT q.asset_id, max(s.reviewed_at), count(*)
                FROM submission s
                JOIN claim c ON c.id = s.claim_id
                JOIN quest q ON q.id = c.quest_id
                WHERE s.status = 'approved' AND s.reviewed_at IS NOT NULL
                GROUP BY q.asset_id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_activity");

            migrationBuilder.DropTable(
                name: "quest_schedule_run");

            migrationBuilder.DropTable(
                name: "quest_schedule");
        }
    }
}
