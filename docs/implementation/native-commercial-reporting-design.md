# Reporting comercial nativo

Estado: implementación y ampliación de filtros múltiples, mesas, navegación jerárquica y rentabilidad verificadas localmente el 2026-11-04.
El despliegue remoto se acredita mediante el pipeline del release. Este documento complementa, sin
reemplazar, `decision-cuatro-motores-operacion-contabilidad-fiscal-reporting.md`.

## Regla de propiedad

Las pantallas de Resumen comercial, Ventas, Impacto comercial, Productos y
rentabilidad y Proveedores: compras y ventas
leen cifras, agrupaciones y detalle analítico exclusivamente de `reporting.*`.
El universo de Impacto también procede de esas proyecciones; los maestros
operativos solo suministran opciones de filtros, nunca completan cifras o filas
de reporte en tiempo de consulta. Operación, pedidos, rutas, inventario y
facturación no calculan indicadores para la interfaz. El motor de reporting es
el único propietario de agregados, costo, utilidad e impacto.

No se crea una tabla por informe. Las vistas reutilizan documentos, hechos de
línea, pagos y totales diarios existentes. Para documentos físicos, el motor
documental de operaciones crea el trabajo durable y, al completar, solicita la
proyección. Para facturas de servicios se aplica la decisión específica
`decision-facturacion-servicios-online.md`: el writer compartido de emisión
guarda la fuente inmutable en el mismo `SalesReportingJobs`, dentro de la
transacción de la factura; tras confirmar se activa el mismo coordinador.
Esta conciliación fue aprobada por el usuario. Los servicios no crean trabajo
operacional, inventario ni otro motor. Sus líneas se proyectan exclusivamente
en `ServiceSalesReportLineFacts`, con costo no modelado y sin margen ficticio.
Pedidos, rutas, visitas, despachos y contabilidad no crean rutas de reporting.

## Atribución histórica de una venta

El writer canónico proyecta las líneas, los medios de pago y los grupos de
impuestos de una factura por conjuntos: una escritura SQL por conjunto, sin
consultas por línea. Los cargos incluidos en factura forman parte del documento,
los impuestos y los totales por cliente, vendedor y bodega. No se atribuyen a un
producto, categoría o proveedor de mercancía; su detalle se consulta en Cargos de
facturación. Los cargos asumidos como gasto no incrementan las ventas. La regla
de inclusión llega congelada en el documento y reporting no la recalcula.
El total documental proyectado conserva el total oficial, redondeado al múltiplo
de COP 100 más cercano, y `RoundingAdjustmentAmount` identifica la diferencia
firmada. El subtotal exacto se reconstruye como `TotalAmount -
RoundingAdjustmentAmount`; líneas, bases e impuestos permanecen exactos y
reporting no reparte el ajuste entre productos ni bases tributarias.

Al confirmar cada línea de factura, `SalesDocumentLines` fija una vez:

- código y nombre del producto;
- categoría;
- proveedor atribuido, usando primero la relación principal activa;
- costo unitario reconocido por el kardex.

Código, nombre y valores comerciales provienen del snapshot de la línea vendida
(incluido el pedido recuperado); el catálogo vigente no los reinterpreta.
Categoría y proveedor sí se consultan al confirmar, exclusivamente para fijar
la atribución histórica de reporting. Una línea marcada como producto genérico
conserva ese indicador y no genera movimiento de inventario aunque el producto
sea reconfigurado después en el catálogo.

`AttributionSnapshotVersion=1` distingue un snapshot capturado, incluso cuando
no existe proveedor o categoría. Las filas heredadas conservan versión `0` y
pueden usar el catálogo vigente durante una reconstrucción. Las líneas nuevas usan atribución `2`, incluida su clasificación de composición.
Las proyecciones nuevas usan versión `5` y conservan los snapshots previos probados;
una línea de versión 1 no se reinterpreta desde el catálogo vigente.

Decisión confirmada por el usuario el 2026-11-04: la utilidad realizada por línea
es `UntaxedAmount - RecognizedCostAmount`; el margen es esa utilidad dividida por
`UntaxedAmount`, después de descuentos y devoluciones. El costo procede de la valoración realmente aplicada por el ledger (incluido el
costo de componentes cuando corresponda), nunca del precio de catálogo ni de una
estimación enviada por el dispositivo. El genérico conserva su costo manual
congelado en la línea, sin generar inventario. Un costo sin prueba queda no
disponible, no se convierte en cero. La definición anterior sobre `TotalAmount` queda sustituida:
incluir impuestos trasladados sobrestimaba la utilidad. Esta decisión coincide
con la sección 6 de la decisión de cuatro motores. Cambiar esta documentación
no corrige por sí solo el runtime ni las proyecciones históricas: véase el corte
de implementación y sus pruebas más abajo.

El lector canónico de fuentes de Reporting deserializa únicamente los campos
que consume la proyección, con la política JSON del serializador de ventas
existente. No exige la estimación `documentUnitCost` del dispositivo: hay fuentes
históricas aceptadas que la guardaron como `null`. Esa estimación no participa
en el costo reconocido ni se sustituye por un cero; el writer conserva la
valoración del kardex o el costo manual congelado del genérico. Este último solo
constituye prueba de costo cuando `UnitCostSnapshot` no es nulo. La recepción
operativa de ventas nuevas conserva su contrato estricto y sigue rechazando
un costo de documento explícitamente nulo.

Esta compatibilidad se aplica en el mismo processor y writer, sin modificar
fuentes inmutables, hashes ni inventario. El corte de datos reconstruye por el
comando canónico todas las sedes con fuentes de Reporting, incluidas las que
ya tenían versión 5, para corregir la cobertura de costo de sus genéricos
históricos; no requiere otro esquema ni un segundo proceso de proyección.
La regresión local reconstruye dos veces ventas con la estimación nula: conserva
el costo del kardex de la mercancía y deja no disponible el costo del genérico
sin snapshot manual. Comprueba importes documentales, payload y hash intactos,
y que el inventario mantenga respectivamente uno y cero movimientos. Pasaron
los siete casos de esta validación, incluida la conciliación del explorador;
el build de backend terminó sin advertencias ni errores. La auditoría conserva
los propietarios de JSON, proyección y costo, sin nuevas consultas, motores,
escrituras operativas, contratos HTTP ni cargas frontend.
Las pruebas de costo histórico usan una fixture propia, separada del escenario
que comprueba los totales de una única venta; no dependen del orden de ejecución.
Los siete casos volvieron a pasar con ese aislamiento.

El corte de datos de DEV del 2026-11-05 reconstruyó 633 fuentes en cinco sedes,
mediante el mismo comando canónico. Quedaron 349 documentos de venta y 225 de
compra proyectados, con una línea de servicio. En cada sede se verificaron los
encabezados contra sus documentos inmutables y las identidades de neto,
impuestos, redondeo y total oficial, además de la conciliación del resumen
diario y la cobertura de costo. La comprobación global no dejó proyecciones
anteriores a la versión 5 ni reconstrucciones abiertas.

