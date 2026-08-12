# Reconciliación automática de clientes externos

Fecha: 2026-08-02.

## Decisión definitiva

La creación o actualización reconciliable de ExternalCommerceCustomers produce
un evento durable en la misma transacción de SQL Server. El evento se publica a
un broker y Auraly lo consume para crear o reutilizar Party y Customer.

No se usan sondeos, triggers SQL, timers ni llamadas directas del adaptador al
módulo Parties. La API administrativa continúa disponible para revisar
conflictos y ejecutar reintentos humanos, pero dejó de ser necesaria para el
flujo normal.

## Flujo de extremo a extremo

    Xion/Mantis/ProductCatalogSync
      -> ExternalCommerceCustomerRepository
      -> ExternalCommerceCustomers + ExternalCustomerReconciliationOutboxMessages
         (un solo SaveChanges)
      -> señal local posterior al commit
      -> dispatcher SQL con lease
      -> Azure Service Bus o RabbitMQ
      -> consumidor canónico Auraly
      -> transacción serializable de Party/Customer
      -> ExternalCustomerReconciliationReceipts
      -> outbox Customers
      -> notificación POS existente

Todos los productores actuales atraviesan el mismo repositorio. Por esto la
regla no está duplicada dentro de cada adaptador.

## Outbox del productor

ExternalCustomerReconciliationOutboxMessages conserva:

- MessageId UUIDv7;
- cliente externo y Business;
- instante original;
- disponibilidad;
- publicación;
- intentos y último error;
- lease y vencimiento;
- rowversion.

Existe un único mensaje no publicado por cliente externo. Una actualización
Pending o Conflict coalesce con el pendiente existente. Un registro Linked no
vuelve a enlazarse automáticamente: la información canónica de Party no se
sobrescribe con posteriores datos incompletos del origen.

El UnitOfWork despierta el dispatcher únicamente después de confirmar la
transacción. En rollback consume el estado local sin emitir señal.

El dispatcher se activa:

1. al iniciar el host, para recuperar mensajes confirmados antes de una caída;
2. después de un commit con mensajes nuevos;
3. al vencer el backoff de un trabajo pendiente ya conocido.

El tercer caso no es polling: la fecha proviene de una fila que el dispatcher
acaba de reclamar o liberar. No existe consulta periódica cuando no hay trabajo.

La reclamación usa UPDLOCK, READPAST, ROWLOCK y lease. Un crash después de
publicar y antes de marcar PublishedAt puede publicar nuevamente; el consumidor
está diseñado para admitirlo sin duplicar efectos.

## Contrato y transporte

El contrato canónico es ExternalCustomerReconciliationSignal:

- MessageId;
- ExternalCommerceCustomerId;
- BusinessId;
- OccurredAt.

El envelope debe repetir MessageId y Business. El consumidor rechaza diferencias
entre envelope y payload.

SaaS puede usar Azure Service Bus. La cola debe tener sesiones habilitadas; la
sesión es el BusinessId.

On-premise puede usar RabbitMQ. Los mensajes son persistentes, el canal usa
confirmaciones del publicador, el consumidor usa prefetch 1, ack explícito y una
cola durable .dead.

No existe fallback en memoria ni fallback por polling. Una configuración sin
credenciales válidas falla al iniciar.

## Consumo e idempotencia

ExternalCustomerReconciliationReceipts tiene MessageId como clave primaria. El
consumidor:

1. valida contrato y envelope;
2. comprueba el recibo;
3. resuelve Tenant desde el Business y el registro externo;
4. ejecuta la misma conciliación serializable usada por la API;
5. guarda el recibo;
6. confirma el mensaje.

La transacción bloquea el registro externo. Si dos entregas concurrentes alcanzan
el servidor, una crea el vínculo y la otra observa Linked. Solo existe una Party,
un Customer y una invalidación POS.

Si hay una caída entre conciliación y recibo, la siguiente entrega observa
Linked, registra el recibo faltante y no repite efectos.

Los conflictos de identidad son resultados de negocio exitosos: quedan Conflict,
se registra el recibo y el mensaje se confirma. Los fallos técnicos se intentan
cinco veces y terminan en dead-letter.

## Auditoría de actor

Una conciliación manual conserva el usuario en ReconciledBy y el origen Manual.

Una integración no se atribuye falsamente a un usuario. CreatedBy de Party y
Customer admite nulo para procesos técnicos y el registro externo conserva
ReconciliationOrigin Integration, proveedor, cuenta, Business e instante. Las
creaciones manuales continúan enviando un usuario real.

## Componentes conectados

Productor:

- ExternalCommerceCustomerRepository;
- UnitOfWork;
- SqlExternalCustomerReconciliationOutboxDispatcher;
- publicadores Service Bus y RabbitMQ;
- hosted service activado por commit y startup.

Consumidor:

- hosted services Service Bus y RabbitMQ en Auraly.Api;
- ExternalCustomerReconciliationSystemService;
- SqlExternalCustomerReconciliationStore;
- recibo SQL e invalidación POS.

La prueba E2E usa exactamente repositorio, outbox, dispatcher, RabbitMQ,
consumidor, SQL Server, Party, Customer y recibo. No sustituye SQL Server por EF
InMemory.

## Límites deliberados

- Los registros ya Linked no actualizan automáticamente Party.
- La retención de outbox y recibos se definirá con la política operativa global;
  no se agregó un borrado ad hoc.
- La vista administrativa sigue resolviendo conflictos, no monitorea el broker.
