# Requirements Document

> Dominio y motor financiero de MiSaldo.

## Introducción

Este spec cubre la capa de dominio de MiSaldo (`MiSaldo.Domain`): las entidades
financieras, las reglas de negocio y el motor único de cálculo que produce todos
los indicadores del dashboard. Es la primera pieza del sistema porque, según el
SRS, "todos los indicadores se calculan en el backend, a partir de movimientos
persistidos" y "el motor financiero vive solo en el backend; el cliente nunca
recalcula saldos" (decisión D-06 de arquitectura, RN-F01 a RN-F14).

El dominio es C# puro, sin dependencias de EF Core, ASP.NET, Cognito ni red.
Se verifica exclusivamente con pruebas unitarias xUnit. El objetivo de este spec
es que todos los cálculos de dinero sean correctos y estén cubiertos por los
casos de aceptación del SRS, antes de construir cualquier capa externa.

**Plataforma:** .NET 10 (LTS, soportado hasta noviembre de 2028).

**Alcance de cálculo:** se implementan las reglas financieras necesarias para F1
(caja, dinero libre, obligaciones, periodos) y F2 (créditos sin intereses y
ahorro), porque el motor de cálculo es una unidad coherente y el SRS define sus
reglas juntas. La persistencia, la API y el frontend quedan fuera de este spec.

**Fuera de alcance:** EF Core, PostgreSQL, Cognito, controladores, Angular,
reportes, recurrencias, presupuestos por categoría, notificaciones e intereses
de crédito (los créditos de MiSaldo nunca tienen intereses, D-06).

### Convenciones

- Todo el dinero se maneja con `decimal`. Los decimales dependen de la moneda
  (COP = 0, USD = 2). El redondeo nunca pierde ni crea dinero (RN-M07).
- Las fechas de movimiento son fecha sin hora (`DateOnly`). "Hoy" depende de la
  zona horaria del usuario, pero el dominio recibe la fecha ya resuelta.
- Código, nombres de tipos y pruebas en inglés; este documento en español.
- Trazabilidad: cada requisito referencia las reglas del SRS que implementa.

---

## Requisitos

### Requisito 1 — Tipo de dinero por moneda

**Historia:** Como motor financiero, quiero representar montos con la precisión
de cada moneda, para que ningún cálculo pierda ni invente dinero.

#### Criterios de aceptación

1. CUANDO se cree un monto con una moneda, el sistema DEBERÁ conservar el número
   de decimales de esa moneda (COP = 0, USD = 2). (RN-M07, RNF-DAT-02)
2. CUANDO se redondee un monto, el sistema DEBERÁ usar redondeo bancario (half
   to even) a los decimales de la moneda. (RN-M07)
3. SI una operación combina montos de monedas distintas, el sistema DEBERÁ
   rechazarla con un error de dominio. (supuesto SRS: una sola moneda principal)
4. CUANDO se sumen o resten varios montos y luego se redondee el total, el
   sistema NO DEBERÁ perder ni crear dinero respecto a la suma exacta. (RN-M07)

### Requisito 2 — Periodo financiero

**Historia:** Como usuario, quiero que mis movimientos se agrupen en periodos con
tipo y fechas fijas, para que los indicadores de cada periodo sean estables.

#### Criterios de aceptación

1. CUANDO se cree un periodo con tipo (semanal, quincenal, mensual), fecha de
   inicio y fecha de fin, el sistema DEBERÁ conservar esos valores sin
   alterarlos por cambios posteriores de configuración. (RF-PRD-04, RN-P06)
2. El sistema DEBERÁ calcular el estado del periodo a partir de una fecha de
   referencia: *Planned* (inicia en el futuro), *Active* (la fecha está dentro),
   *Overdue* (terminó y no está cerrado) o *Closed*. (RF-PRD-05, D-02)
3. SI la fecha de fin es anterior a la de inicio, el sistema DEBERÁ rechazar la
   creación del periodo. (RNF-DAT-05)
