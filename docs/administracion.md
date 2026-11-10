# Administración multiempresa

Brocal atiende a varias empresas de servicios en una sola instalación. Cada empresa tiene sus datos aislados,
sus módulos contratados, cuotas, marca, configuración operativa y fiscal, usuarios y roles.

## Quién administra qué

| Nivel | Quién | Qué configura |
|---|---|---|
| Plataforma | Dentro de ChronoLith: permiso `brocal.platform.manage` del Shell o rol de realm `chronolith-admin` / `brocal-platform-admin`. Independiente: rol de realm `brocal-platform-admin`. | Alta de empresas, módulos contratados, cuotas, plan, estatus (prueba, activa, suspendida, cancelada) y marca de cualquier empresa. **Flujos y reglas globales** (obligatorios u opcionales para todas las empresas), **tareas programadas**, la **réplica de los catálogos de ChronoLith** y, en modo independiente, la **licencia de ChronoLith**. Puede entrar a cualquier empresa. |
| Empresa | Administrador de la empresa (o un rol con `access.*`). | Usuarios y roles, marca y estilo, operación, datos fiscales y PAC, notificaciones, integraciones y flujos y reglas de la empresa (`automation.manage`). |
| Usuario | Cada persona según sus roles. | — |

Dentro de ChronoLith, el Shell decide **quién puede abrir Brocal** (permiso `brocal.access`); todo lo demás se
decide por empresa con los roles de Brocal.

## Alta de una empresa

**Administración de la plataforma → Empresas → Nueva empresa** (`plataforma/empresas`):

1. Identificador (*slug*, p. ej. `servicios-del-golfo`), nombre, razón social, RFC y contacto.
2. **Módulos contratados**: al marcar uno se marcan los que necesita (p. ej. Carta Porte activa Facturación,
   Unidades y Expedientes). Catálogos base, usuarios y roles y bitácora siempre están activos.
3. **Cuotas** (vacío = sin límite): usuarios, usuarios del portal, unidades, personal, boletas por mes,
   almacenamiento en MB y API keys. Al llegar al límite, el alta correspondiente se rechaza con un mensaje claro;
   el uso actual se ve en **Mi empresa → Uso y módulos**.
4. Días de prueba (vacío = activa desde hoy) y **administrador de la empresa** (correo y nombre). La cuenta se
   vincula la primera vez que esa persona entra con ese correo (verificado); si la API tiene
   `Identity__ProvisionUsers=true`, Brocal puede crearla en Keycloak con una contraseña temporal.

