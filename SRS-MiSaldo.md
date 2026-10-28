# SRS — MiSaldo
## Especificación de Requisitos de Software

| Campo | Valor |
|---|---|
| Producto | MiSaldo |
| Versión del documento | 0.2 |
| Estado | **EN PROCESO** |
| Autor / Product Manager | Carlos Alberto Estrada Valle |
| Fecha | 2026-10-26 |
| Base | Prompt maestro MiSaldo + decisiones tomadas en conversación |

> **Regla de oro para quien implemente (persona o herramienta):** solo se construye lo que está en este documento y en la fase indicada. Si algo no está especificado, **se pregunta; no se inventa**.

---

## 1. Introducción

### 1.1 Propósito
Definir de forma precisa y verificable qué debe hacer MiSaldo, de modo que sirva como única fuente de verdad para el diseño, la implementación y las pruebas.

### 1.2 Alcance
MiSaldo es una aplicación web y móvil para que personas **mayores de 18 años** administren sus ingresos y gastos por periodos de pago, planifiquen obligaciones, controlen créditos y construyan el hábito del ahorro. Los datos se guardan en la nube y cada usuario los ve desde cualquier dispositivo tras iniciar sesión.

**Pilares:** Control · Planificación · Seguimiento · Ahorro · Créditos.

### 1.3 Convenciones del documento
- **ID de requisito:** `RF-<MÓDULO>-<NN>` (funcional), `RNF-<ÁREA>-<NN>` (no funcional), `RN-<NN>` (regla de negocio), `D-<NN>` (decisión pendiente), `CP-<NN>` (caso de prueba).
- **Prioridad (MoSCoW):** **M** = debe, **S** = debería, **C** = podría.
- **Fase:** F1 a F4 (ver sección 10). Un requisito solo se implementa en su fase.
- **Redacción (estilo EARS):** `CUANDO <evento>, el sistema DEBERÁ <respuesta>` · `SI <condición>, el sistema DEBERÁ <respuesta>` · `MIENTRAS <estado>, el sistema DEBERÁ <respuesta>` · `El sistema DEBERÁ <respuesta>`.
- **Idioma:** este documento está en español. Código, nombres de clases, tablas, rutas de API y mensajes de commit van en **inglés**. Los textos que ve el usuario viven en archivos de traducción, nunca dentro del código.

### 1.4 Definiciones
| Término | Definición |
|---|---|
| Periodo | Ciclo financiero con fecha inicial y final (semanal, quincenal o mensual). |
| Ingreso recibido | Ingreso con estado *Recibido*. Solo este afecta la caja. |
| Ingreso planificado | Ingreso esperado que aún no se ha recibido. No afecta la caja. |
| Gasto presupuestado | Valor que el usuario espera gastar. |
| Gasto pagado | Valor efectivamente pagado de un gasto. |
| Obligación pendiente | Parte presupuestada de un gasto que aún no se ha pagado. |
| Fondo de ahorro | Dinero separado de la caja, que sigue siendo del usuario. |
| Crédito | Compra financiada o préstamo pagadero en cuotas. |

---

## 2. Descripción general

### 2.1 Perspectiva del producto
Sistema cliente-servidor con **un solo backend** que consumen dos clientes: web (Angular) y móvil (Ionic + Capacitor, misma base de código).

```
Angular web ──┐
              ├──► API ASP.NET Core ──► PostgreSQL (RDS)
App móvil ────┘          │
                         ├── Amazon Cognito (identidad)
                         └── Amazon SES (correos)
```

### 2.2 Tipos de usuario
| Rol | Descripción | Fase |
|---|---|---|
| Usuario estándar | Mayor de 18 años. Solo ve y modifica sus propios datos. | F1 |
| Administrador de plataforma | Gestión técnica global. Fuera del MVP. | Futuro |

### 2.3 Decisiones ya tomadas
| # | Decisión |
|---|---|
| 1 | Backend en C# / ASP.NET Core, Clean Architecture (Domain, Application, Infrastructure, Api). |
| 2 | Frontend en Angular. Móvil con Ionic + Capacitor (después de estabilizar la web). |
| 3 | Base de datos PostgreSQL en Amazon RDS; una sola base, aislamiento por `UserId`. |
| 4 | Autenticación con Amazon Cognito (Authorization Code + PKCE). |
| 5 | Dinero con `decimal` en C# y `numeric(18,2)` en PostgreSQL. |
| 6 | El motor financiero vive solo en el backend; el cliente nunca recalcula saldos. |
| 7 | Solo mayores de 18 años en el lanzamiento. Fecha de nacimiento obligatoria. |
| 8 | Colombia, COP y español por defecto. Español e inglés en el MVP. |
| 9 | El saldo positivo se arrastra al siguiente periodo por defecto (configurable). |
| 10 | Deuda pendiente es un indicador separado de la caja. |
| 11 | Desarrollador único (PM) asistido por Kiro IDE. |

### 2.4 Restricciones
- Un solo desarrollador: el alcance se entrega por fases.
- Infraestructura en AWS.
- Cumplimiento de la Ley 1581 de 2012 (protección de datos personales, Colombia). **Validar con un abogado antes del lanzamiento.**

### 2.5 Supuestos
- El usuario registra sus movimientos manualmente (no hay conexión con bancos).
- Cada usuario maneja **una sola moneda principal** (sin conversión entre monedas).
- Volumen de referencia: hasta 10.000 movimientos por usuario.

---

## 3. Definiciones financieras (fuente única de cálculo)

Todos los indicadores se calculan **en el backend, a partir de movimientos persistidos**. Ninguna pantalla calcula saldos por su cuenta.

