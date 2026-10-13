# Flujos de autorización, reglas de negocio y tareas programadas

Cada empresa configura desde el portal **flujos de autorización** por pasos y **reglas de negocio** que bloquean
una operación o avisan cuando un documento cumple ciertas condiciones. El administrador de la plataforma puede
crear las mismas definiciones **para todas las empresas** y decide si son obligatorias u opcionales. Aparte, un
servicio en segundo plano revisa cada hora vencimientos y pendientes de todas las empresas.

| Pantalla | Ruta | Quién |
|---|---|---|
| Flujos de autorización | `configuracion/flujos` | `automation.read` (consulta) · `automation.manage` (configura) |
| Reglas de negocio | `configuracion/reglas` | `automation.read` (consulta) · `automation.manage` (configura) |
| Autorizaciones (bandeja) | `autorizaciones` | Personal de la empresa (no el portal del cliente) |
| Tareas programadas | `plataforma/tareas` | Administrador de la plataforma |

## Alcance: por empresa o para todas

| Alcance | Quién la crea | Efecto |
|---|---|---|
| **De la empresa** | Quien tenga `automation.manage` en la empresa (el administrador lo tiene). | Solo aplica en esa empresa. Puede usar roles de la empresa y valores de sus catálogos (clientes, locaciones, unidades, empleados…). |
| **Global obligatoria** | Administrador de la plataforma. | Aplica en todas las empresas; ellas la ven en solo lectura y no la pueden desactivar. |
| **Global opcional** | Administrador de la plataforma. | Aplica en todas las empresas, salvo en las que la desactiven con el interruptor **Activo aquí** de la lista (queda en la bitácora). |

El alcance se elige en el editor (solo el administrador de la plataforma ve la opción "Todas las empresas"). Una
definición global solo puede usar **roles base** (existen en todas las empresas: Oficina, Dirección, Facturación
y cobranza…), **permisos** o **administradores**, y no puede usar datos que dependan de los catálogos de una
empresa (cliente, locación, unidad, etc.).

## Documentos y momentos

Cada documento aparece solo si la empresa tiene contratado su módulo.

| Documento | Momentos de las reglas | Flujo de autorización |
|---|---|---|
| Boleta de servicio | Al guardar · Al cerrar o firmar · Al validar | Empieza al firmar o cerrar; la última aprobación **valida la boleta**. |
| Orden de trabajo | Al guardar · Al despachar | Se solicita al despachar; ya autorizada, se puede despachar. |
| Cotización | Al guardar · Al enviar al cliente | Se solicita al enviar (o al marcarla aceptada sin haberla enviado); ya autorizada, se puede enviar. |
| Factura | Al timbrar | Se solicita al timbrar; ya autorizada, se puede timbrar. No aplica a la Carta Porte de traslado. |
| Comprobación de viáticos | Al guardar · Al enviar a autorización · Al autorizar | Empieza al enviar; la última decisión **la aprueba o la rechaza**. |
| Orden de taller | Al guardar · Al cerrar | Se solicita al cerrar; ya autorizada, se puede cerrar y liberar la unidad. |
| Carga de combustible | Al guardar | — |
| Movimiento de almacén | Al guardar | — |

## Condiciones

Las condiciones comparan **datos del documento** que Brocal calcula al evaluarlas. El editor muestra la lista
completa de cada documento; por ejemplo:

- Boleta: total, conceptos con precio modificado, fotos de evidencia, firma del cliente, duración, personal con
  documentos vencidos, unidades con permisos vencidos, contrato vencido, cliente, tipo de servicio, locación.
- Orden de trabajo: prioridad, unidades y personal asignados, horas para el inicio, unidades en taller.
- Cotización: total, descuento global, mayor descuento de un concepto, días de vigencia, moneda.
- Factura: total, método de pago, días de crédito, COPADE, factura manual, saldo vencido del cliente.
- Viáticos: total comprobado, anticipo, gastos sin CFDI o sin foto, gasto más alto, empleado.
- Orden de taller: costos (refacciones, mano de obra, externo), tipo, tareas sin terminar, horas en taller.
- Combustible: litros, importe, precio por litro, desviación del rendimiento, forma de pago.
- Almacén: tipo de movimiento, cantidad, costo, existencia resultante, si queda debajo del mínimo.

| Tipo de dato | Comparaciones |
|---|---|
| Número o importe | igual, distinto, mayor, mayor o igual, menor, menor o igual |
| Lista (prioridad, moneda…) o catálogo (cliente, unidad…) | igual, distinto, es uno de, no es ninguno de |
| Sí / no | es verdadero, es falso |

- Se cumplen **todas** las condiciones o **cualquiera**; hasta 10 por definición.
- Un flujo sin condiciones aplica a todos los documentos de ese tipo; una regla necesita al menos una condición.
- Un dato que no existe (una boleta sin contrato, una carga sin rendimiento calculado) nunca cumple una
  comparación; sí cumple "distinto" y "no es ninguno de".

