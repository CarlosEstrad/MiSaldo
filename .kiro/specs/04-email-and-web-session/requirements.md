# Requirements Document

> Envío de correos y sesión segura para el cliente web de MiSaldo.

## Introduction

Este spec es la cuarta etapa del sistema y prepara el backend para su primer
cliente real, el frontend web. La API (spec `03-api-and-authentication`) ya
registra usuarios, verifica correos, abre sesiones y expone los datos
financieros, pero le faltan tres cosas para que un navegador la use de forma
segura:

1. **Enviar los correos.** Hoy los enlaces de verificación y de recuperación
   de contraseña solo quedan en la memoria del proceso. Sin correo, un usuario
   no puede verificar su cuenta, y sin cuenta verificada no entra a sus datos
   financieros (RF-AUT-03).
2. **Guardar la sesión sin exponer el refresh token al JavaScript.** Hoy el
   refresh token viaja en el cuerpo de la respuesta, y RNF-SEG-03 prohíbe
   guardar tokens en `localStorage`. El refresh token pasa a una cookie
   `HttpOnly`; el access token sigue en el cuerpo y el cliente lo guarda solo
   en memoria.
3. **Aceptar peticiones del origen del frontend (CORS)**, solo de los orígenes
   configurados.

Además deja listo un entorno local con Docker (PostgreSQL y Mailpit) para poder
ejecutar la API a mano y desarrollar el frontend contra ella.

**Decisión de base:** D-13 del SRS. Un único notificador SMTP; por etapa solo
cambia la configuración: Mailpit en desarrollo, un Gmail dedicado (opcional)
para pruebas con entregas reales, y dominio propio con proveedor transaccional
en producción.

**Plataforma:** .NET 10, ASP.NET Core, MailKit para SMTP, Docker Compose para
el entorno local.

**Alcance:** notificador SMTP y plantillas de correo (es/en); reenvío de la
verificación; refresh token en cookie; CORS; protección del uso de la cookie;
entorno local con Docker; configuración y documentación; pruebas de
integración con un Mailpit real.

**Fuera de alcance:** comprar el dominio y configurar SPF/DKIM/DMARC (se hace
al preparar el despliegue a producción, D-13); cambio de correo (RF-PER-03,
etapa 7); notificaciones y recordatorios por correo (RF-NOT-04, F4); el modo de
sesión para la app móvil (F4), que guardará el refresh token en el
almacenamiento seguro del sistema; el frontend mismo (etapa 8).

### Convenciones

- Nombres de clases, rutas, campos y variables en inglés; este documento en
  español.
- Los secretos (contraseña SMTP, clave de firma) solo en variables de entorno o
  un gestor de secretos (RNF-SEG-08).
- Cada requisito referencia las reglas del SRS que implementa.

---

## Glossary

- **Notificador:** implementación de `IAccountNotifier` que entrega los correos
  de cuenta (verificación y recuperación).
- **SMTP:** protocolo estándar de envío de correo. Mailpit, Gmail y Amazon SES
  lo aceptan.
- **Mailpit:** servidor SMTP de desarrollo que atrapa los correos sin
  entregarlos y los muestra en una página local; también expone una API HTTP.
- **Contraseña de aplicación:** credencial de 16 caracteres que Gmail genera
  para que un programa envíe correo; requiere verificación en dos pasos.
- **Enlace de verificación / de recuperación:** URL del frontend que lleva el
  token de un solo uso, por ejemplo `https://app/verify-email?token=...`.
- **Cookie `HttpOnly`:** cookie que el navegador envía al servidor pero que el
  JavaScript de la página no puede leer.
- **`SameSite=Strict`:** atributo que impide que el navegador envíe la cookie
  en peticiones iniciadas desde otro sitio.
- **CORS:** mecanismo del navegador que permite a una página de un origen
  llamar a una API de otro origen solo si la API lo autoriza.
- **Origen:** combinación de esquema, dominio y puerto, por ejemplo
  `http://localhost:4200`.
- **Entorno local:** contenedores de Docker Compose (PostgreSQL y Mailpit) para
  ejecutar la API en la máquina del desarrollador.

