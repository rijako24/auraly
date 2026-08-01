# Decisión final: reintentos y dead-letter del motor documental

**Fecha:** 1 de agosto de 2026  
**Estado:** decisión vigente  
**Prevalencia:** reemplaza cualquier regla anterior que deje un movimiento bloqueado indefinidamente en `NeedsIntervention` después de agotar sus reintentos.

## Regla cerrada

Cada documento confirmado crea exactamente un `DocumentProcessingJob` y publica exactamente un mensaje cuyo `MessageId` es el `MovementId`.

El consumidor documental procesa en orden y con una sola entrega activa:

- RabbitMQ on-premise: un consumidor y `prefetch = 1`;
- Azure Service Bus SaaS: una llamada concurrente por sesión de negocio;
- confirmación manual solamente después del commit completo;
- máximo cinco intentos;
- sin polling SQL;
- sin colas TTL para documentos.

Mientras un movimiento conserva intentos, mantiene su posición y el movimiento siguiente del mismo negocio no se ejecuta. Cada fallo revierte todos los efectos del documento y persiste el intento fuera de la transacción revertida.

Al quinto fallo:

1. el trabajo SQL queda `DeadLettered`;
2. conserva `AttemptCount = 5`, `LastError` y trazabilidad;
3. no conserva efectos parciales de inventario, cartera, caja, contabilidad ni reportes;
4. libera su posición ordenada;
5. el broker mueve el mismo mensaje a su dead-letter durable;
6. el consumidor continúa con la siguiente entrega.

`DeadLettered` no significa procesado ni exitoso. Un duplicado de ese mensaje no puede reaplicar efectos. Su corrección exige una operación administrativa explícita y auditada que produzca un nuevo movimiento; nunca se modifica silenciosamente el documento fallido.

## Consistencia SQL

El incremento realizado al adquirir un lease pertenece inicialmente a la transacción de efectos. Como esa transacción se revierte al fallar, `MarkFailedAsync` incrementa y persiste el intento en una transacción independiente y serializable. De esta forma, el rollback del documento no borra la evidencia del intento.

La transición final a `DeadLettered` y el avance de `BusinessProcessingCursors.LastCompletedSequence` ocurren en la misma transacción. No existe una ventana en la que SQL libere el orden sin haber conservado el fallo final.

## Prueba obligatoria

La regresión debe usar RabbitMQ y SQL Server reales y demostrar:

- un payload JSON válido pero semánticamente inválido;
- cinco intentos exactos;
- rollback total en cada intento;
- cero movimientos de inventario y cero efectos financieros parciales;
- mensaje final en dead-letter con el mismo `MovementId`;
- siguiente documento procesado exactamente una vez.
