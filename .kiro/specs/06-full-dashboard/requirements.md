# Requirements Document

> Dashboard completo de F1: totales anuales, gastos por categoría y filtros.

## Introduction

Este spec es la sexta etapa. El dashboard actual (`GET /api/v1/dashboard`)
entrega los indicadores de un periodo: ingreso recibido, gasto pagado,
obligaciones, arrastre, caja, reservado, dinero libre y consumo del
presupuesto. Para F1, el SRS pide además:

- los **ingresos y gastos anuales** (RF-DSH-02, RN-F13);
- los datos de los gráficos: **gastos por categoría**, **ingresos contra
  gastos** y el **progreso del consumo del presupuesto** (RF-DSH-05);
- los **filtros** por periodo actual, periodos anteriores, mes, año y categoría
  (RF-DSH-07);
- lo necesario para que el cliente muestre **estados vacíos** y **textos de
  ayuda** por indicador (RF-DSH-04, RF-DSH-08).

Todo se sigue calculando en el backend con el mismo motor (`FinancialEngine`);
la API no recalcula nada por su cuenta (RNF-MAN-02, RF-REP-04).

**Depende de:** spec 03 (dashboard e indicadores).

**Estado:** solo requisitos. El diseño y las tareas se escriben al iniciar esta
etapa.

**Alcance:** API de lectura del dashboard ampliada, pruebas de los cálculos y de
rendimiento con el volumen de referencia del SRS.

**Fuera de alcance:** las pantallas y los gráficos (spec 08); los indicadores de
F2 (ahorro acumulado, deuda pendiente, patrimonio neto, RF-DSH-03); las alertas
(F3); la evolución del saldo y del ahorro (RF-DSH-06, F3).

### Convenciones

- Montos como cadenas decimales (RNF-DAT-02); el cliente no hace aritmética
  financiera.
- Rutas y campos en inglés; este documento en español.

---

## Glossary

- **Indicador:** valor calculado por el motor financiero para un rango de fechas
  (caja, dinero libre, gasto pagado, etc.).
- **Totales anuales:** ingreso recibido y gasto pagado del año calendario
  seleccionado (RN-F13).
- **Desglose por categoría:** gasto pagado y obligación pendiente de cada
  categoría en el rango consultado.
- **Serie mensual:** ingreso recibido y gasto pagado de cada mes de un año, para
  el gráfico de barras.
- **Clave de indicador:** identificador estable (por ejemplo `cashOnHand`) que
  el cliente usa para buscar el texto de ayuda en sus traducciones.

---

## Requirements

### Requisito 1 — Totales anuales

**Historia:** Como usuario, quiero ver cuánto recibí y cuánto pagué en el año,
para tener una visión más amplia que la de un periodo.

#### Criterios de aceptación

1. El dashboard DEBERÁ incluir el ingreso recibido y el gasto pagado del año
   calendario del periodo consultado, o del año indicado. (RF-DSH-02, RN-F13)
2. Los totales anuales DEBERÁN obtenerse del mismo motor de cálculo que los
   indicadores del periodo. (RF-REP-04)
3. Un movimiento DEBERÁ contarse en el año que contiene su fecha de recepción o
   de pago, y una sola vez. (RN-M02, RN-M03)

### Requisito 2 — Gastos por categoría

**Historia:** Como usuario, quiero ver en qué categorías se me va el dinero, para
decidir dónde recortar.

#### Criterios de aceptación

1. El dashboard DEBERÁ incluir, por cada categoría con movimientos en el rango,
   el gasto pagado, la obligación pendiente y su porcentaje sobre el gasto
   pagado total. (RF-DSH-05)
2. Las categorías archivadas con movimientos en el rango DEBERÁN aparecer, marcadas
   como archivadas.
3. La suma de los gastos pagados por categoría DEBERÁ ser igual al gasto pagado
   del rango. (RN-M03)
4. El ahorro NO DEBERÁ aparecer como categoría de gasto. (RF-DSH-02, RN-M04)

### Requisito 3 — Ingresos contra gastos

**Historia:** Como usuario, quiero comparar mes a mes lo que recibo y lo que pago,
para ver si gasto más de lo que gano.

#### Criterios de aceptación

1. El sistema DEBERÁ entregar, para un año, el ingreso recibido y el gasto pagado
   de cada uno de sus 12 meses. (RF-DSH-05)
