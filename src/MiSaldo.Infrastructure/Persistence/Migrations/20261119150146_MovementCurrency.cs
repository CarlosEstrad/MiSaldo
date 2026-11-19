using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiSaldo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MovementCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "value_currency",
                table: "income_transactions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "COP");

            migrationBuilder.AddColumn<string>(
                name: "budgeted_currency",
                table: "expenses",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "COP");

            migrationBuilder.AddColumn<string>(
                name: "value_currency",
                table: "expense_payments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "COP");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "value_currency",
                table: "income_transactions");

            migrationBuilder.DropColumn(
                name: "budgeted_currency",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "value_currency",
                table: "expense_payments");
        }
    }
}
