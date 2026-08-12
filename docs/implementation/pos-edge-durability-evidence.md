# Evidencia: durabilidad inicial de Auraly POS Edge

**Fecha:** 27 de julio de 2026

## Alcance implementado

`Auraly.Pos.Edge.Infrastructure` utiliza SQLite real para conectar en una misma
transacción local:

- cursor de serie fiscal exclusivo de la caja;
- consumo de consecutivo;
- confirmación de venta;
- snapshot fiscal y CUFE;
- factura local emitida;
- mensaje durable de outbox.

`DocumentId` tiene restricción única. Repetir la emisión del mismo documento
recupera el número, CUFE y mensaje de outbox ya almacenados en lugar de consumir
otro consecutivo.

La outbox permite consultar pendientes y marcar una carga como realizada de
forma idempotente.

## Prueba ejecutada

`PosEdgeDurabilityTests.Restart_preserves_sales_outbox_cufe_and_idempotency`:

1. crea una base SQLite física temporal;
2. provisiona una serie fiscal;
3. confirma dos facturas;
4. crea una nueva instancia del almacenamiento, simulando reapertura;
5. vuelve a enviar el primer `DocumentId`;
6. comprueba que no cambia número, CUFE ni `OutboxMessageId`;
7. comprueba que existen exactamente dos mensajes pendientes;
8. marca un mensaje como cargado dos veces;
9. comprueba que queda exactamente el segundo mensaje;
10. cierra los pools SQLite y elimina los archivos temporales.

Resultado conjunto:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
Passed: 23, Failed: 0, Skipped: 0
```

## Límite

Esta evidencia cubre durabilidad local y reintentos dentro de POS Edge. Todavía
no demuestra la recepción por la API, la verificación del CUFE en SQL Server ni
el procesamiento servidor de inventario y pagos. Esos escenarios deben probarse
en la siguiente conexión vertical antes de considerarlos terminados.
