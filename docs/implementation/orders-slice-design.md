# Rebanada de pedidos de Auraly Commerce

## Decisión central

La fuente canónica es el pedido que ya crea el bot en `dbo.Orders` y `dbo.OrderItems`. No se creó un segundo maestro de pedidos ni una copia específica para POS. El dashboard y el panel de Pedidos dentro del POS —web, instalado, enrolado o no enrolado— consultan y procesan esos registros directamente mediante la API web canónica. Edge no expone endpoints de estado comercial de pedidos.

Un pedido es comercial y no tributario. Guarda producto, cantidad, **precio público pactado incluido IVA**, descuento público y total comercial bruto. No congela el perfil ni la tarifa tributaria, la base gravable, CUFE, resolución o numeración fiscal. Al convertirlo en factura se consulta la configuración vigente del producto, se descompone el total público en base e impuesto y se construye entonces el snapshot fiscal inmutable.

`OrderItems.UnitPrice`, `OrderItems.DiscountAmount`, `OrderItems.LineTotal` y `Orders.Total` usan siempre esa semántica pública/bruta, sin importar si el productor fue bot, captura de vendedor o POS. Todo productor aplica una sola vez `MonetaryRounding.CeilingLineUnitPrice` cuando el producto ingresa o se edita. El borrador online conserva tanto los importes netos para su cálculo interno como el snapshot público exacto; su adaptador copia ese snapshot al crear o actualizar el pedido. Recuperar, pausar, volver a guardar y facturar transportan los importes pactados sin volver a redondearlos ni reconstruirlos. En una instalación enrolada, crear un pedido desde una venta local escribe el pedido directamente por la API web y solo después limpia el borrador SQLite confirmado.

## Alcance y aislamiento

- `BusinessId` delimita el pedido y todas las consultas. El `TenantId` se obtiene de la identidad autenticada y de `Businesses`; no se repite en las tablas comerciales que ya quedan determinadas por `BusinessId`.
- `Auraly.Contracts.Orders`, `Auraly.Domain.Orders` y `Auraly.Application.Orders` forman el límite del módulo.
- `Auraly.Infrastructure.Persistence` implementa almacenamiento SQL; Orders no consulta tablas internas de Sales directamente.
- La API compone los módulos mediante servicios de aplicación.

## Persistencia

Se conservaron y unificaron las tablas existentes:

- `Orders`: encabezado creado por bot o integración, cliente y valores comerciales.
- `OrderItems`: líneas comerciales sin columnas tributarias.
- `OrderClaims`: arrendamiento breve que evita recuperar simultáneamente el mismo pedido en dos ventas.
- `OrderInvoiceLinks`: relación única pedido–factura y factura–pedido.
- `OrderInvoiceBatchReceipts`: operación durable e idempotente para facturar selecciones de hasta 50 pedidos.
- `SalesDrafts.SourceOrderId`: identifica el pedido recuperado en una venta web.

Las restricciones SQL garantizan un único vínculo por pedido, un único vínculo por documento y una única operación por `BusinessId + IdempotencyKey`.

Todo pedido conserva `CustomerId + PartySiteId` como un par: ambos nulos para
consumidor final o ambos informados para un cliente. En los pedidos capturados
por vendedor (`Source=1`), los contratos web y PWA exigen la sede explícita y
el writer valida en la misma operación que esté activa y pertenezca al cliente
y negocio; el runtime no inventa ni resuelve una sede principal. La migración
previa al DACPAC es el único propietario de la compatibilidad histórica: asigna
la sede principal a los pedidos con cliente ya existentes antes de activar la
restricción física, sin reconstruir por nombre ni dirección.

## Casos de uso

### Consultar

Las consultas son paginadas en servidor y combinan número, cliente, producto, estado, fuente y fechas. Un pedido de otro negocio nunca se devuelve.

### Recuperar uno

1. La caja reclama temporalmente el pedido.
2. Obtiene el detalle desde Auraly Server.
3. Hidrata el borrador online desde la fotografía del pedido, sin consultar ni exigir que el producto siga activo o vendible.
4. Conserva código, nombre, unidad, cantidad, precio público, descuentos, costo, impuesto, moneda y total bruto del pedido.
5. Importa todas las líneas de forma atómica en el borrador del servidor; nunca deja media venta visible.
6. Impide mezclarlo con productos o con otro pedido ya presente.

