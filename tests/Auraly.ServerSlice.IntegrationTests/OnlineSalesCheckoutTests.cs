using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Xml.Linq;
using Auraly.Application.Fiscal;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Fiscal.Ubl;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed partial class OnlineSalesCheckoutTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Sales_checkout_rejects_the_retired_habilitation_property()
    {
        var userId = await CreateUserAsync("retired-habilitation-property");
        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate);
        var captured = await CaptureAsync(client, await OpenAsync(client));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{captured.DraftId:D}/complete")
        {
            Content = JsonContent.Create(new
            {
                expectedVersion = captured.Version,
                payments = new[] { new { methodCode = "Cash", amount = captured.PayableAmount } },
                documentType = PosSaleDocumentTypes.Invoice,
                fiscalHabilitationOnly = true
            })
        };
        request.Headers.Add("Idempotency-Key", $"retired-habilitation-{Guid.NewGuid():N}");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_maps_a_named_natural_person_to_the_required_dian_identification()
    {
        var userId = await CreateUserAsync("natural-person-dian");
        var (customerId, partySiteId) = await CreateNaturalPersonCustomerAsync(userId);
        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate);

        var draft = await OpenAsync(client);
        using (var select = new HttpRequestMessage(
                   HttpMethod.Put,
                   $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/customer")
               {
                   Content = JsonContent.Create(
                       new SelectOnlineSalesDraftCustomerRequest(
                           customerId, draft.Version, partySiteId))
               })
        {
            select.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var selectedResponse = await client.SendAsync(select);
            selectedResponse.EnsureSuccessStatusCode();
            var selection = await selectedResponse.Content
                .ReadFromJsonAsync<OnlineSalesCustomerSelection>();
            Assert.NotNull(selection);
            draft = selection.Draft;
        }

        var captured = await CaptureAsync(client, draft);
        var completed = await CompleteAsync(
            client,
            captured.DraftId,
            new CompleteOnlineSalesDraftRequest(
                captured.Version,
                [new OnlineSalesPayment("Cash", captured.PayableAmount, null)]),
            $"natural-person-dian-{Guid.NewGuid():N}");

        using (var scope = fixture.CreateScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<FiscalGenerationWorker>();
            Assert.True(await worker.ProcessAsync(
                fixture.BusinessId,
                completed.Receipt.DocumentId,
                $"natural-person-dian-{Guid.NewGuid():N}"));
        }

        var unsigned = await ReadFiscalArtifactAsync(
            completed.Receipt.DocumentId,
            FiscalArtifactTypeCodes.UnsignedXml);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(unsigned));
        var customer = xml.Descendants(
            DianUblNamespaces.Cac + "AccountingCustomerParty").Single();
        Assert.Equal("2", customer.Element(
            DianUblNamespaces.Cbc + "AdditionalAccountID")?.Value);
        var identification = customer
            .Descendants(DianUblNamespaces.Cac + "PartyIdentification")
            .Elements(DianUblNamespaces.Cbc + "ID").Single();
        Assert.Equal("1065648633", identification.Value);
        Assert.Equal("13", identification.Attribute("schemeName")?.Value);
        Assert.Null(identification.Attribute("schemeID"));
        Assert.True(new DianSchemaValidator().Validate(unsigned).IsValid);
    }

    [Fact]
    public async Task Online_checkout_uses_the_server_series_and_processes_once()
    {
        var userId = await CreateUserAsync("checkout");
        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate);
        client.Timeout = TimeSpan.FromSeconds(60);

        var draft = await OpenAsync(client);
        var captured = await CaptureAsync(client, draft);
        var tenderedAmount = captured.PayableAmount + 5_000m;
        var command = new CompleteOnlineSalesDraftRequest(
            captured.Version,
            [new OnlineSalesPayment(
                "Cash",
                captured.PayableAmount,
                null,
                TenderedAmount: tenderedAmount)]);
        var key = $"checkout-{Guid.NewGuid():N}";

        var completed = await CompleteAsync(
            client,
            captured.DraftId,
            command,
            key);

        Assert.False(completed.IsDuplicate);
        Assert.Equal(captured.WorkSessionId, completed.NextDraft.WorkSessionId);
        Assert.NotEqual(captured.DraftId, completed.NextDraft.DraftId);
        Assert.Empty(completed.NextDraft.Lines);
        Assert.Equal("Active", completed.NextDraft.Status);
        Assert.StartsWith("VTA00-", completed.Receipt.DocumentNumber);
        Assert.StartsWith(ServerSliceFixture.Prefix, completed.Receipt.FiscalNumber);
        Assert.False(string.IsNullOrWhiteSpace(completed.Receipt.Cufe));
        Assert.False(string.IsNullOrWhiteSpace(completed.Receipt.QrPayload));
        Assert.Equal(captured.PayableAmount, completed.Receipt.PayableAmount);

        var persisted = await ReadPersistenceAsync(completed.Receipt.DocumentId);
        Assert.Null(persisted.DeviceId);
        Assert.NotNull(persisted.WorkSessionId);
        Assert.Equal(SaleSourceModes.Online, persisted.SourceMode);
        Assert.Equal(userId, persisted.SoldByUserId);
        Assert.NotNull(persisted.WorkSessionId);
        Assert.Equal(1, persisted.DocumentCount);
        Assert.Equal(1, persisted.LineCount);
        Assert.Equal(1, persisted.PaymentCount);
        Assert.Equal(1, persisted.InventoryMovementCount);
        Assert.Equal(1, persisted.WorkSessionMovementCount);
        Assert.Equal(1, persisted.ServerOutboxCount);
        Assert.Equal(1, persisted.ProcessingJobCount);
        Assert.Equal("Completed", persisted.CheckoutStatus);
        Assert.Equal("Consumed", persisted.DraftStatus);

        await using (var paymentConnection = new SqlConnection(fixture.ConnectionString))
        {
            await paymentConnection.OpenAsync();
            await using var paymentCommand = new SqlCommand(
                "SELECT TenderedAmount FROM dbo.SalesPayments WHERE DocumentId=@DocumentId;",
                paymentConnection);
            paymentCommand.Parameters.AddWithValue("@DocumentId", completed.Receipt.DocumentId);
            Assert.Equal(tenderedAmount, (decimal)(await paymentCommand.ExecuteScalarAsync())!);
        }

        var context = new OnlineSalesDraftContext(
            fixture.BusinessId,
            fixture.WarehouseId,
            captured.WorkSessionId);
        using (var searchResponse = await client.PostAsJsonAsync(
                   "/api/commerce/v1/pos/drafts/sales/search",
                   new SearchOnlineSalesIssuedSalesRequest(
                       context,
                       completed.Receipt.DocumentNumber,
                       0,
                       10,
                       From: DateOnly.FromDateTime(completed.Receipt.IssuedAt.DateTime),
                       To: DateOnly.FromDateTime(completed.Receipt.IssuedAt.DateTime),
                       ProductId: captured.Lines[0].ProductId,
                       MinimumTotal: completed.Receipt.PayableAmount,
                       MaximumTotal: completed.Receipt.PayableAmount)))
        {
            searchResponse.EnsureSuccessStatusCode();
            var page = await searchResponse.Content
                .ReadFromJsonAsync<OnlineSalesIssuedSalePage>();
            Assert.NotNull(page);
            var issued = Assert.Single(page.Items);
            Assert.Equal(completed.Receipt.DocumentId, issued.DocumentId);
            Assert.Equal(completed.Receipt.DocumentNumber, issued.DocumentNumber);
            Assert.Equal(completed.Receipt.FiscalNumber, issued.FiscalNumber);
            Assert.Equal(completed.Receipt.CustomerName, issued.CustomerName);
        }
        using (var receiptResponse = await client.PostAsJsonAsync(
                   $"/api/commerce/v1/pos/drafts/sales/{completed.Receipt.DocumentId:D}/receipt",
                   context))
        {
            receiptResponse.EnsureSuccessStatusCode();
            var printable = await receiptResponse.Content
                .ReadFromJsonAsync<OnlineSalesReceipt>();
            Assert.NotNull(printable);
            Assert.Equal(completed.Receipt.DocumentId, printable.DocumentId);
            Assert.Equal(completed.Receipt.DocumentNumber, printable.DocumentNumber);
            Assert.Equal(completed.Receipt.FiscalNumber, printable.FiscalNumber);
            Assert.Equal(completed.Receipt.Cufe, printable.Cufe);
            Assert.Equal(completed.Receipt.QrPayload, printable.QrPayload);
            Assert.Equal(completed.Receipt.CustomerName, printable.CustomerName);
            var printableLine = Assert.Single(printable.Lines);
            Assert.Equal(captured.Lines[0].TaxCode, printableLine.TaxCode);
            Assert.Equal(captured.Lines[0].TaxRate, printableLine.TaxRate);
            Assert.Equal(captured.TaxAmount, printable.TaxAmount);
            Assert.Equal(captured.TaxAmount, printableLine.Tax);
            var printablePayment = Assert.Single(printable.Payments);
            Assert.Equal(captured.PayableAmount, printablePayment.Amount);
            Assert.Equal(tenderedAmount, printablePayment.TenderedAmount);
        }
        var qrUrl =
            $"/api/commerce/v1/pos/drafts/sales/{completed.Receipt.DocumentId:D}/qr" +
            $"?businessId={fixture.BusinessId:D}" +
            $"&warehouseId={fixture.WarehouseId:D}" +
            $"&workSessionId={captured.WorkSessionId:D}";
        using (var qrResponse = await client.GetAsync(qrUrl))
        {
            qrResponse.EnsureSuccessStatusCode();
            Assert.Equal("image/svg+xml", qrResponse.Content.Headers.ContentType?.MediaType);
            var svg = await qrResponse.Content.ReadAsStringAsync();
            Assert.Contains("<svg", svg, StringComparison.Ordinal);
            Assert.DoesNotContain(completed.Receipt.Cufe, svg, StringComparison.Ordinal);
        }
        using (var wrongRegisterResponse = await client.GetAsync(
                   $"/api/commerce/v1/pos/drafts/sales/{completed.Receipt.DocumentId:D}/qr" +
                   $"?businessId={fixture.BusinessId:D}" +
                   $"&warehouseId={fixture.WarehouseId:D}" +
                   $"&workSessionId={Guid.NewGuid():D}"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, wrongRegisterResponse.StatusCode);
        }

        var replay = await CompleteAsync(
            client,
            captured.DraftId,
            command,
            key);
        Assert.True(replay.IsDuplicate);
        Assert.Equal(completed.Receipt.DocumentId, replay.Receipt.DocumentId);
        Assert.Equal(completed.Receipt.DocumentNumber, replay.Receipt.DocumentNumber);
        Assert.Equal(completed.Receipt.FiscalNumber, replay.Receipt.FiscalNumber);
        Assert.Equal(completed.Receipt.Cufe, replay.Receipt.Cufe);
        Assert.Equal(completed.NextDraft.DraftId, replay.NextDraft.DraftId);

        var afterReplay = await ReadPersistenceAsync(completed.Receipt.DocumentId);
        Assert.Equal(1, afterReplay.DocumentCount);
        Assert.Equal(1, afterReplay.LineCount);
        Assert.Equal(1, afterReplay.PaymentCount);
        Assert.Equal(1, afterReplay.InventoryMovementCount);
        Assert.Equal(1, afterReplay.WorkSessionMovementCount);
        Assert.Equal(1, afterReplay.ServerOutboxCount);
        Assert.Equal(1, afterReplay.ProcessingJobCount);

        using var conflictRequest = Mutation(
            captured.DraftId,
            command with
            {
                Payments =
                [
                    new OnlineSalesPayment(
                        "Transfer",
                        captured.PayableAmount,
                        "DIFFERENT")
                ]
            },
            key);
        using var conflict = await client.SendAsync(conflictRequest);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Issued_sales_search_does_not_require_rehydrating_a_historical_payload()
    {
        var userId = await CreateUserAsync("historical-issued-sale");
        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate);

        var captured = await CaptureAsync(client, await OpenAsync(client));
        var completed = await CompleteAsync(
            client,
            captured.DraftId,
            new CompleteOnlineSalesDraftRequest(
                captured.Version,
                [new OnlineSalesPayment("Cash", captured.PayableAmount, null)]),
            $"historical-issued-sale-{Guid.NewGuid():N}");

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                UPDATE dbo.DocumentProcessingPayloads
                SET PayloadJson=JSON_MODIFY(PayloadJson,'$.commercialSnapshot',NULL)
                WHERE DocumentId=@DocumentId;
                """, connection);
            command.Parameters.AddWithValue("@DocumentId", completed.Receipt.DocumentId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        var page = await SearchAsync(
            client,
            captured.WorkSessionId,
            completed.Receipt.DocumentNumber);
        var issued = Assert.Single(page.Items);
        Assert.Equal(completed.Receipt.DocumentId, issued.DocumentId);
        Assert.Equal(completed.Receipt.DocumentType, issued.DocumentType);
        Assert.Equal(completed.Receipt.CustomerName, issued.CustomerName);
    }

    [Fact]
    public async Task Promotion_discount_remains_separate_after_checkout_without_manual_discount_permission()
    {
        var promotionId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.Promotions(PromotionId,TenantId,Name,IsActive,Priority,IsCombinable,CreatedAt)
            VALUES(@PromotionId,@TenantId,N'Diez por ciento',1,100,0,SYSUTCDATETIME());
            INSERT pricing.PromotionBusinessScopes(PromotionId,BusinessId,TenantId)
            VALUES(@PromotionId,@BusinessId,@TenantId);
            INSERT dbo.PromotionBenefits(
              PromotionId,TenantId,BenefitType,TargetItemType,ProductId,DiscountPercentage,CreatedAt)
            VALUES(@PromotionId,@TenantId,0,1,@ProductId,10,SYSUTCDATETIME());
            """,
            new("@PromotionId", promotionId), new("@TenantId", fixture.TenantId),
            new("@BusinessId", fixture.BusinessId), new("@ProductId", fixture.ProductId));
        try
        {
            var userId = await CreateUserAsync("promotion-snapshot");
            using var client = fixture.CreateUserClient(
                userId, CommercePermissionCodes.SalesCreate);
            var captured = await CaptureAsync(client, await OpenAsync(client));
            var completed = await CompleteAsync(
                client,
                captured.DraftId,
                new CompleteOnlineSalesDraftRequest(
                    captured.Version,
                    [new OnlineSalesPayment("Cash", captured.PayableAmount, null)]),
                $"promotion-snapshot-{Guid.NewGuid():N}");

            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "SELECT DiscountAmount,PromotionDiscountAmount FROM dbo.SalesDocumentLines WHERE DocumentId=@DocumentId;",
                connection);
            command.Parameters.AddWithValue("@DocumentId", completed.Receipt.DocumentId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetDecimal(1) > 0);
            Assert.Equal(reader.GetDecimal(0), reader.GetDecimal(1));
        }
        finally
        {
            await ExecuteAsync(
                "DELETE dbo.Promotions WHERE PromotionId=@PromotionId;",
                new SqlParameter("@PromotionId", promotionId));
        }
    }

    [Fact]
    public async Task Cash_closure_traces_only_the_cash_share_of_a_split_payment_sale()
    {
        var userId = await CreateUserAsync("split-cash-trace");
        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate,
            WorkSessionPermissionCodes.Close,
            WorkSessionPermissionCodes.ReadCashDifferences);
        client.Timeout = TimeSpan.FromSeconds(60);

        var captured = await CaptureAsync(client, await OpenAsync(client));
        var bankAccountId = await EnsureTransferBankAccountAsync();
        var cashAmount = decimal.Round(captured.PayableAmount / 3m, 2);
        var transferAmount = captured.PayableAmount - cashAmount;
        var completed = await CompleteAsync(
            client,
            captured.DraftId,
            new CompleteOnlineSalesDraftRequest(captured.Version,
            [
                new OnlineSalesPayment("Cash", cashAmount, null),
                new OnlineSalesPayment("Transfer", transferAmount, "TRANSFER-SPLIT",
                    BankAccountId: bankAccountId)
            ]),
            $"split-cash-trace-{Guid.NewGuid():N}");

        using var closeRequest = new HttpRequestMessage(HttpMethod.Post,
            $"/api/commerce/v1/work-sessions/{captured.WorkSessionId:D}/close")
        {
            Content = JsonContent.Create(new CloseWorkSessionRequest(cashAmount,
                "Venta con pago dividido", PaymentCounts:
                [
                    new WorkSessionPaymentCount("Cash", cashAmount),
                    new WorkSessionPaymentCount("Card", 0m),
                    new WorkSessionPaymentCount("Transfer", transferAmount)
                ]))
        };
        closeRequest.Headers.Add("Idempotency-Key", $"split-close-{Guid.NewGuid():N}");
        using var closeResponse = await client.SendAsync(closeRequest);
        closeResponse.EnsureSuccessStatusCode();
        var closure = await closeResponse.Content.ReadFromJsonAsync<WorkSessionClosureView>();
        Assert.NotNull(closure);

        var verificationItems = await client.GetFromJsonAsync<WorkSessionPaymentVerificationItem[]>(
            $"/api/commerce/v1/work-sessions/closures/{closure.WorkSessionClosureId:D}/payment-verifications");
        Assert.NotNull(verificationItems);
        var cashTrace = Assert.Single(verificationItems, item =>
            item.SourceId == completed.Receipt.DocumentId && item.PaymentMethodCode == "Cash");
        var transferTrace = Assert.Single(verificationItems, item =>
            item.SourceId == completed.Receipt.DocumentId && item.PaymentMethodCode == "Transfer");
        Assert.Equal(cashAmount, cashTrace.Amount);
        Assert.Equal(transferAmount, transferTrace.Amount);
        Assert.NotEqual(completed.Receipt.PayableAmount, cashTrace.Amount);
        Assert.Equal(completed.Receipt.DocumentNumber, cashTrace.DocumentNumber);
        Assert.Equal(completed.Receipt.DocumentNumber, transferTrace.DocumentNumber);
    }

    [Fact]
    public async Task Commercial_receipt_uses_its_own_series_and_skips_the_fiscal_stage()
    {
        var userId = await CreateUserAsync("receipt");
        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate);
        client.Timeout = TimeSpan.FromSeconds(60);

        var captured = await CaptureAsync(client, await OpenAsync(client));
        var command = new CompleteOnlineSalesDraftRequest(
            captured.Version,
            [new OnlineSalesPayment("Cash", captured.PayableAmount, null)],
            DocumentType: PosSaleDocumentTypes.Receipt);
        var key = $"receipt-{Guid.NewGuid():N}";

        var completed = await CompleteAsync(client, captured.DraftId, command, key);

        Assert.Equal(PosSaleDocumentTypes.Receipt, completed.Receipt.DocumentType);
        Assert.StartsWith("CVI00-", completed.Receipt.DocumentNumber);
        Assert.Null(completed.Receipt.FiscalNumber);
        Assert.Null(completed.Receipt.Cufe);
        Assert.Null(completed.Receipt.QrPayload);
        Assert.Equal("CommercialAccepted", completed.Receipt.FiscalStatus);
        Assert.Equal(captured.PayableAmount, completed.Receipt.PayableAmount);

        var search = await SearchAsync(
            client,
            captured.WorkSessionId,
            completed.Receipt.DocumentNumber);
        var issuedReceipt = Assert.Single(search.Items);
        Assert.Equal(completed.Receipt.DocumentId, issuedReceipt.DocumentId);
        Assert.Equal(PosSaleDocumentTypes.Receipt, issuedReceipt.DocumentType);
        Assert.Equal(completed.Receipt.DocumentNumber, issuedReceipt.DocumentNumber);
        Assert.Null(issuedReceipt.FiscalNumber);
        Assert.Null(issuedReceipt.FiscalStatus);

        var context = new OnlineSalesDraftContext(
            fixture.BusinessId,
            fixture.WarehouseId,
            captured.WorkSessionId);
        using (var receiptResponse = await client.PostAsJsonAsync(
                   $"/api/commerce/v1/pos/drafts/sales/{completed.Receipt.DocumentId:D}/receipt",
                   context))
        {
            receiptResponse.EnsureSuccessStatusCode();
            var printable = await receiptResponse.Content
                .ReadFromJsonAsync<OnlineSalesReceipt>();
            Assert.NotNull(printable);
            Assert.Equal(completed.Receipt.DocumentId, printable.DocumentId);
            Assert.Equal(PosSaleDocumentTypes.Receipt, printable.DocumentType);
            Assert.Equal(completed.Receipt.DocumentNumber, printable.DocumentNumber);
            Assert.Null(printable.FiscalNumber);
            Assert.Null(printable.Cufe);
            Assert.Null(printable.QrPayload);
        }

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var sql = connection.CreateCommand();
            sql.CommandText = """
                SELECT d.DocumentType,d.FiscalSeriesId,d.FiscalNumber,d.CufeReceived,d.FiscalStatus,
                       (SELECT COUNT(*) FROM dbo.FiscalSnapshots f WHERE f.DocumentId=d.DocumentId),
                       (SELECT COUNT(*) FROM dbo.FiscalDocuments f WHERE f.DocumentId=d.DocumentId),
                       (SELECT COUNT(*) FROM dbo.FiscalDocumentProcesses f WHERE f.DocumentId=d.DocumentId),
                       (SELECT COUNT(*) FROM dbo.InventoryMovements m WHERE m.DocumentId=d.DocumentId),
                       (SELECT COUNT(*) FROM dbo.SalesPayments p WHERE p.DocumentId=d.DocumentId),
                       (SELECT COUNT(*) FROM dbo.ServerOutboxMessages o WHERE o.DocumentId=d.DocumentId),
                       (SELECT COUNT(*) FROM dbo.Receivables r WHERE r.SourceDocumentId=d.DocumentId),
                       (SELECT COUNT(*) FROM dbo.AccountingPostingJobs a WHERE a.SourceDocumentId=d.DocumentId)
                FROM dbo.SalesDocuments d WHERE d.DocumentId=@DocumentId;
                """;
            sql.Parameters.AddWithValue("@DocumentId", completed.Receipt.DocumentId);
            await using var reader = await sql.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(PosSaleDocumentTypes.Receipt, reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(reader.IsDBNull(2));
            Assert.True(reader.IsDBNull(3));
            Assert.True(reader.IsDBNull(4));
            Assert.Equal(0, reader.GetInt32(5));
            Assert.Equal(0, reader.GetInt32(6));
            Assert.Equal(0, reader.GetInt32(7));
            Assert.Equal(1, reader.GetInt32(8));
            Assert.Equal(1, reader.GetInt32(9));
            Assert.Equal(1, reader.GetInt32(10));
            Assert.Equal(0, reader.GetInt32(11));
            Assert.Equal(1, reader.GetInt32(12));
        }

        var replay = await CompleteAsync(client, captured.DraftId, command, key);
        Assert.True(replay.IsDuplicate);
        Assert.Equal(completed.Receipt.DocumentId, replay.Receipt.DocumentId);
        Assert.Equal(completed.Receipt.DocumentNumber, replay.Receipt.DocumentNumber);

        await using var verify = new SqlConnection(fixture.ConnectionString);
        await verify.OpenAsync();
        await using var count = verify.CreateCommand();
        count.CommandText = """
            SELECT COUNT(*),
                   (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@DocumentId),
                   (SELECT COUNT(*) FROM dbo.SalesPayments WHERE DocumentId=@DocumentId)
            FROM dbo.SalesDocuments WHERE DocumentId=@DocumentId;
            """;
        count.Parameters.AddWithValue("@DocumentId", completed.Receipt.DocumentId);
        await using var counts = await count.ExecuteReaderAsync();
        Assert.True(await counts.ReadAsync());
        Assert.Equal(1, counts.GetInt32(0));
        Assert.Equal(1, counts.GetInt32(1));
        Assert.Equal(1, counts.GetInt32(2));
    }

    [Fact]
    public async Task Missing_fiscal_configuration_blocks_invoice_but_allows_receipt()
    {
        var userId = await CreateUserAsync("no-fiscal");
        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate);
        client.Timeout = TimeSpan.FromSeconds(60);
        var captured = await CaptureAsync(client, await OpenAsync(client));
        var payment = new OnlineSalesPayment("Cash", captured.PayableAmount, null);

        await ExecuteAsync(
            """
            UPDATE dbo.FiscalAuthorizations SET IsActive=0
            WHERE FiscalAuthorizationId=@FiscalAuthorizationId;
            UPDATE dbo.FiscalIssuerConfigurations SET IsActive=0
            WHERE FiscalIssuerConfigurationId=@FiscalIssuerConfigurationId;
            """,
            new("@FiscalAuthorizationId", fixture.FiscalAuthorizationId),
            new("@FiscalIssuerConfigurationId", fixture.FiscalIssuerConfigurationId));
        try
        {
            using var invoiceRequest = Mutation(
                captured.DraftId,
                new CompleteOnlineSalesDraftRequest(
                    captured.Version, [payment], DocumentType: PosSaleDocumentTypes.Invoice),
                $"no-fiscal-invoice-{Guid.NewGuid():N}");
            using var invoiceResponse = await client.SendAsync(invoiceRequest);
            Assert.Equal(HttpStatusCode.BadRequest, invoiceResponse.StatusCode);
            Assert.Equal(0, await CountDocumentsForDraftAsync(captured.DraftId));

            var receipt = await CompleteAsync(
                client,
                captured.DraftId,
                new CompleteOnlineSalesDraftRequest(
                    captured.Version, [payment], DocumentType: PosSaleDocumentTypes.Receipt),
                $"no-fiscal-receipt-{Guid.NewGuid():N}");
            Assert.Equal(PosSaleDocumentTypes.Receipt, receipt.Receipt.DocumentType);
            Assert.Null(receipt.Receipt.FiscalNumber);
        }
        finally
        {
            await ExecuteAsync(
                """
                UPDATE dbo.FiscalAuthorizations SET IsActive=1
                WHERE FiscalAuthorizationId=@FiscalAuthorizationId;
                UPDATE dbo.FiscalIssuerConfigurations SET IsActive=1
                WHERE FiscalIssuerConfigurationId=@FiscalIssuerConfigurationId;
                """,
                new("@FiscalAuthorizationId", fixture.FiscalAuthorizationId),
                new("@FiscalIssuerConfigurationId", fixture.FiscalIssuerConfigurationId));
        }
    }

    [Fact]
    public async Task Customer_configured_for_electronic_invoicing_cannot_be_completed_as_a_commercial_receipt()
    {
        var userId = await CreateUserAsync("required-invoice");
        var (customerId, partySiteId) = await CreateElectronicInvoiceCustomerAsync(userId);
        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate);

        var draft = await OpenAsync(client);
        using (var select = new HttpRequestMessage(
                   HttpMethod.Put,
                   $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/customer")
               {
                   Content = JsonContent.Create(
                       new SelectOnlineSalesDraftCustomerRequest(
                           customerId, draft.Version, partySiteId))
               })
        {
            select.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var selectedResponse = await client.SendAsync(select);
            selectedResponse.EnsureSuccessStatusCode();
            var selection = await selectedResponse.Content
                .ReadFromJsonAsync<OnlineSalesCustomerSelection>();
            Assert.NotNull(selection);
            Assert.True(selection.Customer?.RequiresElectronicInvoice);
            draft = selection.Draft;
        }

        var captured = await CaptureAsync(client, draft);
        using var request = Mutation(
            captured.DraftId,
            new CompleteOnlineSalesDraftRequest(
                captured.Version,
                [new OnlineSalesPayment("Cash", captured.PayableAmount, null)],
                DocumentType: PosSaleDocumentTypes.Receipt),
            $"required-invoice-{Guid.NewGuid():N}");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("factura electronica", await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await CountCustomerDocumentsAsync(customerId));
    }



    [Fact]
    public async Task Customer_withholding_reduces_amount_to_collect_and_is_snapshotted()
    {
        var userId = await CreateUserAsync("sale-withholding");
        var (customerId, partySiteId) = await CreateCustomerAsync(
            userId, false, "Cliente con retefuente");
        using (var taxation = fixture.CreateAdminClient(
                   TaxationPermissionCodes.ViewWithholdingRules,
                   TaxationPermissionCodes.ManageWithholdingRules))
        {
            using var profile = await taxation.PutAsJsonAsync(
                $"/api/commerce/v1/taxation/counterparty-profiles/{customerId:D}",
                new SaveCounterpartyTaxProfileRequest(
                    fixture.BusinessId, customerId, true, ["O-23"], null));
            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
            using var rule = await taxation.PostAsJsonAsync(
                "/api/commerce/v1/taxation/withholding-rules",
                new SaveWithholdingRuleRequest(
                    fixture.BusinessId, ($"RF-VTA-{Guid.NewGuid():N}")[..15],
                    "Retefuente venta", WithholdingKinds.IncomeTax,
                    WithholdingDirections.Sale, WithholdingRecognitionMoments.Accrual,
                    WithholdingBaseKinds.TaxExclusiveAmount, null, null,
                    2.5m, 0m, ["O-23"], new DateOnly(2026, 1, 1), null, true));
            Assert.Equal(HttpStatusCode.Created, rule.StatusCode);
        }

        using var client = fixture.CreateUserClient(
            userId, CommercePermissionCodes.SalesCreate);
        var draft = await OpenAsync(client);
        using (var select = new HttpRequestMessage(
                   HttpMethod.Put, $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/customer")
               {
                   Content = JsonContent.Create(
                       new SelectOnlineSalesDraftCustomerRequest(
                           customerId, draft.Version, partySiteId))
               })
        {
            select.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var selected = await client.SendAsync(select);
            selected.EnsureSuccessStatusCode();
            draft = (await selected.Content.ReadFromJsonAsync<OnlineSalesCustomerSelection>())!.Draft;
        }
        var captured = await CaptureAsync(client, draft);
        var settlement = await client.GetFromJsonAsync<WithholdingCalculationSnapshot>(
            $"/api/commerce/v1/pos/drafts/{captured.DraftId:D}/settlement");
        Assert.NotNull(settlement);
        Assert.Equal(captured.PayableAmount, settlement.GrossAmount);
        Assert.Equal(captured.UntaxedAmount * 0.025m, settlement.WithholdingTotal);
        Assert.Equal(settlement.GrossAmount - settlement.WithholdingTotal, settlement.NetAmount);
        Assert.Single(settlement.Lines);

        var completed = await CompleteAsync(
            client, captured.DraftId,
            new CompleteOnlineSalesDraftRequest(
                captured.Version,
                [new OnlineSalesPayment("Cash", settlement.NetAmount, null)]),
            $"sale-withholding-{Guid.NewGuid():N}");

        Assert.Equal(PosSaleRemoteStatuses.FiscalVerified, completed.Receipt.FiscalStatus);
        Assert.Equal(settlement.WithholdingTotal, completed.Receipt.WithholdingTotal);
        Assert.Equal(settlement.NetAmount, completed.Receipt.NetPayableAmount);
        Assert.Single(completed.Receipt.Withholdings!);
        var persisted = await WaitForWithholdingSnapshotAsync(completed.Receipt.DocumentId);
        Assert.Equal(settlement.GrossAmount, persisted.Gross);
        Assert.Equal(settlement.WithholdingTotal, persisted.Withholding);
        Assert.Equal(settlement.NetAmount, persisted.Net);
    }

    [Fact]
    public async Task Two_online_users_share_server_series_without_number_collisions()
    {
        var firstUserId = await CreateUserAsync("parallel-a");
        var secondUserId = await CreateUserAsync("parallel-b");
        using var firstClient = fixture.CreateUserClient(
            firstUserId,
            CommercePermissionCodes.SalesCreate);
        using var secondClient = fixture.CreateUserClient(
            secondUserId,
            CommercePermissionCodes.SalesCreate);
        firstClient.Timeout = TimeSpan.FromSeconds(90);
        secondClient.Timeout = TimeSpan.FromSeconds(90);

        var firstDraft = await CaptureAsync(
            firstClient,
            await OpenAsync(firstClient));
        var secondDraft = await CaptureAsync(
            secondClient,
            await OpenAsync(secondClient));
        using var firstRequest = Mutation(
            firstDraft.DraftId,
            new CompleteOnlineSalesDraftRequest(
                firstDraft.Version,
                [new OnlineSalesPayment("Cash", firstDraft.PayableAmount, null)]),
            $"parallel-a-{Guid.NewGuid():N}");
        using var secondRequest = Mutation(
            secondDraft.DraftId,
            new CompleteOnlineSalesDraftRequest(
                secondDraft.Version,
                [new OnlineSalesPayment("Cash", secondDraft.PayableAmount, null)]),
            $"parallel-b-{Guid.NewGuid():N}");

        var responses = await Task.WhenAll(
            firstClient.SendAsync(firstRequest),
            secondClient.SendAsync(secondRequest));
        try
        {
            foreach (var response in responses)
                Assert.True(
                    response.IsSuccessStatusCode,
                    $"La emisión concurrente respondió {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            var first = await responses[0].Content
                .ReadFromJsonAsync<CompleteOnlineSalesDraftResponse>();
            var second = await responses[1].Content
                .ReadFromJsonAsync<CompleteOnlineSalesDraftResponse>();
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.NotEqual(first.Receipt.DocumentId, second.Receipt.DocumentId);
            Assert.NotEqual(first.Receipt.DocumentNumber, second.Receipt.DocumentNumber);
            Assert.NotEqual(first.Receipt.FiscalNumber, second.Receipt.FiscalNumber);
            Assert.NotEqual(first.Receipt.Cufe, second.Receipt.Cufe);

            var consecutives = await ReadConsecutivesAsync(
                first.Receipt.DocumentId,
                second.Receipt.DocumentId);
            Assert.Equal(2, consecutives.Count);
            Assert.All(
                consecutives,
                value => Assert.Null(value.DeviceId));
            Assert.Equal(
                2,
                consecutives.Select(value => value.DocumentConsecutive)
                    .Distinct().Count());
            Assert.Equal(
                2,
                consecutives.Select(value => value.FiscalConsecutive)
                    .Distinct().Count());
            Assert.Equal(
                2,
                consecutives.Select(value => value.SoldByUserId)
                    .Distinct().Count());
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task Unsupported_credit_does_not_reserve_numbers()
    {
        var userId = await CreateUserAsync("credit");
        using var client = fixture.CreateUserClient(
            userId,
            CommercePermissionCodes.SalesCreate);
        var captured = await CaptureAsync(client, await OpenAsync(client));
        var before = await ReadCursorValuesAsync();
        using var request = Mutation(
            captured.DraftId,
            new CompleteOnlineSalesDraftRequest(
                captured.Version,
                [new OnlineSalesPayment("Credit", captured.PayableAmount, null)]),
            $"credit-{Guid.NewGuid():N}");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await ReadCursorValuesAsync());
    }

    private async Task<Guid> CreateUserAsync(string prefix)
    {
        var userId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
              FirstName,LastName,IsActive,CreatedAt)
            VALUES(
              @UserId,@TenantId,@Username,UPPER(@Username),@Email,UPPER(@Email),
              N'Venta',N'Online',1,SYSDATETIMEOFFSET());
            INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
            VALUES(@UserRoleId,@UserId,@RoleId,@BusinessId,SYSDATETIMEOFFSET());


            """,
            new("@UserId", userId),
            new("@UserRoleId", Guid.NewGuid()),
            new("@TenantId", fixture.TenantId),
            new("@RoleId", fixture.RoleId),
            new("@BusinessId", fixture.BusinessId),
            new("@Username", $"{prefix}-{userId:N}"),
            new("@Email", $"{prefix}-{userId:N}@test.local"));
        return userId;
    }

    private Task<(Guid CustomerId, Guid PartySiteId)> CreateElectronicInvoiceCustomerAsync(Guid userId) =>
        CreateCustomerAsync(userId, true, "Cliente factura requerida");

    private async Task<(Guid CustomerId, Guid PartySiteId)> CreateNaturalPersonCustomerAsync(Guid userId)
    {
        var partyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var partySiteId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.Parties(
              PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,Identification,
              NormalizedIdentification,DisplayName,FirstName,LastName,
              CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES(
              @PartyId,@TenantId,N'NaturalPerson',
              (SELECT TOP (1) CountryId FROM dbo.Countries WHERE Code=N'CO'),N'CC',N'1065648633',
              N'1065648633',N'KEVIN RAMIREZ GRANADOS',N'KEVIN',N'RAMIREZ GRANADOS',
              N'Complete',1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.Customers(
              CustomerId,PartyId,BusinessId,RequiresElectronicInvoice,
              IsActive,CreatedBy,CreatedAt)
            VALUES(
              @CustomerId,@PartyId,@BusinessId,1,1,@UserId,SYSDATETIMEOFFSET());
            DECLARE @CountryId uniqueidentifier,@DivisionId uniqueidentifier,@CityId uniqueidentifier;
            SELECT TOP(1) @CountryId=country.CountryId,
                          @DivisionId=division.AdministrativeDivisionId,
                          @CityId=city.CityId
            FROM dbo.Countries country
            JOIN dbo.AdministrativeDivisions division ON division.CountryId=country.CountryId
            JOIN dbo.Cities city ON city.AdministrativeDivisionId=division.AdministrativeDivisionId
            WHERE country.IsActive=1 AND division.IsActive=1 AND city.IsActive=1;
            INSERT dbo.PartySites(
              PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,
              AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
            VALUES(@PartySiteId,@PartyId,N'PRINCIPAL',N'Sede principal',@CountryId,
              @DivisionId,@CityId,N'Dirección principal',1,1,@UserId,SYSDATETIMEOFFSET());
            """,
            new("@PartyId", partyId),
            new("@CustomerId", customerId),
            new("@PartySiteId", partySiteId),
            new("@TenantId", fixture.TenantId),
            new("@BusinessId", fixture.BusinessId),
            new("@UserId", userId));
        return (customerId, partySiteId);
    }

    private async Task<(Guid CustomerId, Guid PartySiteId)> CreateCustomerAsync(
        Guid userId,
        bool requiresElectronicInvoice,
        string name)
    {
        var partyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var partySiteId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.Parties(
              PartyId,TenantId,PartyType,IdentificationCountryId,
              IdentificationTypeCode,Identification,NormalizedIdentification,
              DisplayName,LegalName,
              CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES(
              @PartyId,@TenantId,N'Organization',
              (SELECT TOP (1) CountryId FROM dbo.Countries WHERE Code=N'CO'),
              N'31',@Identification,@Identification,@Name,
              @Name,N'Complete',1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.Customers(
              CustomerId,PartyId,BusinessId,RequiresElectronicInvoice,
              IsActive,CreatedBy,CreatedAt)
            VALUES(
              @CustomerId,@PartyId,@BusinessId,@RequiresElectronicInvoice,1,@UserId,SYSDATETIMEOFFSET());
            DECLARE @CountryId uniqueidentifier,@DivisionId uniqueidentifier,@CityId uniqueidentifier;
            SELECT TOP(1) @CountryId=country.CountryId,
                          @DivisionId=division.AdministrativeDivisionId,
                          @CityId=city.CityId
            FROM dbo.Countries country
            JOIN dbo.AdministrativeDivisions division ON division.CountryId=country.CountryId
            JOIN dbo.Cities city ON city.AdministrativeDivisionId=division.AdministrativeDivisionId
            WHERE country.IsActive=1 AND division.IsActive=1 AND city.IsActive=1;
            INSERT dbo.PartySites(
              PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,
              AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
            VALUES(@PartySiteId,@PartyId,N'PRINCIPAL',N'Sede principal',@CountryId,
              @DivisionId,@CityId,N'Dirección principal',1,1,@UserId,SYSDATETIMEOFFSET());
            """,
            new("@PartyId", partyId),
            new("@CustomerId", customerId),
            new("@PartySiteId", partySiteId),
            new("@TenantId", fixture.TenantId),
            new("@BusinessId", fixture.BusinessId),
            new("@UserId", userId),
            new("@Identification", ServerSliceFixture.UniqueNit(customerId)),
            new("@Name", name),
            new("@RequiresElectronicInvoice", requiresElectronicInvoice));
        return (customerId, partySiteId);
    }

    private async Task<int> CountCustomerDocumentsAsync(Guid customerId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.SalesDocuments WHERE CustomerId=@CustomerId;",
            connection);
        command.Parameters.AddWithValue("@CustomerId", customerId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<(decimal Gross, decimal Withholding, decimal Net)>
        WaitForWithholdingSnapshotAsync(Guid documentId)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(10);
        string? processingState = null;
        do
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                """
                SELECT GrossAmount,WithholdingTotal,NetAmount
                FROM dbo.DocumentWithholdingSnapshots WHERE DocumentId=@DocumentId;
                SELECT d.ProcessingStatus,COALESCE(s.ConflictReason,j.LastError)
                FROM dbo.SalesDocuments d
                LEFT JOIN dbo.FiscalSnapshots s ON s.DocumentId=d.DocumentId
                LEFT JOIN dbo.DocumentProcessingJobs j ON j.DocumentId=d.DocumentId
                WHERE d.DocumentId=@DocumentId;
                """, connection);
            command.Parameters.AddWithValue("@DocumentId", documentId);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return (reader.GetDecimal(0), reader.GetDecimal(1), reader.GetDecimal(2));
            await reader.NextResultAsync();
            if (await reader.ReadAsync())
            {
                var status = reader.GetString(0);
                var lastError = reader.IsDBNull(1) ? null : reader.GetString(1);
                processingState = lastError is null
                    ? $"Estado del procesamiento: {status}."
                    : $"Estado del procesamiento: {status}. Error: {lastError}";
                if (status is "NeedsIntervention" or "DeadLettered") break;
            }
            await Task.Delay(50);
        } while (DateTimeOffset.UtcNow < expiresAt);

        throw new Xunit.Sdk.XunitException(
            $"La venta no persistió su snapshot de retenciones. " +
            (processingState ?? "No se almacenó la recepción de la venta en 10 segundos."));
    }

    private async Task<int> CountDocumentsForDraftAsync(Guid draftId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM dbo.OnlineSalesCheckoutReceipts receipt
            JOIN dbo.SalesDocuments document ON document.DocumentId=receipt.DocumentId
            WHERE receipt.SalesDraftId=@DraftId;
            """,
            connection);
        command.Parameters.AddWithValue("@DraftId", draftId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<OnlineSalesDraft> OpenAsync(HttpClient client)
    {
        using var workSessionResponse = await client.PostAsJsonAsync(
            "/api/commerce/v1/work-sessions/current",
            new OpenWorkSessionRequest(
                fixture.BusinessId,
                fixture.WarehouseId,
                null));
        workSessionResponse.EnsureSuccessStatusCode();
        var workSession = await workSessionResponse.Content
            .ReadFromJsonAsync<WorkSessionView>()
            ?? throw new InvalidOperationException("Empty work session response.");

        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/drafts/active",
            new OpenOnlineSalesDraftRequest(new(
                fixture.BusinessId,
                fixture.WarehouseId,
                workSession.WorkSessionId)));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("Empty draft response.");
    }

    private async Task<OnlineSalesDraft> CaptureAsync(
        HttpClient client,
        OnlineSalesDraft draft)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId:D}/items")
        {
            Content = JsonContent.Create(
                new AddOnlineSalesDraftItemRequest("P-E2E",
                    1m,
                    draft.Version))
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OnlineSalesDraft>()
            ?? throw new InvalidOperationException("Empty capture response.");
    }

    private static async Task<CompleteOnlineSalesDraftResponse> CompleteAsync(
        HttpClient client,
        Guid draftId,
        CompleteOnlineSalesDraftRequest command,
        string key)
    {
        using var request = Mutation(draftId, command, key);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Checkout failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<CompleteOnlineSalesDraftResponse>()
            ?? throw new InvalidOperationException("Empty checkout response.");
    }

    private async Task<Guid> EnsureTransferBankAccountAsync()
    {
        using var accounting = fixture.CreateAdminClient(
            AccountingPermissionCodes.Read,
            AccountingPermissionCodes.Configure);
        var existing = await accounting.GetFromJsonAsync<BankAccountView[]>(
            "/api/commerce/v1/accounting/bank-accounts?includeInactive=false") ?? [];
        var available = existing.FirstOrDefault(account => account.IsActive);
        if (available is not null)
            return available.BankAccountId;

        var accounts = await accounting.GetFromJsonAsync<AccountingAccountView[]>(
            "/api/commerce/v1/accounting/accounts") ?? [];
        var postingAccount = Assert.Single(accounts, account => account.Code == "111005");
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var option = new SqlCommand("""
            SELECT TOP(1) OptionId FROM reference.Options
            WHERE CatalogCode=N'bank-account-type' AND Code=N'Checking' AND IsActive=1;
            """, connection);
        var optionId = (Guid)(await option.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The bank-account-type seed is missing."));
        var bankAccountId = Guid.NewGuid();
        using var response = await accounting.PutAsJsonAsync(
            $"/api/commerce/v1/accounting/bank-accounts/{bankAccountId:D}",
            new SaveBankAccountRequest(bankAccountId, postingAccount.AccountId, optionId,
                "Banco de prueba", $"{bankAccountId:N}"[..12], "Cuenta de transferencias", true, true, null));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return bankAccountId;
    }

    private async Task<OnlineSalesIssuedSalePage> SearchAsync(
        HttpClient client,
        Guid workSessionId,
        string search)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/drafts/sales/search",
            new SearchOnlineSalesIssuedSalesRequest(
                new OnlineSalesDraftContext(
                    fixture.BusinessId,
                    fixture.WarehouseId,
                    workSessionId),
                search,
                0,
                50));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OnlineSalesIssuedSalePage>()
            ?? throw new InvalidOperationException("Empty issued sales response.");
    }

    private static HttpRequestMessage Mutation(
        Guid draftId,
        CompleteOnlineSalesDraftRequest body,
        string key)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draftId:D}/complete")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private async Task<PersistenceEvidence> ReadPersistenceAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.DeviceId,d.CreatedByDeviceId,d.SourceMode,d.SoldByUserId,d.WorkSessionId,
                   (SELECT COUNT(*) FROM dbo.SalesDocuments x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.SalesDocumentLines x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.SalesPayments x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.InventoryMovements x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.WorkSessionMovements x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.ServerOutboxMessages x WHERE x.DocumentId=d.DocumentId),
                   (SELECT COUNT(*) FROM dbo.DocumentProcessingJobs x WHERE x.DocumentId=d.DocumentId),
                   receipt.Status,draft.Status
            FROM dbo.SalesDocuments d
            JOIN dbo.OnlineSalesCheckoutReceipts receipt
              ON receipt.DocumentId=d.DocumentId
            JOIN dbo.SalesDrafts draft
              ON draft.SalesDraftId=receipt.SalesDraftId
            WHERE d.DocumentId=@DocumentId;
            """;
        command.Parameters.AddWithValue("@DocumentId", documentId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new(
            reader.IsDBNull(0) ? null : reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetString(2),
            reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.GetInt32(10),
            reader.GetInt32(11),
            reader.GetString(12),
            reader.GetString(13));
    }

    private async Task<IReadOnlyList<ConsecutiveEvidence>> ReadConsecutivesAsync(
        Guid firstDocumentId,
        Guid secondDocumentId)
    {
        var rows = new List<ConsecutiveEvidence>();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DeviceId,DocumentConsecutive,FiscalConsecutive,SoldByUserId
            FROM dbo.SalesDocuments
            WHERE DocumentId IN (@First,@Second)
            ORDER BY DocumentConsecutive;
            """;
        command.Parameters.AddWithValue("@First", firstDocumentId);
        command.Parameters.AddWithValue("@Second", secondDocumentId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new(
                reader.IsDBNull(0) ? null : reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetGuid(3)));
        return rows;
    }

    private async Task<(long? Document, long? Fiscal)> ReadCursorValuesAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              (SELECT NextConsecutive FROM dbo.DocumentSeriesCursors
               WHERE DocumentSeriesId=@DocumentSeriesId),
              (SELECT NextConsecutive FROM dbo.FiscalSeriesCursors
               WHERE SeriesId=@FiscalSeriesId);
            """;
        command.Parameters.AddWithValue(
            "@DocumentSeriesId",
            fixture.OnlineDocumentSeriesId);
        command.Parameters.AddWithValue("@FiscalSeriesId", fixture.OnlineSeriesId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            reader.IsDBNull(0) ? null : reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetInt64(1));
    }

    private async Task ExecuteAsync(
        string sql,
        params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<byte[]> ReadFiscalArtifactAsync(Guid documentId, string artifactType)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Content
            FROM dbo.FiscalArtifacts
            WHERE DocumentId=@DocumentId AND ArtifactType=@ArtifactType;
            """;
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@ArtifactType", artifactType);
        return (byte[])(await command.ExecuteScalarAsync())!;
    }

    private sealed record PersistenceEvidence(
        Guid? DeviceId,
        Guid? CreatedByDeviceId,
        string SourceMode,
        Guid SoldByUserId,
        Guid? WorkSessionId,
        int DocumentCount,
        int LineCount,
        int PaymentCount,
        int InventoryMovementCount,
        int WorkSessionMovementCount,
        int ServerOutboxCount,
        int ProcessingJobCount,
        string CheckoutStatus,
        string DraftStatus);

    private sealed record ConsecutiveEvidence(
        Guid? DeviceId,
        long DocumentConsecutive,
        long FiscalConsecutive,
        Guid SoldByUserId);
}
