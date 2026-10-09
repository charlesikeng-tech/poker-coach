using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllInEv : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "all_in_equity",
                schema: "poker",
                table: "hand_hero_facts",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "all_in_expected_net_chips",
                schema: "poker",
                table: "hand_hero_facts",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "all_in_equity",
                schema: "poker",
                table: "hand_hero_facts");

            migrationBuilder.DropColumn(
                name: "all_in_expected_net_chips",
                schema: "poker",
                table: "hand_hero_facts");
        }
    }
}