## Atribución nativa del pedido

Los pedidos comerciales guardan como columnas tipadas `WarehouseId`,
`OrdersWarehouseId`, `ReservationTransferId`, `SellerId`, `RouteId`,
`RouteStopId`, `PartySiteId`, `CapturedByUserId`, `CapturedOffline` y
`RequiresStockReview`. Estas propiedades dejan de pertenecer a
`CustomAttributesJson`.

La creación autoriza exclusivamente mediante `orders.create`; no exige que la
cuenta esté asociada a un vendedor comercial. Si la cuenta sí tiene vendedor,
el pedido conserva esa atribución. Si el pedido proviene de una ruta, conserva
el vendedor propietario de la ruta y una cuenta de vendedor no puede usar una
ruta ajena. Un pedido sin ruta creado por una cuenta administrativa conserva
`CapturedByUserId` y deja `SellerId` vacío, sin atribuir ventas ficticias. El despliegue incluye
un backfill idempotente para pedidos anteriores; las lecturas mantienen un
fallback temporal al JSON únicamente para compatibilidad durante el cutover.

Cuando un pedido se factura, la proyección de la venta conserva el `SellerId`
comercial del pedido y su nombre histórico. Para una venta sin pedido, resuelve
el vendedor por el tercero de la cuenta que registró la venta. El usuario de
caja nunca reemplaza silenciosamente al vendedor que originó el pedido.

## Experiencia funcional cerrada el 2026-11-04

Este diseño sustituye las tres páginas equivalentes de Ventas, Clientes y
Vendedores. Xion Web es referencia de preguntas y dimensiones, no de código,
caché de sesión, fórmulas ni cargas completas para paginar en memoria.

| Entrada | Pregunta y contenido | Presentación |
| --- | --- | --- |
| Resumen comercial | Cómo va la sede activa en el día comercial: venta, devoluciones, costo, utilidad, cobro declarado y crédito, con fecha/corte visibles | Conserva las gráficas actuales; reemplaza el nombre Hoy en menú, navegación y encabezado; conserva `/dashboard` |
| Ventas | Qué se vendió, por quién, a quién y con qué costo/utilidad | Tabla agrupable con detalle, sin gráficas |
| Impacto comercial | Qué clientes compraron y cuáles no dentro del universo autorizado | Tabla de cobertura y acceso a clientes/comprobantes, sin gráficas |
| Productos y rentabilidad | Qué productos aportan ventas, unidades, costo, utilidad, margen y participación | Tabla por producto, agrupable por categoría, con acceso al detalle; sin gráficas |
| Proveedores: compras y ventas | Qué se compró y vendió del portafolio atribuido a cada proveedor | Tabla de compras/devoluciones de compra, ventas/devoluciones de venta, utilidad, clientes alcanzados y comparación; sin gráficas |

Vendedores y Clientes dejan de tener entradas de menú duplicadas. Sus URLs
existentes dirigen al informe Ventas con la agrupación correspondiente,
conservando filtros compatibles; no mantienen componentes consultantes ocultos.
La URL de impacto de proveedores conserva compatibilidad con su nueva etiqueta.
La entrada Productos y rentabilidad pertenece a Reportes y no sustituye la
operación de publicación de precios del catálogo.

### Revisión de diferenciación de vistas

Se conserva la tabla de medidas compartidas: no se crean consultas ni fórmulas
alternas por pantalla. Impacto ya se diferencia por su universo histórico,
compradores, clientes sin compra, cobertura y filtro de actividad. Proveedores
se diferencia por recepciones, devoluciones de compra, compra neta y comparación
con ventas, con los alcances de filtros aprobados descritos abajo.

La decisión adicional del usuario para Productos y rentabilidad es implementar
el análisis de aporte: abre ordenado por utilidad e incorpora participación en
venta neta y utilidad, intervalo de margen, productos con/sin devoluciones y
composición histórica (simple, ingredientes, combo o sin clasificación).

Estas opciones pertenecen al mismo `SalesReportExplorerRequest.Products` y al
lector autorizado existente, no a otra proyección, tabla, motor ni endpoint.
Los catálogos visibles se cargan en el mismo contexto persistido del reporte.
El formulario conserva cambios pendientes y aplica filtros comunes y especiales
en una sola carga. Ventas no incorpora estos filtros especializados.

Primero se agrega por producto el período completo bajo los filtros comunes,
búsqueda, autorizaciones y composición. El margen usa la misma fórmula canónica. El rango acepta hasta dos decimales y
se evalúa sin redondear mediante productos cruzados, para no alterar un límite
por la precisión de la división SQL. La presentación se redondea; un rango activo
excluye margen no calculable.
«Con devoluciones» identifica movimientos de devolución en ese período, incluso
si la venta original es anterior. Luego se filtra el conjunto de productos y se
aplica la navegación por categoría, producto, comprobante o línea. Esa navegación
no vuelve a decidir elegibilidad por el resultado aislado de un comprobante.
La comparación temporal conserva los productos elegibles del período actual.

La participación divide la venta neta o utilidad de la fila entre la suma del
conjunto completo de productos elegibles, sin depender de la página o nivel
abierto. Solo se calcula con denominador positivo y conocido; utilidades
negativas y devoluciones netas pueden dar aportes negativos. Si falta costo,
la utilidad y su participación quedan no calculables. No se reparte ese faltante
entre productos ni se convierte en cero. Las columnas y el CSV usan el mismo
resultado del servidor, con la base visible en pantalla. Categorías históricas
incompletas y composición desconocida no se reconstruyen desde el maestro actual.

El camino realiza una resolución de acceso y un solo comando de datos, con
agregados separados, límite SQL antes de enriquecer los detalles y sin I/O por
producto. El benchmark incluye primera y segunda página de rentabilidad con
margen y devoluciones sobre 1.005.000 hechos, con presupuesto de 1.500 ms p95.

### Contexto, navegación y filtros

**Ampliación confirmada el 2026-11-04:** todas las vistas, incluido Resumen
comercial, permiten rango de fechas y un panel expandible común con selección
múltiple de Área, Línea, Grupo, Subgrupo, clientes, proveedores, vendedores,
usuarios facturadores, productos y bodegas. Meseros y mesas aparecen únicamente
en restaurante. Dentro de un selector se aplica OR y entre selectores AND;
vacío significa todos los autorizados. Se admiten hasta 100 selecciones en total.
La captura queda pendiente hasta Aplicar filtros, que provoca una sola carga
del reporte visible. Expandir o cerrar no consulta el reporte ni monta selectores
ocultos. Los selectores consultan por páginas al abrir o buscar, nunca por ID
seleccionado. La misma selección restringe filas, agregados, exportación y detalle
antes de paginar. Las identidades autorizadas del actor siguen siendo un límite
adicional que ninguna selección amplía.

