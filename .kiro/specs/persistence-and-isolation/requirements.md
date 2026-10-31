# Requirements Document

> Persistencia e aislamiento por usuario de MiSaldo.

## Introducción

Este spec cubre la capa de persistencia de MiSaldo: el modelo de datos en
PostgreSQL, su mapeo con Entity Framework Core y, sobre todo, el **aislamiento
estricto por usuario**. Según el SRS, toda entidad financiera incluye `UserId`,
toda consulta se filtra por el usuario autenticado mediante un filtro global de
EF Core, y existe una **segunda barrera en la base de datos con Row Level
Security (RLS)** de PostgreSQL (RNF-SEG-04). El requisito de seguridad más
crítico del proyecto es que un usuario nunca vea ni modifique datos de otro
(CP-07).

Es la segunda pieza del sistema, después del dominio. El dominio
(`MiSaldo.Domain`) ya está construido y probado; este spec le da almacenamiento
duradero sin que el dominio dependa de la infraestructura: el proyecto
`MiSaldo.Infrastructure` depende de `MiSaldo.Domain`, nunca al revés
(RNF-MAN-01).

**Plataforma:** .NET 10, Entity Framework Core, PostgreSQL (Amazon RDS en
producción; contenedor efímero en pruebas).

**Alcance:** entidades de F1 del modelo conceptual (sección 7 del SRS): `User`,
`UserProfile`, `UserSettings`, `ConsentRecord`, `FinancialPeriod`,
`ExpenseCategory`, `IncomeTransaction`, `Expense`, `ExpensePayment`, `AuditLog`,
`AccountDeletionRecord`. El `DbContext`, las configuraciones de entidad, las
migraciones, el filtro global por usuario, la RLS, las restricciones de
integridad, la concurrencia optimista y la idempotencia de pagos. Las entidades
de F2 (créditos, ahorro, recurrentes, presupuestos) se agregan en un spec
posterior, aunque su diseño debe caber sin romper el esquema.

**Fuera de alcance:** la API y sus controladores, Cognito y la resolución del
usuario desde el token (este spec recibe el `UserId` ya resuelto), el frontend,
los reportes y el motor de cálculo (ya existe en el dominio).

**Verificación:** las pruebas de integración corren contra una PostgreSQL real
en un contenedor (Testcontainers), porque RLS y las restricciones de la base no
pueden verificarse con un proveedor en memoria.

### Convenciones

- Nombres de tablas, columnas y claves en inglés; este documento en español.
- Dinero en `numeric(18,2)`; fechas de movimiento como `date`; marcas de tiempo
  en UTC (`timestamptz`) (RNF-DAT-02, RNF-DAT-06).
- Cada requisito referencia las reglas del SRS que implementa.

---

## Requisitos

### Requisito 1 — Proyecto de infraestructura y DbContext

**Historia:** Como sistema, quiero un proyecto de infraestructura con un
`DbContext` que mapee el dominio a PostgreSQL, para persistir y recuperar datos
sin que el dominio conozca la base.

#### Criterios de aceptación

1. El proyecto `MiSaldo.Infrastructure` DEBERÁ referenciar `MiSaldo.Domain` y los
   paquetes de EF Core y Npgsql; `MiSaldo.Domain` NO DEBERÁ referenciar
   infraestructura. (RNF-MAN-01)
2. El `DbContext` DEBERÁ exponer conjuntos para las entidades de F1 y aplicar
   configuraciones de entidad separadas (una clase de configuración por
   entidad). (RNF-MAN-01)
3. Los montos DEBERÁN mapearse a `numeric(18,2)`, las fechas de movimiento a
   `date` y las marcas de tiempo a `timestamptz` en UTC. (RNF-DAT-02, RNF-DAT-06)
4. Los tipos de valor del dominio (`Money`) DEBERÁN persistirse sin que el
   dominio gane dependencias de EF Core (conversión en la configuración de
   infraestructura).

### Requisito 2 — Identidad por usuario en toda entidad financiera

**Historia:** Como responsable de seguridad, quiero que toda entidad financiera
lleve `UserId`, para poder aislar los datos de cada usuario.

#### Criterios de aceptación

1. Toda entidad financiera DEBERÁ incluir `Id` (UUID), `UserId`, `CreatedAt` y
   `UpdatedAt`. (sección 7 del SRS, RNF-SEG-04)
2. `UserId` NO DEBERÁ ser nulo en ninguna entidad financiera y DEBERÁ tener
   índice para el filtrado por usuario. (RNF-SEG-04, RNF-REN-01)
3. La entidad `User` DEBERÁ vincularse a la identidad externa (Cognito) y llevar
   `AccountType` (`Standard`; futuro `Child`) y `GuardianUserId` nulo. (sección 7)

### Requisito 3 — Filtro global por usuario (primera barrera)

**Historia:** Como responsable de seguridad, quiero que toda consulta se filtre
automáticamente por el usuario autenticado, para que ninguna consulta devuelva
datos de otro usuario por descuido.

#### Criterios de aceptación

1. El `DbContext` DEBERÁ aplicar un filtro de consulta global por `UserId` a toda
   entidad financiera, usando el usuario actual provisto por un servicio de
   contexto. (RNF-SEG-04)
2. El `UserId` DEBERÁ obtenerse del contexto del usuario autenticado, nunca de
   parámetros de la consulta. (RF-AUT-09)
