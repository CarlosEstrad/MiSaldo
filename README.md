# MiSaldo

Aplicación web y móvil para la gestión inteligente de las finanzas personales. Ayuda a las personas a administrar sus ingresos y gastos por periodos de pago, planificar obligaciones, controlar créditos y desarrollar el hábito del ahorro.

> **Estado:** en desarrollo temprano. Están implementados el dominio (motor financiero) y la capa de persistencia (PostgreSQL con EF Core, aislamiento por usuario mediante filtro global y Row Level Security), ambos con sus pruebas.

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
- Un pago de crédito sale de caja y reduce la deuda, pero no se cuenta además como gasto.
- Los pagos son un historial inmutable: al abonar más o menos de la cuota solo se recalculan las cuotas futuras.
- Cambiar la periodicidad (mensual, quincenal, semanal) no altera los periodos históricos.
- Todo el dinero se maneja con `decimal`; los decimales dependen de la moneda (COP = 0).

## Arquitectura

Clean Architecture: las dependencias apuntan hacia el centro y el motor financiero vive solo en el backend.

```
src/
├── MiSaldo.Domain/          # Entidades, cuentas y motor financiero (C# puro)
├── MiSaldo.Application/     # Casos de uso, DTOs, validaciones   (pendiente)
├── MiSaldo.Infrastructure/  # EF Core + PostgreSQL, RLS, auditoría
└── MiSaldo.Api/             # Controladores y configuración      (pendiente)
tests/
├── MiSaldo.Domain.Tests/         # Pruebas xUnit de las reglas financieras
└── MiSaldo.Infrastructure.Tests/ # Pruebas de persistencia (Testcontainers)
web/                         # Angular + Ionic/Capacitor          (pendiente)
infra/                       # Infraestructura como código        (pendiente)
```

## Stack previsto

| Capa | Tecnología |
|---|---|
| Web y móvil | Angular + Ionic/Capacitor |
| Backend | ASP.NET Core Web API (.NET LTS) |
| Datos | Entity Framework Core + PostgreSQL (Amazon RDS) |
| Autenticación | Amazon Cognito (Authorization Code + PKCE) |
| Despliegue | AWS App Runner, S3 + CloudFront |
| Reportes | QuestPDF, ClosedXML, CSV |
| Pruebas | xUnit, Testcontainers, Playwright |

## Ejecutar las pruebas

Requiere el SDK de .NET 10.

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

Toda la suite:

```bash
dotnet test
```

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
- [ ] Autenticación, perfil y periodos
- [ ] Ingresos, gastos y dashboard
- [ ] Ahorro y objetivos
- [ ] Créditos con intereses
- [ ] Reportes (PDF, Excel, CSV)
- [ ] Idiomas adicionales (inglés, chino, hindi, árabe con RTL)
- [ ] Aplicación móvil Android e iOS

## Alcance de usuarios

Por ahora la aplicación es solo para **mayores de 18 años**. El modelo de datos contempla evolucionar a cuentas familiares en el futuro.

## Licencia

Por definir.
