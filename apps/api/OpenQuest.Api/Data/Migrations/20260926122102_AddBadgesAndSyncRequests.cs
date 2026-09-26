using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenQuest.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBadgesAndSyncRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "badge",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    icon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    criteria = table.Column<string>(type: "jsonb", nullable: false),
                    reward_points = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_badge", x => x.id);
                    table.CheckConstraint("ck_badge_reward", "reward_points >= 0");
                });

            migrationBuilder.CreateTable(
                name: "sync_request",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_source_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    force = table.Column<bool>(type: "boolean", nullable: false),
                    accept_schema_change = table.Column<bool>(type: "boolean", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    sync_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_request", x => x.id);
                    table.CheckConstraint("ck_sync_request_status", "status IN ('pending','running','succeeded','failed')");
                    table.ForeignKey(
                        name: "fk_sync_request_sync_run_sync_run_id",
                        column: x => x.sync_run_id,
                        principalTable: "sync_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_sync_request_user_requested_by",
                        column: x => x.requested_by,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_badge",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    badge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    awarded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_badge", x => new { x.user_id, x.badge_id });
                    table.ForeignKey(
                        name: "fk_user_badge_badge_badge_id",
                        column: x => x.badge_id,
                        principalTable: "badge",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_badge_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_badge_key",
                table: "badge",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_request_data_source_key",
                table: "sync_request",
                column: "data_source_key",
                unique: true,
                filter: "status IN ('pending','running')");

            migrationBuilder.CreateIndex(
                name: "ix_sync_request_requested_by",
                table: "sync_request",
                column: "requested_by");

            migrationBuilder.CreateIndex(
                name: "ix_sync_request_status_requested_at",
                table: "sync_request",
                columns: new[] { "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_request_sync_run_id",
                table: "sync_request",
                column: "sync_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_badge_badge_id",
                table: "user_badge",
                column: "badge_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_badge_user_id_awarded_at",
                table: "user_badge",
                columns: new[] { "user_id", "awarded_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_request");

            migrationBuilder.DropTable(
                name: "user_badge");

            migrationBuilder.DropTable(
                name: "badge");
        }
    }
}