Resumen comercial inicia en el día del negocio, pero admite otro rango y todos
esos filtros. Sus medios de pago reutilizan el catálogo ya cargado por el contexto.
Se conservan las gráficas exclusivamente en este resumen.

La jerarquía de categorías se congela en la fuente de venta/recepción mediante
`catalog.ProductCategoryHierarchySnapshot` y llega a `CategoryHierarchyJson`
de los hechos existentes. La devolución hereda el snapshot original. El lector
no consulta el árbol actual ni reclasifica el pasado. Un documento anterior sin
ese snapshot conserva NULL: no coincide con filtros jerárquicos y la interfaz
explica esa limitación. La categoría final anterior sigue disponible para consulta
histórica. No se rellena la jerarquía por inferencia durante un rebuild.

Mesa usa `SalesDocuments.TableId` y el nombre congelado `TableNameSnapshot`,
proyectados en el encabezado de Reporting; los movimientos de devolución usan
el encabezado original. Mesa y mesero se cruzan por las líneas seleccionadas,
sin atribuir al mesero toda la factura de una mesa compartida. Sin mesa histórica
se informa falta de atribución; no se consulta la mesa actual para dar un nombre.

En Proveedores: compras y ventas, el usuario confirmó que cliente, vendedor,
usuario, mesero, mesa y tipo de venta acotan solamente ventas. Compras conserva
fechas, categoría, proveedor, producto y bodega; la pantalla declara esos dos
alcances y el significado de la relación resultante. Las opciones históricas de
producto/categoría/bodega de esa vista incluyen también mercancía solo comprada,
mediante la consulta paginada del mismo `SqlSalesReportingStore`.

La navegación confirmada es Área → Línea → Grupo → Subgrupo → Productos →
Comprobantes → Líneas. Proveedor, cliente, vendedor, usuario, mesero y bodega
abren primero sus productos. Cada ancestro permanece como restricción; no basta
filtrar solo por el último nivel, porque una clasificación puede variar en la
historia. La ruta y la página de regreso quedan en URL, con un máximo de ocho
ancestros. Toda la fila abre el siguiente nivel, responde a teclado y se resalta
al pasar el cursor. Botones internos conservan su acción sin doble navegación.
Se recomienda conservar este recorrido en la misma pantalla; el modal existente
queda para el soporte financiero puntual, no para apilar niveles analíticos.

La versión de Reporting pasa a 5 para reconstruir los encabezados anteriores
antes de habilitar las nuevas dimensiones. Se mantiene el mismo rebuild explícito;
no se crea backfill en startup, otro writer ni otra cola.

El alcance inicial continúa siendo tenant + sede (`BusinessId`) activos. No se
introduce un consolidado entre sedes. Bodega es un filtro distinto de sede y no
se etiqueta como si fueran equivalentes. Fechas y día comercial usan la zona de
la sede, nunca la zona del navegador. Rango inicial de tablas: mes actual hasta
el día comercial actual; Resumen comercial conserva su día actual.

Un selector visible **Agrupar por**, inicializado en **Comprobante**, cambia
realmente las filas de la tabla. Opciones: Comprobante, Usuario que facturó,
Vendedor comercial, Cliente, Proveedor, Producto, Categoría, Bodega y Mesero
(esta última solo en sede restaurante). Mes y día sirven como agrupaciones
temporales del mismo informe. Son opciones persistidas del catálogo canónico,
con códigos validados en servidor; no un array nuevo en frontend.

Filtros combinables: desde/hasta, tipo de documento de venta, cliente, usuario
que facturó, vendedor, proveedor, producto, categoría y bodega; Mesero solo en
restaurante. Terceros usan `Parties/role-options` paginado y el rol correcto.
Productos, categorías y bodegas usan las dimensiones históricas paginadas del
mismo lector canónico de Reporting; no cargan rankings truncados ni los maestros
completos. Así permiten seleccionar lo que existe en el período proyectado. Permisos y existencia dentro del tenant se validan antes
de consultar. El cliente nunca puede ampliar su alcance por filtro o URL.

Rango, agrupación, filtros, búsqueda, orden y página quedan en URL. Abrir una
fila conserva ese contexto; Volver restaura la misma página. Cambiar filtros
restablece página 1. Cambiar de tenant/sede descarta IDs y agrupaciones
incompatibles y cancela solicitudes previas, sin mostrar filas del contexto
anterior. La carga inicial espera a resolver contexto y aplicabilidad.

### Grano y cifras del informe Ventas

Cada agrupación muestra comprobantes distintos, unidades vendidas/devueltas,
venta bruta sin impuestos, descuentos, devoluciones sin impuestos, venta neta
sin impuestos, impuestos netos, total neto con impuestos, costo neto reconocido,
utilidad y margen. La fila Comprobante añade fecha, tipo, número operativo,
número fiscal cuando exista, cliente, usuario y vendedor. El costo queda como
columna explícita, no inferido por el lector desde la utilidad.

Las sumas monetarias son aditivas; margen se calcula sobre las sumas, nunca como
promedio de porcentajes. Comprobantes y clientes se cuentan distintos sobre el
conjunto filtrado: sus conteos por proveedor/mesero no se suman para obtener el
total general. Base cero produce margen no calculable, no 0 % inventado.
Una base negativa por devolución conserva su signo y se identifica como tal.
Las unidades no se interpretan como volumen comparable entre unidades de medida
distintas; el detalle muestra la unidad de cada producto.

El criterio temporal único de las tablas es **movimientos ocurridos en el
período**: venta en su fecha y devolución en la suya, referida al comprobante
original incluso si fue emitido fuera del rango. Al agrupar por comprobante se
incluyen los originales con actividad dentro del período. Su fila representa
solo esa actividad y así se etiqueta; no trae devoluciones futuras ni sustituye
la venta por un saldo histórico acumulado. El detalle contextual conserva rango
y filtros; una acción separada abre el historial completo del documento fuente,
identificándolo como tal. Se usan las mismas reglas al comparar períodos.

Al filtrar por proveedor/producto/categoría/mesero, se suman **solo las líneas
coincidentes**, aunque la factura contenga otras líneas. El detalle contextual y
el total del grupo usan exactamente ese subconjunto. Pagos, crédito, redondeo y
propina del documento completo no se reparten entre proveedores o meseros.
El total del comprobante completo solo aparece en su soporte completo autorizado.
Para identidad proveedor no se revelan encabezados financieros de otras líneas.

Los cargos facturados se presentan como concepto separado sin producto,
proveedor ni mesero atribuido; por ello el total por estas dimensiones se
concilia con una fila explícita Sin atribución. Los servicios facturados se
incluyen con su identidad y costo no modelado; no se inventa un
producto físico ni proveedor. Ningún concepto se excluye silenciosamente.
Si falta costo histórico se identifica como pendiente/no disponible y no se
presenta utilidad definitiva; un costo cero explícito sigue siendo válido.

