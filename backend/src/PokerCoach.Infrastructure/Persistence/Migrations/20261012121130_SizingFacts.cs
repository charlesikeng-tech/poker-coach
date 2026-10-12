using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SizingFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "facing_open_effective_stack",
                schema: "poker",
                table: "hand_hero_facts",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "facing_open_to_big_blinds",
                schema: "poker",
                table: "hand_hero_facts",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "open_all_in",
                schema: "poker",
                table: "hand_hero_facts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "open_to_big_blinds",
                schema: "poker",
                table: "hand_hero_facts",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "three_bet_all_in",
                schema: "poker",
                table: "hand_hero_facts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "three_bet_to_big_blinds",
                schema: "poker",
                table: "hand_hero_facts",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sizing_attempts",
                schema: "training",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<int>(type: "integer", nullable: false),
                    band = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    stack_big_blinds = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    opener = table.Column<int>(type: "integer", nullable: true),
                    open_to_big_blinds = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    answer_size = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    correct = table.Column<bool>(type: "boolean", nullable: false),
                    reference_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sizing_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_sizing_attempts_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sizing_attempts_user_id_format_created_at",
                schema: "training",
                table: "sizing_attempts",
                columns: new[] { "user_id", "format", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sizing_attempts",
                schema: "training");

            migrationBuilder.DropColumn(
                name: "facing_open_effective_stack",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "facing_open_to_big_blinds",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "open_all_in",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "open_to_big_blinds",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "three_bet_all_in",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "three_bet_to_big_blinds",
                schema: "poker",
                table: "hand_hero_facts");
        }
    }
}
