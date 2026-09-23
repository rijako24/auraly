# Rebanada vertical de cuentas por cobrar

Fecha: 2026-08-03
Prevalencia: la distribución vigente de efectos se rige por
`../decision-cuatro-motores-operacion-contabilidad-fiscal-reporting.md`.

Estado del diseño: implementado.

## Decisión

La cuenta por cobrar nace únicamente cuando el motor financiero-contable procesa
la fuente durable de una venta a crédito verificada. El crédito no se representa
como un medio de pago ficticio: la suma de pagos reales más el valor financiado
debe ser igual al total de la venta. Esta cartera comercial funciona aunque el
tenant no active el libro mayor.

El recaudo es un documento financiero durable (`ReceivablePayment`). Su aceptación
crea una única fuente y trabajo financiero-contable. El único
`SqlAccountingPostingProcessor` aplica en una transacción el pago, asignaciones,
saldo de la obligación y movimiento financiero de la sesión. Si el trabajo
congeló contabilidad activa también crea el asiento en esa transacción. El ingreso
financiero es directo mediante `AccountingSourceDocuments` y
`AccountingPostingJobs`; no usa `DocumentProcessingJobs`.

## Modelo

- `CustomerCreditProfiles`: habilitación, plazo y cupo opcional por cliente y
  negocio.
- `Receivables`: obligación originada por un documento de venta, con sede del
  cliente congelada para cada obligación nueva.
- `ReceivableTransactions`: libro inmutable de apertura, pago y futuras compensaciones.
- `CustomerPayments`: encabezado durable e idempotente del recaudo.
- `CustomerPaymentApplications`: distribución del recaudo entre obligaciones.

Las tablas pertenecen al `BusinessId`; el `TenantId` se valida mediante la relación canónica con `Businesses`. No se duplican datos de Party ni del documento fiscal.

## Sede de la factura y vistas de cartera

### Decisión

No existe una configuración de cartera consolidada o separada en el cliente.
Cada factura y cada obligación nueva conservan siempre la `PartySite` elegida,
mientras una `Party` con varias sedes continúa siendo un solo tercero y conserva
un solo `CustomerId` por `BusinessId`. Consolidar o desglosar es una decisión de
consulta y presentación, no una política de escritura ni una reclasificación de
la deuda.

### Dato documental y dato de cartera

La sede seleccionada en la venta no debe inferirse nuevamente después de emitir:

- `SalesDrafts.CustomerPartySiteId`, el contrato de emisión y
  `SalesDocuments.CustomerPartySiteId` conservan la sede elegida para la venta,
  El snapshot comercial/fiscal conserva también el nombre, dirección, geografía
  y contacto usados al emitir.
- `Receivables.PartySiteId` es la dimensión congelada de la cuenta auxiliar y
  contiene la misma sede seleccionada para toda obligación nueva.

El procesador no consulta nuevamente una configuración mutable al abrir la
obligación. El payload durable congela `CustomerPartySiteId` y el único
`SqlAccountingPostingProcessor` copia esa dimensión al crear `Receivables`. De
esta forma un retry o el renombrado de una sede no reclasifican deuda.

La lista de obligaciones muestra `Nombre del cliente · Nombre de la sede`. Sus
tarjetas de totales consolidan el saldo del resultado y la consulta puede
filtrar por cliente o por sede sin alterar las obligaciones individuales.

La consulta admite filtro por `PartySiteId` y búsqueda por nombre o código de
sede, siempre paginada y con una sola consulta. El detalle devuelve ambos IDs y
el nombre visible. El documento fuente conserva el snapshot histórico; la
cartera puede unir la `PartySite` actual para mostrar su nombre vigente, incluso
si fue desactivada.

### Cupo, plazo y recaudo

El cupo, la habilitación y el plazo predeterminado continúan perteneciendo a
`CustomerCreditProfiles` y abarcan al cliente completo. Todas las obligaciones
abiertas y documentos de crédito pendientes de sus sedes consumen el mismo
límite global. Un futuro subcupo por sede sería otra capacidad y requeriría
reglas explícitas; no se deduce de la sede de la factura.