Base + impuestos = subtotal exacto; redondeo documental se muestra por separado
para explicar el total oficial. Propinas permanecen fuera de venta, impuestos,
costo y utilidad. Cobro declarado al facturar no se etiqueta como recaudo de
cartera del período: los cobros posteriores pertenecen al módulo financiero.
Impuestos es la etiqueta general (incluye IVA/INC); el detalle muestra código,
tarifa y base y no llama IVA a todos los tributos.

La tabla es el contenido principal. Sus filas abren sucesivamente el grupo,
sus comprobantes y las líneas que explican las cifras. Productos y categoría
pueden ser un nivel intermedio. No se descargan detalles para filas cerradas.
Orden y filtros por encabezado se aplican en servidor al conjunto completo.
La columna de identificación tiene ancho estable y permanece fija al desplazar
horizontalmente las cifras. Cliente, usuario facturador y vendedor se presentan
en líneas compactas; la tabla no comprime esa columna para intentar encajar todos
los indicadores. Se conservan las cifras completas y el desplazamiento queda
acotado a la tabla, también en móvil; no cambia la consulta ni descarga más filas.

### Mesero y contexto restaurante

Organization conserva la única autoridad de aplicabilidad:
`Businesses.PointOfSaleMode = RestaurantBar`. No se deduce del nombre del
negocio, permisos de usuario o existencia de una sección en el menú. El modo
se obtiene del contexto canónico; no se consulta desde cada filtro.

La atribución de **Mesero** es la persona que incorporó la línea facturada,
usando `RestaurantCheckoutLines.AdditionId` →
`RestaurantAccountAdditions.AddedByUserId/AddedByName` y su
`AddedByWaiterPartyIdSnapshot`, capturado al agregar la línea. Es diferente del usuario
que facturó y del vendedor comercial. Se congela su identidad Party/rol Mesero
al agregarla; al facturar se copia ese nombre e identidad por conjunto en
Sales, incluso si entretanto se desactivó el rol o se cambió el nombre del usuario. La
proyección consume esa atribución; no consulta roles actuales para reinterpretar
ventas antiguas. Si el autor no es mesero o el histórico no permite probarlo,
se agrupa como Sin mesero atribuido, sin adjudicárselo al cajero.

Un relevo de mesa no transfiere ventas anteriores. Una factura puede contener
líneas de varios meseros: cada línea pertenece a uno solo y sus devoluciones
heredan esa misma atribución. No se usa el responsable actual de la mesa ni el
destinatario del reparto de propinas. Pérdidas y cancelaciones antes de facturar
no son ventas; conservan sus informes operativos.

En Venta directa no se ofrece agrupación/filtro Mesero ni controles de propina.
La API rechaza un parámetro de agrupación Mesero no aplicable. El cambio de modo
no elimina documentos históricos: el soporte autorizado sigue accesible por
identidad y Trazabilidad conserva la consulta histórica explícita descrita abajo.
No se habilitan permisos financieros al rol Mesero por esta remodelación.

### Ingredientes y combos: decisión funcional del 2026-11-04

El usuario eligió dos listas distintas y mutuamente excluyentes por producto:
Ingredientes admite únicamente `IsIngredient=1`; Combo admite únicamente
productos terminados, nunca ingredientes. Un producto no conserva ambas listas
activas. La regla y sus validaciones pertenecen a Catalog y al diseño
[gastronómico](restaurant-pos-design.md), no al reporte. Se reemplaza la propuesta
anterior de una lista indiferenciada. Separar captura no crea otro motor, writer
o descuento de inventario: ambas modalidades reutilizan el consumo canónico.
Para filtros actuales del catálogo se incorporarán `Products.HasIngredients` e
`Products.IsCombo`, derivados automáticamente de la composición vigente en su
misma transacción y mutuamente excluyentes. `IsIngredient` sigue indicando insumo.
Los reportes conservan esa clasificación histórica en sus proyecciones; no unen
Products ni reinterpretan ventas anteriores cuando cambian esos indicadores.

El usuario confirmó un costo comercial compuesto a partir de cantidad ×
precio costo con IVA de los componentes, bloqueado para edición manual mientras
exista composición y actualizado cuando cambien sus costos, impuestos o cantidades.
Combo usa impuesto de venta 0 % obligatorio; Ingredientes permite el impuesto
seleccionado del catálogo. Se conserva el margen y el nuevo precio se prepara
para revisión, sin publicación automática. Su fuente y
publicación pertenecen a [Pricing](../decision-prevalente-precios-costos-iva-publicacion.md),
con las respuestas del usuario incorporadas allí. No equivale a modificar costo
promedio, kardex ni costo reconocido de ventas anteriores.

El reporte reconoce una sola venta del producto/combo cobrado. Su costo real es
el reconocido de la única salida aplicada: stock propio o componentes. El detalle
proyectado de composición explica cantidades y costos sin sumarlos otra vez como
ventas/costo. No consulta composición ni precio vigente al abrir una fila. El
proveedor comercial del vendido y los proveedores de los componentes son
atribuciones distintas; no se reparte arbitrariamente el precio del combo.
Devoluciones conservan composición/modo/costos originales y la política canónica
de reintegro. La implementación reutiliza esas mismas versiones de receta y salidas
canónicas. El detalle de retorno explica la composición proporcional original,
no supone que todos los componentes hayan regresado al inventario.

### Impacto comercial y portafolio de proveedores

Impacto comercial muestra clientes únicos compradores, universo de clientes,
clientes sin compra y cobertura = compradores del universo / universo. Desde
un grupo se consultan ambos listados y sus comprobantes. Cliente no identificado
se informa aparte y no aumenta artificialmente clientes únicos. Las
devoluciones solas no generan un cliente comprador: comprador requiere una venta
en el período; devolución posterior no borra el hecho de haber comprado.
Cantidad de comprobantes no se denomina visitas físicas.

La exigencia confirmada es consultar solo proyecciones. Para este corte, el
universo que permiten los hechos existentes se define como
**clientes conocidos por ventas proyectadas hasta el fin del período**, dentro
del tenant/sede y alcance autorizado. Se obtiene de hechos de venta positivos
de Reporting, anteriores o interiores al rango, sin consultar el maestro vivo
de Parties. Cliente sin compra significa un cliente de ese universo sin venta
en el rango. La pantalla identifica esa definición y la primera fecha histórica
disponible: no equivale a todos los clientes registrados ni incluye prospectos
que nunca compraron. No se promete cartera asignada ni efectividad de visitas.

Los filtros de producto/categoría/proveedor definen los compradores alcanzados
por ese portafolio; el denominador sigue siendo el universo de clientes conocidos
del mismo alcance. Un filtro de cliente acota ambos conjuntos. Las restricciones
de identidad vendedor/proveedor se aplican a ambos antes de contar y evitan
revelar clientes fuera de alcance. Todo se agrupa/pagina en servidor desde las
proyecciones y se conservan snapshots de nombres de cliente, no nombres actuales.
Los conteos generales son distintos por CustomerId, nunca sumas entre grupos.
La comparación histórica compara ventas y compradores con fechas explícitas;
no aparenta cobertura sobre un universo de clientes registrados inexistente.
Una futura cobertura de clientes registrados/asignados exigiría su fuente
proyectada canónica y una decisión separada, no un join transaccional oculto ni
una nueva tabla preventiva en este corte.

