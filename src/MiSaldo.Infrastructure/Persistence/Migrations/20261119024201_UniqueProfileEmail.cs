using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiSaldo.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// One account per email, ignoring case. The registration use case checks
    /// first, but only the database can stop two registrations that race with
    /// the same address. An expression index on lower(Email) cannot be declared
    /// in the EF model, so it is created with raw SQL.
    /// </summary>
    public partial class UniqueProfileEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_user_profiles_Email_lower\" " +
                "ON user_profiles (lower(\"Email\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_user_profiles_Email_lower\";");
        }
    }
}
