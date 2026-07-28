# Auraly Commerce MVP: índice de diseño

**Rama de trabajo:** `design/auraly-commerce-mvp`  
**Última actualización:** 24 de julio de 2026

Este índice identifica la documentación vigente creada a partir del análisis de Auraly, Xion, Pedidos OK y Xion Web.

## Orden de prevalencia

Cuando dos documentos se contradigan, se aplica este orden:

1. `decision-numeracion-operativa-y-fiscal-auraly.md` para identidad, prefijos y consecutivos operativos Auraly frente a numeración fiscal DIAN.
1. `decision-definitiva-negativos-por-bodega-y-sin-inventario-local.md` para política de negativos, momento de validación y ausencia de inventario local.
2. `decision-despliegue-onpremise-seguridad-maestros-semillas-calidad.md` para Cloud/On-Premise, usuarios, permisos, terceros, semillas y calidad.
3. `decision-canales-de-precios-mvp.md` para canales de precios.
4. `decision-motor-documentos-ids-y-flujo-pedidos.md` para motor, IDs y procesamiento de pedidos.
5. `decision-pedidos-integrado-en-facturacion.md` para interacción entre Pedidos y Facturación.
6. `auditoria-funcional-consolidada-auraly-commerce-mvp.md` para alcance funcional y exclusiones.
7. `diseno-auraly-commerce-mvp.md` como arquitectura general.
8. Los demás documentos especializados para el detalle de su tema, siempre sujetos a las decisiones anteriores.

## Decisiones cerradas

- Auraly Commerce vive dentro de Auraly como monolito modular.
- La solución, proyectos y namespaces se renombran a Auraly.
- Cada contexto se separa en `Domain`, `Application`, `Infrastructure` y `Contracts`.
- Una API y una base SQL, inicialmente bajo `dbo`.
- El proyecto SQL/DACPAC es el único dueño de cambios de base; no se usan migraciones EF.
- El mismo producto se despliega en Azure o en un perfil On-Premise certificado.
- Facturación electrónica propia conectada con DIAN desde el MVP.
- El motor del servidor procesa todos los documentos definitivos.
- Productos y documentos reciben IDs nuevos; los IDs heredados se conservan solo en mapas de migración.
- CxC y CxP entran desde el MVP.
- Entradas de mercancía pueden generar CxP.
- POS conserva captura continua por lector, balanza, grilla por teclado, recálculo, descuentos, eliminación de líneas, cancelación, temporales, pagos y búsquedas.
- Pedidos tiene vista propia y acceso desde Facturación; se recupera uno a la vez y el botón Facturar procesa cada pedido seleccionado en una factura independiente.
- La caja mantiene catálogo, precios, códigos y configuración local, pero nunca inventario.
- La sincronización inicial del catálogo ocurre automáticamente al abrir Facturación por primera vez; luego se aplican deltas.
- La política de negativos pertenece a la bodega y todas sus cajas la heredan.
- Si la bodega bloquea negativos, se valida en línea al capturar/cambiar cantidad y se revalida en la transacción final.
- Usuarios, perfiles, permisos por usuario, alcances, empleados, vendedores, transportadores, proveedores y datos semilla son módulos fundacionales.
- Menú, acciones y datos respetan permisos; la API siempre vuelve a autorizar.
- Ningún módulo se declara terminado sin trazabilidad, pruebas y conciliación.

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

### Facturación, caja e inventario

- `decision-numeracion-operativa-y-fiscal-auraly.md`
- `especificacion-facturacion-pos-auraly-mvp.md`
- `parametros-caja-auraly-commerce-mvp.md`
- `decision-definitiva-negativos-por-bodega-y-sin-inventario-local.md`
- `decision-inventario-negativo-por-bodega.md`
- `decision-validacion-inventario-al-capturar-linea-pos.md`
- `decision-canales-de-precios-mvp.md`

### Pedidos y motor

- `decision-pedidos-integrado-en-facturacion.md`
- `decision-motor-documentos-ids-y-flujo-pedidos.md`

### Offline y sincronización

- `arquitectura-offline-catalogo-precios-pos.md`
- `arquitectura-definitiva-sync-pos-sin-inventario-local.md`
- `decision-sync-inicial-automatica-pos.md`
- `arquitectura-push-cambios-catalogo-cajas.md`
- `decision-canal-push-servidor-cajas.md`
- `flujo-conexion-webpubsub-pos.md`

## Nota sobre documentos históricos

Algunos documentos registran decisiones intermedias que luego fueron corregidas. No deben implementarse de forma aislada. Este índice y el orden de prevalencia evitan que expresiones antiguas como `CashRegisterSettings.AllowNegativeStockSales` o “existencia local conocida” se interpreten como decisiones vigentes.
