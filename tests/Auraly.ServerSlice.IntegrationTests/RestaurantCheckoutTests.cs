using System.Net;
using System.Net.Http.Json;
using Auraly.Application.Sales;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class RestaurantCheckoutTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Two_partial_collections_create_one_invoice_only_after_full_payment()
    {
        var waiterId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        using var waiter = fixture.CreateUserClient(waiterId, "restaurant.attend");
        using var cashier = fixture.CreateAdminClient(
            "businesses.update", "sales.create", "restaurant.commands.read");
        await RegisterWaiterAndModeAsync(waiterId, partyId);
        try
        {
            using (var created = await cashier.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/tables",
                new CreateRestaurantTableRequest(tableId, fixture.BusinessId,
                    $"F-{tableId:N}"[..12])))
                created.EnsureSuccessStatusCode();
            using (var created = await cashier.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/stations",
                new CreateRestaurantStationRequest(stationId, fixture.BusinessId,
                    $"C-{stationId:N}"[..12], "Cocina", 0, "Preparation", 15)))
                created.EnsureSuccessStatusCode();
            using (var routed = await cashier.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/product-routes/{fixture.ProductId}",
                new SetRestaurantProductRouteRequest(fixture.BusinessId,
                    stationId, "Producto E2E", null)))
                routed.EnsureSuccessStatusCode();

            RestaurantAccount account;
            using (var opened = await waiter.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/accounts",
                new OpenRestaurantAccountRequest(Guid.NewGuid(), tableId,
                    fixture.WarehouseId)))
            {
                opened.EnsureSuccessStatusCode();
                account = (await opened.Content.ReadFromJsonAsync<RestaurantAccount>())!;
            }
            using (var added = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                new AddRestaurantAccountItemRequest(Guid.NewGuid(),
                    fixture.ProductId, 2, 1)))
                added.EnsureSuccessStatusCode();
            SendRestaurantPendingResult sent;
            using (var response = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/commands",
                new SendRestaurantPendingRequest(Guid.NewGuid(), 2)))
            {
                response.EnsureSuccessStatusCode();
                sent = (await response.Content.ReadFromJsonAsync<
                    SendRestaurantPendingResult>())!;
            }
            var addition = Assert.Single(sent.Draft.Additions);
            var workSessionId = await fixture.OpenWebWorkSessionAsync();
            RestaurantCollectionSummary initial;
            using (var response = await cashier.GetAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/collections"))
            {
                response.EnsureSuccessStatusCode();
                initial = (await response.Content.ReadFromJsonAsync<
                    RestaurantCollectionSummary>())!;
            }
            Assert.Equal(0, initial.Collected);
            var unitAmount = initial.Total / 2;
            var firstId = Guid.NewGuid();
            var firstRequest = new CaptureRestaurantCollectionRequest(firstId,
                workSessionId, initial.AccountVersion,
                [new RestaurantCollectionAllocationRequest(addition.AdditionId, 1)],
                [new OnlineSalesPayment("Cash", unitAmount, null)]);
            var firstKey = Guid.NewGuid().ToString("N");
            RestaurantCollectionSummary first;
            using (var response = await CaptureAsync(cashier, account.AccountId,
                firstRequest, firstKey))
            {
                response.EnsureSuccessStatusCode();
                first = (await response.Content.ReadFromJsonAsync<
                    RestaurantCollectionSummary>())!;
            }
            Assert.Equal(unitAmount, first.Collected);
            Assert.Equal(unitAmount, first.Remaining);
            using (var replay = await CaptureAsync(cashier, account.AccountId,
                firstRequest, firstKey))
            {
                replay.EnsureSuccessStatusCode();
                Assert.Equal(first.AccountVersion,
                    (await replay.Content.ReadFromJsonAsync<
                        RestaurantCollectionSummary>())!.AccountVersion);
            }
            using (var tooEarly = await CompleteAsync(cashier, account.AccountId,
                new CompleteRestaurantAccountRequest(workSessionId,
                    new CompleteOnlineSalesDraftRequest(first.AccountVersion,
                        [new OnlineSalesPayment("Cash", unitAmount, null)],
                        DocumentType: PosSaleDocumentTypes.Receipt)),
                Guid.NewGuid().ToString("N")))
                Assert.Equal(HttpStatusCode.BadRequest, tooEarly.StatusCode);

            RestaurantCollectionSummary second;
            using (var response = await CaptureAsync(cashier, account.AccountId,
                new CaptureRestaurantCollectionRequest(Guid.NewGuid(),
                    workSessionId, first.AccountVersion,
                    [new RestaurantCollectionAllocationRequest(addition.AdditionId, 1)],
                    [new OnlineSalesPayment("Cash", unitAmount, null)]),
                Guid.NewGuid().ToString("N")))
            {
                response.EnsureSuccessStatusCode();
                second = (await response.Content.ReadFromJsonAsync<
                    RestaurantCollectionSummary>())!;
            }
            Assert.Equal(0, second.Remaining);
            Assert.Equal(2, second.Collections.Count);
            var checkout = new CompleteRestaurantAccountRequest(workSessionId,
                new CompleteOnlineSalesDraftRequest(second.AccountVersion,
                    [new OnlineSalesPayment("Cash", initial.Total, null,
                        TenderedAmount: initial.Total)],
                    DocumentType: PosSaleDocumentTypes.Receipt));
            CompleteOnlineOrderSaleResponse invoice;
            using (var response = await CompleteAsync(cashier, account.AccountId,
                checkout, Guid.NewGuid().ToString("N")))
            {
                response.EnsureSuccessStatusCode();
                invoice = (await response.Content.ReadFromJsonAsync<
                    CompleteOnlineOrderSaleResponse>())!;
            }
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                SELECT COUNT(*) FROM dbo.SalesDocuments
                WHERE DocumentId=@DocumentId AND TableId=@TableId;
                """, connection);
            command.Parameters.AddWithValue("@DocumentId", invoice.Receipt.DocumentId);
            command.Parameters.AddWithValue("@TableId", tableId);
            Assert.Equal(1, (int)(await command.ExecuteScalarAsync())!);
        }
        finally { await RestoreModeAsync(); }
    }

    [Theory]
    [InlineData(PosSaleDocumentTypes.Receipt)]
    [InlineData(PosSaleDocumentTypes.Invoice)]
    public async Task Sent_table_is_invoiced_once_with_its_table_and_waiter_author(
        string documentType)
    {
        var waiterId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        using var waiter = fixture.CreateUserClient(waiterId, "restaurant.attend");
        using var cashier = fixture.CreateAdminClient(
            "businesses.update", "sales.create", "restaurant.commands.read");
        await RegisterWaiterAndModeAsync(waiterId, partyId);
        try
        {
            using (var created = await cashier.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/tables",
                new CreateRestaurantTableRequest(tableId, fixture.BusinessId,
                    $"F-{tableId:N}"[..12])))
                created.EnsureSuccessStatusCode();
            using (var created = await cashier.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/stations",
                new CreateRestaurantStationRequest(stationId, fixture.BusinessId,
                    $"C-{stationId:N}"[..12], "Cocina", 0, "Preparation", 15)))
                created.EnsureSuccessStatusCode();
            using (var routed = await cashier.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/product-routes/{fixture.ProductId}",
                new SetRestaurantProductRouteRequest(fixture.BusinessId,
                    stationId, "Producto E2E", null)))
                routed.EnsureSuccessStatusCode();

            RestaurantAccount account;
            using (var opened = await waiter.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/accounts",
                new OpenRestaurantAccountRequest(Guid.NewGuid(), tableId,
                    fixture.WarehouseId)))
            {
                opened.EnsureSuccessStatusCode();
                account = (await opened.Content.ReadFromJsonAsync<RestaurantAccount>())!;
            }
            using (var added = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                new AddRestaurantAccountItemRequest(Guid.NewGuid(),
                    fixture.ProductId, 1, 1)))
                added.EnsureSuccessStatusCode();

            var workSessionId = await fixture.OpenWebWorkSessionAsync();
            using (var blocked = await CompleteAsync(cashier, account.AccountId,
                new CompleteRestaurantAccountRequest(workSessionId,
                    new CompleteOnlineSalesDraftRequest(2,
                        [new OnlineSalesPayment("Cash", 10000m, null)],
                        DocumentType: documentType)),
                Guid.NewGuid().ToString("N")))
                Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
            using (var blockedPrebill = await cashier.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/prebill",
                new PrintRestaurantPrebillRequest(2)))
                Assert.Equal(HttpStatusCode.BadRequest, blockedPrebill.StatusCode);

            SendRestaurantPendingResult sent;
            using (var send = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/commands",
                new SendRestaurantPendingRequest(Guid.NewGuid(), 2)))
            {
                send.EnsureSuccessStatusCode();
                sent = (await send.Content.ReadFromJsonAsync<SendRestaurantPendingResult>())!;
            }
            await SetProductStockModeAsync(false);
            using (var changedMode = await cashier.GetAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/settlement-preview?workSessionId={workSessionId:D}"))
                Assert.Equal(HttpStatusCode.BadRequest, changedMode.StatusCode);
            await SetProductStockModeAsync(true);
            using (var prebill = await cashier.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/prebill",
                new PrintRestaurantPrebillRequest(sent.Draft.Account.Version)))
            {
                prebill.EnsureSuccessStatusCode();
                var printed = (await prebill.Content.ReadFromJsonAsync<RestaurantPrebill>())!;
                Assert.Equal(sent.Draft.Account.Version, printed.AccountVersion);
                Assert.Single(printed.Lines);
            }
            using var preview = await cashier.GetAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/settlement-preview?workSessionId={workSessionId:D}");
            preview.EnsureSuccessStatusCode();
            var settlement = (await preview.Content.ReadFromJsonAsync<
                WithholdingCalculationSnapshot>())!;
            var checkout = new CompleteRestaurantAccountRequest(workSessionId,
                new CompleteOnlineSalesDraftRequest(sent.Draft.Account.Version,
                    [new OnlineSalesPayment("Cash", settlement.NetAmount, null)],
                    DocumentType: documentType));
            var key = Guid.NewGuid().ToString("N");
            CompleteOnlineOrderSaleResponse result;
            using (var completed = await CompleteAsync(cashier, account.AccountId,
                checkout, key))
            {
                completed.EnsureSuccessStatusCode();
                result = (await completed.Content.ReadFromJsonAsync<
                    CompleteOnlineOrderSaleResponse>())!;
            }
            Assert.False(result.IsReplay);
            using (var replay = await CompleteAsync(cashier, account.AccountId,
                checkout, key))
            {
                replay.EnsureSuccessStatusCode();
                var again = (await replay.Content.ReadFromJsonAsync<
                    CompleteOnlineOrderSaleResponse>())!;
                Assert.True(again.IsReplay);
                Assert.Equal(result.Receipt.DocumentId, again.Receipt.DocumentId);
            }
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var query = new SqlCommand("""
                SELECT document.TableId,document.SoldByUserId,
                       line.AddedByUserId,line.AddedAt,
                       account.Status,account.ReleasedAt,
                       (SELECT COUNT(*) FROM dbo.SalesDocuments d
                        WHERE d.DocumentId=@DocumentId)
                FROM dbo.SalesDocuments document
                JOIN dbo.SalesDocumentLines line
                  ON line.DocumentId=document.DocumentId
                JOIN sales.RestaurantAccounts account
                  ON account.AccountId=@AccountId
                WHERE document.DocumentId=@DocumentId;
                """, connection);
            query.Parameters.AddWithValue("@DocumentId", result.Receipt.DocumentId);
            query.Parameters.AddWithValue("@AccountId", account.AccountId);
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(tableId, reader.GetGuid(0));
            Assert.Equal(fixture.UserId, reader.GetGuid(1));
            Assert.Equal(waiterId, reader.GetGuid(2));
            Assert.False(reader.IsDBNull(3));
            Assert.Equal("Released", reader.GetString(4));
            Assert.False(reader.IsDBNull(5));
            Assert.Equal(1, reader.GetInt32(6));
            using (var reopened = await waiter.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/accounts",
                new OpenRestaurantAccountRequest(Guid.NewGuid(), tableId,
                    fixture.WarehouseId)))
                reopened.EnsureSuccessStatusCode();
        }
        finally
        {
            await SetProductStockModeAsync(true);
            await RestoreModeAsync();
        }
    }

    [Fact]
    public async Task Restaurant_recipe_uses_frozen_ingredients_once_at_sale_processing()
    {
        var waiterId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var finishedId = Guid.NewGuid();
        var ingredientId = Guid.NewGuid();
        using var waiter = fixture.CreateUserClient(waiterId, "restaurant.attend");
        using var cashier = fixture.CreateAdminClient(
            "businesses.update", "sales.create", "restaurant.commands.read");
        await RegisterWaiterAndModeAsync(waiterId, partyId);
        try
        {
            using (var accounting = fixture.CreateAdminClient(
                       AccountingPermissionCodes.Read,
                       AccountingPermissionCodes.Configure,
                       AccountingPermissionCodes.Activate))
            {
                using var defaults = await accounting.PutAsync(
                    "/api/commerce/v1/accounting/defaults", null);
                defaults.EnsureSuccessStatusCode();
                if (!await AccountingReadyAsync())
                {
                    using var activate = await accounting.PostAsJsonAsync(
                        "/api/commerce/v1/accounting/activate",
                        new ActivateAccountingRequest(
                            new DateOnly(2026, 1, 1), "COP", "ZeroDeclared"));
                    activate.EnsureSuccessStatusCode();
                }
            }
            await SeedRecipeAsync(finishedId, ingredientId);
            using (var created = await cashier.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/tables",
                new CreateRestaurantTableRequest(tableId, fixture.BusinessId,
                    $"R-{tableId:N}"[..12])))
                created.EnsureSuccessStatusCode();
            using (var created = await cashier.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/stations",
                new CreateRestaurantStationRequest(stationId, fixture.BusinessId,
                    $"R-{stationId:N}"[..12], "Cocina", 0, "Preparation", 15)))
                created.EnsureSuccessStatusCode();
            using (var routed = await cashier.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/product-routes/{finishedId}",
                new SetRestaurantProductRouteRequest(fixture.BusinessId,
                    stationId, "Plato con receta", null)))
                routed.EnsureSuccessStatusCode();
            RestaurantAccount account;
            using (var opened = await waiter.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/accounts",
                new OpenRestaurantAccountRequest(Guid.NewGuid(), tableId,
                    fixture.WarehouseId)))
            {
                opened.EnsureSuccessStatusCode();
                account = (await opened.Content.ReadFromJsonAsync<RestaurantAccount>())!;
            }
            using (var added = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                new AddRestaurantAccountItemRequest(Guid.NewGuid(), finishedId, 1, 1)))
                added.EnsureSuccessStatusCode();
            SendRestaurantPendingResult sent;
            using (var send = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/commands",
                new SendRestaurantPendingRequest(Guid.NewGuid(), 2)))
            {
                send.EnsureSuccessStatusCode();
                sent = (await send.Content.ReadFromJsonAsync<SendRestaurantPendingResult>())!;
            }
            var workSessionId = await fixture.OpenWebWorkSessionAsync();
            using var preview = await cashier.GetAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/settlement-preview?workSessionId={workSessionId:D}");
            preview.EnsureSuccessStatusCode();
            var settlement = (await preview.Content.ReadFromJsonAsync<
                WithholdingCalculationSnapshot>())!;
            using var completed = await CompleteAsync(cashier, account.AccountId,
                new CompleteRestaurantAccountRequest(workSessionId,
                    new CompleteOnlineSalesDraftRequest(sent.Draft.Account.Version,
                        [new OnlineSalesPayment("Cash", settlement.NetAmount, null)],
                        DocumentType: PosSaleDocumentTypes.Receipt)),
                Guid.NewGuid().ToString("N"));
            completed.EnsureSuccessStatusCode();
            var result = (await completed.Content.ReadFromJsonAsync<
                CompleteOnlineOrderSaleResponse>())!;
            await WaitForProcessedAsync(result.Receipt.DocumentId);
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                SELECT balance.QuantityOnHand,
                       (SELECT COUNT(*) FROM dbo.InventoryMovements movement
                        WHERE movement.DocumentId=@DocumentId
                          AND movement.ProductId=@IngredientId),
                       (SELECT RecognizedCostAmount
                        FROM sales.RestaurantCheckoutLines
                        WHERE DocumentId=@DocumentId AND LineNumber=1)
                FROM dbo.InventoryBalances balance
                WHERE balance.BusinessId=@BusinessId
                  AND balance.WarehouseId=@WarehouseId
                  AND balance.ProductId=@IngredientId;
                """, connection);
            command.Parameters.AddWithValue("@DocumentId", result.Receipt.DocumentId);
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            command.Parameters.AddWithValue("@IngredientId", ingredientId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(98m, reader.GetDecimal(0));
            Assert.Equal(1, reader.GetInt32(1));
            Assert.Equal(1000m, reader.GetDecimal(2));
            await reader.DisposeAsync();

            using var returns = fixture.CreateAdminClient(
                SalesReturnPermissionCodes.Create, SalesReturnPermissionCodes.Read);
            var returnable = await returns.GetFromJsonAsync<ReturnableSale>(
                $"/api/commerce/v1/sales-returns/sales/{result.Receipt.DocumentId:D}?businessId={fixture.BusinessId:D}");
            Assert.True(returnable!.IsRestaurant);
            var returnId = Guid.NewGuid();
            var cancellation = new ConfirmSalesReturnRequest(returnId,
                fixture.BusinessId, fixture.WarehouseId,
                result.Receipt.DocumentId, DateTimeOffset.UtcNow,
                ReturnEconomicResolutions.Refund, "Cash",
                "Plato preparado y descartado",
                [new ConfirmSalesReturnLineRequest(1, 1,
                    ReturnInventoryDispositions.NotReturned)],
                workSessionId, ReasonCode:
                    SalesReturnReasonCodes.RestaurantPreparedCancellation);
            using (var message = new HttpRequestMessage(HttpMethod.Post,
                "/api/commerce/v1/sales-returns/confirm")
            { Content = JsonContent.Create(cancellation) })
            {
                message.Headers.Add("Idempotency-Key", returnId.ToString("D"));
                using var response = await returns.SendAsync(message);
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            }
            await WaitForReturnProcessedAsync(returnId);
            await using var verifyReturn = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM dbo.InventoryMovements
                        WHERE DocumentId=@ReturnId),
                       (SELECT QuantityOnHand FROM dbo.InventoryBalances
                        WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId
                          AND ProductId=@IngredientId),
                       (SELECT COUNT(*) FROM dbo.SalesReturnLines
                        WHERE ReturnId=@ReturnId AND
                          InventoryDisposition=N'NotReturned');
                """, connection);
            verifyReturn.Parameters.AddWithValue("@ReturnId", returnId);
            verifyReturn.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            verifyReturn.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            verifyReturn.Parameters.AddWithValue("@IngredientId", ingredientId);
            await using var returned = await verifyReturn.ExecuteReaderAsync();
            Assert.True(await returned.ReadAsync());
            Assert.Equal(0, returned.GetInt32(0));
            Assert.Equal(98m, returned.GetDecimal(1));
            Assert.Equal(1, returned.GetInt32(2));
            await returned.DisposeAsync();

            await using var accountingLines = new SqlCommand("""
                SELECT SUM(CASE WHEN damage.Matches=1
                         THEN line.Debit ELSE 0 END),
                       SUM(CASE WHEN cost.Matches=1
                         THEN line.Credit ELSE 0 END)
                FROM dbo.AccountingEntries entry
                JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
                OUTER APPLY (SELECT TOP (1) 1 Matches
                    FROM dbo.AccountingAccountMappings mapping
                    WHERE mapping.TenantId=entry.TenantId
                      AND (mapping.BusinessId IS NULL OR mapping.BusinessId=entry.BusinessId)
                      AND mapping.AccountId=line.AccountId
                      AND mapping.Category=N'DamagedInventoryExpense') damage
                OUTER APPLY (SELECT TOP (1) 1 Matches
                    FROM dbo.AccountingAccountMappings mapping
                    WHERE mapping.TenantId=entry.TenantId
                      AND (mapping.BusinessId IS NULL OR mapping.BusinessId=entry.BusinessId)
                      AND mapping.AccountId=line.AccountId
                      AND mapping.Category=N'CostOfGoodsSold') cost
                WHERE entry.SourceDocumentId=@ReturnId;
                """, connection);
            accountingLines.Parameters.AddWithValue("@ReturnId", returnId);
            await using var posting = await accountingLines.ExecuteReaderAsync();
            Assert.True(await posting.ReadAsync());
            Assert.Equal(1000m, posting.GetDecimal(0));
            Assert.Equal(1000m, posting.GetDecimal(1));
            await posting.DisposeAsync();

            var directSale = fixture.CreateValidRequest(9_997);
            using (var upload = fixture.CreateUploadMessage(directSale))
            using (var accepted = await fixture.CreateClient().SendAsync(upload))
                accepted.EnsureSuccessStatusCode();
            await WaitForProcessedAsync(directSale.DocumentId);
            var invalidRestaurantReturn = cancellation with
            {
                ReturnId = Guid.NewGuid(),
                OriginalDocumentId = directSale.DocumentId
            };
            using (var message = new HttpRequestMessage(HttpMethod.Post,
                "/api/commerce/v1/sales-returns/confirm")
            { Content = JsonContent.Create(invalidRestaurantReturn) })
            {
                message.Headers.Add("Idempotency-Key",
                    invalidRestaurantReturn.ReturnId.ToString("D"));
                using var rejected = await returns.SendAsync(message);
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            }
        }
        finally { await RestoreModeAsync(); }
    }

    private async Task SeedRecipeAsync(Guid finishedId, Guid ingredientId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            DECLARE @TaxProfileId uniqueidentifier=NEWID();
            INSERT dbo.TaxProfiles(TaxProfileId,TenantId,Code,Name,Rate,
                IsActive,CreatedAt)
            VALUES(@TaxProfileId,@TenantId,@TaxCode,N'IVA receta',19,1,
                SYSUTCDATETIME());
            INSERT dbo.Products(ProductId,TenantId,ProductCode,BaseUnitCode,
                TaxProfileId,Name,Currency,ManageStock,IsIngredient,IsActive,CreatedAt)
            SELECT @FinishedId,TenantId,@FinishedCode,N'EA',
                   @TaxProfileId,N'Plato de receta',Currency,0,0,1,SYSUTCDATETIME()
            FROM dbo.Products WHERE ProductId=@SourceId;
            INSERT dbo.Products(ProductId,TenantId,ProductCode,BaseUnitCode,
                TaxProfileId,Name,Currency,ManageStock,IsIngredient,IsActive,CreatedAt)
            SELECT @IngredientId,TenantId,@IngredientCode,N'EA',
                   @TaxProfileId,N'Ingrediente receta',Currency,1,1,1,SYSUTCDATETIME()
            FROM dbo.Products WHERE ProductId=@SourceId;
            INSERT dbo.ProductPrices(ProductPriceId,BusinessId,ProductId,Amount,
                PreparedAmount,CurrencyCode,ValidFrom,IsActive,CreatedAt)
            VALUES(NEWID(),@BusinessId,@FinishedId,10000,10000,N'COP',
                DATEADD(day,-1,SYSDATETIMEOFFSET()),1,SYSDATETIMEOFFSET());
            DECLARE @VersionId uniqueidentifier=NEWID();
            INSERT catalog.ProductRecipeVersions(ProductRecipeVersionId,TenantId,
                ProductId,[Version],IsEnabled,PublishedAt,PublishedByUserId)
            VALUES(@VersionId,@TenantId,@FinishedId,1,1,
                SYSDATETIMEOFFSET(),@UserId);
            INSERT catalog.ProductRecipeIngredients(ProductRecipeVersionId,
                LineNumber,TenantId,IngredientProductId,QuantityPerUnit,
                BaseUnitCodeSnapshot)
            SELECT @VersionId,1,@TenantId,@IngredientId,2,p.BaseUnitCode
            FROM dbo.Products p WHERE p.ProductId=@IngredientId;
            INSERT dbo.InventoryBalances(BusinessId,WarehouseId,ProductId,
                QuantityOnHand,AverageUnitCost,InventoryValue,
                LastProcessingSequence,UpdatedAt)
            VALUES(@BusinessId,@WarehouseId,@IngredientId,
                100,500,50000,
                COALESCE((SELECT LastCompletedSequence FROM dbo.BusinessProcessingCursors
                          WHERE BusinessId=@BusinessId),0),SYSDATETIMEOFFSET());
            """, connection);
        command.Parameters.AddWithValue("@FinishedId", finishedId);
        command.Parameters.AddWithValue("@FinishedCode", $"FIN-{finishedId:N}"[..32]);
        command.Parameters.AddWithValue("@IngredientId", ingredientId);
        command.Parameters.AddWithValue("@IngredientCode", $"ING-{ingredientId:N}"[..32]);
        command.Parameters.AddWithValue("@SourceId", fixture.ProductId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@TaxCode", $"IVA-{finishedId:N}"[..32]);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Published_digital_menu_exposes_only_its_public_categories_and_products()
    {
        var waiterId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var excludedProductId = Guid.NewGuid();
        await RegisterWaiterAndModeAsync(waiterId, partyId);
        try
        {
            await SeedMenuProductAsync(categoryId, productId, excludedProductId);
            using var admin = fixture.CreateAdminClient("businesses.update");
            using var visitor = fixture.CreateClient();
            using (var denied = await visitor.GetAsync(
                "/api/commerce/v1/restaurant/digital-menu/preview"))
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            using (var preview = await admin.GetAsync(
                "/api/commerce/v1/restaurant/digital-menu/preview"))
            {
                preview.EnsureSuccessStatusCode();
                var draft = (await preview.Content.ReadFromJsonAsync<
                    RestaurantDigitalMenuView>())!;
                Assert.Empty(draft.Categories);
                using var previewProducts = await admin.GetAsync(
                    $"/api/commerce/v1/restaurant/digital-menu/preview/products?categoryId={categoryId:D}");
                previewProducts.EnsureSuccessStatusCode();
                var page = (await previewProducts.Content.ReadFromJsonAsync<
                    RestaurantDigitalMenuProductPage>())!;
                Assert.Empty(page.Items);
            }
            using (var hidden = await admin.PutAsJsonAsync(
                "/api/commerce/v1/restaurant/digital-menu",
                new SaveRestaurantDigitalMenuRequest("Carta de prueba",
                    "Comida recién hecha", false, null,
                    [new(productId, "Descripción elegida para clientes")])))
            {
                hidden.EnsureSuccessStatusCode();
                var config = (await hidden.Content.ReadFromJsonAsync<
                    RestaurantDigitalMenuConfiguration>())!;
                Assert.Single(config.Products);
                Assert.Equal(productId, config.Products[0].ProductId);
                using var unpublished = await visitor.GetAsync(
                    $"/api/public/v1/restaurant-menus/{config.PublicCode}");
                Assert.Equal(HttpStatusCode.NotFound, unpublished.StatusCode);

                using var published = await admin.PutAsJsonAsync(
                    "/api/commerce/v1/restaurant/digital-menu",
                    new SaveRestaurantDigitalMenuRequest("Carta de prueba",
                        "Comida recién hecha", true, config.RowVersion));
                published.EnsureSuccessStatusCode();
                config = (await published.Content.ReadFromJsonAsync<
                    RestaurantDigitalMenuConfiguration>())!;
                using var view = await visitor.GetAsync(
                    $"/api/public/v1/restaurant-menus/{config.PublicCode}");
                view.EnsureSuccessStatusCode();
                var menu = (await view.Content.ReadFromJsonAsync<
                    RestaurantDigitalMenuView>())!;
                Assert.Equal("Carta de prueba", menu.Title);
                Assert.Contains(menu.Categories,
                    category => category.CategoryId == categoryId &&
                        category.ProductCount == 1);
                using var products = await visitor.GetAsync(
                    $"/api/public/v1/restaurant-menus/{config.PublicCode}/products?categoryId={categoryId:D}");
                products.EnsureSuccessStatusCode();
                var page = (await products.Content.ReadFromJsonAsync<
                    RestaurantDigitalMenuProductPage>())!;
                var publishedProduct = Assert.Single(page.Items);
                Assert.Equal(productId, publishedProduct.ProductId);
                Assert.Equal(20000m, publishedProduct.Price);
                Assert.Equal("Descripción elegida para clientes",
                    publishedProduct.Description);
                Assert.DoesNotContain(page.Items,
                    product => product.ProductId == excludedProductId);
                using var qr = await visitor.GetAsync(
                    $"/api/public/v1/restaurant-menus/{config.PublicCode}/qr?url={Uri.EscapeDataString($"https://example.com/menu/{config.PublicCode}")}");
                qr.EnsureSuccessStatusCode();
                Assert.Contains("<svg", await qr.Content.ReadAsStringAsync());
            }
        }
        finally { await RestoreModeAsync(); }
    }

    private async Task SeedMenuProductAsync(Guid categoryId, Guid productId,
        Guid excludedProductId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            DECLARE @TaxProfileId uniqueidentifier=NEWID();
            INSERT dbo.TaxProfiles(TaxProfileId,TenantId,Code,Name,Rate,
                IsActive,CreatedAt)
            VALUES(@TaxProfileId,@TenantId,@TaxCode,N'IVA menú',19,1,
                SYSUTCDATETIME());
            INSERT dbo.ProductCategories(ProductCategoryId,TenantId,Name,
                DisplayOrder,IsActive,IsBrowsable)
            VALUES(@CategoryId,@TenantId,N'Especiales',1,1,1);
            INSERT dbo.Products(ProductId,TenantId,ProductCode,BaseUnitCode,
                TaxProfileId,ProductCategoryId,Name,Description,Currency,
                ManageStock,IsIngredient,IsActive,CreatedAt)
            VALUES(@ProductId,@TenantId,@ProductCode,N'EA',@TaxProfileId,
                @CategoryId,N'Plato del día',N'Preparado al momento',N'COP',
                0,0,1,SYSUTCDATETIME());
            INSERT dbo.ProductPrices(ProductPriceId,BusinessId,ProductId,
                Amount,PreparedAmount,CurrencyCode,ValidFrom,IsActive,CreatedAt)
            VALUES(NEWID(),@BusinessId,@ProductId,20000,20000,N'COP',
                DATEADD(day,-1,SYSDATETIMEOFFSET()),1,SYSDATETIMEOFFSET());
            INSERT dbo.Products(ProductId,TenantId,ProductCode,BaseUnitCode,
                TaxProfileId,ProductCategoryId,Name,Description,Currency,
                ManageStock,IsIngredient,IsActive,CreatedAt)
            VALUES(@ExcludedProductId,@TenantId,@ExcludedCode,N'EA',@TaxProfileId,
                @CategoryId,N'Plato privado',N'No publicar',N'COP',
                0,0,1,SYSUTCDATETIME());
            INSERT dbo.ProductPrices(ProductPriceId,BusinessId,ProductId,
                Amount,PreparedAmount,CurrencyCode,ValidFrom,IsActive,CreatedAt)
            VALUES(NEWID(),@BusinessId,@ExcludedProductId,22000,22000,N'COP',
                DATEADD(day,-1,SYSDATETIMEOFFSET()),1,SYSDATETIMEOFFSET());
            """, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@CategoryId", categoryId);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@ExcludedProductId", excludedProductId);
        command.Parameters.AddWithValue("@TaxCode", $"IVA-{productId:N}"[..32]);
        command.Parameters.AddWithValue("@ProductCode", $"MENU-{productId:N}"[..32]);
        command.Parameters.AddWithValue("@ExcludedCode", $"MENU-{excludedProductId:N}"[..32]);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> AccountingReadyAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT Status FROM dbo.AccountingTenantSettings
            WHERE TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        return await command.ExecuteScalarAsync() as string == "Ready";
    }

    private async Task WaitForProcessedAsync(Guid documentId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                SELECT ProcessingStatus FROM dbo.SalesDocuments
                WHERE DocumentId=@DocumentId;
                """, connection);
            command.Parameters.AddWithValue("@DocumentId", documentId);
            var status = (string?)await command.ExecuteScalarAsync();
            if (status == "Completed") return;
            if (status == "Blocked")
                throw new InvalidOperationException("La venta con receta fue rechazada al procesar.");
            await Task.Delay(100);
        }
        throw new TimeoutException("La venta con receta no terminó de procesar.");
    }

    private async Task WaitForReturnProcessedAsync(Guid returnId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "SELECT Status FROM dbo.SalesReturns WHERE ReturnId=@ReturnId;",
                connection);
            command.Parameters.AddWithValue("@ReturnId", returnId);
            if (await command.ExecuteScalarAsync() as string == "Processed") return;
            await Task.Delay(100);
        }
        throw new TimeoutException("La devolución gastronómica no terminó de procesar.");
    }

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient client,
        Guid accountId, CompleteRestaurantAccountRequest request,
        string key)
    {
        var message = new HttpRequestMessage(HttpMethod.Post,
            $"/api/commerce/v1/restaurant/accounts/{accountId:D}/complete")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(message);
    }

    private static Task<HttpResponseMessage> CaptureAsync(HttpClient client,
        Guid accountId, CaptureRestaurantCollectionRequest request, string key)
    {
        var message = new HttpRequestMessage(HttpMethod.Post,
            $"/api/commerce/v1/restaurant/accounts/{accountId:D}/collections")
        { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(message);
    }

    private async Task RegisterWaiterAndModeAsync(Guid waiterId, Guid partyId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.Businesses SET PointOfSaleMode=N'RestaurantBar'
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
            EXEC dbo.AccountingDefaultsProvision @TenantId=@TenantId,
                @BusinessId=@BusinessId,@Now=@Now;
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,
                FirstName,LastName,CompletionStatus,IsActive,CreatedAt)
            VALUES(@PartyId,@TenantId,N'NaturalPerson',N'Mesero factura',
                   N'Mesero',N'Factura',N'Incomplete',1,SYSDATETIMEOFFSET());
            INSERT parties.Waiters(WaiterId,TenantId,PartyId,Code,IsActive,CreatedAt)
            VALUES(NEWID(),@TenantId,@PartyId,@Code,1,SYSDATETIMEOFFSET());
            UPDATE dbo.AppUsers SET PartyId=@PartyId
            WHERE UserId=@WaiterId AND TenantId=@TenantId;
            DELETE FROM sales.RestaurantProductRoutes
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId
              AND ProductId=@ProductId;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@WaiterId", waiterId);
        command.Parameters.AddWithValue("@PartyId", partyId);
        command.Parameters.AddWithValue("@Code", $"W-{partyId:N}"[..12]);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SetProductStockModeAsync(bool manageStock)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.Products SET ManageStock=@ManageStock
            WHERE TenantId=@TenantId AND ProductId=@ProductId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        command.Parameters.AddWithValue("@ManageStock", manageStock);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task RestoreModeAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.Businesses SET PointOfSaleMode=N'DirectSale'
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        await command.ExecuteNonQueryAsync();
    }
}
