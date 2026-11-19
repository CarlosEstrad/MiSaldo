# Requirements Document

> API REST y autenticación propia (JWT) de MiSaldo.

## Introducción

Este spec cubre la capa de API de MiSaldo (`MiSaldo.Api`) y una **autenticación
propia del backend**, además de la capa de aplicación (`MiSaldo.Application`)
que orquesta los casos de uso entre la API y el dominio/persistencia ya
construidos.

Es la tercera pieza del sistema. El dominio (motor financiero) y la persistencia
(PostgreSQL con EF Core, aislamiento por usuario y RLS) ya están implementados y
probados. Esta capa los expone por HTTP, resolviendo el usuario autenticado y
pasándolo a la persistencia a través del `ICurrentUser` que ya existe.

### Desviación consciente del SRS: autenticación propia en vez de Cognito

El SRS especifica Amazon Cognito (RF-AUT-04, decisión 4). Dado que no hay
credenciales de AWS disponibles, este spec implementa **autenticación propia en
el backend**: el backend guarda las credenciales (hash de contraseña) y los
refresh tokens en la base de datos, y **emite y valida sus propios JWT**. El
access token es stateless (se valida con el secreto de firma, no se guarda). La
integración con Cognito queda documentada como alternativa futura, detrás de la
misma abstracción (`ICurrentUser` y el servicio de tokens), para poder migrar
sin reescribir los casos de uso.

Este cambio se registra en el control de cambios del proyecto; el resto de
reglas de seguridad del SRS (RNF-SEG-*) siguen aplicando.

**Plataforma:** .NET 10, ASP.NET Core Web API, autenticación JWT Bearer.

**Alcance:** la capa `MiSaldo.Application` (casos de uso, DTOs, validaciones);
`MiSaldo.Api` con `/api/v1`; autenticación propia (registro, verificación de
correo, login, refresh, logout, recuperación de contraseña); y los endpoints de
F1 para perfil, configuración, categorías, periodos, ingresos, gastos (con
pagos) y dashboard.

**Fuera de alcance:** historial y auditoría consultable, privacidad
(exportación y borrado de cuenta), reportes, el frontend Angular, créditos y
ahorro (F2), y el envío real de correos (la verificación/recuperación se modela,
pero el transporte SES llega en una fase posterior; por ahora el token se expone
de forma verificable en pruebas/entorno de desarrollo).

### Convenciones

- API en `/api/v1`, JSON, fechas ISO 8601, montos como **cadenas decimales**
  (RNF-DAT-02), errores en formato Problem Details RFC 7807 (RNF-MAN-06).
- El `UserId` se obtiene siempre del token, nunca de parámetros (RF-AUT-09).
- Nombres de clases, rutas y campos en inglés; este documento en español.
- Cada requisito referencia las reglas del SRS que implementa.

---

## Requisitos

### Requisito 1 — Capa de aplicación (casos de uso)

**Historia:** Como API, quiero una capa de aplicación que orqueste los casos de
uso sobre el dominio y la persistencia, para que los controladores no tengan
lógica de negocio.

#### Criterios de aceptación

1. El proyecto `MiSaldo.Application` DEBERÁ depender de `MiSaldo.Domain` y definir
   los casos de uso como servicios o handlers; los controladores NO DEBERÁN
   contener reglas de negocio. (RNF-MAN-02)
2. Los casos de uso DEBERÁN exponer DTOs de entrada y salida; los montos en los
   DTOs DEBERÁN ser cadenas decimales y el cliente NO DEBERÁ hacer aritmética.
   (RNF-DAT-02)
3. Las operaciones que afecten varias entidades DEBERÁN ejecutarse en una
   transacción a través de la persistencia. (RNF-DAT-01)
4. Los indicadores del dashboard DEBERÁN provenir del motor del dominio
   (`FinancialEngine`), nunca de cálculos en la API. (RNF-MAN-02, RF-REP-04)

### Requisito 2 — Registro de cuenta

**Historia:** Como visitante, quiero registrarme con mis datos y aceptar los
términos, para crear mi cuenta.

