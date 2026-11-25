# Diseño — Integración continua

## Overview

Un solo workflow de GitHub Actions, `.github/workflows/ci.yml`, corre en cada
push a `dev` o `main` y en cada PR hacia ellas. Tiene tres trabajos que corren en
paralelo:

| Trabajo | Qué revisa | Requisitos |
|---|---|---|
| `build-and-test` | Restaura, compila sin advertencias, revisa que no falten migraciones, ejecuta todas las pruebas (incluidas las de Docker), publica resultados y cobertura, y falla por debajo del mínimo de cobertura. | R1, R2, R5 |
| `dependencies` | Revisa los paquetes NuGet, incluidos los transitivos, contra los avisos de seguridad; falla con severidad alta o crítica. | R3 |
| `secrets` | Busca secretos en todo el historial del repositorio sin imprimir su valor. | R4 |

El pipeline no despliega nada y no usa secretos. Cada revisión es un script del
repositorio (`scripts/ci/*.ps1`) o un comando documentado: el workflow ejecuta lo
mismo que el desarrollador ejecuta en su equipo (R7.2). Dependabot abre PRs
semanales para NuGet y GitHub Actions.

### Estructura (lo que se agrega o cambia)

```
.github/
├── workflows/ci.yml                 # el pipeline
└── dependabot.yml                   # actualizaciones semanales: NuGet y Actions
.gitleaks.toml                       # reglas y excepciones documentadas de la búsqueda de secretos
coverage.runsettings                 # qué ensamblados mide la cobertura
dotnet-tools.json                    # + dotnet-reportgenerator-globaltool (fijado)
Directory.Build.props                # NuGet audit: alta/crítica fallan, baja/moderada avisan
scripts/ci/
├── Write-TestSummary.ps1            # resumen de los .trx: totales y pruebas fallidas
├── Test-Coverage.ps1                # umbrales por ensamblado sobre el resumen de ReportGenerator
└── Test-VulnerablePackages.ps1      # dotnet list package --vulnerable → tabla y fallo por severidad
README.md                            # insignia, qué revisa el CI, cómo reproducirlo, proteger main
```

## Architecture