Proveedor en Ventas responde cuánto se vendió de sus líneas; Proveedores:
compras y ventas compara ese sell-out con recepciones menos devoluciones de
compra del período, sin confundir compra del período con costo vendido. La
atribución histórica del proveedor no demuestra trazabilidad física de lote.
Venta/compras se etiqueta Relación venta/compra, nunca rotación de inventario.
El total de clientes entre proveedores es distinto, no suma de sus conteos.
Sin proveedor atribuido permanece visible en Ventas y productos.

La comparación de tablas es opcional con rango explícito; ofrece el período
inmediatamente anterior de igual duración o el mismo período del año anterior.
Se muestran las fechas efectivas y se valida la regla de fin de mes/año bisiesto.
Sin base anterior se muestra Sin base, nunca crecimiento infinito ni cero falso.
No se compara automáticamente todo informe contra un período oculto.

### Trazabilidad financiera: operación y tipo de comprobante

Son dos filtros independientes y combinables. Tipo de operación mantiene
`accounting-document-type`; **Tipo de comprobante** usa
`accounting-voucher-type` (Contable, Documento soporte, Anulación de soporte,
Egreso y los códigos que publique ese catálogo). No confundir estos códigos con
Factura/Comprobante de venta ni con el número del asiento.

Accounting conserva la consulta `documents`, sus drafts, fuentes y processor.
Se extiende su contrato con `voucherTypeCode` opcional y se filtra por el dato
persistido antes de contar, ordenar y paginar; no por descripción o nombre del
comprobante ni interpretando JSON por cada fila de la página. Las fuentes
nativas sin ese dato conservan null; seleccionar un tipo no las clasifica por
inferencia. La tabla y el detalle muestran el tipo cuando esté disponible.

Las opciones gastronómicas (Propina, Anulación de propina y Entrega de propinas)
se ofrecen por defecto solo en sede RestaurantBar. La aplicabilidad pertenece a
metadata relacional tipada del catálogo `reference.Options`, no a listas de
códigos excluidos en el navegador. Se extiende `ReferenceOptionService`/store/API
con contexto autorizado para resolver esas opciones en un único recorrido.
Sus claves de caché incluyen tenant, sede y contexto; `useReferenceOptions`
actualmente solo usa catalogCode y debe corregirse para esta consulta.

Un documento histórico no desaparece al cambiar de modo: el listado Todas las
operaciones conserva los documentos autorizados; las opciones históricas
gastronómicas aparecen únicamente cuando el rango filtrado contiene esos tipos,
identificadas como históricas. Las opciones combinan aplicabilidad actual y
existencia histórica por conjunto, sin GET por tipo. En una sede Venta directa
sin historia gastronómica no aparece ninguna opción gastronómica. Abrir un
documento histórico conserva su etiqueta aunque la opción ya no sea aplicable
a operaciones nuevas. El catálogo filtra presentación; no sustituye permisos.

### Propietarios, lectura y presupuesto

Se extienden `SalesReportingApi`, `SalesReportingService`, `ISalesReportingStore`,
`SqlSalesReportingStore`, contratos y writer existentes. No se crea otro motor,
writer, tabla de jobs, cola ni tabla por informe. Las extensiones físicas de reporting conservan los granos existentes y agregan
los atributos históricos faltantes, además del grano distinto de servicios
`ServiceSalesReportLineFacts` aprobado expresamente; los índices requieren
evidencia del camino medido. Financial traceability sigue siendo
lectura de Accounting: no se copia al motor de ventas.
El tipo de comprobante nuevo se valida contra su catálogo persistido. Tipo de
operación y estado conservan su contrato de búsqueda: un valor sin coincidencias
devuelve una página vacía. Los diagnósticos SQL reproducen también el parámetro
de comprobante, sin ejecutar una consulta distinta de la medida.

Todas las consultas de las cinco vistas comerciales, incluidos conteos,
encabezados, filtros cruzados, niveles intermedios y detalle mínimo de líneas,
impuestos/pagos, leen únicamente proyecciones. `SqlSalesReportingStore` debe
retirar los joins de enriquecimiento a maestros transaccionales (por ejemplo
nombre del proveedor); usa los snapshots de los hechos. Resolver permisos y
contexto no habilita enriquecer filas desde tablas operativas. La API devuelve
los atributos de aplicabilidad del contexto separado del resultado analítico.
Los catálogos de selección siguen sus lectores propietarios paginados; no
aportan importes ni completan nombres históricos del informe.

Solo **Abrir documento original** abandona el reporte y usa el lector autorizado
del módulo propietario para XML, soporte íntegro o trazabilidad operacional.
Los clics de grupo → comprobantes → líneas continúan dentro de Reporting.
Un atributo analítico ausente se proyecta en el writer canónico desde su fuente
inmutable y con versión/migración; no se completa consultando operación al abrir
una fila. Esta separación debe comprobarse con interceptación de SQL del lector.

Cada tabla mantiene un propietario de carga. Primera página: un recurso de
resultado devuelve filas + total de registros + totales del conjunto + filtros
aplicados + corte/zona/moneda. Una acción de filtro produce una solicitud de ese
recurso; abrir detalle produce una solicitud independiente. Opciones de catálogos
y contexto se reutilizan y no se recargan por página. No se consultan tendencias
ni rankings de gráficas en informes sin gráficas.

Dentro de cada consulta el número de comandos SQL permanece acotado: como máximo
uno para resolver acceso/contexto y uno para resultado con agregados y página,
con aislamiento consistente entre sus resultsets. No hay lecturas por elemento.
La cardinalidad de autorización debe validarse en vez de ocultarse con TOP(1).
Grupos se calculan y paginan en SQL con desempate estable por ID; ninguna opción
usa TOP 100 como sustituto de paginación. Página inicial 25, límite 200. Conteos y
sumas se calculan sobre el mismo conjunto completo antes de enriquecer la página.

Presupuestos heredados: p95 <300 ms resumen diario, <600 ms agrupación simple,
<1.5 s filtros cruzados, medidos en servidor con datos calientes. Dataset de
aceptación: 100.000 comprobantes/1.000.000 líneas de una sede en 24 meses,
incluyendo devoluciones, proveedores y múltiples autores; primera página y
páginas siguientes, sin builds ni diagnósticos concurrentes. Registrar también
latencia de red/UI y número de HTTP/SQL. Un incumplimiento exige optimizar el
lector canónico antes de proponer nuevas proyecciones.

Actualización por acción explícita o invalidación push existente, sin polling.
No se crean mecanismos de recuperación, exportación programada o suscripción.
Las exportaciones existentes conservan alcance/orden y ReportViewer; no se
certifica exportación completa si requiere un fetch-all en el navegador. Su
compatibilidad debe verificarse en el slice que toque esa acción.

