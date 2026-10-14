using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashGame : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_hands_tournaments_tournament_id",
                schema: "poker",
                table: "hands");

            migrationBuilder.AlterColumn<Guid>(
                name: "tournament_id",
                schema: "poker",
                table: "hands",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<short>(
                name: "game_type",
                schema: "poker",
                table: "hands",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<long>(
                name: "rake",
                schema: "poker",
                table: "hands",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stakes",
                schema: "poker",
                table: "hands",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_hands_poker_account_id_game_type",
                schema: "poker",
                table: "hands",
                columns: new[] { "poker_account_id", "game_type" });

            migrationBuilder.AddForeignKey(
                name: "fk_hands_tournaments_tournament_id",
                schema: "poker",
                table: "hands",
                column: "tournament_id",
                principalSchema: "poker",
                principalTable: "tournaments",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_hands_tournaments_tournament_id",
                schema: "poker",
                table: "hands");

            migrationBuilder.DropIndex(
                name: "ix_hands_poker_account_id_game_type",
                schema: "poker",
                table: "hands");

            migrationBuilder.DropColumn(
                name: "game_type",
                schema: "poker",
                table: "hands");

            migrationBuilder.DropColumn(
                name: "rake",
                schema: "poker",
                table: "hands");

            migrationBuilder.DropColumn(
                name: "stakes",
                schema: "poker",
                table: "hands");

            migrationBuilder.AlterColumn<Guid>(
                name: "tournament_id",
                schema: "poker",
                table: "hands",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_hands_tournaments_tournament_id",
                schema: "poker",
                table: "hands",
                column: "tournament_id",
                principalSchema: "poker",
                principalTable: "tournaments",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
