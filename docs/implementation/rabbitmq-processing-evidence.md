# Evidencia: transporte RabbitMQ del motor documental

Fecha: 1 de agosto de 2026  
Rama: `feature/auraly-commerce-accounting-engine`

## Resultado

El transporte on-premise ya no es una intención de arquitectura. `Auraly.Api`
puede seleccionar `RabbitMq` o `ServiceBus` mediante configuración y ambos
implementan los mismos contratos públicos del motor documental y fiscal.

El procesamiento documental con RabbitMQ usa:

- cola principal durable;
- mensajes persistentes;
- confirmación del publicador con seguimiento de `basic.return`;
- `MessageId = MovementId`;
- `businessId` y `documentId` en encabezados validados;
- un consumidor documental;
- `prefetch = 1`;
- confirmación manual únicamente después de completar SQL y publicar el trabajo
  fiscal que corresponda;
- cinco intentos sobre la misma entrega, sin liberar el turno;
- dead-letter durable al agotar los intentos;
- ninguna consulta, temporizador o sondeo SQL dentro del transporte.

La cola documental crea solo la cola principal y su dead-letter. Las colas TTL
se reservan para el carril fiscal, donde existe una fecha explícita de reintento.

## E2E real ejecutado

El contenedor local fue `rabbitmq:4.1-management`. La prueba creó una base SQL
Server aislada desplegando `Auraly.Database.dacpac`, pausó el publicador de
pruebas y confirmó dos entradas de mercancía reales. Después publicó sus dos
`MovementId` a RabbitMQ y arrancó el consumidor productivo.

Se comprobó:

1. los dos mensajes permanecieron en la cola sin consumidor;
2. se procesaron en el orden de `ProcessingSequence`;
3. cada entrada creó una sola entrada de inventario;
4. cada entrada creó una sola cuenta por pagar;
5. volver a publicar el primer `MovementId` no duplicó efectos;
6. un movimiento inexistente consumió cinco intentos y terminó en dead-letter;
7. las colas temporales y el usuario efímero fueron eliminados al finalizar.

Comando ejecutado:

```powershell
$env:AURALY_TEST_RABBITMQ = 'amqp://<test-user>:<test-password>@127.0.0.1:5672/'
$env:AURALY_REQUIRE_RABBITMQ_TEST = '1'
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj `
  --configuration Release --no-build `
  --filter FullyQualifiedName~RabbitMqDocumentProcessingTests
```

Resultado final:

```text
Passed: 1
Failed: 0
Rabbit E2E body: 13 s
Total including DACPAC deployment: 3.87 min
```

La prueba externa solo se ejecuta cuando `AURALY_TEST_RABBITMQ` está definida. Para una ejecución explícita se debe definir también `AURALY_REQUIRE_RABBITMQ_TEST=1`; así la prueba falla si la conexión no llegó al proceso y no puede reportar una omisión como aprobada.

Sin modo obligatorio,
la suite normal no inventa un broker. La prueba arquitectónica siempre comprueba
la persistencia, publisher confirms, ack manual, `prefetch=1`, dead-letter y
ausencia de polling.

## Configuración

```text
Auraly__Processing__Transport=RabbitMq
Auraly__Processing__RabbitMq__ConnectionString=amqp://...
Auraly__DocumentProcessing__RabbitMq__QueueName=auraly-document-processing
Auraly__Fiscal__RabbitMq__QueueName=auraly-fiscal-processing
```

La cadena de conexión se suministra por variable de entorno o proveedor de
secretos. `appsettings.json` no contiene credenciales.

## Referencias técnicas

- RabbitMQ .NET client API guide: https://www.rabbitmq.com/client-libraries/dotnet-api-guide
- RabbitMQ publisher confirms: https://www.rabbitmq.com/tutorials/tutorial-seven-dotnet
- Paquete oficial utilizado: https://www.nuget.org/packages/RabbitMQ.Client/7.2.1

