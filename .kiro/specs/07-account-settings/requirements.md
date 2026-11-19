# Requirements Document

> Ajustes de cuenta: zona horaria, formato de fecha, cambio de correo, y
> observaciones y tipo en los movimientos.

## Introduction

Este spec es la séptima etapa y cierra pendientes pequeños de F1 que quedaron
fuera del spec 03:

- **Zona horaria y formato de fecha** (RF-CFG-04). Hoy "hoy" se calcula siempre
  con la hora de Colombia. RNF-DAT-06 pide usar la zona horaria del usuario, y
  de "hoy" dependen el estado de los periodos (activo, vencido) y la fecha por
  defecto de los movimientos.
- **Cambio de correo con verificación** (RF-PER-03). Usa el envío de correos del
  spec 04.
- **Observaciones y tipo de ingreso** (RF-ING-01, RF-GAS-01). Los ingresos y
  gastos aceptan hoy los campos principales, pero no las observaciones
  opcionales ni el tipo de ingreso.

**Depende de:** spec 04 (envío de correos) y spec 03 (configuración y
movimientos).

**Estado:** solo requisitos. El diseño y las tareas se escriben al iniciar esta
etapa.

**Alcance:** configuración, casos de uso, endpoints, migraciones y pruebas de los
tres temas.

**Fuera de alcance:** idiomas adicionales (RF-CFG-09, F4); preferencias de
notificaciones (RF-CFG-07, F3); las pantallas (spec 08).

### Convenciones

- Zonas horarias con identificadores IANA (por ejemplo `America/Bogota`).
- Rutas y campos en inglés; este documento en español.

---

## Glossary

- **Zona horaria IANA:** nombre estándar de una zona (por ejemplo
  `America/Bogota`, `Europe/Madrid`), que incluye sus reglas de horario de
  verano.
- **"Hoy" del usuario:** la fecha actual en la zona horaria del usuario.
- **Formato de fecha:** cómo el cliente muestra las fechas (por ejemplo
  `dd/MM/yyyy`). La API siempre usa ISO 8601.
- **Cambio de correo pendiente:** un correo nuevo solicitado que todavía no se
  confirmó desde ese buzón.
- **Observaciones:** texto libre opcional de un ingreso o un gasto.
- **Tipo de ingreso:** clasificación de un ingreso (salario, honorarios, etc.).

---

## Requirements

### Requisito 1 — Zona horaria del usuario

**Historia:** Como usuario fuera de Colombia, quiero que "hoy" sea mi fecha, para
que mis periodos cambien de estado a la medianoche correcta.

#### Criterios de aceptación

1. La configuración DEBERÁ incluir la zona horaria del usuario, con
   `America/Bogota` por defecto, y permitir cambiarla. (RF-CFG-04)
2. SI la zona no es un identificador IANA válido, el sistema DEBERÁ rechazarla
   con 400.
3. El estado de los periodos, la fecha por defecto de un ingreso recibido y las
   reglas de escritura por periodo DEBERÁN usar el "hoy" del usuario.
   (RNF-DAT-06)
4. La validación de edad del registro DEBERÁ seguir usando la zona por defecto,
   porque el usuario aún no tiene configuración.
5. El cambio de zona horaria NO DEBERÁ modificar las fechas de los periodos ni de
   los movimientos existentes. (RF-PRD-04)

### Requisito 2 — Formato de fecha

**Historia:** Como usuario, quiero elegir cómo se muestran las fechas, para
leerlas en el formato al que estoy acostumbrado.

#### Criterios de aceptación

1. La configuración DEBERÁ incluir el formato de fecha preferido, entre un
   conjunto cerrado de opciones (por ejemplo día/mes/año, mes/día/año y
   año-mes-día), con día/mes/año por defecto. (RF-CFG-04)
2. La API DEBERÁ seguir entregando y recibiendo fechas en ISO 8601; el formato
   solo lo aplica el cliente. (RNF-I18-01)

### Requisito 3 — Cambio de correo con verificación

**Historia:** Como usuario, quiero cambiar mi correo sin perder la cuenta, y que
nadie pueda cambiarlo sin acceso a mi buzón nuevo.

#### Criterios de aceptación

1. El usuario DEBERÁ poder pedir el cambio de correo indicando el correo nuevo y
   su contraseña actual. (RF-PER-03)
2. SI la contraseña no es correcta, el sistema DEBERÁ rechazar la solicitud con
   el mismo mensaje genérico del login, y contarla como intento fallido.
   (RF-AUT-07)
3. CUANDO la solicitud sea válida, el sistema DEBERÁ enviar al correo nuevo un
   enlace de confirmación de un solo uso con vencimiento, y avisar al correo
   actual que se pidió un cambio. (RF-PER-03)
4. El correo de la cuenta NO DEBERÁ cambiar hasta que se confirme el enlace.
   (RF-PER-03)
5. SI el correo nuevo ya pertenece a otra cuenta, la respuesta al usuario DEBERÁ
   ser la misma que si estuviera libre, y el cambio NO DEBERÁ aplicarse al
   confirmar. (RNF-SEG-05)
6. CUANDO se confirme, el correo nuevo DEBERÁ quedar como verificado y las demás
   sesiones del usuario DEBERÁN cerrarse.
7. Una solicitud nueva DEBERÁ invalidar la anterior que siga pendiente.

### Requisito 4 — Observaciones en ingresos y gastos

**Historia:** Como usuario, quiero anotar un detalle en un ingreso o un gasto,
para recordar de qué se trataba.

#### Criterios de aceptación

1. Los ingresos y los gastos DEBERÁN aceptar observaciones opcionales de hasta
   500 caracteres, al crearlos y al editarlos. (RF-ING-01, RF-GAS-01)
2. Las observaciones DEBERÁN entregarse en las consultas de cada movimiento.
3. Las observaciones NO DEBERÁN registrarse en los logs. (RNF-SEG-10)

### Requisito 5 — Tipo de ingreso

**Historia:** Como usuario, quiero clasificar mis ingresos, para distinguir mi
salario de un ingreso ocasional.

#### Criterios de aceptación

1. Los ingresos DEBERÁN tener un tipo, entre un conjunto cerrado: salario,
   honorarios, negocio propio, inversión, regalo y otro. (RF-ING-01)
2. Los ingresos existentes DEBERÁN quedar con el tipo "otro" tras la migración.
3. SI el tipo no es uno de los permitidos, el sistema DEBERÁ rechazarlo con 400.

> Por confirmar: la lista de tipos de ingreso es una propuesta; el SRS solo dice
> "tipo".

### Requisito 6 — Pruebas

**Historia:** Como mantenedor, quiero que cada ajuste esté probado, en especial
las fechas y la seguridad del cambio de correo.

#### Criterios de aceptación

1. DEBERÁ existir una prueba de que un periodo cambia de estado según la zona del
   usuario, con dos zonas a ambos lados de la medianoche.
2. DEBERÁ existir una prueba del cambio de correo completo con Mailpit: solicitud,
   aviso al correo actual, confirmación y cierre de las otras sesiones.
3. DEBERÁN existir pruebas de que el cambio no se aplica sin confirmar, con un
   enlace usado o vencido, ni con un correo ya registrado por otra cuenta.
4. DEBERÁN existir pruebas de observaciones y tipo de ingreso en la creación, la
   edición y las consultas.
