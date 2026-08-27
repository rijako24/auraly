# Auraly Commerce MVP

> **Decisión vigente desde 2026-08-02:** Auraly Commerce eliminó el concepto de
> caja. El contexto canónico es usuario + sede (`BusinessId`) + bodega + sesión de
> trabajo; `DeviceId` solo identifica un equipo enrolado para operar offline. La
> decisión completa está en
> `decision-sesiones-trabajo-equipos-enrolados-sin-caja.md` y prevalece sobre toda
> mención histórica a caja, turno, arqueo o serie por caja en este índice.
> Decisión organizacional vigente: `Tenant` es la empresa, `Business` es la sede
> y no existe un nivel `Location` en Commerce. Ver
> `decision-tenant-company-business-branch.md`.

**Rama de trabajo:** `design/auraly-commerce-mvp`  
**Última actualización:** 27 de agosto de 2026

Este índice identifica la documentación vigente creada a partir del análisis de Auraly, Xion, Pedidos OK y Xion Web.

## Orden de prevalencia

La fecha o la palabra “consolidado” no otorgan prevalencia por sí solas. Cuando
dos documentos se contradigan, se aplica este orden:

1. `../AGENTS.md` y `estandares-de-ingenieria.md` para el contrato de trabajo y la Definition of Done.
2. `invariantes-arquitectonicas-auraly.md` para motores, writers, colas, catálogos y dropdowns.
3. `mapa-motores-flujos-y-extensiones.md` para localizar el propietario y punto de extensión.
4. `decision-cuatro-motores-operacion-contabilidad-fiscal-reporting.md` para la separación de efectos del ciclo documental.
5. La decisión propietaria vigente y explícita de cada módulo; entre ellas,
   `decision-sesiones-trabajo-equipos-enrolados-sin-caja.md`,
   `decision-definitiva-negativos-por-bodega-y-sin-inventario-local.md`,
   `decision-numeracion-operativa-y-fiscal-auraly.md` y
   `decision-maestro-parties-roles-sedes-y-cuentas-usuario.md`.
6. `diseno-ux-facturacion-pos-web.md` y
   `especificacion-facturacion-pos-auraly-mvp.md` para la experiencia POS; la
   invariante vigente es una línea nueva por adición, scroll a la última línea y
   foco persistente en el lector.
7. Diseños generales y auditorías históricas únicamente como contexto donde no
   contradigan las autoridades anteriores.

## Decisiones cerradas

- Auraly Commerce vive dentro de Auraly como monolito modular.
- La solución, proyectos y namespaces se renombran a Auraly.
- Cada contexto se separa en `Domain`, `Application`, `Infrastructure` y `Contracts`.
- Una API y una base SQL, inicialmente bajo `dbo`.
- El proyecto SQL/DACPAC es el único dueño de cambios de base; no se usan migraciones EF.
- El mismo producto se despliega en Azure o en un perfil On-Premise certificado.
- Facturación electrónica propia conectada con DIAN desde el MVP.
- Toda funcionalidad nueva extiende los motores canónicos existentes; una tarea
  funcional no crea otro motor, writer, job table ni cola paralela.
- El ciclo documental separa operación, contabilidad, fiscal y reporting en sus
  cuatro motores propietarios; el conversacional conserva su pipeline separado.
- Productos y documentos reciben IDs nuevos; los IDs heredados se conservan solo en mapas de migración.
- CxC y CxP entran desde el MVP.
- Entradas de mercancía pueden generar CxP.
- POS conserva captura continua por lector, balanza, grilla por teclado, recálculo, descuentos, eliminación de líneas, cancelación, temporales, pagos y búsquedas.
- Cada adición de producto crea una línea nueva aunque el producto ya exista; la
  grilla baja hasta la última línea sin sacar el foco DOM del lector.
