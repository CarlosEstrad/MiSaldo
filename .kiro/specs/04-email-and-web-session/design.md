# Diseño — Envío de correos y sesión web

## Overview

Tres cambios sobre la API existente y un entorno local nuevo:

- **Correos por SMTP.** Un `SmtpAccountNotifier` (MailKit) reemplaza al
  notificador en memoria cuando hay configuración SMTP. Los correos salen de
  plantillas en español e inglés con un enlace al frontend. Un endpoint nuevo
  reenvía la verificación.
- **Refresh token en cookie.** El login y el refresh lo entregan en una cookie
  `HttpOnly` y ya no en el cuerpo; refresh y logout lo leen de la cookie y
  rechazan peticiones con un `Origin` ajeno.
- **CORS** para los orígenes configurados del frontend.
- **Entorno local** con Docker Compose: PostgreSQL con el rol de aplicación sin
  `BYPASSRLS`, Mailpit y pgAdmin, todo publicado solo en `127.0.0.1`.

Los casos de uso de cuenta no cambian de responsabilidad: siguen generando los
tokens y llamando a `IAccountNotifier`; solo el mensaje lleva ahora el idioma y
el vencimiento.

### Estructura de la solución (lo que se agrega o cambia)

```
src/
├── MiSaldo.Application/
│   ├── Abstractions/IAccountNotifier.cs     # AccountTokenMessage + Language, ExpiresAt
│   └── Accounts/
│       ├── RegisterUser.cs                  # idioma opcional en el registro
│       └── ResendVerification.cs            # caso de uso nuevo
├── MiSaldo.Infrastructure/
│   └── Email/
│       ├── SmtpOptions.cs                   # configuración y validación
│       ├── SmtpAccountNotifier.cs           # envío con MailKit, tiempo máximo, sin secretos en logs
│       ├── AccountEmailComposer.cs          # arma asunto, texto y HTML desde las plantillas
│       └── Templates/                       # recursos incrustados por idioma
│           ├── verify-email.es.txt / .html
│           ├── verify-email.en.txt / .html
│           ├── reset-password.es.txt / .html
│           └── reset-password.en.txt / .html
└── MiSaldo.Api/
    ├── Auth/
    │   ├── RefreshCookie.cs                 # escribir, leer y borrar la cookie
    │   ├── SameOriginGuard.cs               # filtro: Origin permitido en refresh/logout
    │   └── AuthController.cs                # usa la cookie; endpoint resend-verification
    └── Common/
        ├── CorsSetup.cs                     # política CORS y validación de orígenes
        └── FrontendOptions.cs               # URL base del frontend
deploy/dev/
├── postgres-init/01-app-role.sh             # crea misaldo_app sin BYPASSRLS y sus permisos
└── pgadmin/servers.json                     # conexión de administración registrada
docker-compose.dev.yml                       # postgres, mailpit, pgadmin
tests/MiSaldo.Api.Tests/
├── MailpitFixture.cs                        # Mailpit real en Testcontainers + cliente de su API
└── EmailAndSessionTests.cs
```

## Architecture

