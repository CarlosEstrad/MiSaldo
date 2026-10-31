using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiSaldo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentIdempotencyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_expense_payments_UserId_IdempotencyKey",
                table: "expense_payments",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_expense_payments_UserId_IdempotencyKey",
                table: "expense_payments");
        }
    }
}
