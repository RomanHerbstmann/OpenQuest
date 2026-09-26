using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace OpenQuest.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "record_count",
                table: "sync_run",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "schema_hash",
                table: "sync_run",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "snapshot_key",
                table: "sync_run",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "asset_snapshot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sync_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    geom = table.Column<Point>(type: "geography (point)", nullable: false),
                    raw = table.Column<string>(type: "jsonb", nullable: false),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_snapshot", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_snapshot_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asset_snapshot_sync_run_sync_run_id",
                        column: x => x.sync_run_id,
                        principalTable: "sync_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_snapshot_asset_id_sync_run_id",
                table: "asset_snapshot",
                columns: new[] { "asset_id", "sync_run_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_snapshot_sync_run_id",
                table: "asset_snapshot",
                column: "sync_run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_snapshot");

            migrationBuilder.DropColumn(
                name: "record_count",
                table: "sync_run");

            migrationBuilder.DropColumn(
                name: "schema_hash",
                table: "sync_run");

            migrationBuilder.DropColumn(
                name: "snapshot_key",
                table: "sync_run");
        }
    }
}
