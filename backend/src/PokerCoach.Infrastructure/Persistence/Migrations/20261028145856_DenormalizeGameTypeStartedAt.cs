using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DenormalizeGameTypeStartedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "game_type",
                schema: "poker",
                table: "hand_hero_facts",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "started_at",
                schema: "poker",
                table: "hand_hero_facts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.Sql("""
                UPDATE poker.hand_hero_facts f
                SET game_type = h.game_type,
                    started_at = h.started_at
                FROM poker.hands h
                WHERE f.hand_id = h.id
                """);

            migrationBuilder.CreateIndex(
                name: "ix_hand_hero_facts_poker_account_id_facts_version_game_type",
                schema: "poker",
                table: "hand_hero_facts",
                columns: new[] { "poker_account_id", "facts_version", "game_type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_hand_hero_facts_poker_account_id_facts_version_game_type",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "game_type",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "started_at",
                schema: "poker",
                table: "hand_hero_facts");
        }
    }
}
