# Evidencia de reconciliación de clientes externos

Fecha: 2026-08-02.
Rama: `feature/auraly-commerce-customer-reconciliation`.

## Resultado conectado

La rebanada conecta la procedencia externa, SQL Server, API, permisos,
administración web y sincronización POS:

1. el registro externo permanece como evidencia de origen;
2. la conciliación crea o reutiliza Party por teléfono normalizado;
3. Customer se crea o reutiliza para el Business autenticado;
4. datos ausentes permanecen nulos y la Party nueva queda incompleta;
5. un teléfono ambiguo produce `Conflict` sin enlace silencioso;
6. corregir la ambigüedad permite un reintento explícito;
7. solicitudes concurrentes y repetidas son idempotentes;
8. el enlace escribe una invalidación durable `Customers`;
9. la administración ofrece consulta paginada, filtros, acción individual y lote
   de pendientes;
10. los permisos de lectura y conciliación se validan en el backend.

## Evidencia automatizada

```text
dotnet build Auraly.Commerce.sln --configuration Release --disable-build-servers
aprobado: 0 errores, 0 advertencias
```

```text
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release --no-build
149 aprobadas, 0 fallidas
```

```text
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release --no-build
16 aprobadas, 0 fallidas
```

```text
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --no-build
93 aprobadas, 0 fallidas; SQL Server real y DACPAC desplegado
```

```text
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release --disable-build-servers
aprobado: 0 errores, 0 advertencias
```

```text
cd admin
npx tsc --noEmit
aprobado

npm run test:pos
25 aprobadas, 0 fallidas

npm run build
aprobado; 54 páginas estáticas, incluida /dashboard/parties/imports
```

Las dos pruebas nuevas de integración verifican además:

- dos conciliaciones concurrentes producen una sola Party y un solo Customer;
- un segundo origen con el mismo teléfono reutiliza ambos;
- no se fabrica identificación legal;
- proveedor, cuenta externa y Business no atraviesan límites autenticados;
- lectura y conciliación requieren permisos independientes;
- un teléfono enlazado a dos Parties queda en conflicto;
- desactivar el contacto duplicado y reintentar enlaza la Party correcta;
- el lote procesa pendientes y no reintenta conflictos sin revisión;
- la outbox `Customers` avanza sin contaminar la unicidad del stream `Catalog`.

## Hallazgo de regresión corregido

Una prueba anterior de publicación de precios contaba mensajes por Business y
cursor, aunque los cursores son independientes por stream. Al coexistir `Catalog`
y `Customers`, ambos podían tener cursor 1. La aserción ahora incluye
`Stream = 'Catalog'`; la funcionalidad de producción no cambió y las 93 pruebas
completas pasan juntas.

## Automatizaci�n posterior

La limitaci�n aqu� registrada fue cerrada por la rebanada de eventos externos.
Consultar:

- docs/implementation/external-customer-events-design.md;
- docs/implementation/external-customer-events-evidence.md;
- docs/implementation/run-external-customer-events.md.