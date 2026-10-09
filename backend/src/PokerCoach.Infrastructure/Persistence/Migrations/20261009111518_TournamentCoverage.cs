using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TournamentCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tournament_coverage",
                schema: "poker",
                columns: table => new
                {
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: false),
                    coverage_version = table.Column<int>(type: "integer", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    hand_count = table.Column<int>(type: "integer", nullable: false),
                    first_level = table.Column<int>(type: "integer", nullable: true),
                    last_level = table.Column<int>(type: "integer", nullable: true),
                    missing_hands = table.Column<int>(type: "integer", nullable: false),
                    stack_breaks = table.Column<int>(type: "integer", nullable: false),
                    entries_seen = table.Column<int>(type: "integer", nullable: false),
                    start_missing = table.Column<bool>(type: "boolean", nullable: true),
                    end_seen = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tournament_coverage", x => x.tournament_id);
                    table.ForeignKey(
                        name: "fk_tournament_coverage_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalSchema: "poker",
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tournament_coverage",
                schema: "poker");
        }
    }
}
