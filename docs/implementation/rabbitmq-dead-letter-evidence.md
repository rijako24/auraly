# Evidencia: fallo real, cinco intentos y dead-letter

**Fecha:** 1 de agosto de 2026  
**Broker:** `rabbitmq:4.1-management`  
**Persistencia:** SQL Server desplegado desde `Auraly.Database.dacpac`

## Escenario ejecutado

La prueba `RabbitMqDocumentProcessingTests` confirmó dos entradas de mercancía correctas y verificó su orden e idempotencia. Después confirmó otras dos entradas consecutivas:

1. a la primera se le sustituyó el payload por JSON válido pero semánticamente incompleto (`{}`);
2. ambas se publicaron en RabbitMQ en su orden real;
3. el primer manejador falló dentro de la transacción productiva;
4. RabbitMQ conservó el turno y Auraly ejecutó cinco intentos;
5. SQL guardó `DeadLettered`, cinco intentos y el error;
6. no se creó inventario ni cuenta por pagar para el documento fallido;
7. el mensaje terminó en la dead-letter con el mismo `MovementId`;
8. la entrada siguiente creó una sola entrada de inventario y una sola cuenta por pagar.

La prueba usa el consumidor productivo `RabbitMqDocumentProcessingHostedService`, no un consumidor simulado. El usuario del broker y las colas de prueba se crean con nombres efímeros y se eliminan al finalizar.

## Resultado

```text
Passed: 1
Failed: 0
Build Release: 0 errores, 0 advertencias
```

El escenario también descubrió y corrigió que `AttemptCount` se incrementaba originalmente dentro de la transacción que el fallo revertía. La implementación actual persiste cada fallo en una transacción separada y serializable.

## Referencias oficiales

- RabbitMQ .NET client API guide: https://www.rabbitmq.com/client-libraries/dotnet-api-guide
- RabbitMQ consumer acknowledgements and publisher confirms: https://www.rabbitmq.com/docs/confirms
- RabbitMQ dead-letter exchanges: https://www.rabbitmq.com/docs/dlx
