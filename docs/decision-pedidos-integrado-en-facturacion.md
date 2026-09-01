# Decisión: Pedidos integrado dentro de Facturación

## Prevalencia

Este documento corrige la interpretación anterior del módulo Pedidos.

El alcance prioritario solicitado no es una aplicación de pedidos independiente. Es el submódulo que se abre desde Facturación para:

- consultar pedidos pendientes;
- recuperar un pedido puntual;
- seleccionar uno o varios pedidos;
- cargar sus productos en la factura;
- facturarlos individualmente o consolidarlos cuando sean compatibles;
- conservar trazabilidad entre pedido, líneas y factura.

La creación administrativa o móvil de pedidos puede existir, pero no sustituye este flujo y no es su prioridad inicial.

---

## 1. Acceso desde Facturación

Facturación tendrá una acción visible:

```text
Pedidos pendientes
```

Al abrirla se presenta un panel amplio o pantalla superpuesta dentro del contexto de la caja. El cajero no debe abandonar ni cerrar el módulo de facturación.

La consulta permite buscar por:

- número de pedido;
- documento o nombre del cliente;
- teléfono;
- fecha o rango;
- vendedor;
- ruta;
- estado;
- bodega;
- origen;
- número externo.

La tabla muestra:

- selección;
- número;
- fecha;
- cliente;
- vendedor;
- cantidad de líneas;
- cantidad pendiente;
- total pendiente;
- estado;
- estado de facturación;
- alertas;
- origen.

---

## 2. Recuperar un pedido puntual

El cajero puede:

1. escribir o escanear el número del pedido;
2. abrir su vista previa;
3. revisar cliente, productos, cantidades y totales;
4. pulsar **Traer a factura**;
5. continuar en la grilla normal de Facturación;
6. modificar únicamente lo permitido;
7. seleccionar medios de pago;
8. confirmar y emitir la factura.

La factura conserva:

- `OrderId`;
- número del pedido;
- vínculo por cada línea;
- cantidad tomada;
- cantidad pendiente;
- precio y descuento originales;
- usuario y caja que recuperaron el pedido.

---

## 3. Seleccionar varios pedidos

La bandeja permite selección múltiple.

Al pulsar **Facturar seleccionados**, el sistema analiza compatibilidad antes de cargar:

- misma empresa y negocio;
- mismo cliente para una factura consolidada;
- misma moneda;
- misma bodega operativa;
- contexto tributario compatible;
- pedidos confirmados y con cantidades pendientes;
- pedidos no reclamados por otra caja;
- pedidos no cancelados;
- productos todavía facturables.

### 3.1. Consolidar en una factura

Varios pedidos pueden convertirse en una sola factura cuando pertenecen al mismo cliente y son compatibles.

La factura conserva múltiples vínculos:

```text
Factura F-100
  <- Pedido P-10
  <- Pedido P-11
  <- Pedido P-15
```

Si el mismo producto aparece en varios pedidos, la interfaz puede mostrarlo agrupado visualmente, pero la trazabilidad interna conserva la contribución de cada `OrderItem`. No se debe perder el origen al sumar cantidades.

### 3.2. Facturación por lote

Si los pedidos pertenecen a clientes diferentes, no pueden consolidarse en una sola factura.

El sistema puede generar una factura independiente por pedido o por grupo compatible, pero antes debe mostrar:

- cantidad de facturas que se crearán;
- agrupación por cliente;
- total de cada factura;
- medio y condición de pago;
- errores o pedidos excluidos.

La facturación por lote debe ser idempotente. Un reintento no puede generar facturas duplicadas.

Para el primer incremento, la consolidación de pedidos del mismo cliente es obligatoria. La emisión masiva para clientes distintos puede habilitarse después de validar el flujo de medios de pago y cartera.

---

## 4. Factura temporal existente

Si la caja ya tiene líneas capturadas cuando abre Pedidos, el sistema no debe reemplazarlas silenciosamente.

Debe ofrecer:

- **Combinar con factura actual**, si cliente y contexto son compatibles;
- **Guardar factura actual como temporal y abrir pedido**;
- **Cancelar**.

Si el cliente no coincide, combinar queda bloqueado.

La factura temporal conserva todos sus datos y puede recuperarse después.

---

## 5. Cantidades pendientes y facturación parcial

El pedido no se considera únicamente abierto o facturado. La relación se controla por cantidades.

Cada línea conserva:

```text
OrderedQuantity
InvoicedQuantity
CancelledQuantity
PendingQuantity
```

Al recuperar un pedido se carga `PendingQuantity`.

El cajero puede reducir la cantidad si tiene permiso. En ese caso:

- la factura registra lo efectivamente entregado;
- el pedido queda `PartiallyInvoiced`;
- el saldo continúa disponible;
- una caja posterior puede recuperar únicamente lo pendiente.

