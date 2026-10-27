using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnrichOpponentStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cbet_flop",
                schema: "poker",
                table: "opponent_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "cbet_flop_opportunity",
                schema: "poker",
                table: "opponent_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "distinct_tournaments",
                schema: "poker",
                table: "opponent_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_seen_at",
                schema: "poker",
                table: "opponent_stats",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "fold_to_cbet_flop",
                schema: "poker",
                table: "opponent_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "fold_to_cbet_flop_opportunity",
                schema: "poker",
                table: "opponent_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "fold_to_three_bet",
                schema: "poker",
                table: "opponent_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "fold_to_three_bet_opportunity",
                schema: "poker",
                table: "opponent_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_seen_at",
                schema: "poker",
                table: "opponent_stats",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "rfi",
                schema: "poker",
                table: "opponent_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "rfi_opportunity",
                schema: "poker",
                table: "opponent_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "steal",
                schema: "poker",
                table: "opponent_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "steal_opportunity",
                schema: "poker",
                table: "opponent_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "won_at_showdown",
                schema: "poker",
                table: "opponent_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cbet_flop",
                schema: "poker",
                table: "opponent_stats");

            migrationBuilder.DropColumn(
                name: "cbet_flop_opportunity",
                schema: "poker",
                table: "opponent_stats");

            migrationBuilder.DropColumn(
                name: "distinct_tournaments",
                schema: "poker",
                table: "opponent_stats");

            migrationBuilder.DropColumn(
                name: "first_seen_at",
                schema: "poker",
                table: "opponent_stats");

            migrationBuilder.DropColumn(
                name: "fold_to_cbet_flop",
                schema: "poker",
                table: "opponent_stats");

            migrationBuilder.DropColumn(
                name: "fold_to_cbet_flop_opportunity",
                schema: "poker",
                table: "opponent_stats");

            migrationBuilder.DropColumn(
                name: "fold_to_three_bet",
                schema: "poker",
                table: "opponent_stats");

            migrationBuilder.DropColumn(
                name: "fold_to_three_bet_opportunity",
                schema: "poker",
                table: "opponent_stats");

            migrationBuilder.DropColumn(
                name: "last_seen_at",
                schema: "poker",
                table: "opponent_stats");

            migrationBuilder.DropColumn(
                name: "rfi",
                schema: "poker",
                table: "opponent_stats");

            migrationBuilder.DropColumn(
                name: "rfi_opportunity",
                schema: "poker",
                table: "opponent_stats");

            migrationBuilder.DropColumn(
                name: "steal",
                schema: "poker",
                table: "opponent_stats");

            migrationBuilder.DropColumn(
                name: "steal_opportunity",
                schema: "poker",
                table: "opponent_stats");

            migrationBuilder.DropColumn(
                name: "won_at_showdown",
                schema: "poker",
                table: "opponent_stats");
        }
    }
}
