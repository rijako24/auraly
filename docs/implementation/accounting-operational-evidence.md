# Operational accounting evidence

**Executed:** 2026-08-01
**Database:** isolated SQL Server database deployed from the real DACPAC

## Proven scenarios

The focused E2E test proves:

1. a real POS invoice completes its operational processing;
2. its accounting work was created atomically;
3. missing configuration yields `AccountingPendingConfiguration` and no entry;
4. secured APIs create accounts, a default cost center, period and mappings;
5. explicit retry creates one balanced immutable entry;
6. replaying the POS invoice does not duplicate the entry;
7. entry number, source linkage and lines are queryable through the API;
8. a real sales return creates a balanced credit-note entry once;
9. restored inventory value reverses cost of goods sold from recognized cost;
10. trial-balance debits equal credits;
11. a period with no pending work closes;
12. missing permissions and wrong tenant scope return 403.

Unit tests additionally reject unbalanced/two-sided journals and periods spanning
calendar years, and prove completion observers run for both first processing and
replayed broker messages.

## Commands recorded

```powershell
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
# 0 warnings, 0 errors

dotnet build Auraly.Commerce.sln --configuration Release
# 0 warnings, 0 errors

dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release
# 143 passed

dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj `
  --configuration Release --filter AccountingVerticalSliceTests
# 2 passed; real DACPAC deployment and SQL Server
```

Final regression from the exact source state:

```text
Auraly.Commerce.sln Release:                 0 warnings, 0 errors
Auraly.Database.sqlproj Release:             0 warnings, 0 errors
Auraly.Foundation.Tests:                    143 passed
Auraly.Pos.Edge.Host.Tests:                  15 passed
Auraly.ServerSlice.IntegrationTests:         82 passed
RabbitMqDocumentProcessingTests explicit:    1 passed
admin npx tsc --noEmit:                       passed
admin npm run build:                          passed, 47 static pages
```

The server regression deploys the real DACPAC to isolated SQL Server databases.
The accounting collection has its own database so its periods, mappings and
inventory effects cannot contaminate the historical regression collection. The
explicit RabbitMQ run set `AURALY_REQUIRE_RABBITMQ_TEST=1`, used the production
consumer against the local RabbitMQ container and therefore could not pass by
silently skipping the broker connection.

## Compliance statement

These tests prove the implemented software behavior; they do not by themselves
establish statutory or tax compliance. Debit notes, remaining source documents,
tax/withholding rules, books, statements, reconciliations and versioned regulatory
exports remain acceptance gates before Auraly is offered as a complete accounting
system.
