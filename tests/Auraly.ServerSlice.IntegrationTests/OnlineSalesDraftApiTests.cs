using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Application.Sales;
using Auraly.Pos.Printing;
using System.Text.Json;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class OnlineSalesDraftApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public void Receipt_maps_only_invoice_charges_collected_from_the_customer()
    {
        var supplier = new InvoiceChargeSupplier(
            Guid.NewGuid(), "Domiciliario", "900100200", 0, true);
        var charged = new AppliedInvoiceCharge(
            Guid.NewGuid(), Guid.NewGuid(), 1, "DOM", "Domicilio", 60_000m,
            6_000m, 6_000m, 0m, 5_042.02m, 957.98m, "01", 19m,
            6_000m, 0m, Guid.NewGuid(), Guid.NewGuid(), null, null, supplier);
        var companyExpense = charged with
        {
            AppliedChargeId = Guid.NewGuid(),
            Code = "AGOTADO",
            Name = "Agotado",
            InvoicedAmount = 0m,
            ExpenseAmount = 6_000m,
            InvoicedUntaxedAmount = 0m,
            InvoicedTaxAmount = 0m
        };

        var receipt = SalesInvoicePresentationMapper.From(
            fixture.CreateValidRequest(1_982) with { Charges = [charged, companyExpense] },
            "DianAccepted");

        Assert.Contains(receipt.Lines, line => line.ProductCode == "DOM" && line.Total == 6_000m);
        Assert.DoesNotContain(receipt.Lines, line => line.ProductCode == "AGOTADO");
    }

    [Theory]
    [InlineData("Receipt", 58)]
    [InlineData("Receipt", 80)]
    [InlineData("Letter", 80)]
    public async Task Browser_print_reuses_exact_shared_template_and_needs_no_sale_reload(string format, int width)
    {
        using var client = fixture.CreateAdminClient(CommercePermissionCodes.SalesCreate);
        // These IDs were never uploaded: presentation must not issue a history/QR lookup.
        var receipt = SalesInvoicePresentationMapper.From(fixture.CreateValidRequest(1981), "DianAccepted");
        var request = new SalesReceiptsRenderRequest([receipt], format, width, "Sede", AutoPrint: false);
        using var response = await client.PostAsJsonAsync("/api/commerce/v1/pos/drafts/sales/receipts/render", request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var expected = format == "Receipt"
            ? new SalesReceiptHtmlRenderer().RenderBatch([receipt], width, "Sede", autoPrint: false)
            : new HalfLetterDocumentRenderer().Render([receipt], format, autoPrint: false);
        Assert.Equal(expected, body.GetProperty("html").GetString());
        using var empty = await client.PostAsJsonAsync("/api/commerce/v1/pos/drafts/sales/receipts/render", request with { Receipts = [] });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using var tooMany = await client.PostAsJsonAsync("/api/commerce/v1/pos/drafts/sales/receipts/render", request with { Receipts = Enumerable.Repeat(receipt, 501).ToArray() });
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        using var anonymous = fixture.CreateClient();
        using var denied = await anonymous.PostAsJsonAsync("/api/commerce/v1/pos/drafts/sales/receipts/render", request);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task Enrolled_device_return_bootstrap_uses_business_scope_without_session_or_device_affinity()
    {
        using (var client = fixture.CreateClient())
        using (var message = DeviceRequest("/api/pos/v1/sales-returns/bootstrap",
                   new { fixture.BusinessId, fixture.WorkSessionId }, fixture.DeviceId,
                   ServerSliceFixture.DeviceSecret))
        using (var response = await client.SendAsync(message))
            response.EnsureSuccessStatusCode();

        using (var client = fixture.CreateClient())
        using (var message = DeviceRequest("/api/pos/v1/sales-returns/bootstrap",
                   new { fixture.BusinessId, fixture.WorkSessionId }, fixture.DeniedDeviceId,
                   ServerSliceFixture.DeniedDeviceSecret))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    [Fact]
    public async Task Enrolled_device_history_uses_business_scope_without_cashier_session_affinity()
    {
        var request = new SearchOnlineSalesIssuedSalesRequest(
            new OnlineSalesHistoryContext(fixture.BusinessId),
            Take: 20);

        using (var client = fixture.CreateClient())
        using (var message = DeviceHistoryRequest(
                   request,
                   fixture.DeviceId,
                   ServerSliceFixture.DeviceSecret))
        using (var response = await client.SendAsync(message))
        {
            response.EnsureSuccessStatusCode();
            Assert.NotNull(await response.Content.ReadFromJsonAsync<OnlineSalesIssuedSalePage>());
        }

        using (var client = fixture.CreateClient())
        using (var message = DeviceHistoryRequest(
                   request,
                   fixture.DeniedDeviceId,
                   ServerSliceFixture.DeniedDeviceSecret))
        using (var response = await client.SendAsync(message))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Changing_workspace_updates_the_active_draft_warehouse_and_its_inventory_policy()
    {
        var warehouseId = Guid.NewGuid();
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = new SqlCommand(
                """
                INSERT dbo.Warehouses(
                  WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,
                  PriceFormationCostBasis,IsSystem,UseForSales,UseForGoodsReceipts,
                  IsInventoryVisible,IsActive,CreatedAt)
                VALUES(
                  @WarehouseId,@BusinessId,@Code,N'Bodega alterna sin negativos',0,
                  N'WeightedAverageCost',0,1,1,1,1,SYSDATETIMEOFFSET());
                """, connection);
            seed.Parameters.AddWithValue("@WarehouseId", warehouseId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@Code", $"ALT-{warehouseId:N}"[..20]);
            await seed.ExecuteNonQueryAsync();
        }

        try
        {
            using var client = fixture.CreateAdminClient(
                CommercePermissionCodes.SalesCreate,
                CommercePermissionCodes.SalesRestartDraft);
            var current = await OpenAsync(client, new(
                fixture.BusinessId, warehouseId, fixture.WorkSessionId));
            if (current.Lines.Count > 0)
            {
                using var reset = Mutation(
                    HttpMethod.Post,
                    $"/api/commerce/v1/pos/drafts/{current.DraftId:D}/reset",
                    new ResetOnlineSalesDraftRequest(current.Version),
                    Guid.NewGuid().ToString("D"));
                using var resetResponse = await client.SendAsync(reset);
                resetResponse.EnsureSuccessStatusCode();
                current = await resetResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>()
                    ?? throw new InvalidOperationException("The reset draft response was empty.");
            }

            using var deniedAdd = Mutation(
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{current.DraftId:D}/items",
                new AddOnlineSalesDraftItemRequest(
                    fixture.ProductId.ToString("D"), 100000m, current.Version),
                Guid.NewGuid().ToString("D"));
            using var deniedResponse = await client.SendAsync(deniedAdd);
            Assert.Equal(HttpStatusCode.BadRequest, deniedResponse.StatusCode);

            var allowsNegative = await OpenAsync(client, new(
                fixture.BusinessId, fixture.WarehouseId, fixture.WorkSessionId));
            Assert.Equal(current.DraftId, allowsNegative.DraftId);

            using var allowedAdd = Mutation(
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{allowsNegative.DraftId:D}/items",
                new AddOnlineSalesDraftItemRequest(
                    fixture.ProductId.ToString("D"), 100000m, allowsNegative.Version),
                Guid.NewGuid().ToString("D"));
            using var allowedResponse = await client.SendAsync(allowedAdd);
            allowedResponse.EnsureSuccessStatusCode();
            var updated = await allowedResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>()
                ?? throw new InvalidOperationException("The updated draft response was empty.");

            await using var checkConnection = new SqlConnection(fixture.ConnectionString);
            await checkConnection.OpenAsync();
            await using var check = new SqlCommand(
                "SELECT WarehouseId FROM dbo.SalesDrafts WHERE SalesDraftId=@DraftId;",
                checkConnection);
            check.Parameters.AddWithValue("@DraftId", updated.DraftId);
            Assert.Equal(fixture.WarehouseId, (Guid?)await check.ExecuteScalarAsync());

            using var cleanupDraft = Mutation(
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{updated.DraftId:D}/reset",
                new ResetOnlineSalesDraftRequest(updated.Version),
                Guid.NewGuid().ToString("D"));
            using var cleanupDraftResponse = await client.SendAsync(cleanupDraft);
            cleanupDraftResponse.EnsureSuccessStatusCode();
        }
        finally
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var cleanup = new SqlCommand(
                """
                DELETE dbo.Warehouses WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId;
                """, connection);
            cleanup.Parameters.AddWithValue("@WarehouseId", warehouseId);
            cleanup.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Saved_order_clears_its_draft_without_restart_permission()
    {
        var customerId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var partySiteId = Guid.NewGuid();
        await using (var customerConnection = new SqlConnection(fixture.ConnectionString))
        {
            await customerConnection.OpenAsync();
            await using var createCustomer = new SqlCommand(
                """
                DECLARE @CountryId UNIQUEIDENTIFIER,@DivisionId UNIQUEIDENTIFIER,@CityId UNIQUEIDENTIFIER;
                SELECT TOP(1) @CountryId=country.CountryId,
                              @DivisionId=division.AdministrativeDivisionId,
                              @CityId=city.CityId
                FROM dbo.Cities city
                JOIN dbo.AdministrativeDivisions division
                  ON division.AdministrativeDivisionId=city.AdministrativeDivisionId
                JOIN dbo.Countries country ON country.CountryId=division.CountryId
                WHERE city.IsActive=1 AND division.IsActive=1 AND country.IsActive=1;

                INSERT dbo.Parties(
                  PartyId,TenantId,PartyType,DisplayName,LegalName,CompletionStatus,
                  IsActive,CreatedBy,CreatedAt)
                VALUES(@PartyId,@TenantId,N'Organization',N'Cliente pedido web',
                  N'Cliente pedido web',N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());
                INSERT dbo.Customers(CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
                VALUES(@CustomerId,@PartyId,@BusinessId,1,@UserId,SYSDATETIMEOFFSET());
                INSERT dbo.PartySites(
                  PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,
                  CityId,AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
                VALUES(@PartySiteId,@PartyId,N'PRINCIPAL',N'Sede principal',@CountryId,
                  @DivisionId,@CityId,N'Calle prueba 1',1,1,@UserId,SYSDATETIMEOFFSET());
                """,
                customerConnection);
            createCustomer.Parameters.AddWithValue("@PartyId", partyId);
            createCustomer.Parameters.AddWithValue("@CustomerId", customerId);
            createCustomer.Parameters.AddWithValue("@PartySiteId", partySiteId);
            createCustomer.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            createCustomer.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            createCustomer.Parameters.AddWithValue("@UserId", fixture.UserId);
            await createCustomer.ExecuteNonQueryAsync();
        }
        using var client = fixture.CreateAdminClient(CommercePermissionCodes.SalesCreate);
        var draft = await OpenAsync(client, new(
            fixture.BusinessId, fixture.WarehouseId, fixture.WorkSessionId));
        Assert.Empty(draft.Lines);

        using (var add = Mutation(
                   HttpMethod.Post,
                   $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items",
                   new AddOnlineSalesDraftItemRequest(
                       fixture.ProductId.ToString("D"), 1m, draft.Version),
                   Guid.NewGuid().ToString("D")))
        using (var response = await client.SendAsync(add))
        {
            response.EnsureSuccessStatusCode();
            draft = await response.Content.ReadFromJsonAsync<OnlineSalesDraft>()
                ?? throw new InvalidOperationException("The captured draft was empty.");
        }

        using (var incompleteCustomer = Mutation(
                   HttpMethod.Put,
                   $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/customer",
                   new SelectOnlineSalesDraftCustomerRequest(
                       customerId, draft.Version, PartySiteId: null),
                   Guid.NewGuid().ToString("D")))
        using (var response = await client.SendAsync(incompleteCustomer))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var customer = Mutation(
                   HttpMethod.Put,
                   $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/customer",
                   new SelectOnlineSalesDraftCustomerRequest(
                       customerId, draft.Version, partySiteId),
                   Guid.NewGuid().ToString("D")))
        using (var response = await client.SendAsync(customer))
        {
            response.EnsureSuccessStatusCode();
            var selection = await response.Content
                .ReadFromJsonAsync<OnlineSalesCustomerSelection>();
            draft = selection?.Draft
                ?? throw new InvalidOperationException("The customer selection was empty.");
        }

        var orderId = Guid.NewGuid();
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = new SqlCommand(
                """
                INSERT dbo.Orders(
                    OrderId,BusinessId,CustomerId,PartySiteId,WarehouseId,CapturedByUserId,
                    Source,Status,CustomerNameSnapshot,Total,IdempotencyKey)
                VALUES(
                    @OrderId,@BusinessId,@CustomerId,@PartySiteId,@WarehouseId,@UserId,
                    1,3,N'Cliente de prueba',0,@IdempotencyKey);
                """, connection);
            seed.Parameters.AddWithValue("@OrderId", orderId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@CustomerId", customerId);
            seed.Parameters.AddWithValue("@PartySiteId", partySiteId);
            seed.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            seed.Parameters.AddWithValue("@UserId", fixture.UserId);
            seed.Parameters.AddWithValue(
                "@IdempotencyKey", $"pos-order-{draft.DraftId:D}-{draft.Version}");
            await seed.ExecuteNonQueryAsync();
        }

        try
        {
            using var complete = Mutation(
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/complete-order",
                new CompleteOnlineSalesOrderDraftRequest(orderId, draft.Version),
                Guid.NewGuid().ToString("D"));
            using var response = await client.SendAsync(complete);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"Expected workflow cleanup without restart permission, got {response.StatusCode}: {body}");
            var next = await response.Content.ReadFromJsonAsync<OnlineSalesDraft>();
            Assert.NotNull(next);
            Assert.NotEqual(draft.DraftId, next.DraftId);
            Assert.Empty(next.Lines);
        }
        finally
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var cleanup = new SqlCommand(
                "DELETE dbo.Orders WHERE OrderId=@OrderId;", connection);
            cleanup.Parameters.AddWithValue("@OrderId", orderId);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Generic_product_accepts_document_name_cost_and_price_with_zero_discount()
    {
        var productId = Guid.NewGuid();
        var priceId = Guid.NewGuid();
        var code = $"GEN-{productId:N}"[..20];
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = new SqlCommand(
                """
                INSERT dbo.Products
                  (ProductId,TenantId,BusinessId,Source,Sku,Name,
                   Currency,ManageStock,IsGenericProduct,IsActive,CreatedAt)
                VALUES
                  (@ProductId,@TenantId,@BusinessId,0,@Code,N'Producto genérico',
                   N'COP',0,1,1,SYSUTCDATETIME());
                INSERT dbo.ProductPrices
                  (ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,
                   CostBasisType,CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,
                   InputMode,RoundingIncrement,RoundingMode,ValidFrom,IsActive,CreatedAt)
                VALUES
                  (@PriceId,@BusinessId,@ProductId,10000,10000,N'COP',
                   N'AverageCost',4000,60,60,N'SalePrice',1,N'Nearest',
                   DATEADD(day,-1,SYSDATETIMEOFFSET()),1,SYSDATETIMEOFFSET());
                """, connection);
            seed.Parameters.AddWithValue("@ProductId", productId);
            seed.Parameters.AddWithValue("@PriceId", priceId);
            seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@Code", code);
            await seed.ExecuteNonQueryAsync();
        }

        using var client = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate,
            CommercePermissionCodes.SalesChangePrice,
            CommercePermissionCodes.SalesReadCostAndMargin,
            CommercePermissionCodes.SalesChangeDescription,
            CommercePermissionCodes.SalesRestartDraft);
        var opened = await OpenAsync(client, new(
            fixture.BusinessId, fixture.WarehouseId, fixture.WorkSessionId));
        if (opened.Lines.Count > 0)
        {
            using var reset = Mutation(
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/reset",
                new ResetOnlineSalesDraftRequest(opened.Version),
                Guid.NewGuid().ToString("D"));
            using var resetResponse = await client.SendAsync(reset);
            resetResponse.EnsureSuccessStatusCode();
            opened = await resetResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>()
                ?? throw new InvalidOperationException("The reset draft response was empty.");
        }
        using var add = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/items",
            new AddOnlineSalesDraftItemRequest(code, 2m, opened.Version),
            $"generic-add-{Guid.NewGuid():N}");
        using var addResponse = await client.SendAsync(add);
        addResponse.EnsureSuccessStatusCode();
        var captured = await addResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>();
        var line = Assert.Single(captured!.Lines);
        Assert.True(line.AllowsDocumentCostOverride);
        Assert.Equal(0m, line.PublicUnitPrice);
        Assert.Equal(0m, line.DocumentUnitCost);

        using var unauthorizedProration = Mutation(
            HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/lines",
            new UpdateOnlineSalesDraftLinesRequest(
                [new(line.LineId, line.Description, 12_000m, 2_000m, 4_500m)],
                captured.Version,
                IncludesProratedDiscount: true),
            Guid.NewGuid().ToString("D"));
        using var unauthorizedProrationResponse = await client.SendAsync(unauthorizedProration);
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedProrationResponse.StatusCode);

        using var negativeCost = Mutation(
            HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/lines",
            new UpdateOnlineSalesDraftLinesRequest(
                [new(line.LineId, line.Description, line.PublicUnitPrice, line.Discount, -1m)],
                captured.Version),
            Guid.NewGuid().ToString("D"));
        using var negativeCostResponse = await client.SendAsync(negativeCost);
        Assert.Equal(HttpStatusCode.BadRequest, negativeCostResponse.StatusCode);

        using var update = Mutation(
            HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/lines",
            new UpdateOnlineSalesDraftLinesRequest(
                [new(line.LineId, "Servicio puntual", 13_000m, 0m, 4_500m)],
                captured.Version),
            Guid.NewGuid().ToString("D"));
        using var updateResponse = await client.SendAsync(update);
        updateResponse.EnsureSuccessStatusCode();
        var changed = await updateResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>();
        var changedLine = Assert.Single(changed!.Lines);
        Assert.Equal("Servicio puntual", changedLine.Description);
        Assert.Equal(13_000m, changedLine.PublicUnitPrice);
        Assert.Equal(26_000m, changedLine.PublicLineTotal);
        Assert.Equal(0m, changedLine.Discount);
        Assert.Equal(4_500m, changedLine.DocumentUnitCost);

        await using var checkConnection = new SqlConnection(fixture.ConnectionString);
        await checkConnection.OpenAsync();
        await using var check = new SqlCommand(
            """
            SELECT p.Name,pp.Amount,pp.CostBasisAmount,l.Description,l.UnitPrice,
                   l.DiscountAmount,l.DocumentUnitCost,l.PublicUnitPrice,l.PublicLineTotal,
                   l.IsGenericProductSnapshot
            FROM dbo.Products p
            INNER JOIN dbo.ProductPrices pp ON pp.ProductId=p.ProductId AND pp.IsActive=1
            INNER JOIN dbo.SalesDraftLines l ON l.ProductId=p.ProductId
            WHERE p.ProductId=@ProductId AND l.SalesDraftId=@DraftId;
            """, checkConnection);
        check.Parameters.AddWithValue("@ProductId", productId);
        check.Parameters.AddWithValue("@DraftId", opened.DraftId);
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Producto genérico", reader.GetString(0));
        Assert.Equal(10_000m, reader.GetDecimal(1));
        Assert.Equal(4_000m, reader.GetDecimal(2));
        Assert.Equal("Servicio puntual", reader.GetString(3));
        Assert.Equal(changedLine.UnitPrice, reader.GetDecimal(4));
        Assert.Equal(0m, reader.GetDecimal(5));
        Assert.Equal(4_500m, reader.GetDecimal(6));
        Assert.Equal(13_000m, reader.GetDecimal(7));
        Assert.Equal(26_000m, reader.GetDecimal(8));
        Assert.True(reader.GetBoolean(9));
        await reader.DisposeAsync();

        using var addSameProduct = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{changed.DraftId:D}/items",
            new AddOnlineSalesDraftItemRequest(code, 1m, changed.Version),
            $"generic-add-again-{Guid.NewGuid():N}");
        using var addSameProductResponse = await client.SendAsync(addSameProduct);
        addSameProductResponse.EnsureSuccessStatusCode();
        var withNewLine = await addSameProductResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("The updated draft response was empty.");
        Assert.Equal(2, withNewLine.Lines.Count);
        var persistedEditedLine = withNewLine.Lines.Single(value => value.LineId == line.LineId);
        Assert.Equal(13_000m, persistedEditedLine.PublicUnitPrice);
        Assert.Equal(0m, persistedEditedLine.Discount);
        Assert.Equal(4_500m, persistedEditedLine.DocumentUnitCost);
        Assert.Equal("Manual", persistedEditedLine.PriceSource);
        Assert.Equal(0m, withNewLine.Lines.Single(value => value.LineId != line.LineId).PublicUnitPrice);

        using var protectedDiscard = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{withNewLine.DraftId:D}/lines/{line.LineId:D}/discard-unpriced-generic",
            new RemoveOnlineSalesDraftLineRequest(withNewLine.Version),
            Guid.NewGuid().ToString("D"));
        using var protectedDiscardResponse = await client.SendAsync(protectedDiscard);
        Assert.Equal(HttpStatusCode.BadRequest, protectedDiscardResponse.StatusCode);

        var unpricedLine = withNewLine.Lines.Single(value => value.LineId != line.LineId);
        using var discard = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{withNewLine.DraftId:D}/lines/{unpricedLine.LineId:D}/discard-unpriced-generic",
            new RemoveOnlineSalesDraftLineRequest(withNewLine.Version),
            Guid.NewGuid().ToString("D"));
        using var discardResponse = await client.SendAsync(discard);
        discardResponse.EnsureSuccessStatusCode();
        withNewLine = await discardResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("The discarded draft response was empty.");
        Assert.Single(withNewLine.Lines);

        using var addReplacement = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{withNewLine.DraftId:D}/items",
            new AddOnlineSalesDraftItemRequest(code, 1m, withNewLine.Version),
            $"generic-add-replacement-{Guid.NewGuid():N}");
        using var addReplacementResponse = await client.SendAsync(addReplacement);
        addReplacementResponse.EnsureSuccessStatusCode();
        withNewLine = await addReplacementResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("The replacement draft response was empty.");

        var reloaded = await OpenAsync(client, new(
            fixture.BusinessId, fixture.WarehouseId, fixture.WorkSessionId));
        Assert.Equal(withNewLine.DraftId, reloaded.DraftId);
        Assert.Equal(13_000m, reloaded.Lines.Single(value => value.LineId == line.LineId).PublicUnitPrice);
        Assert.Equal(4_500m, reloaded.Lines.Single(value => value.LineId == line.LineId).DocumentUnitCost);
        Assert.Equal("Manual", reloaded.Lines.Single(value => value.LineId == line.LineId).PriceSource);

        using var belowCostUpdate = Mutation(
            HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{reloaded.DraftId:D}/lines",
            new UpdateOnlineSalesDraftLinesRequest(
                reloaded.Lines.Select(value => new UpdateSalesDraftLineRequest(
                    value.LineId,
                    value.Description,
                    value.LineId == line.LineId ? value.PublicUnitPrice : 10_000m,
                    0m,
                    value.LineId == line.LineId ? 14_000m : 4_000m)).ToArray(),
                reloaded.Version),
            Guid.NewGuid().ToString("D"));
        using var belowCostUpdateResponse = await client.SendAsync(belowCostUpdate);
        belowCostUpdateResponse.EnsureSuccessStatusCode();
        var belowCost = await belowCostUpdateResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("The below-cost draft response was empty.");

        await using (var catalogChange = new SqlCommand(
                         "UPDATE dbo.Products SET IsGenericProduct=0,ManageStock=1,Name=N'Nombre posterior del catálogo' WHERE ProductId=@ProductId;",
                         checkConnection))
        {
            catalogChange.Parameters.AddWithValue("@ProductId", productId);
            Assert.Equal(1, await catalogChange.ExecuteNonQueryAsync());
        }

        var checkoutKey = $"generic-checkout-{Guid.NewGuid():N}";
        using (var forbiddenComplete = Mutation(
                   HttpMethod.Post,
                   $"/api/commerce/v1/pos/drafts/{belowCost.DraftId:D}/complete",
                   new CompleteOnlineSalesDraftRequest(
                       belowCost.Version,
                       [new OnlineSalesPayment("Cash", belowCost.PayableAmount, null)],
                       DocumentType: PosSaleDocumentTypes.Receipt),
                   checkoutKey))
        using (var forbiddenResponse = await client.SendAsync(forbiddenComplete))
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        using var complete = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{belowCost.DraftId:D}/complete",
            new CompleteOnlineSalesDraftRequest(
                belowCost.Version,
                [new OnlineSalesPayment("Cash", belowCost.PayableAmount, null)],
                DocumentType: PosSaleDocumentTypes.Receipt),
            checkoutKey);
        complete.Headers.Add("X-Auraly-Operation-Id", Guid.NewGuid().ToString("D"));
        using var authorizedCheckout = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate,
            CommercePermissionCodes.SalesBelowCost);
        using var completeResponse = await authorizedCheckout.SendAsync(complete);
        Assert.True(
            completeResponse.IsSuccessStatusCode,
            $"La venta bajo costo autorizada respondió {(int)completeResponse.StatusCode}: {await completeResponse.Content.ReadAsStringAsync()}");
        var completed = await completeResponse.Content.ReadFromJsonAsync<CompleteOnlineSalesDraftResponse>()
            ?? throw new InvalidOperationException("The completed sale response was empty.");
        Assert.Empty(completed.NextDraft.Lines);
        Assert.Equal(2, completed.Receipt.Lines.Count);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var projectionReady = new SqlCommand(
                """
                SELECT job.Status,job.LastError,
                       (SELECT COUNT(*) FROM reporting.SalesReportLineFacts fact
                        WHERE fact.SourceDocumentId=@DocumentId)
                FROM reporting.SalesReportingJobs job
                WHERE job.SourceDocumentId=@DocumentId;
                """,
                checkConnection);
            projectionReady.Parameters.AddWithValue("@DocumentId", completed.Receipt.DocumentId);
            await using var projectionReader = await projectionReady.ExecuteReaderAsync();
            Assert.True(await projectionReader.ReadAsync());
            var projectionStatus = projectionReader.GetString(0);
            var projectionError = projectionReader.IsDBNull(1) ? null : projectionReader.GetString(1);
            var projectedLineCount = projectionReader.GetInt32(2);
            if (projectedLineCount == 2)
                break;
            if (projectionStatus == "Failed" || attempt == 99)
                Assert.Fail($"La proyección terminó en {projectionStatus}: {projectionError ?? "sin detalle"}.");
            await Task.Delay(50);
        }

        await using var profitability = new SqlCommand(
            """
            SELECT document.ProcessingStatus,job.Status,job.LastError
            FROM dbo.SalesDocuments document
            INNER JOIN dbo.DocumentProcessingJobs job
              ON job.DocumentId=document.DocumentId AND job.DocumentType=document.DocumentType
            WHERE document.DocumentId=@DocumentId;

            SELECT line.LineNumber,line.UnitCostSnapshot,fact.RecognizedCostAmount,
                   line.IsGenericProductSnapshot,line.ProductCodeSnapshot,line.ProductNameSnapshot
            FROM dbo.SalesDocumentLines line
            INNER JOIN reporting.SalesReportLineFacts fact
              ON fact.SourceDocumentId=line.DocumentId
             AND fact.SourceLineNumber=line.LineNumber
             AND fact.MovementType=N'Sale'
            WHERE line.DocumentId=@DocumentId
            ORDER BY line.LineNumber;

            SELECT TotalAmount,RecognizedCostAmount,TotalAmount-RecognizedCostAmount
            FROM reporting.SalesReportDocuments
            WHERE DocumentId=@DocumentId;

            SELECT COUNT(*)
            FROM dbo.InventoryMovements
            WHERE DocumentId=@DocumentId;
            """, checkConnection);
        profitability.Parameters.AddWithValue("@DocumentId", completed.Receipt.DocumentId);
        await using var profitabilityReader = await profitability.ExecuteReaderAsync();
        Assert.True(await profitabilityReader.ReadAsync());
        Assert.Equal("Completed", profitabilityReader.GetString(0));
        Assert.Equal("Completed", profitabilityReader.GetString(1));
        Assert.True(
            profitabilityReader.IsDBNull(2),
            profitabilityReader.IsDBNull(2) ? null : profitabilityReader.GetString(2));
        Assert.True(await profitabilityReader.NextResultAsync());
        Assert.True(await profitabilityReader.ReadAsync());
        Assert.Equal(1, profitabilityReader.GetInt32(0));
        Assert.Equal(14_000m, profitabilityReader.GetDecimal(1));
        Assert.Equal(28_000m, profitabilityReader.GetDecimal(2));
        Assert.True(profitabilityReader.GetBoolean(3));
        Assert.Equal(code, profitabilityReader.GetString(4));
        Assert.Equal("Servicio puntual", profitabilityReader.GetString(5));
        Assert.True(await profitabilityReader.ReadAsync());
        Assert.Equal(2, profitabilityReader.GetInt32(0));
        Assert.Equal(4_000m, profitabilityReader.GetDecimal(1));
        Assert.Equal(4_000m, profitabilityReader.GetDecimal(2));
        Assert.True(profitabilityReader.GetBoolean(3));
        Assert.Equal(code, profitabilityReader.GetString(4));
        Assert.Equal("Producto genérico", profitabilityReader.GetString(5));
        Assert.False(await profitabilityReader.ReadAsync());
        Assert.True(await profitabilityReader.NextResultAsync());
        Assert.True(await profitabilityReader.ReadAsync());
        var projectedTotalAmount = profitabilityReader.GetDecimal(0);
        var projectedCostAmount = profitabilityReader.GetDecimal(1);
        var projectedProfit = profitabilityReader.GetDecimal(2);
        Assert.Equal(completed.Receipt.PayableAmount, projectedTotalAmount);
        Assert.Equal(32_000m, projectedCostAmount);
        Assert.Equal(projectedTotalAmount - 32_000m, projectedProfit);
        Assert.True(await profitabilityReader.NextResultAsync());
        Assert.True(await profitabilityReader.ReadAsync());
        Assert.Equal(0, profitabilityReader.GetInt32(0));
    }

    [Fact]
    public async Task Stocked_product_accepts_a_higher_public_price_and_preserves_inventory_cost()
    {
        using var client = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate,
            CommercePermissionCodes.SalesChangePrice,
            CommercePermissionCodes.SalesReadCostAndMargin,
            CommercePermissionCodes.SalesRestartDraft);
        var opened = await OpenAsync(client, new(
            fixture.BusinessId, fixture.WarehouseId, fixture.WorkSessionId));
        if (opened.Lines.Count > 0)
        {
            using var reset = Mutation(
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/reset",
                new ResetOnlineSalesDraftRequest(opened.Version),
                Guid.NewGuid().ToString("D"));
            using var resetResponse = await client.SendAsync(reset);
            resetResponse.EnsureSuccessStatusCode();
            opened = await resetResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>()
                ?? throw new InvalidOperationException("The reset draft response was empty.");
        }

        using var add = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/items",
            new AddOnlineSalesDraftItemRequest(fixture.ProductId.ToString("D"), 1m, opened.Version),
            $"stock-cost-add-{Guid.NewGuid():N}");
        using var addResponse = await client.SendAsync(add);
        addResponse.EnsureSuccessStatusCode();
        var captured = await addResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("The add item response was empty.");
        var line = Assert.Single(captured.Lines);
        Assert.False(line.AllowsDocumentCostOverride);

        using var raisePrice = Mutation(
            HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{captured.DraftId:D}/lines",
            new UpdateOnlineSalesDraftLinesRequest(
                [new(
                    line.LineId,
                    line.Description,
                    line.PublicUnitPrice + 3_000m,
                    0m,
                    line.DocumentUnitCost)],
                captured.Version),
            Guid.NewGuid().ToString("D"));
        using var raisePriceResponse = await client.SendAsync(raisePrice);
        raisePriceResponse.EnsureSuccessStatusCode();
        var repriced = await raisePriceResponse.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("The line update response was empty.");
        var repricedLine = Assert.Single(repriced.Lines);
        Assert.Equal(line.PublicUnitPrice + 3_000m, repricedLine.PublicUnitPrice);
        Assert.Equal(0m, repricedLine.Discount);
        Assert.Equal("Manual", repricedLine.PriceSource);

        using var update = Mutation(
            HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{repriced.DraftId:D}/lines",
            new UpdateOnlineSalesDraftLinesRequest(
                [new(
                    repricedLine.LineId,
                    repricedLine.Description,
                    repricedLine.PublicUnitPrice,
                    repricedLine.Discount,
                    repricedLine.DocumentUnitCost + 1_000m)],
                repriced.Version),
            Guid.NewGuid().ToString("D"));
        using var updateResponse = await client.SendAsync(update);
        Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);
        Assert.Contains("congelado", await updateResponse.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        using var cleanup = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{repriced.DraftId:D}/reset",
            new ResetOnlineSalesDraftRequest(repriced.Version),
            Guid.NewGuid().ToString("D"));
        using var cleanupResponse = await client.SendAsync(cleanup);
        cleanupResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Draft_survives_client_restart_and_mutations_are_idempotent()
    {
        var context = new OnlineSalesDraftContext(
            fixture.BusinessId,
            fixture.WarehouseId,
            fixture.WorkSessionId);
        Guid draftId;
        Guid lineId;
        using (var firstClient = fixture.CreateAdminClient(
                   CommercePermissionCodes.SalesCreate))
        {
            var opened = await OpenAsync(firstClient, context);
            Assert.Equal(1, opened.Version);
            Assert.Empty(opened.Lines);

            using var add = Mutation(
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/items",
                new AddOnlineSalesDraftItemRequest(fixture.ProductId.ToString("D"), 1m, opened.Version),
                "add-product-once");
            using var addedResponse = await firstClient.SendAsync(add);
            addedResponse.EnsureSuccessStatusCode();
            var added = await addedResponse.Content
                .ReadFromJsonAsync<OnlineSalesDraft>();
            Assert.NotNull(added);
            Assert.Equal(2, added.Version);
            var line = Assert.Single(added.Lines);
            Assert.Equal(1m, line.Quantity);
            Assert.Equal(10_000m, added.PayableAmount);
            draftId = added.DraftId;
            lineId = line.LineId;

            using var duplicate = Mutation(
                HttpMethod.Post,
                $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/items",
                new AddOnlineSalesDraftItemRequest(fixture.ProductId.ToString("D"), 1m, opened.Version),
                "add-product-once");
            using var duplicateResponse = await firstClient.SendAsync(duplicate);
            duplicateResponse.EnsureSuccessStatusCode();
            var replayed = await duplicateResponse.Content
                .ReadFromJsonAsync<OnlineSalesDraft>();
            Assert.NotNull(replayed);
            Assert.Equal(2, replayed.Version);
            Assert.Equal(1m, Assert.Single(replayed.Lines).Quantity);
        }

        using var reopenedClient = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate,
            CommercePermissionCodes.SalesRestartDraft);
        var recovered = await OpenAsync(reopenedClient, context);
        Assert.Equal(draftId, recovered.DraftId);
        Assert.Equal(1m, Assert.Single(recovered.Lines).Quantity);

        using var quantity = Mutation(
            HttpMethod.Put,
            $"/api/commerce/v1/pos/drafts/{draftId:D}/lines/{lineId:D}/quantity",
            new ChangeOnlineSalesDraftQuantityRequest(3m, recovered.Version),
            "quantity-three");
        using var quantityResponse = await reopenedClient.SendAsync(quantity);
        quantityResponse.EnsureSuccessStatusCode();
        var changed = await quantityResponse.Content
            .ReadFromJsonAsync<OnlineSalesDraft>();
        Assert.NotNull(changed);
        Assert.Equal(3m, Assert.Single(changed.Lines).Quantity);
        Assert.Equal(30_000m, changed.PayableAmount);

        using var reset = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draftId:D}/reset",
            new ResetOnlineSalesDraftRequest(changed.Version),
            Guid.NewGuid().ToString("D"));
        using var resetResponse = await reopenedClient.SendAsync(reset);
        resetResponse.EnsureSuccessStatusCode();
        var next = await resetResponse.Content
            .ReadFromJsonAsync<OnlineSalesDraft>();
        Assert.NotNull(next);
        Assert.NotEqual(draftId, next.DraftId);
        Assert.Empty(next.Lines);
        Assert.Equal(1, next.Version);

        var reopenedEmpty = await OpenAsync(reopenedClient, context);
        Assert.Equal(next.DraftId, reopenedEmpty.DraftId);
    }

    [Fact]
    public async Task Two_tabs_cannot_silently_overwrite_the_same_version()
    {
        using var client = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate);
        var opened = await OpenAsync(
            client,
            new(
                fixture.BusinessId,
                fixture.WarehouseId,
                fixture.WorkSessionId));

        using var first = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/items",
            new AddOnlineSalesDraftItemRequest(fixture.ProductId.ToString("D"), 1m, opened.Version),
            $"tab-a-{Guid.NewGuid():N}");
        using var second = Mutation(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{opened.DraftId:D}/items",
            new AddOnlineSalesDraftItemRequest(fixture.ProductId.ToString("D"), 2m, opened.Version),
            $"tab-b-{Guid.NewGuid():N}");

        var responses = await Task.WhenAll(
            client.SendAsync(first),
            client.SendAsync(second));
        try
        {
            Assert.Single(responses, response => response.IsSuccessStatusCode);
            Assert.Single(
                responses,
                response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task Draft_scope_and_permission_are_enforced_by_the_server()
    {
        using var denied = fixture.CreateAdminClient();
        using var permissionResponse = await denied.PostAsJsonAsync(
            "/api/commerce/v1/pos/drafts/active",
            new OpenOnlineSalesDraftRequest(new(
                fixture.BusinessId,
                fixture.WarehouseId,
                fixture.WorkSessionId)));
        Assert.Equal(HttpStatusCode.Forbidden, permissionResponse.StatusCode);

        using var allowed = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate);
        using var scopeResponse = await allowed.PostAsJsonAsync(
            "/api/commerce/v1/pos/drafts/active",
            new OpenOnlineSalesDraftRequest(new(
                Guid.NewGuid(),
                fixture.WarehouseId,
                fixture.WorkSessionId)));
        Assert.Equal(HttpStatusCode.Forbidden, scopeResponse.StatusCode);
    }

    private static async Task<OnlineSalesDraft> OpenAsync(
        HttpClient client,
        OnlineSalesDraftContext context)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/drafts/active",
            new OpenOnlineSalesDraftRequest(context));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException(
                "The online draft response was empty.");
    }

    private static HttpRequestMessage Mutation<T>(
        HttpMethod method,
        string path,
        T body,
        string idempotencyKey)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private HttpRequestMessage DeviceHistoryRequest(
        SearchOnlineSalesIssuedSalesRequest request,
        Guid deviceId,
        string deviceSecret)
    {
        var message = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/pos/v1/history/sales/search")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-Auraly-Device-Id", deviceId.ToString("D"));
        message.Headers.Add("X-Auraly-Device-Secret", deviceSecret);
        message.Headers.Add("X-Auraly-User-Id", fixture.UserId.ToString("D"));
        message.Headers.Add("X-Auraly-Work-Session-Id", fixture.WorkSessionId.ToString("D"));
        return message;
    }

    private HttpRequestMessage DeviceRequest<T>(string path, T body, Guid deviceId, string secret)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        message.Headers.Add("X-Auraly-Device-Id", deviceId.ToString("D"));
        message.Headers.Add("X-Auraly-Device-Secret", secret);
        message.Headers.Add("X-Auraly-User-Id", fixture.UserId.ToString("D"));
        message.Headers.Add("X-Auraly-Work-Session-Id", fixture.WorkSessionId.ToString("D"));
        return message;
    }
}
