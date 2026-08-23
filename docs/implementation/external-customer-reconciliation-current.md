# Conciliación vigente de clientes externos

**Estado:** obligatorio desde 2026-08-22.

La sincronización explícita de clientes guarda la identidad de origen en
`ExternalCommerceCustomers` y, antes de terminar, ejecuta directamente la
conciliación de todos los registros `Pending` del negocio. No existe cola,
outbox, recibo ni worker específico para clientes externos.

La conciliación serializable crea o reutiliza `Party` por teléfono normalizado,
crea o reutiliza su rol `Customer` en el negocio y deja en `Conflict` cualquier
identidad ambigua. La API manual conserva las operaciones individual y por lote
para revisión humana.

Durante una conversación el bot no consulta ni descarga clientes desde el ERP.
El lookup usa `Customers`, `Parties` y `PartyContacts`; después une solamente el
mapeo `ExternalCommerceCustomers` que ya esté `Linked` para obtener las claves
externas necesarias al crear el pedido. Cero o varias coincidencias producen un
resultado no resuelto y nunca una elección silenciosa.

Protecciones reproducibles:

- la infraestructura no declara `auraly-external-customer-reconciliation`;
- la composición no registra publishers ni hosted services de conciliación;
- el DACPAC no incluye tablas de outbox o recibos de esta función;
- una prueba de arquitectura protege las tres reglas anteriores y el lookup
  canónico;
- las pruebas de integración de la API conservan permisos, aislamiento,
  idempotencia y conflictos.
