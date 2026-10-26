using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoachBookmarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "coach_bookmarks",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    coach_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coach_bookmarks", x => x.id);
                    table.ForeignKey(
                        name: "fk_coach_bookmarks_coach_invitations_invitation_id",
                        column: x => x.invitation_id,
                        principalSchema: "identity",
                        principalTable: "coach_invitations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_coach_bookmarks_users_coach_user_id",
                        column: x => x.coach_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_coach_bookmarks_coach_user_id",
                schema: "identity",
                table: "coach_bookmarks",
                column: "coach_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_coach_bookmarks_invitation_id",
                schema: "identity",
                table: "coach_bookmarks",
                column: "invitation_id");

            migrationBuilder.CreateIndex(
                name: "ux_coach_bookmarks_coach_user_id_invitation_id",
                schema: "identity",
                table: "coach_bookmarks",
                columns: new[] { "coach_user_id", "invitation_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coach_bookmarks",
                schema: "identity");
        }
    }
}
