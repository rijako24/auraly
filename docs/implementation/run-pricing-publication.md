# Ejecutar la rebanada de precios y rentabilidad

## Requisitos

- .NET SDK configurado por el repositorio.
- Node y dependencias de admin instaladas.
- SQL Server accesible.
- SqlPackage disponible para desplegar el DACPAC.
- configuraci?n v?lida de autenticaci?n y del transporte push.

No se usan migraciones EF ni EnsureCreated para SQL Server.

## Compilar

Desde la ra?z:

```powershell
dotnet restore Auraly.Commerce.sln
dotnet build Auraly.Commerce.sln --configuration Release --no-restore
dotnet build database\Auraly.Database\Auraly.Database.sqlproj --configuration Release --no-restore
dotnet test tests\Auraly.Foundation.Tests\Auraly.Foundation.Tests.csproj --configuration Release --no-restore
dotnet test tests\Auraly.ServerSlice.IntegrationTests\Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --no-restore
```

Frontend:

```powershell
cd admin
npx tsc --noEmit
npm run build
```

## Desplegar esquema

Generar primero:

```powershell
dotnet build database\Auraly.Database\Auraly.Database.sqlproj --configuration Release --no-restore
```

El artefacto queda en:

```text
database\Auraly.Database\bin\Release\Auraly.Database.dacpac
```

Usar los scripts de publicaci?n existentes del proyecto SQL o SqlPackage con la cadena de conexi?n del entorno. No ejecutar scripts aislados a mano porque se perder?a el modelo declarativo del DACPAC.

## Probar el caso de uso

1. Confirmar una entrada con un costo distinto para un producto.
2. Esperar a que el motor complete el documento.
3. Abrir Productos > Precios y rentabilidad.
4. Filtrar PendingReview.
5. Seleccionar Margen o Precio de venta.
6. Solicitar el c?lculo y revisar precio redondeado y margen efectivo.
7. Guardar la propuesta o publicar.
8. Comprobar la versi?n anterior y la nueva en ProductPrices.
9. Comprobar PricePublicationAudits, CatalogChanges y PosSynchronizationOutboxMessages.
10. Con POS Edge activo, verificar la se?al y el delta aplicado en SQLite.

## Contratos principales

```text
GET  /api/commerce/v1/pricing/proposals
POST /api/commerce/v1/pricing/calculate
PUT  /api/commerce/v1/pricing/proposals/{proposalId}
POST /api/commerce/v1/pricing/proposals/{proposalId}/reject
POST /api/commerce/v1/pricing/publish
GET  /api/commerce/v1/pricing/products/{productId}/history
```

Todas las rutas requieren JWT y los permisos correspondientes.

## Comprobaciones SQL

Para una publicaci?n correcta debe existir:

- exactamente un ProductPrices activo por negocio y producto;
- una auditor?a ligada al ProductPriceId y ProposalId;
- una propuesta Published;
- un CatalogChanges nuevo;
- una outbox durable aun si el transporte push falla.

POS Edge debe recibir importe, moneda y datos vendibles; nunca costo base, margen, proveedor ni auditor?a.
