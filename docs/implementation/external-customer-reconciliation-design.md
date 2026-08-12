# Reconciliación de clientes externos con Terceros

Fecha: 2026-08-02.

## Decisión

`ExternalCommerceCustomers` conserva la identidad y procedencia recibidas de cada
integración. No reemplaza ni se mezcla con el cliente canónico del sistema. Una
reconciliación enlaza ese registro con una `Party` del tenant y con un `Customer`
del Business donde se importó.

No se creó una segunda tabla de clientes. Los campos ausentes en el origen siguen
siendo nulos y no se fabrican identificaciones, direcciones ni sedes.

## Regla de identidad

La coincidencia automática usa el teléfono normalizado dentro del tenant:

1. sin coincidencias se crea una Party `NaturalPerson` incompleta, su contacto
   telefónico cuando existe y el rol Customer para el Business;
2. con una coincidencia se reutiliza la Party y se crea o reutiliza Customer;
3. con más de una coincidencia el registro queda en `Conflict` y no se enlaza;
4. después de corregir los datos, un usuario autorizado puede reintentar;
5. un registro ya `Linked` responde idempotentemente sin duplicar entidades.

El identificador de cuenta de la integración no se interpreta como documento
legal porque su semántica depende del proveedor.

La transacción usa aislamiento serializable y bloqueos de actualización para que
dos solicitudes concurrentes no creen dos Parties o Customers.

## Estados

- `Pending`: pendiente de conciliación.
- `Linked`: enlazado a Party y Customer.
- `Conflict`: requiere revisión humana.

El procesamiento masivo toma solo registros `Pending`. Los conflictos se
reintentan individualmente para evitar ciclos automáticos sin intervención.

## Flujo conectado

```text
Integración externa
  -> ExternalCommerceCustomers (procedencia durable, Pending)
  -> administración /dashboard/parties/imports
  -> API autenticada y autorizada
  -> transacción SQL Server
  -> Party + Customer o Conflict
  -> outbox Customers
  -> notificación al POS
  -> descarga incremental por cursor
```

La outbox se escribe en la misma transacción del enlace. No se agregó polling,
trigger SQL ni un catálogo paralelo. La notificación solo avisa que existe un
cambio; los dispositivos obtienen el delta por el protocolo existente.

## API y permisos

- `GET /api/commerce/v1/external-customers`
- `POST /api/commerce/v1/external-customers/{id}/reconcile`
- `POST /api/commerce/v1/external-customers/reconcile-pending`

La consulta ofrece paginación del servidor y filtros por texto, estado e
integración. Todos los endpoints validan Tenant y Business desde la identidad
autenticada.

Permisos:

- `parties.external-customers.read`
- `parties.external-customers.reconcile`

## Automatizaci�n posterior

La automatizaci�n del productor qued� implementada en la rebanada siguiente.
El dise�o vigente est� en
docs/implementation/external-customer-events-design.md.

Los adaptadores escriben una outbox en la misma transacci�n del registro externo,
el dispatcher publica por Service Bus o RabbitMQ y Auraly consume con recibos
idempotentes. No existe polling ni trigger SQL. La vista y API de esta rebanada
permanecen como herramientas de revisi�n y resoluci�n de conflictos.