using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiUsageGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "credits",
                schema: "coaching",
                table: "model_usage",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "reserved_cost_usd",
                schema: "coaching",
                table: "model_usage",
                type: "numeric(12,6)",
                precision: 12,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "settled_at",
                schema: "coaching",
                table: "model_usage",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "coaching",
                table: "model_usage",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Billed");

            migrationBuilder.AddColumn<bool>(
                name: "usage_known",
                schema: "coaching",
                table: "model_usage",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Calls recorded before the guard were all billed successes, priced as in ADR-0016; the defaults above
            // only fill these rows (the application always sets every column).
            migrationBuilder.Sql("""
                UPDATE coaching.model_usage
                SET credits = CASE purpose WHEN 'tournament-debrief' THEN 5 WHEN 'week-review' THEN 2 ELSE 1 END,
                    settled_at = created_at;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "credits",
                schema: "coaching",
                table: "model_usage");

            migrationBuilder.DropColumn(
                name: "reserved_cost_usd",
                schema: "coaching",
                table: "model_usage");

            migrationBuilder.DropColumn(
                name: "settled_at",
                schema: "coaching",
                table: "model_usage");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "coaching",
                table: "model_usage");

            migrationBuilder.DropColumn(
                name: "usage_known",
                schema: "coaching",
                table: "model_usage");
        }
    }
}
