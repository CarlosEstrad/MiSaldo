# Diseño — Dominio y motor financiero

## Visión general

`MiSaldo.Domain` es una biblioteca de clases de C# puro sobre **.NET 10**, sin
dependencias de infraestructura (RNF-MAN-01). Contiene las entidades
financieras, los objetos de valor, las reglas de negocio y un **motor de cálculo
sin estado** que produce todos los indicadores del periodo a partir de los
movimientos que recibe. Ninguna otra capa recalcula saldos (RN-F*, D-06 de
arquitectura).

El diseño sigue dos principios del SRS:

1. **Los saldos no se almacenan; se calculan** desde los movimientos
   (RNF-DAT-07). El motor es una función pura: mismas entradas → mismas salidas.
2. **Las invariantes se protegen en el dominio.** Un monto no positivo, un pago
   mayor al saldo o un retiro mayor al disponible lanzan una excepción de
   dominio, no se corrigen en silencio (RNF-USA-05, RF-CRD-11).

### Estructura de la solución

```
MiSaldo.sln
├── global.json                       # fija el SDK de .NET 10
├── Directory.Build.props             # Nullable, ImplicitUsings, TreatWarningsAsErrors, LangVersion
├── src/
│   └── MiSaldo.Domain/
│       ├── MiSaldo.Domain.csproj     # net10.0, sin paquetes de infraestructura
│       ├── Common/                   # Money, Currency, Entity, DomainException
│       ├── Periods/                  # FinancialPeriod, PeriodType, PeriodStatus, PeriodCalendar
│       ├── Incomes/                  # IncomeTransaction, IncomeStatus
│       ├── Expenses/                 # Expense, ExpensePayment, ExpenseStatus
│       ├── Savings/                  # SavingsGoal, SavingsTransaction
│       ├── Credits/                  # Credit, CreditInstallment, CreditPayment
│       └── Engine/                   # FinancialEngine, PeriodIndicators, ovp inputs
└── tests/
    └── MiSaldo.Domain.Tests/
        └── MiSaldo.Domain.Tests.csproj  # xUnit, net10.0, referencia a Domain
```

## Decisiones técnicas

| # | Decisión | Motivo |
|---|---|---|
| T1 | `Money` como `readonly record struct` con `decimal Amount` + `Currency`. | Igualdad por valor, inmutable, sin boxing innecesario. |
| T2 | Redondeo con `MidpointRounding.ToEven` (bancario) a los decimales de la moneda. | RN-M07: no pierde ni crea dinero; sesgo nulo en grandes volúmenes. |
| T3 | `Currency` lleva su código ISO y `DecimalPlaces` (COP=0, USD=2). | RNF-DAT-02; evita hardcodear decimales. |
| T4 | Operar `Money` entre monedas distintas lanza `CurrencyMismatchException`. | Supuesto SRS: una sola moneda principal. |
| T5 | `FinancialEngine` es estático/sin estado; recibe los movimientos y devuelve `PeriodIndicators`. | RNF-DAT-07, RF-PRD-08: función pura de los movimientos. |
| T6 | El reparto de residuos (cuotas, redondeos) usa "la última absorbe el residuo". | RN-C03, RF-CRD-02, CP-15. |
| T7 | Los pagos (`ExpensePayment`, `CreditPayment`, `SavingsTransaction`) son inmutables una vez creados. | RN-M08, RN-C06: historial inmutable. |
| T8 | Fechas de movimiento con `DateOnly`; el dominio nunca consulta la hora del sistema. | RNF-DAT-06; mantiene la pureza y la testeabilidad. |
| T9 | `Id` como `Guid` generado fuera o por fábrica inyectable en pruebas. | Determinismo en pruebas; el dominio no depende del reloj ni del RNG global. |
| T10 | Excepciones de dominio heredan de `DomainException`. | Un único punto para distinguir errores de regla de errores técnicos. |

## Componentes y tipos

### Common

```csharp
public sealed record Currency(string Code, int DecimalPlaces)
{
    public static readonly Currency Cop = new("COP", 0);
    public static readonly Currency Usd = new("USD", 2);
}

public readonly record struct Money
{
    public decimal Amount { get; }
    public Currency Currency { get; }

    public Money(decimal amount, Currency currency);     // normaliza al nº de decimales
    public static Money Zero(Currency currency);

    public Money Add(Money other);                        // exige misma moneda (T4)
    public Money Subtract(Money other);
    public Money Round();                                 // ToEven a DecimalPlaces (T2)
    public bool IsPositive { get; }
    public bool IsNegative { get; }
    // operadores +, -, comparadores
}
```

- `Money` **no** redondea en cada operación intermedia: acumula en `decimal` y
  redondea al exponer totales, para cumplir RN-M07 y el criterio R1.4.
