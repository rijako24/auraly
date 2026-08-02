# Inventory operations engine evidence

**Executed:** 2026-08-01
**Database:** isolated SQL Server databases deployed from the real DACPAC

## Verified scenarios

The E2E test creates isolated products, warehouses and document series, then proves:

1. an inbound adjustment creates quantity, average cost and value;
2. a stock count freezes 20 units;
3. a later inbound adjustment increases current stock to 25;
4. confirming the count at 18 applies `-2`, leaving 23 rather than overwriting 18;
5. a transfer moves 3 units and their value atomically;
6. replaying the transfer does not duplicate either movement;
7. a split conversion consumes 4 units at value 20 and creates outputs valued 12 and 8;
8. conversion quantity and value reconcile to zero net value change;
9. an impossible conversion leaves balances, kardex and outbox untouched;
10. five real processing attempts end in dead-letter and release the business order;
11. backend permission denial returns 403.

## Commands and results

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
# 0 warnings, 0 errors

dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
# 0 warnings, 0 errors; DACPAC generated

dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release
# 139 passed

dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
# 80 passed; DACPAC deployed to SQL Server

dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release
# 15 passed

npx tsc --noEmit
# passed

npm run build
# passed; 47 static pages generated, including /pos
```

RabbitMQ was executed against the real local `rabbitmq:4.1-management` container
using its existing credentials without creating users or changing permissions:

```powershell
$env:AURALY_REQUIRE_RABBITMQ_TEST='1'
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj `
  --configuration Release --no-build --no-restore `
  --filter FullyQualifiedName~RabbitMqDocumentProcessingTests
# 1 passed
```

The Rabbit test verifies durable publication, strict order, exactly-once effects,
five attempts, dead-letter routing and processing of the following document.