| # | Decisión | Motivo |
|---|---|---|
| E1 | **MailKit** para SMTP (versión estable al implementar; hoy 5.0.0). `System.Net.Mail.SmtpClient` está marcado por Microsoft como no recomendado para código nuevo. | Soporte de STARTTLS/TLS, tiempo máximo y cancelación. |
| E2 | Selección del notificador al arrancar: si `Smtp:Host` tiene valor, `SmtpAccountNotifier`; si no, el en memoria, y solo en Development o pruebas. En Production sin SMTP válido la API no arranca. | R1.4, R1.5; un fallo de configuración se ve al desplegar, no al primer registro. |
| E3 | El notificador SMTP **no lanza** al fallar el envío: registra el fallo (sin token, enlace ni contraseña) y devuelve. El tiempo máximo (`Smtp:TimeoutSeconds`, 10 s por defecto) se aplica con un `CancellationTokenSource` enlazado. | R3: la cuenta se conserva y la respuesta no cambia; el usuario reenvía con R4. |
| E4 | `AccountTokenMessage` gana `Language` y `ExpiresAt`; los casos de uso los llenan desde la configuración del usuario y el token. | El notificador no consulta la base; solo arma y envía. |
| E5 | Plantillas como **recursos incrustados** en Infrastructure, una por correo, idioma y formato. Reemplazo de `{{Link}}`, `{{ValidFor}}` y `{{Name}}`; en HTML los valores se codifican. | RF-CFG-03 (textos fuera de la lógica); sin motor de plantillas extra. |
| E6 | El registro acepta `language` opcional (`es`/`en`, por defecto `es`), validado igual que en la configuración y guardado en `UserSettings`. | R2.3: el primer correo ya sale en el idioma del usuario. |
| E7 | Reenvío de verificación: con cuenta existente y no verificada, consume los tokens de verificación vigentes y emite uno nuevo; si el último se emitió hace menos de 60 s, no hace nada. Siempre responde 202. | R4; respuesta uniforme (RNF-SEG-05). |
| E8 | Cookie `__Secure-misaldo-refresh`: `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/api/v1/auth`, `Max-Age` igual a la vigencia del refresh token. El prefijo `__Secure-` obliga al navegador a exigir `Secure`. | R5.1, RNF-SEG-03. `__Host-` exigiría `Path=/`. |
| E9 | Respuesta de login y refresh: `SessionResponse` (`userId`, `accessToken`, `accessTokenExpiresAt`, `sessionExpiresAt`, `emailVerified`), sin el refresh token. `AuthTokens` sigue igual dentro de Application. | R5.2; la cookie es asunto de HTTP, no del caso de uso. |
| E10 | `SameOriginGuard` (filtro de acción) en refresh y logout: si llega `Origin`, debe ser un origen CORS permitido o el de la propia API; si no, 403 Problem Details `auth.origin_not_allowed`. Sin `Origin` (clientes que no son navegador) se acepta. | R6.1: defensa adicional a `SameSite=Strict`. |
| E11 | CORS con el middleware nativo: una política con los orígenes de `Cors:AllowedOrigins`, `AllowCredentials`, métodos y cabeceras explícitos, cabeceras expuestas y `SetPreflightMaxAge(10 min)`. Validación al arrancar: sin comodines, solo `http`/`https` con host y sin ruta. | R7. |
| E12 | Orden del pipeline: correlación → excepciones → status pages → **CORS** → autenticación → rate limiting → autorización. CORS va antes de la autenticación para que el preflight no exija token. | Comportamiento documentado del middleware de CORS. |
| E13 | Entorno local con Docker Compose: `postgres:17-alpine` (misma imagen que las pruebas), `axllent/mailpit:v1.34`, `dpage/pgadmin4:9`. Volúmenes para PostgreSQL y pgAdmin; Mailpit sin volumen. `restart: unless-stopped`. Puertos en `127.0.0.1`. | R8. |
| E14 | El superusuario del contenedor (`POSTGRES_USER`) aplica las migraciones y es la conexión de administración de pgAdmin. Un script de inicio crea `misaldo_app` (`NOSUPERUSER NOBYPASSRLS`) y le da permisos por defecto sobre las tablas que creen las migraciones. | R8.2: misma separación de roles que las pruebas. |
| E15 | Las variables del entorno local viven en el `.env` de la raíz (ya ignorado por git). Compose lo lee solo; para `dotnet run` se cargan con `set -a; source .env; set +a`. La clave JWT se genera una vez y se guarda ahí. | R8.3, R8.6. |

## Components and Interfaces

### Configuración nueva

| Clave (variable de entorno) | Uso | Por defecto |
|---|---|---|
| `Smtp__Host`, `Smtp__Port` | Servidor SMTP | sin servidor; puerto 587 |
| `Smtp__Security` | `StartTls`, `SslOnConnect` o `None` (`None` prohibido en Production) | `StartTls` |
| `Smtp__Username`, `Smtp__Password` | Credenciales (opcionales) | vacías |
| `Smtp__FromAddress`, `Smtp__FromName` | Remitente | obligatorio si hay servidor; `MiSaldo` |
| `Smtp__TimeoutSeconds` | Tiempo máximo de envío | 10 |
| `Frontend__BaseUrl` | Base de los enlaces de los correos | obligatorio si hay SMTP |
| `Cors__AllowedOrigins__0`, `__1`, … | Orígenes del frontend | ninguno (solo mismo origen) |

