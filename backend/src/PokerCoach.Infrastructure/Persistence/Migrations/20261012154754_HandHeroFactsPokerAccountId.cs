using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HandHeroFactsPokerAccountId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "poker_account_id",
                schema: "poker",
                table: "hand_hero_facts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("""
                UPDATE poker.hand_hero_facts f
                SET poker_account_id = h.poker_account_id
                FROM poker.hands h
                WHERE f.hand_id = h.id
                """);

            migrationBuilder.CreateIndex(
                name: "ix_hand_hero_facts_poker_account_id_facts_version",
                schema: "poker",
                table: "hand_hero_facts",
                columns: new[] { "poker_account_id", "facts_version" });

            migrationBuilder.AddForeignKey(
                name: "fk_hand_hero_facts_poker_accounts_poker_account_id",
                schema: "poker",
                table: "hand_hero_facts",
                column: "poker_account_id",
                principalSchema: "poker",
                principalTable: "poker_accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_hand_hero_facts_poker_accounts_poker_account_id",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropIndex(
                name: "ix_hand_hero_facts_poker_account_id_facts_version",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "poker_account_id",
                schema: "poker",
                table: "hand_hero_facts");
        }
    }
}