| ID | Indicador | Definición |
|---|---|---|
| RN-F01 | Ingreso total del periodo | Suma de ingresos (planificados + recibidos) del periodo. |
| RN-F02 | Ingreso recibido | Suma de ingresos *Recibidos* con fecha de recepción dentro del periodo. |
| RN-F03 | Gasto pagado | Suma de los pagos de gastos **más los pagos de cuotas de crédito** con fecha de pago dentro del periodo. El pago de crédito cuenta como gasto del periodo (ver D-05). |
| RN-F04 | Obligación pendiente | Por cada gasto del periodo: `max(0, presupuestado − total pagado)`. |
| RN-F05 | Pagos de crédito | Suma de pagos de cuotas con fecha dentro del periodo. Es un **subconjunto** del gasto pagado (RN-F03); se identifica por separado solo para trazabilidad del crédito, no como línea aparte de la caja. |
| RN-F06 | Aportes / retiros de ahorro | Suma de movimientos de ahorro con fecha dentro del periodo. |
| RN-F07 | **Dinero en caja del periodo** | `Arrastre + Ingreso recibido − Gasto pagado − Aportes de ahorro + Retiros de ahorro`. (El pago de crédito ya está incluido en «Gasto pagado».) |
| RN-F08 | Dinero reservado | `Obligaciones pendientes del periodo + cuotas de crédito pendientes del periodo`. |
| RN-F09 | **Dinero libre** | `Dinero en caja − Dinero reservado`. Puede ser negativo y se muestra tal cual. |
| RN-F10 | **Ahorro acumulado** | `Σ aportes − Σ retiros` de todos los objetivos y del ahorro general. |
| RN-F11 | **Deuda pendiente** | `Σ saldo pendiente de créditos activos + Σ obligaciones vencidas no pagadas` (ver D-04). |
| RN-F12 | Patrimonio neto | `Caja acumulada + Ahorro acumulado − Deuda pendiente`. |
| RN-F13 | Ingresos / gastos anuales | Ingreso recibido y gasto pagado del año calendario seleccionado. |
| RN-F14 | Consumo del presupuesto | `Gasto pagado (incluidos pagos de crédito) ÷ (Ingreso recibido + Arrastre)` del periodo (ver D-09). |

**Ejemplo de verificación (debe cumplirse):** ingreso recibido 2.500.000; gastos pagados 800.000 + 300.000 + 100.000; aporte de ahorro 200.000; obligación pendiente 400.000 → **caja 1.100.000, dinero libre 700.000, ahorro acumulado 200.000**.

---

## 4. Requisitos funcionales

### 4.1 Autenticación y cuenta (AUT)
| ID | P | Fase | Requisito |
|---|---|---|---|
| RF-AUT-01 | M | F1 | CUANDO un visitante envíe correo, contraseña, nombre completo, fecha de nacimiento y acepte la política de privacidad y los términos, el sistema DEBERÁ crear la cuenta en Cognito y el perfil en la base de datos. |
| RF-AUT-02 | M | F1 | SI la fecha de nacimiento indica menos de 18 años, el sistema DEBERÁ rechazar el registro con un mensaje claro y NO DEBERÁ crear cuenta ni guardar sus datos. |
| RF-AUT-03 | M | F1 | El sistema DEBERÁ exigir verificación del correo antes de permitir el acceso a datos financieros. |
| RF-AUT-04 | M | F1 | CUANDO el usuario inicie sesión con credenciales válidas, el sistema DEBERÁ entregar un token de acceso de corta duración y un token de refresco. |
| RF-AUT-05 | M | F1 | CUANDO el usuario cierre sesión, el sistema DEBERÁ invalidar el token de refresco. |
| RF-AUT-06 | M | F1 | CUANDO el usuario solicite recuperar su contraseña, el sistema DEBERÁ enviar un enlace o código de recuperación al correo registrado, sin revelar si el correo existe. |
| RF-AUT-07 | M | F1 | SI se superan los intentos fallidos configurados, el sistema DEBERÁ bloquear temporalmente el inicio de sesión. |
| RF-AUT-08 | M | F1 | SI una petición llega sin token válido a una ruta privada, la API DEBERÁ responder 401 y el cliente DEBERÁ redirigir al inicio de sesión. |
| RF-AUT-09 | M | F1 | El sistema DEBERÁ obtener el identificador del usuario **únicamente del token**, nunca de parámetros enviados por el cliente. |
| RF-AUT-10 | C | F4 | El sistema DEBERÁ ofrecer autenticación multifactor opcional. |

### 4.2 Perfil (PER)
| ID | P | Fase | Requisito |
|---|---|---|---|
| RF-PER-01 | M | F1 | El sistema DEBERÁ almacenar: nombre completo, correo, fecha de nacimiento, género (opcional), país, moneda principal, idioma y fecha de creación. |
| RF-PER-02 | M | F1 | CUANDO el usuario edite su perfil, el sistema DEBERÁ validar y guardar los cambios. La fecha de nacimiento NO será editable por el usuario. |
| RF-PER-03 | M | F1 | CUANDO el usuario cambie su correo, el sistema DEBERÁ exigir verificación del nuevo correo antes de aplicarlo. |
| RF-PER-04 | M | F1 | SI el usuario ya tiene movimientos registrados, el sistema NO DEBERÁ permitir cambiar la moneda principal (ver D-07). |
| RF-PER-05 | M | F1 | El sistema NO DEBERÁ solicitar datos personales distintos de los listados en RF-PER-01. |

### 4.3 Configuración (CFG)
| ID | P | Fase | Requisito |
|---|---|---|---|
| RF-CFG-01 | M | F1 | El sistema DEBERÁ permitir elegir tema claro, oscuro o automático según el dispositivo. |
| RF-CFG-02 | M | F1 | El sistema DEBERÁ guardar las preferencias **en el servidor**, de modo que se apliquen en cualquier dispositivo del usuario. |
| RF-CFG-03 | M | F1 | El sistema DEBERÁ permitir elegir idioma (español e inglés en F1). Todas las etiquetas, mensajes de error y formatos DEBERÁN tomarse de archivos de traducción. |
| RF-CFG-04 | M | F1 | El sistema DEBERÁ permitir configurar país, zona horaria, formato de fecha y día de inicio de la semana. |
| RF-CFG-05 | M | F1 | El sistema DEBERÁ permitir configurar el tipo de periodo predeterminado (semanal, quincenal o mensual). |
| RF-CFG-06 | M | F1 | El sistema DEBERÁ permitir configurar si el saldo positivo se arrastra al siguiente periodo (por defecto: sí). |
| RF-CFG-07 | S | F3 | El sistema DEBERÁ permitir configurar preferencias de notificaciones y de visualización del dashboard. |
| RF-CFG-08 | M | F1 | CUANDO el usuario cambie la configuración de periodos, el sistema DEBERÁ aplicarla solo a periodos futuros y NO DEBERÁ modificar periodos existentes. |
| RF-CFG-09 | C | F4 | El sistema DEBERÁ ofrecer chino mandarín, hindi y árabe, este último con dirección de escritura derecha a izquierda (RTL). |

