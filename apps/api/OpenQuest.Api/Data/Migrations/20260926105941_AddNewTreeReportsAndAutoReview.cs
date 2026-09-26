using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace OpenQuest.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNewTreeReportsAndAutoReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_quest_asset_asset_id",
                table: "quest");

            migrationBuilder.AddColumn<string>(
                name: "auto_review",
                table: "submission",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "auto_reviewed_at",
                table: "submission",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "asset_id",
                table: "quest",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "district_id",
                table: "quest",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "asset_proposal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    district_id = table.Column<Guid>(type: "uuid", nullable: true),
                    geom = table.Column<Point>(type: "geography (point)", nullable: false),
                    genus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    species = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    photo_url = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    export_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_proposal", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_proposal_asset_type_asset_type_id",
                        column: x => x.asset_type_id,
                        principalTable: "asset_type",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asset_proposal_data_source_data_source_id",
                        column: x => x.data_source_id,
                        principalTable: "data_source",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asset_proposal_district_district_id",
                        column: x => x.district_id,
                        principalTable: "district",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_asset_proposal_export_runs_export_run_id",
                        column: x => x.export_run_id,
                        principalTable: "export_run",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_asset_proposal_submission_submission_id",
                        column: x => x.submission_id,
                        principalTable: "submission",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_quest_district_id",
                table: "quest",
                column: "district_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_quest_place",
                table: "quest",
                sql: "asset_id IS NOT NULL OR district_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_asset_proposal_asset_type_id",
                table: "asset_proposal",
                column: "asset_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_proposal_data_source_id",
                table: "asset_proposal",
                column: "data_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_proposal_district_id",
                table: "asset_proposal",
                column: "district_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_proposal_export_run_id",
                table: "asset_proposal",
                column: "export_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_proposal_geom",
                table: "asset_proposal",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_asset_proposal_status",
                table: "asset_proposal",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_asset_proposal_submission_id",
                table: "asset_proposal",
                column: "submission_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_quest_asset_asset_id",
                table: "quest",
                column: "asset_id",
                principalTable: "asset",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_quest_districts_district_id",
                table: "quest",
                column: "district_id",
                principalTable: "district",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_quest_asset_asset_id",
                table: "quest");

            migrationBuilder.DropForeignKey(
                name: "fk_quest_districts_district_id",
                table: "quest");

            migrationBuilder.DropTable(
                name: "asset_proposal");

            migrationBuilder.DropIndex(
                name: "ix_quest_district_id",
                table: "quest");

            migrationBuilder.DropCheckConstraint(
                name: "ck_quest_place",
                table: "quest");

            migrationBuilder.DropColumn(
                name: "auto_review",
                table: "submission");

            migrationBuilder.DropColumn(
                name: "auto_reviewed_at",
                table: "submission");

            migrationBuilder.DropColumn(
                name: "district_id",
                table: "quest");

            migrationBuilder.AlterColumn<Guid>(
                name: "asset_id",
                table: "quest",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_quest_asset_asset_id",
                table: "quest",
                column: "asset_id",
                principalTable: "asset",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
