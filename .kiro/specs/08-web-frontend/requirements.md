# Requirements Document

> Frontend web de MiSaldo (Angular).

## Introduction

Este spec es la octava etapa y construye el primer cliente de MiSaldo: la
aplicación web. Con ella se cumple la salida verificable de F1 en el SRS: *"Un
usuario se registra, registra ingresos y gastos, y ve su caja y dinero libre
correctos desde dos dispositivos"*.

La aplicación consume la API existente y no hace aritmética financiera: todos
los saldos e indicadores vienen calculados del backend (decisión 6 del SRS,
RNF-DAT-02).

**Depende de:** spec 04 (correos, sesión por cookie y CORS), spec 06 (datos del
dashboard) y spec 07 (zona horaria, formato de fecha y cambio de correo).

**Estado:** solo requisitos. El diseño y las tareas se escriben al iniciar esta
etapa.

**Alcance:** proyecto web en `web/`, autenticación completa, dashboard, periodos,
ingresos, gastos y pagos, categorías, perfil y configuración; diseño responsive,
accesibilidad, español e inglés, temas claro y oscuro; pruebas unitarias y de
extremo a extremo; trabajos del frontend en el CI.

**Fuera de alcance:** la app móvil (F4); historial, ajustes y privacidad, cuyas
pantallas llegan con los specs 09 y 10; créditos y ahorro (F2); el despliegue a
producción.

### Convenciones

- Código, nombres de componentes y rutas en inglés; textos visibles solo en
  archivos de traducción; este documento en español.
- Cada requisito referencia las reglas del SRS que implementa.

---

## Glossary

- **SPA:** aplicación de una sola página que corre en el navegador y habla con la
  API por HTTP.
- **Angular:** framework del frontend elegido en el SRS (decisión 2).
- **Ionic:** biblioteca de componentes que permite reutilizar la interfaz en la
  app móvil con Capacitor (decisión 2).
- **Access token en memoria:** el token se guarda en una variable de la
  aplicación, no en `localStorage` ni en cookies legibles.
- **Renovación silenciosa:** pedir un access token nuevo con la cookie de refresh
  sin que el usuario lo note.
- **Clave de idempotencia:** identificador único por intento de pago, enviado en
  la cabecera `Idempotency-Key`.
- **Playwright:** herramienta de pruebas de extremo a extremo en navegadores
  reales (stack del SRS).

---

## Requirements

### Requisito 1 — Proyecto y arquitectura del cliente

**Historia:** Como desarrollador, quiero un proyecto web ordenado y estricto,
para que crezca sin volverse frágil.

#### Criterios de aceptación

1. El frontend DEBERÁ vivir en `web/`, con Angular en su versión estable vigente
   al iniciar la etapa y TypeScript en modo estricto. (decisión 2)
2. La interfaz DEBERÁ construirse con componentes de Ionic, para poder
   reutilizarla en la app móvil con Capacitor. (decisión 2)
3. El cliente NO DEBERÁ calcular saldos, indicadores ni estados de gastos; DEBERÁ
   mostrar lo que entrega la API. (decisión 6, RNF-DAT-02)
4. Los montos DEBERÁN tratarse como cadenas decimales de punta a punta; el
   cliente solo los formatea para mostrarlos. (RNF-DAT-02)
5. La URL de la API DEBERÁ configurarse por entorno, no escribirse en el código.

### Requisito 2 — Registro, verificación y acceso

**Historia:** Como visitante, quiero crear mi cuenta, verificar mi correo y entrar,
sin pasos confusos.

#### Criterios de aceptación

1. El registro DEBERÁ pedir los datos de RF-AUT-01 y RF-PER-01, el idioma, y la
   aceptación de la política de privacidad y los términos con enlaces a sus
   textos. (RF-AUT-01, RF-PRV-01)
2. El registro DEBERÁ explicar claramente el rechazo de un menor de 18 años.
   (RF-AUT-02)
3. La página `verify-email` DEBERÁ consumir el token del enlace y ofrecer el
   reenvío si el enlace venció. (RF-AUT-03)
4. DEBERÁN existir el login, la recuperación de contraseña (`forgot-password` y
   `reset-password`) y el cierre de sesión. (RF-AUT-04 a RF-AUT-06)
