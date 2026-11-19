using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiSaldo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PeriodClosingFigures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CarriedOver",
                table: "financial_periods",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ClosingBalance",
                table: "financial_periods",
                type: "numeric(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CarriedOver",
                table: "financial_periods");

            migrationBuilder.DropColumn(
                name: "ClosingBalance",
                table: "financial_periods");
        }
    }
}
