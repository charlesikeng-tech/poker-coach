using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixWinamaxCashGameType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Winamax cash game hands were imported as Tournament (parser bug).
            // A hand without a tournament_id is a cash game hand.
            migrationBuilder.Sql("""
                UPDATE poker.hands
                SET game_type = 2
                WHERE tournament_id IS NULL AND game_type = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE poker.hands
                SET game_type = 1
                WHERE tournament_id IS NULL AND game_type = 2;
                """);
        }
    }
}