`CustomerPayments` continúa siendo un documento del cliente. Sus aplicaciones
referencian obligaciones concretas y por eso preservan naturalmente la sede de
cada saldo. Un recaudo puede aplicar de forma explícita a obligaciones de una o
varias sedes; la UI no distribuye automáticamente entre sedes ni oculta la
asignación. La sede se obtiene del detalle de las obligaciones aplicadas, sin
duplicarla ni convertirla en una clasificación mutable del encabezado. No se
crea otro motor, encabezado de recaudo ni tabla de saldos por sede.

### Selección de sede y facturación

La venta siempre conserva la sede real del cliente:

1. al seleccionar un cliente con una sola sede activa, el servidor puede
   seleccionarla de forma determinista;
2. con varias sedes activas, POS y facturación de servicios exigen selección
   explícita;
3. el servidor valida que la sede esté activa, pertenezca a la misma `Party` del
   `CustomerId`, y que el cliente pertenezca al `BusinessId` emisor autorizado;
4. un pedido que ya tiene `PartySiteId` lo transfiere al borrador y a la factura
   sin resolverlo por nombre o dirección;
5. la identidad legal, NIT y responsabilidades siguen saliendo de `Party`; la
   dirección/contacto del adquirente y la entrega salen del snapshot de la sede
   seleccionada;
6. CUFE, numeración y totales no cambian por dividir cartera, pero el
   `PartySiteId` forma parte del request/payload idempotente y no puede cambiarse
   en un replay.

La emisión deja de elegir silenciosamente la sede principal cuando el operador
seleccionó otra. Desactivar o renombrar la sede después de emitir no altera el
snapshot fiscal, la factura ni la cuenta por cobrar.

### Búsqueda operativa de clientes por sede

Los buscadores usados para facturar, capturar pedidos, asignar rutas o escoger
un destino proyectan una fila por `PartySite` activa, no una fila por
`CustomerId`. Por ejemplo, si `MegaFruver` tiene las sedes `Norte` y `Centro`, el
resultado presenta:

```text
MegaFruver · Norte
MegaFruver · Centro
```

Cada opción devuelve como mínimo `PartyId`, `CustomerId`, `PartySiteId`, nombre
del cliente, nombre/código de sede, identificación y dirección resumida. La clave
de selección y deduplicación es `PartySiteId`; deduplicar por `CustomerId`
eliminaría sedes válidas. La búsqueda coincide por nombre o identificación del
cliente y por nombre, código, dirección o teléfono de la sede. La paginación y
el total cuentan sedes resultantes, mantienen un orden estable por cliente,
sede e ID y se resuelven en una consulta sin N+1.

La expresión escrita se normaliza y divide por espacios en términos. Todos los
términos deben encontrarse en la misma opción cliente-sede, pero pueden aparecer
en cualquier orden y en campos distintos de esa opción. No se busca la frase
completa ni se hace una unión de resultados independientes por palabra:

- `Kevin Ramírez` encuentra `Ramírez Kevin`, `Kevin Daniel Ramírez` y una sede
  donde `Kevin` esté en el nombre del cliente y `Ramírez` en otro nombre
  indexado de la misma opción;
- `Kevin Daniel` exige que tanto `Kevin` como `Daniel` existan en el mismo
  resultado; no mezcla una fila que solo tenga `Kevin` con otra que solo tenga
  `Daniel`;
- `MegaFruver Norte` encuentra solamente la sede `Norte` de `MegaFruver`, mientras
  `MegaFruver` puede devolver todas sus sedes activas.

Mayúsculas, minúsculas, espacios repetidos y el orden de las palabras no cambian
la semántica AND. Online y Edge aplican la misma descomposición por términos; la
paridad se cubre con los mismos casos de contrato.

La implementación envía todos los términos en una sola operación, aplica el
`AND` antes de paginar y no ejecuta una consulta por término ni por sede. Online
consulta `Customers + PartySites` en una sola sentencia; Edge expande el arreglo
local de sedes dentro de una sola consulta SQLite. El camino debe medirse con el
volumen objetivo y conservar el debounce de la UI; si ese volumen exige otra
proyección, se agrega con benchmark sin crear un segundo buscador semántico.