#### Criterios de aceptación

1. CUANDO un visitante envíe correo, contraseña, nombre completo, fecha de
   nacimiento y la aceptación de política de privacidad y términos, el sistema
   DEBERÁ crear el usuario, su perfil y el registro de consentimiento. (RF-AUT-01,
   RF-PRV-01)
2. SI la fecha de nacimiento indica menos de 18 años, el sistema DEBERÁ rechazar
   el registro con un mensaje claro y NO DEBERÁ crear cuenta ni guardar datos.
   (RF-AUT-02, CP-11)
3. SI el correo ya está registrado, el sistema DEBERÁ rechazar el registro sin
   revelar detalles que faciliten enumerar cuentas. (RNF-SEG-05)
4. SI la contraseña tiene menos de 10 caracteres, el sistema DEBERÁ rechazarla.
   (RNF-SEG-09)
5. La contraseña DEBERÁ almacenarse solo como hash con un algoritmo fuerte
   (Argon2id o bcrypt); NUNCA en texto plano ni en logs. (RNF-SEG-10)

### Requisito 3 — Verificación de correo

**Historia:** Como usuario registrado, debo verificar mi correo antes de acceder
a mis datos financieros.

#### Criterios de aceptación

1. CUANDO se cree la cuenta, el sistema DEBERÁ generar un token de verificación
   de correo asociado al usuario y con expiración. (RF-AUT-03)
2. CUANDO el usuario presente un token de verificación válido y vigente, el
   sistema DEBERÁ marcar el correo como verificado. (RF-AUT-03)
3. SI una petición a una ruta financiera llega de un usuario con correo no
   verificado, el sistema DEBERÁ rechazarla. (RF-AUT-03)
4. El token de verificación NO DEBERÁ poder reutilizarse tras ser consumido ni
   después de expirar. (RNF-SEG-02)

### Requisito 4 — Inicio de sesión y emisión de tokens

**Historia:** Como usuario, quiero iniciar sesión con mis credenciales y recibir
tokens, para usar la aplicación.

#### Criterios de aceptación

1. CUANDO el usuario envíe credenciales válidas, el sistema DEBERÁ emitir un
   access token JWT de vida corta (15 a 60 min) y un refresh token rotativo.
   (RF-AUT-04, RNF-SEG-02)
2. El access token DEBERÁ firmarse con un secreto que vive en variables de
   entorno o un gestor de secretos, NUNCA en el repositorio. (RNF-SEG-08)
3. El access token DEBERÁ incluir el identificador del usuario como claim, y la
   API DEBERÁ obtener el `UserId` solo de ese claim. (RF-AUT-09)
4. SI las credenciales son inválidas, el sistema DEBERÁ responder con un error
   genérico que no revele si el fallo fue por correo o por contraseña.
   (RNF-SEG-05)
5. SI se superan los intentos fallidos configurados, el sistema DEBERÁ bloquear
   temporalmente el inicio de sesión de esa cuenta. (RF-AUT-07)

### Requisito 5 — Refresh y cierre de sesión

**Historia:** Como usuario, quiero renovar mi sesión y poder cerrarla, para
mantener el acceso de forma segura.

#### Criterios de aceptación

1. CUANDO el usuario presente un refresh token válido y vigente, el sistema
   DEBERÁ emitir un nuevo access token y un nuevo refresh token, e invalidar el
   anterior (rotación). (RNF-SEG-02)
2. CUANDO el usuario cierre sesión, el sistema DEBERÁ invalidar el refresh token
   para que no pueda volver a usarse. (RF-AUT-05)
3. SI un refresh token ya usado (rotado) se vuelve a presentar, el sistema
   DEBERÁ rechazarlo. (RNF-SEG-02)
4. Los refresh tokens DEBERÁN guardarse de forma que permitan revocarlos
   (hash o referencia en la base), nunca el access token. (RNF-SEG-02)

### Requisito 6 — Recuperación de contraseña

**Historia:** Como usuario, quiero recuperar el acceso si olvido mi contraseña,
sin exponer si un correo existe.

