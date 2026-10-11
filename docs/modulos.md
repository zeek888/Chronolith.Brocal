# Módulos

Brocal agrupa su funcionalidad en 24 módulos más los catálogos base. Cada empresa activa los módulos que
contrató (Administración de la plataforma → Empresas); al activar uno se activan los que necesita. Los módulos
**núcleo** (catálogos base, usuarios y roles, bitácora) siempre están activos. Las rutas son relativas a la base
del módulo (`/brocal/...` dentro del Shell, `/...` en modo independiente).

Los permisos se asignan a roles dentro de cada empresa; un permiso solo surte efecto si su módulo está contratado.

## Núcleo operativo

| # | Módulo | Qué hace | Pantallas | Permisos | Requiere |
|---|---|---|---|---|---|
| 0 | **Catálogos base** (núcleo) | Clientes (operadoras) con datos fiscales y contactos, contratos con vigencia y monto máximo, locaciones y pozos. | `catalogos/clientes`, `catalogos/contratos`, `catalogos/locaciones` | `catalogs.read`, `catalogs.manage` | — |
| 1 | **Boletas de servicio** | Captura en campo **sin señal** por tipo de servicio (formulario configurable), firma del cliente en pantalla, foto-evidencias con descripción, folio consecutivo y **PDF con la marca de la empresa**. Validación de oficina, cancelación y reapertura con motivo, costeo con la asistencia del personal. | `boletas`, `boletas/nueva`, `boletas/pendientes`, `boletas/:id` | `tickets.read`, `tickets.readown`, `tickets.capture`, `tickets.sign`, `tickets.approve`, `tickets.cancel`, `tickets.reopen`, `tickets.priceoverride` | Tarifarios |
| 2 | **Órdenes de trabajo y programación** | Qué unidad y qué cuadrilla van a qué pozo y cuándo; tablero semanal por unidad con detección de traslapes; ciclo programada → despachada → en proceso → terminada. **La boleta nace de la orden.** | `ordenes`, `ordenes/programacion`, `ordenes/nueva`, `ordenes/:id` | `dispatch.read`, `dispatch.readown`, `dispatch.manage` | Boletas, Unidades, Cuadrillas |
| 3 | **Servicios y tarifarios** | Conceptos con clave SAT y unidad, tarifarios por cliente o contrato con vigencia y tipos de servicio (formulario de captura, firma requerida, fotos mínimas). Precio aplicable: contrato → cliente → general → precio base. | `tarifas/servicios`, `tarifas/listas`, `tarifas/tipos-servicio` | `pricing.read`, `pricing.manage` | — |
| 4 | **Tablero de facturación** | Ejecutado (boletas firmadas) contra facturado contra cobrado por mes y cliente; boletas pendientes con días de espera y alerta configurable. | `facturacion/tablero` | `billing.read` | Boletas |

## Activos y flota

| # | Módulo | Qué hace | Pantallas | Permisos | Requiere |
|---|---|---|---|---|---|
| 5 | **Unidades y equipos** | Fichas de autotanques, tractos, unidades de bombeo y equipos: estatus, odómetro y horómetro (con corrección auditada), datos para Carta Porte, documentos, historial y última posición GPS. | `flota/unidades`, `flota/unidades/:id` | `fleet.read`, `fleet.manage`, `fleet.meters` | — |
| 6 | **Mantenimiento preventivo y correctivo** | Programas por kilómetros, horas o días; servicios vencidos o por vencer; órdenes de taller con tareas, refacciones (salen del almacén al cerrar), mano de obra y taller externo; liberación de la unidad. | `mantenimiento/programas`, `mantenimiento/vencimientos`, `mantenimiento/ordenes` | `maintenance.read`, `maintenance.manage` | Unidades |
| 7 | **Permisos y certificaciones por unidad** | SCT, SEMARNAT/ASEA, pólizas y verificaciones con **semáforo de vencimiento** y matriz unidad × documento. | `permisos/unidades`, `permisos/matriz`, `permisos/tipos` | `permits.read`, `permits.manage` | Unidades |
| 8 | **Control de combustible** | Cargas por unidad, rendimiento real (km/l o l/h) contra el esperado y cargas fuera de tolerancia. | `combustible/cargas`, `combustible/rendimiento` | `fuel.read`, `fuel.capture`, `fuel.manage` | Unidades |
| 9 | **Inventario y almacén** | Artículos (refacciones, consumibles, herramienta, EPP), existencias con costo promedio y mínimos, entradas, salidas, traspasos y ajustes, **resguardos de herramienta** a cuadrillas o personas con devolución. | `inventario/existencias`, `inventario/movimientos`, `inventario/herramienta`, `inventario/articulos`, `inventario/almacenes` | `inventory.read`, `inventory.move`, `inventory.manage` | — |