- `DomainException` y subtipos: `NonPositiveAmountException`,
  `CurrencyMismatchException`, `PaymentExceedsBalanceException`,
  `WithdrawalExceedsSavingsException`, `SavingsExceedsAvailableException`,
  `InvalidPeriodRangeException`.

### Periods

```csharp
public enum PeriodType { Weekly, Biweekly, Monthly }
public enum PeriodStatus { Planned, Active, Overdue, Closed }

public sealed class FinancialPeriod
{
    public Guid Id { get; }
    public PeriodType Type { get; }
    public DateOnly StartDate { get; }
    public DateOnly EndDate { get; }
    public bool IsClosed { get; private set; }
    public bool CarryOverEnabled { get; }

    public PeriodStatus StatusOn(DateOnly today);         // R2.2
    public bool Contains(DateOnly date);
    public void Close();                                  // R2.5
}

// Cálculo de rangos a partir de config (RN-P01..P03)
public static class PeriodCalendar
{
    public static (DateOnly start, DateOnly end) RangeFor(
        PeriodType type, DateOnly anchor, PeriodSettings settings);
}
```

`PeriodSettings`: día de inicio de semana, día de inicio del ciclo quincenal,
día de inicio mensual (1–28). Implementa RN-P01 (semanal), RN-P02 (quincenal,
incluido el caso A→B−1 / B→A−1) y RN-P03 (mensual, día 1–28).

### Incomes

```csharp
public enum IncomeStatus { Planned, Received }

public sealed class IncomeTransaction
{
    public Guid Id { get; }
    public Money Value { get; }                 // > 0 (R3.2)
    public DateOnly Date { get; }
    public IncomeStatus Status { get; private set; }
    public DateOnly? ReceivedDate { get; private set; }

    public void MarkReceived(DateOnly receivedDate);     // R3.3
}
```

### Expenses

```csharp
public enum ExpenseStatus { Pending, Partial, Paid }

public sealed class ExpensePayment            // inmutable (T7)
{
    public Guid Id { get; }
    public Money Value { get; }               // > 0
    public DateOnly Date { get; }
}

public sealed class Expense
{
    public Guid Id { get; }
    public Money Budgeted { get; }            // > 0 (R4.3)
    public DateOnly Date { get; }
    public IReadOnlyList<ExpensePayment> Payments { get; }

    public Money TotalPaid();
    public Money Outstanding();               // max(0, Budgeted - TotalPaid) (R4.4)
    public ExpenseStatus Status();            // derivado (R4.2)
    public void AddPayment(ExpensePayment payment);
}
```

### Savings

```csharp
public sealed class SavingsTransaction        // inmutable (T7)
{
    public Guid Id { get; }
    public Money Value { get; }               // > 0
    public DateOnly Date { get; }
    public bool IsContribution { get; }       // true = aporte, false = retiro
    public Guid? GoalId { get; }              // null = ahorro general
}

public sealed class SavingsGoal
{
    public Guid Id { get; }
    public Money Target { get; }
    public Money CurrentBalance(IEnumerable<SavingsTransaction> txns);  // Σaportes-Σretiros
    public decimal ProgressPercent(IEnumerable<SavingsTransaction> txns); // R6.5
}
```

Las validaciones de aporte ≤ disponible (R6.2) y retiro ≤ saldo (R6.3) viven en
el servicio que arma el movimiento con el contexto del periodo, porque dependen
de los indicadores del periodo (dinero disponible), no solo del objetivo.

### Credits

```csharp
public enum CreditStatus { Active, Paid }
public enum InstallmentStatus { Open, Paid, Cancelled }

public sealed class CreditInstallment
{
    public int Number { get; }
    public Money Amount { get; private set; }     // recalculable si está Open
    public DateOnly DueDate { get; }
    public InstallmentStatus Status { get; private set; }
}

public sealed class CreditPayment             // inmutable (T7)
{
    public Guid Id { get; }
    public Money Value { get; }
    public DateOnly Date { get; }
}

public sealed class Credit
{
    public Guid Id { get; }
    public Money OriginalValue { get; }
    public Money DownPayment { get; }
    public IReadOnlyList<CreditInstallment> Installments { get; }
    public IReadOnlyList<CreditPayment> Payments { get; }
    public CreditStatus Status { get; private set; }

    public Money OutstandingBalance();        // Original - Down - Σpagos (R7.2)
    public void RegisterPayment(CreditPayment payment);   // R7.4..R7.10
}
```

**Generación del calendario (R7.1):** se divide el monto financiado
(`Original − Down`) entre el número de cuotas, redondeando cada una a los
decimales de la moneda; la última cuota = financiado − Σ(cuotas anteriores).
Garantiza suma exacta (CP-04, CP-15).

