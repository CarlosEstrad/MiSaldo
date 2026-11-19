using MiSaldo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MiSaldo.Infrastructure.Tests;

/// <summary>
/// Validates that EF Core can build the model for the DbContext: private
/// constructors, backing-field collections, shadow properties and the Money
/// converter all map without error. Building the model does not require a
/// database connection, so these run without Docker.
/// </summary>
public class ModelBuildingTests
{
    private static MiSaldoDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MiSaldoDbContext>()
            .UseNpgsql("Host=localhost;Database=misaldo_model_check;Username=x;Password=x")
            .Options;
        return new MiSaldoDbContext(options, new TestCurrentUser());
    }

    [Fact]
    public void Model_builds_without_error()
    {
        using var context = CreateContext();
        var model = context.Model; // forces model creation
        Assert.NotNull(model);
    }

    [Theory]
    [InlineData("users")]
    [InlineData("user_profiles")]
    [InlineData("user_settings")]
    [InlineData("consent_records")]
    [InlineData("account_deletion_records")]
    [InlineData("financial_periods")]
    [InlineData("expense_categories")]
    [InlineData("income_transactions")]
    [InlineData("expenses")]
    [InlineData("expense_payments")]
    [InlineData("audit_logs")]
    public void Expected_table_is_mapped(string tableName)
    {
        using var context = CreateContext();
        var tables = context.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .ToHashSet();
        Assert.Contains(tableName, tables);
    }

    [Fact]
    public void Financial_entities_have_user_id_shadow_property()
    {
        using var context = CreateContext();
        foreach (var table in new[] { "financial_periods", "expense_categories", "income_transactions", "expenses", "expense_payments" })
        {
            var entity = context.Model.GetEntityTypes().Single(e => e.GetTableName() == table);
            Assert.NotNull(entity.FindProperty("UserId"));
        }
    }

    [Fact]
    public void Money_properties_map_to_numeric_18_2()
    {
        using var context = CreateContext();
        var income = context.Model.GetEntityTypes().Single(e => e.GetTableName() == "income_transactions");

        // Money is a complex value stored in two columns: the amount and its currency (design T2).
        var value = income.FindComplexProperty("Value");
        Assert.NotNull(value);
        var amount = value!.ComplexType.FindProperty("Amount")!;
        Assert.Equal("numeric(18,2)", amount.GetColumnType());
        Assert.Equal("value_amount", amount.GetColumnName());
        Assert.Equal("value_currency", value.ComplexType.FindProperty("Currency")!.GetColumnName());
    }

    [Fact]
    public void Expense_payments_collection_maps_through_backing_field()
    {
        using var context = CreateContext();
        var expense = context.Model.GetEntityTypes().Single(e => e.GetTableName() == "expenses");
        var nav = expense.FindNavigation("Payments");
        Assert.NotNull(nav);
        Assert.Equal("_payments", nav!.GetFieldName());
    }
}