## Cumplimiento y personal

| # | Módulo | Qué hace | Pantallas | Permisos | Requiere |
|---|---|---|---|---|---|
| 10 | **Expedientes de personal** | DC-3, H2S, exámenes médicos y cursos SSPA con vencimientos; requisitos por contrato o cliente; matriz de cumplimiento y **paquete documental** (ZIP con carátula) para entregar al cliente. | `personal/empleados`, `personal/documentos`, `personal/matriz`, `personal/paquetes`, `personal/requisitos` | `personnel.read`, `personnel.manage`, `personnel.packages` | — |
| 11 | **HSE** | Incidentes y casi accidentes con acciones correctivas, inspecciones con plantillas y puntos críticos, pláticas de seguridad con asistentes e indicadores (días sin accidente incapacitante, incidentes por tipo). | `hse/incidentes`, `hse/inspecciones`, `hse/platicas`, `hse/indicadores`, `hse/plantillas` | `hse.read`, `hse.report`, `hse.manage` | Expedientes |
| 12 | **Cuadrillas y asistencia** | Integrantes, jefe y unidades por cuadrilla; pase de lista diario precargado con las órdenes; pre-nómina (días, horas, extras) y costo de mano de obra por boleta. | `cuadrillas`, `cuadrillas/asistencia`, `cuadrillas/prenomina` | `crews.read`, `crews.manage`, `crews.attendance` | Expedientes |
| 13 | **Viáticos y gastos** | Comprobación con foto del ticket desde la app; flujo enviar → aprobar o rechazar → pagado. | `viaticos`, `viaticos/nuevo`, `viaticos/:id` | `expenses.capture`, `expenses.read`, `expenses.approve` | Expedientes |

## Fiscal y comercial

| # | Módulo | Qué hace | Pantallas | Permisos | Requiere |
|---|---|---|---|---|---|
| 14 | **Facturación CFDI 4.0** | **La boleta firmada se convierte en factura en un clic** (una por cliente); borradores, timbrado con el PAC, cancelación con motivo SAT 01–04, PDF y XML. | `facturacion/facturas`, `facturacion/nueva` | `invoicing.read`, `invoicing.issue`, `invoicing.cancel`, `invoicing.settings` | Boletas |
| 15 | **Carta Porte** | Viaje de autotanque ligado a unidad, remolque, operador y ruta, con mercancías (material peligroso); CFDI de traslado o ingreso con complemento Carta Porte 3.1. | `cartaporte`, `cartaporte/nuevo`, `cartaporte/:id` | `cartaporte.read`, `cartaporte.manage` | Facturación, Unidades, Expedientes |
| 16 | **Cobranza** | Antigüedad de saldos, cobros aplicados a facturas con **complemento de pagos 2.0**, estimaciones a Pemex y COPADE (enviada → aprobada → COPADE → facturada → pagada) y gestión de cobro con promesas y recordatorios. | `cobranza/antiguedad`, `cobranza/pagos`, `cobranza/estimaciones`, `cobranza/gestion` | `collections.read`, `collections.manage` | Facturación |
| 17 | **Cotizaciones** | Cotización con precios del tarifario del cliente y PDF; al aceptarse **se convierte en orden de trabajo**. | `cotizaciones`, `cotizaciones/nueva`, `cotizaciones/:id` | `quotes.read`, `quotes.manage` | Tarifarios |

## Dirección y clientes