### 4.4 Periodos financieros (PRD)
| ID | P | Fase | Requisito |
|---|---|---|---|
| RF-PRD-01 | M | F1 | El sistema DEBERÁ soportar periodos semanales, quincenales y mensuales con las reglas de RN-P01 a RN-P03. |
| RF-PRD-02 | M | F1 | El sistema DEBERÁ crear un periodo **solo cuando el usuario lo solicite explícitamente**, tomando como base la configuración vigente y permitiéndole ajustar tipo y fechas antes de confirmar. El sistema NO DEBERÁ crear periodos de forma automática al consultar una fecha sin periodo. |
| RF-PRD-02b | M | F1 | CUANDO el usuario cambie la periodicidad de sus ingresos, el sistema DEBERÁ aplicar la nueva periodicidad a los periodos que el usuario cree a partir de ese momento, sin alterar los periodos ya existentes. |
| RF-PRD-03 | M | F1 | SI ya existe un periodo del mismo usuario que se cruza con las fechas solicitadas, el sistema NO DEBERÁ crear otro. La base de datos DEBERÁ garantizarlo con una restricción de integridad. |
| RF-PRD-04 | M | F1 | Cada periodo DEBERÁ conservar su tipo, fecha de inicio y fecha de fin, independientemente de cambios posteriores en la configuración. |
| RF-PRD-05 | M | F1 | El sistema DEBERÁ calcular el estado del periodo: *Planificado* (inicia en el futuro), *Activo* (la fecha de hoy está dentro), *Vencido* (terminó y no se ha cerrado) o *Cerrado*. |
| RF-PRD-06 | M | F1 | CUANDO el usuario cierre un periodo, el sistema DEBERÁ pedir confirmación, calcular su saldo final y, si el arrastre está activo y el saldo es positivo, aplicarlo como saldo inicial del periodo siguiente. |
| RF-PRD-06b | M | F1 | SI existe un periodo en estado *Vencido*, el sistema DEBERÁ exigir que el usuario lo cierre antes de permitirle continuar o modificar el periodo siguiente. |
| RF-PRD-07 | M | F1 | El dashboard DEBERÁ indicar claramente si el saldo se arrastra y por cuánto. |
| RF-PRD-08 | M | F1 | CUANDO se modifique cualquier movimiento, el sistema DEBERÁ reflejar el cambio en los indicadores del periodo de inmediato. |

### 4.5 Ingresos (ING)
| ID | P | Fase | Requisito |
|---|---|---|---|
| RF-ING-01 | M | F1 | El sistema DEBERÁ permitir registrar varios ingresos por periodo con: descripción, valor, fecha, fuente, tipo y observaciones opcionales. |
| RF-ING-02 | M | F1 | El sistema DEBERÁ diferenciar ingresos *Planificados* de *Recibidos*. |
| RF-ING-03 | M | F1 | CUANDO el usuario marque un ingreso como recibido, el sistema DEBERÁ registrar la fecha de recepción y actualizar la caja. |
| RF-ING-04 | M | F1 | SI el valor es menor o igual a cero, el sistema DEBERÁ rechazar el ingreso con un mensaje comprensible. |
| RF-ING-05 | M | F1 | CUANDO el usuario edite o elimine un ingreso, el sistema DEBERÁ pedir confirmación y recalcular los indicadores afectados. |
| RF-ING-06 | S | F2 | El sistema DEBERÁ permitir programar ingresos recurrentes sin crear duplicados. |

### 4.6 Categorías (CAT)
| ID | P | Fase | Requisito |
|---|---|---|---|
| RF-CAT-01 | M | F1 | CUANDO se cree una cuenta, el sistema DEBERÁ crear las categorías iniciales del usuario (ver D-05): Arriendo/vivienda, Servicios públicos, Alimentación y mercado, Transporte y gasolina, Salud, Educación, Entretenimiento, Compras personales, Suscripciones, Otros. |
| RF-CAT-02 | M | F1 | El sistema DEBERÁ permitir crear, renombrar y ordenar categorías propias. |
| RF-CAT-03 | M | F1 | SI una categoría tiene movimientos asociados, el sistema NO DEBERÁ eliminarla; DEBERÁ archivarla y conservar el historial. |
| RF-CAT-04 | M | F1 | El nombre de una categoría DEBERÁ ser único por usuario (sin distinguir mayúsculas). |

### 4.7 Gastos (GAS)
| ID | P | Fase | Requisito |
|---|---|---|---|
| RF-GAS-01 | M | F1 | El sistema DEBERÁ permitir registrar gastos con: nombre, categoría, valor presupuestado, fecha, tipo (fijo, variable, extraordinario, recurrente) y observaciones opcionales. |
| RF-GAS-02 | M | F1 | El sistema DEBERÁ permitir registrar uno o varios **pagos** sobre un gasto (fecha y valor). |
| RF-GAS-03 | M | F1 | El sistema DEBERÁ derivar el estado del gasto: *Pendiente* (pagado = 0), *Parcial* (0 < pagado < presupuestado) o *Pagado* (pagado ≥ presupuestado). El estado NO se almacena como dato editable independiente. |
| RF-GAS-04 | M | F1 | CUANDO se registre un pago, el sistema DEBERÁ actualizar la caja y el dinero libre de inmediato. |
| RF-GAS-05 | M | F1 | Los gastos pendientes NO DEBERÁN contarse como gastos pagados; DEBERÁN mostrarse como obligación reservada. |
| RF-GAS-06 | M | F1 | CUANDO el usuario edite o elimine un gasto o un pago, el sistema DEBERÁ pedir confirmación y recalcular. |
| RF-GAS-07 | S | F2 | El sistema DEBERÁ permitir gastos recurrentes que generen su ocurrencia en los periodos futuros **sin duplicarla**. |
| RF-GAS-08 | S | F2 | El sistema DEBERÁ permitir definir un presupuesto por categoría y periodo. |

