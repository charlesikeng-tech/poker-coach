using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomRanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ranges");

            migrationBuilder.CreateTable(
                name: "custom_defence_ranges",
                schema: "ranges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<int>(type: "integer", nullable: false),
                    band = table.Column<int>(type: "integer", nullable: false),
                    defender = table.Column<int>(type: "integer", nullable: false),
                    opener = table.Column<int>(type: "integer", nullable: false),
                    call_hands = table.Column<byte[]>(type: "bytea", nullable: false),
                    three_bet_hands = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_custom_defence_ranges", x => x.id);
                    table.ForeignKey(
                        name: "fk_custom_defence_ranges_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "custom_opening_ranges",
                schema: "ranges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<int>(type: "integer", nullable: false),
                    band = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    hands = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_custom_opening_ranges", x => x.id);
                    table.ForeignKey(
                        name: "fk_custom_opening_ranges_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_custom_defence_ranges_user_id_format_band_defender_opener",
                schema: "ranges",
                table: "custom_defence_ranges",
                columns: new[] { "user_id", "format", "band", "defender", "opener" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_custom_opening_ranges_user_id_format_band_position",
                schema: "ranges",
                table: "custom_opening_ranges",
                columns: new[] { "user_id", "format", "band", "position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_defence_ranges",
                schema: "ranges");

            migrationBuilder.DropTable(
                name: "custom_opening_ranges",
                schema: "ranges");
        }
    }
}