| # | Decisión | Motivo |
|---|---|---|
| C1 | **Un workflow con tres trabajos paralelos** en `ubuntu-latest`. La revisión de migraciones va dentro de `build-and-test`, justo después de compilar. | Cada trabajo da un estado propio en el PR y en la protección de `main`. Revisar las migraciones ahí evita compilar dos veces. |
| C2 | Disparadores: `push` y `pull_request` a `dev` y `main`. **Nunca `pull_request_target`.** | Con `pull_request`, un PR desde un fork recibe un token de solo lectura y ningún secreto (R6.3). `pull_request_target` correría código ajeno con permisos del repositorio. |
| C3 | `permissions: contents: read` al nivel del workflow, y ningún trabajo lo amplía. | Ningún paso escribe en el repositorio: los resultados van al resumen del job y a artefactos, que no necesitan más permisos (R6.1). |
| C4 | Acciones de terceros **fijadas al SHA del commit**, con la versión en un comentario (`actions/checkout@<sha> # vX.Y.Z`). Solo acciones oficiales de GitHub: `checkout`, `setup-dotnet`, `cache` y `upload-artifact`. Dependabot las mantiene al día. | Una etiqueta se puede mover; un SHA no (R6.2). Menos acciones de terceros, menos superficie. |
| C5 | `setup-dotnet` con `global-json-file: global.json`. | La misma versión de SDK que en local (R1.2). |
| C6 | Caché de NuGet con `actions/cache` sobre `~/.nuget/packages`. La clave es el hash de los `*.csproj`, `Directory.Build.props`, `global.json` y `dotnet-tools.json`. | `setup-dotnet` solo cachea con archivos de bloqueo (`packages.lock.json`), que el repositorio no usa. Activar los bloqueos queda fuera de este spec (R1.6). |
| C7 | El pipeline compila en **Release** y ejecuta las pruebas con `--no-build`. `TreatWarningsAsErrors` ya convierte cualquier advertencia en error. | R1.4. Release además detecta diferencias de optimización que en Debug no aparecen. |
| C8 | Las pruebas de Docker corren tal cual: los runners de GitHub traen Docker y Testcontainers descarga `postgres:17-alpine` y `axllent/mailpit`. No hay filtros que las omitan. | R1.3. |
| C9 | **Resultados de pruebas:** `--logger trx` en una carpeta; `Write-TestSummary.ps1` escribe totales por proyecto y cada prueba fallida con su mensaje en `$GITHUB_STEP_SUMMARY`. Los `.trx` se suben como artefacto **siempre**, también si las pruebas fallan. | R1.5 sin una acción de terceros que necesite `checks: write` (C3). En PRs desde forks ese permiso no existe. |
| C10 | **Cobertura:** el colector de coverlet que ya está en los cuatro proyectos de pruebas, con `coverage.runsettings` limitado a `[MiSaldo.Domain]*` y `[MiSaldo.Application]*`. ReportGenerator (herramienta local fijada) une los informes de todos los proyectos y produce HTML, Cobertura, `Summary.json` y un resumen Markdown. `Test-Coverage.ps1` lee `Summary.json` y compara cada ensamblado con su mínimo. | R2.1 pide la cobertura medida con **todas** las pruebas: Infrastructure y Api también ejercitan Application. ReportGenerator no tiene umbrales por ensamblado, de ahí el script. |
| C11 | Los mínimos de cobertura (90 % Domain, 70 % Application, RNF-MAN-03) viven solo en `Test-Coverage.ps1`, como valores por defecto de sus parámetros. | Una sola fuente; el workflow y el uso local no los repiten. |
| C12 | **Vulnerabilidades, dos capas.** (a) La auditoría de NuGet en el restore: `NuGetAuditMode=all` (incluye transitivos), y `WarningsNotAsErrors` para NU1901/NU1902 (baja/moderada), así que solo NU1903/NU1904 (alta/crítica) siguen siendo error. (b) El trabajo `dependencies` ejecuta `Test-VulnerablePackages.ps1`, que corre `dotnet list package --vulnerable --include-transitive --format json`, escribe una tabla con paquete, versión, severidad y aviso, y falla con alta o crítica. | (a) Hoy cualquier severidad rompe el restore por `TreatWarningsAsErrors`, y R3.2 pide que baja y moderada solo se reporten. (b) Da el detalle en el resumen aunque el restore pase. |
| C13 | **Secretos con gitleaks** v8.30.1 sobre **todo el historial** (`fetch-depth: 0`), con `--redact` y `.gitleaks.toml`. En el runner se usa el binario oficial de la release, verificado con su SHA-256 publicado; en local, la imagen oficial `ghcr.io/gitleaks/gitleaks` fijada por digest. | Sin licencia ni acción de terceros (la `gitleaks-action` pide licencia a organizaciones). El binario evita el rechazo de git al repositorio montado en un contenedor con otro dueño ("dubious ownership"). El repositorio es chico: revisar todo cubre también el caso de que alguien haya reescrito el historial. `--redact` oculta el valor (R4.2). |
| C14 | `.gitleaks.toml` extiende las reglas por defecto. Las excepciones son **por valor** (expresiones exactas, cada una con un comentario de por qué es segura) y, cuando se puede, limitadas a la ruta donde viven: los `change_me_*` de `.env.example` y las contraseñas fijas de los contenedores efímeros de las pruebas. No se excluyen carpetas enteras. | R4.3. Un secreto real dentro de `tests/` se sigue detectando. |
| C15 | **Migraciones:** `dotnet ef migrations has-pending-model-changes` (EF Core 8+) con la herramienta local, contra el `DesignTimeDbContextFactory` existente. No necesita base de datos. | R5.1. R5.2 ya se cumple: los fixtures aplican todas las migraciones sobre un PostgreSQL vacío en cada corrida. |
| C16 | `concurrency` por rama con `cancel-in-progress: true` y `timeout-minutes` en cada trabajo. | Un push nuevo cancela la corrida vieja de la misma rama; un cuelgue no consume horas. |
| C17 | Los scripts funcionan en **PowerShell 7** (el runner) y **Windows PowerShell 5.1** (el equipo de desarrollo). Fuera de GitHub escriben en la consola. | El mismo script en ambos lados (R7.2). |
| C18 | Dependabot: `nuget` y `github-actions` en `/`, `interval: weekly`, `target-branch: dev`, con grupos para paquetes que se actualizan juntos (EF Core, ASP.NET Core, xUnit, Testcontainers). Las alertas y las actualizaciones de seguridad se activan en la configuración de GitHub (paso manual documentado). | R3.3. Los grupos evitan un PR por cada paquete de la misma familia. |
| C19 | La protección de `main` (ruleset que exige los tres trabajos en verde) **no se automatiza**: se documenta paso a paso. | R7.3. Configurarla necesita credenciales de administrador del repositorio, que el pipeline no tiene ni debe tener. |