### Secuencia de implementación y corte

1. Cambiar Hoy a Resumen comercial preservando rutas, día y gráficas.
2. Completar atribuciones históricas faltantes de usuario/mesero y su migración
   aditiva, reutilizando confirmación Sales y writer canónico por conjuntos.
3. Corregir cálculo de utilidad/margen en hechos, consolidados, lectores y pruebas.
   Versionar proyección y reconstruir con la operación explícita canónica, por
   tenant/sede y desde fuentes inmutables. No mezclar versiones ni presentar
   cifras corregidas hasta completar y conciliar ese alcance. No se corrigen
   facturas, kardex ni asientos; no se reconstruye desde startup.
4. Implementar tabla Ventas agrupada, filtros/contratos paginados, detalles y
   navegación compatible; retirar consumidores duplicados y gráficas de esas
   vistas. Completar servicios/cargos y la conciliación sin atribución.
5. Completar Productos, Impacto comercial y Proveedores con sus preguntas propias.
6. Extender el filtro de tipo de comprobante en Accounting y resolver
   aplicabilidad de opciones gastronómicas mediante catálogo contextual.

El orden permite verificar cada slice sin publicar pantallas incompletas como
terminadas. El rollback usa contrato/esquema aditivo compatible y conserva las
fuentes; una versión anterior que calcula utilidad con impuestos no puede volver
a presentarse como correcta. Un corte fallido conserva el alcance sin certificar
y requiere roll-forward/rebuild explícito; no cambia estados de operación.
Solo se publica DEV desde origin/main integrado, y producción requiere orden
expresa independiente.

### Aceptación y revisión crítica obligatorias

| Escenario | Evidencia exigida |
| --- | --- |
| Base 10.000 + impuestos 1.900; costo 6.000 | Total 11.900, utilidad 4.000, margen 40 % en resumen, grupo, documento y detalle |
| Descuento, múltiples tarifas/INC, redondeo, cargo y propina | Bases/impuestos/costo concilian; propina y redondeo separados; sin utilidad sobre impuesto |
| Venta fuera de rango con devolución dentro, devolución parcial/total | Figura su actividad en el rango, sin sumar venta externa ni devolución futura; revierte costo original |
| Factura con dos proveedores, dos meseros y mismo cliente | Importes por subconjunto correctos, cliente/comprobante únicos en total; detalle y exportación sin fuga |
| Usuario de caja distinto del vendedor y autores de la mesa | Tres atribuciones independientes; relevo no reasigna historia |
| Mesero desactivado/cambio de rol; histórico sin snapshot | Conserva identidad histórica probada o Sin atribución; no usa catálogo vivo para inventarla |
| Venta directa, restaurante, tenant con sedes mixtas, cambio de contexto | Mesero y opciones gastronómicas solo donde aplican; histórico contable accesible y etiquetado; ninguna fila del tenant/sede anterior |
| Tipo de operación + tipo de comprobante + tercero + estado + fechas | Mismos filtros en filas/conteos/sumas; código inválido rechazado, null sin clasificar por inferencia |
| Vendedor/proveedor con alcance restringido | URL, detalle y exportación no amplían acceso; identidad Mesero no obtiene permisos financieros |
| Grupo vacío, error, cero base, datos atrasados o costo pendiente | Estado inequívoco con reintento explícito; no se muestra error como resultado cero ni margen inventado |
| Dos páginas, empates de orden, más de 100 grupos | Sin truncamiento de ranking; orden estable, filtros servidor y mismos totales |
| Apertura, filtro, página, detalle y regreso | Una solicitud por recurso/acción, sin vistas ocultas, polling, N+1 ni refetch para recomponer estado |
| Replay/rebuild, cambio de catálogo y versiones | Sin duplicados ni efectos de venta/inventario/contabilidad; conciliación por fuentes y alcance |
| Navegar grupo → comprobante → líneas, impuestos/pagos | Ninguna lectura de datos operativos para filas o cifras; solo reporting.*, aparte de autorización/contexto y catálogos explícitos |
| Impacto con cliente antiguo, comprador actual y cliente nunca vendido | El primero puede figurar sin compra, el segundo comprador y el tercero no integra el universo; definición y límite histórico visibles |

La auditoría del diseño identificó y resolvió la contradicción documental de
utilidad con la decisión expresa del usuario. Detectó además brechas reales del
runtime: autor de facturación/mesero ausentes de los contratos analíticos,
conteos de impacto sumados, importes documentales completos bajo filtros de
línea, fechas distintas entre movimiento/documento, rankings truncados y caché
de catálogo sin contexto. Son trabajo de implementación, no checks aprobados.
El cierre funcional no equivale a ejecución de regresiones, benchmark ni DEV.

El código local implementa las cinco vistas, los filtros contextuales y la
versión 5 del único writer/processor. Ventas incluye bruto, descuentos, venta
antes de devolución, devolución y neto, impuestos, costo histórico, utilidad y
margen; compras permanece exclusivamente en Proveedores: compras y ventas.
`SalesReportActivity` reúne hechos físicos, servicios y cargos residuales de los
grupos tributarios proyectados; no escribe otra fuente ni lee operación.
Para agrupaciones de documentos completos, la misma función reutiliza el grano
de `SalesReportDocuments`, con cantidad física, hora local y número de líneas
sin costo persistidos por el único writer. Los filtros de partida y el detalle
usan los hechos de línea. Ambas modalidades concilian en la prueba dorada;
no constituyen dos cálculos comerciales ni otro agregado por pantalla.
`SalesReportDocumentMovements` explica subtotal exacto, redondeo y total oficial
por fecha de movimiento. No distribuye dinero del documento entre líneas.

Los lectores se ejecutan en dos comandos como máximo: autorización y un lote
SQL para página, agregados y detalle. El resumen agrupa sus resultsets en ese
mismo límite. El bloqueo de aplicación por sede protege únicamente frente a
reconstrucciones explícitas; es compatible con las escrituras normales del
motor de Reporting. Las consultas reutilizan su actividad filtrada ya leída
para filas y agregados; los movimientos posteriores aparecen al consultar de
nuevo. No bloquea el motor operacional ni rangos de otros tenants.
El explorador materializa una vez los importes oficiales y el redondeo de los
movimientos seleccionados; la página y el total reutilizan ese conjunto numérico.
La carga descriptiva de la página compila las ramas aplicables y sigue después
del límite. No vuelve a consultar los mismos movimientos para cada resultado.
Las APIs anteriores de documentos/proveedor reutilizan ese lector. Sus listas
y detalles exponen paginación, con límite 200; el proveedor sin atribución tiene
identidad nullable. Los importes no atribuibles y márgenes no calculables son
null, no ceros aparentes. CSV exporta explícitamente la página visible, su rango,
filtros y corte, con las mismas cifras y protección de fórmulas.

