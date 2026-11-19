# Requirements Document

> Historial de movimientos, auditoría consultable y movimientos de ajuste.

## Introduction

Este spec es la novena etapa y agrega tres capacidades de F1 que comparten base:

- **Historial unificado** de movimientos con filtros y paginación (RF-HIS-01 a
  RF-HIS-03).
- **Auditoría consultable.** El sistema ya escribe un registro de cada creación,
  modificación y eliminación de movimientos en `audit_logs`, pero nadie puede
  consultarlo (RF-HIS-04).
- **Movimientos de ajuste** (D-08, RF-HIS-05). Es la forma prevista por el SRS
  de corregir un movimiento de un periodo cerrado, o un pago, sin editar el
  original. Resuelve la tensión entre RF-GAS-06 (editar o eliminar un pago) y
  RN-M08 (los movimientos con historial no se eliminan): hoy los pagos son
  inmutables y no hay manera de corregir uno mal registrado.

**Hallazgo previo:** la tabla `audit_logs` no tiene Row Level Security ni filtro
global por usuario, a diferencia de las tablas financieras. No es un problema
mientras nada la consulte, pero este spec la expone, así que debe aislarse
primero.

**Depende de:** spec 03 (movimientos y periodos), spec 06 (dashboard, que debe
incluir los ajustes) y spec 08 (las pantallas se agregan al frontend).

**Estado:** solo requisitos. El diseño y las tareas se escriben al iniciar esta
etapa.

**Alcance:** aislamiento de `audit_logs`; historial; consulta de auditoría;
ajustes en dominio, persistencia, motor, API y frontend; pruebas.

**Fuera de alcance:** la reapertura de periodos cerrados, que D-08 menciona como
alternativa (solo se implementan ajustes); los movimientos de créditos y ahorro
(F2), que se sumarán al historial en su fase; los reportes (F3).

### Convenciones

- Montos como cadenas decimales (RNF-DAT-02); un ajuste lleva signo.
- Rutas y campos en inglés; este documento en español.

---

## Glossary

- **Movimiento:** hecho que mueve dinero: un ingreso recibido, un pago de un
  gasto o un ajuste. Un ingreso planificado y un gasto sin pagos son
  compromisos, no movimientos de caja.
- **Historial unificado:** lista de todos los movimientos del usuario en un solo
  formato, ordenada por fecha.
- **Registro de auditoría:** fila de `audit_logs` que guarda quién cambió qué,
  cuándo, y los valores anteriores y nuevos.
- **Movimiento de ajuste:** movimiento con signo, fechado en un periodo abierto,
  que corrige a otro movimiento sin modificarlo, y conserva el vínculo con él y
  el motivo.
- **Movimiento original:** el que un ajuste corrige.

---

## Requirements

### Requisito 1 — Aislamiento del registro de auditoría

**Historia:** Como responsable de seguridad, quiero que el registro de auditoría
tenga las mismas barreras que los datos financieros, antes de exponerlo.

#### Criterios de aceptación

1. `audit_logs` DEBERÁ tener Row Level Security forzada con la misma política por
   usuario que las tablas financieras. (RNF-SEG-04)
2. Las consultas de auditoría DEBERÁN pasar por el filtro global por usuario.
   (RNF-SEG-04)
3. El registro de auditoría NO DEBERÁ poder modificarse ni eliminarse desde la
   aplicación. (RF-HIS-04)
4. DEBERÁ existir una prueba de que un usuario no ve registros de auditoría de
   otro, ni siquiera con una consulta directa a la base. (CP-07)

### Requisito 2 — Historial unificado

**Historia:** Como usuario, quiero ver todos mis movimientos en una sola lista,
para revisar qué pasó con mi dinero.

#### Criterios de aceptación

1. El sistema DEBERÁ ofrecer una lista unificada con los ingresos recibidos, los
   pagos de gastos y los ajustes, cada uno con su tipo, fecha, valor con signo,
   descripción y categoría cuando aplique. (RF-HIS-01)
2. La lista DEBERÁ filtrarse por rango de fechas, categoría, tipo de movimiento y
   periodo, y paginarse. (RF-HIS-02)
