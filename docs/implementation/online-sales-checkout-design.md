# Emisión durable de ventas online

Fecha: 2026-07-30

## Decisión de contexto

Una venta web siempre pertenece a una caja real. `RegisterId` es obligatorio en
modo online y determina:

- negocio y sede;
- bodega;
- serie operativa Auraly;
- serie fiscal DIAN;
- resolución o autorización vigente;
- cursores de ambos consecutivos;
- sesión de caja y responsabilidad por cajero.

El campo que no existe en una venta web es `DeviceId`, porque un navegador no se
hace pasar por un equipo POS Edge enrolado. El documento conserva
`SourceMode=Online`, `RegisterId` y `SoldByUserId`.

## Flujo transaccional

`POST /api/commerce/v1/pos/drafts/{draftId}/complete` recibe la versión esperada,
los medios de pago y `Idempotency-Key`.

Dentro de una transacción serializable, el servidor:

1. bloquea el borrador del usuario;
2. valida que siga activo y tenga la versión esperada;
3. valida que los pagos cubran exactamente el total;
4. resuelve las series operativa y fiscal activas de la caja;
5. consume atómicamente ambos consecutivos;
6. congela el snapshot comercial, fiscal y UBL;
7. calcula CUFE y QR con la clave técnica de la resolución;
8. cambia el borrador a `Issuing`;
9. crea el siguiente borrador activo vacío;
10. persiste el payload exacto y el recibo idempotente.

Después, el mismo motor usado por POS Edge:

- recalcula y verifica el CUFE;
- persiste el documento recibido;
- crea líneas y resúmenes agrupados por impuesto y tarifa;
- registra la salida de inventario;
- registra pagos;
- abre o reutiliza la sesión de la caja;
- atribuye la venta al turno del cajero;
- registra movimientos de caja;
- publica el evento mediante outbox;
- completa el recibo de procesamiento.

## Concurrencia e idempotencia

Dos usuarios pueden vender simultáneamente desde computadores diferentes usando
el mismo `RegisterId`. Los cursores pertenecen a las series de la caja y se
bloquean en SQL Server; uno recibe N y el otro N+1.

El payload preparado se conserva antes de invocar el motor. Un reintento exacto:

- devuelve el mismo `DocumentId`;
- conserva número Auraly, número DIAN y CUFE;
- no vuelve a consumir consecutivos;
- no duplica líneas, impuestos, inventario, pagos, caja ni outbox.

Reutilizar la misma venta con otra clave o contenido produce conflicto explícito.
Un medio de pago sin un caso de uso completo —por ejemplo crédito antes de
conectar cuentas por cobrar— se rechaza antes de reservar numeración.

## Persistencia

- `DocumentSeriesCursors`: siguiente consecutivo operativo por serie.
- `FiscalSeriesCursors`: siguiente consecutivo fiscal por serie.
- `OnlineSalesCheckoutReceipts`: payload exacto, hash, clave idempotente,
  siguiente borrador y estado.
- `SalesDocuments`: `RegisterId` obligatorio, `DeviceId` nulo para online y
  `SourceMode` explícito.

El proyecto `Auraly.Database.sqlproj` continúa siendo el único dueño del esquema.

## Evidencia ejecutada

- DACPAC: 0 errores y 0 advertencias.
- Fundación: 109 pruebas correctas.
- Checkout online: 3 pruebas correctas sobre SQL Server real.
- Venta online completa con documento, línea, resumen de impuesto, inventario,
  pago, movimiento de caja, recibo del motor y outbox.
- Reintento exacto sin duplicados.
- Conflicto cuando cambia el contenido.
- Dos cajeros concurrentes en una caja sin colisión de numeración.
- Crédito todavía no conectado a cartera rechazado sin consumir consecutivos.

## Pendiente inmediato

El servidor ya cubre el cierre durable. La siguiente tarea es conectar
`OnlinePosClient` a la pantalla POS, recordar la caja seleccionada, imprimir
desde el recibo confirmado y habilitar búsqueda y reimpresión de documentos del
servidor. El modo online no debe mostrarse como terminado hasta que ese recorrido
visual esté conectado y probado.