### 4.8 Créditos y cuotas (CRD)
| ID | P | Fase | Requisito |
|---|---|---|---|
| RF-CRD-01 | M | F2 | El sistema DEBERÁ permitir registrar un crédito con: nombre, descripción, valor original, fecha de compra, cuota inicial, número de cuotas, fecha de primera cuota y frecuencia de pago. |
| RF-CRD-02 | M | F2 | CUANDO se cree un crédito sin intereses, el sistema DEBERÁ generar el calendario de cuotas. La suma de las cuotas DEBERÁ ser exactamente igual al monto financiado (la última cuota absorbe el residuo de redondeo). |
| RF-CRD-03 | M | F2 | El sistema DEBERÁ asociar cada cuota al periodo que contenga su fecha de vencimiento y mostrarla como reserva en ese periodo. |
| RF-CRD-04 | M | F2 | El sistema DEBERÁ mostrar el valor original **separado** del saldo pendiente. |
| RF-CRD-05 | M | F2 | CUANDO el usuario registre un pago, el sistema DEBERÁ aceptar valores iguales, menores o mayores a la cuota programada, siempre que sean positivos y no superen el saldo pendiente. |
| RF-CRD-06 | M | F2 | CUANDO se registre un pago, el sistema DEBERÁ actualizar el saldo pendiente y **recalcular solo las cuotas futuras**, repartiendo el saldo en partes iguales entre las cuotas abiertas. |
| RF-CRD-07 | M | F2 | Los pagos DEBERÁN ser un historial inmutable. Una corrección se hace con un movimiento de ajuste, nunca editando el pago original. |
| RF-CRD-08 | M | F2 | Un pago parcial NO DEBERÁ marcar el crédito como pagado. |
| RF-CRD-09 | M | F2 | CUANDO el saldo pendiente llegue a cero, el sistema DEBERÁ marcar el crédito como *Pagado* y cancelar las cuotas abiertas. |
| RF-CRD-10 | M | F2 | SI el usuario modifica un crédito que ya tiene pagos, el sistema DEBERÁ explicar el efecto y pedir confirmación. |
| RF-CRD-11 | M | F2 | El sistema DEBERÁ evitar cuotas negativas, saldos negativos y pagos duplicados (ver RNF-DAT-03). |
| RF-CRD-12 | M | F2 | El sistema solo manejará créditos **sin intereses ni cargos**, mediante división simple del monto financiado. El sistema NO DEBERÁ ofrecer la opción de intereses (ver D-06). |

### 4.9 Ahorro (AHO)
| ID | P | Fase | Requisito |
|---|---|---|---|
| RF-AHO-01 | M | F2 | El sistema DEBERÁ permitir crear objetivos de ahorro con nombre, valor objetivo y fecha deseada. |
| RF-AHO-02 | M | F2 | El sistema DEBERÁ permitir registrar aportes y retiros, asociados a un objetivo (o al ahorro general) y a un periodo. |
| RF-AHO-03 | M | F2 | Un aporte DEBERÁ registrarse como transferencia entre fondos propios y NO DEBERÁ contarse como gasto. |
| RF-AHO-04 | M | F2 | Los retiros DEBERÁN ser movimientos independientes. SI un retiro supera el saldo del objetivo, el sistema DEBERÁ rechazarlo. |
| RF-AHO-05 | M | F2 | El sistema DEBERÁ mostrar el progreso (valor y porcentaje) de cada objetivo y el ahorro acumulado total. |
| RF-AHO-06 | S | F2 | El sistema DEBERÁ permitir definir un ahorro planificado por periodo (valor fijo o porcentaje de los ingresos) y recomendarlo al inicio del periodo, sin imponerlo. |
| RF-AHO-07 | S | F2 | El sistema DEBERÁ diferenciar ahorro disponible, ahorro comprometido en objetivos y aportes planificados aún no realizados. |
| RF-AHO-08 | M | F2 | SI un aporte de ahorro supera el dinero disponible del periodo, el sistema DEBERÁ advertirlo y **rechazar** la operación. El aporte DEBERÁ ser menor o igual al dinero disponible (ver D-10). |

### 4.10 Dashboard (DSH)
| ID | P | Fase | Requisito |
|---|---|---|---|
| RF-DSH-01 | M | F1 | El dashboard DEBERÁ ser la pantalla inicial tras iniciar sesión. |
| RF-DSH-02 | M | F1 | El dashboard DEBERÁ mostrar: ingresos del periodo, gastos del periodo (incluidos los pagos de crédito), dinero en caja, dinero libre, ingresos anuales y gastos anuales. Los pagos de crédito DEBERÁN presentarse dentro de «Gastos», identificables como pago de crédito. El ahorro NO DEBERÁ mostrarse dentro de «Gastos». |
| RF-DSH-03 | M | F2 | El dashboard DEBERÁ mostrar además: ahorro acumulado (en su propio apartado, separado de los gastos), deuda pendiente y patrimonio neto. |
| RF-DSH-04 | M | F1 | Cada indicador DEBERÁ tener una descripción accesible de lo que significa (por ejemplo, un texto de ayuda). |
| RF-DSH-05 | M | F1 | El dashboard DEBERÁ incluir: gráfico de dona de gastos por categoría, barras de ingresos vs. gastos, y progreso del consumo del presupuesto. |
| RF-DSH-06 | S | F3 | El dashboard DEBERÁ incluir: línea de evolución del saldo, indicador de porcentaje de ahorro y evolución de la deuda. |
| RF-DSH-07 | M | F1 | El dashboard DEBERÁ permitir filtrar por periodo actual, periodos anteriores, mes, año y categoría. |
| RF-DSH-08 | M | F1 | SI no hay datos suficientes para un gráfico, el sistema DEBERÁ mostrar un estado vacío que explique cómo empezar. |
| RF-DSH-09 | M | F1 | SI el saldo es negativo, el sistema DEBERÁ mostrar una alerta clara **sin ocultar el valor real**. |
| RF-DSH-10 | M | F1 | El sistema NO DEBERÁ comunicar información solo mediante color; DEBERÁ acompañarla de texto o icono. |

