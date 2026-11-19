using MiSaldo.Domain.Categories;

namespace MiSaldo.Application.Accounts;

/// <summary>
/// The categories every new account starts with (RF-CAT-01). They become the
/// user's own data from then on and can be renamed, reordered or archived, so
/// they are seeded in the launch language rather than read from translations.
/// </summary>
public static class DefaultCategories
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "Arriendo/vivienda",
        "Servicios públicos",
        "Alimentación y mercado",
        "Transporte y gasolina",
        "Salud",
        "Educación",
        "Entretenimiento",
        "Compras personales",
        "Suscripciones",
        "Otros",
    ];

    /// <summary>New category instances, in display order, for one account.</summary>
    public static IReadOnlyList<ExpenseCategory> Create() =>
        Names.Select((name, index) => new ExpenseCategory(Guid.NewGuid(), name, sortOrder: index + 1)).ToList();
}