Ejemplos por etapa (D-13):

| Etapa | Host | Puerto | Seguridad | Usuario / contraseña |
|---|---|---|---|---|
| Desarrollo (Mailpit) | `localhost` | 1025 | `None` | vacíos |
| Gmail dedicado | `smtp.gmail.com` | 587 | `StartTls` | la cuenta y su contraseña de aplicación |
| Producción (SES u otro) | el del proveedor | 587 | `StartTls` | credenciales SMTP del proveedor |

### Endpoints que cambian o se agregan

| Ruta | Cambio |
|---|---|
| `POST /api/v1/auth/register` | Acepta `language` opcional (`es`/`en`). |
| `POST /api/v1/auth/login` | Responde `SessionResponse` y entrega la cookie. |
| `POST /api/v1/auth/refresh` | Sin cuerpo; lee la cookie, la rota y responde `SessionResponse`. 401 si falta o no vale (y borra la cookie). |
| `POST /api/v1/auth/logout` | Sin cuerpo; lee la cookie, revoca y la borra. Siempre 204. |
| `POST /api/v1/auth/reset-password` | Además de revocar las sesiones, borra la cookie de la petición. |
| `POST /api/v1/auth/resend-verification` | **Nuevo.** Cuerpo `{ "email" }`; siempre 202. |

Refresh y logout llevan `SameOriginGuard`. Todas las respuestas que tocan la
cookie llevan `Cache-Control: no-store` (ya lo tiene el controlador).

### Interfaces

```csharp
// Application
public sealed record AccountTokenMessage(
    Guid UserId, string Email, string Token, string Language, DateTimeOffset ExpiresAt);

public sealed record ResendVerificationCommand(string Email);
public sealed class ResendVerificationHandler { Task<Result> HandleAsync(ResendVerificationCommand, CancellationToken); }

// IAuthRepository (nuevo método)
Task<IReadOnlyList<OneTimeToken>> ListUsableOneTimeTokensAsync(
    Guid userId, OneTimeTokenPurpose purpose, DateTimeOffset now, CancellationToken);

// Infrastructure
public sealed class SmtpAccountNotifier : IAccountNotifier { ... }
public sealed class AccountEmailComposer { AccountEmail Compose(AccountEmailKind, AccountTokenMessage); }
public sealed record AccountEmail(string Subject, string Text, string Html);

// Api
public static class RefreshCookie
{
    public const string Name = "__Secure-misaldo-refresh";
    void Write(HttpResponse, string token, DateTimeOffset expiresAt);
    string? Read(HttpRequest);
    void Delete(HttpResponse);
}
```

## Data Models

No hay tablas ni columnas nuevas. Cambios en tipos:

- **`AccountTokenMessage`** — agrega `Language` (`es`/`en`) y `ExpiresAt`.
  Sigue redactado en `ToString()`.
- **`RegisterUserCommand` / `RegisterRequest`** — agregan `Language` opcional.
- **`SessionResponse`** (API) — respuesta de login y refresh sin el refresh
  token; redactada en `ToString()` porque lleva el access token.
- **`OneTimeToken`** — sin cambios; el reenvío usa `CreatedAt` para la espera
  de 60 s y `Consume` para invalidar los anteriores.

## Error Handling

| Situación | Respuesta |
|---|---|
| Fallo o tiempo agotado del servidor SMTP | Ninguna diferencia para el cliente; `Warning` en el log con el tipo de error y el dominio del destinatario |
| Production sin SMTP válido, `None` en Production, o `Cors:AllowedOrigins` con comodín o mal formado | La API no arranca; el mensaje nombra la clave, sin valores |
| Refresh sin cookie o con cookie inválida, usada o vencida | 401 `auth.invalid_refresh_token` y la cookie se borra |
| Refresh o logout con `Origin` ajeno | 403 `auth.origin_not_allowed`; la sesión no se toca |
| Reenvío de verificación (cualquier caso) | 202 sin cuerpo |
| Preflight CORS de un origen no permitido | Respuesta sin cabeceras CORS (el navegador bloquea) |

## Testing Strategy