| # | Módulo | Qué hace | Pantallas | Permisos | Requiere |
|---|---|---|---|---|---|
| 18 | **Dashboard gerencial** | Utilización de unidades, ingreso por unidad, días de boleta a factura, cartera vencida, servicios del mes, ingreso por tipo de servicio y cliente, boletas por día y estado de la flota. | `dashboard` | `dashboard.read` | Boletas |
| 19 | **Portal del cliente** | La operadora consulta **solo su información**: servicios del mes, órdenes programadas, saldo, boletas (con PDF) y facturas, con la marca de la empresa. La oficina puede previsualizar el portal de cualquier cliente. | `portal` | `portal.access` | Boletas |
| 20 | **Reportes mensuales** | Reporte por mes, cliente o campo en PDF o Excel con la marca de la empresa. | `reportes` | `reports.read` | Boletas |

## Plataforma

| # | Módulo | Qué hace | Pantallas | Permisos | Requiere |
|---|---|---|---|---|---|
| 21 | **Usuarios, roles y permisos** (núcleo) | Invitación por correo, vínculo con un empleado (para "mis órdenes" y "mis boletas") o con un cliente (portal); roles editables a partir de plantillas. **El operador ve solo sus órdenes, la oficina ve todo y el dueño ve el dashboard.** Configuración de la empresa. | `configuracion/usuarios`, `configuracion/roles`, `configuracion/empresa` | `access.users`, `access.roles`, `access.settings` | — |
| 22 | **Bitácora de auditoría** (núcleo) | Quién capturó, editó, firmó, canceló, timbró o exportó qué y cuándo, con los valores anteriores y nuevos. | `configuracion/bitacora` | `audit.read` | — |
| 23 | **Notificaciones** | Reglas por evento (15 eventos: boleta firmada, permiso por vencer, unidad liberada de taller, factura vencida…) con canales en la aplicación, **correo y WhatsApp** (Cloud API de Meta con plantilla aprobada); SMTP propio opcional; bandeja de envíos con reintento. | `configuracion/notificaciones` | `notifications.manage` | — |
| 24 | **Integraciones** | API keys con alcances (`gps.write`, `tickets.read`, `export.read`); **GPS de la flota**; consultas para otros sistemas; **exportación a Excel** de 17 conjuntos de datos por periodo; pólizas de ingresos y cobranza en Excel o CSV para **CONTPAQi Contabilidad / Aspel COI**. | `configuracion/integraciones` | `integrations.manage`, `integrations.export` | — |

## Roles predeterminados

Cada empresa nueva recibe estos roles (editables):

| Rol | Para quién |
|---|---|
| Administrador | Acceso total a la empresa. |
| Dirección | Dashboard, reportes y consulta de toda la operación. |
| Oficina | Catálogos, tarifas, programación, validación de boletas y cotizaciones. |
| Supervisor de campo | Captura y firma de boletas, asistencia de su cuadrilla y reportes HSE. |
| Operador de campo | Solo sus órdenes y boletas; captura en campo, combustible, gastos y reportes HSE. |
| Mantenimiento | Unidades, taller, permisos por unidad y refacciones. |
| Almacén | Artículos, existencias, movimientos y herramienta asignada. |
| HSE | Incidentes, inspecciones, pláticas y expedientes del personal. |
| Facturación y cobranza | Facturas CFDI, Carta Porte, cobranza, COPADE y autorización de gastos. |
| Cliente (portal) | Personal de la operadora: solo el portal de su cliente. |

## API para integraciones

Con una API key (encabezado `X-Api-Key: brk_…`, se muestra una sola vez al crearla y se guarda solo su huella
SHA-256):

| Llamada | Alcance |
|---|---|
| `POST /api/integrations/gps/positions` — posiciones de la flota (el odómetro actualiza la unidad) | `gps.write` |
| `GET /api/integrations/feeds/tickets?from=…&pageSize=…` — boletas para otros sistemas | `tickets.read` |
| `GET /api/integrations/feeds/exports/{dataset}` — cualquier conjunto exportable en Excel | `export.read` |

La documentación interactiva de la API está en `/scalar` en desarrollo (en otros ambientes, con
`OpenApi__Enabled=true`).
