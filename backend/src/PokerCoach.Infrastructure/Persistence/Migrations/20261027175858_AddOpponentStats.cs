using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpponentStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "opponent_stats",
                schema: "poker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    poker_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opponent_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    hands = table.Column<int>(type: "integer", nullable: false),
                    vpip = table.Column<int>(type: "integer", nullable: false),
                    pfr = table.Column<int>(type: "integer", nullable: false),
                    three_bet_opportunity = table.Column<int>(type: "integer", nullable: false),
                    three_bet = table.Column<int>(type: "integer", nullable: false),
                    saw_flop = table.Column<int>(type: "integer", nullable: false),
                    went_to_showdown = table.Column<int>(type: "integer", nullable: false),
                    postflop_aggressive = table.Column<int>(type: "integer", nullable: false),
                    postflop_decisions = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_opponent_stats", x => x.id);
                    table.ForeignKey(
                        name: "fk_opponent_stats_poker_accounts_poker_account_id",
                        column: x => x.poker_account_id,
                        principalSchema: "poker",
                        principalTable: "poker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_opponent_stats_account_opponent",
                schema: "poker",
                table: "opponent_stats",
                columns: new[] { "poker_account_id", "opponent_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "opponent_stats",
                schema: "poker");
        }
    }
}
