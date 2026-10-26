# MiSaldo

Aplicación web y móvil para la gestión inteligente de las finanzas personales. Ayuda a las personas a administrar sus ingresos y gastos por periodos de pago, planificar obligaciones, controlar créditos y desarrollar el hábito del ahorro.

> **Estado:** en desarrollo temprano. Actualmente está implementada la capa de dominio (motor financiero) con sus pruebas.

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
├── MiSaldo.Domain/          # Entidades y motor financiero (C# puro)
├── MiSaldo.Application/     # Casos de uso, DTOs, validaciones   (pendiente)
├── MiSaldo.Infrastructure/  # EF Core, Cognito, SES, reportes    (pendiente)
└── MiSaldo.Api/             # Controladores y configuración      (pendiente)
tests/
└── MiSaldo.Domain.Tests/    # Pruebas xUnit de las reglas financieras
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

```bash
dotnet new sln -n MiSaldo
dotnet sln add src/MiSaldo.Domain tests/MiSaldo.Domain.Tests
dotnet test
```

## Hoja de ruta

- [x] Motor de caja y dinero libre
- [x] Créditos sin intereses con recálculo de cuotas
- [ ] Modelo de datos en PostgreSQL y aislamiento por usuario
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
