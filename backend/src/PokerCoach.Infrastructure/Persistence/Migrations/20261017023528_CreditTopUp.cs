using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreditTopUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "purchased_credits_reserved",
                schema: "coaching",
                table: "model_usage",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "credit_purchases",
                schema: "coaching",
                columns: table => new
                {
                    stripe_event_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credits = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_purchases", x => x.stripe_event_id);
                    table.ForeignKey(
                        name: "fk_credit_purchases_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_credit_balances",
                schema: "coaching",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_credit_balances", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_credit_balances_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credit_purchases_user_id",
                schema: "coaching",
                table: "credit_purchases",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_purchases",
                schema: "coaching");

            migrationBuilder.DropTable(
                name: "user_credit_balances",
                schema: "coaching");

            migrationBuilder.DropColumn(
                name: "purchased_credits_reserved",
                schema: "coaching",
                table: "model_usage");
        }
    }
}
