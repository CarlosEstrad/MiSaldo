# MiSaldo

[![CI](https://github.com/CarlosEstrad/MiSaldo/actions/workflows/ci.yml/badge.svg?branch=dev)](https://github.com/CarlosEstrad/MiSaldo/actions/workflows/ci.yml)

Aplicación web y móvil para la gestión inteligente de las finanzas personales. Ayuda a las personas a administrar sus ingresos y gastos por periodos de pago, planificar obligaciones, controlar créditos y desarrollar el hábito del ahorro.

> **Estado:** en desarrollo (fase F1). Están implementados el dominio (motor financiero), la persistencia (PostgreSQL con EF Core, aislamiento por usuario con filtro global y Row Level Security) y la API REST con autenticación propia: registro, verificación de correo, sesiones, perfil, configuración, categorías, periodos, ingresos, gastos con pagos y dashboard. Los correos de cuenta salen por SMTP y la sesión web usa una cookie `HttpOnly`. Falta el frontend.

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
sobre un PostgreSQL real y la ejercitan por HTTP. Las de correo envían por un
Mailpit real (imagen `axllent/mailpit`), leen el mensaje de su API y siguen el
enlace.

```bash
dotnet test tests/MiSaldo.Api.Tests
```

Toda la suite:

```bash
dotnet test
```

## Integración continua

`.github/workflows/ci.yml` corre en cada push a `dev` o `main` y en cada pull
request hacia ellas (RNF-MAN-04). No despliega nada y no usa secretos. Tiene tres
trabajos que corren en paralelo:

| Trabajo | Qué revisa | Falla si |
|---|---|---|
| `build-and-test` | Compila en Release, revisa las migraciones, corre **todas** las pruebas (incluidas las de PostgreSQL y Mailpit en Docker) y mide la cobertura. | Hay una advertencia o un error de compilación, falta una migración, falla una prueba, o la cobertura de líneas queda bajo 90 % en `MiSaldo.Domain` o bajo 70 % en `MiSaldo.Application` (RNF-MAN-03). |
| `dependencies` | Paquetes NuGet directos y transitivos contra los avisos de seguridad (RNF-SEG-11). | Hay una vulnerabilidad alta o crítica. Las bajas y moderadas solo se listan. |
| `secrets` | Todo el historial de git con gitleaks y `.gitleaks.toml` (RNF-SEG-08). | Aparece un posible secreto. La salida muestra archivo, línea y commit, nunca el valor. |

En la página de cada corrida, el resumen muestra:

- los totales de pruebas por proyecto y el nombre de cada prueba fallida;
- la cobertura medida frente a su mínimo;
- los paquetes vulnerables;
- los hallazgos de secretos, si los hay.

Los artefactos `test-results` (los `.trx`) y `coverage-report` (el informe HTML)
quedan 14 días.

### Reproducir cada revisión en local

El workflow usa los mismos scripts y comandos, así que un fallo se reproduce en el
equipo. Los scripts de `scripts/ci/` funcionan con PowerShell 7 (`pwsh`) y con
Windows PowerShell 5.1:

- En Windows PowerShell, ejecútalos con
  `powershell -ExecutionPolicy Bypass -File scripts/ci/<script>.ps1 ...`.
- En Linux o macOS, con `pwsh scripts/ci/<script>.ps1 ...`.

Antes de empezar, restaura las herramientas locales (`dotnet-ef` y ReportGenerator):

```bash
dotnet tool restore
```

**Compilación sin advertencias:**

```bash
dotnet build MiSaldo.slnx --configuration Release
```

**Migraciones pendientes.** No necesita base de datos. Si falla, crea la
migración con `dotnet ef migrations add <Nombre> --project src/MiSaldo.Infrastructure`.

```bash
dotnet ef migrations has-pending-model-changes --project src/MiSaldo.Infrastructure --startup-project src/MiSaldo.Infrastructure
```

**Pruebas, resumen y cobertura** (con Docker corriendo). Borra antes la carpeta
`TestResults`: los resultados de corridas anteriores se mezclarían con los nuevos.

```bash
dotnet test MiSaldo.slnx --settings coverage.runsettings --logger trx --results-directory TestResults
pwsh scripts/ci/Write-TestSummary.ps1 -ResultsDirectory TestResults
dotnet reportgenerator "-reports:TestResults/*/coverage.cobertura.xml" "-targetdir:coverage-report" "-reporttypes:Html;JsonSummary"
pwsh scripts/ci/Test-Coverage.ps1 -SummaryJson coverage-report/Summary.json
```

El informe queda en `coverage-report/index.html`, con las líneas sin cubrir de
cada clase. El pipeline compila en Release, que cuenta menos líneas que Debug; los
porcentajes cambian unas décimas.

**Dependencias vulnerables:**

```bash
dotnet restore MiSaldo.slnx
pwsh scripts/ci/Test-VulnerablePackages.ps1
```

Una vulnerabilidad alta o crítica ya rompe el `dotnet restore` con NU1903 o
NU1904. El mensaje nombra el paquete y el aviso. Para resolverla, actualiza el
paquete o, si es transitivo, agrega una referencia directa a una versión
corregida. Las bajas y moderadas aparecen como avisos NU1901 y NU1902.

**Secretos** (con Docker):

```bash
docker run --rm -v "$PWD:/repo" ghcr.io/gitleaks/gitleaks:v8.30.1 git /repo --config /repo/.gitleaks.toml --redact --no-banner --verbose
```

En PowerShell, cambia `$PWD` por `${PWD}`.

**Sintaxis del workflow**, si cambias `ci.yml`:

```bash
docker run --rm -v "$PWD:/repo" -w /repo rhysd/actionlint:1.7.12 .github/workflows/ci.yml
```

### Si gitleaks encuentra un secreto

1. **Si es real** (una contraseña, una clave o un token que se usa en algún
   lado):
   - Revócalo y genera uno nuevo **antes de cualquier otra cosa**. Quitarlo del
     archivo no basta: sigue en el historial y en las copias del repositorio.
   - Después, sácalo del código y guárdalo en el `.env` o en el gestor de
     secretos.
   - Limpiar el historial (reescribirlo) es una decisión aparte, porque obliga a
     todos a volver a clonar.
2. **Si no es un secreto** (un valor fijo de prueba o un marcador de ejemplo),
   agrega una excepción en `.gitleaks.toml`:
   - Exceptúa el **valor exacto** y, si se puede, solo en el archivo donde vive.
   - Escribe en la descripción por qué es seguro.
   - Nunca exceptúes una carpeta entera: un secreto real ahí pasaría sin aviso.

   Las instrucciones están al inicio del archivo.

### Proteger `main` en GitHub

Para que nada llegue a `main` sin el pipeline en verde, crea un ruleset. Necesita
permisos de administrador del repositorio. El pipeline debe haber corrido al menos
una vez para que GitHub muestre sus trabajos.

1. En el repositorio, ve a **Settings → Rules → Rulesets → New ruleset → New
   branch ruleset**.
2. **Ruleset name:** `main`. **Enforcement status:** `Active`.
3. **Target branches → Add target → Include by pattern:** `main`.
4. Marca estas reglas:
   - **Restrict deletions** y **Block force pushes**.
   - **Require a pull request before merging**.
   - **Require status checks to pass**. En **Add checks** agrega
     `build-and-test`, `dependencies` y `secrets`, con la fuente **GitHub
     Actions**. Marca también **Require branches to be up to date before
     merging**.
5. Guarda con **Create**.

Desde ese momento, los cambios entran a `main` solo por pull request y con los
tres trabajos en verde. Si se cambia el nombre de un trabajo en `ci.yml`, hay que
actualizar el ruleset.

### Dependabot

`.github/dependabot.yml` abre cada lunes pull requests hacia `dev`:

- **NuGet**, agrupando las familias que se actualizan juntas: EF Core con su
  proveedor de PostgreSQL, ASP.NET Core, IdentityModel, xUnit y Testcontainers.
- **GitHub Actions**, actualizando el SHA fijado y el comentario de la versión.

No propone versiones mayores de ASP.NET Core ni de EF Core, porque esas siguen al
`TargetFramework`.

Las alertas y las actualizaciones de seguridad se activan en la configuración del
repositorio, no en ese archivo:

1. **Settings → Advanced Security** (en algunas cuentas, **Code security**).
2. Activa **Dependency graph**, si no está activo.
3. Activa **Dependabot alerts**: avisan apenas se publica una vulnerabilidad en un
   paquete del repositorio.
4. Activa **Dependabot security updates**: abren el pull request con la versión
   corregida sin esperar al lunes.

## Entorno local

`docker-compose.dev.yml` levanta lo necesario para ejecutar la API a mano y
desarrollar el frontend contra ella. Todo se publica solo en `127.0.0.1`:

| Servicio | Para qué | Dirección |
|---|---|---|
| PostgreSQL 17 | La base, con sus datos en un volumen | `127.0.0.1:5432` |
| Mailpit | Atrapa los correos que envía la API, sin entregarlos | Correos en http://127.0.0.1:8025, SMTP en `127.0.0.1:1025` |
| pgAdmin | Ver y gestionar la base | http://127.0.0.1:5050 |

La base tiene dos roles, igual que en las pruebas:

- `misaldo_admin` es el administrador. Aplica las migraciones y **no** tiene la
  restricción de Row Level Security.
- `misaldo_app` es el rol con el que se conecta la API. **No** tiene `BYPASSRLS`:
  solo ve las filas del usuario de la petición.

Los contenedores vuelven a arrancar solos con Docker y los datos de PostgreSQL
sobreviven a apagar el equipo. Los correos de Mailpit no se conservan.

### 1. Crear el `.env`

```bash
cp .env.example .env
```

Edita `.env`:

- Pon contraseñas propias en `POSTGRES_ADMIN_PASSWORD`, `MISALDO_APP_PASSWORD` y
  `PGADMIN_PASSWORD`. Usa letras y números: los valores van dentro de cadenas de
  conexión.
- Repite las dos primeras en `MISALDO_DB` y `ConnectionStrings__MiSaldo`.
- Genera la clave de firma de los JWT **una sola vez** y guárdala en
  `JWT__SIGNING_KEY`. Si cambia en cada arranque, todas las sesiones se cierran.
  El comando para generarla está en el mismo `.env.example`.

Git ignora el `.env`; nunca lo subas. Compose lo lee solo. Para la API y las
migraciones hay que cargarlo en la terminal:

```bash
# bash / zsh
set -a; source .env; set +a
```

```powershell
# PowerShell
Get-Content .env | Where-Object { $_ -match '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$' } |
  ForEach-Object { Set-Item "env:$($Matches[1])" ($Matches[2].Trim().Trim('"')) }
```

### 2. Levantar el entorno

```bash
docker compose -f docker-compose.dev.yml up -d --wait
```

La primera vez, con el volumen vacío, PostgreSQL crea `misaldo_app` con la
contraseña de `MISALDO_APP_PASSWORD` (`deploy/dev/postgres-init/01-app-role.sh`).
Si después cambias esa contraseña en el `.env`, cámbiala también en la base con
`ALTER ROLE misaldo_app PASSWORD '...'`.

### 3. Aplicar las migraciones

Se usa la herramienta local `dotnet-ef` (fijada en `dotnet-tools.json`). Toma la
cadena de conexión de `MISALDO_DB`, que apunta a `misaldo_admin`:

```bash
dotnet tool restore
dotnet ef database update --project src/MiSaldo.Infrastructure --startup-project src/MiSaldo.Infrastructure
```

### 4. Ejecutar la API

Con el `.env` cargado:

```bash
dotnet run --project src/MiSaldo.Api --no-launch-profile
```

La API queda en http://127.0.0.1:5080 y el documento OpenAPI en
http://127.0.0.1:5080/openapi/v1.json (solo en Development). La API no arranca
sin la cadena de conexión ni la clave de firma. En Production tampoco arranca sin
SMTP válido ni con un comodín en los orígenes CORS; el mensaje nombra la
variable, sin mostrar su valor.

### Ver los correos en Mailpit

Abre http://127.0.0.1:8025. Ahí llegan los correos de verificación y de
recuperación, en el idioma del usuario y con el enlace al frontend
(`Frontend__BaseUrl`). Mientras no exista el frontend, copia el `token` del
enlace y envíalo a `POST /api/v1/auth/verify-email` o a
`POST /api/v1/auth/reset-password`.

Sin `Smtp__Host`, la API en Development no envía correos: guarda los tokens en
memoria, que es lo que usan las pruebas.

### Correo en cada etapa

Hay un solo notificador SMTP (decisión D-13). Para cambiar de servidor solo se
cambian las variables `Smtp__*`; los ejemplos completos están en `.env.example`.

| Etapa | Servidor | Notas |
|---|---|---|
| Desarrollo | Mailpit: `127.0.0.1`, puerto 1025, `Smtp__Security=None` | No entrega nada; todo se ve en la página de Mailpit. |
| Pruebas con entregas reales | Un Gmail dedicado: `smtp.gmail.com`, puerto 587, `StartTls` | La cuenta necesita verificación en dos pasos y una contraseña de aplicación, que va en `Smtp__Password`. Usa una cuenta solo para esto. |
| Producción | Un proveedor transaccional (Amazon SES u otro), puerto 587, `StartTls` | Con el dominio propio y SPF, DKIM y DMARC configurados. `None` está prohibido en Production. |

Si el servidor de correo falla, la API responde igual y registra el fallo sin el
token, el enlace ni la contraseña. El usuario puede pedir otro correo con
`POST /api/v1/auth/resend-verification`.

### pgAdmin

Entra a http://127.0.0.1:5050 con `PGADMIN_EMAIL` y `PGADMIN_PASSWORD`. La
conexión **MiSaldo local (admin)** ya está registrada. La primera vez pide la
contraseña de `misaldo_admin` (`POSTGRES_ADMIN_PASSWORD`).

Esa conexión es la del administrador y **no** tiene la restricción de RLS: ve
los datos de todos los usuarios. Para ver la base como la ve un usuario, abre el
Query Tool y toma el rol de la aplicación con el id del usuario:

```sql
-- El id del usuario
SELECT "UserId" FROM user_profiles WHERE "Email" = 'ana@example.com';

SET ROLE misaldo_app;
SET app.user_id = '<id del usuario>';
SELECT * FROM expense_categories;   -- solo las de ese usuario
RESET app.user_id;
RESET ROLE;
```

Sin `app.user_id`, `misaldo_app` no ve ninguna fila financiera.

> **Cuidado al editar a mano.** Un cambio hecho directamente en la base se salta
> las reglas de negocio (montos positivos, periodos sin solapes, pagos
> inmutables, auditoría). Úsalo para mirar, no para corregir datos.

### Respaldar y restaurar la base local

Respaldo (`backups/` y los `*.dump` están en `.gitignore`: contienen datos
personales):

```bash
docker compose -f docker-compose.dev.yml exec postgres pg_dump -U misaldo_admin -d misaldo -Fc -f /tmp/misaldo.dump
docker compose -f docker-compose.dev.yml cp postgres:/tmp/misaldo.dump ./backups/misaldo.dump
```

Restauración (reemplaza lo que haya en la base):

```bash
docker compose -f docker-compose.dev.yml cp ./backups/misaldo.dump postgres:/tmp/misaldo.dump
docker compose -f docker-compose.dev.yml exec postgres pg_restore -U misaldo_admin -d misaldo --clean --if-exists /tmp/misaldo.dump
```

El respaldo conserva las políticas de RLS y los permisos de `misaldo_app`.

### Apagar el entorno

```bash
docker compose -f docker-compose.dev.yml down      # conserva los datos
```

> **`docker compose -f docker-compose.dev.yml down -v` borra los volúmenes:** la
> base completa y la configuración de pgAdmin. Haz un respaldo antes si lo
> necesitas.

## Rutas de la API

Todas bajo `/api/v1`:

| Ruta | Acceso |
|---|---|
| `/auth/register`, `/auth/verify-email`, `/auth/resend-verification`, `/auth/login`, `/auth/refresh`, `/auth/logout`, `/auth/forgot-password`, `/auth/reset-password` | Pública, con límite estricto de solicitudes |
| `/profile`, `/settings` | Token válido |
| `/categories`, `/periods`, `/incomes`, `/expenses` (con `/expenses/{id}/payments` e `Idempotency-Key`), `/dashboard` | Token válido y correo verificado |

Sesión desde el navegador:

- El login devuelve el access token en el cuerpo. El cliente lo guarda **solo en
  memoria** y lo envía en `Authorization: Bearer`.
- El refresh token no viaja en el cuerpo. Llega en la cookie
  `__Secure-misaldo-refresh` (`HttpOnly`, `Secure`, `SameSite=Strict`,
  `Path=/api/v1/auth`).
- `/auth/refresh` y `/auth/logout` no llevan cuerpo: leen la cookie. Rechazan
  con 403 una petición cuyo `Origin` no esté permitido.
- El frontend debe hacer las peticiones con credenciales, y su origen debe estar
  en `Cors__AllowedOrigins__*`.

Los errores siguen el formato Problem Details con un `code` estable y el
`correlationId` de la petición.

## Hoja de ruta

- [x] Motor de caja y dinero libre
- [x] Créditos sin intereses con recálculo de cuotas
- [x] Modelo de datos en PostgreSQL y aislamiento por usuario
- [x] Autenticación, perfil y periodos
- [x] Ingresos, gastos y dashboard (API)
- [ ] Frontend web (Angular)
- [ ] Historial, auditoría consultable y privacidad (exportar y eliminar cuenta)
- [x] Envío de correos (verificación y recuperación) y sesión web por cookie
- [x] Integración continua (GitHub Actions) con cobertura, dependencias vulnerables y secretos
- [ ] Ahorro, objetivos y créditos en la API (F2)
- [ ] Reportes (PDF, Excel, CSV)
- [ ] Idiomas adicionales (inglés, chino, hindi, árabe con RTL)
- [ ] Aplicación móvil Android e iOS

## Alcance de usuarios

Por ahora la aplicación es solo para **mayores de 18 años**. El modelo de datos contempla evolucionar a cuentas familiares en el futuro.

## Licencia

Por definir.
