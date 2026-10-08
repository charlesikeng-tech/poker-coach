using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Import : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "poker");

            migrationBuilder.EnsureSchema(
                name: "import");

            migrationBuilder.CreateTable(
                name: "poker_accounts",
                schema: "poker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room = table.Column<int>(type: "integer", nullable: false),
                    screen_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_poker_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_poker_accounts_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "imported_files",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    content_gzip = table.Column<byte[]>(type: "bytea", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    poker_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hands_imported = table.Column<int>(type: "integer", nullable: true),
                    hands_already_present = table.Column<int>(type: "integer", nullable: true),
                    hands_rejected = table.Column<int>(type: "integer", nullable: true),
                    rejections = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_imported_files", x => x.id);
                    table.CheckConstraint("ck_imported_files_kind", "kind IS NULL OR kind IN ('hand_history', 'tournament_summary')");
                    table.CheckConstraint("ck_imported_files_status", "status IN ('pending', 'processing', 'completed', 'failed')");
                    table.ForeignKey(
                        name: "fk_imported_files_poker_accounts_poker_account_id",
                        column: x => x.poker_account_id,
                        principalSchema: "poker",
                        principalTable: "poker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_imported_files_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tournaments",
                schema: "poker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    poker_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_tournament_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    buy_in_excluding_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    first_hand_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    prize_pool_buy_in = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    bounty_buy_in = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    registered_players = table.Column<int>(type: "integer", nullable: true),
                    mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    tournament_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    speed = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    flight_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    prize_pool = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    played_duration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    finish_position = table.Column<int>(type: "integer", nullable: true),
                    prize_winnings = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    bounty_winnings = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    summary_imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tournaments", x => x.id);
                    table.ForeignKey(
                        name: "fk_tournaments_poker_accounts_poker_account_id",
                        column: x => x.poker_account_id,
                        principalSchema: "poker",
                        principalTable: "poker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hands",
                schema: "poker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    poker_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: false),
                    imported_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_hand_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    small_blind = table.Column<long>(type: "bigint", nullable: false),
                    big_blind = table.Column<long>(type: "bigint", nullable: false),
                    ante = table.Column<long>(type: "bigint", nullable: true),
                    table_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    max_seats = table.Column<int>(type: "integer", nullable: false),
                    button_seat = table.Column<int>(type: "integer", nullable: false),
                    hero_seat = table.Column<int>(type: "integer", nullable: true),
                    hero_stack = table.Column<long>(type: "bigint", nullable: true),
                    hero_cards = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    total_pot = table.Column<long>(type: "bigint", nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hands", x => x.id);
                    table.ForeignKey(
                        name: "fk_hands_imported_files_imported_file_id",
                        column: x => x.imported_file_id,
                        principalSchema: "import",
                        principalTable: "imported_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_hands_poker_accounts_poker_account_id",
                        column: x => x.poker_account_id,
                        principalSchema: "poker",
                        principalTable: "poker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_hands_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalSchema: "poker",
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hands_imported_file_id",
                schema: "poker",
                table: "hands",
                column: "imported_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_hands_tournament_id_started_at",
                schema: "poker",
                table: "hands",
                columns: new[] { "tournament_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ux_hands_poker_account_id_external_hand_id",
                schema: "poker",
                table: "hands",
                columns: new[] { "poker_account_id", "external_hand_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_imported_files_created_at",
                schema: "import",
                table: "imported_files",
                column: "created_at",
                filter: "status IN ('pending', 'processing')");

            migrationBuilder.CreateIndex(
                name: "ix_imported_files_poker_account_id",
                schema: "import",
                table: "imported_files",
                column: "poker_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_imported_files_user_id_batch_id",
                schema: "import",
                table: "imported_files",
                columns: new[] { "user_id", "batch_id" });

            migrationBuilder.CreateIndex(
                name: "ux_imported_files_user_id_content_sha256",
                schema: "import",
                table: "imported_files",
                columns: new[] { "user_id", "content_sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_poker_accounts_user_id_room_screen_name",
                schema: "poker",
                table: "poker_accounts",
                columns: new[] { "user_id", "room", "screen_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tournaments_poker_account_id_external_tournament_id",
                schema: "poker",
                table: "tournaments",
                columns: new[] { "poker_account_id", "external_tournament_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hands",
                schema: "poker");

            migrationBuilder.DropTable(
                name: "imported_files",
                schema: "import");

            migrationBuilder.DropTable(
                name: "tournaments",
                schema: "poker");

            migrationBuilder.DropTable(
                name: "poker_accounts",
                schema: "poker");
        }
    }
}
