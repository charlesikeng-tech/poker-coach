using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class SizingRealSpots : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "source_hand_id",
            schema: "training",
            table: "sizing_attempts",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_sizing_attempts_user_id_format_source_hand_id",
            schema: "training",
            table: "sizing_attempts",
            columns: new[] { "user_id", "format", "source_hand_id" },
            filter: "source_hand_id IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_sizing_attempts_user_id_format_source_hand_id",
            schema: "training",
            table: "sizing_attempts");

        migrationBuilder.DropColumn(
            name: "source_hand_id",
            schema: "training",
            table: "sizing_attempts");
    }
}