### 4.11 Historial (HIS)
| ID | P | Fase | Requisito |
|---|---|---|---|
| RF-HIS-01 | M | F1 | El sistema DEBERÁ ofrecer una lista unificada de movimientos: ingresos, pagos de gastos, y (desde F2) pagos de crédito, aportes, retiros y ajustes. |
| RF-HIS-02 | M | F1 | El sistema DEBERÁ permitir filtrar por rango de fechas, categoría, tipo de movimiento y periodo, con paginación. |
| RF-HIS-03 | M | F1 | El sistema DEBERÁ mostrar el detalle de cada movimiento y su relación con periodo, gasto, crédito u objetivo. |
| RF-HIS-04 | M | F1 | El sistema DEBERÁ registrar en un log de auditoría la creación, modificación y eliminación de movimientos (quién, cuándo, valor anterior y nuevo). |
| RF-HIS-05 | M | F1 | SI el usuario intenta modificar un movimiento de un periodo cerrado, el sistema DEBERÁ explicar que se hará mediante un movimiento de ajuste que conserva el original (ver D-08). |

### 4.12 Notificaciones y alertas (NOT)
| ID | P | Fase | Requisito |
|---|---|---|---|
| RF-NOT-01 | M | F3 | El sistema DEBERÁ generar alertas dentro de la aplicación para: consumo del presupuesto ≥ 80 %, gastos superiores a ingresos, cuota próxima a vencer, objetivo de ahorro alcanzado y categoría que gasta más que en periodos anteriores. |
| RF-NOT-02 | M | F3 | Las recomendaciones DEBERÁN basarse en los datos registrados y NO DEBERÁN presentarse como resultados garantizados ni como asesoría financiera. |
| RF-NOT-03 | S | F3 | El sistema DEBERÁ evitar enviar la misma alerta repetidamente para el mismo evento. |
| RF-NOT-04 | C | F4 | El sistema DEBERÁ enviar recordatorios por correo (SES) y notificaciones push en la app móvil. |

### 4.13 Reportes (REP)
| ID | P | Fase | Requisito |
|---|---|---|---|
| RF-REP-01 | M | F3 | El sistema DEBERÁ generar reportes por periodo, mes, año, rango personalizado, categoría, crédito y objetivo de ahorro. |
| RF-REP-02 | M | F3 | Cada reporte DEBERÁ incluir: datos generales, ingresos (planificados y recibidos), gastos (presupuestados y pagados), obligaciones pendientes, distribución por categoría, caja y dinero libre, ahorro, créditos y comparación entre periodos. |
| RF-REP-03 | M | F3 | El sistema DEBERÁ exportar a PDF, Excel y CSV, mostrando moneda, fechas y filtros aplicados. |
| RF-REP-04 | M | F3 | Los totales de un reporte DEBERÁN obtenerse del **mismo motor de cálculo** que el dashboard, nunca de cálculos independientes. |

### 4.14 Privacidad y datos personales (PRV)
| ID | P | Fase | Requisito |
|---|---|---|---|
| RF-PRV-01 | M | F1 | El sistema DEBERÁ guardar la versión y la fecha de aceptación de la política de privacidad y de los términos de uso. El contenido de la política vive en el documento [PRIVACIDAD-MiSaldo.md](./PRIVACIDAD-MiSaldo.md). |
| RF-PRV-02 | M | F1 | CUANDO el usuario lo solicite, el sistema DEBERÁ permitirle descargar todos sus datos en formato JSON. |
| RF-PRV-03 | M | F1 | CUANDO el usuario solicite eliminar su cuenta, el sistema DEBERÁ pedir confirmación reforzada y eliminar todos sus datos personales y financieros. DEBERÁ conservar únicamente un registro de la eliminación con el **motivo** y la identidad de **quién realizó el movimiento**, además de lo exigido legalmente (ver D-12). |

---

## 5. Requisitos no funcionales

### 5.1 Seguridad
| ID | P | Requisito |
|---|---|---|
| RNF-SEG-01 | M | Toda comunicación DEBERÁ usar HTTPS (TLS 1.2 o superior). |
| RNF-SEG-02 | M | El token de acceso DEBERÁ tener vida corta (15 a 60 min). El de refresco DEBERÁ ser rotativo y revocable. |
| RNF-SEG-03 | M | En web, los tokens NO DEBERÁN guardarse en `localStorage`. En móvil, DEBERÁN guardarse en almacenamiento seguro del sistema (Keychain/Keystore). |
| RNF-SEG-04 | M | **Aislamiento por usuario:** toda entidad financiera DEBERÁ incluir `UserId`; toda consulta DEBERÁ filtrarse por el usuario autenticado (filtro global de EF Core) y se DEBERÁ evaluar una segunda barrera en la base de datos (Row Level Security). |
| RNF-SEG-05 | M | SI un usuario solicita un recurso de otro usuario, la API DEBERÁ responder 404 (o 403) sin revelar si el recurso existe ni su contenido. |
| RNF-SEG-06 | M | Toda entrada DEBERÁ validarse en el servidor. Solo se usarán consultas parametrizadas / EF Core. |
| RNF-SEG-07 | M | DEBERÁ existir limitación de solicitudes (rate limiting) en la API, con límites más estrictos en autenticación. |
| RNF-SEG-08 | M | Los secretos DEBERÁN guardarse en AWS Secrets Manager o variables de entorno. **Ningún secreto en el repositorio.** Se incluirá `.env.example` sin valores reales. |
| RNF-SEG-09 | M | La política de contraseñas DEBERÁ exigir mínimo 10 caracteres. |
| RNF-SEG-10 | M | Los registros (logs) NO DEBERÁN contener contraseñas, tokens ni datos personales innecesarios. |
| RNF-SEG-11 | S | Se DEBERÁ ejecutar análisis de vulnerabilidades en dependencias dentro del CI. |

