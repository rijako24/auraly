using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GoodsReceiptCostUpgradeCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Purchase cost schema upgrade";
}

[Collection(GoodsReceiptCostUpgradeCollection.Name)]
public sealed class GoodsReceiptCostSchemaUpgradeTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Adding_cost_context_preserves_the_existing_movement_table_and_completed_purchase()
    {
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts, PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var at = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.FromHours(-5));
        var request = new ConfirmGoodsReceiptRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            fixture.SupplierId, $"UPGRADE-LEDGER-{Guid.NewGuid():N}", at, at, true, at.AddDays(30), "COP", "Compra histórica",
            [new(1, fixture.ProductId, "Mercancía", 2m, 100m, 0m, "00", 0m, PurchasingTaxTreatments.NotApplicable)]);
        using (var accepted = await ConfirmAsync())
            Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());

        // This collection owns a disposable database. Remove only the new
        // columns to exercise an additive upgrade with an existing posted row.
        await ExecuteAsync("""
            ALTER TABLE dbo.InventoryMovements DROP CONSTRAINT FK_InventoryMovements_CostPoolEpoch,
              CK_InventoryMovements_CostPoolEpoch,CK_InventoryMovements_CostPoolEvidence,CK_InventoryMovements_Quantity;
            ALTER TABLE dbo.InventoryMovements DROP COLUMN ValuationMode,CostPoolScope,
              PoolQuantityBefore,PoolQuantityAfter,PoolInventoryValueBefore,PoolInventoryValueAfter,CostPoolEpochId;
            ALTER TABLE dbo.InventoryMovements ADD CONSTRAINT CK_InventoryMovements_Quantity CHECK(QuantityChange<>0);
            """);
        var before = await SnapshotAsync();
        await fixture.ReapplySchemaAsync();
        Assert.Equal(before, await SnapshotAsync());
        await ExecuteAsync("""
            IF EXISTS(SELECT 1 FROM dbo.InventoryMovements WHERE ValuationMode IS NOT NULL OR CostPoolEpochId IS NOT NULL)
              THROW 51983,'The schema upgrade must not invent historical cost context.',1;
            """);
        using (var replay = await ConfirmAsync())
        {
            Assert.True(replay.StatusCode == HttpStatusCode.Accepted, await replay.Content.ReadAsStringAsync());
            Assert.True((await replay.Content.ReadFromJsonAsync<GoodsReceiptAcceptance>())!.IdempotentReplay);
        }
        Assert.Equal(before, await SnapshotAsync());

        async Task<HttpResponseMessage> ConfirmAsync()
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
                { Content = JsonContent.Create(request) };
            message.Headers.Add("Idempotency-Key", request.DocumentId.ToString("N"));
            return await client.SendAsync(message);
        }

        async Task<string> SnapshotAsync()
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                SELECT (SELECT OBJECT_ID(N'dbo.InventoryMovements') TableId,
                  (SELECT name,column_id FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.InventoryMovements')
                    AND name NOT IN(N'ValuationMode',N'CostPoolScope',N'PoolQuantityBefore',N'PoolQuantityAfter',
                      N'PoolInventoryValueBefore',N'PoolInventoryValueAfter',N'CostPoolEpochId')
                    ORDER BY column_id FOR JSON PATH) ExistingColumns,
                  (SELECT InventoryMovementId,QuantityChange,ValueChange,QuantityBefore,QuantityAfter,
                    AverageUnitCostBefore,AverageUnitCostAfter,RecognizedUnitCost,ProcessingSequence,OccurredAt,PostedAt,CreatedAt
                    FROM dbo.InventoryMovements WHERE DocumentId=@Id ORDER BY InventoryMovementId FOR JSON PATH) Movements,
                  (SELECT WarehouseId,QuantityOnHand,AverageUnitCost,InventoryValue,LastProcessingSequence,UpdatedAt
                    FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND WarehouseId=@WarehouseId
                    ORDER BY WarehouseId FOR JSON PATH) Balances,
                  (SELECT PayableId,OutstandingAmount,Status FROM dbo.Payables WHERE SourceDocumentId=@Id FOR JSON PATH) Payables,
                  (SELECT EntryId,DebitTotal,CreditTotal FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id FOR JSON PATH) Entries,
                  (SELECT BusinessId,EpochId,LastProcessingSequence FROM inventory.CostPoolHeads
                    WHERE ProductId=@ProductId ORDER BY BusinessId,IsDamageWarehouse FOR JSON PATH) ContextReferences
                FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
                """, connection);
            command.Parameters.AddWithValue("@Id", request.DocumentId);
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
            command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            var snapshot = (string)(await command.ExecuteScalarAsync())!;
            using var evidence = JsonDocument.Parse(snapshot);
            Assert.Equal(JsonValueKind.Object, evidence.RootElement.ValueKind);
            return snapshot;
        }
    }

    [Fact]
    public async Task Extending_existing_cost_tables_preserves_captured_invoices_and_inert_replay()
    {
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts, PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var at = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var cost = new GoodsReceiptCostDocumentRequest(Guid.NewGuid(), fixture.SupplierId,
            PurchaseEvidenceTypes.SupplierElectronicInvoice, $"UPGRADE-COST-{Guid.NewGuid():N}",
            at, true, at.AddDays(30), "COP", 1m, new DateOnly(2026, 8, 2), "FunctionalCurrency",
            [new(1, PurchaseCostKinds.Freight, "Flete capturado", 20m, 0m, "00", 0m, 0m,
                PurchasingTaxTreatments.NotApplicable, PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Value)]);
        var request = new ConfirmGoodsReceiptRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            fixture.SupplierId, $"UPGRADE-{Guid.NewGuid():N}", at, at, true, at.AddDays(30), "COP", "Compra anterior al slice",
            [new(1, fixture.ProductId, "Mercancía", 2m, 100m, 0m, "00", 0m, PurchasingTaxTreatments.NotApplicable)],
            AdditionalCostDocuments: [cost]);
        using (var accepted = await ConfirmAsync(request))
            Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
        var before = await SnapshotAsync();

        // Only this disposable, exclusively owned fixture is changed. Recreate
        // the pre-reference cost-table shape in place, preserving every row.
        // This exercises that schema extension, not an entire production upgrade.
        await ExecuteAsync("""
            IF EXISTS(SELECT 1 FROM purchasing.GoodsReceiptCostDocuments WHERE SourceEntryId IS NOT NULL)
              THROW 51980,'The isolated upgrade fixture must not contain reference assignments.',1;
            DROP FUNCTION purchasing.ReceiptCostVoucherLines;
            DROP PROCEDURE purchasing.GoodsReceiptCostDocumentsInsert;
            ALTER TABLE purchasing.GoodsReceiptCostLines DROP CONSTRAINT FK_GoodsReceiptCostLines_SourceBinding,
              FK_GoodsReceiptCostLines_SourceLine,CK_GoodsReceiptCostLines_SourceShape;
            DROP INDEX IX_GoodsReceiptCostLines_SourceBudget ON purchasing.GoodsReceiptCostLines;
            ALTER TABLE purchasing.GoodsReceiptCostLines DROP COLUMN SourceEntryId,SourceEntryLineNumber;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments DROP CONSTRAINT UQ_GoodsReceiptCostDocuments_SourceBinding,
              FK_GoodsReceiptCostDocuments_SourceEntry,CK_GoodsReceiptCostDocuments_SourceShape;
            DROP INDEX UX_GoodsReceiptCostDocuments_CapturedNumber ON purchasing.GoodsReceiptCostDocuments;
            DROP INDEX UX_GoodsReceiptCostDocuments_ReferencedEntry ON purchasing.GoodsReceiptCostDocuments;
            DROP INDEX IX_GoodsReceiptCostDocuments_SourceEntry ON purchasing.GoodsReceiptCostDocuments;
            DROP INDEX IX_GoodsReceiptCostDocuments_Receipt ON purchasing.GoodsReceiptCostDocuments;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments DROP COLUMN SourceEntryId,AssociationMode;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments ALTER COLUMN SupplierId uniqueidentifier NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments ALTER COLUMN PurchaseEvidenceType nvarchar(40) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments ALTER COLUMN DocumentNumber nvarchar(80) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments ALTER COLUMN IssuedAt datetimeoffset(7) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments ALTER COLUMN CreatesPayable bit NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments ALTER COLUMN CurrencyCode char(3) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments ALTER COLUMN ExchangeRate decimal(19,8) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments ALTER COLUMN ExchangeRateDate date NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments ALTER COLUMN ExchangeRateSource nvarchar(64) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments ALTER COLUMN NetAmount decimal(19,4) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments ALTER COLUMN TaxAmount decimal(19,4) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments ALTER COLUMN GrandTotal decimal(19,4) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments ALTER COLUMN FunctionalNetAmount decimal(19,4) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments ALTER COLUMN FunctionalTaxAmount decimal(19,4) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments ALTER COLUMN FunctionalGrandTotal decimal(19,4) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostDocuments ADD CONSTRAINT UQ_GoodsReceiptCostDocuments_SupplierNumber UNIQUE(SupplierId,DocumentNumber);
            CREATE INDEX IX_GoodsReceiptCostDocuments_Receipt ON purchasing.GoodsReceiptCostDocuments(GoodsReceiptId,IssuedAt);
            ALTER TABLE purchasing.GoodsReceiptCostLines ALTER COLUMN CostKind nvarchar(32) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostLines ALTER COLUMN DescriptionSnapshot nvarchar(250) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostLines ALTER COLUMN Amount decimal(19,4) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostLines ALTER COLUMN TaxableBaseAmount decimal(19,4) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostLines ALTER COLUMN TaxCode nvarchar(32) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostLines ALTER COLUMN TaxRate decimal(9,6) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostLines ALTER COLUMN TaxAmount decimal(19,4) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostLines ALTER COLUMN TaxTreatment nvarchar(32) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostLines ALTER COLUMN CostTreatment nvarchar(16) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostLines ALTER COLUMN FunctionalTaxableBaseAmount decimal(19,4) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostLines ALTER COLUMN FunctionalTaxAmount decimal(19,4) NOT NULL;
            ALTER TABLE purchasing.GoodsReceiptCostLines ALTER COLUMN FunctionalDocumentAmount decimal(19,4) NOT NULL;
            """);
        await fixture.ReapplySchemaAsync();
        await ExecuteAsync("""
            IF EXISTS(SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'purchasing.GoodsReceiptCostDocuments')
                AND name=N'UQ_GoodsReceiptCostDocuments_SupplierNumber')
              THROW 51982,'The retired invoice constraint would still reject more than one reference.',1;
            """);
        Assert.Equal(before, await SnapshotAsync());
        using (var replay = await ConfirmAsync(request))
        {
            Assert.True(replay.StatusCode == HttpStatusCode.Accepted, await replay.Content.ReadAsStringAsync());
            Assert.True((await replay.Content.ReadFromJsonAsync<GoodsReceiptAcceptance>())!.IdempotentReplay);
        }
        Assert.Equal(before, await SnapshotAsync());
        var detail = await client.GetFromJsonAsync<GoodsReceiptDetail>($"/api/commerce/v1/goods-receipts/{request.DocumentId}");
        Assert.NotNull(detail);
        Assert.Empty(detail.ReferencedCostDocuments ?? []);
        Assert.Equal(cost.CostDocumentId, Assert.Single(detail.AdditionalCostDocuments!).CostDocumentId);

        async Task<HttpResponseMessage> ConfirmAsync(ConfirmGoodsReceiptRequest value)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm") { Content = JsonContent.Create(value) };
            message.Headers.Add("Idempotency-Key", value.DocumentId.ToString("N"));
            return await client.SendAsync(message);
        }

        async Task<string> SnapshotAsync()
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                IF (SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId IN(@ReceiptId,@CostId))<>2
                  OR COALESCE((SELECT SUM(OutstandingAmount) FROM dbo.Payables WHERE SourceDocumentId IN(@ReceiptId,@CostId)),0)<>220
                  OR (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@ReceiptId AND QuantityChange=2 AND ValueChange=220)<>1
                  OR (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId IN(@ReceiptId,@CostId))<>2
                  THROW 51981,'The captured purchase must retain both debts, inventory and journals.',1;
                SELECT (SELECT
                  (SELECT Status,PayloadHash,GrandTotal FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@ReceiptId FOR JSON PATH) Receipt,
                  (SELECT SupplierId,DocumentNumber,GrandTotal,FunctionalGrandTotal FROM purchasing.GoodsReceiptCostDocuments WHERE CostDocumentId=@CostId FOR JSON PATH) CostDocument,
                  (SELECT LineNumber,CostKind,FunctionalAmount,AllocationMethod FROM purchasing.GoodsReceiptCostLines WHERE CostDocumentId=@CostId FOR JSON PATH) CostLines,
                  (SELECT ReceiptLineNumber,FunctionalAmount,AllocationFactor FROM purchasing.GoodsReceiptCostAllocations WHERE CostDocumentId=@CostId ORDER BY CostLineNumber,ReceiptLineNumber FOR JSON PATH) Allocations,
                  (SELECT PayableId,SourceDocumentId,OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId IN(@ReceiptId,@CostId) ORDER BY SourceDocumentId FOR JSON PATH) Payables,
                  (SELECT InventoryMovementId,QuantityChange,ValueChange FROM dbo.InventoryMovements WHERE DocumentId=@ReceiptId ORDER BY InventoryMovementId FOR JSON PATH) Inventory,
                  (SELECT SourceDocumentId,PayloadHash,AccountingEntryRequired FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId IN(@ReceiptId,@CostId) ORDER BY SourceDocumentId FOR JSON PATH) Sources,
                  (SELECT AccountingPostingJobId,Status,AttemptCount FROM dbo.AccountingPostingJobs WHERE SourceDocumentId IN(@ReceiptId,@CostId) ORDER BY SourceDocumentId FOR JSON PATH) Jobs,
                  (SELECT EntryId,DebitTotal,CreditTotal FROM dbo.AccountingEntries WHERE SourceDocumentId IN(@ReceiptId,@CostId) ORDER BY SourceDocumentId FOR JSON PATH) Entries
                FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
                """, connection);
            command.Parameters.AddWithValue("@ReceiptId", request.DocumentId);
            command.Parameters.AddWithValue("@CostId", cost.CostDocumentId);
            var snapshot = (string)(await command.ExecuteScalarAsync())!;
            using var evidence = JsonDocument.Parse(snapshot);
            Assert.Equal(JsonValueKind.Object, evidence.RootElement.ValueKind);
            return snapshot;
        }
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