Recuperar es una hidratación literal, no una reprificación ni una revalidación del catálogo. Solo una mutación comercial posterior de la línea vuelve a invocar el coordinador común y aplica las reglas vigentes.

Desde el POS, `Guardar pedido` exige cliente. Si el borrador todavía no lo tiene,
abre primero el selector; seleccionar el cliente pasa por el motor canónico de
precios para aplicar su lista o canal y solamente después guarda el pedido con el
borrador revalorizado. Esta reprificación pertenece a la captura inicial y no se
ejecuta al recuperar un pedido.

Crear un pedido no bloquea la captura por disponibilidad. En la transacción de
guardado, el motor intenta reservar únicamente líneas que manejan inventario. Si
la bodega no permite negativos y una línea completa no alcanza, esa línea no se
mueve parcialmente y el pedido completo queda `InReview`; las líneas suficientes
sí quedan reservadas. Al editar la revisión se identifican solamente las líneas
pendientes, con cantidad solicitada y disponible, para reducirlas o eliminarlas.
Productos sin control de inventario y bodegas que permiten negativos no generan
revisión por existencia.

Un pedido recuperado nunca se copia al borrador SQLite ni entra a la outbox. Aun
en un equipo instalado, el navegador cambia ese trabajo al borrador online y lo
guarda, pausa, recupera, cancela o factura mediante los endpoints web canónicos.
El servidor vincula el pedido en la transacción operacional que procesa la venta
y libera el claim; el pago se procesa por el motor contable canónico. Un reintento
no duplica factura, pago, inventario ni vínculo.

La preparación inicial del POS descarga clientes por páginas y cada registro
incluye todas sus sedes activas en el mismo payload. El snapshot SQLite conserva
esa colección y la búsqueda local devuelve una opción por `PartySiteId`. Al
guardar como pedido desde una venta local, el cliente web envía directamente a
la API `CustomerId + CustomerPartySiteId` y las líneas ya redondeadas; Edge solo
limpia el borrador local después de recibir la confirmación autoritativa. El
snapshot offline del vendedor conserva además el nombre de la sede para poder
renderizar el historial sin una resincronización.

### Inventario del pedido

La reserva y su consumo nunca se fragmentan ni se reconstruyen durante la factura:

- al confirmar el pedido, una sola `WarehouseTransfer` multilínea mueve todos los
  productos inventariables desde la bodega de venta hacia la bodega sistema `PED`;
- al facturar, el orquestador solo entrega `SourceOrderId` al motor canónico; no
  consulta disponibilidad ni prepara, libera o vuelve a trasladar inventario;
- dentro de la transacción canónica de la factura, el procesador registra `Sale`
  directamente contra la bodega `PED` indicada por `Orders.OrdersWarehouseId`.
  La cancelación del pedido sí devuelve la reserva a la bodega de venta.

Una edición reemplaza el detalle pero mueve inventario por diferencia agregada:
si una cantidad no cambia no crea traslados; los aumentos producen como máximo
una transferencia multilínea `VEN -> PED` y las disminuciones como máximo una
`PED -> VEN`, dentro de la misma transacción. El presupuesto del endpoint de
actualización es menor a un segundo para una edición sin cambio de reserva. Si
el pedido confirmado conserva la misma identidad comercial, líneas y reservas,
el writer actualiza únicamente los metadatos y su trabajo durable de reportería;
no vuelve a resolver catálogo, disponibilidad ni reemplaza el detalle.

### Facturar varios

Una selección produce una factura independiente por pedido. La operación completa tiene idempotencia durable, conserva progreso y devuelve resultado por pedido. Un pago ya confirmado por el pedido se registra como transferencia; de lo contrario se usa el medio seleccionado. Nunca se fusionan pedidos en una sola factura.

La API y la interfaz limitan cada lote a 50 pedidos. La selección masiva conserva
ese límite visible para que el usuario no prepare una solicitud que el servidor
rechazará. `Imprimir al facturar` está activo de forma predeterminada y puede
desmarcarse antes de emitir; esta preferencia sólo omite la vista previa o la
impresora física y nunca cambia checkout, numeración, cartera, inventario ni el
envío fiscal.