## Flujos de autorización

Un flujo tiene de 1 a 6 **pasos** que se autorizan en orden. Cada paso define:

| Campo | Detalle |
|---|---|
| Aprobadores | Roles base, roles de la empresa, quien tenga cierto permiso y/o los administradores de la empresa. Si nadie cumple, autorizan los administradores. |
| Aprobaciones requeridas | De 1 a 5 personas distintas. |
| Plazo (horas) | De 1 a 720. Al vencer se recuerda a los aprobadores; al doble del plazo, también a los administradores. |
| Quien solicita puede autorizar | Si no se marca, nadie autoriza su propia solicitud. |

**Qué flujo aplica.** Un documento sigue un solo flujo: el primero cuyas condiciones se cumplen, en este orden:
globales obligatorios, de la empresa y globales opcionales; dentro de cada grupo, por el campo **Orden** (menor
primero) y luego por nombre.

**Dos comportamientos**, según el documento:

- **La autorización es la aprobación** (boletas y viáticos). Mientras la solicitud está pendiente se ocultan los
  botones directos ("Validar" en la boleta, "Aprobar/Rechazar" en viáticos). La última aprobación valida la boleta
  o aprueba la comprobación; un rechazo rechaza la comprobación. Una boleta rechazada no se puede validar ni
  facturar hasta reabrirla y volver a firmarla, o solicitar de nuevo la autorización desde su panel.
- **La autorización desbloquea la acción** (órdenes de trabajo, cotizaciones, facturas y órdenes de taller). Al
  intentar la acción se crea la solicitud y la pantalla avisa "en espera de autorización"; ya autorizada, se repite
  la acción. La autorización vale **mientras el documento no cambie**: si se modifican sus datos se pide de nuevo.
  Tras un rechazo, el siguiente intento crea otra solicitud. Si el documento deja de cumplir las condiciones, la
  solicitud pendiente se cierra sola y la acción procede.

Al crear y timbrar una factura en un solo paso, si requiere autorización la factura queda como borrador (con su
solicitud) y se timbra después. Las boletas con una solicitud pendiente o rechazada no se pueden facturar (en
"Nueva factura" aparecen deshabilitadas con el motivo).

**Avisos.** Los aprobadores de cada paso reciben "Autorización solicitada" y quien solicitó, "Autorización
resuelta", por los canales configurados en Notificaciones. **Retirar** una solicitud lo puede hacer quien la pidió
o quien tenga `automation.manage`; cancelar el documento (o reabrir la boleta, borrar el borrador de factura,
rechazar la cotización) cierra sus solicitudes pendientes.

### Bandeja y panel del documento

- `autorizaciones`: **Por decidir**, **Mis solicitudes** y **Todas** (con `automation.read`), con filtros por
  documento y estatus, y botones para autorizar o rechazar con comentario. El menú, la barra superior y el inicio
  muestran cuántas esperan tu decisión.
- Las boletas, órdenes de trabajo, cotizaciones, facturas, viáticos y órdenes de taller muestran el **panel de
  autorización**: pasos, quién decidió y con qué comentario, **por qué aplica** (las condiciones con los valores
  del documento), historial de solicitudes y los botones de autorizar, rechazar, solicitar o retirar.

## Reglas de negocio

| Campo | Detalle |
|---|---|
| Documento y momentos | Uno o varios momentos del documento (tabla anterior). |
| Condiciones | Como en los flujos; al menos una. |
| Efecto | **Bloquear**: la operación no se realiza y se muestra el mensaje (los destinatarios reciben el aviso del intento). **Avisar**: la operación se realiza y se avisa. |
| Mensaje | Hasta 500 caracteres. Admite `{folio}` y los datos del documento entre llaves, por ejemplo `{total}`. |
| Importancia | Informativa, advertencia o crítica (un bloqueo avisa al menos como advertencia). |
| Destinatarios | Roles base, roles de la empresa, permisos, administradores, quien hizo la operación y correos adicionales (estos siempre por correo). |

Aplican **todas** las reglas que se cumplen (no solo la primera). Si alguna bloquea, la operación se rechaza con
los mensajes de todas las que bloquean y no se envían los avisos de las demás. El mismo documento con los mismos
datos en el mismo momento genera un solo aviso por regla.

Ejemplos:

- Bloquear el cierre de una boleta de más de $100,000 sin fotos de evidencia.
- Avisar a Dirección cuando una cotización lleva un concepto con más de 30 % de descuento.
- Bloquear el despacho de una orden con personal que tiene documentos vencidos.
- Avisar al almacén cuando un movimiento deja un artículo debajo del mínimo.

## Tareas programadas

Cada `Jobs:ScanIntervalMinutes` (60 por omisión) se revisan todas las empresas operativas (activas o en prueba
vigente), una por una; un error en una empresa no detiene a las demás.

