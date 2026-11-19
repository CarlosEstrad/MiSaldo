# Requirements Document

> Integración continua de MiSaldo.

## Introduction

Este spec es la quinta etapa. MiSaldo ya tiene cerca de 500 pruebas automáticas
(dominio, aplicación, infraestructura y API), pero solo se ejecutan cuando
alguien las corre a mano. RNF-MAN-04 exige que se compile y se pruebe todo en
cada cambio. Este spec agrega ese pipeline en GitHub Actions, donde ya vive el
repositorio (`github.com/CarlosEstrad/MiSaldo`).

El pipeline también vigila tres cosas que hoy dependen de la memoria del
desarrollador: la cobertura mínima de pruebas (RNF-MAN-03), las dependencias con
vulnerabilidades conocidas (RNF-SEG-11) y los secretos que pudieran colarse en
el repositorio (RNF-SEG-08).

**Depende de:** spec 04 (las pruebas con Mailpit deben correr en el pipeline).

**Estado:** solo requisitos. El diseño y las tareas se escriben al iniciar esta
etapa.

**Alcance:** pipeline de compilación y pruebas (incluidas las que usan Docker),
cobertura con umbrales, análisis de dependencias vulnerables, búsqueda de
secretos, verificación de migraciones pendientes y documentación del flujo.

**Fuera de alcance:** el despliegue automático (CD) a cualquier ambiente; los
trabajos del frontend, que agrega el spec 08 cuando exista el proyecto web.

### Convenciones

- Archivos del pipeline en `.github/workflows/`; nombres de trabajos en inglés;
  este documento en español.
- Cada requisito referencia las reglas del SRS que implementa.

---

## Glossary

- **CI (integración continua):** ejecución automática de la compilación y las
  pruebas en cada cambio del repositorio.
- **GitHub Actions:** servicio de CI de GitHub; los pipelines se definen en
  archivos YAML del repositorio.
- **Workflow / job:** un pipeline completo y cada uno de sus trabajos.
- **Pull request (PR):** propuesta de cambio de una rama a otra, revisada antes
  de fusionarse.
- **Cobertura:** porcentaje del código que las pruebas ejecutan.
- **Dependencia vulnerable:** paquete con una vulnerabilidad publicada en una
  base de avisos de seguridad.
- **Migración pendiente:** cambio en el modelo de EF Core que todavía no tiene
  su migración.

---

## Requirements

### Requisito 1 — Compilación y pruebas en cada cambio

**Historia:** Como desarrollador, quiero que cada cambio se compile y se pruebe
solo, para enterarme de un error antes de fusionarlo.

#### Criterios de aceptación

1. CUANDO se haga push a `dev` o a `main`, o se abra o actualice un PR hacia
   ellas, el pipeline DEBERÁ restaurar, compilar y ejecutar todas las pruebas de
   la solución. (RNF-MAN-04)
2. El pipeline DEBERÁ usar la versión del SDK fijada en `global.json`.
3. Las pruebas que necesitan Docker (Testcontainers con PostgreSQL y Mailpit)
   DEBERÁN ejecutarse en el pipeline, no omitirse.
4. SI la compilación tiene advertencias o alguna prueba falla, el pipeline
   DEBERÁ fallar. (La solución ya trata las advertencias como errores.)
5. Los resultados de las pruebas DEBERÁN publicarse como artefacto y verse en el
   resumen del pipeline, con el nombre de cada prueba fallida.
6. El pipeline DEBERÁ guardar en caché los paquetes NuGet para no descargarlos
   en cada ejecución.

### Requisito 2 — Cobertura mínima

**Historia:** Como responsable de calidad, quiero que el pipeline mida la
cobertura y falle por debajo del mínimo, para que las reglas de negocio sigan
probadas.

#### Criterios de aceptación

1. El pipeline DEBERÁ medir la cobertura de líneas de `MiSaldo.Domain` y
   `MiSaldo.Application` con todas las pruebas de la solución. (RNF-MAN-03)
2. SI la cobertura de `MiSaldo.Domain` es menor a 90 % o la de
   `MiSaldo.Application` es menor a 70 %, el pipeline DEBERÁ fallar e indicar el
   valor medido. (RNF-MAN-03)
3. El informe de cobertura DEBERÁ publicarse como artefacto y su resumen
   mostrarse en el pipeline.

### Requisito 3 — Dependencias vulnerables

**Historia:** Como responsable de seguridad, quiero saber cuándo un paquete que
uso tiene una vulnerabilidad conocida, para actualizarlo a tiempo.

#### Criterios de aceptación

1. El pipeline DEBERÁ revisar los paquetes NuGet, incluidos los transitivos,
   contra la base de avisos de seguridad. (RNF-SEG-11)
2. SI hay una vulnerabilidad de severidad alta o crítica, el pipeline DEBERÁ
   fallar y nombrar el paquete y el aviso; las de severidad baja o moderada
   DEBERÁN reportarse sin fallar.
3. El repositorio DEBERÁ tener activadas las actualizaciones automáticas de
   dependencias (Dependabot) para NuGet y GitHub Actions, con PRs semanales.

### Requisito 4 — Secretos en el repositorio

**Historia:** Como responsable de seguridad, quiero que el pipeline detecte un
secreto antes de que llegue al historial compartido, para no exponer
credenciales.

#### Criterios de aceptación

1. El pipeline DEBERÁ buscar secretos (claves, contraseñas, tokens) en los
   cambios de cada push y PR. (RNF-SEG-08)
2. SI encuentra uno, el pipeline DEBERÁ fallar e indicar el archivo y la línea,
   sin imprimir el valor completo.
3. Los valores de ejemplo de `.env.example` y las contraseñas fijas de las
   pruebas (contenedores efímeros) DEBERÁN poder excluirse de forma explícita y
   documentada.

### Requisito 5 — Migraciones al día

**Historia:** Como mantenedor, quiero que el pipeline falle si cambié el modelo
de datos sin crear la migración, para que la base de producción no quede
desalineada.

#### Criterios de aceptación

1. SI el modelo de EF Core tiene cambios sin migración, el pipeline DEBERÁ
   fallar. (RNF-DAT-08)
2. Las migraciones DEBERÁN aplicarse sobre una base vacía dentro de las pruebas,
   como ya ocurre con Testcontainers.

### Requisito 6 — Seguridad del propio pipeline

**Historia:** Como responsable de seguridad, quiero que el pipeline tenga solo los
permisos que necesita, para que un PR malicioso no pueda usarlo contra el
repositorio.

#### Criterios de aceptación

1. Los workflows DEBERÁN declarar permisos mínimos (`contents: read` por
   defecto) y ampliarlos solo en el trabajo que lo necesite.
2. Las acciones de terceros DEBERÁN fijarse a una versión concreta.
3. El pipeline NO DEBERÁ necesitar secretos para compilar y probar; los PRs desde
   forks NO DEBERÁN tener acceso a secretos.

### Requisito 7 — Flujo de trabajo documentado

**Historia:** Como desarrollador, quiero saber qué revisa el pipeline y cómo
reproducirlo en mi equipo, para corregir un fallo sin adivinar.

#### Criterios de aceptación

1. El README DEBERÁ mostrar el estado del pipeline (insignia) y explicar qué
   revisa.
2. El README DEBERÁ indicar cómo ejecutar en local cada revisión: pruebas,
   cobertura, dependencias vulnerables y migraciones pendientes.
3. La documentación DEBERÁ recomendar proteger `main` para exigir el pipeline en
   verde antes de fusionar, con los pasos para configurarlo en GitHub.