5. Un usuario con correo sin verificar DEBERÁ ver una pantalla que lo explique y
   le permita reenviar el correo, en lugar de errores 403. (RF-AUT-03)

### Requisito 3 — Sesión segura en el navegador

**Historia:** Como usuario, quiero que mi sesión sea cómoda y segura, sin que un
script malicioso pueda robarla.

#### Criterios de aceptación

1. El access token DEBERÁ guardarse solo en memoria; ningún token DEBERÁ
   guardarse en `localStorage` ni `sessionStorage`. (RNF-SEG-03)
2. El cliente DEBERÁ renovar el access token con la cookie de refresh antes de
   que venza y al recargar la página. (RNF-SEG-02)
3. CUANDO la API responda 401 y la renovación no sea posible, el cliente DEBERÁ
   llevar al usuario al login, conservando la página a la que quería ir.
   (RF-AUT-08)
4. El cliente DEBERÁ evitar renovaciones simultáneas: varias peticiones con 401 a
   la vez DEBERÁN esperar una sola renovación.
5. El cliente DEBERÁ declarar una política de seguridad de contenido (CSP) que
   impida cargar scripts de orígenes no previstos.

### Requisito 4 — Navegación y diseño

**Historia:** Como usuario, quiero usar MiSaldo cómodo en el teléfono y en el
computador.

#### Criterios de aceptación

1. El diseño DEBERÁ ser mobile-first y responsive, con navegación inferior en
   móvil y menú lateral en escritorio. (RNF-USA-01)
2. El dashboard DEBERÁ ser la pantalla inicial tras iniciar sesión. (RF-DSH-01)
3. El tema DEBERÁ ser claro, oscuro o automático según el dispositivo, guardado en
   la configuración del servidor. (RF-CFG-01, RF-CFG-02, RNF-USA-03)
4. Los estilos DEBERÁN usar propiedades CSS lógicas, para quedar listos para RTL.
   (RNF-I18-02)

### Requisito 5 — Dashboard

**Historia:** Como usuario, quiero ver de un vistazo cómo voy en el periodo y en el
año.

#### Criterios de aceptación

1. El dashboard DEBERÁ mostrar los indicadores del periodo y los totales anuales.
   (RF-DSH-02)
2. DEBERÁ mostrar la dona de gastos por categoría, las barras de ingresos contra
   gastos y el progreso del consumo del presupuesto. (RF-DSH-05)
3. DEBERÁ permitir los filtros por periodo, mes, año y categoría. (RF-DSH-07)
4. Cada indicador DEBERÁ tener un texto de ayuda accesible. (RF-DSH-04)
5. SI no hay datos para un bloque, DEBERÁ mostrar un estado vacío que explique
   cómo empezar. (RF-DSH-08)
6. SI el dinero libre es negativo, DEBERÁ mostrar una alerta clara con el valor
   real. (RF-DSH-09)
7. El arrastre del periodo DEBERÁ indicarse con su valor. (RF-PRD-07)
8. SI no hay periodo para hoy, el dashboard DEBERÁ ofrecer crearlo, sin crearlo
   solo. (CP-20)

### Requisito 6 — Periodos

**Historia:** Como usuario, quiero crear y cerrar mis periodos sabiendo qué va a
pasar.

#### Criterios de aceptación

1. El usuario DEBERÁ poder crear un periodo a partir de la propuesta de la API,
   ajustando tipo y fechas antes de confirmar. (RF-PRD-02)
2. Cerrar un periodo DEBERÁ pedir confirmación y mostrar el saldo final y el
   arrastre resultante. (RF-PRD-06, RNF-USA-04)
3. SI hay un periodo vencido sin cerrar, el cliente DEBERÁ mostrarlo destacado y
   ofrecer cerrarlo antes de continuar. (RF-PRD-06b, CP-21)

### Requisito 7 — Ingresos, gastos y pagos

**Historia:** Como usuario, quiero registrar mis movimientos rápido y sin
errores.

#### Criterios de aceptación

