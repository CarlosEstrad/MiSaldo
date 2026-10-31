using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiSaldo.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Enables Row Level Security on the user-scoped tables (second isolation
    /// barrier, RNF-SEG-04). Each table gets a policy that restricts rows to the
    /// user whose id is in the session variable <c>app.user_id</c>, set per
    /// connection by the RLS interceptor. The application connects with a role
    /// that does not bypass RLS, so this barrier holds even if the EF Core query
    /// filter is omitted (CP-07).
    /// </summary>
    public partial class EnableRowLevelSecurity : Migration
    {
        private static readonly string[] UserScopedTables =
        [
            "financial_periods",
            "expense_categories",
            "income_transactions",
            "expenses",
            "expense_payments",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in UserScopedTables)
            {
                // Enable and force RLS (FORCE applies it to the table owner too,
                // so no connected role sees other users' rows by accident).
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY;");

                // Policy: a row is visible/modifiable only when its UserId equals
                // the session's app.user_id. current_setting(..., true) returns
                // NULL when unset, which fails the comparison -> no rows.
                migrationBuilder.Sql(
                    $"CREATE POLICY {table}_user_isolation ON {table} " +
                    "USING (\"UserId\" = NULLIF(current_setting('app.user_id', true), '')::uuid) " +
                    "WITH CHECK (\"UserId\" = NULLIF(current_setting('app.user_id', true), '')::uuid);");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in UserScopedTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_user_isolation ON {table};");
                migrationBuilder.Sql($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
            }
        }
    }
}
