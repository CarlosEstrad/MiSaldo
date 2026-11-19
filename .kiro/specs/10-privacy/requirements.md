# Requirements Document

> Privacidad y datos personales: consentimientos, exportación y eliminación de
> la cuenta.

## Introduction

Este spec es la décima etapa y cierra F1 con los derechos del usuario sobre sus
datos:

- **Consultar** qué versión de la política de privacidad y de los términos
  aceptó (RF-PRV-01). Hoy se guardan al registrarse, pero no se pueden
  consultar.
- **Descargar** todos sus datos en JSON (RF-PRV-02).
- **Eliminar** su cuenta y todos sus datos personales y financieros, conservando
  solo el registro de la eliminación con el motivo y quién la hizo (RF-PRV-03,
  D-12).

El SRS exige cumplir la Ley 1581 de 2012 de Colombia y pide **validar con un
abogado** antes del lanzamiento. Este spec implementa lo que piden el SRS y la
política de privacidad (`PRIVACIDAD-MiSaldo.md`); la revisión legal queda como
paso previo al despliegue, no como parte del código.

**Depende de:** spec 04 (correos), spec 08 (pantallas) y spec 09 (historial,
auditoría y ajustes, que también deben exportarse y eliminarse).

**Estado:** solo requisitos. El diseño y las tareas se escriben al iniciar esta
etapa.

**Alcance:** API, casos de uso y pantallas de consentimientos, exportación y
eliminación; correos de confirmación; pruebas.

**Fuera de alcance:** la revisión legal; el borrado en copias de seguridad, que
se rige por su política de retención (se documenta); la administración global
de cuentas (Futuro).

### Convenciones

- Rutas y campos en inglés; este documento en español.
- Cada requisito referencia las reglas del SRS que implementa.

---

## Glossary

- **Consentimiento:** aceptación de una versión de la política de privacidad y
  de los términos de uso, con su fecha (`ConsentRecord`).
- **Exportación:** archivo JSON con todos los datos del usuario.
- **Confirmación reforzada:** paso adicional antes de una acción irreversible,
  por ejemplo pedir la contraseña y escribir una frase.
- **Registro de eliminación:** fila de `AccountDeletionRecord` con el motivo, el
  autor y la fecha, sin datos personales (D-12).
- **Retención de copias de seguridad:** tiempo durante el cual las copias de la
  base guardan datos que ya se eliminaron de la base activa.

---

## Requirements

### Requisito 1 — Consulta de consentimientos

**Historia:** Como usuario, quiero saber qué versión de la política y de los
términos acepté y cuándo.

#### Criterios de aceptación

1. El sistema DEBERÁ exponer las versiones aceptadas de la política de privacidad
   y de los términos, con su fecha. (RF-PRV-01)
2. El sistema DEBERÁ indicar si existen versiones más recientes que las
   aceptadas.

### Requisito 2 — Exportación de datos

**Historia:** Como usuario, quiero descargar todos mis datos, para tenerlos o
llevarlos a otro lado.

#### Criterios de aceptación

1. CUANDO el usuario lo solicite, el sistema DEBERÁ entregar un archivo JSON con
   todos sus datos: perfil, configuración, consentimientos, categorías, periodos,
   ingresos, gastos, pagos, ajustes y su auditoría. (RF-PRV-02)
2. La exportación NO DEBERÁ incluir el hash de la contraseña, los tokens ni otros
   datos internos de seguridad. (RNF-SEG-10)
3. Los montos DEBERÁN exportarse como cadenas decimales con su moneda, y las
   fechas en ISO 8601. (RNF-DAT-02)
4. El archivo DEBERÁ incluir la fecha de generación y una versión del formato.
5. La exportación DEBERÁ estar limitada en frecuencia, para que no se use como
   ataque de carga. (RNF-SEG-07)
6. Solo el propio usuario DEBERÁ poder exportar sus datos. (RNF-SEG-04)

### Requisito 3 — Eliminación de la cuenta

**Historia:** Como usuario, quiero eliminar mi cuenta y todos mis datos, sabiendo
exactamente qué se borra.

#### Criterios de aceptación

1. Antes de eliminar, el sistema DEBERÁ pedir una confirmación reforzada: la
   contraseña actual y una frase escrita por el usuario. (RF-PRV-03)
2. El usuario DEBERÁ indicar un motivo, de una lista cerrada con opción de texto
   libre. (D-12)
3. CUANDO se confirme, el sistema DEBERÁ eliminar en una sola transacción todos
   los datos personales y financieros del usuario, incluida su auditoría y sus
   credenciales. (RF-PRV-03, RNF-DAT-01)
4. El sistema DEBERÁ conservar únicamente el registro de eliminación con el
   motivo, quién la realizó y la fecha, sin datos personales. (D-12)
5. Las sesiones del usuario DEBERÁN revocarse y la cookie de sesión borrarse.
6. Antes de borrar el correo, el sistema DEBERÁ enviar a esa dirección un aviso de
   que la cuenta se eliminó.
7. Tras la eliminación, el correo DEBERÁ poder usarse para una cuenta nueva.
8. SI la contraseña no es correcta, el sistema NO DEBERÁ eliminar nada y DEBERÁ
   contar el intento como fallido. (RF-AUT-07)

### Requisito 4 — Pantallas

**Historia:** Como usuario, quiero hacer todo esto desde la aplicación, con
explicaciones claras.

#### Criterios de aceptación

1. El frontend DEBERÁ mostrar los consentimientos aceptados con enlaces a los
   textos. (RF-PRV-01)
2. El frontend DEBERÁ permitir descargar la exportación. (RF-PRV-02)
3. El frontend DEBERÁ explicar qué se borra y qué se conserva antes de pedir la
   confirmación reforzada de la eliminación. (RF-PRV-03, RNF-USA-04)

### Requisito 5 — Documentación

**Historia:** Como responsable del producto, quiero que lo que hace el sistema
coincida con lo que promete la política de privacidad.

#### Criterios de aceptación

1. La política de privacidad DEBERÁ revisarse para que describa la exportación,
   la eliminación y lo que se conserva, igual que el sistema.
2. La documentación DEBERÁ explicar cuánto tiempo conservan las copias de
   seguridad los datos eliminados.
3. La revisión legal con un abogado DEBERÁ quedar registrada como pendiente
   previo al lanzamiento.

### Requisito 6 — Pruebas

**Historia:** Como mantenedor, quiero confirmar que la eliminación borra todo y la
exportación no deja nada afuera.

#### Criterios de aceptación

1. DEBERÁ existir una prueba de que, tras eliminar una cuenta, no queda ninguna
   fila con su `UserId` en ninguna tabla, salvo el registro de eliminación.
2. DEBERÁ existir una prueba de que la exportación incluye cada tipo de dato y
   ningún secreto.
3. DEBERÁ existir una prueba de que eliminar una cuenta no afecta los datos de
   otro usuario. (CP-07)
4. DEBERÁN existir pruebas de la confirmación reforzada: contraseña incorrecta,
   frase incorrecta y motivo faltante no eliminan nada.