## Components and Interfaces

### Workflow `ci.yml` (forma)

```yaml
name: CI
on:
  push:         { branches: [dev, main] }
  pull_request: { branches: [dev, main] }
permissions:
  contents: read
concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: true
env:
  DOTNET_NOLOGO: "1"
  DOTNET_CLI_TELEMETRY_OPTOUT: "1"

jobs:
  build-and-test:        # timeout 30 min
    # checkout → setup-dotnet (global.json) → cache NuGet → dotnet tool restore
    # → dotnet restore → dotnet build -c Release --no-restore
    # → dotnet ef migrations has-pending-model-changes (Release, --no-build)
    # → dotnet test -c Release --no-build --settings coverage.runsettings --logger trx --results-directory TestResults
    # → [always] Write-TestSummary.ps1 → [always] upload TestResults
    # → reportgenerator (HTML, Cobertura, JsonSummary, MarkdownSummaryGithub) → summary
    # → Test-Coverage.ps1 → [always] upload coverage-report
  dependencies:          # timeout 10 min
    # checkout → setup-dotnet → cache NuGet → Test-VulnerablePackages.ps1
  secrets:               # timeout 10 min
    # checkout (fetch-depth: 0) → gitleaks binary (SHA-256 checked) git --redact --config .gitleaks.toml
    # → [failure] table of findings (rule, file, line, commit) in the summary
```

Los nombres de los trabajos (`build-and-test`, `dependencies`, `secrets`) son los
que se exigen en la protección de `main`; cambiarlos obliga a actualizar el
ruleset.

### Scripts

```powershell
# Totales por proyecto y pruebas fallidas (nombre y primera línea del mensaje).
# Siempre sale con 0: el fallo de las pruebas ya lo marca `dotnet test`.
scripts/ci/Write-TestSummary.ps1 -ResultsDirectory TestResults

# Falla (exit 1) si algún ensamblado queda por debajo de su mínimo; imprime el valor medido de cada uno.
scripts/ci/Test-Coverage.ps1 -SummaryJson coverage-report/Summary.json [-DomainMinimum 90] [-ApplicationMinimum 70]

# Tabla de paquetes vulnerables; falla con High o Critical, avisa con Low o Moderate.
scripts/ci/Test-VulnerablePackages.ps1 [-Solution MiSaldo.slnx]
```

Los scripts escriben en `$env:GITHUB_STEP_SUMMARY` cuando existe y en la consola
siempre.

### Configuración nueva

| Archivo | Contenido |
|---|---|
| `coverage.runsettings` | DataCollector `XPlat Code Coverage`, formato `cobertura`, `Include` de Domain y Application, `ExcludeByAttribute` de código generado. |
| `Directory.Build.props` | `NuGetAudit=true`, `NuGetAuditMode=all`, `NuGetAuditLevel=low`, `WarningsNotAsErrors` += `NU1901;NU1902`. |
| `dotnet-tools.json` | `dotnet-reportgenerator-globaltool`, versión estable fijada al implementar. |
| `.gitleaks.toml` | `[extend] useDefault = true` y excepciones por valor, cada una comentada. |
| `.github/dependabot.yml` | Ecosistemas `nuget` y `github-actions`, semanal, hacia `dev`, con grupos. |

## Data Models

No hay cambios en el modelo de datos ni en las migraciones.

## Error Handling

