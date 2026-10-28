# Política de Privacidad y Consentimiento — MiSaldo

| Campo | Valor |
|---|---|
| Producto | MiSaldo |
| Versión del documento | 1.0 |
| Estado | **EN PROCESO — pendiente de revisión legal** |
| Responsable del tratamiento | Carlos Alberto Estrada Valle |
| Fecha | 2026-10-26 |
| Marco legal | Ley 1581 de 2012 y Decreto 1377 de 2013 (Colombia) |

> **Aviso.** Este documento es un borrador funcional para el desarrollo del producto. Antes de la publicación al público, **debe ser revisado y aprobado por un abogado** especializado en protección de datos. Los textos que ve el usuario en la aplicación se cargan desde archivos de traducción, no desde el código (ver RNF-I18-01 del SRS).

---

## 1. Objeto

Esta política describe cómo MiSaldo recolecta, usa, almacena, protege y elimina los datos personales de las personas usuarias, y las condiciones bajo las cuales la persona otorga su **consentimiento libre, previo, expreso e informado** para el tratamiento de dichos datos.

Al crear una cuenta, la persona usuaria declara ser **mayor de 18 años** y aceptar esta política y los términos de uso.

---

## 2. Responsable del tratamiento

| Dato | Valor |
|---|---|
| Responsable | Carlos Alberto Estrada Valle |
| Producto | MiSaldo |
| Canal de contacto para datos personales | carlosestradavalle@outlook.com (provisional, hasta habilitar un correo corporativo) |
| País | Colombia |

---

## 3. Datos que se recolectan

MiSaldo recolecta únicamente los datos necesarios para prestar el servicio. No se solicitan datos personales distintos de los aquí listados (ver RF-PER-05 del SRS).

### 3.1 Datos de identificación y cuenta 
- Nombre completo.
- Correo electrónico.
- Fecha de nacimiento (para verificar que la persona es mayor de 18 años).
- Género (opcional).
- País, moneda principal e idioma.
- Fecha de creación de la cuenta.

### 3.2 Datos de autenticación
- Las credenciales de acceso se gestionan mediante **Amazon Cognito**. MiSaldo no almacena contraseñas en su base de datos.

### 3.3 Datos financieros personales
- Ingresos, gastos, pagos, periodos, categorías, créditos sin intereses, objetivos y movimientos de ahorro que la persona registra de forma manual.
- MiSaldo **no se conecta con bancos** ni importa movimientos automáticamente.

### 3.4 Datos técnicos
- Registros de auditoría de operaciones (quién, cuándo y qué cambió), sin incluir contraseñas ni tokens.
- Identificadores de sesión y datos mínimos de seguridad (por ejemplo, control de intentos de inicio de sesión).

---

## 4. Finalidades del tratamiento

Los datos se tratan para:

1. Crear y administrar la cuenta de la persona usuaria.
2. Verificar que la persona es mayor de 18 años.
3. Prestar las funciones del producto: control de ingresos y gastos por periodos, planificación, seguimiento, ahorro y créditos sin intereses.
4. Calcular indicadores financieros (caja, dinero libre, ahorro acumulado, deuda, patrimonio).
5. Garantizar la seguridad, la integridad y la trazabilidad de las operaciones.
6. Atender solicitudes de la persona sobre sus datos (acceso, descarga, eliminación).
7. Cumplir obligaciones legales aplicables.

Los datos **no** se usan para asesoría financiera personalizada ni se presentan recomendaciones como resultados garantizados (ver RF-NOT-02 del SRS).

---

## 5. Base del tratamiento: consentimiento

- El tratamiento se basa en el **consentimiento expreso** otorgado por la persona al momento del registro.
- El sistema guarda la **versión y la fecha de aceptación** de esta política y de los términos de uso (ver RF-PRV-01 del SRS).
- Si esta política cambia de forma sustancial, se solicitará un nuevo consentimiento.

### Texto de consentimiento (propuesto para el registro)

> Declaro que soy mayor de 18 años. Autorizo de forma libre, previa, expresa e informada a MiSaldo para recolectar y tratar mis datos personales y financieros con las finalidades descritas en la Política de Privacidad, que declaro haber leído y aceptado. Entiendo que puedo ejercer mis derechos de conocer, actualizar, rectificar y suprimir mis datos en cualquier momento.

---

## 6. Derechos de la persona usuaria (habeas data)

Conforme a la Ley 1581 de 2012, la persona puede:

- **Conocer** los datos que MiSaldo tiene sobre ella.
- **Actualizar y rectificar** sus datos (perfil editable; la fecha de nacimiento no es editable).
- **Descargar** todos sus datos en formato JSON (ver RF-PRV-02 del SRS).
- **Revocar el consentimiento** y **solicitar la eliminación** de su cuenta y datos (ver RF-PRV-03 del SRS).
- **Presentar consultas y reclamos** por el canal de contacto indicado.

---

## 7. Conservación y eliminación de datos

- Los datos se conservan mientras la cuenta esté activa.
- CUANDO la persona solicite eliminar su cuenta, MiSaldo pedirá una confirmación reforzada y eliminará **todos** sus datos personales y financieros.
- Se conservará únicamente un **registro de la eliminación** con el **motivo** y la identidad de **quién realizó el movimiento**, además de lo que la ley exija conservar (ver D-12 y RF-PRV-03 del SRS). Este registro no contiene datos personales de la persona eliminada más allá de lo estrictamente necesario para la trazabilidad.

---

## 8. Seguridad de los datos

MiSaldo aplica medidas técnicas y organizativas, entre ellas:

- Comunicaciones cifradas mediante HTTPS (TLS 1.2 o superior).
- Aislamiento estricto por usuario: cada persona solo accede a sus propios datos.
- Identidad y contraseñas gestionadas por Amazon Cognito.
- Secretos fuera del código, en un gestor de secretos.
- Registros (logs) sin contraseñas, tokens ni datos personales innecesarios.
- Copias de seguridad con recuperación a un punto en el tiempo.

(Las medidas detalladas están en la sección 5.1 del SRS.)

---

## 9. Encargados y transferencias

Para prestar el servicio, MiSaldo se apoya en proveedores de infraestructura en la nube (Amazon Web Services), que actúan como **encargados del tratamiento**:

- **Amazon Cognito** — gestión de identidad y autenticación.
- **Amazon RDS (PostgreSQL)** — almacenamiento de datos.
- **Amazon SES** — envío de correos transaccionales.

Estos proveedores pueden procesar datos en servidores ubicados fuera de Colombia. La persona usuaria acepta esta transferencia al otorgar su consentimiento. Se exige a los encargados un nivel de protección adecuado.

---

## 10. Menores de edad

MiSaldo está dirigido exclusivamente a **personas mayores de 18 años**. Si se detecta que una persona declaró una edad falsa, su cuenta y sus datos serán eliminados (ver RF-AUT-02 del SRS).

---

## 11. Cambios en esta política

Cualquier cambio sustancial se comunicará a las personas usuarias y, cuando corresponda, se solicitará un nuevo consentimiento. El historial de versiones se registra a continuación.

| Versión | Fecha | Cambio |
|---|---|---|
| 1.0 | 2026-10-26 | Versión inicial para desarrollo, pendiente de revisión legal. |

---

## 12. Contacto

Para ejercer sus derechos o resolver dudas sobre el tratamiento de sus datos, la persona usuaria puede escribir a **carlosestradavalle@outlook.com**. Este correo es provisional y se reemplazará por un correo corporativo antes del lanzamiento.
