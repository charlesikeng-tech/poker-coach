using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TournamentSubFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_weekly_plans",
                schema: "progress",
                table: "weekly_plans");

            migrationBuilder.AddColumn<int>(
                name: "sub_format_key",
                schema: "progress",
                table: "weekly_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "sub_format",
                schema: "poker",
                table: "tournaments",
                type: "integer",
                nullable: true);

            // Backfill sub_format from the raw mode string for existing tournaments.
            migrationBuilder.Sql("""
                UPDATE poker.tournaments
                SET sub_format = CASE
                    WHEN mode = 'sng' THEN 3
                    WHEN mode = 'mtt' THEN 2
                    WHEN mode IS NOT NULL THEN 1
                    ELSE NULL
                END
                WHERE mode IS NOT NULL
                """);

            migrationBuilder.AddPrimaryKey(
                name: "pk_weekly_plans",
                schema: "progress",
                table: "weekly_plans",
                columns: new[] { "user_id", "week_start", "game_type", "sub_format_key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_weekly_plans",
                schema: "progress",
                table: "weekly_plans");

            migrationBuilder.DropColumn(
                name: "sub_format_key",
                schema: "progress",
                table: "weekly_plans");

            migrationBuilder.DropColumn(
                name: "sub_format",
                schema: "poker",
                table: "tournaments");

            migrationBuilder.AddPrimaryKey(
                name: "pk_weekly_plans",
                schema: "progress",
                table: "weekly_plans",
                columns: new[] { "user_id", "week_start", "game_type" });
        }
    }
}