#### Criterios de aceptación

1. CUANDO el usuario solicite recuperar su contraseña, el sistema DEBERÁ generar
   un token de recuperación con expiración y responder igual exista o no el
   correo (sin revelar su existencia). (RF-AUT-06)
2. CUANDO el usuario presente un token de recuperación válido y una nueva
   contraseña que cumpla la política, el sistema DEBERÁ actualizar el hash e
   invalidar el token y las sesiones activas. (RF-AUT-06, RNF-SEG-09)
3. El token de recuperación NO DEBERÁ reutilizarse tras consumirse ni tras
   expirar. (RNF-SEG-02)

### Requisito 7 — Autorización y aislamiento en la API

**Historia:** Como responsable de seguridad, quiero que las rutas privadas
exijan un token válido y que cada petición opere solo sobre los datos del
usuario autenticado.

#### Criterios de aceptación

1. SI una petición llega a una ruta privada sin token válido, la API DEBERÁ
   responder 401. (RF-AUT-08)
2. La API DEBERÁ resolver un `ICurrentUser` a partir del claim del token y
   pasarlo a la persistencia, activando el filtro por usuario y la RLS.
   (RF-AUT-09, RNF-SEG-04)
3. SI un usuario solicita un recurso de otro usuario, la API DEBERÁ responder
   404 (o 403) sin revelar si el recurso existe. (RNF-SEG-05, CP-07)
4. Toda entrada DEBERÁ validarse en el servidor antes de tocar el dominio.
   (RNF-SEG-06)
5. La API DEBERÁ aplicar limitación de solicitudes (rate limiting), más estricta
   en las rutas de autenticación. (RNF-SEG-07)

### Requisito 8 — Perfil y configuración

**Historia:** Como usuario, quiero ver y editar mi perfil y mis preferencias.

#### Criterios de aceptación

1. El sistema DEBERÁ exponer endpoints para leer y actualizar el perfil (nombre,
   país, género); la fecha de nacimiento NO DEBERÁ ser editable. (RF-PER-01,
   RF-PER-02)
2. El sistema DEBERÁ exponer endpoints para leer y actualizar la configuración
   (idioma, tema, tipo de periodo, arrastre, configuración de periodos),
   guardándola en el servidor. (RF-CFG-01 a RF-CFG-06)
3. SI el usuario intenta cambiar la moneda principal teniendo movimientos, el
   sistema DEBERÁ rechazarlo. (RF-PER-04, D-07)

### Requisito 9 — Categorías

**Historia:** Como usuario, quiero gestionar mis categorías de gasto.

#### Criterios de aceptación

1. CUANDO se cree la cuenta, el sistema DEBERÁ crear las categorías iniciales del
   usuario. (RF-CAT-01)
2. El sistema DEBERÁ exponer endpoints para crear, renombrar, reordenar y
   archivar categorías. (RF-CAT-02, RF-CAT-03)
3. SI se intenta crear una categoría con un nombre que ya existe para el usuario
   (sin distinguir mayúsculas), el sistema DEBERÁ rechazarla. (RF-CAT-04)
4. SI una categoría tiene movimientos, el sistema NO DEBERÁ eliminarla; DEBERÁ
   archivarla. (RF-CAT-03)

### Requisito 10 — Periodos

**Historia:** Como usuario, quiero crear y gestionar mis periodos financieros.

#### Criterios de aceptación

1. El sistema DEBERÁ crear un periodo solo cuando el usuario lo solicite,
   tomando la configuración vigente y permitiéndole ajustar tipo y fechas; NUNCA
   de forma automática. (RF-PRD-02, D-01, CP-20)
2. SI el periodo solicitado se cruza con uno existente del usuario, el sistema
   DEBERÁ rechazarlo. (RF-PRD-03)
3. El sistema DEBERÁ exponer el estado del periodo (Planned/Active/Overdue/
   Closed) y permitir cerrarlo, aplicando el arrastre si corresponde. (RF-PRD-05,
   RF-PRD-06, CP-14)
