using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Inventory;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Operational")]
public sealed class InventoryOperationsVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Count_adjustment_transfer_and_conversion_preserve_order_quantity_value_and_idempotency()
    {
        var source = Guid.NewGuid();
        var outputOne = Guid.NewGuid();
        var outputTwo = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(source, outputOne, outputTwo, destination);
        using var client = fixture.CreateAdminClient(
            InventoryPermissionCodes.Count,
            InventoryPermissionCodes.Adjust,
            InventoryPermissionCodes.DispatchTransfer,
            InventoryPermissionCodes.ReceiveTransfer,
            InventoryPermissionCodes.ResolveTransferDifference,
            InventoryPermissionCodes.Convert,
            InventoryPermissionCodes.Read);
        var occurred = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.FromHours(-5));

        await ConfirmAdjustmentAsync(client, new(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            occurred, "INITIAL_BALANCE", null, "Saldo inicial",
            [new(1, source, 20m, 5m)]));
        Assert.Equal((20m, 5m, 100m), await BalanceAsync(fixture.WarehouseId, source));

        var countId = Guid.NewGuid();
        using (var response = await client.PostAsJsonAsync("/api/commerce/v1/stock-counts/start",
                   new StartStockCountRequest(countId, fixture.BusinessId, fixture.WarehouseId,
                       occurred.AddMinutes(1), "PHYSICAL_COUNT", "Conteo ciego", [new StartStockCountLineRequest(source, 19m)])))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var draft = await response.Content.ReadFromJsonAsync<StockCountDraft>();
            Assert.NotNull(draft);
            Assert.Equal(20m, Assert.Single(draft.Lines).SystemQuantityAtBase);
            Assert.Equal(19m, Assert.Single(draft.Lines).PreCountQuantity);
        }

        await ConfirmAdjustmentAsync(client, new(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            occurred.AddMinutes(2), "FOUND_SURPLUS", null, null,
            [new(1, source, 5m, 5m)]));
        Assert.Equal(25m, (await BalanceAsync(fixture.WarehouseId, source)).Quantity);

        using (var message = new HttpRequestMessage(HttpMethod.Post, $"/api/commerce/v1/stock-counts/{countId:D}/confirm")
        {
            Content = JsonContent.Create(new ConfirmStockCountRequest(fixture.BusinessId, [new(1, source, 18m)]))
        })
        {
            message.Headers.Add("Idempotency-Key", $"count-{countId:N}");
            using var response = await client.SendAsync(message);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        Assert.Equal(23m, (await BalanceAsync(fixture.WarehouseId, source)).Quantity);
        Assert.Equal(-2m, await ScalarAsync<decimal>("SELECT QuantityChange FROM dbo.InventoryMovements WHERE DocumentId=@Id AND MovementType=N'StockCountAdjustment'", countId));

        var transferId = Guid.NewGuid();
        var transfer = new DispatchWarehouseTransferRequest(transferId, fixture.BusinessId,
            fixture.WarehouseId, destination, occurred.AddMinutes(3), "WAREHOUSE_TRANSFER", null,
            [new(1, source, 3m)]);
        var transferKey = $"transfer-{transferId:N}";
        var firstTransfer = await SendAsync<DispatchWarehouseTransferRequest>(client,
            "/api/commerce/v1/warehouse-transfers/dispatch", transfer, transferKey);
        Assert.False(firstTransfer.IdempotentReplay);
        var replay = await SendAsync<DispatchWarehouseTransferRequest>(client,
            "/api/commerce/v1/warehouse-transfers/dispatch", transfer, transferKey);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal(20m, (await BalanceAsync(fixture.WarehouseId, source)).Quantity);
        Assert.Equal((0m, 0m, 0m), await BalanceAsync(destination, source));
        var transit = await ScalarAsync<Guid>("SELECT WarehouseId FROM dbo.Warehouses WHERE BusinessId=(SELECT BusinessId FROM dbo.InventoryOperations WHERE InventoryOperationId=@Id) AND Code=N'TRA'", transferId);
        Assert.Equal((3m, 5m, 15m), await BalanceAsync(transit, source));
        Assert.Equal(2, await CountAsync("InventoryMovements", transferId));

        var pending = await client.GetFromJsonAsync<WarehouseTransferPendingPage>($"/api/commerce/v1/warehouse-transfers/pending?destinationWarehouseId={destination:D}&page=1&pageSize=20");
        var pendingTransfer = Assert.Single(pending!.Items.Where(item => item.TransferId == transferId));
        Assert.Equal(3m, pendingTransfer.DispatchedQuantity);
        Assert.Equal(0m, pendingTransfer.ReceivedQuantity);
        var transferDetail = await client.GetFromJsonAsync<WarehouseTransferDetail>($"/api/commerce/v1/warehouse-transfers/{transferId:D}");
        Assert.NotNull(transferDetail);
        var receiptId = Guid.NewGuid();
        await SendAsync(client, $"/api/commerce/v1/warehouse-transfers/{transferId:D}/receipts",
            new ReceiveWarehouseTransferRequest(receiptId, fixture.BusinessId, occurred.AddMinutes(4),
                "TRANSFER_SHORTAGE", "Se verificó la pérdida de una unidad en tránsito", transferDetail.RowVersion,
                [new(1, source, 2m)]), $"receipt-{receiptId:N}");
        Assert.Equal((2m, 5m, 10m), await BalanceAsync(destination, source));
        Assert.Equal((0m, 0m, 0m), await BalanceAsync(transit, source));
        Assert.Equal((3m, 2m), await TransferQuantitiesAsync(transferId, 1));
        Assert.Equal("Received", await ScalarAsync<string>("SELECT Status FROM dbo.InventoryOperations WHERE InventoryOperationId=@Id", transferId));
        Assert.Equal(1m, await ScalarAsync<decimal>(
            "SELECT LostQuantity FROM dbo.InventoryOperationLines WHERE InventoryOperationId=@Id AND LineNumber=1", transferId));
        Assert.Equal("Posted", await ScalarAsync<string>(
            "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'WarehouseTransferReceipt'", receiptId));
        Assert.Equal(5m, await ScalarAsync<decimal>(
            "SELECT SUM(line.Debit) FROM dbo.AccountingEntries entry INNER JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId INNER JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId WHERE entry.SourceDocumentId=@Id AND account.Code=N'529598'", receiptId));

        var conversionId = Guid.NewGuid();
        await SendAsync<ConfirmProductConversionRequest>(client,
            "/api/commerce/v1/product-conversions/confirm",
            new(conversionId, fixture.BusinessId, fixture.WarehouseId, occurred.AddMinutes(4),
                "SPLIT", "PRESENTATION_CHANGE", null, "Conversión E2E",
                [new(1,"INPUT",source,4m,null), new(2,"OUTPUT",outputOne,2m,60m), new(3,"OUTPUT",outputTwo,1m,40m)]),
            $"conversion-{conversionId:N}");
        Assert.Equal((16m, 5m, 80m), await BalanceAsync(fixture.WarehouseId, source));
        Assert.Equal((2m, 6m, 12m), await BalanceAsync(fixture.WarehouseId, outputOne));
        Assert.Equal((1m, 8m, 8m), await BalanceAsync(fixture.WarehouseId, outputTwo));
        Assert.Equal(3, await CountAsync("InventoryMovements", conversionId));
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT TotalValueChange FROM dbo.InventoryOperations WHERE InventoryOperationId=@Id", conversionId));
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT SUM(ValueChange) FROM dbo.InventoryMovements WHERE DocumentId=@Id", conversionId));
        Assert.Equal(1, await CountAsync("ServerOutboxMessages", conversionId));
        Assert.Equal("Completed", await JobStatusAsync(conversionId));

        using var detailResponse = await client.GetAsync($"/api/commerce/v1/inventory/operations/{conversionId:D}");
        var detailBody = await detailResponse.Content.ReadAsStringAsync();
        Assert.True(detailResponse.IsSuccessStatusCode, detailBody);
        var detail = await detailResponse.Content.ReadFromJsonAsync<InventoryOperationDetail>();
        Assert.NotNull(detail);
        Assert.Equal(source, detail.ConversionFamilyRootProductId);
        Assert.Equal(4m, detail.ConversionInputEquivalent);
        Assert.Equal(4m, detail.ConversionOutputEquivalent);
        Assert.Equal(0m, detail.ConversionLossQuantity);
        Assert.All(detail.Lines, line => Assert.NotNull(line.ConversionFactor));

        var candidates = await client.GetFromJsonAsync<ProductConversionProductPage>($"/api/commerce/v1/inventory/conversion-products?warehouseId={fixture.WarehouseId:D}&familyRootProductId={source:D}&page=1&pageSize=2");
        Assert.NotNull(candidates);
        Assert.Equal(3, candidates.TotalCount);
        Assert.Equal(2, candidates.Items.Count);
        Assert.Equal(2, candidates.TotalPages);

        var reverseId = Guid.NewGuid();
        await SendAsync<ConfirmProductConversionRequest>(client,
            "/api/commerce/v1/product-conversions/confirm",
            new(reverseId, fixture.BusinessId, fixture.WarehouseId, occurred.AddMinutes(5),
                "MERGE", "PRESENTATION_CHANGE", null, "Conversión inversa entre vinculados",
                [new(1,"INPUT",outputOne,2m,null), new(2,"INPUT",outputTwo,1m,null), new(3,"OUTPUT",source,4m,null)]),
            $"conversion-reverse-{reverseId:N}");
        Assert.Equal((20m, 5m, 100m), await BalanceAsync(fixture.WarehouseId, source));
        Assert.Equal((0m, 0m, 0m), await BalanceAsync(fixture.WarehouseId, outputOne));
        Assert.Equal((0m, 0m, 0m), await BalanceAsync(fixture.WarehouseId, outputTwo));
    }

    [Fact]
    public async Task Direct_count_application_is_idempotent_and_uses_the_final_optional_recount()
    {
        var product = Guid.NewGuid();
        await SeedAsync(product, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using var client = fixture.CreateAdminClient(
            InventoryPermissionCodes.Count,
            InventoryPermissionCodes.Adjust,
            InventoryPermissionCodes.Read);
        var occurred = new DateTimeOffset(2026, 8, 26, 9, 0, 0, TimeSpan.FromHours(-5));
        await ConfirmAdjustmentAsync(client, new(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, occurred,
            "INITIAL_BALANCE", null, null, [new(1, product, 10m, 5m)]));

        var documentId = Guid.NewGuid();
        var request = new ApplyStockCountRequest(
            documentId, fixture.BusinessId, fixture.WarehouseId, occurred.AddMinutes(1),
            "PHYSICAL_COUNT", "Aplicación directa desde la grilla",
            [new(product, 8m, 7m)]);
        var key = $"direct-count-{documentId:N}";
        var accepted = await SendAsync(client, "/api/commerce/v1/stock-counts/apply", request, key);
        var replay = await SendAsync(client, "/api/commerce/v1/stock-counts/apply", request, key);

        Assert.False(accepted.IdempotentReplay);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal(7m, (await BalanceAsync(fixture.WarehouseId, product)).Quantity);
        Assert.Equal(-3m, await ScalarAsync<decimal>(
            "SELECT QuantityChange FROM dbo.InventoryMovements WHERE DocumentId=@Id AND MovementType=N'StockCountAdjustment'", documentId));
        Assert.Equal(1, await CountAsync("InventoryMovements", documentId));
    }

    [Fact]
    public async Task Physical_count_draft_can_expand_while_counting_and_locks_its_scope_after_recount_starts()
    {
        var first = Guid.NewGuid();
        var added = Guid.NewGuid();
        var rejectedAfterRecount = Guid.NewGuid();
        await SeedAsync(first, added, rejectedAfterRecount, Guid.NewGuid());
        using var client = fixture.CreateAdminClient(
            InventoryPermissionCodes.Read,
            InventoryPermissionCodes.Adjust,
            InventoryPermissionCodes.Count,
            InventoryPermissionCodes.ManagePhysicalCounts,
            InventoryPermissionCodes.CapturePhysicalCounts);
        await ConfirmAdjustmentAsync(client, new(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, DateTimeOffset.UtcNow,
            "INITIAL_BALANCE", null, null, [new(1, first, 5m, 2m), new(2, added, 3m, 2m)]));
        var countId = Guid.NewGuid();
        InventoryPhysicalCountDraft draft;
        using (var response = await client.PostAsJsonAsync(
                   "/api/commerce/v1/inventory/physical-counts",
                   new CreateInventoryPhysicalCountRequest(countId, fixture.BusinessId, fixture.WarehouseId,
                       "Partial", "PHYSICAL_COUNT", null, "Conteo ampliable", [first])))
        {
            Assert.True(response.StatusCode == HttpStatusCode.Created,
                $"Expected Created but received {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            draft = Assert.Single(Assert.IsType<InventoryPhysicalCountDetail>(
                await response.Content.ReadFromJsonAsync<InventoryPhysicalCountDetail>()).Drafts);
        }

        InventoryPhysicalCountDetail expanded;
        using (var response = await client.PutAsJsonAsync(
                   $"/api/commerce/v1/inventory/physical-counts/{countId:D}/drafts/{draft.DraftId:D}",
                   new SaveInventoryPhysicalCountDraftRequest(fixture.BusinessId, draft.Version, draft.Name,
                       [new(first, 4m, null, null), new(added, 2m, null, null)], false, "Count")))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            expanded = Assert.IsType<InventoryPhysicalCountDetail>(
                await response.Content.ReadFromJsonAsync<InventoryPhysicalCountDetail>());
        }
        var expandedDraft = Assert.Single(expanded.Drafts);
        Assert.Equal(2, expandedDraft.Lines.Count);
        Assert.Contains(expandedDraft.Lines, line => line.ProductId == added && line.InitialQuantity == 2m);

        InventoryPhysicalCountDetail recounting;
        using (var response = await client.PutAsJsonAsync(
                   $"/api/commerce/v1/inventory/physical-counts/{countId:D}/drafts/{draft.DraftId:D}",
                   new SaveInventoryPhysicalCountDraftRequest(fixture.BusinessId, expandedDraft.Version, draft.Name,
                       [new(first, 4m, 4m, null), new(added, 2m, 2m, null)], true, "Recount")))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            recounting = Assert.IsType<InventoryPhysicalCountDetail>(
                await response.Content.ReadFromJsonAsync<InventoryPhysicalCountDetail>());
        }
        var recountDraft = Assert.Single(recounting.Drafts);
        Assert.Equal("Recount", recountDraft.CaptureStage);

        InventoryPhysicalCountDetail afterRemoval;
        using (var response = await client.PutAsJsonAsync(
                   $"/api/commerce/v1/inventory/physical-counts/{countId:D}/drafts/{draft.DraftId:D}",
                   new SaveInventoryPhysicalCountDraftRequest(fixture.BusinessId, recountDraft.Version, draft.Name,
                       [new(first, 4m, 4m, null), new(added, null, null, null)], false, "Recount")))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            afterRemoval = Assert.IsType<InventoryPhysicalCountDetail>(
                await response.Content.ReadFromJsonAsync<InventoryPhysicalCountDetail>());
        }
        var removalDraft = Assert.Single(afterRemoval.Drafts);
        Assert.Null(Assert.Single(removalDraft.Lines, line => line.ProductId == added).InitialQuantity);
        Assert.Equal("Recount", removalDraft.CaptureStage);

        using var rejected = await client.PutAsJsonAsync(
            $"/api/commerce/v1/inventory/physical-counts/{countId:D}/drafts/{draft.DraftId:D}",
            new SaveInventoryPhysicalCountDraftRequest(fixture.BusinessId, removalDraft.Version, draft.Name,
                [new(first, 4m, 4m, null), new(added, null, null, null), new(rejectedAfterRecount, 1m, null, null)],
                true, "Count"));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        await ClosePhysicalCountForTestAsync(countId);
    }

    [Fact]
    public async Task Active_physical_counts_can_capture_the_same_product_independently()
    {
        var product = Guid.NewGuid();
        await SeedAsync(product, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using var client = fixture.CreateAdminClient(
            InventoryPermissionCodes.Read,
            InventoryPermissionCodes.Adjust,
            InventoryPermissionCodes.ManagePhysicalCounts,
            InventoryPermissionCodes.CapturePhysicalCounts);
        await ConfirmAdjustmentAsync(client, new(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, DateTimeOffset.UtcNow,
            "INITIAL_BALANCE", null, null, [new(1, product, 1m, 1m)]));
        var firstCountId = Guid.NewGuid();
        var secondCountId = Guid.NewGuid();

        try
        {
            var first = await client.PostAsJsonAsync(
                "/api/commerce/v1/inventory/physical-counts",
                new CreateInventoryPhysicalCountRequest(firstCountId, fixture.BusinessId, fixture.WarehouseId,
                    "Partial", "PHYSICAL_COUNT", null, "Conteo simultáneo A", [product]));
            Assert.True(first.StatusCode == HttpStatusCode.Created,
                $"Expected first count to be created but received {first.StatusCode}: {await first.Content.ReadAsStringAsync()}");

            var second = await client.PostAsJsonAsync(
                "/api/commerce/v1/inventory/physical-counts",
                new CreateInventoryPhysicalCountRequest(secondCountId, fixture.BusinessId, fixture.WarehouseId,
                    "Partial", "PHYSICAL_COUNT", null, "Conteo simultáneo B", [product]));
            Assert.True(second.StatusCode == HttpStatusCode.Created,
                $"Expected the same product in a second active count but received {second.StatusCode}: {await second.Content.ReadAsStringAsync()}");

            var firstDetail = Assert.IsType<InventoryPhysicalCountDetail>(
                await first.Content.ReadFromJsonAsync<InventoryPhysicalCountDetail>());
            var secondDetail = Assert.IsType<InventoryPhysicalCountDetail>(
                await second.Content.ReadFromJsonAsync<InventoryPhysicalCountDetail>());
            Assert.Equal(product, Assert.Single(Assert.Single(firstDetail.Drafts).Lines).ProductId);
            Assert.Equal(product, Assert.Single(Assert.Single(secondDetail.Drafts).Lines).ProductId);
        }
        finally
        {
            await ClosePhysicalCountForTestAsync(firstCountId);
            await ClosePhysicalCountForTestAsync(secondCountId);
        }
    }

    [Fact]
    public async Task General_reconciliation_includes_inventory_products_created_after_the_count_started_as_uncounted()
    {
        var counted = Guid.NewGuid();
        var originalUncounted = Guid.NewGuid();
        var third = Guid.NewGuid();
        var addedAfterStart = Guid.NewGuid();
        await SeedAsync(counted, originalUncounted, third, Guid.NewGuid());
        using var client = fixture.CreateAdminClient(
            InventoryPermissionCodes.Read,
            InventoryPermissionCodes.Adjust,
            InventoryPermissionCodes.Count,
            InventoryPermissionCodes.ManagePhysicalCounts,
            InventoryPermissionCodes.CapturePhysicalCounts);
        await ConfirmAdjustmentAsync(client, new(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, DateTimeOffset.UtcNow,
            "INITIAL_BALANCE", null, null,
            [new(1, counted, 5m, 2m), new(2, originalUncounted, 4m, 2m), new(3, third, 2m, 2m)]));
        var countId = Guid.NewGuid();
        InventoryPhysicalCountDraft draft;
        using (var response = await client.PostAsJsonAsync(
                   "/api/commerce/v1/inventory/physical-counts",
                   new CreateInventoryPhysicalCountRequest(countId, fixture.BusinessId, fixture.WarehouseId,
                       "General", "PHYSICAL_COUNT", null, "Conteo general", [])))
        {
            Assert.True(response.StatusCode == HttpStatusCode.Created,
                $"Expected Created but received {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            draft = Assert.Single(Assert.IsType<InventoryPhysicalCountDetail>(
                await response.Content.ReadFromJsonAsync<InventoryPhysicalCountDetail>()).Drafts);
        }
        using (var response = await client.PutAsJsonAsync(
                   $"/api/commerce/v1/inventory/physical-counts/{countId:D}/drafts/{draft.DraftId:D}",
                   new SaveInventoryPhysicalCountDraftRequest(fixture.BusinessId, draft.Version, draft.Name,
                       draft.Lines.Select(line => new InventoryPhysicalCountDraftLineInput(
                           line.ProductId, line.ProductId == counted ? 3m : null, null,
                           line.ProductId == counted ? null : "No contado")).ToArray(), true)))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await SeedAdditionalInventoryProductAsync(addedAfterStart);
        InventoryReconciliationDetail reconciliation;
        using (var response = await client.PostAsJsonAsync(
                   $"/api/commerce/v1/inventory/physical-counts/{countId:D}/reconciliations",
                   new PrepareInventoryReconciliationRequest(fixture.BusinessId, [new(draft.DraftId, 2)])))
        {
            Assert.True(response.StatusCode == HttpStatusCode.Created,
                $"Expected Created but received {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            reconciliation = Assert.IsType<InventoryReconciliationDetail>(
                await response.Content.ReadFromJsonAsync<InventoryReconciliationDetail>());
        }
        Assert.Equal("Uncounted", Assert.Single(reconciliation.Products,
            product => product.ProductId == originalUncounted).Status);
        var addedProduct = Assert.Single(reconciliation.Products,
            product => product.ProductId == addedAfterStart);
        Assert.Equal("Uncounted", addedProduct.Status);
        Assert.Equal(0m, addedProduct.ProposedQuantity);
        await ClosePhysicalCountForTestAsync(countId);
    }

    [Fact]
    public async Task Physical_count_reconciliation_sums_selected_drafts_and_preserves_movements_after_capture()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await SeedAsync(first, second, Guid.NewGuid(), Guid.NewGuid());
        using var client = fixture.CreateAdminClient(
            InventoryPermissionCodes.Read,
            InventoryPermissionCodes.Adjust,
            InventoryPermissionCodes.Count,
            InventoryPermissionCodes.ManagePhysicalCounts,
            InventoryPermissionCodes.CapturePhysicalCounts);
        var occurred = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.FromHours(-5));

        await ConfirmAdjustmentAsync(client, new(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, occurred,
            "INITIAL_BALANCE", null, "Base para inventario físico",
            [new(1, first, 10m, 5m), new(2, second, 20m, 5m)]));

        var countId = Guid.NewGuid();
        var firstDraftName = $"Equipo A {countId:N}";
        var secondDraftName = $"Equipo B {countId:N}";
        Guid firstDraft;
        using (var create = await client.PostAsJsonAsync(
                   "/api/commerce/v1/inventory/physical-counts",
                   new CreateInventoryPhysicalCountRequest(
                       countId, fixture.BusinessId, fixture.WarehouseId, "Partial", "PHYSICAL_COUNT", null,
                       firstDraftName, [first, second])))
        {
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var detail = await create.Content.ReadFromJsonAsync<InventoryPhysicalCountDetail>();
            firstDraft = Assert.Single(Assert.IsType<InventoryPhysicalCountDetail>(detail).Drafts).DraftId;
        }

        var secondDraft = Guid.NewGuid();
        using (var createDraft = await client.PostAsJsonAsync(
                   $"/api/commerce/v1/inventory/physical-counts/{countId:D}/drafts",
                   new CreateInventoryPhysicalCountDraftRequest(fixture.BusinessId, secondDraft, secondDraftName, [first])))
        {
            Assert.Equal(HttpStatusCode.OK, createDraft.StatusCode);
        }

        using (var response = await client.PutAsJsonAsync(
                   $"/api/commerce/v1/inventory/physical-counts/{countId:D}/drafts/{firstDraft:D}",
                   new SaveInventoryPhysicalCountDraftRequest(fixture.BusinessId, 1, firstDraftName,
                       [new(first, 10m, 9m, null), new(second, 20m, 22m, null)], true)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var response = await client.PutAsJsonAsync(
                   $"/api/commerce/v1/inventory/physical-counts/{countId:D}/drafts/{secondDraft:D}",
                   new SaveInventoryPhysicalCountDraftRequest(fixture.BusinessId, 1, secondDraftName,
                       [new(first, 2m, 2m, null)], true, "Recount")))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var updated = Assert.IsType<InventoryPhysicalCountDetail>(
                await response.Content.ReadFromJsonAsync<InventoryPhysicalCountDetail>());
            Assert.Equal("Recount", Assert.Single(updated.Drafts, draft => draft.DraftId == secondDraft).CaptureStage);
        }

        var filteredDrafts = await client.GetFromJsonAsync<InventoryPhysicalCountDraftPage>(
            $"/api/commerce/v1/inventory/physical-count-drafts?search={Uri.EscapeDataString(secondDraftName)}&page=1&pageSize=1&from=2020-01-01T00:00:00Z&to=2030-01-01T00:00:00Z");
        Assert.NotNull(filteredDrafts);
        Assert.Equal(1, filteredDrafts.TotalCount);
        Assert.Equal(secondDraftName, Assert.Single(filteredDrafts.Items).Name);

        InventoryReconciliationDetail reconciliation;
        using (var prepare = await client.PostAsJsonAsync(
                   $"/api/commerce/v1/inventory/physical-counts/{countId:D}/reconciliations",
                   new PrepareInventoryReconciliationRequest(fixture.BusinessId,
                       [new(firstDraft, 2), new(secondDraft, 2)])))
        {
            Assert.True(prepare.StatusCode == HttpStatusCode.Created,
                $"Expected Created but received {prepare.StatusCode}: {await prepare.Content.ReadAsStringAsync()}");
            reconciliation = Assert.IsType<InventoryReconciliationDetail>(
                await prepare.Content.ReadFromJsonAsync<InventoryReconciliationDetail>());
        }
        Assert.Equal(11m, Assert.Single(reconciliation.Products, product => product.ProductId == first).ProposedQuantity);
        Assert.Equal(22m, Assert.Single(reconciliation.Products, product => product.ProductId == second).ProposedQuantity);
        Assert.All(reconciliation.Products.Where(product => product.Status == "Uncounted"),
            product => Assert.Equal(0m, product.ProposedQuantity));

        await ConfirmAdjustmentAsync(client, new(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, occurred.AddMinutes(5),
            "FOUND_SURPLUS", null, "Movimiento posterior al conteo",
            [new(1, first, 3m, 5m)]));

        Guid finalOperationId = Guid.Empty;
        fixture.PauseDocumentProcessing();
        try
        {
            using (var apply = await client.PostAsJsonAsync(
                       $"/api/commerce/v1/inventory/physical-counts/{countId:D}/reconciliations/{reconciliation.ReconciliationId:D}/apply",
                       new ApplyInventoryReconciliationRequest(fixture.BusinessId, "Counted")))
            {
                Assert.True(apply.StatusCode == HttpStatusCode.Accepted,
                    $"Expected Accepted but received {apply.StatusCode}: {await apply.Content.ReadAsStringAsync()}");
            }
            var applying = await client.GetFromJsonAsync<InventoryReconciliationDetail>(
                $"/api/commerce/v1/inventory/physical-counts/{countId:D}/reconciliation");
            finalOperationId = Assert.IsType<Guid>(Assert.IsType<InventoryReconciliationDetail>(applying).CountedDocumentId);

            Assert.Equal(0, await CountAsync("InventoryMovements", finalOperationId));
            Assert.Equal(13m, (await BalanceAsync(fixture.WarehouseId, first)).Quantity);
            Assert.Equal(20m, (await BalanceAsync(fixture.WarehouseId, second)).Quantity);

            var signal = Assert.Single(fixture.DrainDocumentSignals());
            Assert.Equal(finalOperationId, signal.DocumentId);
            Assert.Equal(InventoryDocumentTypes.StockCount, signal.DocumentType);
            fixture.ResumeDocumentProcessing();
            await fixture.DocumentSignals.PublishAsync(signal);

            var completed = await client.GetFromJsonAsync<InventoryPhysicalCountDetail>(
                $"/api/commerce/v1/inventory/physical-counts/{countId:D}");
            Assert.NotNull(completed);
            Assert.Equal("Reconciling", completed.Status);
        }
        finally
        {
            fixture.ResumeDocumentProcessing();
            fixture.DrainDocumentSignals();
        }

        Assert.Equal(14m, (await BalanceAsync(fixture.WarehouseId, first)).Quantity);
        Assert.Equal(22m, (await BalanceAsync(fixture.WarehouseId, second)).Quantity);
        Assert.Equal(2, await CountAsync("InventoryMovements", finalOperationId));
        await ClosePhysicalCountForTestAsync(countId);
    }

    [Fact]
    public async Task Reconciliation_saves_all_uncounted_products_as_a_populated_draft_and_can_apply_them_at_zero()
    {
        var countedProduct = Guid.NewGuid();
        var uncountedProduct = Guid.NewGuid();
        await SeedAsync(countedProduct, uncountedProduct, Guid.NewGuid(), Guid.NewGuid());
        using var client = fixture.CreateAdminClient(
            InventoryPermissionCodes.Read,
            InventoryPermissionCodes.Adjust,
            InventoryPermissionCodes.Count,
            InventoryPermissionCodes.ManagePhysicalCounts,
            InventoryPermissionCodes.CapturePhysicalCounts);
        await ConfirmAdjustmentAsync(client, new(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, DateTimeOffset.UtcNow,
            "INITIAL_BALANCE", null, null,
            [new(1, countedProduct, 5m, 2m), new(2, uncountedProduct, 6m, 2m)]));

        var countId = Guid.NewGuid();
        InventoryPhysicalCountDetail created;
        using (var response = await client.PostAsJsonAsync(
                   "/api/commerce/v1/inventory/physical-counts",
                   new CreateInventoryPhysicalCountRequest(countId, fixture.BusinessId,
                       fixture.WarehouseId, "Partial", "PHYSICAL_COUNT", null,
                       "Conteo parcial", [countedProduct, uncountedProduct])))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            created = Assert.IsType<InventoryPhysicalCountDetail>(
                await response.Content.ReadFromJsonAsync<InventoryPhysicalCountDetail>());
        }
        var sourceDraft = Assert.Single(created.Drafts);
        using (var response = await client.PutAsJsonAsync(
                   $"/api/commerce/v1/inventory/physical-counts/{countId:D}/drafts/{sourceDraft.DraftId:D}",
                   new SaveInventoryPhysicalCountDraftRequest(fixture.BusinessId, 1,
                       sourceDraft.Name,
                       [new(countedProduct, 4m, null, null), new(uncountedProduct, null, null, "Pendiente")], true)))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        InventoryReconciliationDetail reconciliation;
        using (var response = await client.PostAsJsonAsync(
                   $"/api/commerce/v1/inventory/physical-counts/{countId:D}/reconciliations",
                   new PrepareInventoryReconciliationRequest(fixture.BusinessId,
                       [new(sourceDraft.DraftId, 2)])))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            reconciliation = Assert.IsType<InventoryReconciliationDetail>(
                await response.Content.ReadFromJsonAsync<InventoryReconciliationDetail>());
        }
        var uncounted = reconciliation.Products.Where(product => product.Status == "Uncounted").ToArray();
        Assert.Contains(uncounted, product => product.ProductId == uncountedProduct);
        Assert.Contains(uncounted, product => product.ProductId == fixture.ProductId);
        Assert.All(uncounted, product => Assert.Equal(0m, product.ProposedQuantity));

        var pendingDraftId = Guid.NewGuid();
        using (var response = await client.PostAsJsonAsync(
                   $"/api/commerce/v1/inventory/physical-counts/{countId:D}/reconciliations/{reconciliation.ReconciliationId:D}/drafts",
                   new SaveInventoryReconciliationDraftRequest(fixture.BusinessId,
                       "Uncounted", pendingDraftId, "Pendientes de la conciliación")))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var detail = Assert.IsType<InventoryPhysicalCountDetail>(
                await response.Content.ReadFromJsonAsync<InventoryPhysicalCountDetail>());
            var pendingDraft = Assert.Single(detail.Drafts, draft => draft.DraftId == pendingDraftId);
            Assert.Equal(uncounted.Length, pendingDraft.Lines.Count);
            Assert.Contains(pendingDraft.Lines, line => line.ProductId == uncountedProduct);
            Assert.Contains(pendingDraft.Lines, line => line.ProductId == fixture.ProductId);
            Assert.All(pendingDraft.Lines, line => Assert.Null(line.InitialQuantity));
        }

        using (var response = await client.PostAsJsonAsync(
                   $"/api/commerce/v1/inventory/physical-counts/{countId:D}/reconciliations/{reconciliation.ReconciliationId:D}/apply",
                   new ApplyInventoryReconciliationRequest(fixture.BusinessId, "Uncounted")))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        for (var attempt = 0; attempt < 80 && (await BalanceAsync(fixture.WarehouseId, uncountedProduct)).Quantity != 0; attempt++)
            await Task.Delay(25);
        Assert.Equal(0m, (await BalanceAsync(fixture.WarehouseId, uncountedProduct)).Quantity);
        Assert.Equal(5m, (await BalanceAsync(fixture.WarehouseId, countedProduct)).Quantity);
    }

    [Fact]
    public async Task Stock_count_with_two_changed_products_creates_two_kardex_movements()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await SeedAsync(first, second, Guid.NewGuid(), Guid.NewGuid());
        using var client = fixture.CreateAdminClient(
            InventoryPermissionCodes.Count,
            InventoryPermissionCodes.Adjust);
        var occurred = new DateTimeOffset(2026, 8, 13, 9, 0, 0, TimeSpan.FromHours(-5));

        await ConfirmAdjustmentAsync(client, new(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, occurred,
            "INITIAL_BALANCE", null, "Base para conteo de dos líneas",
            [new(1, first, 10m, 5m), new(2, second, 8m, 5m)]));

        var countId = Guid.NewGuid();
        using (var start = await client.PostAsJsonAsync(
            "/api/commerce/v1/stock-counts/start",
            new StartStockCountRequest(
                countId, fixture.BusinessId, fixture.WarehouseId,
                occurred.AddMinutes(1), "PHYSICAL_COUNT", "Conteo de dos líneas",
                [new StartStockCountLineRequest(first, 9m), new StartStockCountLineRequest(second, 7m)])))
        {
            Assert.Equal(HttpStatusCode.OK, start.StatusCode);
            var draft = await start.Content.ReadFromJsonAsync<StockCountDraft>();
            Assert.NotNull(draft);
            Assert.Equal(2, draft.Lines.Count);
        }

        using (var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/commerce/v1/stock-counts/{countId:D}/confirm")
        {
            Content = JsonContent.Create(new ConfirmStockCountRequest(
                fixture.BusinessId,
                [new(1, first, 7m), new(2, second, 12m)]))
        })
        {
            request.Headers.Add("Idempotency-Key", $"count-two-{countId:N}");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        Assert.Equal(7m, (await BalanceAsync(fixture.WarehouseId, first)).Quantity);
        Assert.Equal(12m, (await BalanceAsync(fixture.WarehouseId, second)).Quantity);
        Assert.Equal(2, await CountAsync("InventoryMovements", countId));
        Assert.Equal(-3m, await ScalarAsync<decimal>(
            "SELECT QuantityChange FROM dbo.InventoryMovements WHERE DocumentId=@Id AND ProductId='" + first + "'", countId));
        Assert.Equal(4m, await ScalarAsync<decimal>(
            "SELECT QuantityChange FROM dbo.InventoryMovements WHERE DocumentId=@Id AND ProductId='" + second + "'", countId));
    }
    [Fact]
    public async Task Damage_updates_inventory_once_and_queries_respect_cost_permission()
    {
        var product = Guid.NewGuid();
        await SeedAsync(product, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using var client = fixture.CreateAdminClient(InventoryPermissionCodes.Adjust, InventoryPermissionCodes.Damage, InventoryPermissionCodes.Read, InventoryPermissionCodes.ReadCosts);
        await ConfirmAdjustmentAsync(client, new(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, DateTimeOffset.UtcNow, "INITIAL_BALANCE", null, null, [new(1, product, 10m, 5m)]));
        var damageId = Guid.NewGuid();
        var request = new ConfirmInventoryDamageRequest(damageId, fixture.BusinessId, fixture.WarehouseId, DateTimeOffset.UtcNow, "DAMAGE", null, "Empaque destruido", [new(1, product, 3m)]);
        var key = $"damage-{damageId:N}";
        var accepted = await SendAsync(client, "/api/commerce/v1/inventory-damages/confirm", request, key);
        var replay = await SendAsync(client, "/api/commerce/v1/inventory-damages/confirm", request, key);
        Assert.False(accepted.IdempotentReplay); Assert.True(replay.IdempotentReplay);
        Assert.Equal((7m,5m,35m), await BalanceAsync(fixture.WarehouseId, product));
        var damagedWarehouseId = await ScalarAsync<Guid>(
            "SELECT DestinationWarehouseId FROM dbo.InventoryOperations WHERE InventoryOperationId=@Id", damageId);
        Assert.Equal((3m,0m,0m), await BalanceAsync(damagedWarehouseId, product));
        Assert.Equal(-3m, await ScalarAsync<decimal>("SELECT QuantityChange FROM dbo.InventoryMovements WHERE DocumentId=@Id AND MovementType=N'InventoryDamage'", damageId));
        Assert.Equal(3m, await ScalarAsync<decimal>("SELECT QuantityChange FROM dbo.InventoryMovements WHERE DocumentId=@Id AND MovementType=N'DamageWarehouseIn'", damageId));
        Assert.Equal(2, await CountAsync("InventoryMovements", damageId)); Assert.Equal(1, await CountAsync("ServerOutboxMessages", damageId));
        Assert.Equal("Posted", await ScalarAsync<string>(
            "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'Damage'", damageId));
        Assert.Equal("DamagedInventoryExpense", await ScalarAsync<string>(
            "SELECT JSON_VALUE(PayloadJson,'$.counterpartAccountingCategory') FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id AND SourceDocumentType=N'Damage'", damageId));
        Assert.Equal(15m, await ScalarAsync<decimal>(
            "SELECT SUM(line.Debit) FROM dbo.AccountingEntries entry INNER JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId INNER JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId WHERE entry.SourceDocumentId=@Id AND entry.SourceDocumentType=N'Damage' AND account.Code=N'529596'", damageId));
        Assert.Equal(15m, await ScalarAsync<decimal>(
            "SELECT SUM(line.Credit) FROM dbo.AccountingEntries entry INNER JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId INNER JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId WHERE entry.SourceDocumentId=@Id AND entry.SourceDocumentType=N'Damage' AND account.Code=N'143505'", damageId));
        var balances = await client.GetFromJsonAsync<InventoryBalancePage>($"/api/commerce/v1/inventory/balances?warehouseId={fixture.WarehouseId:D}&search=Insumo&page=1&pageSize=20");
        var row = Assert.Single(balances!.Items.Where(x => x.ProductId == product)); Assert.Equal(5m,row.AverageUnitCost); Assert.Equal(35m,row.InventoryValue);
        foreach (var search in new[] { $"REF-{product:N}", $"BAR-{product:N}" })
        {
            var result = await client.GetFromJsonAsync<InventoryBalancePage>($"/api/commerce/v1/inventory/balances?warehouseId={fixture.WarehouseId:D}&search={search}&page=1&pageSize=20");
            Assert.Contains(result!.Items, item => item.ProductId == product);
        }
        var products = await client.GetFromJsonAsync<InventoryProductPage>($"/api/commerce/v1/inventory/products?warehouseId={fixture.WarehouseId:D}&search=Insumo&page=1&pageSize=20");
        var productRow = Assert.Single(products!.Items.Where(x => x.ProductId == product)); Assert.Equal(7m, productRow.QuantityOnHand); Assert.Equal(5m, productRow.AverageUnitCost);
        foreach (var search in new[] { $"I-{product:N}", $"REF-{product:N}", $"BAR-{product:N}" })
        {
            var result = await client.GetFromJsonAsync<InventoryProductPage>($"/api/commerce/v1/inventory/products?warehouseId={fixture.WarehouseId:D}&search={search}&page=1&pageSize=20");
            Assert.Contains(result!.Items, item => item.ProductId == product);
        }
        var movements = await client.GetFromJsonAsync<InventoryMovementPage>($"/api/commerce/v1/inventory/movements?productId={product:D}&page=1&pageSize=20");
        Assert.Contains(movements!.Items,x=>x.DocumentId==damageId&&x.MovementType=="InventoryDamage");
        var searchedMovements = await client.GetFromJsonAsync<InventoryMovementPage>($"/api/commerce/v1/inventory/movements?search=REF-{product:N}&page=1&pageSize=20");
        Assert.Contains(searchedMovements!.Items, x => x.DocumentId == damageId);
        var searchedOperations = await client.GetFromJsonAsync<InventoryOperationPage>("/api/commerce/v1/inventory/operations?search=Insumo&page=1&pageSize=20");
        Assert.Contains(searchedOperations!.Items, x => x.DocumentId == damageId);
        var filteredOperations = await client.GetFromJsonAsync<InventoryOperationPage>("/api/commerce/v1/inventory/operations?documentType=Damage&reasonCode=DAMAGE&page=1&pageSize=20");
        Assert.Contains(filteredOperations!.Items, x => x.DocumentId == damageId);
        var excludedOperations = await client.GetFromJsonAsync<InventoryOperationPage>("/api/commerce/v1/inventory/operations?documentType=Damage&reasonCode=EXPIRED&page=1&pageSize=20");
        Assert.DoesNotContain(excludedOperations!.Items, x => x.DocumentId == damageId);
        using var restricted = fixture.CreateAdminClient(InventoryPermissionCodes.Read);
        var hidden = await restricted.GetFromJsonAsync<InventoryBalancePage>($"/api/commerce/v1/inventory/balances?warehouseId={fixture.WarehouseId:D}&search=Insumo&page=1&pageSize=20");
        var hiddenRow = Assert.Single(hidden!.Items.Where(x => x.ProductId == product)); Assert.Null(hiddenRow.AverageUnitCost); Assert.Null(hiddenRow.InventoryValue);
        var hiddenProducts = await restricted.GetFromJsonAsync<InventoryProductPage>($"/api/commerce/v1/inventory/products?warehouseId={fixture.WarehouseId:D}&search=Insumo&page=1&pageSize=20");
        Assert.Null(Assert.Single(hiddenProducts!.Items.Where(x => x.ProductId == product)).AverageUnitCost);
    }
    [Fact]
    public async Task Conversion_loss_reduces_inventory_value_and_posts_to_the_configured_account()
    {
        var source = Guid.NewGuid();
        var output = Guid.NewGuid();
        await SeedAsync(source, output, Guid.NewGuid(), Guid.NewGuid());
        await ExecuteAsync(
            "UPDATE dbo.Products SET ConversionMaximumLossPercent=5 WHERE ProductId=@Id",
            source);
        using var client = fixture.CreateAdminClient(
            InventoryPermissionCodes.Adjust, InventoryPermissionCodes.Convert,
            InventoryPermissionCodes.Read, InventoryPermissionCodes.ReadCosts,
            InventoryPermissionCodes.ManageReasons);
        using var reasonResponse = await client.PostAsJsonAsync(
            "/api/commerce/v1/inventory/reasons",
            new SaveInventoryReasonRequest(
                InventoryDocumentTypes.Conversion, "Merma configurable de prueba", true, 900,
                "OtherExpense", null, true));
        reasonResponse.EnsureSuccessStatusCode();
        var configurableReason = await reasonResponse.Content.ReadFromJsonAsync<InventoryReasonItem>()
            ?? throw new InvalidOperationException("The configurable conversion reason is missing.");
        await ConfirmAdjustmentAsync(client, new(Guid.NewGuid(), fixture.BusinessId,
            fixture.WarehouseId, DateTimeOffset.UtcNow, "INITIAL_BALANCE", null,
            "Base para probar merma", [new(1, source, 10m, 5m)]));

        var conversionId = Guid.NewGuid();
        await SendAsync<ConfirmProductConversionRequest>(client,
            "/api/commerce/v1/product-conversions/confirm",
            new(conversionId, fixture.BusinessId, fixture.WarehouseId, DateTimeOffset.UtcNow,
                "SPLIT", configurableReason.Code, null, "Merma física verificada",
                [new(1, "INPUT", source, 10m, null), new(2, "OUTPUT", output, 9.5m, null)]),
            $"conversion-loss-{conversionId:N}");

        Assert.Equal((0m, 0m, 0m), await BalanceAsync(fixture.WarehouseId, source));
        Assert.Equal((9.5m, 5m, 47.5m), await BalanceAsync(fixture.WarehouseId, output));
        Assert.Equal(-2.5m, await ScalarAsync<decimal>(
            "SELECT TotalValueChange FROM dbo.InventoryOperations WHERE InventoryOperationId=@Id", conversionId));
        Assert.Equal("Posted", await ScalarAsync<string>(
            "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'ProductConversion'", conversionId));
        Assert.Equal(2.5m, await ScalarAsync<decimal>(
            "SELECT SUM(line.Debit-line.Credit) FROM dbo.AccountingEntries entry INNER JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId INNER JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId WHERE entry.SourceDocumentId=@Id AND account.Code=N'539595'", conversionId));
        Assert.Equal(-2.5m, await ScalarAsync<decimal>(
            "SELECT SUM(line.Debit-line.Credit) FROM dbo.AccountingEntries entry INNER JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId INNER JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId WHERE entry.SourceDocumentId=@Id AND account.Code=N'143505'", conversionId));
    }

    [Fact]
    public async Task Inventory_endpoints_enforce_permission_and_conversion_rejects_insufficient_stock()
    {
        var source = Guid.NewGuid(); var output = Guid.NewGuid(); var destination = Guid.NewGuid();
        await SeedAsync(source, output, Guid.NewGuid(), destination);
        var request = new ConfirmInventoryAdjustmentRequest(Guid.NewGuid(), fixture.BusinessId,
            fixture.WarehouseId, DateTimeOffset.UtcNow, "INITIAL_BALANCE", null, null,
            [new(1, source, 1m, 2m)]);
        using (var deniedClient = fixture.CreateAdminClient())
        using (var deniedMessage = CreateMessage("/api/commerce/v1/inventory-adjustments/confirm", request, Guid.NewGuid().ToString("N")))
        using (var denied = await deniedClient.SendAsync(deniedMessage))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var deniedDetail = await deniedClient.GetAsync($"/api/commerce/v1/inventory/operations/{request.DocumentId:D}");
            Assert.Equal(HttpStatusCode.Forbidden, deniedDetail.StatusCode);
        }

        using var client = fixture.CreateAdminClient(InventoryPermissionCodes.Adjust, InventoryPermissionCodes.Convert);
        await ConfirmAdjustmentAsync(client, request);
        var before = await BalanceAsync(fixture.WarehouseId, source);
        var conversionId = Guid.NewGuid();
        var conversion = new ConfirmProductConversionRequest(
            conversionId, fixture.BusinessId, fixture.WarehouseId, DateTimeOffset.UtcNow,
            "SPLIT", "PRESENTATION_CHANGE", null, null,
            [new(1,"INPUT",source,99m,null),new(2,"OUTPUT",output,99m,null)]);
        using (var message = CreateMessage("/api/commerce/v1/product-conversions/confirm", conversion, $"negative-{conversionId:N}"))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await BalanceAsync(fixture.WarehouseId, source));
        Assert.Equal((0m, 0m, 0m), await BalanceAsync(fixture.WarehouseId, output));
        Assert.Equal(0, await CountAsync("InventoryMovements", conversionId));
        Assert.Equal(0, await CountAsync("ServerOutboxMessages", conversionId));
    }

    [Fact]
    public async Task Active_non_sales_warehouses_are_available_to_inventory_and_system_warehouses_are_never_selectable()
    {
        var product = Guid.NewGuid();
        await SeedAsync(product, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using var client = fixture.CreateAdminClient(
            InventoryPermissionCodes.Read,
            InventoryPermissionCodes.Adjust,
            InventoryPermissionCodes.DispatchTransfer,
            InventoryPermissionCodes.ManageWarehouses,
            InventoryPermissionCodes.ManagePhysicalCounts,
            InventoryPermissionCodes.CapturePhysicalCounts,
            PurchasingPermissionCodes.ReadGoodsReceipts);

        using var create = await client.PostAsJsonAsync(
            "/api/commerce/v1/inventory/warehouse-masters",
            new SaveWarehouseRequest("Bodega Tina", false, "WeightedAverageCost", false, true));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = Assert.IsType<WarehouseMasterItem>(
            await create.Content.ReadFromJsonAsync<WarehouseMasterItem>());
        Assert.False(created.UseForSales);
        Assert.True(created.UseForGoodsReceipts);
        Assert.True(created.IsInventoryVisible);
        Assert.Equal(0, await ScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM dbo.Warehouses warehouse
            INNER JOIN dbo.Products product ON product.BusinessId=warehouse.BusinessId
            LEFT JOIN dbo.InventoryBalances balance
              ON balance.BusinessId=warehouse.BusinessId
             AND balance.WarehouseId=warehouse.WarehouseId
             AND balance.ProductId=product.ProductId
            WHERE warehouse.WarehouseId=@Id
              AND (balance.ProductId IS NULL OR balance.QuantityOnHand<>0
                   OR balance.AverageUnitCost<>0 OR balance.InventoryValue<>0)
            """, created.WarehouseId));

        var options = Assert.IsAssignableFrom<IReadOnlyList<InventoryWarehouseOption>>(
            await client.GetFromJsonAsync<List<InventoryWarehouseOption>>(
                "/api/commerce/v1/inventory/warehouses"));
        Assert.Contains(options, option => option.WarehouseId == created.WarehouseId);
        Assert.DoesNotContain(options, option => option.Code is "AVE" or "PED" or "TRA");
        var receiptOptions = Assert.IsType<GoodsReceiptWorkspaceOptions>(
            await client.GetFromJsonAsync<GoodsReceiptWorkspaceOptions>(
                "/api/commerce/v1/goods-receipts/options"));
        Assert.Contains(receiptOptions.Warehouses,
            option => option.WarehouseId == created.WarehouseId);
        Assert.DoesNotContain(receiptOptions.Warehouses,
            option => option.Code is "AVE" or "PED" or "TRA");

        await ConfirmAdjustmentAsync(client, new(
            Guid.NewGuid(), fixture.BusinessId, created.WarehouseId, DateTimeOffset.UtcNow,
            "INITIAL_BALANCE", null, null, [new(1, product, 2m, 3m)]));
        Assert.Equal((2m,3m,6m), await BalanceAsync(created.WarehouseId, product));

        var systemWarehouseId = await SystemWarehouseIdAsync("AVE");
        var rejectedAdjustment = new ConfirmInventoryAdjustmentRequest(
            Guid.NewGuid(), fixture.BusinessId, systemWarehouseId, DateTimeOffset.UtcNow,
            "INITIAL_BALANCE", null, null, [new(1, product, 1m, 1m)]);
        using (var request = CreateMessage(
                   "/api/commerce/v1/inventory-adjustments/confirm", rejectedAdjustment,
                   $"system-warehouse-{Guid.NewGuid():N}"))
        using (var response = await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var count = await client.PostAsJsonAsync(
            "/api/commerce/v1/inventory/physical-counts",
            new CreateInventoryPhysicalCountRequest(Guid.NewGuid(), fixture.BusinessId,
                systemWarehouseId, "Partial", "PHYSICAL_COUNT", null,
                "Conteo inválido de sistema", [product]));
        Assert.Equal(HttpStatusCode.BadRequest, count.StatusCode);
    }

    private async Task SeedAsync(Guid first, Guid second, Guid third, Guid destination)
    {
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Warehouses WHERE BusinessId=@BusinessId AND Code=N'TRA')
              INSERT dbo.Warehouses(WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive,CreatedAt)
              VALUES(NEWID(),@BusinessId,N'TRA',N'Mercancía en tránsito',0,1,0,0,0,1,SYSDATETIMEOFFSET());
            IF NOT EXISTS(SELECT 1 FROM dbo.Warehouses WHERE BusinessId=@BusinessId AND Code=N'PED')
              INSERT dbo.Warehouses(WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive,CreatedAt)
              VALUES(NEWID(),@BusinessId,N'PED',N'Bodega de pedidos',0,1,0,0,0,1,SYSDATETIMEOFFSET());
            IF NOT EXISTS(SELECT 1 FROM dbo.Warehouses WHERE BusinessId=@BusinessId AND Code=N'AVE')
              INSERT dbo.Warehouses(WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive,CreatedAt)
              VALUES(NEWID(),@BusinessId,N'AVE',N'Bodega de averías',0,1,0,0,0,1,SYSDATETIMEOFFSET());
            INSERT dbo.Warehouses(WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,IsActive,CreatedAt)
            VALUES(@Destination,@BusinessId,@WarehouseCode,N'Bodega destino',0,1,SYSDATETIMEOFFSET());
            INSERT dbo.TaxProfiles(
                TaxProfileId,BusinessId,Code,Name,Rate,IsActive,CreatedAt)
            VALUES(@TaxProfileId,@BusinessId,@TaxCode,N'IVA de prueba',19,1,SYSDATETIMEOFFSET());
            INSERT dbo.Products(ProductId,BusinessId,ProductCode,Reference,BaseUnitCode,TaxProfileId,Source,Sku,Name,UnitPrice,Currency,ManageStock,ConversionMaximumLossPercent,IsActive,CreatedAt)
            VALUES(@First,@BusinessId,@FirstSku,@FirstReference,N'EA',@TaxProfileId,0,@FirstSku,N'Insumo',0,N'COP',1,0,1,SYSUTCDATETIME()),
                  (@Second,@BusinessId,NULL,NULL,N'EA',@TaxProfileId,0,@SecondSku,N'Salida uno',0,N'COP',1,NULL,1,SYSUTCDATETIME()),
                  (@Third,@BusinessId,NULL,NULL,N'EA',@TaxProfileId,0,@ThirdSku,N'Salida dos',0,N'COP',1,NULL,1,SYSUTCDATETIME());
            INSERT dbo.InventoryBalances(BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,InventoryValue,LastProcessingSequence,UpdatedAt)
            SELECT product.BusinessId,warehouse.WarehouseId,product.ProductId,0,0,0,
                   COALESCE((SELECT LastCompletedSequence FROM dbo.BusinessProcessingCursors WHERE BusinessId=@BusinessId),0),SYSDATETIMEOFFSET()
            FROM dbo.Products product
            INNER JOIN dbo.Warehouses warehouse ON warehouse.BusinessId=product.BusinessId
            WHERE product.ProductId IN(@First,@Second,@Third)
              AND NOT EXISTS(SELECT 1 FROM dbo.InventoryBalances balance WHERE balance.BusinessId=product.BusinessId AND balance.WarehouseId=warehouse.WarehouseId AND balance.ProductId=product.ProductId);
            INSERT dbo.ProductLinks(ProductLinkId,BusinessId,ChildProductId,ParentProductId,InventoryFactor,PriceFactor,ConversionFactor,SharesInventory,SharesPrice,AllowsConversion,IsActive,CreatedAt)
            VALUES(NEWID(),@BusinessId,@Second,@First,NULL,NULL,1,0,0,1,1,SYSUTCDATETIME()),
                  (NEWID(),@BusinessId,@Third,@First,NULL,NULL,2,0,0,1,1,SYSUTCDATETIME());
            INSERT dbo.ProductBarcodes(ProductBarcodeId,BusinessId,ProductId,Barcode,IsPrimary,IsActive,CreatedAt)
            VALUES(NEWID(),@BusinessId,@First,@FirstBarcode,1,1,SYSUTCDATETIME());
            IF NOT EXISTS(
                SELECT 1 FROM dbo.DocumentSeries
                WHERE BusinessId=@BusinessId AND DocumentType=N'StockCount'
                  AND Prefix=N'CTI' AND SeriesCode=@Series)
            BEGIN
                INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                VALUES(NEWID(),@BusinessId,NULL,N'StockCount',N'CTI',@Series,8,1,99999999,0,1,SYSDATETIMEOFFSET()),
                      (NEWID(),@BusinessId,NULL,N'InventoryAdjustment',N'AJI',@Series,8,1,99999999,0,1,SYSDATETIMEOFFSET()),
                      (NEWID(),@BusinessId,NULL,N'WarehouseTransfer',N'TRB',@Series,8,1,99999999,0,1,SYSDATETIMEOFFSET()),
                      (NEWID(),@BusinessId,NULL,N'ProductConversion',N'CNV',@Series,8,1,99999999,0,1,SYSDATETIMEOFFSET());
                IF NOT EXISTS(SELECT 1 FROM dbo.DocumentSeries WHERE BusinessId=@BusinessId AND DocumentType=N'Damage' AND IsActive=1)
                  INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                  VALUES(NEWID(),@BusinessId,NULL,N'Damage',N'AVE',@Series,8,1,99999999,0,1,SYSDATETIMEOFFSET());
            END;
            """;
        await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Destination",destination); command.Parameters.AddWithValue("@BusinessId",fixture.BusinessId); command.Parameters.AddWithValue("@TaxProfileId",Guid.NewGuid()); command.Parameters.AddWithValue("@TaxCode",$"IVA-{first:N}"[..32]);
        command.Parameters.AddWithValue("@WarehouseCode",$"W-{destination:N}"[..18]); command.Parameters.AddWithValue("@First",first); command.Parameters.AddWithValue("@Second",second); command.Parameters.AddWithValue("@Third",third);
        command.Parameters.AddWithValue("@FirstSku",$"I-{first:N}"); command.Parameters.AddWithValue("@FirstReference",$"REF-{first:N}"); command.Parameters.AddWithValue("@FirstBarcode",$"BAR-{first:N}"); command.Parameters.AddWithValue("@SecondSku",$"O-{second:N}"); command.Parameters.AddWithValue("@ThirdSku",$"O-{third:N}"); command.Parameters.AddWithValue("@Series","00");
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedAdditionalInventoryProductAsync(Guid productId)
    {
        const string sql = """
            INSERT dbo.Products(ProductId,BusinessId,ProductCode,Reference,BaseUnitCode,TaxProfileId,Source,Sku,Name,UnitPrice,Currency,ManageStock,IsActive,CreatedAt)
            SELECT @ProductId,@BusinessId,@Sku,@Sku,N'EA',MIN(TaxProfileId),0,@Sku,N'Producto agregado después del conteo',0,N'COP',1,1,SYSUTCDATETIME()
            FROM dbo.TaxProfiles WHERE BusinessId=@BusinessId;
            INSERT dbo.InventoryBalances(BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,InventoryValue,LastProcessingSequence,UpdatedAt)
            SELECT @BusinessId,warehouse.WarehouseId,@ProductId,0,0,0,
                   COALESCE((SELECT LastCompletedSequence FROM dbo.BusinessProcessingCursors WHERE BusinessId=@BusinessId),0),SYSDATETIMEOFFSET()
            FROM dbo.Warehouses warehouse
            WHERE warehouse.BusinessId=@BusinessId;
            """;
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@Sku", $"LATE-{productId:N}"[..32]);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ClosePhysicalCountForTestAsync(Guid countId)
    {
        const string sql = """
            UPDATE dbo.InventoryPhysicalCountReconciliations SET Status=N'Superseded'
            WHERE InventoryPhysicalCountId=@CountId AND Status=N'Active';
            UPDATE dbo.InventoryPhysicalCounts SET Status=N'Cancelled',ClosedAt=SYSUTCDATETIME()
            WHERE InventoryPhysicalCountId=@CountId;
            """;
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@CountId", countId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ConfirmAdjustmentAsync(HttpClient client, ConfirmInventoryAdjustmentRequest request) =>
        _ = await SendAsync<ConfirmInventoryAdjustmentRequest>(client, "/api/commerce/v1/inventory-adjustments/confirm", request, $"adjust-{request.DocumentId:N}");

    private static async Task<InventoryOperationAcceptance> SendAsync<T>(HttpClient client, string url, T request, string key)
    {
        using var message = CreateMessage(url, request, key); using var response = await client.SendAsync(message);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            $"Expected Accepted, received {response.StatusCode}: {responseBody}");
        return await response.Content.ReadFromJsonAsync<InventoryOperationAcceptance>() ?? throw new InvalidOperationException("Acceptance missing.");
    }
    private static HttpRequestMessage CreateMessage<T>(string url,T request,string key){var message=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(request)};message.Headers.Add("Idempotency-Key",key);return message;}
    private async Task<(decimal Quantity,decimal Average,decimal Value)> BalanceAsync(Guid warehouse,Guid product)
    {await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();await using var command=new SqlCommand("SELECT QuantityOnHand,AverageUnitCost,InventoryValue FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId",connection);command.Parameters.AddWithValue("@BusinessId",fixture.BusinessId);command.Parameters.AddWithValue("@WarehouseId",warehouse);command.Parameters.AddWithValue("@ProductId",product);await using var reader=await command.ExecuteReaderAsync();if(!await reader.ReadAsync())return(0,0,0);return(reader.GetDecimal(0),reader.GetDecimal(1),reader.GetDecimal(2));}
    private async Task<T> ScalarAsync<T>(string sql,Guid id){await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@Id",id);return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!,typeof(T));}
    private async Task ExecuteAsync(string sql,Guid id){await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@Id",id);await command.ExecuteNonQueryAsync();}
    private async Task<Guid> SystemWarehouseIdAsync(string code){await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();await using var command=new SqlCommand("SELECT WarehouseId FROM dbo.Warehouses WHERE BusinessId=@BusinessId AND Code=@Code AND IsSystem=1 AND IsActive=1",connection);command.Parameters.AddWithValue("@BusinessId",fixture.BusinessId);command.Parameters.AddWithValue("@Code",code);return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException($"System warehouse '{code}' is missing."));}
    private async Task<int> CountAsync(string table,Guid id){Assert.Contains(table,new[]{"InventoryMovements","ServerOutboxMessages"});return await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.[{table}] WHERE DocumentId=@Id",id);}
    private async Task<(decimal Dispatched,decimal Received)> TransferQuantitiesAsync(Guid id,int line)
    {await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();await using var command=new SqlCommand("SELECT DispatchedQuantity,ReceivedQuantity FROM dbo.InventoryOperationLines WHERE InventoryOperationId=@Id AND LineNumber=@Line",connection);command.Parameters.AddWithValue("@Id",id);command.Parameters.AddWithValue("@Line",line);await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());return(reader.GetDecimal(0),reader.GetDecimal(1));}
    private Task<string> JobStatusAsync(Guid id)=>ScalarAsync<string>("SELECT Status FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id",id);
}
