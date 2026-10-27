using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Auraly.Application.DocumentProcessing;
using Auraly.Application.Fiscal;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Operational")]
public sealed partial class GoodsReceiptProcessingTests(ServerSliceFixture fixture, Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Twenty_receipt_lines_keep_the_same_SQL_trip_budget_and_preserve_line_history(bool distinctProducts)
    {
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        var measured = new List<System.Collections.Concurrent.ConcurrentDictionary<string, int>>();
        async Task<int> ConfirmAsync(ConfirmGoodsReceiptRequest request)
        {
            var commands = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
            var elapsedByCommand = new System.Collections.Concurrent.ConcurrentDictionary<string, double>();
            using var activity = new System.Diagnostics.Activity("receipt-batch-measurement")
                .SetIdFormat(System.Diagnostics.ActivityIdFormat.W3C).Start();
            static bool IsAuthorization(SqlCommand command) => command.CommandType == CommandType.StoredProcedure
                && command.CommandText == "dbo.ExecutionAccessResolve";
            using var authorization = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId,
                commandFilter: IsAuthorization);
            using var counter = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId,
                commandFilter: command => !IsAuthorization(command),
                beforeExecute: command => commands.AddOrUpdate(command.CommandText, 1, (_, count) => count + 1),
                afterExecute: (command, milliseconds) => elapsedByCommand.AddOrUpdate(command.CommandText,
                    milliseconds, (_, elapsed) => elapsed + milliseconds));
            using var message = CreateMessage(request, $"receipt-batch-{request.DocumentId:N}");
            message.Headers.Add("traceparent", activity.Id);
            var started = System.Diagnostics.Stopwatch.StartNew();
            using var response = await client.SendAsync(message);
            started.Stop();
            Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
            Assert.InRange(authorization.Count, 0, 1);
            if (started.Elapsed >= TimeSpan.FromSeconds(2))
                foreach (var sample in elapsedByCommand.OrderByDescending(item => item.Value).Take(5))
                    output.WriteLine($"SQL {sample.Value:F1} ms: {new string(sample.Key.ReplaceLineEndings(" ").Take(220).ToArray())}");
            if (request.Lines.Count > 1)
                Assert.True(started.Elapsed < TimeSpan.FromSeconds(2), $"La recepción demoró {started.Elapsed}.");
            output.WriteLine($"{request.Lines.Count} partidas: {counter.Count} comandos del recorrido + {authorization.Count} autorización, {started.Elapsed.TotalMilliseconds:F1} ms.");
            measured.Add(commands);
            return counter.Count;
        }
        await ConfirmAsync(CreateRequest());
        var single = await ConfirmAsync(CreateRequest());
        var request = CreateRequest();
        var productIds = Enumerable.Range(1, 20).Select(_ => distinctProducts ? Guid.NewGuid() : fixture.ProductId).ToArray();
        if (distinctProducts)
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var seed = new SqlCommand("""
                DECLARE @Products TABLE(ProductId uniqueidentifier PRIMARY KEY);
                DECLARE @TaxId uniqueidentifier=NEWID();
                INSERT dbo.TaxProfiles(TaxProfileId,TenantId,Code,DianTaxCode,Name,Rate,IsActive,CreatedAt)
                VALUES(@TaxId,@TenantId,LEFT(CONVERT(nvarchar(36),@TaxId),32),N'01',N'IVA prueba lote',19,1,SYSDATETIMEOFFSET());
                INSERT @Products SELECT ProductId FROM OPENJSON(@Json) WITH(ProductId uniqueidentifier '$');
                INSERT dbo.Products(ProductId,TenantId,ProductCode,Reference,Sku,Name,Description,BaseUnitCode,
                  TaxProfileId,PurchaseTaxProfileId,ManageStock,IsWeighable,IsActive,Source,Currency,CreatedAt)
                SELECT input.ProductId,source.TenantId,CONVERT(nvarchar(36),input.ProductId),
                  CONVERT(nvarchar(36),input.ProductId),CONVERT(nvarchar(36),input.ProductId),
                  N'Lote recepción',source.Description,COALESCE(source.BaseUnitCode,N'EA'),COALESCE(source.TaxProfileId,@TaxId),source.PurchaseTaxProfileId,
                  1,source.IsWeighable,1,source.Source,source.Currency,SYSDATETIMEOFFSET()
                FROM @Products input CROSS JOIN dbo.Products source WHERE source.ProductId=@Parent;
                INSERT dbo.ProductPrices(ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,
                  CostBasisType,CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,InputMode,
                  RoundingIncrement,RoundingMode,ValidFrom,IsActive,CreatedAt)
                SELECT NEWID(),source.BusinessId,input.ProductId,source.Amount,source.PreparedAmount,source.CurrencyCode,
                  source.CostBasisType,source.CostBasisAmount,source.TargetMarginPercent,source.EffectiveMarginPercent,
                  source.InputMode,source.RoundingIncrement,source.RoundingMode,SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET()
                FROM @Products input CROSS JOIN dbo.ProductPrices source
                WHERE source.ProductId=@Parent AND source.BusinessId=@BusinessId AND source.IsActive=1;
                INSERT dbo.SupplierProducts(SupplierProductId,ProductId,SupplierId,TenantId,IsPrimary,IsActive,CreatedAt)
                SELECT NEWID(),ProductId,@SupplierId,@TenantId,1,1,SYSDATETIMEOFFSET() FROM @Products;
                """, connection);
            seed.Parameters.AddWithValue("@Json", JsonSerializer.Serialize(productIds));
            seed.Parameters.AddWithValue("@Parent", fixture.ProductId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            seed.Parameters.AddWithValue("@SupplierId", fixture.SupplierId);
            await seed.ExecuteNonQueryAsync();
        }
        request = request with { Lines = Enumerable.Range(1, 20).Select(number => request.Lines.Single() with
            { LineNumber = number, ProductId = productIds[number - 1], UnitCost = 6_000m + number, DiscountAmount = 0m }).ToArray() };
        var many = await ConfirmAsync(request);
        foreach (var command in measured[2].Where(item => item.Value > measured[1].GetValueOrDefault(item.Key)))
            output.WriteLine($"Comando creció {measured[1].GetValueOrDefault(command.Key)} → {command.Value}: {command.Key[..Math.Min(command.Key.Length, 220)]}");
        Assert.Equal("Completed", (await ReadJobAsync(request.DocumentId)).Status);
        Assert.Equal(20, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id", request.DocumentId));
        Assert.Equal(20, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SupplierCostObservations WHERE SourceDocumentId=@Id", request.DocumentId));
        Assert.Equal(20, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PriceRevisionProposals WHERE SourceDocumentId=@Id", request.DocumentId));
        Assert.Equal(distinctProducts ? 20 : 1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PriceRevisionProposals WHERE SourceDocumentId=@Id AND Status=N'PendingReview'", request.DocumentId));
        if (!distinctProducts)
        {
            Assert.Equal(6_019m, await ScalarAsync<decimal>("SELECT PreviousUnitCost FROM dbo.SupplierProductLatestCosts WHERE SourceDocumentId=@Id", request.DocumentId));
            Assert.Equal(6_020m, await ScalarAsync<decimal>("SELECT LatestUnitCost FROM dbo.SupplierProductLatestCosts WHERE SourceDocumentId=@Id", request.DocumentId));
        }
        await ConfirmAsync(request);
        Assert.Equal(20, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SupplierCostObservations WHERE SourceDocumentId=@Id", request.DocumentId));
        Assert.Equal(20, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ProductPricePreparations WHERE SourceDocumentId=@Id", request.DocumentId));
        Assert.Equal(20, await ScalarAsync<int>("SELECT COUNT(*) FROM reporting.PurchaseReportLineFacts WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'", request.DocumentId));
        Assert.Equal(0, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.GoodsReceiptLines source
            LEFT JOIN reporting.PurchaseReportLineFacts fact ON fact.SourceDocumentId=source.GoodsReceiptId
              AND fact.SourceDocumentType=N'GoodsReceipt' AND fact.SourceLineNumber=source.LineNumber
            WHERE source.GoodsReceiptId=@Id AND (fact.PurchaseFactId IS NULL OR fact.ProductId<>source.ProductId
              OR fact.Quantity<>source.Quantity OR fact.UnitCost<>source.UnitCost
              OR fact.DiscountAmount<>source.DiscountAmount OR fact.NetAmount<>source.NetAmount
              OR fact.TaxAmount<>source.TaxAmount OR fact.TotalAmount<>source.LineTotal);
            """, request.DocumentId));
        Assert.True(single > 0 && many <= single, $"Una partida: {single} comandos; veinte: {many}.");
    }

    [Fact]
    public async Task Receipt_batch_keeps_linked_presentations_and_all_preparation_history_in_line_order()
    {
        var childId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var seed = new SqlCommand("""
            DECLARE @TaxId uniqueidentifier=NEWID();
            INSERT dbo.TaxProfiles(TaxProfileId,TenantId,Code,DianTaxCode,Name,Rate,IsActive,CreatedAt)
            VALUES(@TaxId,@TenantId,LEFT(@Code,32),N'01',N'IVA prueba presentación',19,1,SYSDATETIMEOFFSET());
            INSERT dbo.Products(ProductId,TenantId,ProductCode,Reference,Sku,Name,Description,BaseUnitCode,
              TaxProfileId,PurchaseTaxProfileId,ManageStock,IsWeighable,IsActive,Source,Currency,CreatedAt)
            SELECT @Child,TenantId,@Code,@Code,@Code,N'Presentación de recepción',Description,COALESCE(BaseUnitCode,N'EA'),
              COALESCE(TaxProfileId,@TaxId),PurchaseTaxProfileId,0,IsWeighable,1,Source,Currency,SYSDATETIMEOFFSET()
            FROM dbo.Products WHERE ProductId=@Parent;
            INSERT dbo.ProductPrices(ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,
              CostBasisType,CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,InputMode,
              RoundingIncrement,RoundingMode,ValidFrom,IsActive,CreatedAt)
            SELECT NEWID(),BusinessId,@Child,Amount,PreparedAmount,CurrencyCode,CostBasisType,CostBasisAmount,
              TargetMarginPercent,EffectiveMarginPercent,InputMode,RoundingIncrement,RoundingMode,
              SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET()
            FROM dbo.ProductPrices WHERE ProductId=@Parent AND BusinessId=@BusinessId AND IsActive=1;
            INSERT dbo.SupplierProducts(SupplierProductId,ProductId,SupplierId,TenantId,IsPrimary,IsActive,CreatedAt)
            VALUES(NEWID(),@Child,@SupplierId,@TenantId,1,1,SYSDATETIMEOFFSET());
            INSERT dbo.ProductLinks(ProductLinkId,TenantId,ChildProductId,ParentProductId,InventoryFactor,
              PriceFactor,SharesInventory,SharesPrice,AllowsConversion,IsActive,CreatedAt)
            VALUES(NEWID(),@TenantId,@Child,@Parent,2,2,1,1,0,1,SYSDATETIMEOFFSET());
            """, connection))
        {
            seed.Parameters.AddWithValue("@Child", childId);
            seed.Parameters.AddWithValue("@Parent", fixture.ProductId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            seed.Parameters.AddWithValue("@SupplierId", fixture.SupplierId);
            seed.Parameters.AddWithValue("@Code", $"RB-{childId:N}");
            await seed.ExecuteNonQueryAsync();
        }
        try
        {
            var before = await ReadNullableDecimalAsync("QuantityOnHand") ?? 0m;
            var request = CreateRequest();
            var original = request.Lines.Single() with { Quantity = 1, DiscountAmount = 0 };
            request = request with { Lines = [original with { ProductId = childId, LineNumber = 1, UnitCost = 5_000 },
                original with { LineNumber = 2, UnitCost = 6_000 },
                original with { ProductId = childId, LineNumber = 3, UnitCost = 4_000 },
                original with { LineNumber = 4, UnitCost = 7_000 }] };
            using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
                PurchasingPermissionCodes.ConfirmGoodsReceipts);
            using var message = CreateMessage(request, $"receipt-family-{request.DocumentId:N}");
            using var response = await client.SendAsync(message);
            Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
            var job = await ReadJobAsync(request.DocumentId);
            Assert.True(job.Status == "Completed", job.LastError);
            Assert.Equal(before + 6, await ReadNullableDecimalAsync("QuantityOnHand"));
            await using var verify = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id AND ProductId=@Parent),
                  (SELECT COUNT(*) FROM dbo.ProductPricePreparations WHERE ProductId=@Child),
                  (SELECT COUNT(*) FROM dbo.ProductPricePreparations WHERE ProductId=@Child AND Status=N'Pending'),
                  (SELECT COUNT(*) FROM dbo.ProductPricePreparations WHERE ProductId=@Child AND Status=N'Superseded'),
                  (SELECT CostBasisAmount FROM dbo.ProductPricePreparations WHERE ProductId=@Child AND BusinessId=@BusinessId AND Status=N'Pending'),
                  (SELECT CostBasisAmount FROM dbo.ProductPricePreparations WHERE ProductId=@Parent AND BusinessId=@BusinessId AND Status=N'Pending'),
                  (SELECT PreviousUnitCost FROM dbo.SupplierProductLatestCosts WHERE SourceDocumentId=@Id AND ProductId=@Child),
                  (SELECT LatestUnitCost FROM dbo.SupplierProductLatestCosts WHERE SourceDocumentId=@Id AND ProductId=@Child),
                  (SELECT COUNT(*) FROM dbo.PriceRevisionProposals WHERE SourceDocumentId=@Id AND ProductId=@Parent);
                SELECT child.SourceLineNumber,child.CostBasisAmount,root.ObservedUnitCost,child.SourceProductId
                FROM dbo.ProductPricePreparations child
                LEFT JOIN dbo.PriceRevisionProposals root ON root.BusinessId=child.BusinessId
                  AND root.SourceDocumentId=child.SourceDocumentId AND root.ProductId=@Parent
                  AND root.SourceLineNumber=CASE WHEN child.SourceLineNumber=3 THEN 2 ELSE child.SourceLineNumber END
                WHERE child.SourceDocumentId=@Id AND child.ProductId=@Child AND child.BusinessId=@BusinessId
                ORDER BY child.SourceLineNumber;
                """, connection);
            verify.Parameters.AddWithValue("@Id", request.DocumentId);
            verify.Parameters.AddWithValue("@Parent", fixture.ProductId);
            verify.Parameters.AddWithValue("@Child", childId);
            verify.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal((4, 4, 1, 3), (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)));
            Assert.Equal(reader.GetDecimal(5) * 2, reader.GetDecimal(4));
            Assert.Equal((5_000m, 4_000m, 2), (reader.GetDecimal(6), reader.GetDecimal(7), reader.GetInt32(8)));
            Assert.True(await reader.NextResultAsync());
            for (var line = 1; line <= 4; line++)
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(line, reader.GetInt32(0));
                Assert.Equal(fixture.ProductId, reader.GetGuid(3));
                if (line > 1) Assert.Equal(reader.GetDecimal(2) * 2, reader.GetDecimal(1));
            }
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            await using var unlink = new SqlCommand("UPDATE dbo.ProductLinks SET IsActive=0 WHERE ChildProductId=@Child;", connection);
            unlink.Parameters.AddWithValue("@Child", childId);
            await unlink.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_after_supplier_cost_writes_rolls_back_receipt_effects_and_retries_once(bool afterTaxHeader)
    {
        var request = CreateRequest();
        const string snapshotSql = """
            SELECT (SELECT
              (SELECT QuantityOnHand,AverageUnitCost,InventoryValue,LastProcessingSequence,UpdatedAt
               FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId
               FOR JSON PATH) Balance,
              (SELECT PreviousUnitCost,LatestUnitCost,SourceDocumentId,SourceLineNumber,ObservedAt
               FROM dbo.SupplierProductLatestCosts WHERE BusinessId=@BusinessId AND SupplierId=@SupplierId AND ProductId=@ProductId
               FOR JSON PATH) Supplier,
              (SELECT ProductPricePreparationId,Status,SupersededAt FROM dbo.ProductPricePreparations
               WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND Status=N'Pending'
               ORDER BY ProductPricePreparationId FOR JSON PATH) Price
            FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
            """;
        var before = await ScalarAsync<string>(snapshotSql, request.DocumentId);
        fixture.PauseDocumentProcessing();
        try
        {
            using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
                PurchasingPermissionCodes.ConfirmGoodsReceipts);
            using var message = CreateMessage(request, $"receipt-rollback-{request.DocumentId:N}");
            using var response = await client.SendAsync(message);
            Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
            var signal = Assert.Single(fixture.DrainDocumentSignals());
            Assert.Equal(request.DocumentId, signal.DocumentId);
            using var scope = fixture.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<DocumentProcessingWorker>();
            var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
            Assert.StartsWith("AuralyServerSlice_", database);
            using (var fault = new InventoryBalanceProcessingTests.CommandCounter(database,
                commandFilter: command => afterTaxHeader
                    ? command.CommandText.Contains("INSERT dbo.DocumentWithholdingLines", StringComparison.Ordinal)
                    : command.CommandText.Contains("INSERT dbo.SupplierCostObservations(", StringComparison.Ordinal)
                        && command.Parameters["@DocumentId"].Value is Guid id && id == request.DocumentId,
                beforeExecute: command =>
                {
                    if (afterTaxHeader)
                    {
                        command.CommandText = command.CommandText.Replace("INSERT dbo.DocumentWithholdingLines",
                            "THROW 51999,'Injected failure after the persisted tax header.',1; INSERT dbo.DocumentWithholdingLines",
                            StringComparison.Ordinal);
                        return;
                    }
                    var rows = JsonNode.Parse((string)command.Parameters["@LinesJson"].Value)!.AsArray();
                    rows[0]!["RawPrice"] = -1m;
                    command.Parameters["@LinesJson"].Value = rows.ToJsonString();
                }))
            {
                var error = await Assert.ThrowsAsync<SqlException>(() => worker.ProcessOneAsync(signal));
                Assert.Contains(error.Errors.Cast<SqlError>(), item => item.Number == (afterTaxHeader ? 51999 : 547));
                Assert.Equal(1, fault.Count);
            }
            Assert.Equal(before, await ScalarAsync<string>(snapshotSql, request.DocumentId));
            Assert.Equal(0, await ScalarAsync<int>("""
                SELECT (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id)
                  +(SELECT COUNT(*) FROM dbo.SupplierCostObservations WHERE SourceDocumentId=@Id)
                  +(SELECT COUNT(*) FROM dbo.PriceRevisionProposals WHERE SourceDocumentId=@Id)
                  +(SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id)
                  +(SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id)
                  +(SELECT COUNT(*) FROM dbo.DocumentWithholdingSnapshots WHERE DocumentId=@Id);
                """, request.DocumentId));
            Assert.Equal("Accepted", await ScalarAsync<string>("SELECT Status FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", request.DocumentId));
            Assert.Equal("RetryScheduled", (await ReadJobAsync(request.DocumentId)).Status);
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var due = new SqlCommand("SELECT AvailableAt FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id;", connection);
            due.Parameters.AddWithValue("@Id", request.DocumentId);
            await ServerSliceFixture.WaitUntilAvailableAsync(Assert.IsType<DateTimeOffset>(await due.ExecuteScalarAsync()));
            Assert.Equal(DocumentProcessingResult.Processed, await worker.ProcessOneAsync(signal));
            Assert.Equal(DocumentProcessingResult.AlreadyProcessed, await worker.ProcessOneAsync(signal));
            Assert.Equal(1, await CountAsync("InventoryMovements", request.DocumentId));
            Assert.Equal(1, await CountAsync("SupplierCostObservations", request.DocumentId));
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PriceRevisionProposals WHERE SourceDocumentId=@Id", request.DocumentId));
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DocumentWithholdingSnapshots WHERE DocumentId=@Id", request.DocumentId));
        }
        finally
        {
            fixture.ResumeDocumentProcessing();
            fixture.DrainDocumentSignals();
        }
    }

    [Fact]
    public async Task Immediately_due_purchase_opens_one_commercial_payable_when_accounting_is_disabled()
    {
        var request = CreateRequest();
        request = request with { DueDate = request.SupplierInvoiceDate };
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        // Create the connection-scoped temp table outside the parameterized
        // command; a table created inside sp_executesql ends with that scope.
        await using (var snapshot = new SqlCommand("""
            CREATE TABLE #SettingsBefore(Status nvarchar(16),EffectiveFrom date,
              ActivatedAt datetimeoffset(7),ActivatedByUserId uniqueidentifier);
            """, connection))
            await snapshot.ExecuteNonQueryAsync();
        await using (var disable = new SqlCommand("""
            INSERT #SettingsBefore SELECT Status,EffectiveFrom,ActivatedAt,ActivatedByUserId
            FROM dbo.AccountingTenantSettings WHERE TenantId=@TenantId;
            UPDATE dbo.AccountingTenantSettings SET Status=N'Disabled',EffectiveFrom=NULL,
              ActivatedAt=NULL,ActivatedByUserId=NULL WHERE TenantId=@TenantId;
            """, connection))
        {
            disable.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            await disable.ExecuteNonQueryAsync();
        }
        try
        {
            using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
                PurchasingPermissionCodes.ConfirmGoodsReceipts);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var message = CreateMessage(request, $"immediate-commercial-{request.DocumentId:N}");
                using var response = await client.SendAsync(message);
                Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
            }
            await using var verify = new SqlCommand("""
                SELECT payable.OutstandingAmount,payable.DueDate,
                  CONVERT(decimal(19,4),JSON_VALUE(source.PayloadJson,'$.withholding.netAmount')),
                  source.AccountingEntryRequired,job.AccountingEntryRequired,job.Status,
                  (SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id),
                  (SELECT COUNT(*) FROM dbo.PayableTransactions WHERE SourceDocumentId=@Id),
                  (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id),
                  (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id),
                  payable.OpeningEntryId,payable.OpeningEntryLineNumber
                FROM dbo.Payables payable
                JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=payable.SourceDocumentId
                  AND source.SourceDocumentType=payable.SourceDocumentType AND source.BusinessId=payable.BusinessId
                JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=source.SourceDocumentId
                  AND job.SourceDocumentType=source.SourceDocumentType AND job.BusinessId=source.BusinessId
                WHERE payable.SourceDocumentId=@Id AND payable.BusinessId=@BusinessId;
                """, connection);
            verify.Parameters.AddWithValue("@Id", request.DocumentId);
            verify.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(reader.GetDecimal(2), reader.GetDecimal(0));
            Assert.True(reader.GetDecimal(0) > 0);
            Assert.Equal(request.SupplierInvoiceDate!.Value.Date, reader.GetDateTimeOffset(1).Date);
            Assert.False(reader.GetBoolean(3));
            Assert.False(reader.GetBoolean(4));
            Assert.Equal("CommercialEffectsApplied", reader.GetString(5));
            Assert.Equal(1, reader.GetInt32(6));
            Assert.Equal(1, reader.GetInt32(7));
            Assert.Equal(0, reader.GetInt32(8));
            Assert.Equal(1, reader.GetInt32(9));
            Assert.True(reader.IsDBNull(10));
            Assert.True(reader.IsDBNull(11));
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            await using var restore = new SqlCommand("""
                UPDATE settings SET Status=previous.Status,EffectiveFrom=previous.EffectiveFrom,
                  ActivatedAt=previous.ActivatedAt,ActivatedByUserId=previous.ActivatedByUserId
                FROM dbo.AccountingTenantSettings settings CROSS JOIN #SettingsBefore previous
                WHERE settings.TenantId=@TenantId;
                """, connection);
            restore.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            await restore.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Workspace_options_expose_persisted_catalogs_required_by_cost_document_selectors()
    {
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.ReadGoodsReceipts);

        using var response = await client.GetAsync("/api/commerce/v1/goods-receipts/options");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await response.Content.ReadFromJsonAsync<GoodsReceiptWorkspaceOptions>();
        Assert.NotNull(options);
        Assert.Contains(options.PurchaseCostEvidenceTypes, item => item.Code == PurchaseEvidenceTypes.ImportDeclaration);
        Assert.Contains(options.PurchaseCostKinds, item => item.Code == PurchaseCostKinds.Freight);
        Assert.Contains(options.PurchaseCostTreatments, item => item.Code == PurchaseCostTreatments.Capitalize);
        Assert.Contains(options.PurchaseCostAllocationMethods, item => item.Code == PurchaseCostAllocationMethods.Weight);
        Assert.Contains(options.PurchaseCostAllocationMethods, item => item.Code == PurchaseCostAllocationMethods.Manual);
        Assert.Contains(options.PurchaseTaxRates, item => item.Code == "19");
        Assert.Contains(options.PurchaseTaxTreatments, item => item.Code == PurchasingTaxTreatments.CapitalizedCost);
        Assert.Contains(options.PurchaseCurrencies, item => item.Code == "USD");
        Assert.Contains(options.ExchangeRateSources, item => item.Code == "Negotiated");
    }

    [Fact]
    public async Task Shared_price_businesses_use_one_weighted_cost_pool_without_moving_other_stock()
    {
        var sharedBusinessId = Guid.NewGuid();
        var sharedWarehouseId = Guid.NewGuid();
        var sharedSupplierId = fixture.SupplierId;
        var receipt = CreateRequest() with { DocumentId = Guid.NewGuid() };
        var previousShares = await ScalarAsync<bool>(
            "SELECT SharesProductPrices FROM dbo.Businesses WHERE BusinessId=@BusinessId", receipt.DocumentId);
        var previousCostBasis = await ScalarAsync<string>(
            "SELECT InventoryCostBasis FROM dbo.Tenants WHERE TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)", receipt.DocumentId);
        var quantityBefore = await ReadNullableDecimalAsync("QuantityOnHand") ?? 0m;
        var valueBefore = await ReadNullableDecimalAsync("InventoryValue") ?? 0m;
        const decimal otherQuantity = 20m;
        const decimal otherAverage = 4_000m;

        await ExecuteAsync("""
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,TimeZone,SharesProductPrices,IsActive,CreatedAt)
            SELECT @OtherBusinessId,TenantId,N'Sede costo compartido',N'',N'',N'',N'',N'',TimeZone,1,1,SYSUTCDATETIME()
            FROM dbo.Businesses WHERE BusinessId=@BusinessId;
            INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
            SELECT NEWID(),UserId,RoleId,@OtherBusinessId,SYSUTCDATETIME()
            FROM dbo.UserRoles WHERE UserId=@UserId AND BusinessId=@BusinessId;
            INSERT dbo.Warehouses(WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,PriceFormationCostBasis,IsActive,CreatedAt)
            VALUES(@OtherWarehouseId,@OtherBusinessId,N'BOD-SHARED',N'Bodega compartida',1,0,1,1,1,N'WeightedAverageCost',1,SYSUTCDATETIME());
            INSERT dbo.ProductPrices(ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,CostBasisType,CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,InputMode,RoundingIncrement,RoundingMode,ValidFrom,IsActive,CreatedAt)
            SELECT NEWID(),@OtherBusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,CostBasisType,CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,InputMode,RoundingIncrement,RoundingMode,SYSUTCDATETIME(),1,SYSUTCDATETIME()
            FROM dbo.ProductPrices WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND IsActive=1;
            INSERT dbo.InventoryBalances(BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,InventoryValue,LastProcessingSequence,UpdatedAt)
            VALUES(@OtherBusinessId,@OtherWarehouseId,@ProductId,@OtherQuantity,@OtherAverage,@OtherValue,0,SYSUTCDATETIME());
            UPDATE dbo.Businesses SET SharesProductPrices=1 WHERE BusinessId=@BusinessId;
            UPDATE dbo.Tenants SET InventoryCostBasis=N'WeightedAverageCost'
            WHERE TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId);
            """, sharedBusinessId, sharedWarehouseId, otherQuantity, otherAverage,
            otherSupplierId: sharedSupplierId);

        try
        {
            using var client = fixture.CreateAdminClient(
                PurchasingPermissionCodes.CreateGoodsReceipts,
                PurchasingPermissionCodes.ConfirmGoodsReceipts);
            using var message = CreateMessage(receipt, $"shared-cost-{receipt.DocumentId:N}");
            using var response = await client.SendAsync(message);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("Completed", (await ReadJobAsync(receipt.DocumentId)).Status);

            var expectedAverage = decimal.Round(
                (valueBefore + otherQuantity * otherAverage + 50_000m) /
                (quantityBefore + otherQuantity + 10m), 6, MidpointRounding.AwayFromZero);
            Assert.Equal(quantityBefore + 10m, await ReadNullableDecimalAsync("QuantityOnHand"));
            Assert.Equal(expectedAverage, await ReadNullableDecimalAsync("AverageUnitCost"));
            Assert.Equal(otherQuantity, await ReadBalanceAsync(sharedBusinessId, sharedWarehouseId, "QuantityOnHand"));
            Assert.Equal(expectedAverage, await ReadBalanceAsync(sharedBusinessId, sharedWarehouseId, "AverageUnitCost"));
            Assert.Equal(decimal.Round(otherQuantity * expectedAverage, 4, MidpointRounding.AwayFromZero),
                await ReadBalanceAsync(sharedBusinessId, sharedWarehouseId, "InventoryValue"));
            Assert.Equal(2, await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.PriceRevisionProposals WHERE SourceDocumentId=@Id", receipt.DocumentId));

            var firstEvidence = await ReadValuationEvidenceAsync(receipt.DocumentId);
            Assert.Equal("WeightedAverageReceipt", firstEvidence.GetProperty("ValuationMode").GetString());
            Assert.Equal("SharedTenant", firstEvidence.GetProperty("CostPoolScope").GetString());
            Assert.Equal(quantityBefore + otherQuantity, firstEvidence.GetProperty("PoolQuantityBefore").GetDecimal());
            Assert.Equal(quantityBefore + otherQuantity + 10m, firstEvidence.GetProperty("PoolQuantityAfter").GetDecimal());
            Assert.Equal(valueBefore + otherQuantity * otherAverage, firstEvidence.GetProperty("PoolInventoryValueBefore").GetDecimal());
            Assert.Equal((await ReadNullableDecimalAsync("InventoryValue") ?? 0m) +
                await ReadBalanceAsync(sharedBusinessId, sharedWarehouseId, "InventoryValue"),
                firstEvidence.GetProperty("PoolInventoryValueAfter").GetDecimal());

            var reverseReceipt = CreateRequest() with
            {
                DocumentId = Guid.NewGuid(),
                BusinessId = sharedBusinessId,
                WarehouseId = sharedWarehouseId,
                SupplierId = sharedSupplierId
            };
            var primaryQuantityBeforeReverse = await ReadNullableDecimalAsync("QuantityOnHand") ?? 0m;
            var sharedQuantityBeforeReverse = await ReadBalanceAsync(
                sharedBusinessId, sharedWarehouseId, "QuantityOnHand");
            var poolValueBeforeReverse =
                (await ReadNullableDecimalAsync("InventoryValue") ?? 0m) +
                await ReadBalanceAsync(sharedBusinessId, sharedWarehouseId, "InventoryValue");
            using var sharedClient = fixture.CreateAdminClientWithBusinessHeader(
                sharedBusinessId,
                PurchasingPermissionCodes.CreateGoodsReceipts,
                PurchasingPermissionCodes.ConfirmGoodsReceipts);
            using var reverseMessage = CreateMessage(
                reverseReceipt, $"shared-cost-reverse-{reverseReceipt.DocumentId:N}");
            using var reverseResponse = await sharedClient.SendAsync(reverseMessage);
            Assert.Equal(HttpStatusCode.Accepted, reverseResponse.StatusCode);
            Assert.Equal("Completed", (await ReadJobAsync(reverseReceipt.DocumentId)).Status);

            var reverseExpectedAverage = decimal.Round(
                (poolValueBeforeReverse + 50_000m) /
                (primaryQuantityBeforeReverse + sharedQuantityBeforeReverse + 10m),
                6, MidpointRounding.AwayFromZero);
            Assert.Equal(primaryQuantityBeforeReverse,
                await ReadNullableDecimalAsync("QuantityOnHand"));
            Assert.Equal(sharedQuantityBeforeReverse + 10m,
                await ReadBalanceAsync(sharedBusinessId, sharedWarehouseId, "QuantityOnHand"));
            var primaryAverageAfterReverse =
                await ReadNullableDecimalAsync("AverageUnitCost");
            var sharedAverageAfterReverse = await ReadBalanceAsync(
                sharedBusinessId, sharedWarehouseId, "AverageUnitCost");
            Assert.True(
                primaryAverageAfterReverse == reverseExpectedAverage &&
                sharedAverageAfterReverse == reverseExpectedAverage,
                $"Expected shared average {reverseExpectedAverage}; primary was " +
                $"{primaryAverageAfterReverse} and receiving business was {sharedAverageAfterReverse}.");
            Assert.Equal(2, await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.PriceRevisionProposals WHERE SourceDocumentId=@Id",
                reverseReceipt.DocumentId));
            Assert.Equal(firstEvidence.GetRawText(), (await ReadValuationEvidenceAsync(receipt.DocumentId)).GetRawText());
            var originalEpoch = await ScalarAsync<Guid>(
                "SELECT CostPoolEpochId FROM dbo.InventoryMovements WHERE DocumentId=@Id", receipt.DocumentId);
            var initialSharedEpoch = originalEpoch;
            Assert.Equal(originalEpoch, await ScalarAsync<Guid>(
                "SELECT CostPoolEpochId FROM dbo.InventoryMovements WHERE DocumentId=@Id", reverseReceipt.DocumentId));
            foreach (var shares in new[] { false, true })
            {
                await ExecuteAsync("UPDATE dbo.Businesses SET SharesProductPrices=@PreviousShares WHERE BusinessId=@BusinessId;",
                    sharedBusinessId, sharedWarehouseId, otherQuantity, otherAverage, shares);
                var changed = CreateRequest();
                using var changedMessage = CreateMessage(changed, $"changed-pool-{changed.DocumentId:N}");
                using var changedResponse = await client.SendAsync(changedMessage);
                Assert.True(changedResponse.StatusCode == HttpStatusCode.Accepted, await changedResponse.Content.ReadAsStringAsync());
                Assert.Equal("Completed", (await ReadJobAsync(changed.DocumentId)).Status);
                var changedEpoch = await ScalarAsync<Guid>(
                    "SELECT CostPoolEpochId FROM dbo.InventoryMovements WHERE DocumentId=@Id", changed.DocumentId);
                // Rejoining the same members after separate movements must not
                // recover the old context just because membership looks equal.
                Assert.NotEqual(originalEpoch, changedEpoch);
                if (shares) Assert.NotEqual(initialSharedEpoch, changedEpoch);
                var membership = JsonSerializer.Deserialize<Guid[]>(await ScalarAsync<string>("""
                    SELECT epoch.MemberBusinessIdsJson FROM dbo.InventoryMovements movement
                    JOIN inventory.CostPoolEpochs epoch ON epoch.EpochId=movement.CostPoolEpochId
                    WHERE movement.DocumentId=@Id;
                    """, changed.DocumentId))!;
                Assert.Contains(fixture.BusinessId, membership);
                if (shares) Assert.Contains(sharedBusinessId, membership);
                else Assert.Single(membership);
                Assert.Equal(1, await ScalarAsync<int>("""
                    SELECT COUNT(*) FROM dbo.InventoryMovements movement
                    JOIN inventory.CostPoolEpochs epoch ON epoch.EpochId=movement.CostPoolEpochId
                    WHERE movement.DocumentId=@Id AND epoch.InitialQuantity=movement.PoolQuantityBefore;
                    """, changed.DocumentId));
                originalEpoch = changedEpoch;
            }
            Assert.Equal(firstEvidence.GetRawText(), (await ReadValuationEvidenceAsync(receipt.DocumentId)).GetRawText());
        }
        finally
        {
            await ExecuteAsync("""
                UPDATE dbo.Businesses SET SharesProductPrices=@PreviousShares WHERE BusinessId=@BusinessId;
                UPDATE dbo.Businesses SET SharesProductPrices=0,IsActive=0 WHERE BusinessId=@OtherBusinessId;
                UPDATE dbo.Tenants SET InventoryCostBasis=@PreviousCostBasis
                WHERE TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId);
                """, sharedBusinessId, sharedWarehouseId, otherQuantity, otherAverage,
                previousShares, previousCostBasis, sharedSupplierId);
        }
    }

    [Fact]
    public async Task Weighted_cost_receipt_preserves_negative_quantity_without_corrupting_shared_cost()
    {
        var previousQuantity = await ReadNullableDecimalAsync("QuantityOnHand") ?? 0m;
        var previousAverage = await ReadNullableDecimalAsync("AverageUnitCost") ?? 0m;
        var previousValue = await ReadNullableDecimalAsync("InventoryValue") ?? 0m;
        var previousShares = await ScalarAsync<bool>(
            "SELECT SharesProductPrices FROM dbo.Businesses WHERE BusinessId=@BusinessId", Guid.NewGuid());
        var previousCostBasis = await ScalarAsync<string>(
            "SELECT InventoryCostBasis FROM dbo.Tenants WHERE TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)", Guid.NewGuid());

        try
        {
            await SetPrimaryCostStateAsync(-10m, 5_000m, -50_000m, true, "WeightedAverageCost");
            Assert.Equal(-10m, await ReadNullableDecimalAsync("QuantityOnHand"));
            Assert.Equal(5_000m, await ReadNullableDecimalAsync("AverageUnitCost"));
            var request = CreateRequest() with
            {
                DocumentId = Guid.NewGuid(),
                Lines =
                [
                    CreateRequest().Lines.Single() with
                    {
                        Quantity = 4m,
                        UnitCost = 6_000m,
                        DiscountAmount = 0m
                    }
                ]
            };
            using var client = fixture.CreateAdminClient(
                PurchasingPermissionCodes.CreateGoodsReceipts,
                PurchasingPermissionCodes.ConfirmGoodsReceipts);
            using var message = CreateMessage(request, $"negative-shared-cost-{request.DocumentId:N}");
            using var response = await client.SendAsync(message);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("Completed", (await ReadJobAsync(request.DocumentId)).Status);

            Assert.Equal(-10m, await ScalarAsync<decimal>(
                "SELECT QuantityBefore FROM dbo.InventoryMovements WHERE DocumentId=@Id", request.DocumentId));
            Assert.Equal(-6m, await ReadNullableDecimalAsync("QuantityOnHand"));
            Assert.Equal(5_000m, await ReadNullableDecimalAsync("AverageUnitCost"));
            Assert.Equal(-30_000m, await ReadNullableDecimalAsync("InventoryValue"));
            var evidence = await ReadValuationEvidenceAsync(request.DocumentId);
            Assert.Equal("WeightedAverageReceipt", evidence.GetProperty("ValuationMode").GetString());
            Assert.Equal(-10m, evidence.GetProperty("PoolQuantityBefore").GetDecimal());
            Assert.Equal(-6m, evidence.GetProperty("PoolQuantityAfter").GetDecimal());
            Assert.Equal(-50_000m, evidence.GetProperty("PoolInventoryValueBefore").GetDecimal());
            Assert.Equal(-30_000m, evidence.GetProperty("PoolInventoryValueAfter").GetDecimal());
        }
        finally
        {
            await SetPrimaryCostStateAsync(
                previousQuantity, previousAverage, previousValue,
                previousShares, previousCostBasis);
        }
    }

    [Fact]
    public async Task Deferred_financial_discount_keeps_original_payable_and_cost_even_after_the_informative_date()
    {
        var request = CreateRequest() with
        {
            FinancialDiscount = new(10m, new DateOnly(2020, 1, 1), false)
        };
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        using var message = CreateMessage(request, $"receipt-deferred-financial-{request.DocumentId:N}");
        using var response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = await ReadJobAsync(request.DocumentId);
        Assert.True(job.Status == "Completed", job.LastError ?? job.Status);
        Assert.Equal(50_000m, await ScalarAsync<decimal>(
            "SELECT RecognizedInventoryCostAmount FROM dbo.GoodsReceiptLines WHERE GoodsReceiptId=@Id AND LineNumber=1", request.DocumentId));
        Assert.Equal(59_500m, await ScalarAsync<decimal>(
            "SELECT OriginalAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'", request.DocumentId));
        Assert.Equal("Posted", await ScalarAsync<string>(
            "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'", request.DocumentId));
    }

    [Fact]
    public async Task Anticipated_financial_discount_reduces_current_cost_and_payable_without_rewriting_the_invoice()
    {
        var request = CreateRequest() with
        {
            FinancialDiscount = new(10m, new DateOnly(2026, 8, 15), true)
        };
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        using var message = CreateMessage(request, $"receipt-financial-{request.DocumentId:N}");
        using var response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = await ReadJobAsync(request.DocumentId);
        Assert.True(job.Status == "Completed", job.LastError ?? job.Status);
        Assert.Equal(59_500m, await ScalarAsync<decimal>(
            "SELECT FunctionalGrandTotal FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", request.DocumentId));
        Assert.Equal(45_000m, await ScalarAsync<decimal>(
            "SELECT SUM(ValueChange) FROM dbo.InventoryMovements WHERE DocumentId=@Id AND DocumentType=N'GoodsReceipt'", request.DocumentId));
        Assert.Equal(54_500m, await ScalarAsync<decimal>(
            "SELECT OriginalAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'", request.DocumentId));
        Assert.Equal("Posted", await ScalarAsync<string>(
            "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'", request.DocumentId));
        Assert.Equal(0m, await ScalarAsync<decimal>("""
            SELECT SUM(line.Debit-line.Credit) FROM dbo.AccountingEntryLines line
            JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
            WHERE entry.SourceDocumentId=@Id AND entry.SourceDocumentType=N'GoodsReceipt'
            """, request.DocumentId));
    }

    [Fact]
    public async Task Receipt_flows_once_through_inventory_cost_payable_and_price_review()
    {
        var request = CreateRequest();
        var quantityBefore = await ReadNullableDecimalAsync("QuantityOnHand") ?? 0m;
        var valueBefore = await ReadNullableDecimalAsync("InventoryValue") ?? 0m;
        const string idempotencyKey = "receipt-e2e-001";
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts,
            PayablesPermissionCodes.Read);

        using (var message = CreateMessage(request, idempotencyKey))
        using (var response = await client.SendAsync(message))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var acceptance = await response.Content.ReadFromJsonAsync<GoodsReceiptAcceptance>();
            Assert.NotNull(acceptance);
            Assert.StartsWith("EMC00-", acceptance.DocumentNumber);
            Assert.False(acceptance.IdempotentReplay);
        }

        Assert.Equal("Processed", await ScalarAsync<string>(
            "SELECT Status FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", request.DocumentId));
        Assert.Equal(10_000m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@ProductId AND IsActive=1",
            request.DocumentId));

        var job = await ReadJobAsync(request.DocumentId);
        Assert.True(job.Status == "Completed", job.LastError ?? job.Status);

        var quantityAfter = quantityBefore + 10m;
        var valueAfter = valueBefore + 50_000m;
        var averageAfter = decimal.Round(valueAfter / quantityAfter, 6, MidpointRounding.AwayFromZero);
        Assert.Equal("Processed", await ScalarAsync<string>(
            "SELECT Status FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", request.DocumentId));
        Assert.Equal(quantityAfter, await ReadNullableDecimalAsync("QuantityOnHand"));
        Assert.Equal(valueAfter, await ReadNullableDecimalAsync("InventoryValue"));
        Assert.Equal(averageAfter, await ReadNullableDecimalAsync("AverageUnitCost"));
        Assert.Equal(5_000m, await ScalarAsync<decimal>(
            "SELECT LatestUnitCost FROM dbo.SupplierProductLatestCosts WHERE BusinessId=@BusinessId AND SupplierId=@SupplierId AND ProductId=@ProductId",
            request.DocumentId));
        Assert.Equal(59_500m, await ScalarAsync<decimal>(
            "SELECT OriginalAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
            request.DocumentId));
        var payableId = await ScalarAsync<Guid>(
            "SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
            request.DocumentId);
        var payableDetail = await client.GetFromJsonAsync<PayableDetail>(
            $"/api/commerce/v1/payables/{payableId}");
        Assert.Equal(request.DocumentId, payableDetail?.GoodsReceiptId);
        Assert.Null(payableDetail?.ExpenseConceptName);
        Assert.Equal("PendingReview", await ScalarAsync<string>(
            "SELECT Status FROM dbo.PriceRevisionProposals WHERE SourceDocumentId=@Id",
            request.DocumentId));
        Assert.Equal(10_000m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.ProductPrices WHERE ProductId=@ProductId AND IsActive=1",
            request.DocumentId));

        Assert.Equal(1, await CountAsync("InventoryMovements", request.DocumentId));
        Assert.Equal(1, await CountAsync("SupplierCostObservations", request.DocumentId));
        Assert.Equal(1, await CountAsync("Payables", request.DocumentId));
        Assert.Equal(1, await CountAsync("PayableTransactions", request.DocumentId));
        Assert.Equal(1, await CountAsync("PriceRevisionProposals", request.DocumentId));
        Assert.Equal(1, await CountAsync("ServerOutboxMessages", request.DocumentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM reporting.SalesReportingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
            request.DocumentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM reporting.PurchaseReportDocuments WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
            request.DocumentId));

        using (var duplicate = CreateMessage(request, idempotencyKey))
        using (var response = await client.SendAsync(duplicate))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var replay = await response.Content.ReadFromJsonAsync<GoodsReceiptAcceptance>();
            Assert.NotNull(replay);
            Assert.True(replay.IdempotentReplay);
            Assert.Equal(request.DocumentId, replay.DocumentId);
        }
        Assert.Equal(1, await CountAsync("InventoryMovements", request.DocumentId));
        Assert.Equal(1, await CountAsync("Payables", request.DocumentId));
        Assert.Equal(1, await CountAsync("ServerOutboxMessages", request.DocumentId));
    }

    [Fact]
    public async Task Freight_invoice_posts_its_own_payable_and_capitalizes_cost_without_duplicating_vat()
    {
        var request = CreateRequest();
        var freightDocumentId = Guid.NewGuid();
        var freightDocumentNumber = $"FLETE-INTERNACIONAL-{Guid.NewGuid():N}-SOPORTE-TRANSPORTADOR";
        request = request with
        {
            AdditionalCostDocuments =
            [
                new GoodsReceiptCostDocumentRequest(
                    freightDocumentId, fixture.SupplierId,
                    PurchaseEvidenceTypes.SupplierElectronicInvoice,
                    freightDocumentNumber, request.ReceivedAt, true,
                    request.ReceivedAt.AddDays(30), "COP", 1m,
                    DateOnly.FromDateTime(request.ReceivedAt.Date), "FunctionalCurrency",
                    [
                        new GoodsReceiptCostLineRequest(
                            1, PurchaseCostKinds.Freight, "Flete nacional", 10_000m,
                            10_000m, "01", 19m, 1_900m,
                            PurchasingTaxTreatments.DeductibleInputVat,
                            PurchaseCostTreatments.Capitalize,
                            PurchaseCostAllocationMethods.Value)
                    ])
            ]
        };
        var previousShares = await ScalarAsync<bool>(
            "SELECT SharesProductPrices FROM dbo.Businesses WHERE BusinessId=@BusinessId", request.DocumentId);
        var previousCostBasis = await ScalarAsync<string>(
            "SELECT InventoryCostBasis FROM dbo.Tenants WHERE TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)", request.DocumentId);
        await SetPrimaryCostPolicyAsync(false, "WeightedAverageCost");

        try
        {
            using var client = fixture.CreateAdminClient(
                PurchasingPermissionCodes.CreateGoodsReceipts,
                PurchasingPermissionCodes.ConfirmGoodsReceipts,
                PayablesPermissionCodes.Read);

            using var message = CreateMessage(request, $"freight-{request.DocumentId:N}");
            using var response = await client.SendAsync(message);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.Accepted,
                $"Expected freight receipt to be accepted, got {response.StatusCode}: {body}");

            var processingJob = await ReadJobAsync(request.DocumentId);
            Assert.True(processingJob.Status == "Completed", processingJob.LastError ?? processingJob.Status);

            Assert.Equal(10m, await ScalarAsync<decimal>(
                "SELECT SUM(QuantityChange) FROM dbo.InventoryMovements WHERE DocumentId=@Id AND DocumentType=N'GoodsReceipt'",
                request.DocumentId));
            Assert.Equal(60_000m, await ScalarAsync<decimal>(
                "SELECT SUM(ValueChange) FROM dbo.InventoryMovements WHERE DocumentId=@Id AND DocumentType=N'GoodsReceipt'",
                request.DocumentId));
            Assert.Equal("Posted||", await ScalarAsync<string>(
                "SELECT CONCAT(Status,N'|',COALESCE(LastErrorCode,N''),N'|',COALESCE(LastErrorMessage,N'')) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
                request.DocumentId));
            Assert.Equal(59_500m, await ScalarAsync<decimal>(
                "SELECT OriginalAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
                request.DocumentId));
            Assert.Equal(11_900m, await ScalarAsync<decimal>(
                "SELECT OriginalAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostDocument'",
                freightDocumentId));
            Assert.Equal(11_900m, await ScalarAsync<decimal>("""
                SELECT line.Credit FROM dbo.Payables payable
                JOIN dbo.AccountingEntryLines line ON line.EntryId=payable.OpeningEntryId
                  AND line.LineNumber=payable.OpeningEntryLineNumber
                JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
                  AND entry.SourceDocumentId=payable.SourceDocumentId
                  AND entry.SourceDocumentType=payable.SourceDocumentType AND entry.BusinessId=payable.BusinessId
                JOIN dbo.Suppliers supplier ON supplier.SupplierId=payable.SupplierId AND supplier.PartyId=line.PartyId
                JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId AND account.TenantId=entry.TenantId
                WHERE payable.SourceDocumentId=@Id AND account.Code=N'220505';
                """, freightDocumentId));
            Assert.Equal(freightDocumentNumber, await ScalarAsync<string>(
                "SELECT DocumentNumber FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostDocument'",
                freightDocumentId));
            var costPayableId = await ScalarAsync<Guid>(
                "SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostDocument'",
                freightDocumentId);
            var costPayable = await client.GetFromJsonAsync<PayableDetail>(
                $"/api/commerce/v1/payables/{costPayableId}");
            Assert.Equal(request.DocumentId, costPayable?.GoodsReceiptId);
            Assert.Equal("Posted", await ScalarAsync<string>(
                "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostDocument'",
                freightDocumentId));
            Assert.Equal(10_000m, await ScalarAsync<decimal>(
                "SELECT SUM(FunctionalAmount) FROM purchasing.GoodsReceiptCostAllocations WHERE CostDocumentId=@Id",
                freightDocumentId));
            Assert.Equal(1_900m, await ScalarAsync<decimal>(
                "SELECT FunctionalTaxAmount FROM purchasing.GoodsReceiptCostDocuments WHERE CostDocumentId=@Id",
                freightDocumentId));
            Assert.Equal(60_000m, await CombinedAccountAmountAsync(
                request.DocumentId, "143505", debit: true));
            Assert.Equal(0m, await CombinedAccountAmountAsync(
                request.DocumentId, "143505", debit: false));
            Assert.Equal(0m, await CombinedAccountAmountAsync(
                request.DocumentId, "613595", debit: false));
            Assert.Equal(11_400m, await CombinedAccountAmountAsync(
                request.DocumentId, "240810", debit: true));
            Assert.Equal(71_400m, await CombinedAccountAmountAsync(
                request.DocumentId, "220505", debit: false));
        }
        finally
        {
            await SetPrimaryCostPolicyAsync(previousShares, previousCostBasis);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(100)]
    public async Task Two_freight_suppliers_and_repeated_freight_concepts_create_independent_payables(int documentCount)
    {
        var secondPartyId = Guid.NewGuid();
        var secondSupplierId = Guid.NewGuid();
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,LegalName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
                VALUES(@PartyId,@TenantId,N'Organization',N'Segundo transportador',N'Segundo transportador',N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());
                INSERT dbo.Suppliers(SupplierId,TenantId,PartyId,Identification,Name,PurchaseEvidencePolicy,IsActive,CreatedAt)
                VALUES(@SupplierId,@TenantId,@PartyId,@Identification,N'Segundo transportador',N'InternalReceiptVoucher',1,SYSDATETIMEOFFSET());
                INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,
                  AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
                SELECT NEWID(),@PartyId,N'PRINCIPAL',N'Sede principal',CountryId,AdministrativeDivisionId,CityId,
                  AddressLine,1,1,@UserId,SYSDATETIMEOFFSET()
                FROM dbo.PartySites WHERE PartyId=@OriginalPartyId AND IsPrimary=1 AND IsActive=1;
                """;
            command.Parameters.AddWithValue("@PartyId", secondPartyId);
            command.Parameters.AddWithValue("@SupplierId", secondSupplierId);
            command.Parameters.AddWithValue("@OriginalPartyId", fixture.SupplierPartyId);
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            command.Parameters.AddWithValue("@UserId", fixture.UserId);
            command.Parameters.AddWithValue("@Identification", $"FLETE-{secondSupplierId:N}");
            await command.ExecuteNonQueryAsync();
        }

        var request = CreateRequest();
        GoodsReceiptCostLineRequest FreightLine(int number, decimal amount) =>
            new(number, PurchaseCostKinds.Freight, $"Flete {number}", amount, 0m,
                "IVA-0", 0m, 0m, PurchasingTaxTreatments.NotApplicable,
                PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Value);
        GoodsReceiptCostDocumentRequest FreightDocument(Guid supplierId, string evidenceType, string number,
            params GoodsReceiptCostLineRequest[] lines) =>
            new(Guid.NewGuid(), supplierId, evidenceType,
                number, request.ReceivedAt, true, request.ReceivedAt.AddDays(30), "COP", 1m,
                DateOnly.FromDateTime(request.ReceivedAt.Date), "FunctionalCurrency", lines);
        var first = FreightDocument(fixture.SupplierId, PurchaseEvidenceTypes.SupplierElectronicInvoice,
            $"FLETE-A-{Guid.NewGuid():N}",
            FreightLine(1, 2_000m), FreightLine(2, 3_000m));
        var second = FreightDocument(secondSupplierId, PurchaseEvidenceTypes.InternalReceiptVoucher,
            $"FLETE-B-{Guid.NewGuid():N}",
            FreightLine(1, 4_000m));
        var documents = new[] { first, second }.Concat(Enumerable.Range(2, documentCount - 2)
            .Select(_ => second with { CostDocumentId = Guid.NewGuid(), DocumentNumber = $"FLETE-{Guid.NewGuid():N}" }))
            .ToArray();
        request = request with { AdditionalCostDocuments = documents };

        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts,
            PayablesPermissionCodes.Read);
        using var message = CreateMessage(request, $"two-freight-{request.DocumentId:N}");
        using var acceptance = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandType == CommandType.StoredProcedure &&
                command.CommandText == "dbo.AccountingSourcesAccept");
        using var withholding = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains(
                "INSERT dbo.DocumentWithholdingSnapshots", StringComparison.Ordinal));
        using var costPersistence = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText == "purchasing.GoodsReceiptCostDocumentsInsert" ||
                command.CommandText.Contains("INSERT purchasing.GoodsReceiptCostDocuments", StringComparison.Ordinal));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        using var response = await client.SendAsync(message);
        timer.Stop();
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            await response.Content.ReadAsStringAsync());
        output.WriteLine("Additional documents: {0}; persistence commands: {1}; confirm/engines: {2:F1} ms",
            documentCount, costPersistence.Count, timer.Elapsed.TotalMilliseconds);
        Assert.Equal(1, costPersistence.Count);
        Assert.Equal("Completed", (await ReadJobAsync(request.DocumentId)).Status);
        var expectedBatches = (documentCount + 100) / 100;
        Assert.Equal((expectedBatches, expectedBatches), (acceptance.Count, withholding.Count));
        Assert.Equal(documentCount + 1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Payables WHERE ParentGoodsReceiptId=@Id", request.DocumentId));
        Assert.Equal(documentCount, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.GoodsReceiptCostDocuments WHERE GoodsReceiptId=@Id", request.DocumentId));
        Assert.Equal(5_000m + (documentCount - 1) * 4_000m, await ScalarAsync<decimal>(
            "SELECT SUM(FunctionalAmount) FROM purchasing.GoodsReceiptCostAllocations WHERE GoodsReceiptId=@Id",
            request.DocumentId));
        Assert.Equal(documentCount + 1, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM purchasing.GoodsReceiptCostLines line
            JOIN purchasing.GoodsReceiptCostDocuments document ON document.CostDocumentId=line.CostDocumentId
            WHERE document.GoodsReceiptId=@Id;
            """, request.DocumentId));
        Assert.Equal(documentCount + 1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.GoodsReceiptCostAllocations WHERE GoodsReceiptId=@Id", request.DocumentId));
        const string persistedSql = """
            SELECT (SELECT document.*,
              (SELECT line.* FROM purchasing.GoodsReceiptCostLines line
               WHERE line.CostDocumentId=document.CostDocumentId ORDER BY line.LineNumber FOR JSON PATH) Lines,
              (SELECT allocation.* FROM purchasing.GoodsReceiptCostAllocations allocation
               WHERE allocation.CostDocumentId=document.CostDocumentId
               ORDER BY allocation.CostLineNumber,allocation.ReceiptLineNumber FOR JSON PATH) Allocations
            FROM purchasing.GoodsReceiptCostDocuments document WHERE document.GoodsReceiptId=@Id
            ORDER BY document.CostDocumentId FOR JSON PATH);
            """;
        var persisted = await ScalarAsync<string>(persistedSql, request.DocumentId);
        using var persistedEvidence = JsonDocument.Parse(persisted);
        Assert.Equal(documentCount, persistedEvidence.RootElement.GetArrayLength());
        using var replay = CreateMessage(request, $"two-freight-{request.DocumentId:N}");
        var commandsBeforeReplay = costPersistence.Count;
        using var repeated = await client.SendAsync(replay);
        Assert.True(repeated.StatusCode == HttpStatusCode.Accepted, await repeated.Content.ReadAsStringAsync());
        Assert.Equal(commandsBeforeReplay, costPersistence.Count);
        Assert.Equal(persisted, await ScalarAsync<string>(persistedSql, request.DocumentId));
    }

    [Fact]
    public async Task Invalid_cost_allocation_rolls_back_acceptance_and_the_same_request_can_be_confirmed_once()
    {
        var request = CreateRequest();
        var costId = Guid.NewGuid();
        request = request with { AdditionalCostDocuments = [new GoodsReceiptCostDocumentRequest(
            costId, fixture.SupplierId, PurchaseEvidenceTypes.SupplierElectronicInvoice,
            $"FLETE-ROLLBACK-{costId:N}", request.ReceivedAt, true, request.ReceivedAt.AddDays(30),
            "COP", 1m, DateOnly.FromDateTime(request.ReceivedAt.Date), "FunctionalCurrency",
            [new GoodsReceiptCostLineRequest(1, PurchaseCostKinds.Freight, "Cargo con reparto",
                5_000m, 0m, "IVA-0", 0m, 0m, PurchasingTaxTreatments.NotApplicable,
                PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Value)])] };
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        Assert.StartsWith("AuralyServerSlice_", database);
        using (var fault = new InventoryBalanceProcessingTests.CommandCounter(database,
            commandFilter: command => command.CommandText == "purchasing.GoodsReceiptCostDocumentsInsert" &&
                command.Parameters["@GoodsReceiptId"].Value is Guid id && id == request.DocumentId,
            beforeExecute: command =>
            {
                var documents = JsonNode.Parse((string)command.Parameters["@DocumentsJson"].Value)!.AsArray();
                documents[0]!["Lines"]![0]!["Allocations"]![0]!["FunctionalAmount"] = -1m;
                command.Parameters["@DocumentsJson"].Value = documents.ToJsonString();
            }))
        {
            using var message = CreateMessage(request, $"cost-rollback-{request.DocumentId:N}");
            using var response = await client.SendAsync(message);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(1, fault.Count);
        }
        Assert.Equal(0, await ScalarAsync<int>("""
            SELECT (SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id)
              +(SELECT COUNT(*) FROM dbo.GoodsReceiptLines WHERE GoodsReceiptId=@Id)
              +(SELECT COUNT(*) FROM purchasing.GoodsReceiptCostDocuments WHERE GoodsReceiptId=@Id)
              +(SELECT COUNT(*) FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id)
              +(SELECT COUNT(*) FROM dbo.DocumentProcessingPayloads WHERE DocumentId=@Id)
              +(SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id)
              +(SELECT COUNT(*) FROM dbo.Payables WHERE ParentGoodsReceiptId=@Id);
            """, request.DocumentId));
        Assert.Equal(0, await ScalarAsync<int>("""
            SELECT (SELECT COUNT(*) FROM purchasing.GoodsReceiptCostLines WHERE CostDocumentId=@Id)
              +(SELECT COUNT(*) FROM purchasing.GoodsReceiptCostAllocations WHERE CostDocumentId=@Id)
              +(SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id);
            """, costId));
        using var retry = CreateMessage(request, $"cost-rollback-{request.DocumentId:N}");
        using var accepted = await client.SendAsync(retry);
        Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
        Assert.Equal("Completed", (await ReadJobAsync(request.DocumentId)).Status);
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.GoodsReceiptCostDocuments WHERE GoodsReceiptId=@Id", request.DocumentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.GoodsReceiptCostAllocations WHERE GoodsReceiptId=@Id", request.DocumentId));
    }

    [Fact]
    public async Task Freight_not_attributable_to_merchandise_posts_expense_without_increasing_inventory()
    {
        var request = CreateRequest();
        var freightId = Guid.NewGuid();
        request = request with
        {
            AdditionalCostDocuments =
            [new GoodsReceiptCostDocumentRequest(
                freightId, fixture.SupplierId, PurchaseEvidenceTypes.SupplierElectronicInvoice,
                $"GASTO-FLETE-{freightId:N}", request.ReceivedAt, true,
                request.ReceivedAt.AddDays(30), "COP", 1m,
                DateOnly.FromDateTime(request.ReceivedAt.Date), "FunctionalCurrency",
                [new GoodsReceiptCostLineRequest(
                    1, PurchaseCostKinds.Freight, "Transporte posterior a la compra",
                    10_000m, 10_000m, "01", 19m, 1_900m,
                    PurchasingTaxTreatments.DeductibleInputVat,
                    PurchaseCostTreatments.Expense, PurchaseCostAllocationMethods.None)])]
        };
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts,
            PayablesPermissionCodes.Read);
        using var message = CreateMessage(request, $"expensed-freight-{request.DocumentId:N}");
        using var response = await client.SendAsync(message);
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            await response.Content.ReadAsStringAsync());
        Assert.Equal("Completed", (await ReadJobAsync(request.DocumentId)).Status);
        Assert.Equal("Posted", await ScalarAsync<string>(
            "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostDocument'",
            freightId));
        Assert.Equal(50_000m, await ScalarAsync<decimal>(
            "SELECT SUM(ValueChange) FROM dbo.InventoryMovements WHERE DocumentId=@Id AND DocumentType=N'GoodsReceipt'",
            request.DocumentId));
        Assert.Equal(50_000m, await CombinedAccountAmountAsync(
            request.DocumentId, "143505", debit: true));
        Assert.Equal(10_000m, await CombinedAccountAmountAsync(
            request.DocumentId, "513550", debit: true));
        Assert.Equal(11_400m, await CombinedAccountAmountAsync(
            request.DocumentId, "240810", debit: true));
        Assert.Equal(71_400m, await CombinedAccountAmountAsync(
            request.DocumentId, "220505", debit: false));
    }

    [Fact]
    public async Task Internal_additional_voucher_cannot_create_deductible_vat_or_a_partial_receipt()
    {
        var request = CreateRequest();
        var voucherId = Guid.NewGuid();
        request = request with
        {
            AdditionalCostDocuments =
            [new GoodsReceiptCostDocumentRequest(
                voucherId, fixture.SupplierId, PurchaseEvidenceTypes.InternalReceiptVoucher,
                $"INTERNO-{voucherId:N}", request.ReceivedAt, true,
                request.ReceivedAt.AddDays(30), "COP", 1m,
                DateOnly.FromDateTime(request.ReceivedAt.Date), "FunctionalCurrency",
                [new GoodsReceiptCostLineRequest(
                    1, PurchaseCostKinds.Freight, "Flete sin soporte fiscal",
                    10_000m, 10_000m, "01", 19m, 1_900m,
                    PurchasingTaxTreatments.DeductibleInputVat,
                    PurchaseCostTreatments.Expense, PurchaseCostAllocationMethods.None)])]
        };
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        using var message = CreateMessage(request, $"internal-vat-{request.DocumentId:N}");
        using var response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no puede registrar IVA descontable",
            await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id",
            request.DocumentId));
        Assert.Equal(0, await CountAsync("Payables", voucherId));
    }

    [Fact]
    public async Task Foreign_invoice_and_import_declaration_use_the_negotiated_rate_and_post_import_vat_once()
    {
        var request = CreateRequest();
        var declarationId = Guid.NewGuid();
        request = request with
        {
            SupplierInvoiceNumber = $"USD-{Guid.NewGuid():N}",
            PurchaseEvidenceType = PurchaseEvidenceTypes.ForeignCommercialInvoice,
            CurrencyCode = "USD",
            ExchangeRate = 4_000m,
            ExchangeRateDate = DateOnly.FromDateTime(request.ReceivedAt.Date),
            ExchangeRateSource = "Negotiated",
            Lines =
            [
                request.Lines.Single() with
                {
                    UnitCost = 2m,
                    DiscountAmount = 0m,
                    TaxCode = "00",
                    TaxRate = 0m,
                    TaxTreatment = PurchasingTaxTreatments.NotApplicable
                }
            ],
            AdditionalCostDocuments =
            [
                new GoodsReceiptCostDocumentRequest(
                    declarationId, fixture.SupplierId,
                    PurchaseEvidenceTypes.ImportDeclaration,
                    $"DECL-{Guid.NewGuid():N}", request.ReceivedAt, true,
                    request.ReceivedAt.AddDays(30), "COP", 1m,
                    DateOnly.FromDateTime(request.ReceivedAt.Date), "FunctionalCurrency",
                    [
                        new GoodsReceiptCostLineRequest(
                            1, PurchaseCostKinds.CustomsDuty, "Arancel", 10_000m,
                            0m, "00", 0m, 0m,
                            PurchasingTaxTreatments.NotApplicable,
                            PurchaseCostTreatments.Capitalize,
                            PurchaseCostAllocationMethods.Value),
                        new GoodsReceiptCostLineRequest(
                            2, PurchaseCostKinds.ImportVat, "IVA de importación", 0m,
                            90_000m, "01", 19m, 17_100m,
                            PurchasingTaxTreatments.DeductibleInputVat,
                            PurchaseCostTreatments.Expense,
                            PurchaseCostAllocationMethods.None)
                    ])
            ]
        };
        var quantityBefore = await ReadNullableDecimalAsync("QuantityOnHand") ?? 0m;
        var valueBefore = await ReadNullableDecimalAsync("InventoryValue") ?? 0m;
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts,
            PayablesPermissionCodes.Read);

        using var message = CreateMessage(request, $"import-{request.DocumentId:N}");
        using var response = await client.SendAsync(message);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            $"Expected import receipt to be accepted, got {response.StatusCode}: {body}");

        Assert.Equal(quantityBefore + 10m, await ReadNullableDecimalAsync("QuantityOnHand"));
        // The shared fixture can already have a fractional average. Reconcile
        // its actual book-value delta and the existing rounding counterpart;
        // the acquisition and supplier invoice amounts remain exact.
        var inventoryChange = (await ReadNullableDecimalAsync("InventoryValue"))!.Value - valueBefore;
        Assert.Equal(90_000m, await ScalarAsync<decimal>(
            "SELECT SUM(RecognizedInventoryCostAmount) FROM dbo.GoodsReceiptLines WHERE GoodsReceiptId=@Id",
            request.DocumentId));
        Assert.Equal(inventoryChange, await ScalarAsync<decimal>(
            "SELECT SUM(ValueChange) FROM dbo.InventoryMovements WHERE DocumentId=@Id AND DocumentType=N'GoodsReceipt'",
            request.DocumentId));
        Assert.Equal(inventoryChange,
            await CombinedAccountAmountAsync(request.DocumentId, "143505", debit: true) -
            await CombinedAccountAmountAsync(request.DocumentId, "143505", debit: false));
        Assert.Equal(90_000m - inventoryChange,
            await CombinedAccountAmountAsync(request.DocumentId, "613595", debit: true) -
            await CombinedAccountAmountAsync(request.DocumentId, "613595", debit: false));
        Assert.Equal(20m, await ScalarAsync<decimal>(
            "SELECT OriginalAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
            request.DocumentId));
        Assert.Equal(80_000m, await ScalarAsync<decimal>(
            "SELECT FunctionalOriginalAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
            request.DocumentId));
        Assert.Equal(4_000m, await ScalarAsync<decimal>(
            "SELECT ExchangeRate FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
            request.DocumentId));
        Assert.Equal(27_100m, await ScalarAsync<decimal>(
            "SELECT OriginalAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostDocument'",
            declarationId));
        Assert.Equal(10_000m, await ScalarAsync<decimal>(
            "SELECT SUM(FunctionalAmount) FROM purchasing.GoodsReceiptCostAllocations WHERE CostDocumentId=@Id",
            declarationId));
        Assert.Equal(17_100m, await ScalarAsync<decimal>(
            "SELECT FunctionalTaxAmount FROM purchasing.GoodsReceiptCostDocuments WHERE CostDocumentId=@Id",
            declarationId));
        Assert.Equal("Posted", await ScalarAsync<string>(
            "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostDocument'",
            declarationId));
        using var payableResponse = await client.GetAsync(
            $"/api/commerce/v1/payables?page=1&pageSize=20&supplierId={fixture.SupplierId:D}");
        Assert.True(payableResponse.IsSuccessStatusCode,
            await payableResponse.Content.ReadAsStringAsync());
        var payablePage = await payableResponse.Content.ReadFromJsonAsync<PayablePage>();
        Assert.NotNull(payablePage);
        Assert.Contains(payablePage.CurrencyTotals, item => item.CurrencyCode == "USD"
            && item.OutstandingAmount >= 20m);
        var copTotal = Assert.Single(payablePage.CurrencyTotals.Where(item => item.CurrencyCode == "COP"));
        Assert.Equal(copTotal.OutstandingAmount, payablePage.TotalOutstanding);
        using var supplierResponse = await client.GetAsync(
            $"/api/commerce/v1/payables/suppliers?page=1&pageSize=20&supplierId={fixture.SupplierId:D}");
        Assert.True(supplierResponse.IsSuccessStatusCode,
            await supplierResponse.Content.ReadAsStringAsync());
        var supplierPage = await supplierResponse.Content.ReadFromJsonAsync<SupplierPortfolioPage>();
        Assert.NotNull(supplierPage);
        Assert.Contains(supplierPage.Items, item => item.CurrencyCode == "USD"
            && item.OutstandingAmount >= 20m);
        Assert.Equal(supplierPage.CurrencyTotals.Single(item => item.CurrencyCode == "COP").OutstandingAmount,
            supplierPage.TotalOutstanding);
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow);
        var report = await client.GetFromJsonAsync<PayablesReportPage>(
            $"/api/commerce/v1/payables/report?page=1&pageSize=20&consolidated=true&cutoff={cutoff:yyyy-MM-dd}&supplierId={fixture.SupplierId:D}");
        Assert.Contains(report!.CurrencyTotals, item => item.CurrencyCode == "USD" && item.OutstandingAmount >= 20m);
        Assert.Contains(report.CurrencyTotals, item => item.CurrencyCode == "COP" && item.OutstandingAmount >= 27_100m);
        var sortedByCurrency = await client.GetFromJsonAsync<PayablesReportPage>(
            $"/api/commerce/v1/payables/report?page=1&pageSize=20&consolidated=true&cutoff={cutoff:yyyy-MM-dd}&supplierId={fixture.SupplierId:D}&sortBy=currency&sortDirection=asc");
        Assert.Equal(sortedByCurrency!.Items.OrderBy(item => item.CurrencyCode, StringComparer.Ordinal)
            .Select(item => item.CurrencyCode), sortedByCurrency.Items.Select(item => item.CurrencyCode));
        var printedReport = await client.GetStringAsync(
            $"/api/commerce/v1/payables/report/print?consolidated=true&cutoff={cutoff:yyyy-MM-dd}&supplierId={fixture.SupplierId:D}");
        Assert.Contains("USD", printedReport);
        Assert.Contains("COP", printedReport);
        Assert.Contains(report.Items, item => item.CurrencyCode == "USD" && item.OutstandingAmount >= 20m);
        Assert.Contains(report.Items, item => item.CurrencyCode == "COP" && item.OutstandingAmount >= 27_100m);
    }

    [Fact]
    public async Task Zero_value_credit_receipt_processes_inventory_without_opening_an_empty_payable()
    {
        var request = CreateRequest();
        request = request with
        {
            DocumentId = Guid.NewGuid(),
            Lines = [request.Lines.Single() with { UnitCost = 0m, DiscountAmount = 0m }]
        };
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        using var message = CreateMessage(request, $"zero-value-{request.DocumentId:N}");
        using var response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var job = await ReadJobAsync(request.DocumentId);
        Assert.Equal("Completed", job.Status);
        Assert.Equal(1, await CountAsync("InventoryMovements", request.DocumentId));
        Assert.Equal(0, await CountAsync("Payables", request.DocumentId));
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var accounting = new SqlCommand("""
            SELECT Status,LastErrorCode,AttemptCount,
              (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id)
            FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt';
            """, connection);
        accounting.Parameters.AddWithValue("@Id", request.DocumentId);
        await using (var reader = await accounting.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal("CommercialEffectsApplied", reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
            Assert.Equal(1, reader.GetInt32(2));
            Assert.Equal(0, reader.GetInt32(3));
        }
        using var replay = CreateMessage(request, $"zero-value-{request.DocumentId:N}");
        using var replayResponse = await client.SendAsync(replay);
        Assert.Equal(HttpStatusCode.Accepted, replayResponse.StatusCode);
        Assert.Equal(1, await CountAsync("InventoryMovements", request.DocumentId));
    }

    [Fact]
    public async Task Positive_cost_after_a_zero_cost_receipt_does_not_block_the_business_sequence()
    {
        var zeroCost = CreateRequest() with
        {
            DocumentId = Guid.NewGuid(),
            Lines = [CreateRequest().Lines.Single() with { UnitCost = 0m, DiscountAmount = 0m }]
        };
        var positiveCost = CreateRequest() with { DocumentId = Guid.NewGuid() };
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);

        using (var message = CreateMessage(zeroCost, $"zero-before-positive-{zeroCost.DocumentId:N}"))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using (var message = CreateMessage(positiveCost, $"positive-after-zero-{positiveCost.DocumentId:N}"))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        Assert.Equal("Completed", (await ReadJobAsync(zeroCost.DocumentId)).Status);
        Assert.Equal("Completed", (await ReadJobAsync(positiveCost.DocumentId)).Status);
        Assert.Equal(1, await CountAsync("InventoryMovements", positiveCost.DocumentId));
        Assert.Equal(1, await CountAsync("PriceRevisionProposals", positiveCost.DocumentId));
    }

    [Fact]
    public async Task Buyer_support_document_creates_the_inventory_entry_and_immutable_fiscal_work()
    {
        await ConfigureSupportDocumentAsync();
        var template = CreateRequest();
        var request = template with
        {
            DocumentId = Guid.NewGuid(),
            SupplierInvoiceNumber = null,
            PurchaseEvidenceType = PurchaseEvidenceTypes.BuyerElectronicSupportDocument,
            Lines = [template.Lines.Single() with
            {
                TaxCode = "IVA-0",
                TaxRate = 0m,
                TaxTreatment = PurchasingTaxTreatments.NotApplicable
            }]
        };
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        using var message = CreateMessage(request, $"support-document-{request.DocumentId:N}");
        using var response = await client.SendAsync(message);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            $"Expected support document to be accepted, got {response.StatusCode}: {responseBody}");

        Assert.Equal("Processed", await ScalarAsync<string>(
            "SELECT Status FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", request.DocumentId));
        Assert.Equal(1, await CountAsync("InventoryMovements", request.DocumentId));
        Assert.Equal("SupportDocument", await ScalarAsync<string>(
            "SELECT FiscalDocumentType FROM dbo.FiscalDocuments WHERE DocumentId=@Id", request.DocumentId));
        Assert.Equal("CUDS", await ScalarAsync<string>(
            "SELECT UniqueCodeType FROM dbo.FiscalDocuments WHERE DocumentId=@Id", request.DocumentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM fiscal.PurchaseSupportFiscalSnapshots WHERE DocumentId=@Id", request.DocumentId));
        Assert.Contains("\"dianTaxCode\":\"01\"", await ScalarAsync<string>(
            "SELECT SnapshotJson FROM fiscal.PurchaseSupportFiscalSnapshots WHERE DocumentId=@Id",
            request.DocumentId), StringComparison.Ordinal);
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@Id", request.DocumentId));
        Assert.NotNull(await ScalarAsync<string>(
            "SELECT SupportFiscalNumber FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", request.DocumentId));

        await GenerateFiscalAsync(request.DocumentId);
        var fiscal = await ReadFiscalGenerationAsync(request.DocumentId);
        Assert.Equal(FiscalDocumentStatusCodes.PendingSubmission, fiscal.Status);
        Assert.False(string.IsNullOrWhiteSpace(fiscal.UniqueCode));
        var signedXml = await ReadArtifactTextAsync(request.DocumentId, "SignedXml");
        Assert.Contains(
            $"https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey={fiscal.UniqueCode}</sts:QRCode>",
            signedXml, StringComparison.Ordinal);
        Assert.DoesNotContain("?documentkey=?documentkey=", signedXml,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repeated_additional_supplier_invoice_identifies_previous_receipt_in_spanish()
    {
        var invoiceNumber = $"FLETE-{Guid.NewGuid():N}";
        var first = CreateRequest();
        var second = CreateRequest();
        GoodsReceiptCostDocumentRequest CostDocument(Guid id, DateTimeOffset issuedAt) =>
            new(id, fixture.SupplierId, PurchaseEvidenceTypes.SupplierElectronicInvoice,
                invoiceNumber, issuedAt, true, issuedAt.AddDays(30), "COP", 1m,
                DateOnly.FromDateTime(issuedAt.Date), "FunctionalCurrency",
                [new GoodsReceiptCostLineRequest(1, PurchaseCostKinds.Freight, "Flete", 5_000m,
                    0m, "IVA-0", 0m, 0m, PurchasingTaxTreatments.NotApplicable,
                    PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Value)]);
        first = first with { AdditionalCostDocuments = [CostDocument(Guid.NewGuid(), first.ReceivedAt)] };
        second = second with { AdditionalCostDocuments = [CostDocument(Guid.NewGuid(), second.ReceivedAt)] };
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        using (var message = CreateMessage(first, $"first-cost-{first.DocumentId:N}"))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var firstNumber = await ScalarAsync<string>(
            "SELECT DocumentNumber FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", first.DocumentId);
        using var repeated = CreateMessage(second, $"repeated-cost-{second.DocumentId:N}");
        using var rejected = await client.SendAsync(repeated);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var body = await rejected.Content.ReadAsStringAsync();
        Assert.Contains(invoiceNumber, body, StringComparison.Ordinal);
        Assert.Contains(firstNumber, body, StringComparison.Ordinal);
        Assert.Contains("ya está registrada", body, StringComparison.Ordinal);
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", second.DocumentId));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    public async Task Additional_support_documents_use_the_series_valid_on_each_issue_date(int newSeriesDocuments)
    {
        await ConfigureSupportDocumentAsync();
        var newAuthorizationId = Guid.NewGuid();
        var newSeriesId = Guid.NewGuid();
        var newPrefix = $"NX{newSeriesDocuments}";
        Guid previousAuthorizationId;
        DateTime previousValidUntil;
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var select = new SqlCommand("""
                SELECT TOP(1) auth.FiscalAuthorizationId,auth.ValidUntil
                FROM dbo.FiscalSeries series
                JOIN dbo.FiscalAuthorizations auth
                  ON auth.FiscalAuthorizationId=series.FiscalAuthorizationId
                WHERE series.BusinessId=@BusinessId AND series.DocumentType=N'SupportDocument'
                  AND series.EmitterKind=N'Server' AND series.IsActive=1
                ORDER BY auth.ValidUntil DESC;
                """, connection);
            select.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            await using var reader = await select.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            previousAuthorizationId = reader.GetGuid(0);
            previousValidUntil = reader.GetDateTime(1);
        }
        try
        {
            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var configure = new SqlCommand("""
                    UPDATE dbo.FiscalAuthorizations SET ValidUntil='2026-07-30'
                    WHERE FiscalAuthorizationId=@PreviousAuthorizationId;
                    INSERT dbo.FiscalAuthorizations(FiscalAuthorizationId,BusinessId,AuthorizationNumber,
                      SupplierTaxId,Environment,QrValidationUrl,TechnicalKeyVersion,ValidFrom,ValidUntil,
                      AuthorizedRangeStart,AuthorizedRangeEnd,IsActive,CreatedAt)
                    VALUES(@AuthorizationId,@BusinessId,@AuthorizationNumber,@IssuerTaxId,2,
                      N'https://catalogo-vpfe-hab.dian.gov.co/document/searchqr',N'1',
                      '2026-07-31','2028-12-31',1,999,1,SYSDATETIMEOFFSET());
                    INSERT dbo.FiscalSeries(SeriesId,BusinessId,DeviceId,EmitterKind,FiscalAuthorizationId,
                      DocumentType,Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
                    VALUES(@SeriesId,@BusinessId,NULL,N'Server',@AuthorizationId,
                      N'SupportDocument',@NewPrefix,1,999,1,SYSDATETIMEOFFSET());
                    """, connection);
                configure.Parameters.AddWithValue("@PreviousAuthorizationId", previousAuthorizationId);
                configure.Parameters.AddWithValue("@AuthorizationId", newAuthorizationId);
                configure.Parameters.AddWithValue("@SeriesId", newSeriesId);
                configure.Parameters.AddWithValue("@NewPrefix", newPrefix);
                configure.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                configure.Parameters.AddWithValue("@AuthorizationNumber", $"MIXED-{newAuthorizationId:N}");
                configure.Parameters.AddWithValue("@IssuerTaxId", ServerSliceFixture.SupplierTaxId);
                await configure.ExecuteNonQueryAsync();
            }

            var request = CreateRequest();
            var costDocumentId = Guid.NewGuid();
            var secondCostDocumentId = Guid.NewGuid();
            request = request with
            {
                SupplierInvoiceNumber = null,
                PurchaseEvidenceType = PurchaseEvidenceTypes.BuyerElectronicSupportDocument,
                Lines = [request.Lines.Single() with
                {
                    TaxCode = "IVA-0", TaxRate = 0m,
                    TaxTreatment = PurchasingTaxTreatments.NotApplicable
                }],
                AdditionalCostDocuments = [new GoodsReceiptCostDocumentRequest(
                    costDocumentId, fixture.SupplierId,
                    PurchaseEvidenceTypes.BuyerElectronicSupportDocument, "", request.ReceivedAt,
                    true, request.ReceivedAt.AddDays(30), "COP", 1m,
                    DateOnly.FromDateTime(request.ReceivedAt.Date), "FunctionalCurrency",
                    [new GoodsReceiptCostLineRequest(1, PurchaseCostKinds.Freight,
                        "Flete con nueva resolución", 10_000m, 10_000m,
                        "IVA-0", 0m, 0m, PurchasingTaxTreatments.NotApplicable,
                        PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Value)]),
                    new GoodsReceiptCostDocumentRequest(
                        secondCostDocumentId, fixture.SupplierId,
                        PurchaseEvidenceTypes.BuyerElectronicSupportDocument, "", request.ReceivedAt,
                        true, request.ReceivedAt.AddDays(30), "COP", 1m,
                        DateOnly.FromDateTime(request.ReceivedAt.Date), "FunctionalCurrency",
                        [new GoodsReceiptCostLineRequest(1, PurchaseCostKinds.Freight,
                            "Segundo flete con nueva resolución", 2_000m, 2_000m,
                            "IVA-0", 0m, 0m, PurchasingTaxTreatments.NotApplicable,
                            PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Value)])]
            };
            var originalCosts = request.AdditionalCostDocuments!.ToArray();
            request = request with
            {
                AdditionalCostDocuments = originalCosts.Concat(
                    Enumerable.Range(2, newSeriesDocuments - 2).Select(_ =>
                        originalCosts[1] with { CostDocumentId = Guid.NewGuid() })).ToArray()
            };
            using var client = fixture.CreateAdminClient(
                PurchasingPermissionCodes.CreateGoodsReceipts,
                PurchasingPermissionCodes.ConfirmGoodsReceipts);
            using var message = CreateMessage(request, $"mixed-support-series-{request.DocumentId:N}");
            using var lookup = new InventoryBalanceProcessingTests.CommandCounter(
                new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
                commandFilter: command => command.CommandText.Contains("fs.FiscalAuthorizationId", StringComparison.Ordinal)
                    && command.CommandText.Contains("dbo.PartyContacts", StringComparison.Ordinal));
            using var acceptance = new InventoryBalanceProcessingTests.CommandCounter(
                new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
                commandFilter: command => command.CommandType == CommandType.StoredProcedure &&
                    command.CommandText == "dbo.AccountingSourcesAccept");
            using var withholding = new InventoryBalanceProcessingTests.CommandCounter(
                new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
                commandFilter: command => command.CommandText.Contains(
                    "INSERT dbo.DocumentWithholdingSnapshots", StringComparison.Ordinal));
            var fiscalBatchSizes = new List<int>();
            using var fiscalPersistence = new InventoryBalanceProcessingTests.CommandCounter(
                new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
                commandFilter: command => command.CommandText.Contains(
                    "INSERT fiscal.PurchaseSupportFiscalSnapshots", StringComparison.Ordinal),
                beforeExecute: command =>
                {
                    using var sourceBatch = JsonDocument.Parse((string)command.Parameters["@SourcesJson"].Value);
                    fiscalBatchSizes.Add(sourceBatch.RootElement.GetArrayLength());
                });
            using var response = await client.SendAsync(message);
            Assert.True(response.StatusCode == HttpStatusCode.Accepted,
                await response.Content.ReadAsStringAsync());
            Assert.Equal(1, lookup.Count);
            Assert.Equal((1, 1), (acceptance.Count, withholding.Count));
            Assert.Equal(2, fiscalPersistence.Count);
            Assert.Equal(new[] { 1, newSeriesDocuments }, fiscalBatchSizes.Order().ToArray());
            var primaryNumber = await ScalarAsync<string>(
                "SELECT FiscalNumber FROM dbo.FiscalDocuments WHERE DocumentId=@Id", request.DocumentId);
            Assert.StartsWith("DS", primaryNumber);
            Assert.False(primaryNumber.StartsWith(newPrefix, StringComparison.Ordinal));
            await using var fiscalConnection = new SqlConnection(fixture.ConnectionString);
            await fiscalConnection.OpenAsync();
            await using var fiscalDocuments = new SqlCommand("""
                SELECT fiscal.FiscalNumber FROM OPENJSON(@Documents) WITH(Id uniqueidentifier '$') requested
                JOIN dbo.FiscalDocuments fiscal ON fiscal.DocumentId=requested.Id AND fiscal.BusinessId=@BusinessId;
                """, fiscalConnection);
            fiscalDocuments.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            fiscalDocuments.Parameters.Add("@Documents", SqlDbType.NVarChar, -1).Value =
                JsonSerializer.Serialize(request.AdditionalCostDocuments.Select(cost => cost.CostDocumentId));
            var numbers = new List<string>();
            await using (var reader = await fiscalDocuments.ExecuteReaderAsync())
                while (await reader.ReadAsync()) numbers.Add(reader.GetString(0));
            Assert.Equal(newSeriesDocuments, numbers.Count);
            Assert.Equal(newSeriesDocuments, numbers.Distinct(StringComparer.Ordinal).Count());
            Assert.All(numbers, number => Assert.StartsWith(newPrefix, number));
        }
        finally
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var restore = new SqlCommand("""
                UPDATE dbo.FiscalAuthorizations SET ValidUntil=@PreviousValidUntil
                WHERE FiscalAuthorizationId=@PreviousAuthorizationId;
                UPDATE dbo.FiscalSeries SET IsActive=0 WHERE SeriesId=@SeriesId;
                UPDATE dbo.FiscalAuthorizations SET IsActive=0 WHERE FiscalAuthorizationId=@AuthorizationId;
                """, connection);
            restore.Parameters.AddWithValue("@PreviousValidUntil", previousValidUntil);
            restore.Parameters.AddWithValue("@PreviousAuthorizationId", previousAuthorizationId);
            restore.Parameters.AddWithValue("@SeriesId", newSeriesId);
            restore.Parameters.AddWithValue("@AuthorizationId", newAuthorizationId);
            await restore.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Support_root_failure_rolls_back_the_whole_acceptance_and_retries_once()
    {
        await ConfigureSupportDocumentAsync();
        var baseRequest = CreateRequest();
        var costs = Enumerable.Range(0, 2).Select(_ => new GoodsReceiptCostDocumentRequest(
            Guid.NewGuid(), fixture.SupplierId, PurchaseEvidenceTypes.BuyerElectronicSupportDocument, "",
            baseRequest.ReceivedAt, true, baseRequest.ReceivedAt.AddDays(30), "COP", 1m,
            DateOnly.FromDateTime(baseRequest.ReceivedAt.Date), "FunctionalCurrency",
            [new(1, PurchaseCostKinds.Freight, "Flete con soporte", 1_000m, 1_000m, "IVA-0", 0m, 0m,
                PurchasingTaxTreatments.NotApplicable, PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Value)])).ToArray();
        var request = baseRequest with
        {
            SupplierInvoiceNumber = null, PurchaseEvidenceType = PurchaseEvidenceTypes.BuyerElectronicSupportDocument,
            Lines = [baseRequest.Lines.Single() with { TaxCode = "IVA-0", TaxRate = 0m, TaxTreatment = PurchasingTaxTreatments.NotApplicable }],
            AdditionalCostDocuments = costs
        };
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts, PurchasingPermissionCodes.ConfirmGoodsReceipts);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        var trigger = $"TestRejectSupport_{Guid.NewGuid():N}";
        await using (var create = new SqlCommand($"""
            CREATE TRIGGER fiscal.[{trigger}] ON fiscal.PurchaseSupportFiscalSnapshots AFTER INSERT AS
            BEGIN
              IF EXISTS(SELECT 1 FROM inserted WHERE DocumentId='{costs[^1].CostDocumentId:D}')
                THROW 51997,'Injected support batch failure.',1;
            END;
            """, connection)) await create.ExecuteNonQueryAsync();
        try
        {
            using var failedMessage = CreateMessage(request, request.DocumentId.ToString("N"));
            using var failed = await client.SendAsync(failedMessage);
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            await using var verify = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM dbo.FiscalDocuments WHERE DocumentId IN(SELECT value FROM OPENJSON(@Ids))) +
                  (SELECT COUNT(*) FROM fiscal.PurchaseSupportFiscalSnapshots WHERE DocumentId IN(SELECT value FROM OPENJSON(@Ids))) +
                  (SELECT COUNT(*) FROM dbo.FiscalDocumentProcesses WHERE DocumentId IN(SELECT value FROM OPENJSON(@Ids))) +
                  (SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id) +
                  (SELECT COUNT(*) FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id) +
                  (SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId IN(SELECT value FROM OPENJSON(@Ids)));
                """, connection);
            verify.Parameters.AddWithValue("@Id", request.DocumentId);
            verify.Parameters.Add("@Ids", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
                costs.Select(cost => cost.CostDocumentId).Prepend(request.DocumentId));
            Assert.Equal(0, (int)(await verify.ExecuteScalarAsync())!);
        }
        finally
        {
            await using var drop = new SqlCommand($"DROP TRIGGER fiscal.[{trigger}];", connection);
            await drop.ExecuteNonQueryAsync();
        }
        using (var retryMessage = CreateMessage(request, request.DocumentId.ToString("N")))
        using (var retried = await client.SendAsync(retryMessage))
            Assert.True(retried.StatusCode == HttpStatusCode.Accepted, await retried.Content.ReadAsStringAsync());
        using (var replayMessage = CreateMessage(request, request.DocumentId.ToString("N")))
        using (var replay = await client.SendAsync(replayMessage))
            Assert.True((await replay.Content.ReadFromJsonAsync<GoodsReceiptAcceptance>())!.IdempotentReplay);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.FiscalDocuments WHERE DocumentId=@Id", request.DocumentId));
        await using var complete = new SqlCommand("""
            SELECT COUNT(*) FROM fiscal.PurchaseSupportFiscalSnapshots
            WHERE DocumentId IN(SELECT value FROM OPENJSON(@Ids));
            """, connection);
        complete.Parameters.Add("@Ids", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
            costs.Select(cost => cost.CostDocumentId).Prepend(request.DocumentId));
        Assert.Equal(3, (int)(await complete.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Additional_support_document_posts_its_payable_and_generates_its_own_cuds()
    {
        await ConfigureSupportDocumentAsync();
        var request = CreateRequest();
        var costDocumentId = Guid.NewGuid();
        request = request with
        {
            SupplierInvoiceNumber = null,
            PurchaseEvidenceType = PurchaseEvidenceTypes.BuyerElectronicSupportDocument,
            Lines = [request.Lines.Single() with
            {
                TaxCode = "IVA-0",
                TaxRate = 0m,
                TaxTreatment = PurchasingTaxTreatments.NotApplicable
            }],
            AdditionalCostDocuments =
            [new GoodsReceiptCostDocumentRequest(costDocumentId, fixture.SupplierId,
                PurchaseEvidenceTypes.BuyerElectronicSupportDocument, "", request.ReceivedAt,
                true, request.ReceivedAt.AddDays(30), "COP", 1m,
                DateOnly.FromDateTime(request.ReceivedAt.Date), "FunctionalCurrency",
                [new GoodsReceiptCostLineRequest(1, PurchaseCostKinds.Freight,
                    "Flete sin factura electrónica", 10_000m, 10_000m,
                    "IVA-19", 19m, 1_900m, PurchasingTaxTreatments.DeductibleInputVat,
                    PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Value)])]
        };
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var manuallyNumbered = request with
        {
            AdditionalCostDocuments = [request.AdditionalCostDocuments!.Single() with
            { DocumentNumber = "MANUAL-IGNORED" }]
        };
        using (var invalid = CreateMessage(manuallyNumbered,
                   $"additional-support-manual-{request.DocumentId:N}"))
        using (var rejected = await client.SendAsync(invalid))
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var message = CreateMessage(request, $"additional-support-{request.DocumentId:N}");
        using var response = await client.SendAsync(message);
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            await response.Content.ReadAsStringAsync());
        Assert.Equal("Completed", (await ReadJobAsync(request.DocumentId)).Status);
        Assert.Equal("Posted", await ScalarAsync<string>(
            "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostDocument'",
            costDocumentId));
        Assert.Equal(11_900m, await ScalarAsync<decimal>(
            "SELECT OriginalAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceiptCostDocument'",
            costDocumentId));
        Assert.Equal("SupportDocument", await ScalarAsync<string>(
            "SELECT FiscalDocumentType FROM dbo.FiscalDocuments WHERE DocumentId=@Id", costDocumentId));
        Assert.Equal("GoodsReceiptCostDocument", await ScalarAsync<string>(
            "SELECT SourceDocumentType FROM dbo.FiscalDocuments WHERE DocumentId=@Id", costDocumentId));
        Assert.Equal("SupportDocument", await ScalarAsync<string>(
            "SELECT FiscalDocumentType FROM dbo.FiscalDocuments WHERE DocumentId=@Id",
            request.DocumentId));
        Assert.NotEqual(await ScalarAsync<string>(
                "SELECT FiscalNumber FROM dbo.FiscalDocuments WHERE DocumentId=@Id",
                request.DocumentId),
            await ScalarAsync<string>(
                "SELECT FiscalNumber FROM dbo.FiscalDocuments WHERE DocumentId=@Id",
                costDocumentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM fiscal.PurchaseSupportFiscalSnapshots WHERE DocumentId=@Id",
            costDocumentId));
        Assert.Equal(10_000m, await ScalarAsync<decimal>("""
            SELECT COALESCE(SUM(line.Debit),0) FROM dbo.AccountingEntries entry
            JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
            JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId
            WHERE entry.SourceDocumentId=@Id AND account.Code=N'143505'
            """, costDocumentId));
        Assert.Equal(1_900m, await ScalarAsync<decimal>("""
            SELECT COALESCE(SUM(line.Debit),0) FROM dbo.AccountingEntries entry
            JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
            JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId
            WHERE entry.SourceDocumentId=@Id AND account.Code=N'240810'
            """, costDocumentId));
        Assert.Equal(11_900m, await ScalarAsync<decimal>("""
            SELECT COALESCE(SUM(line.Credit),0) FROM dbo.AccountingEntries entry
            JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
            JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId
            WHERE entry.SourceDocumentId=@Id AND account.Code=N'220505'
            """, costDocumentId));
        var generationSignals = fixture.DrainFiscalSignals();
        Assert.Contains(generationSignals, item =>
            item.Signal.DocumentId == request.DocumentId &&
            item.Signal.Stage == FiscalProcessingStage.Generation);
        Assert.Contains(generationSignals, item =>
            item.Signal.DocumentId == costDocumentId &&
            item.Signal.Stage == FiscalProcessingStage.Generation);
        await SetReceiptStatusAsync(request.DocumentId, "Accepted");
        try
        {
            using var scope = fixture.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<FiscalGenerationWorker>();
            var workStore = scope.ServiceProvider.GetRequiredService<IFiscalGenerationWorkStore>();
            Assert.False(await worker.ProcessAsync(fixture.BusinessId, request.DocumentId,
                "premature-receipt", CancellationToken.None));
            Assert.False(await worker.ProcessAsync(fixture.BusinessId, costDocumentId,
                "premature-cost", CancellationToken.None));
            Assert.Null(await workStore.GetResumeAtAsync(fixture.BusinessId,
                request.DocumentId, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2),
                CancellationToken.None));
            Assert.Null(await workStore.GetResumeAtAsync(fixture.BusinessId,
                costDocumentId, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2),
                CancellationToken.None));
            Assert.Equal(0, await ScalarAsync<int>(
                "SELECT AttemptCount FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@Id",
                request.DocumentId));
            Assert.Equal(0, await ScalarAsync<int>(
                "SELECT AttemptCount FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@Id",
                costDocumentId));
        }
        finally
        {
            await SetReceiptStatusAsync(request.DocumentId, "Processed");
        }
        using (var scope = fixture.CreateScope())
        {
            var workStore = scope.ServiceProvider.GetRequiredService<IFiscalGenerationWorkStore>();
            Assert.NotNull(await workStore.GetResumeAtAsync(fixture.BusinessId,
                request.DocumentId, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2),
                CancellationToken.None));
            Assert.NotNull(await workStore.GetResumeAtAsync(fixture.BusinessId,
                costDocumentId, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2),
                CancellationToken.None));
        }
        await GenerateFiscalAsync(costDocumentId);
        await GenerateFiscalAsync(request.DocumentId);
        var fiscal = await ReadFiscalGenerationAsync(costDocumentId);
        Assert.Equal(FiscalDocumentStatusCodes.PendingSubmission, fiscal.Status);
        Assert.False(string.IsNullOrWhiteSpace(fiscal.UniqueCode));
        Assert.Contains("Flete sin factura electrónica",
            await ReadArtifactTextAsync(costDocumentId, "SignedXml"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Purchase_return_of_a_support_document_generates_a_type_95_adjustment_with_original_cuds()
    {
        await ConfigureSupportDocumentAsync();
        var receipt = CreateRequest() with
        {
            DocumentId = Guid.NewGuid(),
            SupplierInvoiceNumber = null,
            PurchaseEvidenceType = PurchaseEvidenceTypes.BuyerElectronicSupportDocument
        };
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts,
            PurchasingPermissionCodes.CreatePurchaseReturns,
            PurchasingPermissionCodes.ConfirmPurchaseReturns);
        using (var message = CreateMessage(receipt, $"support-adjustment-source-{receipt.DocumentId:N}"))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains(fixture.DrainFiscalSignals(), item =>
            item.Signal.DocumentId == receipt.DocumentId &&
            item.Signal.Stage == FiscalProcessingStage.Generation);
        await GenerateFiscalAsync(receipt.DocumentId);

        var originalFiscal = await ReadFiscalGenerationAsync(receipt.DocumentId);
        Assert.True(!string.IsNullOrWhiteSpace(originalFiscal.UniqueCode),
            $"Original support document was not generated: {originalFiscal.Status} " +
            $"{originalFiscal.ErrorCode} {originalFiscal.ErrorMessage}");
        var originalCuds = originalFiscal.UniqueCode!;
        Assert.Contains("Invoice", await ReadArtifactTextAsync(receipt.DocumentId, "SignedXml"));

        var returnId = Guid.NewGuid();
        var returnedAt = receipt.ReceivedAt.AddDays(1);
        var purchaseReturn = new ConfirmPurchaseReturnRequest(
            returnId, fixture.BusinessId, receipt.DocumentId, returnedAt,
            "QualityIssue", "Devolución parcial de documento soporte",
            [new PurchaseReturnLineRequest(1, 2m)]);
        using (var message = new HttpRequestMessage(
                   HttpMethod.Post, "/api/commerce/v1/purchase-returns/confirm")
               { Content = JsonContent.Create(purchaseReturn) })
        {
            message.Headers.Add("Idempotency-Key", $"support-adjustment-{returnId:N}");
            using var response = await client.SendAsync(message);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.Accepted,
                $"Expected support adjustment to be accepted, got {response.StatusCode}: {body}");
        }
        Assert.Contains(fixture.DrainFiscalSignals(), item =>
            item.Signal.DocumentId == returnId &&
            item.Signal.Stage == FiscalProcessingStage.Generation);
        await GenerateFiscalAsync(returnId);

        Assert.Equal("SupportDocumentAdjustment", await ScalarAsync<string>(
            "SELECT FiscalDocumentType FROM dbo.FiscalDocuments WHERE DocumentId=@Id", returnId));
        Assert.Equal("CUDS", await ScalarAsync<string>(
            "SELECT UniqueCodeType FROM dbo.FiscalDocuments WHERE DocumentId=@Id", returnId));
        var adjustmentFiscalNumber = await ScalarAsync<string>(
            "SELECT FiscalNumber FROM dbo.FiscalDocuments WHERE DocumentId=@Id", returnId);
        var adjustmentAuralyNumber = await ScalarAsync<string>(
            "SELECT AuralyDocumentNumber FROM dbo.FiscalDocuments WHERE DocumentId=@Id", returnId);
        Assert.StartsWith("DS", adjustmentFiscalNumber, StringComparison.Ordinal);
        Assert.NotEqual(adjustmentAuralyNumber, adjustmentFiscalNumber);
        Assert.NotEqual(await ScalarAsync<string>(
            "SELECT FiscalNumber FROM dbo.FiscalDocuments WHERE DocumentId=@Id",
            receipt.DocumentId), adjustmentFiscalNumber);
        Assert.False(string.IsNullOrWhiteSpace(await ScalarAsync<string>(
            "SELECT UniqueCode FROM dbo.FiscalDocuments WHERE DocumentId=@Id", returnId)));
        var snapshot = await ScalarAsync<string>(
            "SELECT SnapshotJson FROM fiscal.PurchaseSupportFiscalSnapshots WHERE DocumentId=@Id", returnId);
        Assert.Contains(originalCuds, snapshot, StringComparison.Ordinal);
        var signedXml = await ReadArtifactTextAsync(returnId, "SignedXml");
        Assert.Contains("<cbc:CreditNoteTypeCode>95</cbc:CreditNoteTypeCode>", signedXml,
            StringComparison.Ordinal);
        Assert.Contains("<cbc:ProfileID>DIAN 2.1: Nota de ajuste al documento soporte",
            signedXml, StringComparison.Ordinal);
        using (var snapshotJson = System.Text.Json.JsonDocument.Parse(snapshot))
            Assert.Contains($"<cbc:PostalZone>{snapshotJson.RootElement.GetProperty("sellerPostalZone").GetString()}</cbc:PostalZone>",
                signedXml, StringComparison.Ordinal);
        Assert.Contains("schemeName=\"CUDS-SHA384\"", signedXml, StringComparison.Ordinal);
        Assert.Contains(originalCuds, signedXml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expense_uses_selected_evidence_and_only_support_document_is_sent_to_DIAN()
    {
        await ConfigureSupportDocumentAsync();
        await SetSupplierPurchaseEvidencePolicyAsync(
            PurchaseEvidenceTypes.BuyerElectronicSupportDocument);
        try
        {
            using var client = fixture.CreateAdminClient(
                ExpensePermissionCodes.Read,
                ExpensePermissionCodes.Create,
                ExpensePermissionCodes.Configure,
                ExpensePermissionCodes.Cancel);
            var options = await client.GetFromJsonAsync<ExpenseWorkspaceOptions>(
                "/api/commerce/v1/expenses/options")
                ?? throw new InvalidOperationException("Expense options were not returned.");
            var account = options.ExpenseAccounts.First();
            var center = Assert.Single(options.CostCenters, item => item.IsDefault);
            var conceptId = Guid.NewGuid();
            using (var concept = await client.PutAsJsonAsync(
                       $"/api/commerce/v1/expenses/concepts/{conceptId:D}",
                       new SaveExpenseConceptRequest(conceptId, fixture.BusinessId,
                           "Servicio documento soporte",
                           account.AccountId, center.CostCenterId, null, true)))
                Assert.Equal(HttpStatusCode.OK, concept.StatusCode);

            var issuedAt = new DateTimeOffset(2026, 9, 1, 10, 0, 0,
                TimeSpan.FromHours(-5));
            var supportExpenseId = Guid.NewGuid();
            var expenseCommand = new ConfirmExpenseRequest(
                supportExpenseId, fixture.BusinessId, fixture.SupplierId,
                conceptId, null, $"PROV-{supportExpenseId:N}", issuedAt,
                issuedAt.AddDays(30), "COP", "Servicio de proveedor no obligado",
                100_000m, 19_000m, null, null,
                PurchaseEvidenceTypes.BuyerElectronicSupportDocument);
            await using (var db = new SqlConnection(fixture.ConnectionString))
            {
                await db.OpenAsync();
                await using var postal = new SqlCommand("""
                    SELECT PartySiteId,PostalCode FROM dbo.PartySites
                    WHERE PartyId=@PartyId AND IsPrimary=1 AND IsActive=1;
                    """, db);
                postal.Parameters.AddWithValue("@PartyId", fixture.SupplierPartyId);
                Guid primarySiteId;
                string? originalPostalCode;
                await using (var existing = await postal.ExecuteReaderAsync())
                {
                    Assert.True(await existing.ReadAsync());
                    primarySiteId = existing.GetGuid(0);
                    originalPostalCode = existing.IsDBNull(1) ? null : existing.GetString(1);
                }
                await using var update = new SqlCommand("""
                    UPDATE dbo.PartySites SET PostalCode=@PostalCode
                    WHERE PartySiteId=@SiteId;
                    """, db);
                update.Parameters.AddWithValue("@SiteId", primarySiteId);
                var postalParameter = update.Parameters.Add("@PostalCode", System.Data.SqlDbType.NVarChar, 16);
                try
                {
                    postalParameter.Value = DBNull.Value;
                    Assert.Equal(1, await update.ExecuteNonQueryAsync());
                    using var missingPostal = new HttpRequestMessage(HttpMethod.Post,
                        "/api/commerce/v1/expenses/confirm") { Content = JsonContent.Create(expenseCommand) };
                    missingPostal.Headers.Add("Idempotency-Key", $"expense-support-{supportExpenseId:N}");
                    using var rejected = await client.SendAsync(missingPostal);
                    Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                    Assert.Contains("sede principal activa del proveedor",
                        await rejected.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    postalParameter.Value = (object?)originalPostalCode ?? DBNull.Value;
                    Assert.Equal(1, await update.ExecuteNonQueryAsync());
                }
                await using var primary = new SqlCommand("""
                    UPDATE dbo.PartySites SET IsPrimary=@IsPrimary
                    WHERE PartySiteId=@SiteId;
                    """, db);
                primary.Parameters.AddWithValue("@SiteId", primarySiteId);
                var primaryParameter = primary.Parameters.Add("@IsPrimary", System.Data.SqlDbType.Bit);
                try
                {
                    primaryParameter.Value = false;
                    Assert.Equal(1, await primary.ExecuteNonQueryAsync());
                    using var missingPrimary = new HttpRequestMessage(HttpMethod.Post,
                        "/api/commerce/v1/expenses/confirm") { Content = JsonContent.Create(expenseCommand) };
                    missingPrimary.Headers.Add("Idempotency-Key", $"expense-support-{supportExpenseId:N}");
                    using var rejected = await client.SendAsync(missingPrimary);
                    Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                    Assert.Contains("sede principal activa",
                        await rejected.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    primaryParameter.Value = true;
                    Assert.Equal(1, await primary.ExecuteNonQueryAsync());
                }
            }
            using (var missingResolution = new HttpRequestMessage(HttpMethod.Post,
                       "/api/commerce/v1/expenses/confirm")
                   {
                       Content = JsonContent.Create(expenseCommand with
                       {
                           IssuedAt = issuedAt.AddYears(20),
                           DueDate = issuedAt.AddYears(20).AddDays(30)
                       })
                   })
            {
                missingResolution.Headers.Add("Idempotency-Key", $"expense-no-resolution-{supportExpenseId:N}");
                using var rejected = await client.SendAsync(missingResolution);
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                Assert.Contains("resolución DIAN de documento soporte vigente",
                    await rejected.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            }
            using (var request = new HttpRequestMessage(HttpMethod.Post,
                       "/api/commerce/v1/expenses/confirm")
                   {
                       Content = JsonContent.Create(expenseCommand)
                   })
            {
                request.Headers.Add("Idempotency-Key", $"expense-support-{supportExpenseId:N}");
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            }

            Assert.Equal("SupportDocument", await ScalarAsync<string>(
                "SELECT FiscalDocumentType FROM dbo.FiscalDocuments WHERE DocumentId=@Id",
                supportExpenseId));
            Assert.Equal("Expense", await ScalarAsync<string>(
                "SELECT SourceDocumentType FROM dbo.FiscalDocuments WHERE DocumentId=@Id",
                supportExpenseId));
            Assert.Equal(1, await ScalarAsync<int>(
                "SELECT COUNT(*) FROM fiscal.PurchaseSupportFiscalSnapshots WHERE DocumentId=@Id",
                supportExpenseId));
            var snapshot = await ScalarAsync<string>(
                "SELECT SnapshotJson FROM fiscal.PurchaseSupportFiscalSnapshots WHERE DocumentId=@Id",
                supportExpenseId);
            Assert.Contains($"\"expenseId\":\"{supportExpenseId:D}\"", snapshot,
                StringComparison.OrdinalIgnoreCase);
            await GenerateFiscalAsync(supportExpenseId);
            await using (var db = new SqlConnection(fixture.ConnectionString))
            {
                await db.OpenAsync();
                await using var accepted = new SqlCommand("""
                    UPDATE dbo.FiscalDocuments SET FiscalStatus=N'DianAccepted'
                    WHERE DocumentId=@Id AND FiscalDocumentType=N'SupportDocument';
                    """, db);
                accepted.Parameters.AddWithValue("@Id", supportExpenseId);
                Assert.Equal(1, await accepted.ExecuteNonQueryAsync());
            }
            var cancellationId = Guid.NewGuid();
            using (var cancellation = await client.PostAsJsonAsync(
                       $"/api/commerce/v1/expenses/{supportExpenseId:D}/cancel",
                       new CancelExpenseRequest(cancellationId, "El servicio no fue prestado")))
                Assert.True(cancellation.StatusCode == HttpStatusCode.Accepted,
                    await cancellation.Content.ReadAsStringAsync());
            Assert.Equal("SupportDocumentAdjustment", await ScalarAsync<string>(
                "SELECT FiscalDocumentType FROM dbo.FiscalDocuments WHERE DocumentId=@Id", cancellationId));
            Assert.Equal("Cancelled", await ScalarAsync<string>(
                "SELECT Status FROM dbo.Expenses WHERE ExpenseId=@Id", supportExpenseId));
            await GenerateFiscalAsync(cancellationId);
            var adjustmentXml = await ReadArtifactTextAsync(cancellationId, "SignedXml");
            Assert.Contains("<cbc:CreditNoteTypeCode>95</cbc:CreditNoteTypeCode>", adjustmentXml,
                StringComparison.Ordinal);
            Assert.Contains("<cbc:ProfileID>DIAN 2.1: Nota de ajuste al documento soporte",
                adjustmentXml, StringComparison.Ordinal);
            var adjustmentSnapshot = await ScalarAsync<string>(
                "SELECT SnapshotJson FROM fiscal.PurchaseSupportFiscalSnapshots WHERE DocumentId=@Id",
                cancellationId);
            using (var snapshotJson = System.Text.Json.JsonDocument.Parse(adjustmentSnapshot))
                Assert.Contains($"<cbc:PostalZone>{snapshotJson.RootElement.GetProperty("sellerPostalZone").GetString()}</cbc:PostalZone>",
                    adjustmentXml, StringComparison.Ordinal);
            Assert.Contains("<cbc:ResponseCode>2</cbc:ResponseCode>", adjustmentXml,
                StringComparison.Ordinal);

            await SetSupplierPurchaseEvidencePolicyAsync(null);
            var ordinaryExpenseId = Guid.NewGuid();
            using (var request = new HttpRequestMessage(HttpMethod.Post,
                       "/api/commerce/v1/expenses/confirm")
                   {
                       Content = JsonContent.Create(new ConfirmExpenseRequest(
                           ordinaryExpenseId, fixture.BusinessId, fixture.SupplierId,
                           conceptId, null, $"PROV-{ordinaryExpenseId:N}", issuedAt.AddMinutes(1),
                           issuedAt.AddDays(30), "COP", "Gasto con soporte ordinario",
                           50_000m, 0m, null, null,
                           PurchaseEvidenceTypes.InternalReceiptVoucher))
                   })
            {
                request.Headers.Add("Idempotency-Key", $"expense-ordinary-{ordinaryExpenseId:N}");
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            }
            Assert.Equal(0, await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.FiscalDocuments WHERE DocumentId=@Id",
                ordinaryExpenseId));
            Assert.Equal(PurchaseEvidenceTypes.InternalReceiptVoucher,
                await ScalarAsync<string>(
                    "SELECT PurchaseEvidenceType FROM dbo.Expenses WHERE ExpenseId=@Id",
                    ordinaryExpenseId));
            using (var deactivate = await client.PutAsJsonAsync($"/api/commerce/v1/expenses/concepts/{conceptId}",
                new SaveExpenseConceptRequest(conceptId, fixture.BusinessId, "Concepto retirado",
                    account.AccountId, center.CostCenterId, null, false))) deactivate.EnsureSuccessStatusCode();
            using var retry = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/expenses/confirm")
                { Content = JsonContent.Create(expenseCommand) };
            retry.Headers.Add("Idempotency-Key", $"expense-support-{supportExpenseId:N}");
            using var replay = await client.SendAsync(retry);
            replay.EnsureSuccessStatusCode();
            Assert.True((await replay.Content.ReadFromJsonAsync<ExpenseAcceptance>())!.IdempotentReplay);
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id", supportExpenseId));
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", supportExpenseId));
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.FiscalDocuments WHERE DocumentId=@Id", supportExpenseId));
        }
        finally
        {
            await SetSupplierPurchaseEvidencePolicyAsync(null);
        }
    }

    [Fact]
    public async Task Receipt_requires_both_backend_permissions_and_authenticated_business()
    {
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts);
        using (var denied = CreateMessage(CreateRequest(), $"denied-{Guid.NewGuid():N}"))
        using (var response = await client.SendAsync(denied))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var allowed = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var wrongBusiness = CreateRequest() with { BusinessId = Guid.NewGuid() };
        using var message = CreateMessage(wrongBusiness, $"scope-{Guid.NewGuid():N}");
        using var scopedResponse = await allowed.SendAsync(message);
        Assert.Equal(HttpStatusCode.Forbidden, scopedResponse.StatusCode);


        var invalidTax = CreateRequest();
        invalidTax = invalidTax with
        {
            Lines =
            [
                invalidTax.Lines.Single() with { TaxTreatment = "InferredByServer" }
            ]
        };
        using var invalidMessage = CreateMessage(
            invalidTax, $"invalid-tax-{Guid.NewGuid():N}");
        using var invalidResponse = await allowed.SendAsync(invalidMessage);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        var invalidDueDate = CreateRequest();
        invalidDueDate = invalidDueDate with
        {
            DueDate = invalidDueDate.SupplierInvoiceDate!.Value.AddDays(-1)
        };
        using var invalidDueDateMessage = CreateMessage(
            invalidDueDate, $"invalid-due-date-{Guid.NewGuid():N}");
        using var invalidDueDateResponse = await allowed.SendAsync(invalidDueDateMessage);
        Assert.Equal(HttpStatusCode.BadRequest, invalidDueDateResponse.StatusCode);
    }

    [Fact]
    public async Task Receipt_accepts_a_document_due_date_that_overrides_the_supplier_default()
    {
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var request = CreateRequest();
        request = request with { DueDate = request.SupplierInvoiceDate!.Value.AddDays(45) };
        using var message = CreateMessage(request, $"custom-due-date-{Guid.NewGuid():N}");
        using var response = await client.SendAsync(message);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            $"Expected custom due date to be accepted, got {response.StatusCode}: {body}");
    }

    private async Task<JsonElement> ReadValuationEvidenceAsync(Guid documentId)
    {
        var json = await ScalarAsync<string>("""
            SELECT ValuationMode,CostPoolScope,PoolQuantityBefore,PoolQuantityAfter,
              PoolInventoryValueBefore,PoolInventoryValueAfter
            FROM dbo.InventoryMovements
            WHERE DocumentId=@Id AND BusinessId=@BusinessId AND LineNumber=1
              AND DocumentType=N'GoodsReceipt' AND MovementType=N'GoodsReceipt'
            FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
            """, documentId);
        using var parsed = JsonDocument.Parse(json);
        return parsed.RootElement.Clone();
    }

    private ConfirmGoodsReceiptRequest CreateRequest()
    {
        var received = new DateTimeOffset(2026, 7, 31, 11, 30, 0, TimeSpan.FromHours(-5));
        return new ConfirmGoodsReceiptRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            $"FC-{Guid.NewGuid():N}", received.AddDays(-1), received, true,
            received.AddDays(29), "cop", "Entrada E2E",
            [new GoodsReceiptLineRequest(
                1, fixture.ProductId, "Producto E2E", 10m, 6_000m,
                10_000m, "01", 19m, PurchasingTaxTreatments.DeductibleInputVat)]);
    }

    private async Task ConfigureSupportDocumentAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @CountryId UNIQUEIDENTIFIER,@DivisionId UNIQUEIDENTIFIER,@CityId UNIQUEIDENTIFIER;
            SELECT TOP(1) @CountryId=country.CountryId,@DivisionId=division.AdministrativeDivisionId,@CityId=city.CityId
            FROM dbo.Countries country
            INNER JOIN dbo.AdministrativeDivisions division ON division.CountryId=country.CountryId
            INNER JOIN dbo.Cities city ON city.AdministrativeDivisionId=division.AdministrativeDivisionId
            WHERE country.Code=N'CO' ORDER BY city.Code;
            IF @CityId IS NULL THROW 51201,'Colombian geography seed is required.',1;

            IF NOT EXISTS(
              SELECT 1 FROM dbo.TaxProfiles WHERE TenantId=@TenantId AND Code=N'IVA-0')
              INSERT dbo.TaxProfiles(
                TaxProfileId,TenantId,Code,DianTaxCode,Name,Rate,IsActive,CreatedAt)
              VALUES(NEWID(),@TenantId,N'IVA-0',N'01',N'IVA 0%',0,1,SYSDATETIMEOFFSET());
            IF NOT EXISTS(
              SELECT 1 FROM dbo.TaxProfiles WHERE TenantId=@TenantId AND Code=N'IVA-19')
              INSERT dbo.TaxProfiles(
                TaxProfileId,TenantId,Code,DianTaxCode,Name,Rate,IsActive,CreatedAt)
              VALUES(NEWID(),@TenantId,N'IVA-19',N'01',N'IVA 19%',19,1,SYSDATETIMEOFFSET());

            UPDATE party SET IdentificationCountryId=@CountryId,IdentificationTypeCode=N'31',
              Identification=N'900999001',NormalizedIdentification=N'900999001',VerificationDigit=N'1',
              LegalName=N'PROVEEDOR DOCUMENTO SOPORTE',DisplayName=N'PROVEEDOR DOCUMENTO SOPORTE',CompletionStatus=N'Complete'
            FROM dbo.Parties party INNER JOIN dbo.Suppliers supplier ON supplier.PartyId=party.PartyId
            WHERE supplier.SupplierId=@SupplierId;
            IF NOT EXISTS(SELECT 1 FROM dbo.PartySites WHERE PartyId=@PartyId AND IsPrimary=1 AND IsActive=1)
              INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,
                AddressLine,PostalCode,IsPrimary,IsActive,CreatedBy,CreatedAt)
              VALUES(NEWID(),@PartyId,N'PRINCIPAL',N'Sede principal',@CountryId,@DivisionId,@CityId,
                N'Carrera 1 # 2-3',N'200001',1,1,@UserId,SYSDATETIMEOFFSET());

            IF NOT EXISTS(
              SELECT 1 FROM dbo.FiscalSeries
              WHERE BusinessId=@BusinessId AND DeviceId IS NULL AND EmitterKind=N'Server'
                AND DocumentType=N'SupportDocument' AND IsActive=1)
            BEGIN
              INSERT dbo.FiscalAuthorizations(FiscalAuthorizationId,BusinessId,AuthorizationNumber,SupplierTaxId,
                Environment,QrValidationUrl,TechnicalKeyVersion,ValidFrom,ValidUntil,AuthorizedRangeStart,
                AuthorizedRangeEnd,IsActive,CreatedAt)
              VALUES(@AuthorizationId,@BusinessId,@AuthorizationNumber,@IssuerTaxId,2,
                N'https://catalogo-vpfe-hab.dian.gov.co/document/searchqr',N'1',
                '2026-01-01','2028-12-31',1,999,1,SYSDATETIMEOFFSET());
              INSERT dbo.FiscalSeries(SeriesId,BusinessId,DeviceId,EmitterKind,FiscalAuthorizationId,
                DocumentType,Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
              VALUES(@SeriesId,@BusinessId,NULL,N'Server',@AuthorizationId,N'SupportDocument',N'DS',1,999,1,SYSDATETIMEOFFSET());
            END;
            """;
        var authorizationId = Guid.NewGuid();
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@SupplierId", fixture.SupplierId);
        command.Parameters.AddWithValue("@PartyId", fixture.SupplierPartyId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        command.Parameters.AddWithValue("@AuthorizationId", authorizationId);
        command.Parameters.AddWithValue("@SeriesId", Guid.NewGuid());
        command.Parameters.AddWithValue("@AuthorizationNumber",
            DateTimeOffset.UtcNow.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@IssuerTaxId", ServerSliceFixture.SupplierTaxId);
        await command.ExecuteNonQueryAsync();
    }

    private static HttpRequestMessage CreateMessage(
        ConfirmGoodsReceiptRequest request, string idempotencyKey)
    {
        var message = new HttpRequestMessage(
            HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return message;
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Id", documentId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@SupplierId", fixture.SupplierId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        var value = await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The expected SQL scalar was not returned.");
        if (value is DBNull) throw new InvalidOperationException("The expected SQL scalar is null.");
        return (T)Convert.ChangeType(value, typeof(T));
    }

    private async Task<decimal> CombinedAccountAmountAsync(
        Guid receiptId, string accountCode, bool debit)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COALESCE(SUM(line.{(debit ? "Debit" : "Credit")}),0)
            FROM dbo.AccountingEntries entry
            INNER JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
            INNER JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId
            WHERE account.Code=@AccountCode AND (
              entry.SourceDocumentId=@ReceiptId OR
              entry.SourceDocumentId IN (
                SELECT CostDocumentId FROM purchasing.GoodsReceiptCostDocuments
                WHERE GoodsReceiptId=@ReceiptId))
            """;
        command.Parameters.AddWithValue("@ReceiptId", receiptId);
        command.Parameters.AddWithValue("@AccountCode", accountCode);
        return (decimal)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Expected accounting amount."));
    }

    private async Task<string> ReadArtifactTextAsync(Guid documentId, string artifactType)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP(1) Content FROM dbo.FiscalArtifacts
            WHERE DocumentId=@Id AND ArtifactType=@Type
            ORDER BY ArtifactVersion DESC;
            """;
        command.Parameters.AddWithValue("@Id", documentId);
        command.Parameters.AddWithValue("@Type", artifactType);
        var bytes = await command.ExecuteScalarAsync() as byte[]
            ?? throw new InvalidOperationException(
                $"Fiscal artifact {artifactType} was not generated for {documentId:D}.");
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private async Task<FiscalGenerationEvidence> ReadFiscalGenerationAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT document.UniqueCode,process.Status,process.LastErrorCode,process.LastErrorMessage
            FROM dbo.FiscalDocuments document
            INNER JOIN dbo.FiscalDocumentProcesses process ON process.DocumentId=document.DocumentId
            WHERE document.DocumentId=@Id;
            """;
        command.Parameters.AddWithValue("@Id", documentId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("The fiscal generation root was not found.");
        return new FiscalGenerationEvidence(
            reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private async Task GenerateFiscalAsync(Guid documentId)
    {
        using var scope = fixture.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<FiscalGenerationWorker>();
        var generated = await worker.ProcessAsync(
            fixture.BusinessId, documentId, $"support-test-{documentId:N}",
            CancellationToken.None);
        if (generated) return;
        var evidence = await ReadFiscalGenerationAsync(documentId);
        Assert.Fail($"Fiscal generation did not complete: {evidence.Status} " +
                    $"{evidence.ErrorCode} {evidence.ErrorMessage}");
    }

    private async Task SetReceiptStatusAsync(Guid documentId, string status)
    {
        Assert.Contains(status, new[] { "Accepted", "Processed" });
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.GoodsReceipts SET Status=@Status
            WHERE GoodsReceiptId=@DocumentId AND BusinessId=@BusinessId;
            """, connection);
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task<decimal?> ReadNullableDecimalAsync(string column)
    {
        Assert.Contains(column, new[] { "QuantityOnHand", "InventoryValue", "AverageUnitCost" });
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT [{column}] FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId";
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (decimal)value;
    }

    private async Task<decimal> ReadBalanceAsync(Guid businessId, Guid warehouseId, string column)
    {
        Assert.Contains(column, new[] { "QuantityOnHand", "InventoryValue", "AverageUnitCost" });
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT [{column}] FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId";
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@WarehouseId", warehouseId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        return (decimal)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The shared inventory balance was not found."));
    }

    private async Task SetPrimaryCostStateAsync(
        decimal quantity, decimal average, decimal value,
        bool sharesPrices, string costBasis)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF EXISTS(
                SELECT 1 FROM dbo.InventoryBalances
                WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId)
              UPDATE dbo.InventoryBalances
              SET QuantityOnHand=@Quantity,AverageUnitCost=@Average,InventoryValue=@Value,
                  OriginContextId=NULL,OriginSnapshotSequence=NULL,
                  UpdatedAt=SYSUTCDATETIME()
              WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;
            ELSE
              INSERT dbo.InventoryBalances(
                  BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
                  InventoryValue,LastProcessingSequence,UpdatedAt)
              VALUES(@BusinessId,@WarehouseId,@ProductId,@Quantity,@Average,@Value,0,SYSUTCDATETIME());
            UPDATE dbo.Businesses SET SharesProductPrices=@SharesPrices
            WHERE BusinessId=@BusinessId;
            UPDATE dbo.Tenants SET InventoryCostBasis=@CostBasis
            WHERE TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId);
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        command.Parameters.AddWithValue("@Quantity", quantity);
        command.Parameters.AddWithValue("@Average", average);
        command.Parameters.AddWithValue("@Value", value);
        command.Parameters.AddWithValue("@SharesPrices", sharesPrices);
        command.Parameters.AddWithValue("@CostBasis", costBasis);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SetPrimaryCostPolicyAsync(bool sharesPrices, string costBasis)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.Businesses SET SharesProductPrices=@SharesPrices
            WHERE BusinessId=@BusinessId;
            UPDATE dbo.Tenants SET InventoryCostBasis=@CostBasis
            WHERE TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId);
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@SharesPrices", sharesPrices);
        command.Parameters.AddWithValue("@CostBasis", costBasis);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SetSupplierPurchaseEvidencePolicyAsync(string? policy)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.Suppliers SET PurchaseEvidencePolicy=@Policy
            WHERE SupplierId=@SupplierId AND TenantId=@TenantId;
            """;
        command.Parameters.AddWithValue("@Policy", (object?)policy ?? DBNull.Value);
        command.Parameters.AddWithValue("@SupplierId", fixture.SupplierId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExecuteAsync(
        string sql, Guid otherBusinessId, Guid otherWarehouseId,
        decimal otherQuantity, decimal otherAverage,
        bool? previousShares = null, string? previousCostBasis = null,
        Guid? otherSupplierId = null)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        command.Parameters.AddWithValue("@SupplierId", fixture.SupplierId);
        command.Parameters.AddWithValue("@OtherBusinessId", otherBusinessId);
        command.Parameters.AddWithValue("@OtherWarehouseId", otherWarehouseId);
        command.Parameters.AddWithValue("@OtherQuantity", otherQuantity);
        command.Parameters.AddWithValue("@OtherAverage", otherAverage);
        command.Parameters.AddWithValue("@OtherValue", otherQuantity * otherAverage);
        command.Parameters.AddWithValue("@OtherSupplierId", (object?)otherSupplierId ?? DBNull.Value);
        command.Parameters.AddWithValue("@PreviousShares", (object?)previousShares ?? false);
        command.Parameters.AddWithValue("@PreviousCostBasis", (object?)previousCostBasis ?? "LatestReceiptCost");
        await command.ExecuteNonQueryAsync();
    }

    private async Task<JobEvidence> ReadJobAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Status,LastError
            FROM dbo.DocumentProcessingJobs
            WHERE DocumentId=@Id AND DocumentType=N'GoodsReceipt';
            """;
        command.Parameters.AddWithValue("@Id", documentId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("The goods receipt job was not found.");
        return new JobEvidence(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private async Task<int> CountAsync(string table, Guid documentId)
    {
        var columns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["InventoryMovements"] = "DocumentId",
            ["SupplierCostObservations"] = "SourceDocumentId",
            ["Payables"] = "SourceDocumentId",
            ["PayableTransactions"] = "SourceDocumentId",
            ["PriceRevisionProposals"] = "SourceDocumentId",
            ["ServerOutboxMessages"] = "DocumentId"
        };
        Assert.True(columns.TryGetValue(table, out var idColumn));
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM dbo.[{table}] WHERE [{idColumn}]=@Id";
        command.Parameters.AddWithValue("@Id", documentId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed record JobEvidence(string Status, string? LastError);
    private sealed record FiscalGenerationEvidence(
        string? UniqueCode, string Status, string? ErrorCode, string? ErrorMessage);
}
