# Auditoría final de preparación — Auraly Commerce

Fecha: 2026-08-12

## Alcance validado

Esta auditoría cubre la aplicación unificada de Auraly: los módulos preexistentes de agentes, canales, campañas, contactos, servicios y configuración permanecen en el mismo frontend `admin`; Commerce agrega productos, terceros, compras, inventario, rentabilidad, rutas, pedidos, cartera, devoluciones y punto de venta. No existe un segundo panel administrativo.

`Auraly.Api` es el único host HTTP administrativo y de Commerce. Los workers consumen contratos y servicios de aplicación; no constituyen una segunda API de negocio. El proyecto SQL Database continúa siendo el único dueño del esquema de SQL Server.

## Flujo vertical comprobado

La regresión de navegador se ejecutó contra el frontend real, `Auraly.Api`, SQL Server real y RabbitMQ. No sustituyó el servidor con EF InMemory ni validó únicamente endpoints.

Los 18 escenarios E2E cubren:

- creación de un tenant desde la vista administrativa;
- creación transaccional de la primera sede, bodegas `VEN` y `PED`, cliente consumidor final y roles Cajero, Supervisor, Administrativo y Administrador;
- emisión y aceptación de la invitación del primer administrador;
- creación y edición de productos desde la vista;
- creación y edición de cliente, proveedor, vendedor y transportador;
- carga real de combos, jerarquías y maestros;
- entrada de mercancía, búsqueda de productos y captura con teclado;
- conteo físico, ajuste, traslado, conversión y avería;
- procesamiento por el motor mediante RabbitMQ y persistencia de inventario, kárdex e historial;
- edición de margen/precio con teclado y publicación por lote;
- creación, programación y edición de rutas con vendedor y clientes;
- venta POS online con teclado, tirilla y preparación inmediata de una nueva venta.

## Evidencia ejecutada

| Puerta | Resultado |
| --- | --- |
| `dotnet build Auraly.Commerce.sln --configuration Release` | aprobado, 0 errores y 0 advertencias |
| `Auraly.Foundation.Tests` | 165/165 |
| `Auraly.Pos.Edge.Host.Tests` | 23/23 |
| `Auraly.ServerSlice.IntegrationTests` | 129/129 con SQL Server real y RabbitMQ |
| `Auraly.Platform.Tests` — regresión de módulos preexistentes | 727/727 |
| Build de `Auraly.Database.sqlproj` | aprobado, 0 errores y 0 advertencias |
| Publicación DACPAC en `AuralyCutoverValidation` | aprobada |
| Actualización DACPAC de `AuralyLocal` preservando datos | aprobada |
| TypeScript | aprobado |
| ESLint | aprobado |
| Pruebas web POS | 56/56 |
| Build Next.js 16 | aprobado, 58 rutas |
| Regresión Playwright desde navegador | 18/18 en 9 archivos |
| Verificación de codificación de textos | aprobada |

## Defectos detectados y corregidos por las pruebas

- La operación de avería ahora prepara existencia en su propio escenario y valida el procesamiento ordenado del documento, sin depender del estado acumulado de otras pruebas.
- El POS E2E resuelve tenant y sede desde el contexto autenticado, sin identificadores quemados.
- El buscador de inventario conserva listo el foco con texto vacío sin desplegar una capa de resultados que bloquee la confirmación.
- La composición de configuración Azure quedó encapsulada fuera de `Program.cs`, manteniendo la frontera de arquitectura canónica.
- Las pruebas históricas consumen el DACPAC canónico `Auraly.Database`.
- Se corrigieron textos con codificación dañada y la referencia inválida del logo en el build standalone.
- El workflow de `main` publica automáticamente solo DEV. PROD exige una ejecución manual explícita con `environment=prod`.

## Idempotencia, orden y conectividad

Cada documento registra su movimiento por procesar y publica un mensaje identificable. El consumidor procesa un documento por mensaje, conserva el orden por Business mediante sesiones/partición, confirma el mensaje solo después del commit y aplica reintentos y dead-letter ante fallos persistentes. Inventario, kárdex, pagos, contabilidad operativa y proyecciones se generan desde el mismo procesamiento idempotente; no existe un timer que sondee SQL para descubrir trabajo.

El POS Edge conserva SQLite, catálogo local, identidad offline, consecutivos, documentos y outbox. La outbox es almacenamiento durable local, no polling de catálogo. Los cambios requeridos por la caja se notifican mediante el transporte configurado y se descargan por versión; la caja no mantiene inventario completo.

## Seguridad y autorización sensible

El aprovisionamiento crea permisos mínimos por rol. Acciones sensibles del POS se validan en backend. Cuando el cajero no posee el permiso se crea una solicitud auditable para supervisor; la aprobación remota es el camino normal y la credencial secundaria local cifrada queda disponible para operación Edge sin conexión. La aprobación identifica usuario, acción, dispositivo y documento; no es un booleano anónimo.

No se versionaron certificados, claves privadas, contraseñas de producción ni cadenas de conexión. Los proveedores de prueba viven en proyectos de prueba.

## Límites que no se deben ocultar

- No se declara conectividad real ni habilitación DIAN aprobada: faltan certificado válido, SoftwareIdentificationCode, PIN, TestSetId y configuración oficial completa. La generación/validación determinística puede probarse localmente, pero no sustituye una ejecución real de habilitación.
- El instalador debe firmarse con un certificado de firma de código antes de distribución externa.
- La promoción a PROD no forma parte de esta entrega. Solo se habilita después de aprobar en DEV el mismo release y sus hashes.

## Criterio de aceptación

La rama es apta para PR y despliegue controlado a DEV cuando el árbol final vuelve a pasar todas las puertas anteriores, el PR no presenta hallazgos bloqueantes y el release reproducible conserva el commit y hashes revisados.
