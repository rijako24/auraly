# Run the operational accounting slice

## Build and deploy

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
```

Use the existing database deployment scripts. They deploy the DACPAC and execute
the idempotent `SeedAccountingPermissions.sql` through `PostDeployment.sql`.
No EF migration or `EnsureCreated` is used.

## Tests

```powershell
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release
```

The server fixture creates an isolated SQL Server database and deploys the real
DACPAC before tests. The focused accounting test can be run with:

```powershell
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj `
  --configuration Release --filter AccountingVerticalSliceTests
```

## Reproduce pending configuration

1. Issue a sale without an open period or required account mappings.
2. Query `AccountingPostingJobs`; status is
   `AccountingPendingConfiguration` and there is no `AccountingEntries` row.
3. Create the period, accounts and effective mappings through the accounting API.
4. Call `POST /api/commerce/v1/accounting/postings/{documentId}/retry`.
5. Query the entry by source document and verify equal debit and credit totals.
6. Repeat the upload and retry; exactly one entry remains.

## Reproduce a purchase posting

1. Configure an open period and mappings for `Inventory`, `InputVat`,
   `PurchasesExpense` and `AccountsPayable`.
2. Confirm a goods receipt through
   `POST /api/commerce/v1/goods-receipts/confirm`.
3. Every line must declare `DeductibleInputVat`, `CapitalizedCost` or
   `NotApplicable`.
4. Wait for the same document-processing message to complete.
5. Verify one payable, one accounting entry and equal debit/credit totals.
6. Repeat the same idempotency key and verify that no effect is duplicated.
7. Confirm a receipt with `CreatesPayable=false` and no settlement evidence;
   the operational receipt remains, while accounting records
   `SettlementSourceMissing` and creates no false journal entry.

## Operational inspection

```sql
SELECT * FROM dbo.AccountingPostingJobs ORDER BY CreatedAt DESC;
SELECT * FROM dbo.AccountingEntries ORDER BY PostedAt DESC;
SELECT * FROM dbo.AccountingEntryLines WHERE EntryId=@EntryId ORDER BY LineNumber;
```