---

## Requirements

### Requisito 1 — Notificador SMTP configurable

**Historia:** Como responsable del sistema, quiero que los correos de cuenta se
envíen por SMTP, para usar Mailpit, Gmail o un proveedor transaccional sin
cambiar el código.

#### Criterios de aceptación

1. CUANDO exista configuración SMTP (servidor, puerto, remitente y modo de
   seguridad), el sistema DEBERÁ enviar los correos de verificación y de
   recuperación por SMTP. (RF-AUT-03, RF-AUT-06, D-13)
2. El modo de seguridad DEBERÁ ser configurable: STARTTLS, TLS implícito o sin
   cifrado; el modo sin cifrado solo DEBERÁ aceptarse fuera del entorno
   Production (para Mailpit). (RNF-SEG-01)
3. El usuario y la contraseña SMTP DEBERÁN ser opcionales (Mailpit no los usa)
   y leerse solo de variables de entorno o un gestor de secretos. (RNF-SEG-08)
4. SI no hay configuración SMTP, el sistema DEBERÁ usar el notificador en
   memoria, y SOLO en los entornos Development y de pruebas.
5. SI el entorno es Production y no hay configuración SMTP válida, la API NO
   DEBERÁ arrancar, con un mensaje que nombre la configuración faltante sin
   mostrar valores.
6. Los registros NO DEBERÁN contener los tokens, los enlaces ni la contraseña
   SMTP; la dirección del destinatario NO DEBERÁ registrarse completa.
   (RNF-SEG-10)

### Requisito 2 — Contenido de los correos

**Historia:** Como usuario, quiero recibir un correo claro, en mi idioma, con un
enlace que funcione, para verificar mi cuenta o recuperar mi contraseña.

#### Criterios de aceptación

1. El correo de verificación DEBERÁ incluir un enlace a
   `{URL del frontend}/verify-email?token={token}` y el de recuperación a
   `{URL del frontend}/reset-password?token={token}`; la URL base del frontend
   DEBERÁ ser configurable. (RF-AUT-03, RF-AUT-06)
2. Cada correo DEBERÁ tener versión en texto plano y en HTML, e indicar cuánto
   tiempo es válido el enlace.
3. El idioma del correo DEBERÁ ser el del usuario (español o inglés). En el
   registro, el visitante PODRÁ indicar su idioma; si no lo indica, DEBERÁ
   usarse español, y ese idioma DEBERÁ guardarse en su configuración.
   (RF-CFG-03)
4. Los textos de los correos DEBERÁN vivir en plantillas o archivos de
   traducción, no mezclados con la lógica. (RF-CFG-03)
5. Los correos NO DEBERÁN contener datos financieros ni la contraseña del
   usuario. (RNF-SEG-10)
6. El correo de recuperación DEBERÁ indicar que, si el usuario no lo pidió,
   puede ignorarlo.

### Requisito 3 — Fallos de envío

**Historia:** Como usuario, quiero que un fallo del servidor de correo no me deje
sin cuenta ni revele información, para poder intentarlo de nuevo.

#### Criterios de aceptación

1. SI el envío del correo de verificación falla, el sistema DEBERÁ conservar la
   cuenta creada y registrar el fallo sin el token; el usuario DEBERÁ poder
   pedir un nuevo correo (Requisito 4).
2. SI el envío del correo de recuperación falla, la respuesta al cliente DEBERÁ
   ser la misma que cuando el envío funciona, para no revelar si el correo
   existe. (RF-AUT-06, RNF-SEG-05)
3. El envío DEBERÁ tener un tiempo máximo de espera configurable, para que un
   servidor SMTP lento no deje colgada la petición.

### Requisito 4 — Reenvío del correo de verificación

**Historia:** Como usuario que no recibió el correo, o cuyo enlace venció, quiero
pedir uno nuevo, para poder verificar mi cuenta.

#### Criterios de aceptación

1. El sistema DEBERÁ exponer un endpoint público para pedir un nuevo correo de
   verificación a partir del correo de la cuenta. (RF-AUT-03)
