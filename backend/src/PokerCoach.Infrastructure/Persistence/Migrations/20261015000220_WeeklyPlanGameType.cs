using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WeeklyPlanGameType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_weekly_plans",
                schema: "progress",
                table: "weekly_plans");

            migrationBuilder.AddColumn<int>(
                name: "game_type",
                schema: "progress",
                table: "weekly_plans",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddPrimaryKey(
                name: "pk_weekly_plans",
                schema: "progress",
                table: "weekly_plans",
                columns: new[] { "user_id", "week_start", "game_type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_weekly_plans",
                schema: "progress",
                table: "weekly_plans");

            migrationBuilder.DropColumn(
                name: "game_type",
                schema: "progress",
                table: "weekly_plans");

            migrationBuilder.AddPrimaryKey(
                name: "pk_weekly_plans",
                schema: "progress",
                table: "weekly_plans",
                columns: new[] { "user_id", "week_start" });
        }
    }
}