- **Unitarias (sin Docker):**
  - `SmtpOptions`: validación, `None` prohibido en Production.
  - `AccountEmailComposer`: idioma, enlace, vigencia y codificación HTML.
  - Validación de orígenes CORS.
  - Selección del notificador por entorno.
  - `RefreshCookie`: atributos de la cookie.
- **Integración 🐳 con Mailpit real** (`axllent/mailpit` en Testcontainers,
  leído por su API HTTP `/api/v1/messages`):
  - Registro en español: llega el correo, se extrae el enlace, se verifica la
    cuenta por la API.
  - Registro en inglés y recuperación en inglés: asunto y texto en inglés.
  - Reenvío: invalida el enlace anterior; un segundo pedido antes de 60 s no
    envía otro correo.
  - SMTP caído (puerto cerrado): el registro responde 201 y la cuenta existe.
- **Sesión por cookie 🐳:** el cliente de prueba usa `https://localhost` como
  base, porque las cookies `Secure` no viajan por `http`. Login →
  refresh → logout solo con la cookie; atributos de la cookie; el cuerpo no trae
  el refresh token; reutilización rechazada y cookie borrada.
- **CORS y origen:** preflight de un origen permitido y de uno no permitido;
  refresh con `Origin` ajeno → 403.
- **Arranque:** Production sin SMTP y con comodín en CORS no arranca.
- **Regresión:** las pruebas existentes se ajustan al refresh por cookie; la
  prueba de logs sin secretos se extiende al envío real por Mailpit (el token y
  el enlace no aparecen en ningún log).
- **Entorno local:** se verifica levantándolo, aplicando las migraciones,
  arrancando la API contra él y completando un registro con verificación vía
  Mailpit; y comprobando que `misaldo_app` no ve filas sin `app.user_id`.

## Correctness Properties

1. **Un solo notificador por entorno:** en Production solo existe el SMTP; el
   en memoria nunca se activa fuera de Development o pruebas.
2. **Envío sin secretos en logs:** ningún token, enlace ni contraseña SMTP
   aparece en un log, a ningún nivel.
3. **Respuesta uniforme:** recuperación y reenvío responden igual exista o no
   la cuenta, y funcione o no el servidor de correo.
4. **Un enlace de verificación vigente:** después de un reenvío, solo el último
   token de verificación sirve.
5. **El refresh token nunca llega al JavaScript:** no está en ningún cuerpo de
   respuesta y la cookie es `HttpOnly`.
6. **La cookie solo viaja donde debe:** `Secure`, `SameSite=Strict` y limitada
   a `/api/v1/auth`.
7. **Ningún sitio ajeno usa la sesión:** refresh y logout con un `Origin` no
   permitido no cambian nada; CORS no concede permisos a orígenes no
   configurados.
8. **Rotación intacta:** cada refresh invalida el token anterior y la
   reutilización revoca la sesión, igual que antes.
9. **Datos locales persistentes y aislados:** los datos sobreviven a apagar el
   equipo, la base solo escucha en `127.0.0.1` y la aplicación local corre con
   un rol sujeto a RLS.

## Trazabilidad requisito → componente

| Requisito | Componentes principales |
|---|---|
| R1 Notificador SMTP | `SmtpOptions`, `SmtpAccountNotifier`, registro en `ServiceRegistration` |
| R2 Contenido | `AccountEmailComposer`, `Templates/*`, `AccountTokenMessage`, idioma en el registro |
| R3 Fallos de envío | `SmtpAccountNotifier` (tiempo máximo, sin lanzar) |
| R4 Reenvío | `ResendVerificationHandler`, `ListUsableOneTimeTokensAsync`, `AuthController` |
| R5 Cookie | `RefreshCookie`, `SessionResponse`, `AuthController` |
| R6 Protección | `SameOriginGuard`, `SameSite=Strict`, `no-store` |
| R7 CORS | `CorsSetup`, orden del pipeline |
| R8 Entorno local | `docker-compose.dev.yml`, `deploy/dev/*`, README |
| R9 Configuración | `.env.example`, README, OpenAPI |
| R10 Pruebas | `MailpitFixture`, `EmailAndSessionTests`, ajustes a las pruebas existentes |