### 5.2 Integridad de datos
| ID | P | Requisito |
|---|---|---|
| RNF-DAT-01 | M | Las operaciones que afecten varias tablas (por ejemplo, un pago de crédito y el recálculo de cuotas) DEBERÁN ejecutarse en **una transacción**. |
| RNF-DAT-02 | M | Los montos se almacenarán como `numeric(18,2)`, se manejarán como `decimal` y se transmitirán por la API como **cadenas decimales** (por ejemplo, `"1500000.00"`). El cliente NO DEBERÁ hacer aritmética financiera. |
| RNF-DAT-03 | M | Las operaciones de pago DEBERÁN ser **idempotentes** mediante una clave de idempotencia, para que un doble clic o reintento no registre dos veces el mismo pago. |
| RNF-DAT-04 | M | La edición concurrente DEBERÁ controlarse con concurrencia optimista (versión de fila). |
| RNF-DAT-05 | M | La base de datos DEBERÁ aplicar restricciones: montos válidos (`CHECK`), llaves foráneas, unicidad de periodos por usuario y de categorías por usuario. |
| RNF-DAT-06 | M | Las fechas de movimientos se guardarán como fecha (sin hora); las marcas de tiempo, en UTC. «Hoy» se calcula con la zona horaria del usuario. |
| RNF-DAT-07 | M | Los saldos NO se guardarán como valores independientes; se calcularán desde los movimientos. Si más adelante se almacenan para rendimiento, se DEBERÁ documentar su sincronización. |
| RNF-DAT-08 | M | Cambios de esquema SOLO mediante migraciones versionadas. |

### 5.3 Rendimiento y disponibilidad
| ID | P | Requisito |
|---|---|---|
| RNF-REN-01 | M | El dashboard DEBERÁ cargar en menos de 2 s (p95) con hasta 10.000 movimientos por usuario. |
| RNF-REN-02 | M | Las operaciones comunes de la API DEBERÁN responder en menos de 500 ms (p95). |
| RNF-DIS-01 | S | Disponibilidad objetivo del MVP: 99,5 %. |
| RNF-DIS-02 | M | Copias de seguridad automáticas con recuperación a un punto en el tiempo. Objetivo: RPO ≤ 1 h, RTO ≤ 4 h. DEBERÁ probarse una restauración antes del lanzamiento. |

### 5.4 Usabilidad, accesibilidad e internacionalización
| ID | P | Requisito |
|---|---|---|
| RNF-USA-01 | M | Diseño *mobile-first* y responsive para teléfono, tableta y computador. Menú lateral en escritorio y navegación inferior en móvil. |
| RNF-USA-02 | M | Cumplimiento de WCAG 2.1 nivel AA (contraste, teclado, lectores de pantalla). |
| RNF-USA-03 | M | Verde para saldos positivos y ahorro, rojo para gastos y alertas, siempre acompañado de texto o icono. Temas claro y oscuro con paleta consistente. |
| RNF-USA-04 | M | Confirmación antes de cualquier operación destructiva o que afecte información financiera. |
| RNF-USA-05 | M | Formularios con validación inmediata y mensajes comprensibles. |
| RNF-I18-01 | M | Ningún texto visible DEBERÁ estar escrito directamente en el código. Montos y fechas DEBERÁN formatearse según la configuración regional del usuario. |
| RNF-I18-02 | M | La interfaz DEBERÁ construirse con propiedades CSS lógicas para quedar lista para RTL. |

### 5.5 Mantenibilidad y calidad
| ID | P | Requisito |
|---|---|---|
| RNF-MAN-01 | M | Clean Architecture: Domain no depende de ninguna otra capa; Application depende solo de Domain; Infrastructure y Api dependen hacia el centro. |
| RNF-MAN-02 | M | La lógica financiera DEBERÁ vivir solo en `Domain`. Los controladores NO contienen reglas de negocio. |
| RNF-MAN-03 | M | Cobertura de pruebas mínima: Domain ≥ 90 %, Application ≥ 70 %. |
| RNF-MAN-04 | M | DEBERÁ existir integración continua que compile y ejecute todas las pruebas en cada cambio. |
| RNF-MAN-05 | M | Logs estructurados con identificador de correlación por solicitud. |
| RNF-MAN-06 | M | La API DEBERÁ versionarse (`/api/v1`) y documentarse con OpenAPI. Los errores usarán el formato Problem Details (RFC 7807). |

---

## 6. Reglas de negocio

### 6.1 Periodos
| ID | Regla |
|---|---|
| RN-P01 | **Semanal:** 7 días que empiezan en el día de la semana configurado. |
| RN-P02 | **Quincenal:** dos ciclos por mes con día de inicio configurable. Por defecto: ciclo 1 del día 1 al 15 y ciclo 2 del día 16 al fin de mes. Si el día de inicio del ciclo 2 es *B* y el del ciclo 1 es *A*, el ciclo 1 va de *A* a *B−1* y el ciclo 2 de *B* a *A−1* del mes siguiente. |
| RN-P03 | **Mensual:** por defecto, del día 1 al último día del mes. Se permite configurar un día de inicio entre 1 y 28 (por ejemplo, el día de pago). |
| RN-P04 | Un periodo cerrado conserva sus cifras; cualquier corrección posterior se registra como ajuste (ver D-08). |
| RN-P05 | El arrastre solo aplica si está activo y el saldo del periodo cerrado es positivo (ver D-03). |
| RN-P06 | Los periodos los crea el usuario de forma explícita; el sistema nunca los genera automáticamente (ver D-01). |
| RN-P07 | Un periodo *Vencido* debe cerrarse antes de continuar o modificar el periodo siguiente (ver D-02). |

### 6.2 Movimientos
| ID | Regla |
|---|---|
| RN-M01 | Lo presupuestado nunca se confunde con lo recibido o pagado. |
| RN-M02 | Un pago pertenece al periodo que contiene **su fecha de pago**; la obligación pendiente pertenece al periodo del gasto. |
| RN-M03 | Ningún movimiento se contabiliza dos veces. |
| RN-M04 | Un aporte de ahorro es una transferencia entre fondos propios: reduce la caja y aumenta el ahorro; no es gasto. |
| RN-M05 | Un pago de crédito **es un gasto del periodo**: reduce la caja y reduce la deuda, y se contabiliza dentro del «Gasto pagado» y se muestra en la categoría «Gastos» del dashboard. Conserva su vínculo con el crédito para trazabilidad, pero no se duplica como movimiento aparte (ver D-05). |
| RN-M06 | Un gasto pagado por más de lo presupuestado es válido; la obligación pendiente queda en 0. |
| RN-M07 | Los redondeos usan los decimales de la moneda (COP = 0, USD = 2) y nunca pierden ni crean dinero. |
| RN-M08 | Los movimientos con historial no se eliminan físicamente; se anulan o se ajustan conservando trazabilidad. |

