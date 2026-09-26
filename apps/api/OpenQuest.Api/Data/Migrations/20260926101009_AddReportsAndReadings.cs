using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace OpenQuest.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReportsAndReadings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asset_report",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    category = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    status_notes = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    media_url = table.Column<string>(type: "text", nullable: true),
                    geom = table.Column<Point>(type: "geography (point)", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    distance_m = table.Column<double>(type: "double precision", nullable: true),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    raw = table.Column<string>(type: "jsonb", nullable: false),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_report", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_report_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_asset_report_data_source_data_source_id",
                        column: x => x.data_source_id,
                        principalTable: "data_source",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "environment_reading",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    metric = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<double>(type: "double precision", nullable: false),
                    unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    measured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    geom = table.Column<Point>(type: "geography (point)", nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_environment_reading", x => x.id);
                    table.ForeignKey(
                        name: "fk_environment_reading_data_source_data_source_id",
                        column: x => x.data_source_id,
                        principalTable: "data_source",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_report_asset_id_status",
                table: "asset_report",
                columns: new[] { "asset_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_asset_report_category_status_reported_at",
                table: "asset_report",
                columns: new[] { "category", "status", "reported_at" });

            migrationBuilder.CreateIndex(
                name: "ix_asset_report_data_source_id_external_id",
                table: "asset_report",
                columns: new[] { "data_source_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_report_geom",
                table: "asset_report",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_environment_reading_data_source_id_station_id_metric_measur",
                table: "environment_reading",
                columns: new[] { "data_source_id", "station_id", "metric", "measured_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_environment_reading_metric_measured_at",
                table: "environment_reading",
                columns: new[] { "metric", "measured_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_report");

            migrationBuilder.DropTable(
                name: "environment_reading");
        }
    }
}