4. CUANDO se calcule el rango de fechas de un periodo semanal, quincenal o
   mensual a partir de una fecha base y la configuración, el sistema DEBERÁ
   aplicar las reglas RN-P01, RN-P02 y RN-P03. (RF-PRD-01)
5. SI el usuario cierra un periodo, el sistema DEBERÁ marcarlo como *Closed* y
   calcular su saldo final; el periodo cerrado conserva sus cifras. (RF-PRD-06,
   RN-P04)
6. CUANDO un periodo se cierre con arrastre activo y saldo final positivo, el
   motor DEBERÁ exponer ese saldo como arrastre (carry-over) para el siguiente
   periodo; si el saldo es negativo o el arrastre está inactivo, el arrastre
   DEBERÁ ser cero. (RF-PRD-06, RN-P05, D-03)

### Requisito 3 — Ingresos

**Historia:** Como usuario, quiero registrar ingresos planificados y recibidos,
para que solo los recibidos afecten mi caja.

#### Criterios de aceptación

1. CUANDO se registre un ingreso con descripción, valor, fecha, fuente y tipo, el
   sistema DEBERÁ distinguir su estado entre *Planned* y *Received*. (RF-ING-01,
   RF-ING-02)
2. SI el valor del ingreso es menor o igual a cero, el sistema DEBERÁ rechazarlo
   con un error de dominio. (RF-ING-04)
3. CUANDO un ingreso se marque como *Received*, el sistema DEBERÁ registrar la
   fecha de recepción y contarlo en la caja del periodo que contenga esa fecha.
   (RF-ING-03, RN-F02)
4. SI un ingreso está en estado *Planned*, el sistema NO DEBERÁ contarlo en la
   caja. (RF-ING-02, RN-F01, CP-13)

### Requisito 4 — Gastos y pagos

**Historia:** Como usuario, quiero presupuestar gastos y registrar uno o varios
pagos sobre cada uno, para separar lo presupuestado de lo realmente pagado.

#### Criterios de aceptación

1. CUANDO se registre un gasto con nombre, categoría, valor presupuestado, fecha
   y tipo, el sistema DEBERÁ permitir asociarle cero o más pagos con fecha y
   valor. (RF-GAS-01, RF-GAS-02)
2. El sistema DEBERÁ derivar el estado del gasto sin almacenarlo: *Pending*
   (pagado = 0), *Partial* (0 < pagado < presupuestado) o *Paid* (pagado ≥
   presupuestado). (RF-GAS-03)
3. SI el valor presupuestado o el valor de un pago es menor o igual a cero, el
   sistema DEBERÁ rechazarlo. (RNF-DAT-05)
4. El sistema DEBERÁ calcular la obligación pendiente de un gasto como
   `max(0, presupuestado − total pagado)`. (RN-F04, RF-GAS-05)
5. CUANDO un gasto se pague por más de lo presupuestado, el sistema DEBERÁ
   aceptarlo y dejar la obligación pendiente en cero. (RN-M06)
6. El sistema DEBERÁ asignar cada pago al periodo que contenga su fecha de pago,
   y la obligación pendiente al periodo del gasto. (RN-M02)

### Requisito 5 — Motor de indicadores del periodo

**Historia:** Como dashboard, quiero un único motor que calcule los indicadores
del periodo a partir de los movimientos, para que ninguna pantalla recalcule
saldos por su cuenta.

#### Criterios de aceptación

1. El motor DEBERÁ calcular el ingreso recibido del periodo como la suma de
   ingresos *Received* con fecha de recepción dentro del periodo. (RN-F02)
2. El motor DEBERÁ calcular el gasto pagado del periodo como la suma de pagos de
   gastos más pagos de cuotas de crédito con fecha dentro del periodo. (RN-F03,
   RN-M05, D-05)
