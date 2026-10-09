using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HandHeroFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hand_hero_facts",
                schema: "poker",
                columns: table => new
                {
                    hand_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facts_version = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: true),
                    players_dealt = table.Column<int>(type: "integer", nullable: false),
                    stack_in_big_blinds = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    had_preflop_decision = table.Column<bool>(type: "boolean", nullable: false),
                    vpip = table.Column<bool>(type: "boolean", nullable: false),
                    pfr = table.Column<bool>(type: "boolean", nullable: false),
                    rfi_opportunity = table.Column<bool>(type: "boolean", nullable: false),
                    rfi = table.Column<bool>(type: "boolean", nullable: false),
                    limp = table.Column<bool>(type: "boolean", nullable: false),
                    steal_opportunity = table.Column<bool>(type: "boolean", nullable: false),
                    steal = table.Column<bool>(type: "boolean", nullable: false),
                    three_bet_opportunity = table.Column<bool>(type: "boolean", nullable: false),
                    three_bet = table.Column<bool>(type: "boolean", nullable: false),
                    fold_to_three_bet_opportunity = table.Column<bool>(type: "boolean", nullable: false),
                    fold_to_three_bet = table.Column<bool>(type: "boolean", nullable: false),
                    saw_flop = table.Column<bool>(type: "boolean", nullable: false),
                    cbet_flop_opportunity = table.Column<bool>(type: "boolean", nullable: false),
                    cbet_flop = table.Column<bool>(type: "boolean", nullable: false),
                    went_to_showdown = table.Column<bool>(type: "boolean", nullable: false),
                    won_at_showdown = table.Column<bool>(type: "boolean", nullable: false),
                    net_chips = table.Column<long>(type: "bigint", nullable: false),
                    net_big_blinds = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hand_hero_facts", x => x.hand_id);
                    table.ForeignKey(
                        name: "fk_hand_hero_facts_hands_hand_id",
                        column: x => x.hand_id,
                        principalSchema: "poker",
                        principalTable: "hands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hand_hero_facts_facts_version",
                schema: "poker",
                table: "hand_hero_facts",
                column: "facts_version");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hand_hero_facts",
                schema: "poker");
        }
    }
}
