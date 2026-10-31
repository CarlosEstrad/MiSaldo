# Diseño — Persistencia e aislamiento por usuario

## Visión general

`MiSaldo.Infrastructure` es la capa que persiste el dominio en PostgreSQL con
Entity Framework Core y Npgsql. Depende de `MiSaldo.Domain`; el dominio nunca
depende de ella (RNF-MAN-01). El aislamiento por usuario se construye en **dos
barreras independientes**, como exige el SRS (RNF-SEG-04):

1. **Filtro global de EF Core** por `UserId` (barrera de aplicación).
2. **Row Level Security de PostgreSQL** por `UserId` de sesión (barrera de base
   de datos), que protege aun si la primera se omitiera.

El principio rector: los saldos no se guardan; se recuperan los movimientos y el
motor del dominio los calcula (RNF-DAT-07). La base guarda hechos (movimientos),
no resultados.

### Estructura de la solución (lo que se agrega)

```
src/
└── MiSaldo.Infrastructure/
    ├── MiSaldo.Infrastructure.csproj   # net10.0; EF Core, Npgsql; ref a Domain
    ├── Persistence/
    │   ├── MiSaldoDbContext.cs          # DbSets, filtro global, SaveChanges
    │   ├── ICurrentUser.cs              # provee el UserId del usuario actual
    │   ├── Configurations/              # IEntityTypeConfiguration por entidad
    │   │   ├── UserConfiguration.cs
    │   │   ├── FinancialPeriodConfiguration.cs
    │   │   ├── ExpenseConfiguration.cs
    │   │   └── ...
    │   ├── Conversions/
    │   │   └── MoneyConverter.cs        # Money <-> numeric(18,2) + currency
    │   ├── Interceptors/
    │   │   ├── AuditingInterceptor.cs   # CreatedAt/UpdatedAt + AuditLog
    │   │   └── RlsConnectionInterceptor.cs  # fija el UserId de sesión (RLS)
    │   └── Migrations/                  # migraciones versionadas (incluye SQL de RLS)
tests/
└── MiSaldo.Infrastructure.Tests/
    ├── MiSaldo.Infrastructure.Tests.csproj  # xUnit, Testcontainers.PostgreSql
    └── ...                               # pruebas de integración (CP-07, CP-12, ...)
```

## Decisiones técnicas

| # | Decisión | Motivo |
|---|---|---|
| T1 | EF Core con Npgsql; una `IEntityTypeConfiguration<T>` por entidad. | Mantenibilidad; configuración aislada del `DbContext` (RNF-MAN-01). |
| T2 | `Money` se persiste con dos columnas: `*_amount numeric(18,2)` y, donde la moneda pueda variar, `*_currency`. En F1 la moneda es la del usuario; se guarda a nivel de `UserSettings` y se reconstruye `Money` en materialización. | RNF-DAT-02; evita que el dominio dependa de EF Core (conversión vive aquí). |
| T3 | Filtro global `HasQueryFilter(e => e.UserId == _currentUser.UserId)` en toda entidad financiera. | Primera barrera (RNF-SEG-04). |
| T4 | `ICurrentUser` inyectable expone `UserId`. En pruebas se controla; en la API lo llena el token (fuera de este spec). | El `UserId` nunca viene de la consulta (RF-AUT-09). |
| T5 | RLS: cada tabla financiera con `ENABLE ROW LEVEL SECURITY` y política `USING (user_id = current_setting('app.user_id')::uuid)`. Un interceptor de conexión ejecuta `SET app.user_id` por unidad de trabajo. | Segunda barrera en la base (RNF-SEG-04, RNF-SEG-05, CP-07). |
| T6 | La app se conecta con un rol **sin** `BYPASSRLS`. Las migraciones corren con un rol administrador. | Que la política RLS realmente aplique a la app. |
| T7 | Concurrencia optimista con columna `xmin` de PostgreSQL como token de concurrencia. | Nativo en Npgsql; sin columna extra (RNF-DAT-04). |
| T8 | `CreatedAt`/`UpdatedAt` y escritura de `AuditLog` mediante un `SaveChangesInterceptor`. | Centraliza auditoría y marcas de tiempo (RF-HIS-04). |
| T9 | Idempotencia de pagos: tabla/columna de `IdempotencyKey` con índice único por (usuario, clave); el pago se inserta dentro de una transacción. | Doble clic no duplica (RNF-DAT-03, CP-12). |
| T10 | Restricción de no solapamiento de periodos con `EXCLUDE USING gist` sobre `(user_id, daterange(start,end))`. | La base impide periodos cruzados (RF-PRD-03). |
| T11 | Unicidad de categoría por usuario sin mayúsculas con índice único sobre `(user_id, lower(name))`. | RF-CAT-04. |
| T12 | Pruebas de integración con Testcontainers.PostgreSql; migraciones aplicadas al contenedor antes de la suite. | RLS y restricciones solo se prueban contra PostgreSQL real. |

## Mapeo del dominio a tablas

El dominio ya define las entidades; aquí se mapean. Nombres de tabla en
`snake_case`.