3. El motor DEBERÁ calcular la obligación pendiente del periodo como la suma de
   `max(0, presupuestado − pagado)` de los gastos del periodo. (RN-F04)
4. El motor DEBERÁ calcular el dinero en caja como
   `arrastre + ingreso recibido − gasto pagado − aportes de ahorro + retiros de
   ahorro`, con el pago de crédito ya incluido en el gasto pagado. (RN-F07)
5. El motor DEBERÁ calcular el dinero reservado como
   `obligaciones pendientes del periodo + cuotas de crédito pendientes del
   periodo`. (RN-F08)
6. El motor DEBERÁ calcular el dinero libre como `caja − reservado`, y DEBERÁ
   poder ser negativo y exponerse tal cual. (RN-F09, RF-DSH-09)
7. El motor DEBERÁ calcular el consumo del presupuesto como
   `gasto pagado ÷ (ingreso recibido + arrastre)`, evitando la división por cero.
   (RN-F14, D-09)
8. CUANDO cambie cualquier movimiento, el resultado del motor DEBERÁ reflejarlo
   (el motor es una función pura de los movimientos, sin estado cacheado).
   (RF-PRD-08, RNF-DAT-07)
9. El motor DEBERÁ satisfacer los casos CP-01, CP-02, CP-03, CP-10, CP-13 y
   CP-17 con los valores exactos indicados en el SRS.

### Requisito 6 — Ahorro (aportes y retiros)

**Historia:** Como usuario, quiero aportar y retirar de fondos de ahorro, para
separar dinero sin que eso cuente como gasto.

#### Criterios de aceptación

1. CUANDO se registre un aporte de ahorro, el sistema DEBERÁ tratarlo como
   transferencia entre fondos propios: reduce la caja y aumenta el ahorro, y NO
   DEBERÁ contarlo como gasto. (RN-M04, RF-AHO-03, CP-03, CP-19)
2. SI un aporte de ahorro supera el dinero disponible del periodo, el sistema
   DEBERÁ rechazarlo; el aporte DEBERÁ ser menor o igual al disponible.
   (RF-AHO-08, D-10, CP-18)
3. SI un retiro supera el saldo del objetivo (o del ahorro general), el sistema
   DEBERÁ rechazarlo. (RF-AHO-04)
4. El motor DEBERÁ calcular el ahorro acumulado como `Σ aportes − Σ retiros` de
   todos los objetivos y del ahorro general. (RN-F10)
5. El sistema DEBERÁ calcular el progreso de un objetivo (valor y porcentaje)
   respecto a su valor objetivo. (RF-AHO-05)

### Requisito 7 — Créditos sin intereses y recálculo de cuotas

**Historia:** Como usuario, quiero financiar compras en cuotas sin intereses y
que los abonos distintos a la cuota recalculen solo las cuotas futuras, para que
mi saldo y mi calendario reflejen mis pagos reales.

#### Criterios de aceptación

1. CUANDO se cree un crédito con valor original, cuota inicial, número de cuotas,
   fecha de primera cuota y frecuencia, el sistema DEBERÁ generar un calendario
   de cuotas cuya suma sea exactamente el monto financiado, y la última cuota
   DEBERÁ absorber el residuo de redondeo. (RF-CRD-02, RN-C03, CP-04, CP-15)
2. El sistema DEBERÁ calcular el saldo pendiente como
   `valor original − cuota inicial − Σ pagos`. (RN-C01, RF-CRD-04)
3. El sistema solo DEBERÁ manejar créditos sin intereses ni cargos (división
   simple); NO DEBERÁ ofrecer intereses. (RF-CRD-12, D-06)
4. CUANDO se registre un pago, el sistema DEBERÁ aceptar valores iguales,
   menores o mayores a la cuota programada, siempre que sean positivos y no
   superen el saldo pendiente. (RF-CRD-05, RN-C02, RN-C05)