Una sede con proyecciones anteriores a versión 5 o reconstrucción incompleta
responde indisponible, en vez de mezclar cifras. La operación explícita es:
`Auraly.Console rebuild-sales-reporting <TenantId> <BusinessId>`, usando su
conexión configurada explícitamente. Toma el bloqueo exclusivo de Reporting de
esa sede, acepta servicios históricos faltantes desde su snapshot inmutable,
fija las claves de fuentes y las recorre en páginas de 200. Cada documento es
una transacción independiente del mismo processor; ventas/recepciones preceden
a devoluciones. No ejecuta handlers de operación, ledger, contabilidad ni fiscal.
Si falla, conserva el checkpoint no disponible hasta completar un rebuild
explícito. No hay rebuild desde startup, cola nueva ni otro motor.

### Conciliación obligatoria de totales

La aceptación cruza las cinco vistas con idénticos filtros y un mismo alcance.
Se verifica bruto menos descuentos = ventas, ventas menos devoluciones = neto,
neto más impuestos = subtotal exacto y subtotal más redondeo = total oficial.
Utilidad = neto sin impuestos menos costo; el margen se recalcula sobre los
importes agregados, nunca sumando ni promediando márgenes de filas.

La regresión dorada contiene un descuento no nulo, varias tarifas, una devolución
de venta anterior, un servicio de costo no modelado y un cargo sin producto.
Conciliación: 15 agrupaciones, sus filas, páginas de comprobantes, detalle por
línea paginado, impuestos, redondeo, costo y utilidad. Se cruza Resumen con Ventas
y se concilian Productos y Proveedores bajo el mismo portafolio. La diferencia
completa de servicios y cargos se prueba explícitamente; las compras conservan
su subtotal propio. Las identidades distintas se cuentan en el conjunto y no
se suman entre grupos. Resumen identifica los comprobantes emitidos en el período;
Ventas lista los originales con movimientos, incluidos originales antiguos con
devoluciones actuales. Los gráficos comerciales del resumen usan venta neta
sin impuestos; pagos conserva importes declarados netos de reembolsos.

### Lectura y carga del resumen

El POST de resumen acepta `includeBreakdowns`, opcional y desactivado para los
clientes anteriores. Cuando se solicita, la misma respuesta incluye hora, medios
de pago, vendedores, productos, proveedores y bodegas. Conserva los límites de
presentación de esos paneles y comparte el corte transaccional con las tarjetas.
La pantalla tiene un único propietario de carga para esa respuesta: no solicita
seis desgloses adicionales. El contexto de sede/catálogos conserva su carga
independiente. Pagos permanece vacío bajo un filtro de líneas; no se distribuye
dinero documental entre productos, categorías, proveedores o meseros.

El lector reutiliza la actividad filtrada para página y agregados. Rentabilidad
forma su portafolio sobre el período completo una vez y aplica después el
recorrido al detalle, sin cambiar la base de participación. Impacto reutiliza
ese conjunto para compradores y totales; sin filtros de líneas usa el grano
documental existente. Proveedores conserva compras y ventas separadas, reutiliza
sus hechos de venta filtrados y prepara el período anterior sólo cuando se pide.
Los bloques cerrados de comparación, pagos e impuestos mantienen sus resultsets
compatibles sin preparar las uniones que no se van a consultar.

Los parámetros opcionales conservan tipos SQL estables. El lector especializa
las banderas booleanas y la presencia o ausencia de filtros que determinan la
forma de la consulta; fechas, identidades y valores seleccionados siguen
parametrizados. Esto permite podar
ramas inactivas y reutilizar planes, sin forzar recompilación en cada lectura ni
guardar resultados que puedan quedar desactualizados. Se conserva la protección
de reconstrucción por sede, la validación de versión y el máximo de dos comandos SQL por
consulta. Las consultas normales no esperan a la
cola de procesamiento: leen lo confirmado y los movimientos posteriores aparecen
al volver a consultar. Se usa `READ COMMITTED` con la configuración existente de
la base; no se habilita otro nivel de aislamiento, caché de resultados, tabla ni
proyección paralela.
Las tarjetas y gráficas del resumen se derivan de la misma actividad filtrada.
Impacto y proveedores materializan sólo sus campos financieros y claves de
agrupación. Los identificadores de línea, impuestos desglosados y nombres de
comprobante quedan en el explorador que realmente los necesita para el detalle.
En el recurso de aplicación existente, los lectores usan `IntentShared`,
compatible con otros lectores y con `Shared` del processor. Sólo se conserva la
exclusión ante `Exclusive` de una reconstrucción explícita, para no mostrar un
conjunto que está siendo borrado y repuesto. La regresión comprueba el recurso
canónico y lecturas completas mientras otro lector o escritor conserva su lock.
El frontend propaga cancelación al cambiar filtros y conserva el plazo
de timeout, incluso durante la lectura del cuerpo de la respuesta.

El índice existente `IX_SalesReportLineFacts_Business_Date` incluye también
`ProductName`, `SupplierId`, `SupplierName` y `BusinessLocalHour`, los atributos congelados que usan
los rankings y agrupaciones habituales. El plan del ensayo de volumen recorría
1.005.000 hechos para devolver 42.586 del período al no tener esa cobertura.
La cobertura evita exigir búsquedas de la fila completa por cada hecho del
período y permite elegir el acceso por sede y fecha. No incorpora otra tabla,
proyección ni índice paralelo; las categorías y otros detalles mantienen su
alcance filtrado en la consulta canónica.

Los importes visibles de las cinco vistas comparten formato COP de hasta dos
decimales. Ese formato no redondea datos persistidos, cálculos ni exportaciones.
La regresión modifica el costo en `0,0001` y exige la misma utilidad exacta
en resumen, ventas, productos, impacto comercial y proveedores.
Si el benchmark incumple un presupuesto, obtiene sus planes reales después
de terminar todas las mediciones; el diagnóstico no corre en paralelo con
las muestras ni cambia sus límites.

### Evidencia local del 2026-11-04

- Backend y dacpac Release: cero errores y cero advertencias. Build de producción
  web: 94 rutas. ESLint: cero errores y ocho advertencias preexistentes; los
  archivos modificados al cierre también pasaron lint. `git diff --check` limpio.
- Integración SQL ejecutada por slices: 25 escenarios nativos aprobados y la
  regresión dorada de conciliación aprobada tras corregir el alcance del cohort
  de productos al entrar a comprobantes. También aprobaron el escenario de
  creador de pedidos, los dos escenarios aislados de compras y el benchmark.
  Se cubren servicios/rebuild, genéricos, devolución parcial con redondeo,
  composición/precios, inventario gastronómico y trazabilidad financiera.
- La regresión dorada cruza las cinco vistas, 15 agrupaciones y detalle paginado.
  Sus importes esperados son bruto 380, descuento 10, venta 370, devolución 100,
  neto sin impuestos 270, impuestos 32,3, subtotal exacto 302,3, redondeo 38,2 y
  total oficial 340,5. El portafolio físico conserva neto 200, costo 120 y utilidad
  80; servicio 50 y cargo 20 explican el puente de alcance. El costo del servicio
  no modelado sigue desconocido, sin utilidad ni margen inventados. Las compras
  concilian separadamente recepción 400.000 menos devolución 10.000 = 390.000.
