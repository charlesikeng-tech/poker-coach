using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DenormalizeRemainingHandColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "big_blind",
                schema: "poker",
                table: "hand_hero_facts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "hero_cards",
                schema: "poker",
                table: "hand_hero_facts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "level",
                schema: "poker",
                table: "hand_hero_facts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "max_seats",
                schema: "poker",
                table: "hand_hero_facts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "tournament_id",
                schema: "poker",
                table: "hand_hero_facts",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE poker.hand_hero_facts f
                SET big_blind = h.big_blind,
                    hero_cards = h.hero_cards,
                    level = h.level,
                    max_seats = h.max_seats,
                    tournament_id = h.tournament_id
                FROM poker.hands h
                WHERE f.hand_id = h.id
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "big_blind",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "hero_cards",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "level",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "max_seats",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "tournament_id",
                schema: "poker",
                table: "hand_hero_facts");
        }
    }
}
