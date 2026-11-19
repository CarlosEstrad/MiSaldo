# Diseño — API REST y autenticación propia (JWT)

## Overview

Dos capas nuevas sobre lo ya construido:

- **`MiSaldo.Application`** — casos de uso que orquestan dominio + persistencia,
  con DTOs y validación. No conoce HTTP. Depende de `MiSaldo.Domain` y, para
  persistir, de una abstracción que implementa `MiSaldo.Infrastructure`.
- **`MiSaldo.Api`** — ASP.NET Core Web API en `/api/v1`: controladores delgados,
  autenticación JWT, middleware que resuelve `ICurrentUser` desde el token,
  Problem Details, rate limiting y OpenAPI.

La **autenticación es propia del backend** (no Cognito): el backend guarda el
hash de la contraseña y los refresh tokens en la base, y firma/valida sus
propios JWT. El access token es stateless. Toda la auth vive detrás de
interfaces (`ITokenService`, `IPasswordHasher`, `ICurrentUser`) para poder
cambiar a Cognito más adelante sin tocar los casos de uso.

### Estructura de la solución (lo que se agrega)

```
src/
├── MiSaldo.Application/
│   ├── MiSaldo.Application.csproj     # ref a Domain; sin ASP.NET
│   ├── Abstractions/                  # IUnitOfWork, repos, IClock, ITokenService...
│   ├── Accounts/                      # Register, VerifyEmail, Login, Refresh...
│   ├── Profile/ · Settings/ · Categories/ · Periods/ · Incomes/ · Expenses/
│   ├── Dashboard/                     # GetDashboard (usa FinancialEngine)
│   └── Common/                        # DTOs, Result, errores de aplicación
└── MiSaldo.Api/
    ├── MiSaldo.Api.csproj             # ref a Application e Infrastructure
    ├── Program.cs                     # DI, auth, rate limit, OpenAPI, pipeline
    ├── Auth/                          # JwtTokenService, PasswordHasher, CurrentUser
    ├── Controllers/                   # v1: Auth, Profile, Settings, Categories...
    └── Common/                        # ProblemDetails mapping, correlation id
tests/
└── MiSaldo.Api.Tests/                 # WebApplicationFactory + Testcontainers
```

## Architecture

| # | Decisión | Motivo |
|---|---|---|
| A1 | `MiSaldo.Application` no referencia ASP.NET ni EF Core; usa abstracciones. | Mantiene la regla de dependencias (RNF-MAN-01). |
| A2 | Casos de uso como handlers simples inyectables (sin framework de mediación por ahora). | Menos peso; se puede introducir MediatR después si hace falta. |
| A3 | Persistencia tras un `IUnitOfWork` + repositorios en Application, implementados en Infrastructure sobre el `DbContext`. | La API/Application no dependen de EF Core directamente. |
| A4 | Auth propia: `IPasswordHasher` (Argon2id), `ITokenService` (JWT firmado HS256/secreto). | No hay AWS; testeable localmente (R-intro). |
| A5 | Access token stateless (claims: sub=UserId, email_verified, exp). Refresh token opaco, su **hash** se guarda en la base y rota en cada uso. | RNF-SEG-02; el access token nunca se persiste. |
| A6 | `ICurrentUser` en la API se llena desde el claim `sub` del JWT por petición (scoped). | RF-AUT-09; reusa el aislamiento ya construido. |
| A7 | Errores de dominio/aplicación → Problem Details vía un middleware/exception handler. | RNF-MAN-06, RNF-USA-05. |
| A8 | Rate limiting con el middleware nativo de ASP.NET Core, ventana fija por minuto: 10 solicitudes por IP en `/auth/*` y 120 por usuario (o IP si es anónimo) en el resto; 429 con Problem Details y `Retry-After`. Configurable en `RateLimiting`. | RNF-SEG-07. |
| A9 | Verificación de correo y recuperación: se genera un token con expiración; en F-actual el envío SES no existe, el token se expone por un canal de desarrollo (log/entorno de test) para poder probar el flujo. | Alcance sin SES; flujo verificable. |
| A10 | Montos en DTOs como `string` decimal; conversión a `Money` en Application. | RNF-DAT-02. |
| A11 | OpenAPI con el soporte nativo de .NET 10 (`Microsoft.AspNetCore.OpenApi`), servido en `/openapi/v1.json` solo en Development. | RNF-MAN-06. |
| A12 | Autorización por defecto: una política fallback exige token en toda ruta; solo `/auth/*`, `/health` y OpenAPI son anónimas. Las rutas financieras (`/categories`, `/periods`, `/incomes`, `/expenses`, `/dashboard`) usan la política `VerifiedEmail` (claim `email_verified = true`, 403 si falta); `/profile` y `/settings` solo exigen sesión. | RF-AUT-03, RF-AUT-08. |
| A13 | Los enums viajan por nombre en el JSON (`"Monthly"`, `"Dark"`) y los números se rechazan. | Contrato legible; solo entran valores conocidos. |
| A14 | Al cerrar un periodo se calcula su saldo final con `FinancialEngine` y se guardan `ClosingBalance` y `CarriedOver` en el periodo. El saldo inicial de un periodo es lo que arrastró el periodo anterior cerrado. Los periodos se cierran en orden. | RN-P04 (un cerrado conserva sus cifras), RN-P05, CP-14. |
| A15 | Reglas de escritura por periodo: ningún movimiento con fecha dentro de un periodo cerrado se crea, edita ni elimina (422 `period.closed`), y nada posterior a un periodo vencido cambia hasta cerrarlo (422 `period.overdue_pending`). | RN-P04, RF-PRD-06b, CP-21. |
| A16 | Los pagos son inmutables: no se editan ni eliminan, y un gasto con pagos no se elimina (409 `expense.has_payments`). Prevalece RN-M08 sobre RF-GAS-06 hasta que existan los ajustes. | RN-M08. |
| A17 | Idempotencia de pagos con el encabezado `Idempotency-Key`: un reenvío devuelve el primer pago (200 + `Idempotent-Replayed: true`); la misma clave con otro monto, fecha o gasto responde 409. Una carrera la decide el índice único. | RNF-DAT-03, CP-12. |
| A18 | Los montos de entrada son texto decimal, positivos y con no más decimales que la moneda (sin redondeo silencioso). | RNF-DAT-02, RN-M07. |
| A19 | Los `record` con contraseñas, tokens o correo redefinen `ToString()` (texto redactado), porque los frameworks registran los argumentos; las excepciones inesperadas se registran como `Error` y el cliente recibe un 500 genérico. | RNF-SEG-10, RNF-MAN-05. |

