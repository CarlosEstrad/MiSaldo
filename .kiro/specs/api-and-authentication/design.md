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
| A8 | Rate limiting con el middleware nativo de ASP.NET Core; política estricta en `/auth/*`. | RNF-SEG-07. |
| A9 | Verificación de correo y recuperación: se genera un token con expiración; en F-actual el envío SES no existe, el token se expone por un canal de desarrollo (log/entorno de test) para poder probar el flujo. | Alcance sin SES; flujo verificable. |
| A10 | Montos en DTOs como `string` decimal; conversión a `Money` en Application. | RNF-DAT-02. |
| A11 | OpenAPI con el soporte nativo de .NET 10 (`Microsoft.AspNetCore.OpenApi`). | RNF-MAN-06. |

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
- Paginación en listados (page/pageSize) donde aplique.

## Error Handling

| Situación | HTTP |
|---|---|
| Entrada inválida (validación) | 400 |
| No autenticado / token inválido | 401 |
| Correo no verificado en ruta financiera | 403 |
| Recurso de otro usuario o inexistente | 404 |
| Conflicto (correo duplicado, categoría duplicada, periodo solapado) | 409 |
| Invariante de dominio violada no cubierta arriba | 422 |
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