**Recálculo tras un pago (R7.5):** se descuenta el pago del saldo, se identifica
la primera cuota abierta, y el saldo restante se reparte en partes iguales entre
las cuotas abiertas, con la última absorbiendo el residuo. Los pagos registrados
no se tocan (R7.6). Si el saldo llega a 0, el crédito pasa a *Paid* y las cuotas
abiertas se cancelan (R7.8). Un pago no puede exceder el saldo (R7.4, R7.10). Si
en la última cuota se paga de menos, se agrega una cuota extra con el faltante
(R7.7).

### Engine

```csharp
public sealed record PeriodIndicators
{
    public Money ReceivedIncome { get; init; }      // RN-F02
    public Money PaidExpense { get; init; }         // RN-F03 (incluye cuotas de crédito)
    public Money CreditPayments { get; init; }      // RN-F05 (subconjunto, trazabilidad)
    public Money OutstandingObligations { get; init; } // RN-F04
    public Money CarryOver { get; init; }           // RN-P05 / D-03
    public Money SavingsContributions { get; init; }// RN-F06
    public Money SavingsWithdrawals { get; init; }
    public Money CashOnHand { get; init; }          // RN-F07
    public Money Reserved { get; init; }            // RN-F08
    public Money FreeMoney { get; init; }           // RN-F09 (puede ser negativo)
    public decimal BudgetConsumption { get; init; } // RN-F14
}

public sealed record EngineInput        // todo lo que necesita el motor para un periodo
{
    public FinancialPeriod Period { get; init; }
    public Money CarryOver { get; init; }
    public IReadOnlyList<IncomeTransaction> Incomes { get; init; }
    public IReadOnlyList<Expense> Expenses { get; init; }
    public IReadOnlyList<SavingsTransaction> Savings { get; init; }
    public IReadOnlyList<CreditInstallment> Installments { get; init; }
    public IReadOnlyList<CreditPayment> CreditPayments { get; init; }
}

public static class FinancialEngine
{
    public static PeriodIndicators Compute(EngineInput input);   // R5.*
    public static Money NetWorth(/* caja acumulada, ahorro, deuda */); // RN-F12
    public static Money PendingDebt(/* créditos activos, obligaciones vencidas */); // RN-F11
}
```

El motor filtra cada movimiento por su fecha contra el rango del periodo
(RN-M02), nunca cuenta un pago de crédito dos veces (R8.2), y expone
`CreditPayments` solo para trazabilidad (R8.3). `BudgetConsumption` devuelve 0
cuando el denominador es 0 (R5.7).

## Modelo de errores

Todas las validaciones de regla lanzan `DomainException` o un subtipo. Los
mensajes son neutrales (sin texto de UI): la capa de aplicación los traduce a
`Problem Details` más adelante. El dominio no referencia archivos de traducción.

## Estrategia de pruebas

- **Framework:** xUnit sobre .NET 10. Un archivo de pruebas por agregado
  (`MoneyTests`, `PeriodTests`, `ExpenseTests`, `CreditTests`, `EngineTests`…).
- **Casos de aceptación del SRS como pruebas nombradas:** cada CP-xx aplicable es
  un `[Fact]` con el nombre del caso (p. ej. `Cp01_ReceivedIncomeMinusPaid`),
  usando los valores exactos del SRS.
- **Pruebas de propiedad para dinero:** la suma de cuotas = monto financiado para
  distintos montos y conteos (RN-C03); el redondeo no crea ni pierde dinero
  (RN-M07).
- **Cobertura:** objetivo ≥ 90 % en Domain (RNF-MAN-03), medida con
  `dotnet test --collect:"XPlat Code Coverage"` (coverlet).
- Las pruebas no tocan red, disco ni reloj; las fechas y los `Guid` se inyectan.

## Trazabilidad requisito → componente

| Requisito | Componentes principales |
|---|---|
| R1 Dinero | `Money`, `Currency` |
| R2 Periodo | `FinancialPeriod`, `PeriodCalendar`, `PeriodSettings` |
| R3 Ingresos | `IncomeTransaction` |
| R4 Gastos | `Expense`, `ExpensePayment` |
| R5 Motor | `FinancialEngine`, `PeriodIndicators`, `EngineInput` |
| R6 Ahorro | `SavingsGoal`, `SavingsTransaction`, servicio de validación |
| R7 Créditos | `Credit`, `CreditInstallment`, `CreditPayment` |
| R8 Pago de crédito como gasto | `FinancialEngine` (RN-F03/F05) |
| R9 Deuda / patrimonio | `FinancialEngine.PendingDebt`, `FinancialEngine.NetWorth` |
| R10 Pureza e invariantes | `DomainException` + subtipos, estructura de proyecto |
