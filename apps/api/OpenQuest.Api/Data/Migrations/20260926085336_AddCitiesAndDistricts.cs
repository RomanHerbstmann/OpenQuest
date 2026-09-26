using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace OpenQuest.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCitiesAndDistricts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "district_id",
                table: "point_transaction",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "city",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    center_lat = table.Column<double>(type: "double precision", nullable: true),
                    center_lon = table.Column<double>(type: "double precision", nullable: true),
                    default_zoom = table.Column<int>(type: "integer", nullable: true),
                    timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_city", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "district",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    city_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    total_points = table.Column<int>(type: "integer", nullable: false),
                    geom = table.Column<Polygon>(type: "geography (polygon)", nullable: false),
                    centroid_lat = table.Column<double>(type: "double precision", nullable: false),
                    centroid_lon = table.Column<double>(type: "double precision", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_district", x => x.id);
                    table.ForeignKey(
                        name: "fk_district_city_city_id",
                        column: x => x.city_id,
                        principalTable: "city",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_district_user_created_by",
                        column: x => x.created_by,
                        principalTable: "user",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "district_point",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    district_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    lat = table.Column<double>(type: "double precision", nullable: false),
                    lon = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_district_point", x => x.id);
                    table.CheckConstraint("ck_district_point_position", "position >= 0");
                    table.ForeignKey(
                        name: "fk_district_point_district_district_id",
                        column: x => x.district_id,
                        principalTable: "district",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_point_transaction_district_id_created_at",
                table: "point_transaction",
                columns: new[] { "district_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_city_key",
                table: "city",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_district_city_id_key",
                table: "district",
                columns: new[] { "city_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_district_city_id_name",
                table: "district",
                columns: new[] { "city_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_district_created_by",
                table: "district",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_district_geom",
                table: "district",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_district_point_district_id_position",
                table: "district_point",
                columns: new[] { "district_id", "position" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_point_transaction_district_district_id",
                table: "point_transaction",
                column: "district_id",
                principalTable: "district",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_point_transaction_district_district_id",
                table: "point_transaction");

            migrationBuilder.DropTable(
                name: "district_point");

            migrationBuilder.DropTable(
                name: "district");

            migrationBuilder.DropTable(
                name: "city");

            migrationBuilder.DropIndex(
                name: "ix_point_transaction_district_id_created_at",
                table: "point_transaction");

            migrationBuilder.DropColumn(
                name: "district_id",
                table: "point_transaction");
        }
    }
}
