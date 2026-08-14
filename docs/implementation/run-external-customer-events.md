# Ejecutar la reconciliación automática de clientes externos

Fecha: 2026-08-02.

## Requisitos

- SQL Server local disponible en .\LOCAL o AURALY_TEST_SQLSERVER.
- DACPAC compilado.
- RabbitMQ para la prueba on-premise, o Azure Service Bus con cola de sesiones
  para SaaS.

## RabbitMQ local

Configuración de ejemplo:

    Auraly__Processing__Transport=RabbitMq
    Auraly__Processing__RabbitMq__ConnectionString=amqp://usuario:clave@localhost:5672
    Auraly__ExternalCustomerReconciliation__QueueName=auraly-external-customer-reconciliation

El productor también acepta una conexión dedicada en:

    Auraly__ExternalCustomerReconciliation__RabbitMq__ConnectionString

Si no existe, reutiliza la conexión RabbitMQ del motor de procesamiento.

## Azure Service Bus

    Auraly__Processing__Transport=ServiceBus
    ServiceBusConnection=<connection string del productor>
    Auraly__DocumentProcessing__ServiceBus__ConnectionString=<connection string del consumidor>
    Auraly__ExternalCustomerReconciliation__QueueName=auraly-external-customer-reconciliation

La cola de reconciliación debe tener sesiones habilitadas. El BusinessId es el
SessionId.

## Compilar

    dotnet build Auraly.Commerce.sln --configuration Release --disable-build-servers
    dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release --disable-build-servers
    dotnet build Auraly.Commerce.sln --configuration Release --disable-build-servers

## Pruebas automáticas

Productor y señal post-commit:

    dotnet test src/Tests/Auraly.Platform.Tests/Auraly.Platform.Tests.csproj --configuration Release --filter FullyQualifiedName~ExternalCustomerReconciliation

Productor completo con SQL Server y RabbitMQ reales:

    $env:AURALY_TEST_RABBITMQ='amqp://usuario:clave@localhost:5672'
    $env:AURALY_REQUIRE_RABBITMQ_TEST='1'
    dotnet test src/Tests/Auraly.Platform.Tests/Auraly.Platform.Tests.csproj --configuration Release --filter FullyQualifiedName~ExternalCustomerReconciliationSqlOutboxIntegrationTests
SQL de idempotencia y concurrencia:

    dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~ExternalCustomerReconciliationEventTests

E2E RabbitMQ real:

    $env:AURALY_TEST_RABBITMQ='amqp://usuario:clave@localhost:5672'
    $env:AURALY_REQUIRE_RABBITMQ_TEST='1'
    dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~ExternalCustomerReconciliationRabbitMqTests

La prueba crea colas con nombre único y las elimina al terminar. La base SQL de
prueba también es aislada y desplegada desde el DACPAC.

## Inspección SQL

Pendientes del productor:

    SELECT MessageId, ExternalCommerceCustomerId, BusinessId, AttemptCount,
           AvailableAt, PublishedAt, LastError
    FROM dbo.ExternalCustomerReconciliationOutboxMessages
    WHERE PublishedAt IS NULL
    ORDER BY OccurredAt, MessageId;

Recibos del consumidor:

    SELECT MessageId, ExternalCommerceCustomerId, BusinessId, ResultStatus, ProcessedAt
    FROM dbo.ExternalCustomerReconciliationReceipts
    ORDER BY ProcessedAt DESC;

Conflictos de negocio:

    SELECT ExternalCommerceCustomerId, BusinessId, ReconciliationError, ReconciledAt
    FROM dbo.ExternalCommerceCustomers
    WHERE ReconciliationStatus = N'Conflict';

Un conflicto se revisa en /dashboard/parties/imports. Un mensaje en .dead es un
fallo técnico y debe investigarse antes de republicarlo.