La vista ofrece únicamente `Efectivo` y `Crédito`. `Efectivo` entra directamente al lote. Para `Crédito`, el mismo `POST /api/commerce/v1/orders/invoice` registra primero el recibo idempotente y ejecuta una validación agrupada de todos los clientes y del valor acumulado de sus pedidos; es una sola consulta SQL para la selección completa. Si un cliente no tiene crédito habilitado o el valor agregado supera su cupo disponible, la respuesta identifica los clientes rechazados y el recibo queda finalizado para que un reintento devuelva exactamente la misma decisión, sin repetir lecturas. No crea borrador, liberación de inventario, factura ni cartera. Si todos cumplen, los pedidos se emiten uno por uno con idempotencia independiente.

La prevalidación masiva no reemplaza la validación transaccional. Cada pedido a crédito envía `OnlineSalesCreditTerms` al checkout de ventas existente, que vuelve a validar el saldo bajo bloqueo y genera la cuenta por cobrar por el canal canónico. No existe un writer ni una ruta de cartera específica para pedidos.

El camino exitoso carga encabezados y líneas de toda la selección en una sola
lectura acotada. Luego conserva la transacción independiente de cada pedido,
pero reutiliza el borrador limpio que devuelve el checkout, el snapshot completo
que devuelve la recuperación y el material fiscal ya resuelto para la misma
autorización. El progreso durable se guarda al fallar, al finalizar y cada cinco
resultados; no se hace una escritura redundante por documento. Así se preservan
recuperación e idempotencia sin introducir lecturas N+1 ni refetches del estado
que el motor canónico ya produjo.

La sede elegida atraviesa sin reinterpretación `Orders`, el borrador recuperado,
`SalesDocuments.CustomerPartySiteId`, `Receivables.PartySiteId` y las proyecciones
de ventas, líneas, servicios y pedidos comerciales. Los reportes históricos se
completan únicamente desde sus documentos y pedidos canónicos durante el
post-despliegue; no consultan nombres o direcciones para adivinar identidades.

## Seguridad

Permisos mínimos: `orders.read`, `orders.recover`, `orders.invoice`, `orders.cancel` y `orders.override-pricing`. Facturar también exige `sales.create`.

Todas las operaciones de pedidos usan siempre el JWT del usuario y los endpoints `/api/commerce/v1/orders/**`, incluso cuando se ejecutan dentro de un POS enrolado. Esto incluye lista, detalle, reclamo, recuperación, renovación, liberación, creación, actualización, impresión documental y facturación. Edge participa únicamente como transporte físico: la web obtiene primero el DTO imprimible desde el servidor y luego lo entrega al endpoint genérico de impresión. Edge no reenvía ni posee estado, recuperación, guardado o facturación del pedido. Si la API no está disponible, la interfaz informa que Pedidos requiere conexión y conserva el borrador que todavía no haya sido confirmado.

## Experiencia

`/dashboard/orders` y el panel embebido de `/pos` reutilizan `OrdersWorkspace`:

- tabla moderna y paginada;
- filtros combinables;
- selección múltiple y facturación por lote;
- control explícito `Imprimir al facturar`, activo por defecto;
- detalle lateral;
- recuperación de un solo pedido;
- modo compacto dentro del POS;
- expansión a espacio completo sin abandonar la venta.

El perfil operativo de vendedor ruta consume la misma consulta canónica desde
la pestaña `Pedidos` de su recorrido diario. La vista filtra por bodega, usuario
autenticado y límites UTC del día operativo, y permite abrir el mismo detalle de
líneas sin navegar fuera del recorrido. Los atributos heredados de un pedido se
leen únicamente cuando contienen JSON válido; una carga histórica malformada no
puede derribar ni la lista ni el detalle. El cliente web sólo presenta mensajes
de problema JSON controlados y nunca imprime una página HTML de error dentro de
la interfaz.

La búsqueda en vivo del catálogo de captura solicita una sola página de diez
productos por interacción, después de 250 ms de espera, y muestra únicamente
nombre, código, referencia, unidad, existencia y precio resuelto. Al llegar al
final visible solicita automáticamente las siguientes diez filas; no descarga el
catálogo completo ni hace enriquecimiento por producto. El presupuesto del
camino servidor es menor a un segundo. La preparación offline sigue siendo una
operación explícita y separada que descarga páginas grandes para instalar un
snapshot completo; nunca se ejecuta al buscar o abrir el selector en línea.

Los cambios tributarios dentro del pedido continúan fuera de esta rebanada:
pertenecen deliberadamente a la factura y deben entrar por el motor fiscal
canónico, no como campos o contratos paralelos en pedidos.