| Situación | Resultado |
|---|---|
| Advertencia de compilación o error | `build-and-test` falla en el paso de compilación. |
| Cambio del modelo sin migración | `build-and-test` falla en el paso de migraciones; EF nombra el contexto. |
| Prueba fallida | `build-and-test` falla; el resumen nombra cada prueba fallida con su mensaje; los `.trx` quedan como artefacto. |
| Docker no disponible en el runner | Las pruebas de Testcontainers fallan (no se omiten); el mensaje de Testcontainers queda en el resumen. |
| Cobertura bajo el mínimo | `build-and-test` falla en el paso de cobertura, con el valor medido y el mínimo de cada ensamblado. |
| Vulnerabilidad alta o crítica | El restore falla con NU1903/NU1904 (paquete y aviso en el mensaje) y `dependencies` falla con la tabla. |
| Vulnerabilidad baja o moderada | El restore avisa (NU1901/NU1902); `dependencies` la lista en el resumen y pasa. |
| Secreto detectado | `secrets` falla con archivo, línea, commit y regla; el valor sale redactado. |
| Falso positivo de secretos | Se agrega una excepción por valor en `.gitleaks.toml`, con un comentario que explique por qué es seguro. |

## Testing Strategy

El pipeline es en sí una verificación, pero cada pieza se prueba antes de subirla:

- **Scripts:** cada uno se ejecuta en local sobre la salida real del repositorio y
  sobre una entrada preparada que debe hacerlo fallar:
  - un `.trx` con una prueba fallida;
  - un `Summary.json` bajo el mínimo;
  - una salida JSON de `dotnet list package` con una vulnerabilidad alta y una
    moderada.
- **Cobertura:** se mide la línea base actual antes de activar los umbrales.
- **Secretos:** gitleaks sobre todo el historial actual. Después, sobre una carpeta
  temporal con un secreto falso, para confirmar que lo detecta y que la salida lo
  redacta.
- **Migraciones:** el comando pasa con el modelo actual y falla con un cambio de
  modelo temporal sin migración, que después se revierte.
- **Workflow:** `actionlint` (imagen oficial fijada) valida la sintaxis, las
  expresiones y los permisos.
- **En GitHub:** el primer push a `dev` (lo hace el usuario) debe terminar con los
  tres trabajos en verde, el resumen de pruebas y cobertura visible y los
  artefactos publicados.

## Correctness Properties

1. **Nada se omite:** el pipeline ejecuta todas las pruebas de la solución,
   incluidas las que usan Docker, sin filtros.
2. **Cero advertencias:** una advertencia de compilación hace fallar el pipeline.
3. **Cobertura vigilada:** Domain < 90 % o Application < 70 % hace fallar el
   pipeline y el mensaje dice el valor medido.
4. **Severidad respetada:** solo una vulnerabilidad alta o crítica hace fallar el
   pipeline; baja y moderada se reportan.
5. **Ningún secreto impreso:** el pipeline nunca muestra el valor completo de un
   secreto encontrado.
6. **Sin privilegios de más:** ningún trabajo tiene más que `contents: read`,
   ningún paso usa un secreto del repositorio y un PR desde un fork no obtiene
   permisos de escritura.
7. **Reproducible:** cada revisión del pipeline se ejecuta igual en local con el
   mismo script o comando.
8. **Migraciones al día:** un cambio de modelo sin migración no llega a `main` en
   verde.

## Trazabilidad requisito → componente

| Requisito | Componentes principales |
|---|---|
| R1 Compilación y pruebas | `ci.yml` (`build-and-test`), `Write-TestSummary.ps1`, caché NuGet |
| R2 Cobertura | `coverage.runsettings`, ReportGenerator, `Test-Coverage.ps1` |
| R3 Dependencias | `Directory.Build.props` (NuGet audit), `Test-VulnerablePackages.ps1`, `dependabot.yml` |
| R4 Secretos | `ci.yml` (`secrets`), `.gitleaks.toml` |
| R5 Migraciones | `ci.yml` (paso `has-pending-model-changes`), fixtures existentes |
| R6 Seguridad del pipeline | `permissions`, disparadores sin `pull_request_target`, acciones fijadas por SHA |
| R7 Documentación | README (insignia, revisiones, comandos locales, proteger `main`) |
