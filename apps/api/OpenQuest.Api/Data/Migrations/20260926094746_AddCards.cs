using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenQuest.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "genus_stats_at",
                table: "district",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "known_genus_trees",
                table: "district",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "card",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    district_id = table.Column<Guid>(type: "uuid", nullable: true),
                    genus = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    rarity = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    frequency = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    share = table.Column<double>(type: "double precision", nullable: true),
                    reasons = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card", x => x.id);
                    table.ForeignKey(
                        name: "fk_card_asset_asset_id",
                        column: x => x.asset_id,
                        principalTable: "asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_card_district_district_id",
                        column: x => x.district_id,
                        principalTable: "district",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_card_submission_submission_id",
                        column: x => x.submission_id,
                        principalTable: "submission",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_card_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "district_genus_stat",
                columns: table => new
                {
                    district_id = table.Column<Guid>(type: "uuid", nullable: false),
                    genus = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    tree_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_district_genus_stat", x => new { x.district_id, x.genus });
                    table.ForeignKey(
                        name: "fk_district_genus_stat_district_district_id",
                        column: x => x.district_id,
                        principalTable: "district",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_card_asset_id",
                table: "card",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_card_district_id",
                table: "card",
                column: "district_id");

            migrationBuilder.CreateIndex(
                name: "ix_card_submission_id",
                table: "card",
                column: "submission_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_card_user_id_created_at",
                table: "card",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_card_user_id_genus",
                table: "card",
                columns: new[] { "user_id", "genus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card");

            migrationBuilder.DropTable(
                name: "district_genus_stat");

            migrationBuilder.DropColumn(
                name: "genus_stats_at",
                table: "district");

            migrationBuilder.DropColumn(
                name: "known_genus_trees",
                table: "district");
        }
    }
}
