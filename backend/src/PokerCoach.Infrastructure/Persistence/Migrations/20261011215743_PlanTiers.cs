using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlanTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tier",
                schema: "subscriptions",
                table: "subscriptions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plan",
                schema: "subscriptions",
                table: "pro_grants",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Pro");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tier",
                schema: "subscriptions",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "plan",
                schema: "subscriptions",
                table: "pro_grants");
        }
    }
}