2. La respuesta DEBERÁ ser la misma exista o no la cuenta, y esté o no
   verificada. (RNF-SEG-05)
3. CUANDO la cuenta exista y no esté verificada, el sistema DEBERÁ emitir un
   token nuevo e invalidar los tokens de verificación anteriores de esa cuenta.
   (RNF-SEG-02)
4. SI ya se envió un correo de verificación a esa cuenta en el último minuto,
   el sistema NO DEBERÁ enviar otro, y la respuesta DEBERÁ ser la misma.
5. El endpoint DEBERÁ estar bajo la política estricta de limitación de
   solicitudes de `/auth/*`. (RNF-SEG-07)

### Requisito 5 — Refresh token en cookie

**Historia:** Como usuario del navegador, quiero que mi sesión se renueve sin que
el código de la página pueda leer el refresh token, para que un script
malicioso no pueda robar mi sesión.

#### Criterios de aceptación

1. CUANDO el login o el refresh tengan éxito, el sistema DEBERÁ entregar el
   refresh token en una cookie `HttpOnly`, `Secure` y `SameSite=Strict`,
   limitada a la ruta de autenticación y con la misma vigencia que el token.
   (RNF-SEG-02, RNF-SEG-03)
2. El refresh token NO DEBERÁ aparecer en el cuerpo de la respuesta; el cuerpo
   DEBERÁ seguir incluyendo el access token, su vencimiento, el vencimiento de
   la sesión y si el correo está verificado. (RNF-SEG-03)
3. El refresh y el logout DEBERÁN leer el refresh token de la cookie. SI la
   cookie falta o no es válida, el refresh DEBERÁ responder 401.
4. CUANDO el usuario cierre sesión, o el refresh falle por token inválido,
   reutilizado o vencido, el sistema DEBERÁ borrar la cookie. (RF-AUT-05)
5. CUANDO se restablezca la contraseña, el sistema DEBERÁ revocar las sesiones,
   como hoy, y borrar la cookie de la petición. (RF-AUT-06)
6. Las reglas existentes DEBERÁN mantenerse: rotación en cada uso, rechazo de
   un token ya usado y revocación de las sesiones al detectar reutilización.
   (RNF-SEG-02)

### Requisito 6 — Protección de las rutas que usan la cookie

**Historia:** Como responsable de seguridad, quiero que otro sitio no pueda usar
la cookie de sesión de un usuario, para evitar ataques de falsificación de
peticiones (CSRF).

#### Criterios de aceptación

1. SI una petición a refresh o logout trae una cabecera `Origin` que no es un
   origen permitido ni el propio de la API, el sistema DEBERÁ rechazarla con
   403 sin tocar la sesión.
2. La cookie DEBERÁ ser `SameSite=Strict`, de modo que el navegador no la envíe
   en peticiones iniciadas desde otro sitio. (RNF-SEG-03)
3. Las respuestas que entregan o borran la cookie NO DEBERÁN guardarse en caché.

### Requisito 7 — CORS para el frontend

**Historia:** Como desarrollador del frontend, quiero que la API acepte
peticiones de mi origen y de ningún otro, para que el navegador permita las
llamadas sin abrir la API a cualquier sitio.

#### Criterios de aceptación

1. Los orígenes permitidos DEBERÁN configurarse como una lista; solo esos
   orígenes DEBERÁN recibir permiso CORS, con credenciales (cookies) permitidas.
2. SI la lista contiene un comodín (`*`) o un origen mal formado, la API NO
   DEBERÁ arrancar, porque un comodín con credenciales expondría las sesiones.
3. La API DEBERÁ permitir los métodos y cabeceras que usa
   (`Authorization`, `Content-Type`, `Idempotency-Key`, `X-Correlation-ID`) y
   exponer al cliente las cabeceras `X-Correlation-ID`, `Idempotent-Replayed`,
   `Retry-After` y `Location`.
4. SI no hay orígenes configurados, la API NO DEBERÁ conceder permisos CORS
   (solo mismo origen).
5. Un origen no permitido NO DEBERÁ recibir cabeceras CORS en la respuesta.

### Requisito 8 — Entorno local con Docker

