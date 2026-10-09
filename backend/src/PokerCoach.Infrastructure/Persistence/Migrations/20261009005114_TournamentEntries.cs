using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TournamentEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bounty_winnings",
                schema: "poker",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "finish_position",
                schema: "poker",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "played_duration",
                schema: "poker",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "prize_winnings",
                schema: "poker",
                table: "tournaments");

            migrationBuilder.CreateTable(
                name: "tournament_entries",
                schema: "poker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_number = table.Column<int>(type: "integer", nullable: false),
                    late_registration = table.Column<bool>(type: "boolean", nullable: false),
                    played_duration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    finish_position = table.Column<int>(type: "integer", nullable: true),
                    prize_winnings = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    bounty_winnings = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tournament_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_tournament_entries_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalSchema: "poker",
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_tournament_entries_tournament_id_entry_number",
                schema: "poker",
                table: "tournament_entries",
                columns: new[] { "tournament_id", "entry_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tournament_entries",
                schema: "poker");

            migrationBuilder.AddColumn<decimal>(
                name: "bounty_winnings",
                schema: "poker",
                table: "tournaments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "finish_position",
                schema: "poker",
                table: "tournaments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "played_duration",
                schema: "poker",
                table: "tournaments",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "prize_winnings",
                schema: "poker",
                table: "tournaments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);
        }
    }
}