### 6.3 Créditos
| ID | Regla |
|---|---|
| RN-C01 | `Saldo pendiente = Valor original − Cuota inicial − Σ pagos`. |
| RN-C02 | Un pago se aplica a la primera cuota abierta. Pagos mayores o menores a la cuota se aceptan. |
| RN-C03 | Tras un pago, el saldo se reparte en partes iguales entre las cuotas abiertas; la última absorbe el residuo. |
| RN-C04 | Si se paga menos de lo programado en la **última** cuota, el sistema agrega una cuota adicional con el faltante, un periodo de pago después. |
| RN-C05 | El pago nunca puede superar el saldo pendiente. |
| RN-C06 | Los pagos registrados nunca se modifican al recalcular. |

---

## 7. Modelo de datos conceptual

Todas las entidades financieras incluyen: `Id` (UUID), `UserId`, `CreatedAt`, `UpdatedAt` y versión de fila. El diseño detallado (columnas, tipos, índices) se entrega en el documento de diseño, no aquí.

| Entidad | Propósito | Fase |
|---|---|---|
| User | Cuenta (vinculada a Cognito), `AccountType` (`Standard`; futuro `Child`), `GuardianUserId` (nulo) | F1 |
| UserProfile | Datos del perfil | F1 |
| UserSettings | Preferencias personales y financieras | F1 |
| ConsentRecord | Aceptación de política y términos | F1 |
| FinancialPeriod | Periodo con tipo, fechas y estado | F1 |
| ExpenseCategory | Categorías del usuario | F1 |
| IncomeTransaction | Ingresos | F1 |
| Expense | Gasto (presupuestado) | F1 |
| ExpensePayment | Pago de un gasto | F1 |
| AuditLog | Cambios relevantes | F1 |
| AccountDeletionRecord | Registro de eliminación de cuenta: motivo, autor del movimiento y fecha (sin datos personales) | F1 |
| Credit / CreditInstallment / CreditPayment | Créditos, cuotas y pagos | F2 |
| SavingsGoal / SavingsTransaction | Objetivos y movimientos de ahorro | F2 |
| RecurringTransaction | Operaciones recurrentes | F2 |
| Budget | Presupuesto por categoría y periodo | F2 |
| Notification | Alertas | F3 |

---

## 8. Interfaces externas
- **API REST** en `/api/v1`, JSON, fechas ISO 8601, montos como cadenas decimales, errores en Problem Details, paginación en listados.
- **Amazon Cognito:** registro, inicio de sesión, recuperación y verificación. La API valida el token y extrae el identificador del usuario.
- **Amazon SES:** correos transaccionales (F3 en adelante).
- **EventBridge Scheduler:** tareas programadas como recurrencias y alertas (F2 en adelante).

---

## 9. Casos de prueba de aceptación

| ID | Escenario | Resultado esperado |
|---|---|---|
| CP-01 | Ingreso recibido 2.000.000; gasto pagado 500.000 | Caja 1.500.000 |
| CP-02 | Ingreso 2.000.000; pagado 500.000; obligación pendiente 300.000 | Caja 1.500.000; dinero libre 1.200.000 |
| CP-03 | Ingreso 2.000.000; aporte de ahorro 200.000 | Caja 1.800.000; ahorro +200.000; gasto 0 |
| CP-04 | Crédito de 1.500.000 en 3 cuotas sin intereses | 3 cuotas de 500.000 |
| CP-05 | Abono de 600.000 en la cuota 1 | Saldo 900.000; cuotas futuras de 450.000 |
| CP-06 | Abono de 400.000 en la cuota 1 | Saldo 1.100.000; cuotas futuras de 550.000 |
| CP-07 | Usuario A consulta un movimiento del usuario B | Acceso denegado sin revelar datos |
| CP-08 | Cambio de periodicidad de mensual a quincenal | Periodos históricos intactos; solo cambian los futuros |
| CP-09 | Reporte anual vs. movimientos | Totales idénticos, sin duplicados |
| CP-10 | Ejemplo de la sección 3 (2.500.000) | Caja 1.100.000; libre 700.000; ahorro 200.000 |
| CP-11 | Registro con fecha de nacimiento de menor de 18 años | Registro rechazado; no se crea cuenta |
| CP-12 | Mismo pago enviado dos veces con la misma clave de idempotencia | Se registra una sola vez |
| CP-13 | Ingreso planificado sin marcar como recibido | No afecta la caja |
| CP-14 | Cierre de periodo con saldo positivo y arrastre activo | El siguiente periodo inicia con ese saldo |
| CP-15 | Crédito de 1.000.000 en 3 cuotas | Cuotas 333.333, 333.333 y 333.334 (suma exacta) |
| CP-16 | Inicio de sesión en dos dispositivos | Ambos muestran los mismos datos |
| CP-17 | Ingreso recibido 2.000.000; pago de cuota de crédito 500.000 | Caja 1.500.000; el pago aparece dentro de «Gastos» como pago de crédito; gasto pagado del periodo = 500.000 |
| CP-18 | Aporte de ahorro 300.000 con dinero disponible 200.000 | Operación rechazada; el aporte no puede superar el disponible |
| CP-19 | Aporte de ahorro 200.000 con dinero disponible 200.000 | Operación aceptada; el aporte se muestra en el apartado «Ahorro», nunca en «Gastos» |
| CP-20 | Usuario consulta una fecha sin periodo | No se crea ningún periodo automáticamente; se ofrece crearlo explícitamente |
| CP-21 | Periodo en estado «Vencido» sin cerrar | El sistema impide continuar o modificar el periodo siguiente hasta cerrarlo |