**Historia:** Como desarrollador, quiero levantar la base de datos y el servidor
de correo con un comando, para ejecutar la API a mano y desarrollar el frontend.

#### Criterios de aceptación

1. DEBERÁ existir un archivo de Docker Compose para desarrollo con PostgreSQL y
   Mailpit, con datos persistidos en un volumen.
2. La base DEBERÁ crearse con un rol de aplicación SIN `BYPASSRLS`, distinto del
   rol que aplica las migraciones, igual que en las pruebas. (RNF-SEG-04)
3. Las contraseñas del entorno local DEBERÁN venir de un archivo `.env` local
   (ignorado por git) basado en `.env.example`, nunca escritas en el Compose.
   (RNF-SEG-08)
4. El README DEBERÁ explicar cómo levantar el entorno, aplicar las migraciones,
   ejecutar la API y ver los correos en Mailpit.
5. Los datos de PostgreSQL DEBERÁN sobrevivir a apagar el equipo y a detener
   los contenedores; los contenedores DEBERÁN volver a arrancar solos con
   Docker (`restart: unless-stopped`). Los correos de Mailpit no necesitan
   conservarse.
6. La clave de firma de los JWT del entorno local DEBERÁ ser fija (guardada en
   el `.env` local), para que las sesiones no se invaliden en cada arranque.
7. PostgreSQL y las herramientas del entorno local DEBERÁN publicarse solo en
   `127.0.0.1`, nunca en la red. (RNF-SEG-04)
8. El entorno local DEBERÁ incluir pgAdmin, con su configuración guardada en un
   volumen y la conexión de administración ya registrada (sin la contraseña),
   para ver y gestionar los datos.
9. El README DEBERÁ explicar cómo conectarse como administrador (que no tiene
   la restricción de RLS), cómo ver la base como un usuario con
   `SET app.user_id`, y que editar a mano se salta las reglas de negocio.
10. El README DEBERÁ explicar cómo respaldar y restaurar la base local con
    `pg_dump` y `pg_restore`, y advertir que `docker compose down -v` borra los
    datos.

### Requisito 9 — Configuración y documentación

**Historia:** Como mantenedor, quiero que cada ajuste nuevo esté documentado, para
configurar cada entorno sin leer el código.

#### Criterios de aceptación

1. `.env.example` DEBERÁ documentar la configuración SMTP, la URL del frontend,
   los orígenes CORS y las variables del entorno local, sin valores reales.
   (RNF-SEG-08)
2. La documentación DEBERÁ describir cómo pasar de Mailpit a un Gmail dedicado
   y, en producción, a un proveedor transaccional, cambiando solo variables.
   (D-13)
3. El documento OpenAPI DEBERÁ reflejar el endpoint nuevo y que el refresh
   token viaja en una cookie. (RNF-MAN-06)

### Requisito 10 — Pruebas

**Historia:** Como mantenedor, quiero verificar el envío de correos y la sesión
por cookie contra servicios reales, para confiar en el flujo completo.

#### Criterios de aceptación

1. DEBERÁ existir una prueba de integración que levante Mailpit en un
   contenedor, registre un usuario por HTTP, lea el correo recibido desde la
   API de Mailpit, siga el enlace y verifique la cuenta.
2. DEBERÁ existir una prueba del flujo de recuperación con el correo real de
   Mailpit, en español y en inglés.
3. DEBERÁ existir una prueba de que la cookie se entrega con `HttpOnly`,
   `Secure` y `SameSite=Strict`, de que el cuerpo no trae el refresh token y del
   flujo login → refresh → logout usando solo la cookie.
4. DEBERÁ existir una prueba de CORS: un origen permitido recibe permiso y uno no
   permitido no; y de que refresh con un `Origin` ajeno responde 403.
5. DEBERÁ existir una prueba de que la API no arranca en Production sin SMTP ni
   con un comodín en los orígenes CORS.
6. Las pruebas existentes DEBERÁN seguir pasando, ajustadas al refresh token en
   cookie, y la verificación de logs sin secretos DEBERÁ incluir el envío de
   correos. (RNF-SEG-10)