5. CUANDO se registre un pago, el sistema DEBERÁ aplicarlo a la primera cuota
   abierta, recalcular solo las cuotas futuras repartiendo el saldo en partes
   iguales, y la última cuota DEBERÁ absorber el residuo. (RF-CRD-06, RN-C02,
   RN-C03, CP-05, CP-06)
6. Los pagos DEBERÁN ser un historial inmutable; un recálculo NO DEBERÁ
   modificar pagos ya registrados. Una corrección se hace con un ajuste.
   (RF-CRD-07, RN-C06)
7. SI se paga menos de lo programado en la última cuota, el sistema DEBERÁ
   agregar una cuota adicional con el faltante, un periodo de pago después.
   (RN-C04)
8. CUANDO el saldo pendiente llegue a cero, el sistema DEBERÁ marcar el crédito
   como *Paid* y cancelar las cuotas abiertas. (RF-CRD-09)
9. Un pago parcial NO DEBERÁ marcar el crédito como pagado. (RF-CRD-08)
10. El sistema DEBERÁ evitar cuotas negativas y saldos negativos. (RF-CRD-11)
11. El sistema DEBERÁ asociar cada cuota al periodo que contenga su fecha de
    vencimiento, para exponerla como reserva de ese periodo. (RF-CRD-03)

### Requisito 8 — Pago de crédito como gasto del periodo

**Historia:** Como usuario, quiero que el pago de una cuota reduzca mi caja y mi
deuda y aparezca entre mis gastos, sin contarse dos veces.

#### Criterios de aceptación

1. El motor DEBERÁ contar el pago de cuota de crédito dentro del gasto pagado del
   periodo (RN-F03), reduciendo la caja. (RN-M05, D-05, CP-17)
2. El pago de crédito NO DEBERÁ contarse además como un gasto independiente; se
   cuenta una sola vez. (RN-M03, RN-M05)
3. El motor DEBERÁ exponer el total de pagos de crédito del periodo por separado,
   solo para trazabilidad, sin duplicarlo en la caja. (RN-F05)

### Requisito 9 — Deuda pendiente y patrimonio neto

**Historia:** Como usuario, quiero ver cuánto debo y mi patrimonio neto, para
entender mi situación más allá de la caja del periodo.

#### Criterios de aceptación

1. El motor DEBERÁ calcular la deuda pendiente como
   `Σ saldo pendiente de créditos activos + Σ obligaciones vencidas no pagadas`;
   las obligaciones futuras no son deuda. (RN-F11, D-04)
2. El motor DEBERÁ calcular el patrimonio neto como
   `caja acumulada + ahorro acumulado − deuda pendiente`. (RN-F12)

### Requisito 10 — Pureza, invariantes y errores de dominio

**Historia:** Como mantenedor, quiero que el dominio proteja sus invariantes con
errores claros y sin dependencias externas, para que las reglas sean confiables
y verificables de forma aislada.

#### Criterios de aceptación

1. El proyecto `MiSaldo.Domain` NO DEBERÁ depender de EF Core, ASP.NET, Cognito
   ni de ningún paquete de infraestructura o red. (RNF-MAN-01, RNF-MAN-02)
2. CUANDO una operación viole una invariante del dominio (monto no positivo,
   monedas distintas, pago mayor al saldo, retiro mayor al disponible, fechas
   inválidas), el sistema DEBERÁ lanzar una excepción de dominio específica con
   un mensaje claro. (RNF-USA-05, RF-CRD-11)
3. Las operaciones que afecten varias entidades (pago de crédito y recálculo de
   cuotas) DEBERÁN dejar el agregado en un estado consistente o no aplicarse.
   (RNF-DAT-01)
4. La cobertura de pruebas del dominio DEBERÁ ser ≥ 90 %. (RNF-MAN-03)
5. Las pruebas DEBERÁN incluir los casos de aceptación aplicables al dominio:
   CP-01 a CP-06, CP-10, CP-13, CP-15, CP-17, CP-18 y CP-19. (sección 9 del SRS)
