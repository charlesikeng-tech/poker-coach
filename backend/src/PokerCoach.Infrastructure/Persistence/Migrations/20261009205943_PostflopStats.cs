using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PostflopStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "cbet_turn",
                schema: "poker",
                table: "hand_hero_facts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "cbet_turn_opportunity",
                schema: "poker",
                table: "hand_hero_facts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "check_raise_flop",
                schema: "poker",
                table: "hand_hero_facts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "check_raise_flop_opportunity",
                schema: "poker",
                table: "hand_hero_facts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "fold_to_cbet_flop",
                schema: "poker",
                table: "hand_hero_facts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "fold_to_cbet_flop_opportunity",
                schema: "poker",
                table: "hand_hero_facts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "postflop_aggressive",
                schema: "poker",
                table: "hand_hero_facts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "postflop_decisions",
                schema: "poker",
                table: "hand_hero_facts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "raise_cbet_flop",
                schema: "poker",
                table: "hand_hero_facts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "won_when_saw_flop",
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
                name: "cbet_turn",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "cbet_turn_opportunity",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "check_raise_flop",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "check_raise_flop_opportunity",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "fold_to_cbet_flop",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "fold_to_cbet_flop_opportunity",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "postflop_aggressive",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "postflop_decisions",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "raise_cbet_flop",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "won_when_saw_flop",
                schema: "poker",
                table: "hand_hero_facts");
        }
    }
}
