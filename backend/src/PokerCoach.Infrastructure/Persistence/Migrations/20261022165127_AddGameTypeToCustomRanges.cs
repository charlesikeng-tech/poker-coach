using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGameTypeToCustomRanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_custom_opening_ranges_user_id_format_band_position",
                schema: "ranges",
                table: "custom_opening_ranges");

            migrationBuilder.DropIndex(
                name: "ux_custom_defence_ranges_user_id_format_band_defender_opener",
                schema: "ranges",
                table: "custom_defence_ranges");

            migrationBuilder.AddColumn<int>(
                name: "game_type",
                schema: "ranges",
                table: "custom_opening_ranges",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "game_type",
                schema: "ranges",
                table: "custom_defence_ranges",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ux_custom_opening_ranges_user_id_format_band_position_game_type",
                schema: "ranges",
                table: "custom_opening_ranges",
                columns: new[] { "user_id", "format", "band", "position", "game_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_custom_defence_ranges_user_id_format_band_defender_opener_game_type",
                schema: "ranges",
                table: "custom_defence_ranges",
                columns: new[] { "user_id", "format", "band", "defender", "opener", "game_type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_custom_opening_ranges_user_id_format_band_position_game_type",
                schema: "ranges",
                table: "custom_opening_ranges");

            migrationBuilder.DropIndex(
                name: "ux_custom_defence_ranges_user_id_format_band_defender_opener_game_type",
                schema: "ranges",
                table: "custom_defence_ranges");

            migrationBuilder.DropColumn(
                name: "game_type",
                schema: "ranges",
                table: "custom_opening_ranges");

            migrationBuilder.DropColumn(
                name: "game_type",
                schema: "ranges",
                table: "custom_defence_ranges");

            migrationBuilder.CreateIndex(
                name: "ux_custom_opening_ranges_user_id_format_band_position",
                schema: "ranges",
                table: "custom_opening_ranges",
                columns: new[] { "user_id", "format", "band", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_custom_defence_ranges_user_id_format_band_defender_opener",
                schema: "ranges",
                table: "custom_defence_ranges",
                columns: new[] { "user_id", "format", "band", "defender", "opener" },
                unique: true);
        }
    }
}
