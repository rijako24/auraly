using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Inventory;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class GoodsReceiptWorkspaceTests(ServerSliceFixture fixture)
{
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

        var staleConfirmation = new ConfirmGoodsReceiptRequest(
            request.DraftId, fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            request.SupplierInvoiceNumber, request.SupplierInvoiceDate, request.ReceivedAt,
            request.CreatesPayable, request.DueDate, request.CurrencyCode, request.Notes,
            changedRequest.Lines, created.ConcurrencyToken);
        using (var staleMessage = new HttpRequestMessage(
                   HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
               { Content = JsonContent.Create(staleConfirmation) })
        {
            staleMessage.Headers.Add("Idempotency-Key", $"stale-confirm-{request.DraftId:N}");
            using var staleConfirmationResponse = await client.SendAsync(staleMessage);
            Assert.Equal(HttpStatusCode.Conflict, staleConfirmationResponse.StatusCode);
        }

        using var deleted = await client.DeleteAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}" +
            $"?concurrencyToken={Uri.EscapeDataString(changed.ConcurrencyToken)}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        using var missing = await client.GetAsync(
            $"/api/commerce/v1/goods-receipts/drafts/{request.DraftId:D}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
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
        Assert.Empty(options.Suppliers);

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
                    (TaxProfileId,BusinessId,Code,DianTaxCode,Name,Rate,IsActive,CreatedAt)
                  VALUES
                    (@TaxProfileId,@BusinessId,@TaxCode,N'01',N'IVA compra 19%',19,1,SYSDATETIMEOFFSET());

                INSERT dbo.Products
                  (ProductId,TenantId,BusinessId,ProductCode,Reference,Sku,Name,Description,BaseUnitCode,
                   TaxProfileId,PurchaseTaxProfileId,PurchaseTaxTreatment,ManageStock,IsWeighable,
                   IsActive,Source,Currency,CreatedAt)
                SELECT @ProductId,TenantId,BusinessId,@Code,@Code,@Code,N'Producto catálogo general',
                       N'Producto aún no asociado al proveedor',N'EA',@TaxProfileId,
                       @TaxProfileId,N'CapitalizedCost',ManageStock,0,1,Source,Currency,SYSUTCDATETIME()
                FROM dbo.Products WHERE ProductId=@SourceProductId;
                """;
            command.Parameters.AddWithValue("@ProductId", productId);
            command.Parameters.AddWithValue("@SourceProductId", fixture.ProductId);
            command.Parameters.AddWithValue("@TaxProfileId", fixture.TaxProfileId);
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
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
