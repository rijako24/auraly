# Evidencia parcial — Party, Customer y vínculo de usuario

Fecha: 2026-07-29
Rama: `feature/auraly-commerce-parties`

## Decisiones aplicadas

- `Party` es la identidad canónica compartida.
- `Customer` es un rol de `Party` limitado a un `Business`.
- `UserAccount` conserva autenticación, roles y permisos en Security y se enlaza
  opcionalmente uno a uno con `Party`.
- Una misma identidad se reconoce por país, tipo y documento normalizado y puede
  tener varias sedes.
- El barrio es texto libre; país, división administrativa y ciudad son maestros.
- La configuración comercial del cliente permite una lista o un canal, nunca
  ambos.
- El POS puede crear clientes mediante identidad de dispositivo, pero no asignar
  listas ni canales.
- La venta guarda `CustomerId` como referencia interna fuera del snapshot.
- El snapshot no copia toda la Party. Conserva solamente los datos usados por la
  factura y el UBL: identidad fiscal, nombre fiscal, responsabilidades, contacto
  requerido y dirección geográfica mínima exigida. No conserva roles, permisos,
  otras sedes, listas, canales ni historial administrativo.

## Flujo conectado

```text
Admin o POS autenticado
  -> API Party/Customer
  -> validación de permiso y Business
  -> Party normalizada
  -> Customer
  -> sede geográfica
  -> configuración lista/canal
  -> SQL Server

Borrador POS con CustomerId
  -> emisión durable
  -> outbox SQLite
  -> API de ventas
  -> validación Customer/Business/Tenant
  -> SalesDocuments.CustomerId
  -> FiscalSnapshots.SnapshotJson inmutable
```

La repetición de una creación de cliente o sede usa recibos durables por
`BusinessId + OperationId`; no crea otra identidad ni otra sede.

## Evidencia ejecutada

```powershell
dotnet build Auraly.Commerce.sln --configuration Release --maxcpucount:1 -nodeReuse:false
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release --maxcpucount:1 -nodeReuse:false --no-build
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release --maxcpucount:1 -nodeReuse:false --no-build
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --maxcpucount:1 -nodeReuse:false --no-build
```

Resultados:

- solución y DACPAC: 0 errores, 0 advertencias;
- fundación: 97/97;
- host POS: 3/3;
- integración con SQL Server real y DACPAC desplegado: 27/27.

Escenarios nuevos comprobados:

- permiso denegado para administrar geografía;
- alta de país, división y ciudad;
- alta de cliente con lista de precio;
- documento con puntuación encontrado mediante otra presentación normalizada;
- reintento no duplica Party ni Customer;
- segunda sede y reintento de sede sin duplicados;
- lista y canal simultáneos rechazados;
- asignación de precio sin permiso rechazada;
- caja sin permiso rechazada;
- creación rápida por caja autenticada;
- caja impedida de asignar lista o canal;
- vínculo Party–UserAccount idempotente y limitado al tenant;
- una cuenta o Party no puede enlazarse dos veces;
- desvincular no elimina la cuenta ni sus roles;
- CustomerId válido llega de SQLite/contrato a SalesDocuments;
- cliente de otro Business produce 403 y no persiste la venta;
- cambios posteriores en Party no alteran el snapshot emitido.

## Pendiente antes de declarar completa la rebanada

- reconciliación automática de `ExternalCommerceCustomers` hacia Party/Customer;
- edición y desactivación de Party, Customer, contactos y sedes;
- creación administrativa de usuarios enlazados y migración controlada de las
  columnas personales transitorias de `AppUsers`;
- experiencia web de Terceros y centro de Maestros;
- selector/alta de cliente dentro de la interfaz POS;
- definir y probar explícitamente el comportamiento de alta de cliente cuando la
  caja está sin conexión;
- pruebas E2E visuales y de accesibilidad;
- documentación operativa para ejecutar esta rebanada.

Por esos pendientes este documento registra un hito verificable, no la
finalización de toda la rebanada Party/Customer.
