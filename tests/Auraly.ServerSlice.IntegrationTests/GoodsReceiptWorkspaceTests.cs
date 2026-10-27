using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Auraly.Application.Purchasing;
using Auraly.Domain.Purchasing;
using Auraly.Contracts.Inventory;
using Auraly.Contracts.Purchasing;
using Auraly.Commerce.Taxation.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed partial class GoodsReceiptWorkspaceTests(
    ServerSliceFixture fixture,
    ITestOutputHelper output)
{
    [Fact]
    public async Task Financial_discount_terms_survive_draft_save_and_reload()
    {
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ReadGoodsReceipts);
        var request = CreateDraft() with
        {
            FinancialDiscount = new(2.5m, new DateOnly(2026, 8, 15), true)
        };
        using var saved = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}", request);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var recovered = await client.GetFromJsonAsync<GoodsReceiptDraft>(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}");
        Assert.Equal(request.FinancialDiscount, recovered?.FinancialDiscount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Oversized_receipt_text_is_a_validation_error_before_saving_or_confirming(bool description)
    {
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var draft = CreateDraft();
        var line = draft.Lines.Single();
        draft = draft with { Lines = [description
            ? line with { Description = new string('X', 251) }
            : line with { TaxCode = new string('X', 33) }] };
        using (var save = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{draft.DraftId:D}", draft))
            Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
        var request = new ConfirmGoodsReceiptRequest(draft.DraftId, fixture.BusinessId,
            fixture.WarehouseId, fixture.SupplierId, draft.SupplierInvoiceNumber, draft.SupplierInvoiceDate,
            draft.ReceivedAt, true, draft.DueDate, "COP", draft.Notes, draft.Lines);
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
            { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", $"invalid-receipt-text-{draft.DraftId:N}");
        using var response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Legacy_receipt_fingerprint_still_replays_and_a_percentage_cannot_change_an_accepted_receipt()
    {
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts, PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var draft = CreateDraft();
        var request = new ConfirmGoodsReceiptRequest(draft.DraftId, fixture.BusinessId,
            fixture.WarehouseId, fixture.SupplierId, draft.SupplierInvoiceNumber, draft.SupplierInvoiceDate,
            draft.ReceivedAt, true, draft.DueDate, "COP", draft.Notes, draft.Lines);
        async Task<HttpResponseMessage> Confirm(ConfirmGoodsReceiptRequest value)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
                { Content = JsonContent.Create(value) };
            message.Headers.Add("Idempotency-Key", $"legacy-fingerprint-{value.DocumentId:N}");
            return await client.SendAsync(message);
        }
        using (var accepted = await Confirm(request))
            Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var read = new SqlCommand("""
            SELECT receipt.PayloadHash,payload.PayloadJson FROM dbo.GoodsReceipts receipt
            JOIN dbo.DocumentProcessingPayloads payload ON payload.DocumentId=receipt.GoodsReceiptId
              AND payload.DocumentType=N'GoodsReceipt' AND payload.BusinessId=receipt.BusinessId
            WHERE receipt.GoodsReceiptId=@Id;
            """, connection);
        read.Parameters.AddWithValue("@Id", request.DocumentId);
        await using var reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var fingerprint = reader.GetFieldValue<byte[]>(0);
        var source = GoodsReceiptContractSerializer.Deserialize(reader.GetString(1));
        var legacyFingerprint = LegacyReceiptFingerprint(request, source);
        Assert.NotEqual(legacyFingerprint, fingerprint);
        await reader.DisposeAsync();
        // Keep a real pre-cutover signature in the disposable fixture. New
        // acceptances use submitted facts, without rewriting historical rows.
        await using (var historical = new SqlCommand(
            "UPDATE dbo.GoodsReceipts SET PayloadHash=@Hash WHERE GoodsReceiptId=@Id", connection))
        {
            historical.Parameters.AddWithValue("@Id", request.DocumentId);
            historical.Parameters.AddWithValue("@Hash", legacyFingerprint);
            Assert.Equal(1, await historical.ExecuteNonQueryAsync());
        }
        using (var replay = await Confirm(request)) Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        using var conflict = await Confirm(request with {
            Lines = [request.Lines.Single() with { CommercialDiscountPercent = 0m }]
        });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Percentage_draft_lines_are_saved_in_one_batch_with_a_constant_number_of_SQL_commands()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<GoodsReceiptWorkspaceService>();
        var user = new PurchasingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { PurchasingPermissionCodes.CreateGoodsReceipts });
        await service.SaveDraftAsync(user, CreateDraft());
        var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        int? firstCount = null;
        foreach (var count in new[] { 1, 50, 500 })
        {
            var request = CreateDraft();
            var template = request.Lines.Single() with { CommercialDiscountPercent = 12.5m };
            request = request with { Lines = Enumerable.Range(1, count)
                .Select(number => template with { LineNumber = number }).ToArray() };
            using var activity = new Activity("purchase-discount-draft").SetIdFormat(ActivityIdFormat.W3C).Start();
            using var counter = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId);
            var timer = Stopwatch.StartNew();
            var saved = await service.SaveDraftAsync(user, request);
            timer.Stop();
            firstCount ??= counter.Count;
            Assert.Equal(firstCount.Value, counter.Count);
            Assert.InRange(counter.Count, 1, 6);
            Assert.Equal(count, saved.Lines.Count);
            Assert.All(saved.Lines, line => Assert.Equal(12.5m, line.CommercialDiscountPercent));
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), $"{count} líneas: {timer.Elapsed.TotalMilliseconds:F1} ms.");
            output.WriteLine($"Borrador con porcentaje, {count} líneas: {counter.Count} SQL, {timer.Elapsed.TotalMilliseconds:F1} ms.");
        }
    }

    [Fact]
    public async Task Commercial_percentage_survives_draft_confirmation_and_replay_with_the_same_tax_and_inventory_cost()
    {
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts, PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var draftRequest = CreateDraft();
        draftRequest = draftRequest with { Lines = [draftRequest.Lines.Single() with { CommercialDiscountPercent = 10m }] };
        using var savedResponse = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{draftRequest.DraftId:D}", draftRequest);
        Assert.True(savedResponse.StatusCode == HttpStatusCode.OK, await savedResponse.Content.ReadAsStringAsync());
        var saved = await savedResponse.Content.ReadFromJsonAsync<GoodsReceiptDraft>();
        Assert.NotNull(saved);
        Assert.Equal(10m, Assert.Single(saved.Lines).CommercialDiscountPercent);
        Assert.Equal(1m, saved.Lines[0].DiscountAmount);
        Assert.Equal(10.71m, saved.GrandTotal);
        var recovered = await client.GetFromJsonAsync<GoodsReceiptDraft>(
            $"/api/commerce/v1/goods-receipts/drafts/{draftRequest.DraftId:D}");
        Assert.NotNull(recovered);
        Assert.Equal(saved.Lines, recovered.Lines);

        var request = new ConfirmGoodsReceiptRequest(draftRequest.DraftId, fixture.BusinessId,
            fixture.WarehouseId, fixture.SupplierId, draftRequest.SupplierInvoiceNumber,
            draftRequest.SupplierInvoiceDate, draftRequest.ReceivedAt, true, draftRequest.DueDate,
            "COP", draftRequest.Notes, draftRequest.Lines, saved.ConcurrencyToken);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
                { Content = JsonContent.Create(request) };
            message.Headers.Add("Idempotency-Key", $"commercial-percent-{request.DocumentId:N}");
            using var confirmed = await client.SendAsync(message);
            Assert.True(confirmed.StatusCode == HttpStatusCode.Accepted, await confirmed.Content.ReadAsStringAsync());
        }
        var detail = await client.GetFromJsonAsync<GoodsReceiptDetail>(
            $"/api/commerce/v1/goods-receipts/{request.DocumentId:D}");
        Assert.NotNull(detail);
        var line = Assert.Single(detail.Lines);
        Assert.Equal(10m, line.CommercialDiscountPercent);
        Assert.Equal(1m, line.DiscountAmount);
        Assert.Equal(9m, line.RecognizedInventoryCostAmount);
        Assert.Equal(1.71m, line.TaxAmount);
        Assert.Equal(10.71m, detail.GrandTotal);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var verify = new SqlCommand("""
            SELECT CommercialDiscountPercent,DiscountAmount,RecognizedInventoryCostAmount,
              (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id),
              (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id)
            FROM dbo.GoodsReceiptLines WHERE GoodsReceiptId=@Id;
            """, connection);
        verify.Parameters.AddWithValue("@Id", request.DocumentId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(10m, reader.GetDecimal(0));
        Assert.Equal(1m, reader.GetDecimal(1));
        Assert.Equal(9m, reader.GetDecimal(2));
        Assert.Equal(1, reader.GetInt32(3));
        Assert.Equal(1, reader.GetInt32(4));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task Product_search_defaults_to_ten_rows()
    {
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.ReadGoodsReceipts);
        var endpoint = $"/api/commerce/v1/goods-receipts/products?supplierId={fixture.SupplierId:D}" +
                       "&includeUnassociated=true";
        _ = await client.GetFromJsonAsync<GoodsReceiptProductPage>(endpoint);

        var elapsed = new List<TimeSpan>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var startedAt = Stopwatch.GetTimestamp();
            var products = await client.GetFromJsonAsync<GoodsReceiptProductPage>(
                endpoint + $"&search={attempt}");
            elapsed.Add(Stopwatch.GetElapsedTime(startedAt));
            Assert.NotNull(products);
            Assert.Equal(10, products.PageSize);
            Assert.InRange(products.Items.Count, 0, 10);
        }

        Assert.True(elapsed.Max() < TimeSpan.FromSeconds(1),
            $"La consulta más lenta tardó {elapsed.Max().TotalMilliseconds:N0} ms.");
        output.WriteLine(
            "Búsqueda paginada (10 filas): máximo {0:N0} ms; promedio {1:N0} ms; 5 mediciones.",
            elapsed.Max().TotalMilliseconds,
            elapsed.Average(value => value.TotalMilliseconds));
    }

    [Fact]
    public async Task Product_weight_drives_the_receipt_line_total_in_kilograms()
    {
        object originalWeight;
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var read = new SqlCommand(
                "SELECT UnitGrossWeightKg FROM dbo.Products WHERE ProductId=@ProductId;", connection);
            read.Parameters.AddWithValue("@ProductId", fixture.ProductId);
            originalWeight = await read.ExecuteScalarAsync() ?? DBNull.Value;
            await using var update = new SqlCommand(
                "UPDATE dbo.Products SET UnitGrossWeightKg=1.25 WHERE ProductId=@ProductId;", connection);
            update.Parameters.AddWithValue("@ProductId", fixture.ProductId);
            await update.ExecuteNonQueryAsync();
        }

        var draftId = Guid.NewGuid();
        try
        {
            using var client = fixture.CreateAdminClient(
                PurchasingPermissionCodes.ReadGoodsReceipts,
                PurchasingPermissionCodes.CreateGoodsReceipts);
            var products = await client.GetFromJsonAsync<GoodsReceiptProductPage>(
                $"/api/commerce/v1/goods-receipts/products?supplierId={fixture.SupplierId:D}&page=1&pageSize=100");
            Assert.NotNull(products);
            Assert.Equal(1.25m, products.Items.Single(item => item.ProductId == fixture.ProductId).UnitGrossWeightKg);

            var request = CreateDraft() with
            {
                DraftId = draftId,
                Lines = [new GoodsReceiptLineRequest(
                    1, fixture.ProductId, "Producto pesado", 4m, 10m, 0m,
                    "01", 19m, PurchasingTaxTreatments.DeductibleInputVat,
                    TotalGrossWeightKg: 999m)]
            };
            using var response = await client.PutAsJsonAsync(
                $"/api/commerce/v1/goods-receipts/drafts/{draftId:D}", request);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var draft = (await response.Content.ReadFromJsonAsync<GoodsReceiptDraft>())!;
            var line = Assert.Single(draft.Lines);
            Assert.Equal(1.25m, line.UnitGrossWeightKg);
            Assert.Equal(5m, line.TotalGrossWeightKg);
        }
        finally
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var cleanup = new SqlCommand("""
                DELETE dbo.GoodsReceiptDraftLines WHERE GoodsReceiptDraftId=@DraftId;
                DELETE dbo.GoodsReceiptDrafts WHERE GoodsReceiptDraftId=@DraftId;
                UPDATE dbo.Products SET UnitGrossWeightKg=@OriginalWeight WHERE ProductId=@ProductId;
                """, connection);
            cleanup.Parameters.AddWithValue("@DraftId", draftId);
            cleanup.Parameters.AddWithValue("@ProductId", fixture.ProductId);
            cleanup.Parameters.AddWithValue("@OriginalWeight", originalWeight);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Workspace_options_expose_purchase_concepts_and_colombian_municipalities()
    {
        var ruleId = Guid.NewGuid();
        var suffix = ruleId.ToString("N")[..8];
        var conceptCode = $"SERV-{suffix}";
        string cityCode;
        string cityName;

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT dbo.WithholdingRules
                  (RuleId,Version,BusinessId,Code,Name,Kind,Direction,Moment,BaseKind,
                   ConceptCode,JurisdictionCode,Rate,MinimumBase,RequiredResponsibilities,
                   EffectiveFrom,EffectiveTo,IsActive,CreatedAt,CreatedByUserId)
                VALUES
                  (@RuleId,1,@BusinessId,@Code,N'Servicios de prueba',N'IncomeTax',N'Purchase',
                   N'Accrual',N'TaxExclusiveAmount',@ConceptCode,NULL,2.5,0,N'[]',
                   CAST(SYSDATETIMEOFFSET() AS date),NULL,1,SYSDATETIMEOFFSET(),@UserId);

                SELECT TOP (1) city.Code,city.Name
                FROM dbo.Cities city
                JOIN dbo.AdministrativeDivisions division
                  ON division.AdministrativeDivisionId=city.AdministrativeDivisionId
                JOIN dbo.Countries country ON country.CountryId=division.CountryId
                WHERE country.Code=N'CO' AND country.IsActive=1
                  AND division.IsActive=1 AND city.IsActive=1
                ORDER BY city.Name,city.Code;
                """;
            command.Parameters.AddWithValue("@RuleId", ruleId);
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@Code", $"WR-{suffix}");
            command.Parameters.AddWithValue("@ConceptCode", conceptCode);
            command.Parameters.AddWithValue("@UserId", fixture.UserId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            cityCode = reader.GetString(0);
            cityName = reader.GetString(1);
        }

        try
        {
            using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.ReadGoodsReceipts);
            using var response = await client.GetAsync("/api/commerce/v1/goods-receipts/options");
            var responseBody = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, responseBody);
            var options = await response.Content.ReadFromJsonAsync<GoodsReceiptWorkspaceOptions>();

            Assert.NotNull(options);
            Assert.Contains(options.WithholdingConcepts,
                item => item.Code == conceptCode && item.Label.Contains("Servicios de prueba"));
            Assert.Contains(options.WithholdingJurisdictions,
                item => item.Code == cityCode && item.Label.Contains(cityName));
        }
        finally
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var delete = new SqlCommand(
                "DELETE dbo.WithholdingRules WHERE RuleId=@RuleId;", connection);
            delete.Parameters.AddWithValue("@RuleId", ruleId);
            await delete.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Warehouse_not_enabled_for_receipts_is_hidden_and_rejected_by_the_server()
    {
        var warehouseId = Guid.NewGuid();
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = new SqlCommand("""
                INSERT dbo.Warehouses(
                  WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,
                  IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,
                  IsActive,CreatedAt)
                VALUES(
                  @WarehouseId,@BusinessId,N'PED-REC',N'Pedidos internos',0,
                  1,0,0,0,1,SYSDATETIMEOFFSET());
                """, connection);
            insert.Parameters.AddWithValue("@WarehouseId", warehouseId);
            insert.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            await insert.ExecuteNonQueryAsync();
        }

        try
        {
            using var client = fixture.CreateAdminClient(
                PurchasingPermissionCodes.ReadGoodsReceipts,
                PurchasingPermissionCodes.CreateGoodsReceipts);
            var options = await client.GetFromJsonAsync<GoodsReceiptWorkspaceOptions>(
                "/api/commerce/v1/goods-receipts/options");
            Assert.NotNull(options);
            Assert.DoesNotContain(options.Warehouses,
                warehouse => warehouse.WarehouseId == warehouseId);

            var request = CreateDraft() with { WarehouseId = warehouseId };
            using var response = await client.PutAsJsonAsync(
                $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}", request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var delete = new SqlCommand(
                "DELETE dbo.Warehouses WHERE WarehouseId=@WarehouseId;", connection);
            delete.Parameters.AddWithValue("@WarehouseId", warehouseId);
            await delete.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Draft_is_durable_concurrent_and_visible_in_the_workspace()
    {
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var request = CreateDraft();

        using var createdResponse = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}", request);
        Assert.Equal(HttpStatusCode.OK, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<GoodsReceiptDraft>();
        Assert.NotNull(created);
        Assert.Equal(10m, created.NetAmount);
        Assert.Equal(1.9m, created.TaxAmount);
        Assert.Equal(11.9m, created.GrandTotal);
        Assert.NotEmpty(created.ConcurrencyToken);

        var recovered = await client.GetFromJsonAsync<GoodsReceiptDraft>(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}");
        Assert.NotNull(recovered);
        Assert.Single(recovered.Lines);
        Assert.Equal("Unidad", recovered.Lines.Single().PresentationName);
        Assert.Equal(1m, recovered.Lines.Single().PresentationQuantity);
        Assert.Equal(1m, recovered.Lines.Single().UnitsPerPresentation);

        var page = await client.GetFromJsonAsync<GoodsReceiptPage>(
            "/api/commerce/v1/goods-receipts?status=Draft&page=1&pageSize=25");
        Assert.NotNull(page);
        Assert.Contains(page.Items, item => item.DocumentId == request.DraftId && item.Status == "Draft");

        var changedRequest = request with
        {
            ConcurrencyToken = created.ConcurrencyToken,
            Lines = [request.Lines.Single() with { Quantity = 2m }]
        };
        using var changedResponse = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}", changedRequest);
        Assert.Equal(HttpStatusCode.OK, changedResponse.StatusCode);
        var changed = await changedResponse.Content.ReadFromJsonAsync<GoodsReceiptDraft>();
        Assert.NotNull(changed);
        Assert.Equal(23.8m, changed.GrandTotal);
        Assert.NotEqual(created.ConcurrencyToken, changed.ConcurrencyToken);

        using var staleResponse = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}", changedRequest);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        using var deleted = await client.DeleteAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}" +
            $"?concurrencyToken={Uri.EscapeDataString(changed.ConcurrencyToken)}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        using var alreadyDeleted = await client.DeleteAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}" +
            $"?concurrencyToken={Uri.EscapeDataString(changed.ConcurrencyToken)}");
        Assert.Equal(HttpStatusCode.OK, alreadyDeleted.StatusCode);
        using var missing = await client.GetAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var confirmation = new ConfirmGoodsReceiptRequest(
            request.DraftId, fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            request.SupplierInvoiceNumber, request.SupplierInvoiceDate, request.ReceivedAt,
            request.CreatesPayable, request.DueDate, request.CurrencyCode, request.Notes,
            changedRequest.Lines, created.ConcurrencyToken);
        using var confirmMessage = new HttpRequestMessage(
            HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
        { Content = JsonContent.Create(confirmation) };
        confirmMessage.Headers.Add("Idempotency-Key", $"stale-confirm-{request.DraftId:N}");
        using var confirmed = await client.SendAsync(confirmMessage);
        Assert.Equal(HttpStatusCode.Accepted, confirmed.StatusCode);
    }

    [Fact]
    public async Task Draft_preserves_additional_invoices_and_import_declarations()
    {
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts);
        var request = CreateDraft();
        var freightId = Guid.NewGuid();
        var declarationId = Guid.NewGuid();
        request = request with
        {
            AdditionalCostDocuments =
            [
                new GoodsReceiptCostDocumentRequest(
                    freightId, fixture.SupplierId, PurchaseEvidenceTypes.SupplierElectronicInvoice,
                    $"FLETE-{Guid.NewGuid():N}", request.ReceivedAt, true, request.DueDate,
                    "COP", 1m, DateOnly.FromDateTime(request.ReceivedAt.Date), "FunctionalCurrency",
                    [new GoodsReceiptCostLineRequest(
                        1, PurchaseCostKinds.Freight, "Flete", 10m, 10m, "01", 19m, 1.9m,
                        PurchasingTaxTreatments.DeductibleInputVat,
                        PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Value)]),
                new GoodsReceiptCostDocumentRequest(
                    declarationId, fixture.SupplierId, PurchaseEvidenceTypes.ImportDeclaration,
                    $"DECL-{Guid.NewGuid():N}", request.ReceivedAt, true, request.DueDate,
                    "COP", 1m, DateOnly.FromDateTime(request.ReceivedAt.Date), "FunctionalCurrency",
                    [new GoodsReceiptCostLineRequest(
                        1, PurchaseCostKinds.CustomsDuty, "Arancel", 5m, 0m, "00", 0m, 0m,
                        PurchasingTaxTreatments.NotApplicable,
                        PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Value)])
            ]
        };

        using var savedResponse = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}", request);
        var savedBody = await savedResponse.Content.ReadAsStringAsync();
        Assert.True(savedResponse.IsSuccessStatusCode, savedBody);

        var recovered = await client.GetFromJsonAsync<GoodsReceiptDraft>(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}");
        Assert.NotNull(recovered);
        Assert.Equal(2, recovered.AdditionalCostDocuments?.Count);
        Assert.Contains(recovered.AdditionalCostDocuments!, document =>
            document.CostDocumentId == freightId && document.Lines.Single().CostKind == PurchaseCostKinds.Freight);
        Assert.Contains(recovered.AdditionalCostDocuments!, document =>
            document.CostDocumentId == declarationId && document.PurchaseEvidenceType == PurchaseEvidenceTypes.ImportDeclaration);

        using var deleted = await client.DeleteAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}" +
            $"?concurrencyToken={Uri.EscapeDataString(recovered.ConcurrencyToken)}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
    }

    [Fact]
    public async Task Manual_withholding_survives_draft_and_requires_reviewed_preview_before_confirmation()
    {
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts,
            TaxationPermissionCodes.ManageWithholdingRules,
            TaxationPermissionCodes.ViewWithholdingRules);
        var request = CreateDraft();
        var concept = "MAN-" + Guid.NewGuid().ToString("N")[..12];
        var rule = new SaveWithholdingRuleRequest(fixture.BusinessId, concept,
            "Retención adicional de compra", "IncomeTax", "Purchase", "Accrual",
            "TaxExclusiveAmount", concept, null, 2.5m, 0m, [],
            new DateOnly(2026, 1, 1), null, true);
        using var createdRule = await client.PostAsJsonAsync(
            "/api/commerce/v1/taxation/withholding-rules", rule);
        createdRule.EnsureSuccessStatusCode();
        var savedRule = (await createdRule.Content.ReadFromJsonAsync<WithholdingRuleView>())!;
        var adjustment = new WithholdingAdjustmentRequest(savedRule.RuleId, "Add", 10m, 0.25m,
            "Retención revisada para esta factura");
        var costAdjustment = adjustment with { TaxableBase = 5m, Amount = 0.10m,
            Reason = "Retención revisada para el flete" };
        var costDocument = new GoodsReceiptCostDocumentRequest(
            Guid.NewGuid(), fixture.SupplierId, PurchaseEvidenceTypes.SupplierElectronicInvoice,
            $"FLETE-{Guid.NewGuid():N}", request.ReceivedAt, true, request.DueDate,
            "COP", 1m, DateOnly.FromDateTime(request.ReceivedAt.Date), "FunctionalCurrency",
            [new GoodsReceiptCostLineRequest(1, PurchaseCostKinds.Freight, "Flete", 5m, 5m,
                "00", 0m, 0m, PurchasingTaxTreatments.NotApplicable,
                PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Value)],
            WithholdingAdjustments: [costAdjustment]);
        try
        {
            request = request with { WithholdingConceptCode = null,
                WithholdingAdjustments = [adjustment], AdditionalCostDocuments = [costDocument] };
            using var withoutPermission = fixture.CreateAdminClient(
                PurchasingPermissionCodes.CreateGoodsReceipts);
            using (var denied = await withoutPermission.PutAsJsonAsync(
                $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}", request))
                Assert.Equal(System.Net.HttpStatusCode.Forbidden, denied.StatusCode);
            using var saved = await client.PutAsJsonAsync(
                $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}", request);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var recovered = await client.GetFromJsonAsync<GoodsReceiptDraft>(
                $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}");
            Assert.Equal(adjustment, Assert.Single(recovered!.WithholdingAdjustments!));
            Assert.Equal(costAdjustment, Assert.Single(Assert.Single(
                recovered.AdditionalCostDocuments!).WithholdingAdjustments!));

            var previewRequest = new PreviewGoodsReceiptWithholdingRequest(
                fixture.BusinessId, fixture.SupplierId, request.SupplierInvoiceDate!.Value,
                request.Lines, PurchaseEvidenceType: PurchaseEvidenceTypes.SupplierElectronicInvoice,
                WithholdingAdjustments: [adjustment]);
            using var previewResponse = await client.PostAsJsonAsync(
                "/api/commerce/v1/goods-receipts/withholding-preview", previewRequest);
            Assert.True(previewResponse.IsSuccessStatusCode,
                await previewResponse.Content.ReadAsStringAsync());
            var preview = (await previewResponse.Content.ReadFromJsonAsync<WithholdingCalculationSnapshot>())!;
            Assert.Contains(preview.Lines, line => line.RuleId == savedRule.RuleId && line.Amount == 0.25m);
            Assert.NotNull(preview.ReviewHash);
            using var costPreviewResponse = await client.PostAsJsonAsync(
                "/api/commerce/v1/goods-receipts/cost-withholding-preview",
                new PreviewGoodsReceiptCostWithholdingRequest(fixture.BusinessId, costDocument));
            Assert.True(costPreviewResponse.IsSuccessStatusCode,
                await costPreviewResponse.Content.ReadAsStringAsync());
            var costPreview = (await costPreviewResponse.Content.ReadFromJsonAsync<WithholdingCalculationSnapshot>())!;
            Assert.NotNull(costPreview.ReviewHash);
            Assert.Equal(0.10m, costPreview.WithholdingTotal);

            var confirmation = new ConfirmGoodsReceiptRequest(
                request.DraftId, fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
                request.SupplierInvoiceNumber, request.SupplierInvoiceDate, request.ReceivedAt,
                request.CreatesPayable, request.DueDate, request.CurrencyCode, request.Notes,
                request.Lines, PurchaseEvidenceType: PurchaseEvidenceTypes.SupplierElectronicInvoice,
                WithholdingAdjustments: [adjustment], WithholdingReviewHash: "stale",
                AdditionalCostDocuments: [costDocument with { WithholdingReviewHash = costPreview.ReviewHash }]);
            using (var stale = await SendConfirmationAsync(client, confirmation))
                Assert.Equal(System.Net.HttpStatusCode.Conflict, stale.StatusCode);
            using var accepted = await SendConfirmationAsync(client,
                confirmation with { WithholdingReviewHash = preview.ReviewHash });
            Assert.Equal(System.Net.HttpStatusCode.Accepted, accepted.StatusCode);
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var persisted = new SqlCommand("""
                SELECT receipt.Status,snapshot.DocumentId,snapshot.GrossAmount,snapshot.WithholdingTotal,
                  snapshot.NetAmount,snapshot.AdjustmentsJson,line.RuleId,line.RuleVersion,
                  line.TaxableBase,line.Rate,line.Amount
                FROM dbo.GoodsReceipts receipt
                JOIN dbo.DocumentWithholdingSnapshots snapshot ON snapshot.BusinessId=receipt.BusinessId
                  AND ((snapshot.DocumentId=receipt.GoodsReceiptId AND snapshot.DocumentType=N'GoodsReceipt')
                    OR (snapshot.DocumentId=@CostId AND snapshot.DocumentType=N'GoodsReceiptCostDocument'))
                JOIN dbo.DocumentWithholdingLines line ON line.DocumentId=snapshot.DocumentId
                  AND line.DocumentType=snapshot.DocumentType
                WHERE receipt.GoodsReceiptId=@ReceiptId AND receipt.BusinessId=@BusinessId;
                """, connection);
            persisted.Parameters.AddWithValue("@ReceiptId", request.DraftId);
            persisted.Parameters.AddWithValue("@CostId", costDocument.CostDocumentId);
            persisted.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            var expectedSnapshots = new Dictionary<Guid, WithholdingCalculationSnapshot>
            {
                [request.DraftId] = preview, [costDocument.CostDocumentId] = costPreview
            };
            await using var reader = await persisted.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                Assert.Equal("Processed", reader.GetString(0));
                Assert.True(expectedSnapshots.Remove(reader.GetGuid(1), out var expected));
                Assert.Equal((expected!.GrossAmount, expected.WithholdingTotal, expected.NetAmount),
                    (reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4)));
                Assert.Equal(expected.Adjustments!.ToArray(),
                    JsonSerializer.Deserialize<WithholdingAdjustmentSnapshot[]>(reader.GetString(5)));
                var expectedLine = Assert.Single(expected.Lines);
                Assert.Equal((expectedLine.RuleId, expectedLine.RuleVersion, expectedLine.TaxableBase,
                    expectedLine.Rate, expectedLine.Amount),
                    (reader.GetGuid(6), reader.GetInt32(7), reader.GetDecimal(8),
                        reader.GetDecimal(9), reader.GetDecimal(10)));
            }
            Assert.Empty(expectedSnapshots);
        }
        finally
        {
            using var disabled = await client.PutAsJsonAsync(
                $"/api/commerce/v1/taxation/withholding-rules/{savedRule.RuleId}",
                rule with { IsActive = false });
            disabled.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task One_off_withholding_without_a_rule_is_reviewed_on_purchase_invoice()
    {
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts,
            TaxationPermissionCodes.ManageWithholdingRules,
            TaxationPermissionCodes.ViewWithholdingRules);
        Guid accountId;
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                SELECT TOP(1) AccountId FROM dbo.AccountingAccounts
                WHERE TenantId=@TenantId AND AccountType=N'Liability' AND IsActive=1 AND AllowsPosting=1
                ORDER BY CASE WHEN Code LIKE N'236%' THEN 0 ELSE 1 END,Code;
                """, connection);
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            accountId = (Guid)(await command.ExecuteScalarAsync())!;
        }
        var request = CreateDraft();
        var lineId = Guid.NewGuid();
        var manual = new WithholdingAdjustmentRequest(Guid.Empty, "Manual", 10m, 0.10m,
            "Retención puntual de la factura", lineId, WithholdingKinds.IndustryCommerce,
            "ReteICA puntual", 1m, "11001", accountId);
        using var previewResponse = await client.PostAsJsonAsync(
            "/api/commerce/v1/goods-receipts/withholding-preview",
            new PreviewGoodsReceiptWithholdingRequest(fixture.BusinessId, fixture.SupplierId,
                request.SupplierInvoiceDate!.Value, request.Lines,
                PurchaseEvidenceType: PurchaseEvidenceTypes.SupplierElectronicInvoice,
                WithholdingAdjustments: [manual]));
        Assert.True(previewResponse.IsSuccessStatusCode, await previewResponse.Content.ReadAsStringAsync());
        var preview = (await previewResponse.Content.ReadFromJsonAsync<WithholdingCalculationSnapshot>())!;
        Assert.Contains(preview.Lines, line => line.ManualLineId == lineId &&
            line.AccountId == accountId && line.Amount == 0.10m);
        Assert.NotNull(preview.ReviewHash);
        var costLineId = Guid.NewGuid();
        var costDocument = new GoodsReceiptCostDocumentRequest(
            Guid.NewGuid(), fixture.SupplierId, PurchaseEvidenceTypes.SupplierElectronicInvoice,
            $"FLETE-{Guid.NewGuid():N}", request.ReceivedAt, true, request.DueDate,
            "COP", 1m, DateOnly.FromDateTime(request.ReceivedAt.Date), "FunctionalCurrency",
            [new GoodsReceiptCostLineRequest(1, PurchaseCostKinds.Freight, "Flete", 5m, 5m,
                "00", 0m, 0m, PurchasingTaxTreatments.NotApplicable,
                PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Value)],
            WithholdingAdjustments: [manual with { ManualLineId = costLineId,
                TaxableBase = 5m, Amount = 0.05m }]);
        using var costPreviewResponse = await client.PostAsJsonAsync(
            "/api/commerce/v1/goods-receipts/cost-withholding-preview",
            new PreviewGoodsReceiptCostWithholdingRequest(fixture.BusinessId, costDocument));
        Assert.True(costPreviewResponse.IsSuccessStatusCode,
            await costPreviewResponse.Content.ReadAsStringAsync());
        var costPreview = (await costPreviewResponse.Content.ReadFromJsonAsync<WithholdingCalculationSnapshot>())!;
        Assert.Contains(costPreview.Lines, line => line.ManualLineId == costLineId &&
            line.AccountId == accountId && line.Amount == 0.05m);
        Assert.NotNull(costPreview.ReviewHash);
        var confirmation = new ConfirmGoodsReceiptRequest(
            request.DraftId, fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            request.SupplierInvoiceNumber, request.SupplierInvoiceDate, request.ReceivedAt,
            request.CreatesPayable, request.DueDate, request.CurrencyCode, request.Notes,
            request.Lines, PurchaseEvidenceType: PurchaseEvidenceTypes.SupplierElectronicInvoice,
            WithholdingAdjustments: [manual], WithholdingReviewHash: preview.ReviewHash,
            AdditionalCostDocuments: [costDocument with { WithholdingReviewHash = costPreview.ReviewHash }]);
        using (var stale = await SendConfirmationAsync(client,
            confirmation with { WithholdingReviewHash = "stale" }))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var accepted = await SendConfirmationAsync(client, confirmation);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await using var persisted = new SqlConnection(fixture.ConnectionString);
        await persisted.OpenAsync();
        await using var evidence = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.DocumentWithholdingLines
            WHERE AccountId=@AccountId AND RuleId IS NULL
              AND ManualLineId IN (@MainLineId,@CostLineId);
            """, persisted);
        evidence.Parameters.AddWithValue("@AccountId", accountId);
        evidence.Parameters.AddWithValue("@MainLineId", lineId);
        evidence.Parameters.AddWithValue("@CostLineId", costLineId);
        Assert.Equal(2, Convert.ToInt32(await evidence.ExecuteScalarAsync()));

        using var reportResponse = await client.GetAsync(
            "/api/commerce/v1/taxation/withholdings/applied?from=2026-08-01&to=2026-08-02&page=1&pageSize=100");
        reportResponse.EnsureSuccessStatusCode();
        var report = await reportResponse.Content.ReadFromJsonAsync<AppliedWithholdingReportView>();
        Assert.NotNull(report);
        Assert.Contains(report.Items, item => item.DocumentId == request.DraftId &&
            item.DocumentType == "GoodsReceipt" && item.Amount == 0.10m && item.IsManual);
        Assert.Contains(report.Items, item => item.DocumentId == costDocument.CostDocumentId &&
            item.DocumentType == "GoodsReceiptCostDocument" && item.Amount == 0.05m && item.IsManual);
    }

    private static async Task<HttpResponseMessage> SendConfirmationAsync(
        HttpClient client, ConfirmGoodsReceiptRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post,
            "/api/commerce/v1/goods-receipts/confirm") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", request.DocumentId.ToString("N"));
        return await client.SendAsync(message);
    }

    [Fact]
    public async Task Workspace_is_scoped_and_confirmation_atomically_removes_the_draft()
    {
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts,
            InventoryPermissionCodes.Read);
        var options = await client.GetFromJsonAsync<GoodsReceiptWorkspaceOptions>(
            "/api/commerce/v1/goods-receipts/options");
        Assert.NotNull(options);
        Assert.Contains(options.Warehouses, item => item.WarehouseId == fixture.WarehouseId);
        Assert.Contains(options.Suppliers, item => item.SupplierId == fixture.SupplierId);

        var products = await client.GetFromJsonAsync<GoodsReceiptProductPage>(
            $"/api/commerce/v1/goods-receipts/products?supplierId={fixture.SupplierId:D}&page=1&pageSize=50");
        Assert.NotNull(products);
        Assert.Contains(products.Items, item => item.ProductId == fixture.ProductId);

        var draftRequest = CreateDraft();
        using var savedResponse = await client.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{draftRequest.DraftId:D}", draftRequest);
        Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode);
        var savedDraft = await savedResponse.Content.ReadFromJsonAsync<GoodsReceiptDraft>();
        Assert.NotNull(savedDraft);
        var confirm = new ConfirmGoodsReceiptRequest(
            draftRequest.DraftId, fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            draftRequest.SupplierInvoiceNumber, draftRequest.SupplierInvoiceDate,
            draftRequest.ReceivedAt, draftRequest.CreatesPayable, draftRequest.DueDate,
            draftRequest.CurrencyCode, draftRequest.Notes, draftRequest.Lines,
            savedDraft.ConcurrencyToken,
            PurchaseEvidenceType: PurchaseEvidenceTypes.SupplierElectronicInvoice);
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
        {
            Content = JsonContent.Create(confirm)
        };
        message.Headers.Add("Idempotency-Key", $"workspace-confirm-{draftRequest.DraftId:N}");
        using var confirmedResponse = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, confirmedResponse.StatusCode);
        var accepted = await confirmedResponse.Content.ReadFromJsonAsync<GoodsReceiptAcceptance>();
        Assert.NotNull(accepted);
        Assert.StartsWith("EMC00-", accepted.DocumentNumber);

        InventoryOperationItem? historyItem = null;
        for (var attempt = 0; attempt < 80 && historyItem is null; attempt++)
        {
            var history = await client.GetFromJsonAsync<InventoryOperationPage>(
                $"/api/commerce/v1/inventory/operations?warehouseId={fixture.WarehouseId:D}" +
                $"&search={Uri.EscapeDataString(accepted.DocumentNumber)}&page=1&pageSize=20");
            historyItem = history?.Items.SingleOrDefault(item =>
                item.DocumentId == accepted.DocumentId);
            if (historyItem is null) await Task.Delay(25);
        }
        Assert.NotNull(historyItem);
        Assert.Equal(PurchasingDocumentTypes.GoodsReceipt, historyItem.DocumentType);
        Assert.Equal("Processed", historyItem.Status);
        Assert.Equal(accepted.DocumentNumber, historyItem.DocumentNumber);
        Assert.Equal(1, historyItem.LineCount);
        Assert.Null(historyItem.TotalValueChange);

        var filteredHistory = await client.GetFromJsonAsync<InventoryOperationPage>(
            $"/api/commerce/v1/inventory/operations?documentType=GoodsReceipt" +
            $"&supplierId={fixture.SupplierId:D}&purchaseEvidenceType=SupplierElectronicInvoice&page=1&pageSize=20");
        Assert.Contains(filteredHistory!.Items, item => item.DocumentId == accepted.DocumentId);
        var excludedHistory = await client.GetFromJsonAsync<InventoryOperationPage>(
            $"/api/commerce/v1/inventory/operations?documentType=GoodsReceipt" +
            $"&supplierId={fixture.SupplierId:D}&purchaseEvidenceType=BuyerElectronicSupportDocument&page=1&pageSize=20");
        Assert.DoesNotContain(excludedHistory!.Items, item => item.DocumentId == accepted.DocumentId);

        using var draftGone = await client.GetAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{draftRequest.DraftId:D}");
        Assert.Equal(HttpStatusCode.NotFound, draftGone.StatusCode);

        using var denied = fixture.CreateAdminClient(PurchasingPermissionCodes.ReadGoodsReceipts);
        var deniedDraft = CreateDraft();
        using var deniedResponse = await denied.PutAsJsonAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{deniedDraft.DraftId:D}", deniedDraft);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var wrongSupplier = await client.GetAsync(
            $"/api/commerce/v1/goods-receipts/products?supplierId={Guid.NewGuid():D}&page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.BadRequest, wrongSupplier.StatusCode);
    }

    [Fact]
    public async Task Confirmation_rolls_back_header_job_and_draft_deletion_when_detail_write_fails()
    {
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var draftRequest = CreateDraft();
        var failureTrigger = $"TestReceiptWrite_{draftRequest.DraftId:N}";

        try
        {
            using var savedResponse = await client.PutAsJsonAsync(
                $"/api/commerce/v1/goods-receipts/drafts/{draftRequest.DraftId:D}", draftRequest);
            Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode);
            var savedDraft = await savedResponse.Content.ReadFromJsonAsync<GoodsReceiptDraft>();
            Assert.NotNull(savedDraft);

            await using (var faultConnection = new SqlConnection(fixture.ConnectionString))
            {
                await faultConnection.OpenAsync();
                await using var fault = new SqlCommand($"""
                    CREATE TRIGGER dbo.[{failureTrigger}] ON dbo.GoodsReceiptLines AFTER INSERT AS
                    BEGIN
                      SET NOCOUNT ON;
                      IF EXISTS(SELECT 1 FROM inserted WHERE GoodsReceiptId='{draftRequest.DraftId:D}')
                        THROW 51098,N'Injected receipt detail persistence failure.',1;
                    END;
                    """, faultConnection);
                await fault.ExecuteNonQueryAsync();
            }
            var confirmation = new ConfirmGoodsReceiptRequest(
                draftRequest.DraftId, fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
                draftRequest.SupplierInvoiceNumber, draftRequest.SupplierInvoiceDate,
                draftRequest.ReceivedAt, draftRequest.CreatesPayable, draftRequest.DueDate,
                draftRequest.CurrencyCode, draftRequest.Notes, draftRequest.Lines,
                savedDraft.ConcurrencyToken,
                PurchaseEvidenceType: PurchaseEvidenceTypes.SupplierElectronicInvoice);
            using var message = new HttpRequestMessage(
                HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
            {
                Content = JsonContent.Create(confirmation)
            };
            message.Headers.Add("Idempotency-Key", $"atomic-failure-{draftRequest.DraftId:N}");

            using var response = await client.SendAsync(message);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                SELECT
                  (SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id),
                  (SELECT COUNT(*) FROM dbo.GoodsReceiptLines WHERE GoodsReceiptId=@Id),
                  (SELECT COUNT(*) FROM dbo.DocumentProcessingJobs
                    WHERE DocumentId=@Id AND DocumentType=N'GoodsReceipt'),
                  (SELECT COUNT(*) FROM dbo.GoodsReceiptDrafts WHERE GoodsReceiptDraftId=@Id),
                  (SELECT COUNT(*) FROM dbo.GoodsReceiptDraftLines WHERE GoodsReceiptDraftId=@Id);
                """, connection);
            command.Parameters.AddWithValue("@Id", draftRequest.DraftId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(0, reader.GetInt32(0));
            Assert.Equal(0, reader.GetInt32(1));
            Assert.Equal(0, reader.GetInt32(2));
            Assert.Equal(1, reader.GetInt32(3));
            Assert.Equal(1, reader.GetInt32(4));
        }
        finally
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var cleanup = new SqlCommand($"""
                DROP TRIGGER IF EXISTS dbo.[{failureTrigger}];
                DELETE dbo.GoodsReceiptDraftLines WHERE GoodsReceiptDraftId=@Id;
                DELETE dbo.GoodsReceiptDrafts WHERE GoodsReceiptDraftId=@Id;
                """, connection);
            cleanup.Parameters.AddWithValue("@Id", draftRequest.DraftId);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Receipt_prefers_supplier_catalog_and_explicitly_associates_a_general_product()
    {
        var productId = Guid.NewGuid();
        var productCode = $"GEN-{productId:N}";
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                IF NOT EXISTS (SELECT 1 FROM dbo.TaxProfiles WHERE TaxProfileId=@TaxProfileId)
                  INSERT dbo.TaxProfiles
                    (TaxProfileId,TenantId,Code,DianTaxCode,Name,Rate,IsActive,CreatedAt)
                  VALUES
                    (@TaxProfileId,@TenantId,@TaxCode,N'01',N'IVA compra 19%',19,1,SYSDATETIMEOFFSET());

                INSERT dbo.Products
                  (ProductId,TenantId,ProductCode,Reference,Sku,Name,Description,BaseUnitCode,
                   TaxProfileId,PurchaseTaxProfileId,PurchaseTaxTreatment,ManageStock,IsWeighable,
                   IsActive,Source,Currency,CreatedAt)
                SELECT @ProductId,TenantId,@Code,@Code,@Code,N'Producto catálogo general',
                       N'Producto aún no asociado al proveedor',N'EA',@TaxProfileId,
                       @TaxProfileId,N'CapitalizedCost',ManageStock,0,1,Source,Currency,SYSUTCDATETIME()
                FROM dbo.Products WHERE ProductId=@SourceProductId;
                """;
            command.Parameters.AddWithValue("@ProductId", productId);
            command.Parameters.AddWithValue("@SourceProductId", fixture.ProductId);
            command.Parameters.AddWithValue("@TaxProfileId", fixture.TaxProfileId);
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            command.Parameters.AddWithValue("@TaxCode", ($"VAT-{productId:N}")[..24]);
            command.Parameters.AddWithValue("@Code", productCode);
            Assert.True(await command.ExecuteNonQueryAsync() >= 1);
        }

        using var readClient = fixture.CreateAdminClient(PurchasingPermissionCodes.ReadGoodsReceipts);
        var supplierCatalog = await readClient.GetFromJsonAsync<GoodsReceiptProductPage>(
            $"/api/commerce/v1/goods-receipts/products?supplierId={fixture.SupplierId:D}&page=1&pageSize=50");
        Assert.NotNull(supplierCatalog);
        Assert.DoesNotContain(supplierCatalog.Items, item => item.ProductId == productId);

        var generalCatalog = await readClient.GetFromJsonAsync<GoodsReceiptProductPage>(
            $"/api/commerce/v1/goods-receipts/products?supplierId={fixture.SupplierId:D}" +
            $"&search={Uri.EscapeDataString(productCode)}&includeUnassociated=true&page=1&pageSize=50");
        Assert.NotNull(generalCatalog);
        var candidate = Assert.Single(generalCatalog.Items);
        Assert.False(candidate.IsAssociated);
        Assert.Equal(PurchasingTaxTreatments.CapitalizedCost, candidate.TaxTreatment);

        var association = new AssociateGoodsReceiptProductRequest(
            fixture.SupplierId, productId, $"PROV-{productId:N}", false, "Caja", 24m);
        using var denied = await readClient.PostAsJsonAsync(
            "/api/commerce/v1/goods-receipts/supplier-products", association);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var manageClient = fixture.CreateAdminClient(
            PurchasingPermissionCodes.ReadGoodsReceipts, "catalog.costs.manage");
        using var associatedResponse = await manageClient.PostAsJsonAsync(
            "/api/commerce/v1/goods-receipts/supplier-products", association);
        Assert.Equal(HttpStatusCode.OK, associatedResponse.StatusCode);
        var associated = await associatedResponse.Content.ReadFromJsonAsync<GoodsReceiptProductOption>();
        Assert.NotNull(associated);
        Assert.True(associated.IsAssociated);
        Assert.Equal(association.SupplierProductCode, associated.SupplierProductCode);
        Assert.Equal(PurchasingTaxTreatments.CapitalizedCost, associated.TaxTreatment);
        Assert.Equal("Caja", associated.PurchasePresentationName);
        Assert.Equal(24m, associated.UnitsPerPresentation);

        var refreshedSupplierCatalog = await readClient.GetFromJsonAsync<GoodsReceiptProductPage>(
            $"/api/commerce/v1/goods-receipts/products?supplierId={fixture.SupplierId:D}" +
            $"&search={Uri.EscapeDataString(productCode)}&page=1&pageSize=50");
        Assert.NotNull(refreshedSupplierCatalog);
        Assert.Contains(refreshedSupplierCatalog.Items,
            item => item.ProductId == productId && item.IsAssociated);

        using var replay = await manageClient.PostAsJsonAsync(
            "/api/commerce/v1/goods-receipts/supplier-products", association);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    }
    private SaveGoodsReceiptDraftRequest CreateDraft()
    {
        var receivedAt = new DateTimeOffset(2026, 8, 2, 10, 30, 0, TimeSpan.FromHours(-5));
        return new(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            $"PRV-{Guid.NewGuid():N}", receivedAt.AddDays(-1), receivedAt, true,
            receivedAt.AddDays(29), "COP", "Borrador de integraci?n",
            [new GoodsReceiptLineRequest(
                1, fixture.ProductId, "Producto de recepci?n", 1m, 10m, 0m,
                "01", 19m, PurchasingTaxTreatments.DeductibleInputVat)],
            null);
    }
}