- Pedidos tiene vista propia y acceso desde Facturación; se recupera uno a la vez y el botón Facturar procesa cada pedido seleccionado en una factura independiente.
- POS Edge mantiene catálogo, precios, códigos y configuración local, pero nunca inventario.
- La sincronización inicial del catálogo ocurre automáticamente al abrir Facturación por primera vez; luego se aplican deltas.
- La política de negativos pertenece a la bodega y todas sus sesiones/dispositivos la heredan.
- Si la bodega bloquea negativos, se valida en línea al capturar/cambiar cantidad y se revalida en la transacción final.
- Usuarios, perfiles, permisos por usuario, alcances, empleados, vendedores, transportadores, proveedores y datos semilla son módulos fundacionales.
- Una sola `Party` representa la identidad; clientes, proveedores, empleados, vendedores, transportadores, conductores y cuentas de usuario son relaciones separadas.
- Las sedes no duplican la identificación y un rol comercial no concede acceso al sistema.
- Menú, acciones y datos respetan permisos; la API siempre vuelve a autorizar.
- Ningún módulo se declara terminado sin trazabilidad, pruebas, conciliación y
  auditoría posterior del diff contra las reglas y buenas prácticas vigentes.

## Documentos

### Diseño general y alcance

- `diseno-auraly-commerce-mvp.md`
- `auditoria-funcional-consolidada-auraly-commerce-mvp.md`
- `alcance-definitivo-mvp-pos-devoluciones.md`

### Plataforma y persistencia

- `decision-monolito-modular-librerias-net.md`
- `arquitectura-modular-net-y-bootstrap-pos.md`
- `decision-persistencia-simple-monolito-modular.md`
- `decision-renombrado-auraly-database-pedidos-y-diseno-web.md`
- `decision-despliegue-onpremise-seguridad-maestros-semillas-calidad.md`
- `decision-maestro-parties-roles-sedes-y-cuentas-usuario.md`

### Facturación POS, sesiones e inventario

- `decision-numeracion-operativa-y-fiscal-auraly.md`
- `especificacion-facturacion-pos-auraly-mvp.md`
- `parametros-caja-auraly-commerce-mvp.md` (auditoría histórica; no normativa)
- `decision-definitiva-negativos-por-bodega-y-sin-inventario-local.md`
- `decision-inventario-negativo-por-bodega.md`
- `decision-validacion-inventario-al-capturar-linea-pos.md`
- `decision-canales-de-precios-mvp.md`

### Pedidos y motores

- `decision-pedidos-integrado-en-facturacion.md`
- `decision-motor-documentos-ids-y-flujo-pedidos.md`
- `decision-cuatro-motores-operacion-contabilidad-fiscal-reporting.md`
- `decision-motor-documental-ordenado-y-efectos-intrinsecos.md`

### Offline y sincronización

- `arquitectura-offline-catalogo-precios-pos.md`
- `arquitectura-definitiva-sync-pos-sin-inventario-local.md`
- `decision-sync-inicial-automatica-pos.md`
- `arquitectura-push-cambios-catalogo-cajas.md`
- `decision-canal-push-servidor-cajas.md`
- `flujo-conexion-webpubsub-pos.md`

### Sesiones de trabajo y corte sin caja

- `decision-sesiones-trabajo-equipos-enrolados-sin-caja.md`
- `implementation/work-session-cutover-design.md`
- `implementation/run-work-session-cutover.md`
- `implementation/work-session-cutover-evidence.md`
## Nota sobre documentos históricos

Algunos documentos registran decisiones intermedias que luego fueron corregidas.
No deben implementarse de forma aislada. En particular,
`auditoria-funcional-consolidada-auraly-commerce-mvp.md`,
`alcance-definitivo-mvp-pos-devoluciones.md`,
`parametros-caja-auraly-commerce-mvp.md` y los archivos `implementation/*evidence*`
son contexto o evidencia, no propietarios de reglas cuando existe una decisión
canónica posterior. Las expresiones históricas no autorizan revivir caja,
inventario local, agrupación automática de líneas ni efectos financieros dentro
del motor documental.