2. Los meses sin movimientos DEBERÁN aparecer con cero, no omitirse.
3. La suma de los 12 meses DEBERÁ ser igual a los totales anuales del
   Requisito 1.

### Requisito 4 — Progreso del consumo del presupuesto

**Historia:** Como usuario, quiero saber qué parte de mi dinero del periodo ya
gasté, para frenar a tiempo.

#### Criterios de aceptación

1. El dashboard DEBERÁ seguir entregando el consumo del presupuesto como
   `Gasto pagado ÷ (Ingreso recibido + Arrastre)`. (RN-F14, D-09)
2. SI el denominador es cero, el consumo DEBERÁ ser cero y el dashboard DEBERÁ
   indicarlo como sin datos, no como 0 % gastado.
3. El dashboard DEBERÁ indicar si el consumo alcanzó o superó el 80 %, para que
   el cliente lo destaque. (D-09)

### Requisito 5 — Filtros

**Historia:** Como usuario, quiero ver el dashboard de otros periodos, de un mes,
de un año o de una categoría, para revisar mi historia.

#### Criterios de aceptación

1. El dashboard DEBERÁ aceptar como alcance uno de estos: el periodo actual (por
   defecto), un periodo indicado, un mes (`año-mes`) o un año. (RF-DSH-07)
2. CUANDO el alcance sea un mes o un año, los indicadores DEBERÁN calcularse con
   el mismo motor sobre ese rango de fechas; el arrastre DEBERÁ ser el del primer
   periodo del rango si existe, y cero si no.
3. El dashboard DEBERÁ aceptar además un filtro por categoría. CUANDO se use, los
   indicadores de gasto (pagado, obligación, desglose) DEBERÁN limitarse a esa
   categoría, y los indicadores que mezclan ingresos y gastos (caja, dinero
   libre, consumo) NO DEBERÁN entregarse, para no mostrar cifras engañosas.
4. SI el alcance pedido no existe (periodo de otro usuario, mes mal formado), la
   API DEBERÁ responder 404 o 400 según corresponda, sin revelar datos ajenos.
   (RNF-SEG-05)
5. La respuesta DEBERÁ indicar el alcance y el rango de fechas efectivamente
   usados.

### Requisito 6 — Apoyo a textos de ayuda y estados vacíos

**Historia:** Como usuario, quiero entender qué significa cada cifra y saber qué
hacer cuando todavía no tengo datos.

#### Criterios de aceptación

1. Cada indicador DEBERÁ entregarse con una clave estable, para que el cliente
   muestre el texto de ayuda desde sus traducciones. Los textos NO DEBERÁN vivir
   en la API. (RF-DSH-04, RNF-I18-01)
2. El dashboard DEBERÁ indicar, para cada bloque (indicadores, desglose por
   categoría, serie mensual), si hay datos suficientes para mostrarlo.
   (RF-DSH-08)
3. El dinero libre negativo DEBERÁ seguir entregándose tal cual, con su marca de
   negativo. (RF-DSH-09)

### Requisito 7 — Rendimiento

**Historia:** Como usuario, quiero que el dashboard cargue rápido aunque tenga
años de movimientos.

#### Criterios de aceptación

1. Con 10.000 movimientos por usuario, el dashboard DEBERÁ responder en menos de
   2 s en el percentil 95, medido en el entorno de pruebas. (RNF-REN-01)
2. Las consultas del dashboard DEBERÁN usar índices adecuados sobre usuario y
   fechas; SI hace falta un índice nuevo, DEBERÁ crearse con una migración.
   (RNF-DAT-08)
3. Los saldos NO DEBERÁN guardarse precalculados; SI una optimización lo
   requiere, DEBERÁ documentarse cómo se mantiene sincronizada. (RNF-DAT-07)

### Requisito 8 — Pruebas

**Historia:** Como mantenedor, quiero que los cálculos nuevos estén probados con
valores exactos, para confiar en lo que ve el usuario.

#### Criterios de aceptación

1. DEBERÁN existir pruebas de totales anuales, desglose por categoría y serie
   mensual con valores exactos, incluidos movimientos en el borde de un mes y de
   un año.
2. DEBERÁ existir una prueba de que la suma por categoría y la suma de los meses
   coinciden con los totales. (RN-M03)
3. DEBERÁ existir una prueba de cada alcance y del filtro por categoría, y de que
   un periodo de otro usuario no se puede consultar. (CP-07)
4. DEBERÁ existir una prueba de rendimiento con 10.000 movimientos. (RNF-REN-01)
