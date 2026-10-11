using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pro_grants",
                schema: "subscriptions",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pro_grants", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_pro_grants_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pro_grants",
                schema: "subscriptions");
        }
    }
}
