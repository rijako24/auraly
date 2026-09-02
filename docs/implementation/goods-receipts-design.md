# Órdenes y recepciones de compra

Fecha de actualización: 2026-09-02

## Lenguaje de producto

- **Orden de compra**: compromiso planificado con un proveedor. Antes se había denominado “orden de pedido”.
- **Recepción de compra**: registro de lo que físicamente llegó. Reemplaza en la interfaz “entrada/recepción de mercancía”.
- Los nombres técnicos `PurchaseOrder` y `GoodsReceipt` permanecen estables en contratos y persistencia para evitar una ruptura artificial.

## Propietarios y flujo

```text
Orden de compra (Purchasing, sin efecto físico)
  -> borrador recuperable
  -> confirmación inmutable OCP
  -> cumplimiento acumulado por línea

Recepción de compra (Purchasing)
  -> puede recuperar una OCP abierta o parcial
  -> el usuario registra la cantidad real
  -> confirmación inmutable EMC
  -> DocumentProcessingEngine / handler GoodsReceipt
  -> inventario, costo, cuentas por pagar, contabilidad y cumplimiento OCP

Venta/devolución procesada
  -> SalesReportingProcessingCoordinator
  -> hechos de reporting
  -> ProductRotationSnapshots por negocio, bodega y producto
```

Purchasing es el propietario de la orden y su cumplimiento. Inventario sólo cambia dentro del handler canónico de `GoodsReceipt`. Reporting es el único propietario del cálculo persistido de rotación; compras y catálogo sólo leen su snapshot.

La fecha física de recepción la registra el sistema al guardar o confirmar y no se edita en la interfaz. El usuario sí informa la fecha de emisión del documento de compra. Esa fecha gobierna la causación contable, las retenciones y el inicio de la cuenta por pagar. `Suppliers.DefaultPaymentDueDays` es la condición comercial canónica del proveedor; el vencimiento se calcula como fecha de emisión más ese plazo, se muestra sólo para lectura y vuelve a validarse transaccionalmente en el servidor.

Las tablas de órdenes pertenecen al esquema `purchasing`. Las consultas y mutaciones nuevas se versionan en el proyecto de base de datos mediante procedimientos de los esquemas `purchasing` y `reporting`; los stores y handlers de C# sólo hacen invocaciones parametrizadas y no contienen SQL embebido.

## Ciclo de vida de la orden

`Draft -> Open -> PartiallyReceived -> Received`

Una orden abierta o parcial también puede pasar a `Closed` cuando un usuario autorizado cierra explícitamente el saldo con un motivo. No se permite cerrar si una recepción aceptada aún espera procesamiento. Una orden confirmada no se edita: la realidad se registra en recepciones separadas y auditables.

## Cantidades reales

Recuperar una orden propone el saldo pendiente, pero no bloquea la cantidad:

- orden 10, llegan 8: la recepción registra 8 y quedan 2;
- luego llegan 2: la orden queda recibida;
- orden 10, llegan 12: se conservan las 12 reales, pero el excedente exige motivo y el permiso `purchasing.goods-receipts.over-receive`;
- sin motivo o permiso, el servidor rechaza el excedente;
- el cumplimiento puede superar 100 %, mientras el pendiente nunca es negativo.

La recepción congela `PurchaseOrderId`, `PurchaseOrderLineId`, motivo y autorización del excedente. El handler bloquea orden y líneas, acumula lo recibido y deriva el estado en la misma transacción que aplica inventario. Esto evita dobles aplicaciones por reintentos.

Una recepción confirmada en estado `Accepted` cuenta como cantidad pendiente de aplicar mientras espera el motor. La recuperación descuenta ese pendiente y una confirmación concurrente lo valida bajo aislamiento serializable. Esto no reserva ni modifica inventario: evita que dos capturas consuman silenciosamente el mismo saldo documental de la orden.

## Rotación persistida

`reporting.ProductRotationSnapshots` guarda ventanas móviles de 30 y 90 días, ventas brutas, devoluciones, venta neta y demanda diaria de 90 días. La clave es `BusinessId + WarehouseId + ProductId`; por tanto, “Ver producto” muestra la rotación de cada bodega/sede del negocio y no mezcla existencias.

El writer canónico de reporting recalcula sólo los productos afectados al proyectar una venta o devolución. Una transacción histórica no puede hacer retroceder `WindowEndDate`. Las órdenes leen el snapshot para apoyar abastecimiento junto con stock actual y cantidades en camino; nunca recalculan ventas desde la pantalla ni desde Purchasing.

## Experiencia web

Las bandejas usan la grilla compartida, paginación de servidor y filtros por estado. Órdenes y recepciones usan el mismo selector paginado de productos del proveedor:

1. buscar o escanear por nombre, códigos, referencia o código del proveedor;
2. seleccionar agrega la línea y enfoca su cantidad;
3. flechas arriba/abajo recorren cantidades;
4. Enter en cantidad devuelve el foco al buscador.

El detalle de producto presenta rotación sólo como información de lectura. La edición del producto no puede escribirla.

## Contratos principales

- `GET /api/commerce/v1/purchase-orders`
- `GET /api/commerce/v1/purchase-orders/{id}`
- `GET /api/commerce/v1/purchase-orders/{id}/receipt-source`
- `PUT /api/commerce/v1/purchase-orders/{id}/draft`
- `POST /api/commerce/v1/purchase-orders/confirm`
- `POST /api/commerce/v1/purchase-orders/{id}/close`
- `GET /api/commerce/v1/goods-receipts/products`
- `PUT /api/commerce/v1/goods-receipts/drafts/{id}`
- `POST /api/commerce/v1/goods-receipts/confirm`
- `GET /api/commerce/v1/products/{id}/rotation`

Todos derivan tenant y negocio de la identidad autenticada y validan que bodega, proveedor, producto y orden pertenezcan al mismo alcance. En una compra a crédito también validan que el vencimiento coincida con el plazo del proveedor, para que un cliente desactualizado no pueda alterar la condición calculada.

## Rollback y compatibilidad

Las columnas nuevas de recepción son opcionales; las recepciones sin orden conservan el comportamiento anterior. El despliegue puede revertir código sin perder documentos: las tablas y columnas agregadas son compatibles. No se eliminan ni reescriben recepciones confirmadas; cualquier reversión económica o física se realiza mediante los documentos compensatorios canónicos.
