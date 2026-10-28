# Términos de Uso — MiSaldo

| Campo | Valor |
|---|---|
| Producto | MiSaldo |
| Versión del documento | 1.0 |
| Estado | **EN PROCESO — pendiente de revisión legal** |
| Responsable | Carlos Alberto Estrada Valle |
| Fecha | 2026-10-26 |
| País | Colombia |

> **Aviso.** Este documento es un borrador funcional para el desarrollo del producto. Antes de la publicación al público, **debe ser revisado y aprobado por un abogado**. Los textos que ve la persona usuaria en la aplicación se cargan desde archivos de traducción, no desde el código (ver RNF-I18-01 del SRS).

---

## 1. Aceptación de los términos

Al crear una cuenta o usar MiSaldo, la persona usuaria acepta estos Términos de Uso y la [Política de Privacidad](./PRIVACIDAD-MiSaldo.md). Si no está de acuerdo con ellos, no debe usar la aplicación.

El sistema guarda la **versión y la fecha de aceptación** de estos términos (ver RF-PRV-01 del SRS).

---

## 2. Descripción del servicio

MiSaldo es una aplicación web y móvil para la gestión de finanzas personales. Permite a la persona usuaria:

- Administrar ingresos y gastos por periodos de pago.
- Planificar obligaciones y controlar créditos **sin intereses**.
- Registrar objetivos y movimientos de ahorro.
- Visualizar indicadores como dinero en caja, dinero libre, ahorro acumulado, deuda y patrimonio.

El registro de movimientos es **manual**. MiSaldo **no se conecta con bancos** ni importa información financiera de forma automática.

---

## 3. Requisitos para usar MiSaldo

- La persona debe ser **mayor de 18 años**. El registro exige fecha de nacimiento y se rechaza a quien no cumpla la edad mínima (ver RF-AUT-02 del SRS).
- La persona debe proporcionar datos veraces y mantener actualizada su información.
- La persona es responsable de la confidencialidad de sus credenciales de acceso.

---

## 4. Cuenta de la persona usuaria

- Cada persona accede únicamente a sus propios datos.
- La autenticación se gestiona mediante Amazon Cognito.
- La persona es responsable de la actividad realizada desde su cuenta.
- La persona puede descargar sus datos y solicitar la eliminación de su cuenta en cualquier momento (ver secciones 6 y 7 de la Política de Privacidad).

---

## 5. Uso aceptable

La persona usuaria se compromete a **no**:

- Usar MiSaldo para fines ilícitos o no autorizados.
- Intentar acceder a datos de otras personas usuarias.
- Vulnerar, probar o eludir los mecanismos de seguridad de la aplicación.
- Interferir con el funcionamiento del servicio (por ejemplo, con cargas automatizadas abusivas).
- Suplantar la identidad de otra persona.

El incumplimiento puede derivar en la suspensión o eliminación de la cuenta.

---

## 6. Naturaleza de la información (no es asesoría financiera)

- MiSaldo es una **herramienta de organización y seguimiento**. Los indicadores, alertas y recomendaciones se basan únicamente en los datos que la persona registra.
- MiSaldo **no presta asesoría financiera, contable, tributaria ni legal**, y sus resultados no son garantías ni promesas de rendimiento (ver RF-NOT-02 del SRS).
- Las decisiones financieras que tome la persona usuaria son de su exclusiva responsabilidad.

---

## 7. Exactitud de los datos

- La calidad de los cálculos depende de la información que la persona registra. Datos incompletos o erróneos producen resultados incorrectos.
- MiSaldo calcula los indicadores en el servidor a partir de los movimientos registrados; la persona es responsable de registrar sus movimientos de forma correcta.

---

## 8. Disponibilidad del servicio

- MiSaldo se ofrece "tal cual" y "según disponibilidad".
- Se procura una alta disponibilidad, pero pueden existir interrupciones por mantenimiento, fallas técnicas o causas externas.
- Se realizan copias de seguridad, pero la persona usuaria es responsable de descargar y conservar sus datos si lo considera necesario.

---

## 9. Propiedad intelectual

- El software, la marca MiSaldo, el diseño y el contenido propio de la aplicación pertenecen a su titular.
- Los **datos financieros y personales registrados por la persona usuaria son de su propiedad**; MiSaldo solo los trata para prestar el servicio, conforme a la Política de Privacidad.

---

## 10. Limitación de responsabilidad

En la medida permitida por la ley, MiSaldo y su responsable no serán responsables por:

- Decisiones financieras tomadas con base en la información de la aplicación.
- Pérdidas derivadas de datos ingresados de forma incorrecta por la persona usuaria.
- Interrupciones, errores o indisponibilidad temporal del servicio.
- Pérdida de datos cuando la persona no haya conservado una copia disponible mediante la función de descarga.

---

## 11. Modificaciones del servicio y de los términos

- MiSaldo puede actualizar sus funciones y estos términos.
- Cuando el cambio sea sustancial, se comunicará a la persona usuaria y, cuando corresponda, se solicitará una nueva aceptación.
- El historial de versiones se registra al final de este documento.

---

## 12. Terminación

- La persona usuaria puede dejar de usar MiSaldo y eliminar su cuenta en cualquier momento.
- MiSaldo puede suspender o cancelar cuentas que incumplan estos términos.
- Al eliminar la cuenta se borran los datos personales y financieros, conservando únicamente un registro de la eliminación con el motivo y el autor del movimiento, y lo que la ley exija (ver RF-PRV-03 y D-12 del SRS).

---

## 13. Ley aplicable

Estos términos se rigen por las leyes de la República de Colombia. Cualquier controversia se resolverá ante las autoridades competentes de Colombia.

---

## 14. Contacto

Para dudas sobre estos Términos de Uso, la persona usuaria puede escribir a **carlosestradavalle@outlook.com**. Este correo es provisional y se reemplazará por un correo corporativo antes del lanzamiento.

---

## 15. Control de versiones

| Versión | Fecha | Cambio |
|---|---|---|
| 1.0 | 2026-10-26 | Versión inicial para desarrollo, pendiente de revisión legal. |