## Data Models

### Entidades/datos nuevos (persistencia)

- **UserCredential** — `UserId`, `PasswordHash`, `EmailVerified`, contadores de
  intentos fallidos y `LockedUntil`. (Se añade al modelo de persistencia.)
- **RefreshToken** — `UserId`, `TokenHash`, `ExpiresAt`, `ConsumedAt`,
  `ReplacedByTokenHash`. Rotación y revocación.
- **OneTimeToken** — para verificación de correo y recuperación: `UserId`,
  `Purpose`, `TokenHash`, `ExpiresAt`, `ConsumedAt`.

Estas entidades llevan su migración; NO llevan `UserId` como filtro RLS de datos
financieros (son de cuenta), pero sí se consultan por usuario.

### Cambios a entidades existentes

- **FinancialPeriod** — `ClosingBalance` y `CarriedOver` (`numeric(18,2)`, nulos
  mientras está abierto), fijados al cerrar (migración `PeriodClosingFigures`).
- **Montos** — cada `Money` se guarda en dos columnas, `*_amount` y
  `*_currency`, mapeado como tipo complejo de EF Core (migración
  `MovementCurrency`). Antes se asumía COP para todo.
- **Expense / IncomeTransaction** — métodos `Update(...)` para la edición; los
  pagos no cambian.

### Flujo

1. **Registro:** valida edad (dominio `User.Register`), política de contraseña,
   correo único; crea `User`, `UserProfile`, `UserSettings`, `ConsentRecord`,
   `UserCredential` (hash), categorías iniciales, y un `OneTimeToken` de
   verificación. Todo en una transacción.
2. **Verificación:** consume el token, marca `EmailVerified`.
3. **Login:** verifica hash; si `EmailVerified` es falso, rechaza el acceso a
   rutas financieras; emite access + refresh. Cuenta intentos fallidos y bloquea.
4. **Refresh:** valida el refresh token por su hash, rota (marca el viejo
   consumido, emite uno nuevo).
5. **Logout:** marca el refresh token consumido/revocado.
6. **Recuperación:** emite `OneTimeToken` de reset; respuesta uniforme exista o
   no el correo; al resetear, actualiza hash y revoca sesiones.

### JWT

- Firmado con secreto HS256 desde `JWT__SIGNING_KEY` (entorno/secreto), issuer y
  audience configurables. Vida del access token 30 min por defecto (configurable
  15–60). Claims mínimos: `sub` (UserId), `email_verified`, `exp`, `iss`, `aud`.
- La API valida el token con los parámetros estándar; el middleware de auth puebla
  `ICurrentUser.UserId` desde `sub`.

## Components and Interfaces

- Base `/api/v1`. Rutas públicas: `/auth/register`, `/auth/verify-email`,
  `/auth/login`, `/auth/refresh`, `/auth/forgot-password`, `/auth/reset-password`.
  Rutas privadas (Bearer): `/profile`, `/settings`, `/categories`, `/periods`,
  `/incomes`, `/expenses` (+ `/expenses/{id}/payments`), `/dashboard`.
- Respuestas de error en Problem Details; validación de modelo y de dominio
  mapeada a 400/409/404 según el caso. Un recurso de otro usuario se comporta
  como inexistente (404) gracias al filtro + RLS (CP-07).
