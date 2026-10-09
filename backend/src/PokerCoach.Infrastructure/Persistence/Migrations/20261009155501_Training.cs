using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Training : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "training");

            migrationBuilder.CreateTable(
                name: "opening_attempts",
                schema: "training",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<int>(type: "integer", nullable: false),
                    band = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    hand = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    answer = table.Column<int>(type: "integer", nullable: false),
                    correct = table.Column<bool>(type: "boolean", nullable: false),
                    reference_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_opening_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_opening_attempts_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_opening_attempts_user_id_format_created_at",
                schema: "training",
                table: "opening_attempts",
                columns: new[] { "user_id", "format", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "opening_attempts",
                schema: "training");
        }
    }
}