Online y Edge exponen la misma proyección. Edge conserva las sedes activas en el
registro local sincronizado del cliente y las expande como opciones sin duplicar
la identidad `Party` ni el rol `Customer`. Seleccionar un resultado persiste
juntos `CustomerId + CustomerPartySiteId` en el borrador.

Esta regla aplica solamente cuando la operación necesita una ubicación concreta.
El maestro de terceros, la configuración global de crédito y otros selectores
puramente jurídicos continúan mostrando una fila por cliente. La pantalla de
CxC muestra una fila por obligación y usa `Cliente · Sede` solo cuando su
`Receivables.PartySiteId` no es nulo.

Un cliente sin ninguna sede activa no aparece como opción facturable hasta que
se cree o reactive una sede. La creación rápida de cliente continúa creando
atómicamente su sede principal, por lo que no introduce clientes incompletos en
el buscador.

### POS web y equipo enrolado

El enrolamiento continúa fijando el `TenantId`, `BusinessId` y la identidad del
equipo; no crea una sesión, caja, serie ni bodega por sede del cliente. Las
`PartySites` son ubicaciones del comprador y no forman parte de la jerarquía
operativa del emisor.

El adaptador online guarda `CustomerPartySiteId` en el borrador durable. Edge
sincroniza clientes y sedes activas en su proyección SQLite,
persiste la selección en el borrador local y la incluye en la emisión. La UI
compartida presenta el mismo selector y etiqueta en ambos adaptadores; no
calcula cupo.

La autorización de crédito sigue siendo exclusivamente del validador canónico de
SQL Server. Un equipo enrolado sin conexión no puede vender a crédito, aunque
tenga sedes o cupo sincronizados. Con conexión, el host envía
`CustomerId + CustomerPartySiteId + Amount`; el servidor valida la relación y el
cupo global dentro de la misma transacción serializable usada actualmente.

Los cierres de sesión y movimientos de recaudo continúan totalizando por
`WorkSessionId` y medio de pago. La sede del cliente puede agregarse como
desglose consultable o imprimible, pero no abre otra sesión ni modifica el saldo
de caja.

### Devoluciones, despacho, fiscal, contabilidad y reporting

- Una devolución o nota crédito aplica a la `ReceivableId` de la venta original;
  hereda por referencia su dimensión y no vuelve a resolver la sede.
- Despacho consume `SalesDocuments.CustomerPartySiteId` y su snapshot. No debe
  adivinar la sede comparando direcciones ni escoger la principal.
- El XML y la representación fiscal conservan el mismo adquirente legal; la sede
  seleccionada aporta la dirección congelada cuando corresponde.
- El libro mayor conserva a la `Party` como tercero y la misma cuenta contable
  de clientes. `PartySiteId` es una dimensión del submayor de CxC, no un tercero,
  centro de costo ni cuenta contable nueva.
- Reporting puede proyectar `CustomerPartySiteId` desde el documento para ventas
  y `Receivables.PartySiteId` para cartera. No se crea una nueva cola o tabla de
  jobs; se extiende la proyección existente solamente cuando exista un reporte
  consumidor y un benchmark.
- La constancia de venta a crédito y los formatos de cartera muestran
  `Cliente · Sede`, sin sustituir el nombre
  legal del adquirente en la factura.

### Migración

El despliegue es aditivo:

- `CustomerPartySiteId` y `Receivables.PartySiteId` son inicialmente anulables;
- las obligaciones históricas sin sede permanecen identificables como legado y no se asignan a la sede
  principal ni se reconstruyen comparando direcciones;
- los nuevos payloads documentales congelan la sede;
- una sede con obligaciones históricas puede desactivarse, pero no eliminarse
  físicamente mientras esté referenciada.

El rollback de aplicación conserva las columnas aditivas y los saldos. No se
borran ni consolidan obligaciones ya emitidas.

### Integridad, rendimiento y observabilidad

- La validación de cliente, sede, perfil, saldo abierto y crédito pendiente se
  realiza bajo el mismo orden de bloqueos e aislamiento serializable del
  validador actual; no se agrega una consulta por obligación o por resultado.
