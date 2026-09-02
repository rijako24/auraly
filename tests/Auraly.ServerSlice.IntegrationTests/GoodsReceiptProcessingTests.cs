using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Operational")]
public sealed class GoodsReceiptProcessingTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Shared_price_businesses_use_one_weighted_cost_pool_without_moving_other_stock()
    {
        var sharedBusinessId = Guid.NewGuid();
        var sharedWarehouseId = Guid.NewGuid();
        var sharedSupplierId = Guid.NewGuid();
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
            INSERT dbo.Suppliers(SupplierId,BusinessId,PartyId,Identification,Name,IsActive,CreatedAt)
            SELECT @OtherSupplierId,@OtherBusinessId,PartyId,Identification,Name,1,SYSUTCDATETIME()
            FROM dbo.Suppliers WHERE SupplierId=@SupplierId;
            INSERT dbo.SupplierProducts(SupplierProductId,BusinessId,ProductId,SupplierId,SupplierProductCode,IsPrimary,IsActive,CreatedAt)
            VALUES(NEWID(),@OtherBusinessId,@ProductId,@OtherSupplierId,N'PROV-SHARED',1,1,SYSUTCDATETIME());
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

            Assert.Equal(-6m, await ReadNullableDecimalAsync("QuantityOnHand"));
            Assert.Equal(6_000m, await ReadNullableDecimalAsync("AverageUnitCost"));
            Assert.Equal(-36_000m, await ReadNullableDecimalAsync("InventoryValue"));
        }
        finally
        {
            await SetPrimaryCostStateAsync(
                previousQuantity, previousAverage, previousValue,
                previousShares, previousCostBasis);
        }
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
            PurchasingPermissionCodes.ConfirmGoodsReceipts);

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
        var request = CreateRequest() with
        {
            DocumentId = Guid.NewGuid(),
            SupplierInvoiceNumber = null,
            PurchaseEvidenceType = PurchaseEvidenceTypes.BuyerElectronicSupportDocument
        };
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        using var message = CreateMessage(request, $"support-document-{request.DocumentId:N}");
        using var response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        Assert.Equal("Processed", await ScalarAsync<string>(
            "SELECT Status FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", request.DocumentId));
        Assert.Equal(1, await CountAsync("InventoryMovements", request.DocumentId));
        Assert.Equal("SupportDocument", await ScalarAsync<string>(
            "SELECT FiscalDocumentType FROM dbo.FiscalDocuments WHERE DocumentId=@Id", request.DocumentId));
        Assert.Equal("CUDS", await ScalarAsync<string>(
            "SELECT UniqueCodeType FROM dbo.FiscalDocuments WHERE DocumentId=@Id", request.DocumentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM fiscal.PurchaseSupportFiscalSnapshots WHERE DocumentId=@Id", request.DocumentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@Id", request.DocumentId));
        Assert.NotNull(await ScalarAsync<string>(
            "SELECT SupportFiscalNumber FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", request.DocumentId));
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
            DueDate = invalidDueDate.SupplierInvoiceDate!.Value.AddDays(31)
        };
        using var invalidDueDateMessage = CreateMessage(
            invalidDueDate, $"invalid-due-date-{Guid.NewGuid():N}");
        using var invalidDueDateResponse = await allowed.SendAsync(invalidDueDateMessage);
        Assert.Equal(HttpStatusCode.BadRequest, invalidDueDateResponse.StatusCode);
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

            UPDATE party SET IdentificationCountryId=@CountryId,IdentificationTypeCode=N'31',
              Identification=N'900999001',NormalizedIdentification=N'900999001',VerificationDigit=N'1',
              LegalName=N'PROVEEDOR DOCUMENTO SOPORTE',DisplayName=N'PROVEEDOR DOCUMENTO SOPORTE',CompletionStatus=N'Complete'
            FROM dbo.Parties party INNER JOIN dbo.Suppliers supplier ON supplier.PartyId=party.PartyId
            WHERE supplier.SupplierId=@SupplierId;
            IF NOT EXISTS(SELECT 1 FROM dbo.PartySites WHERE PartyId=@PartyId AND IsPrimary=1 AND IsActive=1)
              INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,
                AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
              VALUES(NEWID(),@PartyId,N'PRINCIPAL',N'Sede principal',@CountryId,@DivisionId,@CityId,
                N'Carrera 1 # 2-3',1,1,@UserId,SYSDATETIMEOFFSET());

            INSERT dbo.FiscalAuthorizations(FiscalAuthorizationId,BusinessId,AuthorizationNumber,SupplierTaxId,
              Environment,QrValidationUrl,TechnicalKeyVersion,ValidFrom,ValidUntil,AuthorizedRangeStart,
              AuthorizedRangeEnd,IsActive,CreatedAt)
            VALUES(@AuthorizationId,@BusinessId,@AuthorizationNumber,@IssuerTaxId,2,
              N'https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=',N'1',
              '2026-01-01','2028-12-31',1,999,1,SYSDATETIMEOFFSET());
            INSERT dbo.FiscalSeries(SeriesId,BusinessId,DeviceId,EmitterKind,FiscalAuthorizationId,
              DocumentType,Prefix,RangeStart,RangeEnd,IsActive,CreatedAt)
            VALUES(@SeriesId,@BusinessId,NULL,N'Server',@AuthorizationId,N'SupportDocument',N'DS',1,999,1,SYSDATETIMEOFFSET());
            """;
        var authorizationId = Guid.NewGuid();
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@SupplierId", fixture.SupplierId);
        command.Parameters.AddWithValue("@PartyId", fixture.SupplierPartyId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        command.Parameters.AddWithValue("@AuthorizationId", authorizationId);
        command.Parameters.AddWithValue("@SeriesId", Guid.NewGuid());
        command.Parameters.AddWithValue("@AuthorizationNumber", $"SUP-{authorizationId:N}");
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
            UPDATE dbo.InventoryBalances
            SET QuantityOnHand=@Quantity,AverageUnitCost=@Average,InventoryValue=@Value,
                UpdatedAt=SYSUTCDATETIME()
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;
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
}
