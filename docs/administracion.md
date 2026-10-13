# Administración multiempresa

Brocal atiende a varias empresas de servicios en una sola instalación. Cada empresa tiene sus datos aislados,
sus módulos contratados, cuotas, marca, configuración operativa y fiscal, usuarios y roles.

## Quién administra qué

| Nivel | Quién | Qué configura |
|---|---|---|
| Plataforma | Dentro de ChronoLith: permiso `brocal.platform.manage` del Shell o rol de realm `chronolith-admin` / `brocal-platform-admin`. Independiente: rol de realm `brocal-platform-admin`. | Alta de empresas, módulos contratados, cuotas, plan, estatus (prueba, activa, suspendida, cancelada) y marca de cualquier empresa. **Flujos y reglas globales** (obligatorios u opcionales para todas las empresas) y **tareas programadas**. Puede entrar a cualquier empresa. |
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