1. DEBERÁN existir formularios para crear y editar ingresos y gastos, con
   validación inmediata y mensajes comprensibles. (RF-ING-01, RF-GAS-01,
   RNF-USA-05)
2. El usuario DEBERÁ poder marcar un ingreso como recibido y registrar pagos de un
   gasto, viendo su estado derivado. (RF-ING-03, RF-GAS-02, RF-GAS-03)
3. Cada intento de pago DEBERÁ enviar una clave de idempotencia nueva, y
   conservarla en los reintentos del mismo intento. (RNF-DAT-03, CP-12)
4. Eliminar o modificar información financiera DEBERÁ pedir confirmación.
   (RNF-USA-04, RF-ING-05, RF-GAS-06)
5. SI la API rechaza un cambio por un periodo cerrado, el cliente DEBERÁ explicar
   el motivo. (RN-P04)

### Requisito 8 — Categorías, perfil y configuración

**Historia:** Como usuario, quiero ajustar mis categorías, mis datos y mis
preferencias.

#### Criterios de aceptación

1. DEBERÁ existir la gestión de categorías: crear, renombrar, ordenar y archivar.
   (RF-CAT-02, RF-CAT-03)
2. DEBERÁ existir la edición del perfil, con la fecha de nacimiento visible pero
   no editable, y el cambio de correo con verificación. (RF-PER-02, RF-PER-03)
3. DEBERÁ existir la configuración: idioma, tema, moneda (bloqueada si hay
   movimientos), zona horaria, formato de fecha y periodos. (RF-CFG-01 a
   RF-CFG-06, RF-PER-04)

### Requisito 9 — Idiomas y formatos

**Historia:** Como usuario, quiero la aplicación en mi idioma y con mis formatos.

#### Criterios de aceptación

1. La aplicación DEBERÁ estar en español e inglés, con todos los textos en
   archivos de traducción. (RF-CFG-03, RNF-I18-01)
2. Los montos DEBERÁN formatearse según la moneda del usuario y la región, y las
   fechas según su formato preferido. (RNF-I18-01)
3. Los errores de la API DEBERÁN mostrarse traducidos según su `code`, nunca con
   el texto técnico de la respuesta.

### Requisito 10 — Accesibilidad

**Historia:** Como usuario con una discapacidad, quiero poder usar MiSaldo con
teclado y lector de pantalla.

#### Criterios de aceptación

1. La aplicación DEBERÁ cumplir WCAG 2.1 nivel AA: contraste, uso completo con
   teclado, foco visible y etiquetas para lectores de pantalla. (RNF-USA-02)
2. Ninguna información DEBERÁ comunicarse solo con color; DEBERÁ acompañarse de
   texto o icono. (RF-DSH-10, RNF-USA-03)
3. Los gráficos DEBERÁN tener una alternativa en texto o tabla.

### Requisito 11 — Errores y soporte

**Historia:** Como usuario, quiero entender qué falló y poder reportarlo.

#### Criterios de aceptación

1. Un error inesperado DEBERÁ mostrar un mensaje comprensible y el identificador
   de correlación de la petición, para reportarlo. (RNF-MAN-05)
2. Sin conexión o con la API caída, el cliente DEBERÁ indicarlo y permitir
   reintentar, sin perder lo que el usuario escribió.

### Requisito 12 — Pruebas y CI

**Historia:** Como mantenedor, quiero probar el cliente solo y contra la API real.

#### Criterios de aceptación

1. DEBERÁN existir pruebas unitarias de la lógica del cliente (sesión,
   renovación, manejo de errores, formatos).
2. DEBERÁN existir pruebas de extremo a extremo con Playwright contra la API y el
   entorno local (PostgreSQL y Mailpit): registro con verificación por correo,
   login, periodo, ingreso, gasto con pago y dashboard.
3. DEBERÁ existir una prueba de extremo a extremo con dos navegadores
   independientes del mismo usuario que vean los mismos datos (salida de F1,
   CP-16).
4. DEBERÁ existir una revisión automática de accesibilidad de las pantallas
   principales.
5. El CI DEBERÁ compilar el frontend, ejecutar sus pruebas y revisar sus
   dependencias vulnerables. (RNF-MAN-04, RNF-SEG-11)