- Los listados de ingresos y gastos se filtran por `periodId`, por `from` y `to`,
  o por el periodo actual; no hay paginación todavía.
- `GET /periods/current` y `GET /dashboard` sin periodo para hoy responden 404
  `period.none_for_date` y no crean nada (CP-20); `GET /periods/proposal`
  sugiere el rango sin guardarlo.

## Error Handling

| Situación | HTTP |
|---|---|
| Entrada inválida (validación) | 400 |
| No autenticado / token inválido | 401 |
| Correo no verificado en ruta financiera | 403 |
| Recurso de otro usuario o inexistente | 404 |
| Conflicto (correo duplicado, categoría duplicada, periodo solapado, periodo ya cerrado, gasto con pagos, clave de idempotencia reutilizada) | 409 |
| Regla de periodos (cerrado, vencido pendiente, no iniciado, anterior abierto) o invariante de dominio | 422 |
| Rate limit excedido | 429 |

Las excepciones de dominio (`DomainException` y subtipos) se traducen en el
middleware a estos códigos; nunca se devuelven como 500.

## Testing Strategy

- **`MiSaldo.Api.Tests`** con `WebApplicationFactory<Program>` y una PostgreSQL de
  Testcontainers (misma infraestructura de pruebas que persistencia); las
  migraciones se aplican al contenedor.
- **Auth end-to-end:** registro → verificación (consumiendo el token expuesto en
  desarrollo) → login → petición autorizada → refresh → logout.
- **Autorización:** ruta privada sin token → 401; usuario A no ve datos de B
  (CP-07) a través de la API.
- **Financiero:** crear periodo, ingreso y gasto con pago, leer dashboard y
  verificar caja/dinero libre (CP-01, CP-02).
- **Idempotencia:** doble POST de pago con la misma clave → un solo pago (CP-12).
- Las pruebas de dominio, persistencia y API conviven; las de dominio siguen sin
  tocar red ni base.

## Correctness Properties

Propiedades que deben cumplirse siempre, sin importar la entrada; cada una se
verifica con las pruebas de la sección *Testing Strategy*.

1. **Aislamiento por usuario:** para cualquier petición autenticada, solo se leen
   o modifican datos del `UserId` del token; un recurso de otro usuario responde
   404 como si no existiera (CP-07, RNF-SEG-05).
2. **Identidad solo desde el token:** el `UserId` nunca proviene de la ruta, la
   query ni el cuerpo; sin token válido no hay usuario y la ruta privada responde
   401 (RF-AUT-08, RF-AUT-09).
3. **Rutas privadas por defecto:** toda ruta exige token salvo las marcadas
   explícitamente como anónimas.
4. **Tokens de un solo uso:** un token de verificación o de reset se consume una
   sola vez; uno consumido o expirado siempre falla.
5. **Rotación de refresh tokens:** tras un refresh, el token anterior queda
   consumido y su reutilización es rechazada.
6. **Respuesta uniforme:** la recuperación de contraseña y el login con
   credenciales inválidas responden igual exista o no la cuenta, sin revelar su
   existencia.
7. **Secretos no expuestos:** contraseñas, hashes, tokens y la clave de firma
   nunca aparecen en respuestas ni en logs; los errores inesperados devuelven un
   500 genérico (RNF-SEG-08).
8. **Idempotencia de pagos:** repetir un pago con la misma clave de idempotencia
   produce un único pago (CP-12).
9. **Dinero exacto:** los montos viajan como cadenas decimales y nunca se
   convierten a punto flotante (RNF-DAT-02).
10. **Límite de solicitudes:** superado el límite de una ventana, las peticiones
    siguientes responden 429 hasta que la ventana se reinicia; en `/auth/*` el
    límite es más estricto (RNF-SEG-07).

## Trazabilidad requisito → componente

| Requisito | Componentes principales |
|---|---|
| R1 Application | `MiSaldo.Application`, handlers, DTOs, `IUnitOfWork` |
| R2–R6 Auth | `Register/VerifyEmail/Login/Refresh/Logout/Reset` handlers; `ITokenService`, `IPasswordHasher`; `UserCredential`, `RefreshToken`, `OneTimeToken` |
| R7 Autorización | middleware JWT, `CurrentUser`, rate limiting |
| R8 Perfil/Config | `Profile`, `Settings` controllers + handlers |
| R9 Categorías | `Categories` controller + handlers |
| R10 Periodos | `Periods` controller + handlers (usa reglas de dominio) |
| R11 Ingresos | `Incomes` controller + handlers |
| R12 Gastos/pagos | `Expenses` controller + handlers (idempotencia) |
| R13 Dashboard | `Dashboard` handler sobre `FinancialEngine` |
| R14 Errores/OpenAPI | middleware Problem Details, correlación, OpenAPI |
| R15 Pruebas | `MiSaldo.Api.Tests` (WebApplicationFactory + Testcontainers) |