---

## 10. Plan por fases

| Fase | Contenido | Salida verificable |
|---|---|---|
| **F1 — Fundamentos y motor base** | Autenticación, perfil, configuración (es/en, temas), categorías, periodos, ingresos, gastos y pagos, dashboard básico, historial y auditoría, privacidad mínima | Un usuario se registra, registra ingresos y gastos, y ve su caja y dinero libre correctos desde dos dispositivos |
| **F2 — Créditos y ahorro** | Créditos sin intereses con recálculo de cuotas, ahorro y objetivos, recurrentes, presupuestos por categoría, dashboard completo | Casos CP-03 a CP-06, CP-10 y CP-15 pasan |
| **F3 — Reportes y alertas** | Reportes, exportación PDF/Excel/CSV, notificaciones en la app, gráficos avanzados | CP-09 pasa; reportes coinciden con el dashboard |
| **F4 — Alcance y móvil** | Idiomas adicionales (RTL), app móvil Android/iOS, push y correos, MFA | App publicada en tiendas |
| **Futuro** | Cuentas familiares/menores, administración global, conexión bancaria | — |

---

## 11. Fuera de alcance (por ahora)
- Menores de 18 años y cuentas familiares.
- Conexión automática con bancos.
- Multimoneda con conversión de divisas.
- Presupuestos compartidos entre usuarios.
- Inversiones, impuestos, lectura de facturas o asesoría financiera personalizada.
- Créditos con intereses o cargos (no forman parte del proyecto).

---

## 12. Decisiones pendientes de aprobación

Cada una trae una propuesta. Si la propuesta es aceptable, basta con aprobarla.

| ID | Tema | Decisión final | Estado |
|---|---|---|---|
| D-01 | Creación de periodos | **Rechazada la creación automática.** El usuario crea sus periodos; puede cambiar la periodicidad de sus ingresos en cualquier momento. El sistema no crea periodos sin una acción explícita del usuario (RF-PRD-02). | **Rechazada** |
| D-02 | Estado «Vencido» | Periodo cuya fecha de fin ya pasó y no ha sido cerrado por el usuario. El usuario **debe cerrarlo** para continuar o modificar el periodo siguiente. | **Aprobada** |
| D-03 | Saldo negativo y arrastre | Solo se arrastra el saldo **positivo**; el negativo se muestra como alerta en el periodo cerrado. | **Aprobada** |
| D-04 | Deuda pendiente | Saldo de créditos activos + obligaciones **vencidas** no pagadas (las futuras no son deuda). | **Aprobada** |
| D-05 | Ahorro y pagos de crédito | **Ajustada.** El **pago de crédito es un gasto del periodo**: reduce el monto disponible y se muestra dentro de la categoría «Gastos» del dashboard (conservando su trazabilidad con el crédito). El **ahorro** sale de caja y reduce el disponible, pero se muestra en el apartado «Ahorro» y **nunca** en «Gastos». | **Aprobada (ajustada)** |
| D-06 | Créditos con intereses | **Reforzada.** Los créditos de MiSaldo **nunca** tendrán intereses ni cargos en este proyecto. Solo se manejan créditos sin intereses (división simple). | **Aprobada** |
| D-07 | Cambio de moneda | Bloqueado una vez existan movimientos. | **Aprobada** |
| D-08 | Correcciones en periodos cerrados | Se hacen con **movimiento de ajuste** en el periodo abierto o con reapertura controlada y auditada; el original se conserva. | **Aprobada** |
| D-09 | «Presupuesto» para la alerta del 80 % | `(Gasto pagado, incluidos pagos de crédito) ÷ (Ingreso recibido + Arrastre)` del periodo. | **Aprobada** |
| D-10 | Aporte de ahorro mayor que la caja | **Cambiada.** El aporte de ahorro **no puede superar** el dinero disponible del periodo: debe ser menor o igual. Si se excede, el sistema advierte y **no permite** la operación. | **Aprobada (cambiada)** |
| D-11 | Frecuencia de cuotas de crédito | Mensual en F2; quincenal y semanal en una fase posterior. | **Aprobada** |
| D-12 | Eliminación de cuenta | **Ajustada.** Al eliminar la cuenta se borran **todos** los datos personales y financieros, pero se conserva un registro con el **motivo** de la eliminación y **quién realizó el movimiento**. Validar con abogado lo que la ley exija conservar. | **Aprobada (ajustada)** |

---

## 13. Riesgos
| Riesgo | Mitigación |
|---|---|
| Alcance demasiado grande para una persona | Entrega por fases; F1 es un producto utilizable por sí solo. |
| Errores de cálculo con dinero real | Motor único en Domain, pruebas por cada regla y casos CP-xx obligatorios. |
| Fuga de datos entre usuarios | Filtro global + RLS + prueba automática CP-07. |
| Cumplimiento legal (datos personales) | Revisión con abogado antes del lanzamiento; política y términos versionados. |
| La herramienta de desarrollo se sale del alcance | Este SRS, reglas de dirección (steering) y tareas pequeñas verificables. |
| Costos de AWS | Empezar con servicios administrados pequeños y alertas de facturación. |

---

## 14. Aprobación

| Rol | Nombre | Fecha | Firma / confirmación |
|---|---|---|---|
| Product Manager | Carlos Alberto Estrada Valle | 2026-10-26 | En proceso |

**Control de cambios:** todo cambio al alcance se registra aquí con versión, fecha y motivo antes de implementarse.

| Versión | Fecha | Cambio |
|---|---|---|
| 0.1 | 2026-10-26 | Borrador inicial |
| 0.2 | 2026-10-26 | Resolución de las decisiones D-01 a D-12. D-01 rechazada (periodos creados por el usuario, no automáticamente). D-05 ajustada: el pago de crédito cuenta como gasto del periodo y el ahorro se muestra en su propio apartado. D-06 reforzada: los créditos nunca tendrán intereses. D-10 cambiada: el aporte de ahorro no puede superar el dinero disponible. D-12 ajustada: al eliminar la cuenta se conserva el motivo y el autor del movimiento. Autor/PM y estado del documento definidos. |