3. El orden por defecto DEBERÁ ser del más reciente al más antiguo, estable entre
   páginas.
4. La suma de la lista para un periodo DEBERÁ coincidir con los indicadores del
   dashboard de ese periodo. (RF-REP-04, RN-M03)

### Requisito 3 — Detalle de un movimiento

**Historia:** Como usuario, quiero ver el detalle de un movimiento y con qué se
relaciona.

#### Criterios de aceptación

1. El detalle DEBERÁ mostrar el movimiento y su relación con el periodo, el gasto
   (para un pago) y, si existen, el movimiento original o los ajustes que lo
   corrigen. (RF-HIS-03)
2. Un movimiento de otro usuario DEBERÁ responder 404. (RNF-SEG-05)

### Requisito 4 — Auditoría consultable

**Historia:** Como usuario, quiero ver quién cambió un movimiento y qué cambió,
para entender por qué una cifra es distinta.

#### Criterios de aceptación

1. El sistema DEBERÁ ofrecer la auditoría de un movimiento: cada cambio con
   fecha, acción (creación, modificación o eliminación), valores anteriores y
   nuevos. (RF-HIS-04)
2. La auditoría DEBERÁ incluir los movimientos eliminados, para que su historia no
   desaparezca.
3. La auditoría NO DEBERÁ exponer campos internos (marcas de concurrencia,
   identificadores de otras tablas sin significado para el usuario).

### Requisito 5 — Movimientos de ajuste

**Historia:** Como usuario, quiero corregir un error sin borrar la historia, para
que mis cifras sean correctas y trazables.

#### Criterios de aceptación

1. El usuario DEBERÁ poder crear un ajuste sobre un ingreso recibido o un pago de
   gasto, con valor con signo (distinto de cero), fecha y motivo obligatorio.
   (D-08)
2. El ajuste DEBERÁ fecharse en un periodo abierto; el movimiento original NO
   DEBERÁ modificarse. (D-08, RN-P04, RN-M08)
3. SI un ajuste dejara el valor neto del movimiento original por debajo de cero,
   el sistema DEBERÁ rechazarlo.
4. Los ajustes DEBERÁN ser inmutables; una corrección de un ajuste DEBERÁ ser otro
   ajuste. (RN-M08)
5. El motor financiero DEBERÁ contar los ajustes en el periodo de su fecha, y el
   dashboard DEBERÁ reflejarlos. (RNF-MAN-02, RF-PRD-08)
6. CUANDO la API rechace la edición de un movimiento de un periodo cerrado, la
   respuesta DEBERÁ indicar que la corrección se hace con un ajuste. (RF-HIS-05)
7. CUANDO el usuario quiera corregir un pago, el cliente DEBERÁ ofrecer un ajuste
   en lugar de editarlo o eliminarlo. (RF-GAS-06, RN-M08)

### Requisito 6 — Pantallas

**Historia:** Como usuario, quiero usar el historial y los ajustes desde la
aplicación web.

#### Criterios de aceptación

1. El frontend DEBERÁ incluir la lista del historial con sus filtros, el detalle
   de un movimiento y su auditoría. (RF-HIS-01 a RF-HIS-04)
2. El frontend DEBERÁ permitir crear un ajuste desde el detalle de un movimiento,
   con confirmación y explicando el efecto. (RF-HIS-05, RNF-USA-04)

### Requisito 7 — Pruebas

**Historia:** Como mantenedor, quiero confirmar que los ajustes corrigen las
cifras sin perder historia.

#### Criterios de aceptación

1. DEBERÁN existir pruebas del motor con ajustes positivos y negativos, en el
   mismo periodo y en uno posterior al original.
2. DEBERÁ existir una prueba de que corregir un pago de un periodo cerrado con un
   ajuste deja intacto el saldo de cierre y corrige el periodo abierto.
3. DEBERÁN existir pruebas del historial: filtros, paginación estable y
   coincidencia con el dashboard.
4. DEBERÁN existir pruebas de la auditoría: creación, modificación y eliminación,
   y aislamiento entre usuarios.
