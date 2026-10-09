using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rebuys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "addon_cost",
                schema: "poker",
                table: "tournaments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "rebuy_cost",
                schema: "poker",
                table: "tournaments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "addons",
                schema: "poker",
                table: "tournament_entries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "rebuys",
                schema: "poker",
                table: "tournament_entries",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "addon_cost",
                schema: "poker",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "rebuy_cost",
                schema: "poker",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "addons",
                schema: "poker",
                table: "tournament_entries");

            migrationBuilder.DropColumn(
                name: "rebuys",
                schema: "poker",
                table: "tournament_entries");
        }
    }
}