- 117 pruebas de arquitectura, catálogos, sincronización SQLite y contratos;
  254 pruebas frontend/POS y 13 pruebas de repositorio de productos aprobadas.
- Benchmark reproducible: `SalesReportPerformanceTests`, sin builds ni carga
  diagnóstica concurrente durante las lecturas. SQL Server local, 100.000 ventas,
  5.000 devoluciones, 1.005.000 líneas, 24 meses y ventana de 31 días; tres
  calentamientos y quince muestras por caso. p95: resumen 11,4 ms (límite 300);
  comprobantes primera/segunda página 483,8/493,3 ms (600); proveedor y bodega
  400,2/395,7 ms; selección múltiple 369,8/706,2 ms; rentabilidad con devoluciones
  620,2/546,7 ms (estos últimos con límite 1.500). Dos comandos SQL por llamada,
  totales estables entre páginas y ausencia de filas repetidas. Son mediciones
  de esta máquina, no un SLA de nube. La preparación optimiza solo la base
  desechable del test: dimensiones precalculadas, lotes de 50.000 líneas y log
  simple; mantiene cardinalidad, distribución y presupuestos del lector.
- Compras: veinte partidas, repetidas o distintas, pasaron en 332,6/397,1 ms;
  conservan los mismos 55 comandos del motor que una partida y replay de tres
  comandos sin duplicar efectos. Pedidos: primera/segunda página 32,3/37,1 ms
  sobre 2.000 pedidos, un comando de lista más la autorización compartida.
- Navegador con API y SQL locales reales: cinco vistas, selección múltiple en
  borrador y un único Apply, área → línea → grupo → subgrupo → producto →
  comprobante → líneas, clic en cifras, teclado y regreso; mesa más mesero;
  margen, devoluciones, composición y participación; filtro de creador de pedido
  y fecha original; selector deslizante Ingredientes/Combo en creación y edición.
  Sin errores JavaScript ni respuestas fallidas de reportes. El regreso reutiliza
  el estado en caché cuando corresponde, sin forzar otra consulta.
- Capturas completas inspeccionadas en escritorio y móvil de 390 px: columna
  identificadora fija (320 px en escritorio, aproximadamente 235 px en móvil),
  filas legibles de unos 120 px, cifras desplazables sin desbordar la página y
  columnas alineadas en los cuatro módulos. Gráficas solo en Resumen comercial.
  Los comprobantes ficticios tienen números FV-DEMO/FS-DEMO, no etiquetas
  ambiguas «actual», «servicio» o «histórica» como identificación.

La auditoría posterior revisó el diff de esquema, migración, contratos, fuentes,
writers, lectores, UI, pruebas y documentación contra AGENTS, estándares e
invariantes. Reporting conserva el processor, job, writer y coordinador existentes;
servicios solo amplía sus fuentes. El inventario conserva su motor y ledger.
Las restricciones de identidad se aplican en servidor antes de agregar/paginar;
hash y claves de fuente protegen replay. Las escrituras de composición/precio
son transaccionales y versionadas; los lectores fijan un corte por sede. No hay
lecturas por línea, enriquecimiento previo al límite de página, fetch-all al
cliente, polling ni un propietario de carga oculto para el mismo recurso.
El botón financiero del original es una navegación explícita y autorizada,
separada del detalle analítico proyectado.

La migración de clasificación es idempotente y conserva versiones. Los contratos
anteriores delegan el lector nativo con límites explícitos. El corte requiere
reconstruir las proyecciones anteriores a versión 5 antes de habilitar lectura;
no se hace desde startup. Un rollback de la aplicación no debe volver a mezclar
versiones: mantener el reporte indisponible y reconstruir con el writer elegido.
Esta evidencia corresponde al entorno local. La aplicación local usa datos ficticios y
una base aislada; sus comprobantes de demostración no son soportes operativos.
No se implementó un adaptador de devolución de servicios inexistente ni expansión
de componentes fuera del canal gastronómico aprobado.

## Granos físicos necesarios

`GoodsReceipt` y `PurchaseReturn` reutilizan la tubería documental y la única
cola de reporting. Sus fuentes inmutables se proyectan en
`PurchaseReportDocuments` y `PurchaseReportLineFacts`; las devoluciones se
guardan con signo negativo. Se conservan proveedor, bodega, producto, moneda,
cantidades y valores históricos. Los agregados se calculan desde estos hechos;
no se crea una tabla por pantalla.

La proyección de líneas de recepción y devolución de compra se persiste por
conjunto en `SqlSalesReportingProjectionWriter.ProjectPurchaseAsync`: un comando
por documento, sin escrituras por partida. Conserva las identidades de origen,
los signos y escalas existentes y valida que se escriba el lote completo dentro
de la misma transacción de Reporting. La versión 4 conserva moneda/importes originales y agrega los importes
funcionales COP y tasa congelados por la fuente, sin recalcular conversión
en el reporte. Recepciones, devoluciones de compra y neto se distinguen; tampoco crea otro writer o trabajo. El gate del
recorrido de compra compara una y veinte partidas, además de verificar que los
hechos proyectados coincidan con las líneas operativas y no se dupliquen en replay.

La devolución de venta también proyecta sus partidas por conjunto mediante
`InsertReturnLineFactsAsync` del mismo writer. Conserva los importes con signo,
el redondeo individual de costo y las atribuciones originales; la suma
del costo procede de ese mismo lote. Un lote incompleto falla dentro de la
transacción de Reporting. La versión 4 incorpora la fecha del movimiento, disponibilidad de costo y
composición proporcional, sin cambiar los snapshots comerciales originales. La evidencia del incremento y sus límites están
en [devoluciones de venta](sales-returns-slice-design.md).

## Aislamiento por identidad

El alcance se resuelve en servidor mediante `AppUsers.PartyId` y la relación
canónica del negocio con `CommerceSellers` o `Suppliers`:

- un vendedor solo puede leer sus ventas, clientes, rutas, visitas y pedidos;
- un proveedor solo puede leer las líneas de sus productos y su propio impacto;
- filtros, URL, detalle y futuras exportaciones no pueden ampliar ese alcance;
- una identidad asociada simultáneamente a vendedor y proveedor, inexistente o
  ambigua falla cerrada;
- una cuenta sin esas asociaciones conserva el alcance administrativo que le
  otorgue `sales.reports.read`.

Las restricciones se aplican antes de ejecutar cada consulta semántica. No son
filtros cosméticos del navegador.

## Fuera de alcance

Metas, comisiones, geocercas, seguimiento GPS y predicciones quedan fuera de
este corte. No se inventan datos históricos de cobertura: la interfaz informa
la primera fecha realmente disponible en la proyección.