No se permite cargar una cantidad superior a la pendiente como si perteneciera al pedido. La diferencia puede agregarse como una línea de venta independiente y queda identificada como tal.

Estados mínimos:

```text
Confirmed
ClaimedForInvoicing
PartiallyInvoiced
Invoiced
Cancelled
```

---

## 6. Prevención de doble facturación

Abrir un pedido en dos cajas no puede producir dos facturas por las mismas cantidades.

Flujo:

1. la caja consulta pedidos disponibles;
2. al traer uno, el servidor crea un `claim` temporal con caja, usuario y vencimiento;
3. la respuesta incluye versión o `rowversion`;
4. otra caja lo ve como **En proceso en caja X**;
5. al confirmar, una transacción vuelve a validar cantidades pendientes y versión;
6. crea factura y vínculos;
7. actualiza cantidades facturadas;
8. libera el claim;
9. un abandono o vencimiento libera el claim sin facturar.

La validación transaccional final es obligatoria aunque exista el claim.

El endpoint de confirmación utiliza `IdempotencyKey`.

---

## 7. Precios, descuentos e impuestos

El pedido conserva snapshots de:

- precio;
- descuento;
- impuesto;
- unidad;
- descripción;
- producto.

Al recuperarlo:

- el precio comercial del pedido se conserva por defecto;
- los descuentos se conservan si siguen autorizados;
- el sistema valida reglas fiscales obligatorias;
- cualquier diferencia se muestra antes de continuar;
- ninguna recotización ocurre silenciosamente.

El cajero puede ejecutar **Actualizar precios** únicamente con permiso. La acción muestra antes/después y deja auditoría.

Si una norma fiscal exige cambiar el impuesto, el sistema bloquea la emisión hasta resolverlo y registra el motivo.

---

## 8. Inventario

La caja sigue sin descargar inventario.

Al cargar uno o varios pedidos:

1. Facturación agrupa las cantidades requeridas por producto y bodega.
2. Si la caja está configurada para bloquear negativos, consulta disponibilidad en línea por lote.
3. La interfaz marca las líneas insuficientes.
4. El cajero puede reducir cantidades o excluir líneas según permisos.
5. Al confirmar, se valida nuevamente dentro de la transacción.

La validación ocurre al cargar el pedido y al cambiar cantidades, no solamente al confirmar.

Que un pedido esté confirmado no significa necesariamente que el inventario esté reservado. La reserva será una política explícita:

```text
DoNotReserve
ReserveOnOrderConfirmation
```

Para el MVP se debe escoger una política por negocio y mostrar claramente si las cantidades están reservadas.

---

## 9. Funcionamiento offline

Consultar y reclamar pedidos pendientes del servidor requiere conexión, porque:

- la caja no debe mantener todos los pedidos;
- debe conocer cantidades ya facturadas;
- debe impedir doble facturación;
- puede necesitar inventario en línea.

Si un pedido ya fue recuperado y guardado como factura temporal local, puede conservarse durante una pérdida de red. Sin embargo, antes de emitir debe reconciliar el claim y las cantidades con el servidor.

En modo offline:

- se pueden recuperar temporales creados en esa caja;
- no se presentan pedidos del servidor como disponibles;
- no se permite facturar un pedido remoto sin reconciliación;
- una venta libre puede continuar según la política offline de la caja.

La captura móvil del vendedor es distinta de recuperar pedidos remotos para
facturarlos. La acción **Preparar** conserva para el usuario autenticado la
carcasa de la aplicación, sede, bodega, todas sus rutas activas, clientes, el
catálogo activo completo —incluidos catálogos de decenas de miles de productos—,
precios, existencias y pedidos del día. La descarga es paginada, sustituye cada
catálogo completo en una sola escritura y valida el almacenamiento disponible;
no reescribe un arreglo creciente por cada página. Cada actualización se guarda
en una generación nueva y el puntero activo solo cambia al terminar todo el
corte, por lo que una interrupción conserva íntegro el catálogo anterior.

Preparar y operar son decisiones separadas. La UI inyecta un único contrato de
datos con modo `online` o `local`; no duplica pantallas ni reglas. En modo local,
cada pedido se confirma primero en el outbox con su misma clave idempotente,
aparece de inmediato en **Pedidos** y se sincroniza en segundo plano aunque haya
Internet. La visita se sincroniza junto con ese pedido; no se genera además una
segunda visita en otra cola. **Actualizar datos** descarga un nuevo corte y
muestra fecha y hora. La preparación y el modo se aíslan por
`UserId + BusinessId + WarehouseId`, no caducan a medianoche y permanecen hasta
que el usuario vuelva explícitamente al modo online. Cada día se proyectan desde
las definiciones guardadas las rutas programadas para esa fecha, mientras
pedidos y visitas mantienen su fecha operativa.

