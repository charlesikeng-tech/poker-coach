using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrainingRealHands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_hand_id",
                schema: "training",
                table: "opening_attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_opening_attempts_user_id_source_hand_id",
                schema: "training",
                table: "opening_attempts",
                columns: new[] { "user_id", "source_hand_id" },
                filter: "source_hand_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_opening_attempts_user_id_source_hand_id",
                schema: "training",
                table: "opening_attempts");

            migrationBuilder.DropColumn(
                name: "source_hand_id",
                schema: "training",
                table: "opening_attempts");
        }
    }
}
