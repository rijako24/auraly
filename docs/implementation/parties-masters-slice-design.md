# Auraly Commerce: rebanada de Terceros y Maestros

Fecha: 2026-08-02.

## Resultado de diseño

`Party` es la identidad canónica de una persona u organización dentro del tenant.
`Customer` y `Supplier` son roles por `Business`; una misma Party puede tener ambos
sin duplicar identificación, contactos ni sedes. `UserAccount` permanece separado
porque representa credenciales, sesiones, roles y permisos, aunque puede enlazarse
opcionalmente con una Party.

El `BusinessId` de un cliente indica su relación comercial y configuración de
precios, no una restricción para comprar. Si la persona compra en otro Business, se
reutiliza la Party y se crea o reutiliza el rol correspondiente. La venta nunca se
bloquea por no existir una relación previa.

## Flujo conectado

```text
Admin autenticado
  -> Terceros
  -> API paginada y autorizada
  -> Party + Customer/Supplier
  -> SQL Server
  -> outbox de sincronización cuando cambia un Customer
  -> notificación al dispositivo
  -> descarga incremental del catálogo POS
  -> SQLite local

POS conectado
  -> alta rápida de cliente
  -> API autenticada como dispositivo
  -> Party + Customer + sede
  -> descarga inmediata de la proyección de clientes/precios
  -> cliente disponible para la venta actual
```

No se agregó sondeo. La outbox durable evita perder cambios y el transporte de
notificaciones existente solo avisa que hay una versión nueva; POS Edge descarga
el delta por la API canónica.

## Modelo de datos

- `Parties`: identidad legal o personal normalizada por tenant.
- `PartyIdentifications`: documento normalizado y dígito de verificación.
- `PartyContacts`: teléfono y correo reutilizables.
- `PartySites`: múltiples sedes/direcciones; país, división y ciudad como llaves;
  barrio como texto libre.
- `Customers`: rol y configuración comercial por Business.
- `Suppliers`: rol por Business enlazado mediante `PartyId`.
- `SupplierCreationReceipts`: idempotencia durable de altas administrativas.
- `PosSynchronizationOutboxMessages`: invalidación durable del stream `Customers`.

La migración de proveedores es aditiva: conserva temporalmente las columnas
anteriores que aún tienen consumidores y crea una Party incompleta para cada fila
existente. Desde esta rebanada, `PartyId` es la identidad canónica de todo proveedor
nuevo. El retiro físico de columnas antiguas se hará solo cuando las rebanadas de
compras, cartera y reportes hayan dejado de leerlas.

## API administrativa

- `GET /api/commerce/v1/parties`: paginación, ordenamiento implícito estable y
  filtros combinables por texto, rol, estado y datos incompletos.
- `PUT /api/commerce/v1/parties/{partyId}`: edición concurrente mediante rowversion.
- `POST /api/commerce/v1/parties/{partyId}/status`: activa o desactiva los roles
  del Business autenticado sin eliminar la identidad.
- `POST /api/commerce/v1/suppliers`: agrega Supplier a una Party existente o crea
  una nueva Party; la operación es idempotente.

Permisos separados controlan lectura, actualización, desactivación, alta de
proveedores y las capacidades existentes de clientes y geografía. El backend
valida siempre el Business autenticado.

## Experiencia web

El menú agrega dos superficies, sin multiplicar CRUD simples:

- **Terceros**: lista unificada, búsqueda, filtros, paginación, alta de cliente o
  proveedor, edición y cambio de estado.
- **Maestros**: centro compacto de geografía con países, departamentos/estados y
  ciudades dependientes. El barrio se escribe libremente al crear la sede.

El formulario de cliente usa dropdowns dependientes de país, división y ciudad.
La clasificación de productos no se implementa como CRUD aislado: se entregará
con el editor de producto que la consuma.

## POS Edge

La búsqueda offline continúa usando el catálogo SQLite ya sincronizado. La
creación de un cliente requiere conexión porque debe resolver identidad global y
evitar duplicados. Tras crear, POS Edge descarga la proyección actualizada antes
de devolver el cliente a facturación. Reiniciar el proceso conserva las facturas,
series y outbox existentes porque no se reemplaza la base local.

## Alcance pendiente deliberado

- reconciliación de `ExternalCommerceCustomers` hacia Party/Customer;
- roles Employee, Seller, Carrier y Driver con sus consumidores reales;
- administración visual de múltiples sedes y contactos secundarios;
- edición/desactivación de geografía y paquetes normativos oficiales;
- árbol de clasificación desde el editor de productos;
- retiro final de columnas de proveedor anteriores cuando no tengan consumidores.

Estos puntos no tienen contratos o interfaces vacías en esta rebanada.
