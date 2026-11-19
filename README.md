# MiSaldo

Aplicación web y móvil para la gestión inteligente de las finanzas personales. Ayuda a las personas a administrar sus ingresos y gastos por periodos de pago, planificar obligaciones, controlar créditos y desarrollar el hábito del ahorro.

> **Estado:** en desarrollo (fase F1). Están implementados el dominio (motor financiero), la persistencia (PostgreSQL con EF Core, aislamiento por usuario con filtro global y Row Level Security) y la API REST con autenticación propia: registro, verificación de correo, sesiones, perfil, configuración, categorías, periodos, ingresos, gastos con pagos y dashboard. Falta el frontend.

## Pilares

| Pilar | Descripción |
|---|---|
| **Control** | Saber cuánto ingresa, cuánto se gasta y cuánto queda disponible. |
| **Planificación** | Distribuir el sueldo antes de gastarlo. |
| **Seguimiento** | Visualizar los gastos del periodo actual y los acumulados del año. |
| **Ahorro** | Establecer metas y separar dinero para objetivos personales. |
| **Créditos** | Organizar compras financiadas y ajustar las cuotas según los pagos reales. |

## Indicadores del dashboard

- **Dinero en caja:** dinero recibido menos pagos y transferencias de ahorro realizados.
- **Dinero libre:** dinero en caja menos obligaciones y reservas pendientes.
- **Ahorro acumulado:** dinero separado en ahorro, descontando retiros.
- **Deuda pendiente:** saldo por pagar de créditos y obligaciones.

## Reglas financieras clave

- Lo presupuestado no es lo recibido, y lo pendiente no es lo pagado.
- El ahorro es una transferencia entre fondos propios, no un gasto.
- Un pago de crédito es un gasto del periodo: sale de caja y reduce la deuda, y se cuenta una sola vez.
- Los pagos son un historial inmutable: al abonar más o menos de la cuota solo se recalculan las cuotas futuras.
- Cambiar la periodicidad (mensual, quincenal, semanal) no altera los periodos históricos.
- Todo el dinero se maneja con `decimal`; los decimales dependen de la moneda (COP = 0, USD = 2). La API recibe y devuelve los montos como texto decimal.
- Los periodos los crea el usuario; un periodo cerrado conserva sus cifras y un periodo vencido debe cerrarse antes de seguir.

## Arquitectura

Clean Architecture: las dependencias apuntan hacia el centro y el motor financiero vive solo en el backend.

```
src/
├── MiSaldo.Domain/          # Entidades, cuentas y motor financiero (C# puro)
├── MiSaldo.Application/     # Casos de uso, DTOs, validaciones
├── MiSaldo.Infrastructure/  # EF Core + PostgreSQL, RLS, auditoría, JWT y Argon2id
└── MiSaldo.Api/             # API REST /api/v1, Problem Details, OpenAPI
tests/
├── MiSaldo.Domain.Tests/         # Pruebas xUnit de las reglas financieras
├── MiSaldo.Application.Tests/    # Validaciones y tipos de la capa de aplicación
├── MiSaldo.Infrastructure.Tests/ # Pruebas de persistencia (Testcontainers)
└── MiSaldo.Api.Tests/            # Pruebas HTTP de extremo a extremo (Testcontainers)
web/                         # Angular + Ionic/Capacitor          (pendiente)
infra/                       # Infraestructura como código        (pendiente)
```

## Stack previsto

| Capa | Tecnología |
|---|---|
| Web y móvil | Angular + Ionic/Capacitor |
| Backend | ASP.NET Core Web API (.NET LTS) |
| Datos | Entity Framework Core + PostgreSQL (Amazon RDS) |
| Autenticación | Propia: JWT HS256, Argon2id y refresh tokens rotativos (Cognito queda como migración futura, ver SRS v0.3) |
| Despliegue | AWS App Runner, S3 + CloudFront |
| Reportes | QuestPDF, ClosedXML, CSV |
| Pruebas | xUnit, Testcontainers, Playwright |

