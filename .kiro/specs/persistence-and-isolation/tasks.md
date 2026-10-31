# Plan de implementación — Persistencia e aislamiento por usuario

Cada tarea es pequeña y verificable. El orden va del andamiaje y el modelo hacia
las dos barreras de aislamiento y, al final, las restricciones y la idempotencia.
Las tareas marcadas con 🐳 necesitan **Docker corriendo** (Testcontainers levanta
PostgreSQL); el resto se verifica con compilación y pruebas sin base. Marca una
tarea como completada solo cuando compila y sus pruebas pasan.

- [x] 1. Proyecto de infraestructura y entidades de cuenta
  - Crear `src/MiSaldo.Infrastructure` (net10.0) con EF Core y Npgsql, referencia
    a `MiSaldo.Domain`, y agregarlo a la solución.
  - Definir las entidades de cuenta de F1 que el dominio aún no tiene (`User`,
    `UserProfile`, `UserSettings`, `ConsentRecord`, `AccountDeletionRecord`),
    decidiendo dominio vs. infraestructura según tengan reglas o no.
  - Verificar con `dotnet build`.
  - _Requisitos: R1.1, R2.3_

- [x] 2. DbContext y configuraciones de entidad
  - [x] 2.1 Implementar `MiSaldoDbContext` con los `DbSet` de F1 y `ICurrentUser`.
    - _Requisitos: R1.2_
  - [x] 2.2 Implementar `MoneyConverter` y una `IEntityTypeConfiguration<T>` por
        entidad: tipos `numeric(18,2)`, `date`, `timestamptz`; campos base
        (`Id`, `UserId`, `CreatedAt`, `UpdatedAt`); mapeo de `Expense` con su
        colección de pagos por campo de respaldo.
    - Pruebas: el modelo se construye sin errores (`Database.EnsureCreated` en
      SQLite in-memory NO; validación del modelo con `context.Model`).
    - _Requisitos: R1.3, R1.4, R2.1, R2.2_

- [ ] 3. Filtro global por usuario (barrera 1)
  - Aplicar `HasQueryFilter` por `UserId` a toda entidad financiera y asignar
    `UserId` desde `ICurrentUser` al crear; fallar seguro si no hay usuario.
  - Pruebas (sin base, con proveedor en memoria de EF Core): una consulta solo
    devuelve entidades del usuario actual; crear asigna el `UserId` correcto.
  - _Requisitos: R3.1, R3.2, R3.3, R3.4_

- [ ] 4. Migración inicial y restricciones de integridad
  - [ ] 4.1 Generar la migración inicial con todas las tablas de F1, llaves
        foráneas, `CHECK` de montos, token de concurrencia (`xmin`) e índices
        por `UserId`.
    - _Requisitos: R5.1, R5.2, R6.1, R6.3_
  - [ ] 4.2 Agregar al esquema el EXCLUDE de no solapamiento de periodos por
        usuario, el índice único de categorías por usuario sin mayúsculas y el
        `CHECK` de fin ≥ inicio de periodo.
    - _Requisitos: R5.3, R5.4, R5.5_

- [ ] 5. Row Level Security (barrera 2)
  - Migración SQL que habilita RLS y crea la política por `UserId` de sesión en
    cada tabla financiera; `RlsConnectionInterceptor` que ejecuta
    `SET app.user_id` por unidad de trabajo; rol de app sin `BYPASSRLS`.
  - _Requisitos: R4.1, R4.2_

- [ ] 6. Auditoría e idempotencia
  - [ ] 6.1 `AuditingInterceptor`: llena `CreatedAt`/`UpdatedAt` y escribe
        `AuditLog` (quién, cuándo, antes/después) en cada cambio de movimiento.
    - _Requisitos: R6.2_
  - [ ] 6.2 Idempotencia de pagos: `IdempotencyKey` con índice único por
        (usuario, clave); inserción del pago en transacción.
    - _Requisitos: R7.1, R7.3_

- [ ] 7. 🐳 Base de pruebas de integración con Testcontainers
  - Crear `tests/MiSaldo.Infrastructure.Tests` con `Testcontainers.PostgreSql`;
    fixture que levanta PostgreSQL, aplica migraciones y expone una fábrica de
    `DbContext` con un `ICurrentUser` controlable.
  - Verificar con una prueba de humo: insertar y leer una entidad.
  - _Requisitos: R9.1, R9.3_

- [ ] 8. 🐳 Verificar el aislamiento (CP-07)
  - Pruebas: con datos de A y B, y la sesión fijada a A, una consulta normal y
    una consulta cruda (que ignore el filtro de EF Core) NO ven datos de B; la
    RLS lo impide.
  - _Requisitos: R4.3, R4.4_

- [ ] 9. 🐳 Verificar restricciones, idempotencia y multi-dispositivo
  - Pruebas: periodos solapados del mismo usuario rechazados (CP: RF-PRD-03);
    categoría duplicada por usuario rechazada (RF-CAT-04); fin < inicio
    rechazado; mismo pago con la misma clave de idempotencia → una sola fila
    (CP-12); dos contextos sobre la misma base ven los mismos datos (CP-16);
    concurrencia optimista falla de forma controlada.
  - _Requisitos: R5.3, R5.4, R5.5, R6.1, R7.2, R8.1, R8.2_

- [ ] 10. Cierre: configuración, secretos y documentación
  - Añadir `.env.example` (sin valores reales), documentar cómo correr las
    pruebas (requiere Docker) y actualizar el README/hoja de ruta.
  - Verificar la suite completa: dominio + infraestructura en verde.
  - _Requisitos: R9.2, y RNF-SEG-08_