4. SI existe un periodo vencido sin cerrar, el sistema DEBERÁ exigir cerrarlo
   antes de permitir continuar con el siguiente. (RF-PRD-06b, D-02, CP-21)

### Requisito 11 — Ingresos

**Historia:** Como usuario, quiero registrar y gestionar mis ingresos.

#### Criterios de aceptación

1. El sistema DEBERÁ exponer endpoints para registrar, editar y eliminar
   ingresos, con confirmación y recálculo de indicadores. (RF-ING-01, RF-ING-05)
2. El sistema DEBERÁ permitir marcar un ingreso como recibido, registrando la
   fecha de recepción. (RF-ING-03)
3. SI el valor es menor o igual a cero, el sistema DEBERÁ rechazarlo. (RF-ING-04)

### Requisito 12 — Gastos y pagos

**Historia:** Como usuario, quiero presupuestar gastos y registrar pagos.

#### Criterios de aceptación

1. El sistema DEBERÁ exponer endpoints para registrar, editar y eliminar gastos
   (nombre, categoría, presupuesto, fecha, tipo), con recálculo. (RF-GAS-01,
   RF-GAS-06)
2. El sistema DEBERÁ permitir registrar uno o varios pagos sobre un gasto, con
   una clave de idempotencia para no duplicarlos. (RF-GAS-02, RNF-DAT-03, CP-12)
3. El sistema DEBERÁ derivar y exponer el estado del gasto (Pending/Partial/
   Paid) sin almacenarlo. (RF-GAS-03)
4. SI un valor es menor o igual a cero, el sistema DEBERÁ rechazarlo.

### Requisito 13 — Dashboard

**Historia:** Como usuario, quiero ver los indicadores de mi periodo.

#### Criterios de aceptación

1. El sistema DEBERÁ exponer un endpoint de dashboard que devuelva, para un
   periodo, los indicadores del motor: ingreso recibido, gasto pagado,
   obligaciones, caja, reservado, dinero libre y consumo de presupuesto.
   (RF-DSH-02, RN-F02 a RN-F14)
2. El dinero libre DEBERÁ exponerse tal cual, incluso negativo. (RF-DSH-09,
   RN-F09)
3. Los indicadores DEBERÁN provenir del `FinancialEngine`, nunca recalculados en
   la API. (RNF-MAN-02)

### Requisito 14 — Errores, formato y observabilidad

**Historia:** Como consumidor de la API, quiero errores consistentes y trazables.

#### Criterios de aceptación

1. Los errores DEBERÁN devolverse en formato Problem Details (RFC 7807) con el
   código HTTP adecuado. (RNF-MAN-06)
2. Las violaciones de invariantes del dominio DEBERÁN mapearse a respuestas 4xx
   claras, no a 500. (RNF-USA-05)
3. La API DEBERÁ versionarse en `/api/v1` y documentarse con OpenAPI.
   (RNF-MAN-06)
4. Los logs DEBERÁN ser estructurados con un identificador de correlación por
   solicitud y NO DEBERÁN contener contraseñas, tokens ni datos sensibles.
   (RNF-MAN-05, RNF-SEG-10)

### Requisito 15 — Pruebas de integración de la API

**Historia:** Como mantenedor, quiero verificar la API de extremo a extremo
contra una base real.

#### Criterios de aceptación

1. Las pruebas DEBERÁN ejercitar la API con `WebApplicationFactory` sobre una
   PostgreSQL real (Testcontainers), aplicando las migraciones. (sección de
   pruebas del SRS)
2. DEBERÁ existir una prueba del flujo de autenticación: registro → verificación
   → login → acceso autorizado → refresh → logout. (RF-AUT-01 a RF-AUT-05)
3. DEBERÁ existir una prueba de que una ruta privada sin token responde 401 y de
   que un usuario no ve datos de otro (CP-07). (RF-AUT-08, RNF-SEG-05)
4. DEBERÁ existir una prueba de un flujo financiero: crear periodo, registrar
   ingreso y gasto con pago, y leer el dashboard con los indicadores correctos.
   (CP-01, CP-02)