Sin preparación, la captura permanece server-first y la lista consulta al
servidor. Una caída muestra un error útil y no afirma que el pedido quedó
guardado. El modo local nunca se infiere de `navigator.onLine`: es una decisión
explícita y durable del usuario.

Los snapshots, preparaciones, pedidos pendientes y visitas locales se aíslan por
`UserId`; cambiar de usuario no permite leer ni enviar el trabajo pendiente de
otro vendedor. Preparar conserva la sesión actual para reabrir la aplicación sin
red, pero nunca almacena contraseñas ni crea verificadores de acceso en el
navegador. Un nuevo login con credenciales completamente desconectado continúa
siendo exclusivo de un equipo Edge enrolado y su proyección de identidad
firmada, conforme a `decision-sesion-unica-usuario-online-offline.md`.
En el teléfono, si la aplicación se vuelve a abrir, la pantalla de acceso
reconoce la sesión autenticada conservada y el modo local activo, haya o no red,
restaura su sede y bodega y entra directamente a **Pedidos**. Un cierre de
sesión explícito elimina esa sesión conservada y exige volver a autenticarse en
línea; preparar nunca convierte el navegador en un verificador de credenciales.

---

## 10. Diseño web dentro del POS

```text
+------------------------------------------------------------------+
| Facturación                                      Pedidos (12)    |
+------------------------------------------------------------------+
| Buscar número, cliente, teléfono...  Estado  Fecha  Vendedor     |
+------------------------------------------------------------------+
| [ ] P-1045 | Cliente A | 4 líneas | $350.000 | Confirmado        |
| [ ] P-1046 | Cliente A | 2 líneas | $120.000 | Parcial           |
| [ ] P-1047 | Cliente B | 8 líneas | $900.000 | En otra caja      |
+------------------------------------------------------------------+
| 2 seleccionados | Cliente A | Total pendiente $470.000           |
|                       Vista previa | Traer a factura              |
+------------------------------------------------------------------+
```

La vista previa muestra productos, pendientes, disponibilidad, precios y advertencias sin abrir múltiples ventanas.

Comportamiento:

- Enter abre o recupera;
- barra espaciadora selecciona;
- lector puede buscar el número del pedido;
- selección múltiple por teclado;
- foco predecible;
- panel de advertencias;
- no usar ventanas modales pequeñas para tablas grandes;
- regresar a la grilla de facturación conservando el foco de captura.

No se copiará visualmente el formulario WinForms de Xion. Se conserva su velocidad operativa y sus reglas, adaptadas a una experiencia web.

---

## 11. Modelo de datos mínimo

Además de `Orders` y `OrderItems`, se necesitan vínculos explícitos:

```text
OrderInvoiceLinks
  OrderInvoiceLinkId
  OrderId
  InvoiceId
  CreatedAtUtc

OrderItemInvoiceLinks
  OrderItemInvoiceLinkId
  OrderItemId
  InvoiceLineId
  InvoicedQuantity
  CreatedAtUtc

OrderInvoicingClaims
  OrderInvoicingClaimId
  OrderId
  CashRegisterId
  UserId
  DeviceId
  ExpiresAtUtc
  RowVersion

OrderStatusHistory
  OrderStatusHistoryId
  OrderId
  PreviousStatus
  NewStatus
  Reason
  UserId
  CreatedAtUtc
```

Estas tablas se definen en `Auraly.Database.sqlproj`, dentro de `dbo`. No se crean mediante migraciones EF.

---

## 12. Casos de uso de Application

```text
SearchPendingOrders
GetOrderForInvoicing
ClaimOrderForInvoicing
ClaimOrdersForInvoicing
ValidateOrderSelection
LoadOrdersIntoInvoiceDraft
ReleaseOrderClaim
RefreshOrderClaim
InvoiceSelectedOrders
GetOrderInvoicingProgress
```

La lógica pertenece a:

```text
Auraly.Application.Orders
Auraly.Application.Sales
```

Orders controla pendientes y claims. Sales controla el borrador y la factura. La API solo expone los endpoints.

---

## 13. Criterios de aceptación

- Pedidos se abre dentro de Facturación.
- Puede buscarse un pedido puntual.
- Puede seleccionarse uno o varios.
- Varios pedidos compatibles del mismo cliente pueden consolidarse.
- Pedidos de clientes distintos nunca forman una sola factura.
- Se conservan vínculos por pedido y línea.
- Se soporta facturación parcial.
- Solo se cargan cantidades pendientes.
- Una caja no puede duplicar lo facturado por otra.
- Una factura temporal existente no se pierde.
- Los precios no cambian silenciosamente.
- La política de inventario se valida al cargar y cambiar cantidades.
- La consulta de pedidos remotos requiere conexión.
- Las tablas se administran desde `Auraly.Database.sqlproj`.
- El diseño web prioriza teclado y velocidad sin copiar WinForms.
