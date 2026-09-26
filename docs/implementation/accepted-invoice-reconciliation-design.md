# Conciliación de facturas DIAN aceptadas sin venta económica

## Incidente verificado

En Megafruver `FVL112`, `FVL143` y `FVL154` recibieron respuesta DIAN `00`
el 18 de septiembre de 2026 mientras `SalesDocuments.ProcessingStatus`
permanecía `Blocked`. Ninguna tiene trabajo de procesamiento comercial,
líneas, pagos ni cartera. `FVL143` y `FVL154` documentan el mismo pedido y las
mismas doce líneas; solo existe una venta económica. La cronología y los
identificadores están en `dian-megafruver-20260925-evidence.md`.

## Regla preventiva

Una factura electrónica de venta solo se puede generar y transmitir por
primera vez después de completar en una única transacción el trabajo comercial
canónico. El guardia de generación y el guardia de adquisición de transmisión
exigen `ProcessingStatus=Completed`. Una clave de envío ya persistida permite
consultar el resultado de ese envío, nunca crear una nueva transmisión. El
checkout de pedidos conserva un recibo único por pedido antes de reservar
numeración; el vínculo comercial único se crea al procesar la venta.

## Conciliación histórica

1. Confirmar desde los snapshots inmutables y las respuestas DIAN que las tres
   facturas están aceptadas, que importes, CUFE, cliente y líneas coinciden con
   el pedido o borrador original, y que no hay efecto económico previo.
2. Procesar `FVL112` una vez desde su snapshot original mediante el trabajo
   comercial existente. Conservar su número, CUFE y aceptación DIAN; no volver
   a transmitirla. El borrador de origen estaba asociado a un pedido, aunque
   el recibo de checkout es de tipo borrador.
3. Procesar una sola factura del pedido compartido. Se propone `FVL143` por
   haber sido emitida primero; la elección se valida contra los documentos
   entregados al cliente antes de ejecutar la corrección. El trabajo comercial
   debe crear su único vínculo `OrderInvoiceLinks`, líneas, inventario,
   cuentas por cobrar y asientos. La unicidad del vínculo impide procesar
   `FVL154` como segunda venta.
4. Crear para `FVL154` una nota crédito electrónica que la referencie, con
   motivo de factura duplicada y por su valor total. Es una corrección
   **exclusivamente fiscal**: no hay mercancía devuelta, pago que reintegrar,
   inventario que ingresar ni ingreso/cartera reconocidos en esa factura.
   Nunca se registra como `SalesReturn`. Usar la numeración de notas crédito,
   el generador UBL, firmador, cola, transporte, artefactos e historial del
   motor fiscal existente. Un registro de corrección vincula de manera única
   el documento corregido, la nota, el motivo y el operador.
5. Confirmar aceptación DIAN de la nota y entregar PDF/XML. Reconciliar el
   correo de la factura que quedó vigente. El 26 de septiembre las tres
   tareas originales seguían sin procesar: agotaron diez intentos por
   respuesta `429 Too Many Requests` del proveedor de correo. Rehabilitar
   explícitamente solo los envíos de las facturas vigentes cuando el servicio
   admita entregarlos; nunca enviar `FVL154` como factura vigente después de
   corregirla. La nota crédito usa la entrega fiscal de PDF/XML existente.

## Invariantes de la recuperación

- No modificar XML, CUFE, importes, respuesta DIAN ni numeración de facturas
  aceptadas.
- El comando de recuperación exige tenant, autorización, verificación fiscal
  del snapshot, aceptación DIAN comprobada y ausencia de trabajo, líneas,
  pagos, cartera, inventario y vínculo comercial previo. Se rechaza ante una
  segunda factura ya vinculada al mismo pedido.
- Actualizar estado y encolar el trabajo comercial en una sola transacción,
  usando `BusinessProcessingCursors`, `DocumentProcessingJobs` y
  `DocumentProcessingPayloads` existentes. Si viene de un pedido, reservar
  también su único `OrderInvoiceLinks` en esa transacción: así no se encolan
  dos facturas aceptadas del mismo pedido mientras el primer trabajo está
  pendiente. Un segundo intento devuelve el resultado de la primera
  operación y no encola de nuevo. Si el trabajo sigue `Received`, puede
  republicar la señal del mismo trabajo durable; si terminó, no repite efectos.
- Permitir al procesador comercial completar una factura ya `DianAccepted`
  solo por este camino de recuperación. Ningún estado fiscal retrocede ni
  despierta `SendBillSync` de nuevo.
- La corrección fiscal es idempotente por factura duplicada y no crea asientos
  o movimientos de inventario. Si se descubre un efecto económico previo,
  detenerla y conciliar ese efecto antes de generar la nota.
- Antes de operar en producción, probar con dos facturas aceptadas del mismo
  pedido y una factura aceptada de borrador, confirmar el asiento balanceado,
  una sola cuenta por cobrar y un solo descuento de inventario, nota crédito
  con referencia correcta, reintentos sin efectos nuevos y cero retransmisión
  de las facturas originales. Publicar primero en DEV desde `origin/main`.
