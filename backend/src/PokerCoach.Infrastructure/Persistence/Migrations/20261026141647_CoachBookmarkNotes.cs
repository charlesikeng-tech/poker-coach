using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoachBookmarkNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "notes",
                schema: "identity",
                table: "coach_bookmarks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "notes",
                schema: "identity",
                table: "coach_bookmarks");
        }
    }
}
