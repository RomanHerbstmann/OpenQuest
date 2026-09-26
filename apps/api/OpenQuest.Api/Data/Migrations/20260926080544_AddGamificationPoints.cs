using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenQuest.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGamificationPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "point_transaction",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_point_transaction", x => x.id);
                    table.CheckConstraint("ck_point_transaction_amount", "amount <> 0");
                    table.ForeignKey(
                        name: "fk_point_transaction_submission_submission_id",
                        column: x => x.submission_id,
                        principalTable: "submission",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_point_transaction_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_point_transaction_submission_id_reason",
                table: "point_transaction",
                columns: new[] { "submission_id", "reason" },
                unique: true,
                filter: "submission_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_point_transaction_user_id_created_at",
                table: "point_transaction",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "point_transaction");
        }
    }
}