Al crearse, la empresa recibe los roles predeterminados (ver [módulos](modulos.md#roles-predeterminados)) y la
configuración inicial (zona horaria de Centro, MXN, IVA 16 %, PAC simulado).

### Estatus

| Estatus | Efecto |
|---|---|
| En prueba | Opera normalmente hasta la fecha fin de la prueba; después, como suspendida. |
| Activa | Opera normalmente. |
| Suspendida / Cancelada | Sus usuarios no tienen acceso, las API keys dejan de funcionar y no se envían avisos programados. La plataforma puede seguir consultándola y reactivarla. |

Cada cambio de estatus pide un motivo y queda en la bitácora.

## Configuración de la empresa (Mi empresa)

### Operación

Zona horaria (México: Centro, Noreste, Sureste, Quintana Roo, Chihuahua, Pacífico, Noroeste), moneda, idioma
predeterminado, tasa de IVA, fotos mínimas por boleta, firma obligatoria del cliente, **validación de boletas
antes de facturar**, días para alertar boletas sin facturar, días de anticipación para vencimientos, tolerancia de
rendimiento de combustible y **prefijos de folio** por tipo de documento (boletas, órdenes, cotizaciones, taller,
incidentes, viajes, estimaciones, viáticos).

### Marca y estilo

- **Usar el estilo configurado en ChronoLith**: Brocal toma colores, tipografía y modo oscuro de ChronoLith
  (dentro del Shell, el tema del Shell; fuera, el tema predeterminado que publica la API de ChronoLith).
- **Paleta propia**: 8 colores (primario, secundario, acento, neutro, error, advertencia, éxito, información),
  esquinas, tipografía, densidad y modo de color, con **vista previa en vivo** antes de guardar.
- Nombre de la aplicación, lema, logotipo para fondo claro y oscuro, y pie de página de los documentos.

La marca se aplica a la aplicación, al portal del cliente y a los PDF (boletas, facturas, cotizaciones, reportes,
paquetes documentales).

### Datos fiscales y PAC

RFC, razón social (como en la constancia, sin régimen societario), régimen fiscal, código postal de expedición,
series de facturas y de complementos de pago, y el PAC. El PAC **"Simulado"** genera CFDI reales en estructura
pero sin validez fiscal (demostraciones y capacitación); para timbrar en producción se configura el PAC contratado
con su usuario y contraseña (se guarda cifrada) — ver [arquitectura](arquitectura.md#fiscal).

## Usuarios y roles

- **Invitar usuario** (`configuracion/usuarios`): correo, nombre, roles o administrador de la empresa. La persona
  entra con su cuenta de Keycloak y Brocal la vincula por su **correo verificado**. Si la API tiene
  `Identity__ProvisionUsers=true`, se puede crear la cuenta con una contraseña temporal.
- **Vincular con un empleado**: el operador ve "mis órdenes" y "mis boletas" (`dispatch.readown`,
  `tickets.readown`).
- **Vincular con un cliente**: usuario del portal; solo ve la información de ese cliente.
- **Roles** (`configuracion/roles`): se crean a partir de plantillas y se ajustan permiso por permiso; los
  permisos de módulos no contratados aparecen deshabilitados.
- Una misma persona puede pertenecer a varias empresas y cambia de empresa desde el selector de la barra superior.

## Notificaciones

`configuracion/notificaciones`: para cada evento se eligen canales (en la aplicación, correo, WhatsApp) y
destinatarios (roles, personal asignado, contactos del cliente marcados para avisos, correos y celulares
adicionales). Correo: servidor de la plataforma o SMTP propio. WhatsApp: Cloud API de Meta con una plantilla
aprobada (token y *phone number id*). La pestaña **Envíos** muestra la bandeja de salida con reintento manual; sin
servidor configurado los mensajes quedan como "simulados".

## Flujos de autorización y reglas de negocio

- **Flujos** (`configuracion/flujos`): qué documentos requieren autorización (condiciones sobre sus datos) y quién
  autoriza cada paso, con aprobaciones requeridas y plazo. Las solicitudes se deciden en `autorizaciones` o en el
  panel de cada documento.
- **Reglas** (`configuracion/reglas`): en qué momento de un documento se **bloquea** la operación o se **avisa**, a
  quién y con qué mensaje.
- **Alcance**: cada empresa crea las suyas. El administrador de la plataforma crea definiciones **globales** y
  decide si son **obligatorias** (las empresas solo las consultan) u **opcionales** (cada empresa puede
  desactivarlas desde la lista; el cambio queda en la bitácora).

Detalle completo en [flujos, reglas y tareas programadas](automatizacion.md).

## Tareas programadas (plataforma)

`plataforma/tareas`: cada hora se revisan todas las empresas operativas (permisos, documentos del personal,
licencias, contratos y tarifarios por vencer; documentos obligatorios faltantes; acciones correctivas, llaves de
API, mantenimiento, facturas vencidas, boletas sin facturar, existencias bajas, cotizaciones vencidas y
autorizaciones atrasadas). La pantalla muestra la bitácora de ejecuciones con los hallazgos por empresa y permite
**ejecutar ahora**. Los plazos de aviso salen del tipo de documento, de la regla de notificación del evento o de
los días de anticipación de la empresa.

## Catálogos de ChronoLith (plataforma)

Solo cuando Brocal está conectado a un Shell de ChronoLith (como módulo, o independiente con `Shell:ApiUrl` y la
cuenta de servicio del realm `chronolith`; ver [integración](integracion-chronolith.md#catálogos-compartidos-réplica-y-feed-de-cambios)).
La réplica es **de la instalación**: todas las empresas ven los mismos pozos, instalaciones, campos y compañías.

- **En los formularios.** Al capturar una locación de tipo pozo o instalación aparecen *Campo en ChronoLith* y *Pozo
  en ChronoLith* (o *Instalación en ChronoLith*); al elegirlos, el campo de la locación se llena solo y, al guardar,
  clave, nombre, coordenadas y campo se envían al catálogo maestro. Un cliente puede ligarse a una *Compañía en
  ChronoLith* (se envían clave, nombre y RFC). En el menú del renglón, **Publicar en ChronoLith** crea el registro en
  el maestro (o lo enlaza si su clave ya existe allí); un pozo nuevo necesita el campo de ChronoLith. Si quien guarda no
  tiene `shell.catalogs.manage` en el Shell, el cambio queda en cola y lo envía la cuenta de servicio.
- **Pantalla** `plataforma/catalogos-chronolith` (solo administradores de la plataforma): cursor del feed y último
  cambio en el Shell (su diferencia es el rezago), última consulta, último reporte y última carga completa, frecuencia,
  resultado de la última ejecución, elementos activos y totales por catálogo, el último error y los **envíos al
  maestro** pendientes, rechazados (el Shell no aceptó los datos: corrige el registro y *Reintentar*) o agotados (ocho
  intentos sin respuesta), con *Reintentar* y *Descartar*.
- **Consultar ahora** lee el feed de inmediato (y envía los pendientes); **Recargar completo** vuelve a descargar los
  siete catálogos del Shell y sigue el feed desde su último cambio (úsalo si la réplica se ve incompleta). Con
  `Jobs:Enabled=false` la pantalla avisa que la sincronización automática está apagada; estos botones siguen funcionando.
- **Conflictos.** Gana el lado que cambió más tarde: un cambio del Shell más reciente que la locación o el cliente se
  aplica (la clave solo si no choca en la empresa); un registro local más reciente se envía al maestro. Un pozo o
  instalación desactivado en el Shell desactiva las locaciones enlazadas.

## Licencia de ChronoLith

Solo en modo independiente; como módulo de ChronoLith la licencia la valida el Shell y esta sección no aplica.

La licencia es **de la instalación**, no de cada empresa: todas las empresas la comparten, junto con sus límites.
Sin una licencia válida la API no arranca (ver [producción](produccion.md#licencia) para configurarla).

- **Aviso** bajo la barra superior, para todos los usuarios: amarillo si vence en menos de 30 días; rojo durante
  el periodo de gracia y cuando ya está en solo lectura; azul con una licencia de desarrollo (no válida para
  producción). El administrador de la plataforma ve además el enlace *Ver licencia*.
- **Pantalla Licencia** (`plataforma/licencia`, solo administradores de la plataforma; no la ve el administrador
  de una empresa): cliente, estado, vigencia, fin de la gracia, origen, productos, uso frente a cada límite,
  **módulos incluidos**, **id de la instalación con botón Copiar** (para pedir la licencia) y un campo para **pegar
  una licencia nueva**, que se activa sin reiniciar si se emitió después de la actual. Si el token no sirve, la
  pantalla explica por qué (incompleto, de otra instalación, de desarrollo, sin Brocal, anterior a la activa…).
- **Solo lectura**: terminada la gracia se puede consultar y exportar, pero los cambios responden «Licencia
  vencida». Siguen permitidos iniciar sesión, marcar notificaciones como leídas, generar paquetes documentales
  (exportación) y cargar una licencia nueva.

### Módulos de la licencia

La licencia concede módulos con productos `brocal.<módulo>` (los códigos de [módulos](modulos.md), p. ej.
`brocal.tickets`, `brocal.invoicing`). Cada módulo concedido incluye los que necesita (`brocal.dispatch` concede
también boletas, tarifarios, unidades, cuadrillas y expedientes) y los módulos núcleo siempre están incluidos. Una
licencia **sin** productos `brocal.*` (solo `brocal` o `*`) incluye todos los módulos, como las licencias anteriores
a esta versión.

- Los módulos **efectivos** de una empresa son los que contrató y la licencia incluye: el menú, los permisos, las
  API keys, los avisos programados y las exportaciones solo ven esos.
- En **Empresas → Módulos** (y al crear una empresa) solo se pueden activar los módulos de la licencia; los demás
  aparecen marcados «Fuera de la licencia». Activar uno que no incluye responde 403 («La licencia de la instalación
  no incluye…»).
- Si una licencia nueva deja fuera un módulo que una empresa ya tenía, la selección de la empresa se conserva y el
  módulo queda apagado; vuelve solo cuando una licencia lo incluye otra vez. El administrador puede desmarcarlo.
- Un código `brocal.*` que Brocal no conoce se ignora y queda en la bitácora de la API como advertencia.

### Límites de la licencia

| Límite | Qué cuenta (en toda la instalación) | Dónde se exige |
|---|---|---|
| `users` | Personas distintas (por correo) con acceso habilitado (invitado o activo) como personal de cualquier empresa; quien pertenece a varias cuenta una vez. No cuenta a los usuarios del portal. | Invitar un usuario, reactivar uno desactivado, pasar un usuario del portal a personal y crear una empresa con un administrador nuevo. |
| `portalusers` | Personas distintas con acceso habilitado al portal del cliente. | Invitar o reactivar un usuario del portal y pasar un usuario de personal al portal. |
| `companies` | Empresas activas o en prueba. | Crear una empresa y reactivar (activar o poner en prueba) una suspendida o cancelada. |
| `units` | Unidades y equipos activos. | Dar de alta una unidad. |
| `employees` | Personal activo (expedientes). | Dar de alta un empleado. |
| `tickets` | Boletas creadas en el mes calendario (UTC). | Capturar una boleta. |
| `storagemb` | Megabytes de archivos guardados (fotos, firmas, PDF, documentos). | Subir o generar un archivo. |
| `apikeys` | API keys sin revocar. | Crear una API key. |

Un límite ausente en la licencia no limita. Desactivar un usuario (en todas sus empresas), dar de baja una unidad o un
empleado, revocar una API key o suspender o cancelar una empresa libera su lugar. Al alcanzar un límite la operación
responde 403 con un mensaje traducido. Estos límites son de la licencia y se suman a las **cuotas** que el
administrador de la plataforma fija a cada empresa: primero se revisa la cuota de la empresa y después el límite de la
instalación.

## Integraciones

`configuracion/integraciones`: API keys con alcances (se muestran una sola vez), instrucciones para el proveedor de
GPS y para otros sistemas, **exportación a Excel** por periodo de 17 conjuntos (boletas y sus conceptos, órdenes,
clientes, locaciones, servicios, unidades, combustible, órdenes de taller, existencias, personal, documentos con
vencimiento, asistencia, viáticos, incidentes, facturas y cobros; cada uno disponible si su módulo está contratado)
y cuentas contables para exportar **pólizas de ingresos y cobranza** para CONTPAQi Contabilidad o Aspel COI.

## Bitácora

`configuracion/bitacora`: filtro por usuario, acción, tipo de registro y fechas; cada cambio muestra los valores
anteriores y nuevos. Las exportaciones (Excel, pólizas, reportes mensuales y paquetes documentales) también quedan
registradas.
