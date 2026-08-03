# Reconciliaci�n autom�tica de clientes externos

Fecha: 2026-08-02.

## Decisi�n definitiva

La creaci�n o actualizaci�n reconciliable de ExternalCommerceCustomers produce
un evento durable en la misma transacci�n de SQL Server. El evento se publica a
un broker y Auraly lo consume para crear o reutilizar Party y Customer.

No se usan sondeos, triggers SQL, timers ni llamadas directas del adaptador al
m�dulo Parties. La API administrativa contin�a disponible para revisar
conflictos y ejecutar reintentos humanos, pero dej� de ser necesaria para el
flujo normal.

## Flujo de extremo a extremo

    Xion/Mantis/ProductCatalogSync
      -> ExternalCommerceCustomerRepository
      -> ExternalCommerceCustomers + ExternalCustomerReconciliationOutboxMessages
         (un solo SaveChanges)
      -> se�al local posterior al commit
      -> dispatcher SQL con lease
      -> Azure Service Bus o RabbitMQ
      -> consumidor can�nico Auraly
      -> transacci�n serializable de Party/Customer
      -> ExternalCustomerReconciliationReceipts
      -> outbox Customers
      -> notificaci�n POS existente

Todos los productores actuales atraviesan el mismo repositorio. Por esto la
regla no est� duplicada dentro de cada adaptador.

## Outbox del productor

ExternalCustomerReconciliationOutboxMessages conserva:

- MessageId UUIDv7;
- cliente externo y Business;
- instante original;
- disponibilidad;
- publicaci�n;
- intentos y �ltimo error;
- lease y vencimiento;
- rowversion.

Existe un �nico mensaje no publicado por cliente externo. Una actualizaci�n
Pending o Conflict coalesce con el pendiente existente. Un registro Linked no
vuelve a enlazarse autom�ticamente: la informaci�n can�nica de Party no se
sobrescribe con posteriores datos incompletos del origen.

El UnitOfWork despierta el dispatcher �nicamente despu�s de confirmar la
transacci�n. En rollback consume el estado local sin emitir se�al.

El dispatcher se activa:

1. al iniciar el host, para recuperar mensajes confirmados antes de una ca�da;
2. despu�s de un commit con mensajes nuevos;
3. al vencer el backoff de un trabajo pendiente ya conocido.

El tercer caso no es polling: la fecha proviene de una fila que el dispatcher
acaba de reclamar o liberar. No existe consulta peri�dica cuando no hay trabajo.

La reclamaci�n usa UPDLOCK, READPAST, ROWLOCK y lease. Un crash despu�s de
publicar y antes de marcar PublishedAt puede publicar nuevamente; el consumidor
est� dise�ado para admitirlo sin duplicar efectos.

## Contrato y transporte

El contrato can�nico es ExternalCustomerReconciliationSignal:

- MessageId;
- ExternalCommerceCustomerId;
- BusinessId;
- OccurredAt.

El envelope debe repetir MessageId y Business. El consumidor rechaza diferencias
entre envelope y payload.

SaaS puede usar Azure Service Bus. La cola debe tener sesiones habilitadas; la
sesi�n es el BusinessId.

On-premise puede usar RabbitMQ. Los mensajes son persistentes, el canal usa
confirmaciones del publicador, el consumidor usa prefetch 1, ack expl�cito y una
cola durable .dead.

No existe fallback en memoria ni fallback por polling. Una configuraci�n sin
credenciales v�lidas falla al iniciar.

## Consumo e idempotencia

ExternalCustomerReconciliationReceipts tiene MessageId como clave primaria. El
consumidor:

1. valida contrato y envelope;
2. comprueba el recibo;
3. resuelve Tenant desde el Business y el registro externo;
4. ejecuta la misma conciliaci�n serializable usada por la API;
5. guarda el recibo;
6. confirma el mensaje.

La transacci�n bloquea el registro externo. Si dos entregas concurrentes alcanzan
el servidor, una crea el v�nculo y la otra observa Linked. Solo existe una Party,
un Customer y una invalidaci�n POS.

Si hay una ca�da entre conciliaci�n y recibo, la siguiente entrega observa
Linked, registra el recibo faltante y no repite efectos.

Los conflictos de identidad son resultados de negocio exitosos: quedan Conflict,
se registra el recibo y el mensaje se confirma. Los fallos t�cnicos se intentan
cinco veces y terminan en dead-letter.

## Auditor�a de actor

Una conciliaci�n manual conserva el usuario en ReconciledBy y el origen Manual.

Una integraci�n no se atribuye falsamente a un usuario. CreatedBy de Party y
Customer admite nulo para procesos t�cnicos y el registro externo conserva
ReconciliationOrigin Integration, proveedor, cuenta, Business e instante. Las
creaciones manuales contin�an enviando un usuario real.

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
- recibo SQL e invalidaci�n POS.

La prueba E2E usa exactamente repositorio, outbox, dispatcher, RabbitMQ,
consumidor, SQL Server, Party, Customer y recibo. No sustituye SQL Server por EF
InMemory.

## L�mites deliberados

- Los registros ya Linked no actualizan autom�ticamente Party.
- La retenci�n de outbox y recibos se definir� con la pol�tica operativa global;
  no se agreg� un borrado ad hoc.
- La vista administrativa sigue resolviendo conflictos, no monitorea el broker.
