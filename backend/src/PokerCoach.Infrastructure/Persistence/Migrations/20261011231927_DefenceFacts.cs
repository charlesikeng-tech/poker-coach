using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DefenceFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "facing_open_call",
                schema: "poker",
                table: "hand_hero_facts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "facing_open_from",
                schema: "poker",
                table: "hand_hero_facts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "facing_open_three_bet",
                schema: "poker",
                table: "hand_hero_facts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "facing_open_call",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "facing_open_from",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "facing_open_three_bet",
                schema: "poker",
                table: "hand_hero_facts");
        }
    }
}
