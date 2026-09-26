# Ejecutar la rebanada de cuentas por cobrar

## Requisitos

- .NET SDK 8.
- SQL Server accesible con las mismas variables usadas por las pruebas de integración existentes.
- Node.js y dependencias instaladas en `admin`.

## Compilar y probar

Desde la raíz del repositorio aislado:

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet build database\Auraly.Database\Auraly.Database.sqlproj --configuration Release
dotnet test tests\Auraly.Foundation.Tests\Auraly.Foundation.Tests.csproj --configuration Release
dotnet test tests\Auraly.ServerSlice.IntegrationTests\Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
dotnet test tests\Auraly.Pos.Edge.Host.Tests\Auraly.Pos.Edge.Host.Tests.csproj --configuration Release
dotnet test src\Tests\Auraly.Platform.Tests\Auraly.Platform.Tests.csproj --configuration Release
```

Desde `admin`:

```powershell
npx tsc --noEmit
npm run test:pos
npm run build
```

`npm run lint` no es actualmente automatizable porque el proyecto no posee configuración ESLint y Next.js abre su asistente interactivo.

## API

La API requiere identidad autenticada con `tenant_id`, `business_id`, identificador de usuario y permisos.

- `GET /api/commerce/v1/receivables`
- `GET /api/commerce/v1/receivables/{receivableId}`
- `GET /api/commerce/v1/customers/{customerId}/credit`
- `PUT /api/commerce/v1/customers/{customerId}/credit`
- `POST /api/commerce/v1/receivable-payments/confirm`

El registro de recaudo exige `Idempotency-Key`. La administración usa `receivables.read`, `receivables.payments.create` y `receivables.credit.manage`. El abono desde punto de venta usa `pos.receivables.payments.create`; al iniciar la operación se abre o retoma automáticamente la sesión operativa del usuario, aunque no exista una venta previa. En la caja preparada, Edge confirma que esa sesión ya está registrada en el servidor antes de consultar o pagar cartera. Ese permiso no habilita la vista administrativa de cuentas por cobrar.

## Verificación manual

1. Configure un cliente con crédito habilitado, plazo y cupo.
2. Confirme una venta online indicando el saldo financiado y vencimiento.
3. Espere a que el motor procese el documento.
4. Abra `/dashboard/receivables`; filtre por cliente, documento, estado o vencimiento.
5. Abra la obligación y registre un abono parcial.
6. Compruebe el nuevo saldo y el movimiento en el historial.
7. Repita la solicitud con la misma llave: debe responder como replay sin duplicar efectos.

El DACPAC es el único dueño del esquema SQL Server; esta rebanada no usa migraciones EF ni `EnsureCreated`.