| Revisión | Cuándo avisa | Evento |
|---|---|---|
| Permisos de unidades y documentos del personal | Solo el documento más reciente de cada tipo, de unidades y empleados activos. Anticipación: la del tipo de documento, si no la del evento en Notificaciones, si no la de la empresa. Avisa al entrar en el plazo, a 7 días y al vencer. El empleado vinculado a un usuario recibe los suyos. | Permiso de unidad por vencer · Documento del personal por vencer |
| Licencias federales | Anticipación del evento o de la empresa; al entrar en el plazo, a 7 días y al vencer. | Documento del personal por vencer |
| Documentos obligatorios faltantes | Resumen semanal de unidades y empleados sin los documentos marcados como obligatorios. | Documentos obligatorios faltantes |
| Contratos y tarifarios | 60 días antes (o los días del evento), a 7 días y al vencer. | Contratos y tarifarios por vencer |
| Acciones correctivas de HSE | 3 días antes; cada semana mientras sigan vencidas. | Acciones correctivas por vencer |
| Llaves de API | 15 días antes, a 7 días y al vencer. | Llaves de API por vencer |
| Mantenimiento preventivo | Servicios próximos o vencidos de los programas. | Mantenimiento por vencer |
| Facturas vencidas | Cada semana por factura con saldo. | Factura vencida |
| Boletas sin facturar | Resumen diario (días de alerta de la empresa). | Boletas sin facturar |
| Existencias bajas | Resumen diario de artículos bajo el mínimo. | Existencia bajo el mínimo |
| Cotizaciones vencidas | Pasan a "Vencida" y se cierran sus solicitudes de autorización. | — |
| Autorizaciones atrasadas | Recordatorios de los plazos de los pasos. | Autorización solicitada |

Cada aviso lleva una **llave de deduplicación**: aunque la revisión corra cada hora, el mismo aviso no se repite.
Los destinatarios y canales de cada evento se configuran en `configuracion/notificaciones`.

`plataforma/tareas` muestra si la ejecución automática está activa, la frecuencia, la última y la siguiente
ejecución, y la bitácora de las últimas ejecuciones (se guardan 500) con los hallazgos y errores por empresa.
**Ejecutar ahora** corre la revisión en el momento, incluso con `Jobs:Enabled=false`. Un candado de PostgreSQL
impide dos revisiones simultáneas (varias réplicas de la API, o "Ejecutar ahora" durante una revisión: responde
"Ya hay una revisión en curso").

## Permisos

| Permiso | Permite |
|---|---|
| `automation.read` | Consultar flujos y reglas, y la vista **Todas** de la bandeja. El rol predeterminado Dirección lo incluye. |
| `automation.manage` | Crear y cambiar flujos y reglas de la empresa, desactivar las globales opcionales y retirar solicitudes de otros. |
| (ninguno adicional) | Autorizar o rechazar: basta con ser aprobador del paso. |
| Permiso de la acción | Solicitar una autorización desde el panel (p. ej. `invoicing.issue` para una factura). |

El módulo **Flujos y reglas de negocio** es núcleo: está activo en todas las empresas. Las empresas creadas antes
de este módulo no reciben `automation.read` en su rol Dirección automáticamente; se agrega desde
`configuracion/roles`.

## API

| Llamada | Uso |
|---|---|
| `GET /api/automation/catalog` | Documentos, momentos, datos y comparaciones disponibles. |
| `GET·POST /api/automation/workflows` · `PUT·DELETE …/{id}` · `PUT …/{id}/opt-out` | Flujos (`global: true` solo para la plataforma); `opt-out` desactiva o reactiva una global opcional en la empresa. |
| `GET·POST /api/automation/rules` · `PUT·DELETE …/{id}` · `PUT …/{id}/opt-out` | Reglas, igual que los flujos. |
| `GET /api/automation/approvals?view=mine\|requested\|all&status=&document=` · `GET …/count` · `GET …/{id}` | Bandeja, contador y detalle. |
| `POST /api/automation/approvals/{id}/decision` · `POST …/{id}/cancel` | Autorizar o rechazar con comentario; retirar con motivo. |
| `GET /api/automation/documents/{documento}/{id}` · `POST …/request` | Panel del documento; solicitar la autorización sin hacer la acción. |
| `GET /api/platform/jobs` · `POST /api/platform/jobs/run` | Estado y bitácora de las tareas programadas; ejecutar ahora. |

Cuando un flujo retiene una acción, la API responde **`202 Accepted`** en lugar de ejecutar la acción:

```json
{ "approvalPending": true, "instanceId": "…", "message": "Se envió a autorización (Facturas mayores a $500,000, paso 1 de 2: Dirección)." }
```

Una regla que bloquea responde `422` con el código `Rules.Blocked` y el mensaje de la regla.