3. SI no hay usuario en contexto en una operación que requiere uno, el sistema
   DEBERÁ fallar de forma segura (sin devolver datos). (RNF-SEG-04)
4. CUANDO se cree una entidad financiera, el sistema DEBERÁ asignarle el `UserId`
   del usuario actual automáticamente. (RF-AUT-09)

### Requisito 4 — Row Level Security (segunda barrera)

**Historia:** Como responsable de seguridad, quiero una segunda barrera en la
propia base de datos, para que el aislamiento no dependa solo del código de la
aplicación.

#### Criterios de aceptación

1. Las tablas financieras DEBERÁN tener habilitada Row Level Security con una
   política que restrinja filas al `UserId` de la sesión. (RNF-SEG-04)
2. La aplicación DEBERÁ fijar el `UserId` de la sesión de PostgreSQL por cada
   unidad de trabajo, de modo que la política RLS lo use. (RNF-SEG-04)
3. SI una consulta intenta leer filas de otro usuario con la sesión fijada, la
   base de datos NO DEBERÁ devolverlas, aunque el filtro de EF Core se omitiera.
   (RNF-SEG-04, RNF-SEG-05)
4. El caso CP-07 (usuario A consulta un movimiento de usuario B) DEBERÁ resultar
   en acceso denegado sin revelar si el recurso existe. (CP-07, RNF-SEG-05)

### Requisito 5 — Restricciones de integridad en la base

**Historia:** Como sistema, quiero que la base garantice las invariantes que no
deben depender solo de la aplicación, para proteger la integridad de los datos.

#### Criterios de aceptación

1. Los montos DEBERÁN tener restricción `CHECK` de validez (p. ej. no negativos
   donde aplique). (RNF-DAT-05)
2. Las llaves foráneas DEBERÁN declararse entre entidades relacionadas (pagos a
   gasto, perfil a usuario, etc.). (RNF-DAT-05)
3. Los periodos DEBERÁN ser únicos por usuario sin solaparse en fechas; la base
   DEBERÁ impedir periodos cruzados del mismo usuario. (RF-PRD-03, RNF-DAT-05)
4. El nombre de categoría DEBERÁ ser único por usuario sin distinguir mayúsculas.
   (RF-CAT-04, RNF-DAT-05)
5. SI la fecha de fin de un periodo es anterior a la de inicio, la base DEBERÁ
   rechazar la fila. (RNF-DAT-05)

### Requisito 6 — Concurrencia optimista y auditoría

**Historia:** Como sistema, quiero controlar la edición concurrente y registrar
los cambios relevantes, para no perder datos ni trazabilidad.

#### Criterios de aceptación

1. Toda entidad financiera DEBERÁ tener un token de concurrencia (versión de
   fila) y la edición concurrente DEBERÁ fallar de forma controlada. (RNF-DAT-04)
2. CUANDO se cree, modifique o elimine un movimiento, el sistema DEBERÁ escribir
   un registro de auditoría con quién, cuándo, valor anterior y nuevo.
   (RF-HIS-04)
3. Los cambios de esquema DEBERÁN hacerse solo mediante migraciones versionadas.
   (RNF-DAT-08)

### Requisito 7 — Idempotencia de pagos

**Historia:** Como usuario, quiero que un doble clic o un reintento no registre
dos veces el mismo pago, para no duplicar movimientos.

#### Criterios de aceptación

1. Las operaciones de pago DEBERÁN aceptar una clave de idempotencia y registrar
   el pago una sola vez para la misma clave. (RNF-DAT-03)
2. CUANDO se reciba dos veces el mismo pago con la misma clave de idempotencia,
   el sistema DEBERÁ persistir una sola fila. (CP-12, RNF-DAT-03)
3. Las operaciones que afecten varias tablas DEBERÁN ejecutarse en una
   transacción. (RNF-DAT-01)

### Requisito 8 — Consistencia multi-dispositivo y saldos no almacenados

**Historia:** Como usuario, quiero ver los mismos datos desde cualquier
dispositivo, porque todo vive en la nube y los saldos se calculan, no se
guardan.

#### Criterios de aceptación

1. Los saldos NO DEBERÁN almacenarse como valores independientes; se recuperan
   los movimientos y el motor del dominio los calcula. (RNF-DAT-07)
2. CUANDO el mismo usuario consulte desde dos dispositivos, ambos DEBERÁN ver los
   mismos datos persistidos. (CP-16)

### Requisito 9 — Pruebas de integración contra PostgreSQL real

**Historia:** Como mantenedor, quiero verificar el aislamiento y las
restricciones contra una PostgreSQL real, para confiar en que las barreras
funcionan de verdad.

#### Criterios de aceptación

1. Las pruebas de integración DEBERÁN ejecutarse contra una PostgreSQL real
   provista por un contenedor efímero (Testcontainers). (sección sobre pruebas
   del SRS)
2. DEBERÁ existir una prueba que verifique CP-07 (aislamiento entre usuarios con
   RLS), CP-12 (idempotencia) y las restricciones de unicidad de periodos y
   categorías. (CP-07, CP-12, RF-PRD-03, RF-CAT-04)
3. Las migraciones DEBERÁN aplicarse sobre la base del contenedor y dejarla en un
   estado consistente antes de cada suite. (RNF-DAT-08)