| Dominio | Tabla | Notas de mapeo |
|---|---|---|
| `User` (nuevo en infra/futuro) | `users` | `external_id` (Cognito), `account_type`, `guardian_user_id` nulo |
| `UserProfile` | `user_profiles` | FK a `users`; fecha de nacimiento no editable |
| `UserSettings` | `user_settings` | moneda principal, idioma, tema, config de periodos |
| `ConsentRecord` | `consent_records` | versión y fecha de aceptación de política/términos |
| `FinancialPeriod` | `financial_periods` | `type`, `start_date`, `end_date`, `is_closed`, EXCLUDE de no solape |
| `ExpenseCategory` | `expense_categories` | único `(user_id, lower(name))`, `is_archived` |
| `IncomeTransaction` | `income_transactions` | `status`, `received_date` nulo, `value_amount` |
| `Expense` | `expenses` | `budgeted_amount`, `type`; pagos en tabla aparte |
| `ExpensePayment` | `expense_payments` | FK a `expenses`, `idempotency_key`, `value_amount` |
| `AuditLog` | `audit_logs` | quién, cuándo, valor anterior/nuevo; sin RLS por usuario (ver nota) |
| `AccountDeletionRecord` | `account_deletion_records` | motivo y autor; sin datos personales (D-12) |

> **Nota sobre `Expense` y sus pagos:** `Expense` del dominio mantiene una lista
> privada `_payments` con un `IReadOnlyList` expuesto. Se mapea como entidad
> propietaria con colección; EF Core accede al campo de respaldo. El estado y la
> obligación pendiente NO se almacenan: son métodos del dominio (RNF-DAT-07).

> **`User` en el dominio:** hoy el dominio no tiene una clase `User` (su alcance
> era el motor). Este spec introduce `User`, `UserProfile`, `UserSettings`,
> `ConsentRecord` y `AccountDeletionRecord`, decidiendo si viven como entidades
> del dominio (si tienen reglas) o como modelos de infraestructura (si son solo
> datos). Se resuelve en la fase de diseño de la tarea correspondiente.

## Las dos barreras de aislamiento

### Barrera 1 — filtro global (aplicación)

En `OnModelCreating`, cada entidad financiera recibe:

```csharp
builder.Entity<Expense>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
```

`_currentUser` es un `ICurrentUser` inyectado. Toda consulta LINQ queda filtrada
sin que el llamador lo pida. Al crear entidades, un interceptor o el propio
`SaveChanges` asigna `UserId` desde `_currentUser` (RF-AUT-09).

### Barrera 2 — RLS (base de datos)

Una migración ejecuta SQL crudo por cada tabla financiera:

```sql
ALTER TABLE expenses ENABLE ROW LEVEL SECURITY;
CREATE POLICY expenses_user_isolation ON expenses
  USING (user_id = current_setting('app.user_id')::uuid);
```

Un `DbConnectionInterceptor` ejecuta `SET app.user_id = '<guid>'` tras abrir la
conexión, tomando el valor de `ICurrentUser`. La app usa un rol sin `BYPASSRLS`.
Resultado: aun si el filtro de EF Core se omitiera (consulta cruda, bug), la base
no devuelve filas de otro usuario (CP-07).

## Modelo de errores y seguridad

- Un recurso de otro usuario se comporta como inexistente: el filtro y la RLS lo
  excluyen, y la capa superior responde 404/403 sin revelar su existencia
  (RNF-SEG-05). La capa de API hace ese mapeo (fuera de este spec).
- Las operaciones multi-tabla van en transacción (RNF-DAT-01).
- Los secretos (cadena de conexión) vienen de variables de entorno / Secrets
  Manager; nunca en el repo (RNF-SEG-08). Se incluye `.env.example` sin valores.

## Estrategia de pruebas

- **Testcontainers.PostgreSql** levanta una PostgreSQL real por suite; se aplican
  las migraciones y se corre con un rol sin `BYPASSRLS`.
- **CP-07:** crear datos de usuario A y de usuario B; con la sesión fijada a A,
  verificar que una consulta (incluida una cruda que ignore el filtro de EF Core)
  no ve datos de B.
- **CP-12:** enviar el mismo pago dos veces con la misma clave de idempotencia;
  verificar una sola fila.
- **Restricciones:** periodos solapados del mismo usuario rechazados (RF-PRD-03);
  categoría duplicada por usuario rechazada (RF-CAT-04); fin < inicio rechazado.
- **CP-16:** dos contextos (dos "dispositivos") sobre la misma base ven los
  mismos datos.
- Las pruebas de dominio existentes siguen sin tocar la base.

## Trazabilidad requisito → componente

| Requisito | Componentes principales |
|---|---|
| R1 Infra/DbContext | `MiSaldoDbContext`, `Configurations/*`, `MoneyConverter` |
| R2 Identidad por usuario | campos base en configuraciones; `users` |
| R3 Filtro global | `HasQueryFilter`, `ICurrentUser`, `SaveChanges` |
| R4 RLS | migración SQL de RLS, `RlsConnectionInterceptor`, rol sin BYPASSRLS |
| R5 Restricciones | `CHECK`, FKs, EXCLUDE de periodos, índice único de categorías |
| R6 Concurrencia/auditoría | token `xmin`, `AuditingInterceptor`, migraciones |
| R7 Idempotencia | `idempotency_key` único, transacción |
| R8 Multi-dispositivo | ausencia de saldos persistidos; pruebas CP-16 |
| R9 Pruebas de integración | `MiSaldo.Infrastructure.Tests`, Testcontainers |