## Ejecutar las pruebas

Requiere el SDK de .NET 10 (versión fijada en `global.json`).

Pruebas de dominio (C# puro, sin dependencias externas):

```bash
dotnet test tests/MiSaldo.Domain.Tests
```

Pruebas de persistencia (**requieren Docker corriendo**): usan Testcontainers,
que levanta un PostgreSQL real en un contenedor, aplica las migraciones y verifica
el aislamiento por usuario (RLS), las restricciones y la idempotencia. La primera
corrida descarga la imagen `postgres:17-alpine`.

```bash
dotnet test tests/MiSaldo.Infrastructure.Tests
```

Pruebas de la API (**requieren Docker corriendo**): levantan la API en memoria
sobre un PostgreSQL real y la ejercitan por HTTP.

```bash
dotnet test tests/MiSaldo.Api.Tests
```

Toda la suite:

```bash
dotnet test
```

## Ejecutar la API

Necesita una base PostgreSQL con las migraciones aplicadas (ver abajo) y un rol
de aplicación **sin** `BYPASSRLS`. La configuración viene de variables de entorno
(ver `.env.example`); la API no arranca sin la cadena de conexión ni la clave de
firma de los JWT.

```bash
export ConnectionStrings__MiSaldo="Host=localhost;Port=5432;Database=misaldo;Username=misaldo_app;Password=..."
export JWT__SIGNING_KEY="$(openssl rand -base64 48)"
export ASPNETCORE_ENVIRONMENT=Development   # habilita /openapi/v1.json
dotnet run --project src/MiSaldo.Api
```

Rutas principales, todas bajo `/api/v1`:

| Ruta | Acceso |
|---|---|
| `/auth/register`, `/auth/verify-email`, `/auth/login`, `/auth/refresh`, `/auth/logout`, `/auth/forgot-password`, `/auth/reset-password` | Pública, con límite estricto de solicitudes |
| `/profile`, `/settings` | Token válido |
| `/categories`, `/periods`, `/incomes`, `/expenses` (con `/expenses/{id}/payments` e `Idempotency-Key`), `/dashboard` | Token válido y correo verificado |

Los errores siguen el formato Problem Details con un `code` estable y el
`correlationId` de la petición. Todavía no hay envío de correo: los tokens de
verificación y recuperación solo quedan en la memoria del proceso (las pruebas
los leen de ahí), así que al ejecutar la API a mano no hay forma de verificar un
correo.

### Migraciones de base de datos

Se usa la herramienta local `dotnet-ef` (fijada en `dotnet-tools.json`). La
cadena de conexión se toma de la variable `MISALDO_DB` (ver `.env.example`); el
rol de la aplicación **no** debe tener `BYPASSRLS` para que la seguridad por fila
aplique.

```bash
dotnet tool restore
dotnet ef database update --project src/MiSaldo.Infrastructure
```

## Hoja de ruta

- [x] Motor de caja y dinero libre
- [x] Créditos sin intereses con recálculo de cuotas
- [x] Modelo de datos en PostgreSQL y aislamiento por usuario
- [x] Autenticación, perfil y periodos
- [x] Ingresos, gastos y dashboard (API)
- [ ] Frontend web (Angular)
- [ ] Historial, auditoría consultable y privacidad (exportar y eliminar cuenta)
- [ ] Envío de correos (verificación y recuperación)
- [ ] Ahorro, objetivos y créditos en la API (F2)
- [ ] Reportes (PDF, Excel, CSV)
- [ ] Idiomas adicionales (inglés, chino, hindi, árabe con RTL)
- [ ] Aplicación móvil Android e iOS

## Alcance de usuarios

Por ahora la aplicación es solo para **mayores de 18 años**. El modelo de datos contempla evolucionar a cuentas familiares en el futuro.

## Licencia

Por definir.
