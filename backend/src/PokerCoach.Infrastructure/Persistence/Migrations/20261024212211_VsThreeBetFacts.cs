using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VsThreeBetFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "call_three_bet",
                schema: "poker",
                table: "hand_hero_facts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "facing_three_bet_from",
                schema: "poker",
                table: "hand_hero_facts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "four_bet",
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
                name: "call_three_bet",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "facing_three_bet_from",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "four_bet",
                schema: "poker",
                table: "hand_hero_facts");
        }
    }
}