- El escritor canónico valida nuevamente la pertenencia de la sede antes de
  insertar. Las FK impiden IDs inexistentes y los índices soportan
  `BusinessId + CustomerId + PartySiteId + Status + DueDate` sin N+1.
- Listado, cupo y recaudo siguen acotados por `TenantId/BusinessId`; un
  `PartySiteId` enviado por UI o Edge nunca amplía el alcance autorizado.
- Logs y métricas incluyen `BusinessId`, `DocumentId`, `CustomerId`,
  `PartySiteId` cuando aplique, sin copiar nombre,
  dirección, identificación ni otro PII.
- La aceptación exige regresiones para selección de sede, saldo global,
  replays, cambios concurrentes de cupo, pagos multi-sede,
  devoluciones, emisión online/Edge conectada, rechazo offline, fiscal y
  aislamiento entre tenants y negocios.

## Flujo conectado

1. La venta online o Edge conectado recibe pagos reales y, opcionalmente,
   términos de crédito.
2. Un único validador SQL de cartera valida cliente, perfil, obligaciones abiertas
   y crédito pendiente de procesar. El checkout web lo usa en su transacción; Edge
   lo invoca por el servidor antes de emitir y el ingreso durable lo revalida.
3. El motor operacional procesa inventario/costo y publica una señal contable exactamente una vez.
4. El motor financiero-contable crea en su transacción la obligación y el movimiento inicial; agrega el asiento únicamente si contabilidad estaba activa al aceptar la fuente.
5. La API permite consultar cartera paginada y registrar un recaudo con llave de idempotencia.
6. El recaudo crea directamente su fuente y trabajo contables durables.
7. El procesador canónico aplica abonos sin permitir sobrepago y actualiza el estado; contabiliza caja/banco contra cartera únicamente cuando el trabajo requiere asiento.
8. Los recaudos por transferencia conservan la cuenta bancaria seleccionada; la
   cuenta principal es el valor predeterminado y el operador puede escoger otra
   cuenta activa del mismo tenant.
8. La vista administrativa consulta el libro real y registra abonos; no calcula saldos en el navegador.

## Concurrencia e idempotencia

- La base impide una obligación duplicada por documento origen.
- `PaymentId` e `IdempotencyKey` son únicos por negocio.
- Un replay con el mismo contenido devuelve la aceptación previa; contenido distinto produce conflicto.
- En caja preparada, el proxy local guarda primero una proyección mínima por
  `PaymentId` y `WorkSessionId`; solo entonces confirma el recaudo en el servidor.
  El cierre usa esa proyección local y un replay conserva el mismo identificador.
- La aceptación usa aislamiento `Serializable`, bloqueos de actualización y reintento acotado de deadlock.
- Dos abonos concurrentes que excedan el saldo no pueden ser aceptados ambos.
- La configuración de cupo se actualiza en una transacción serializable.
- La UI y el catálogo local no son autoridad para autorizar cupo. Un snapshot
  descargado puede orientar la captura, pero la decisión usa el saldo actual del
  servidor.

## Contabilidad

- Venta a crédito: débito a cuentas por cobrar y créditos a ingresos e impuestos, además del costo/inventario aplicable.
- Recaudo: débito al medio de recaudo configurado y crédito a cuentas por cobrar.
- No se crean pagos de venta ficticios para representar financiación.
- Con contabilidad inactiva, venta, recaudo y devolución conservan la misma
  cartera y transacciones, pero generan cero `AccountingEntries`.
- `AccountingEntryRequired` se congela por trabajo. La activación posterior usa
  saldos iniciales aprobados y no reprocesa el historial como ingresos o recaudos.

## Límites deliberados de esta rebanada

Esta entrega cubre crédito y recaudo online y crédito desde POS Edge mientras
exista conexión efectiva con Auraly Server. El crédito sin servidor permanece
bloqueado; no se habilita a partir del cupo sincronizado. Tampoco incluye
intereses, cuotas, cheques posfechados, castigos, retenciones ni conciliación
bancaria.
