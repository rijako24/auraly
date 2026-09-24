using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Operational")]
public sealed class SalesReturnProcessingTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Enrolled_pos_rejects_a_refund_without_the_active_work_session()
    {
        var request = new ConfirmSalesReturnRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, Guid.NewGuid(),
            DateTimeOffset.UtcNow, ReturnEconomicResolutions.Refund,
            SalesReturnRefundMethods.Cash, "Devolución sin sesión",
            [new ConfirmSalesReturnLineRequest(
                1, 1m, ReturnInventoryDispositions.Sellable)],
            WorkSessionId: null, ReasonCode: "Other");
        using var client = fixture.CreateClient();
        using var message = new HttpRequestMessage(
            HttpMethod.Post, "/api/pos/v1/sales-returns/confirm")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        message.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        message.Headers.Add("X-Auraly-User-Id", fixture.UserId.ToString("D"));
        message.Headers.Add("Idempotency-Key", request.ReturnId.ToString("D"));

        using var response = await client.SendAsync(message);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("no identificó el usuario o el negocio",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Closure_subtracts_each_refund_from_its_payment_method()
    {
        const long consecutive = 9_505;
        var bankAccountId = await EnsureTransferBankAccountAsync();
        var original = WithUblSnapshot(fixture.CreateValidRequest(consecutive) with
        {
            Payments =
            [
                new PosSalePaymentContract(1, "Cash", 4_000m, null),
                new PosSalePaymentContract(2, "CreditCard", 4_000m,
                    "SALE-9505", "Visa", "APPROVAL-9505"),
                new PosSalePaymentContract(3, "Transfer", 3_900m, "TRANSFER-9505",
                    BankAccountId: bankAccountId)
            ]
        });
        using (var pos = fixture.CreateClient())
        using (var upload = fixture.CreateUploadMessage(original))
        using (var uploadResponse = await pos.SendAsync(upload))
            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        var originalJob = await JobEvidenceAsync(original.DocumentId);
        Assert.True(originalJob.Status == "Completed", originalJob.LastError ?? originalJob.Status);

        using var user = fixture.CreateAdminClient(
            SalesReturnPermissionCodes.Create,
            SalesReturnPermissionCodes.Confirm,
            WorkSessionPermissionCodes.Read,
            WorkSessionPermissionCodes.Close);
        foreach (var (refundMethod, closureMethod) in new[]
        {
            (SalesReturnRefundMethods.Cash, "Cash"),
            (SalesReturnRefundMethods.CreditCard, "Card"),
            (SalesReturnRefundMethods.Transfer, "Transfer")
        })
        {
            var before = await user.GetFromJsonAsync<WorkSessionClosurePreviewView>(
                $"/api/commerce/v1/work-sessions/{fixture.WorkSessionId:D}/closure-preview");
            Assert.NotNull(before);
            var beforeMethod = Assert.Single(before.PaymentTotals,
                value => value.PaymentMethodCode == closureMethod);
            var request = new ConfirmSalesReturnRequest(
                Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
                original.DocumentId, DateTimeOffset.UtcNow,
                ReturnEconomicResolutions.Refund, refundMethod,
                "Regresión de cierre por medio de pago",
                [new ConfirmSalesReturnLineRequest(
                    1, .2m, ReturnInventoryDispositions.Sellable)],
                fixture.WorkSessionId,
                refundMethod == SalesReturnRefundMethods.CreditCard ? 2 : null,
                "Other",
                BankAccountId: refundMethod == SalesReturnRefundMethods.Transfer
                    ? bankAccountId
                    : null,
                SettlementReference: refundMethod == SalesReturnRefundMethods.Transfer
                    ? "REFUND-TRANSFER-9505"
                    : null);
            using var message = Message(request, $"closure-refund-{request.ReturnId:N}");
            using var response = await user.SendAsync(message);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var acceptance = await response.Content.ReadFromJsonAsync<SalesReturnAcceptance>();
            Assert.NotNull(acceptance);
            Assert.Equal(2_380m, acceptance.TotalAmount);
            Assert.Equal(refundMethod, acceptance.RefundMethodCode);
            Assert.Equal(fixture.WorkSessionId, acceptance.WorkSessionId);

            var after = await user.GetFromJsonAsync<WorkSessionClosurePreviewView>(
                $"/api/commerce/v1/work-sessions/{fixture.WorkSessionId:D}/closure-preview");
            Assert.NotNull(after);
            var afterMethod = Assert.Single(after.PaymentTotals,
                value => value.PaymentMethodCode == closureMethod);
            Assert.Equal(beforeMethod.RefundAmount + 2_380m, afterMethod.RefundAmount);
            Assert.Equal(beforeMethod.NetAmount - 2_380m, afterMethod.NetAmount);
            Assert.Equal(before.TotalRefunds + 2_380m, after.TotalRefunds);
            Assert.Equal(before.NetAmount - 2_380m, after.NetAmount);
        }
    }

    [Fact]
    public async Task Card_sale_can_be_refunded_in_cash_without_linking_the_original_payment()
    {
        var original = WithUblSnapshot(fixture.CreateValidRequest(9_502) with
        {
            Payments = [new PosSalePaymentContract(
                1, "CreditCard", 11_900m, "SALE-CARD", "Visa", "SALE-APPROVAL")]
        });
        using (var pos = fixture.CreateClient())
        using (var upload = fixture.CreateUploadMessage(original))
        using (var uploadResponse = await pos.SendAsync(upload))
            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        Assert.Equal("Completed", await JobStatusAsync(original.DocumentId));

        var request = new ConfirmSalesReturnRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            original.DocumentId,
            new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.FromHours(-5)),
            ReturnEconomicResolutions.Refund, SalesReturnRefundMethods.Cash,
            "Cambio de medio para el reintegro",
            [new ConfirmSalesReturnLineRequest(
                1, .25m, ReturnInventoryDispositions.Sellable)],
            fixture.WorkSessionId, null, "Other");
        using var user = fixture.CreateAdminClient(
            SalesReturnPermissionCodes.Create, SalesReturnPermissionCodes.Confirm);
        using var message = Message(request, $"sales-return-cash-{request.ReturnId:N}");
        using var response = await user.SendAsync(message);
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            await response.Content.ReadAsStringAsync());
        Assert.Equal("Completed", await JobStatusAsync(request.ReturnId));

        Assert.Equal("Cash", await ScalarAsync<string>(
            "SELECT RefundMethodCode FROM dbo.SalesReturns WHERE ReturnId=@Id", request.ReturnId));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.SalesReturns WHERE ReturnId=@Id AND OriginalPaymentNumber IS NOT NULL",
            request.ReturnId));
        Assert.Equal(-2_975m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.WorkSessionMovements WHERE SourceKey=CONCAT(N'sales-return:',REPLACE(CONVERT(nvarchar(36),@Id),N'-',N''))",
            request.ReturnId));
    }

    [Fact]
    public async Task Administrative_refund_uses_the_current_users_open_work_session()
    {
        var original = WithUblSnapshot(fixture.CreateValidRequest(9_504));
        using (var pos = fixture.CreateClient())
        using (var upload = fixture.CreateUploadMessage(original))
        using (var uploadResponse = await pos.SendAsync(upload))
            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        Assert.Equal("Completed", await JobStatusAsync(original.DocumentId));

        var request = new ConfirmSalesReturnRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            original.DocumentId,
            new DateTimeOffset(2026, 8, 1, 10, 10, 0, TimeSpan.FromHours(-5)),
            ReturnEconomicResolutions.Refund, SalesReturnRefundMethods.Cash,
            "Reintegro administrativo fuera del punto de venta",
            [new ConfirmSalesReturnLineRequest(
                1, .25m, ReturnInventoryDispositions.Sellable)],
            null, null, "Other");
        using var user = fixture.CreateAdminClient(SalesReturnPermissionCodes.Create);
        using (var saleResponse = await user.GetAsync(
                   $"/api/commerce/v1/sales-returns/sales/{original.DocumentId:D}?businessId={fixture.BusinessId:D}"))
            Assert.Equal(HttpStatusCode.OK, saleResponse.StatusCode);
        using var message = Message(
            request, $"sales-return-admin-cash-{request.ReturnId:N}");
        using var response = await user.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var acceptance = await response.Content.ReadFromJsonAsync<SalesReturnAcceptance>();
        Assert.NotNull(acceptance);
        Assert.NotNull(acceptance.WorkSessionId);
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.WorkSessions WHERE WorkSessionId=@Id AND Status=N'Open'",
            acceptance.WorkSessionId.Value));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.SalesReturns WHERE ReturnId=@Id",
            request.ReturnId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE SourceKey=CONCAT(N'sales-return:',REPLACE(CONVERT(nvarchar(36),@Id),N'-',N''))",
            request.ReturnId));
    }

    [Fact]
    public async Task Card_refund_reverses_original_payment_and_copies_its_evidence()
    {
        var original = WithUblSnapshot(fixture.CreateValidRequest(9_503) with
        {
            Payments = [new PosSalePaymentContract(
                1, "CreditCard", 11_900m, "SALE-CARD", "Visa", "SALE-APPROVAL-503")]
        });
        using (var pos = fixture.CreateClient())
        using (var upload = fixture.CreateUploadMessage(original))
        using (var uploadResponse = await pos.SendAsync(upload))
            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        Assert.Equal("Completed", await JobStatusAsync(original.DocumentId));

        var request = new ConfirmSalesReturnRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            original.DocumentId,
            new DateTimeOffset(2026, 8, 1, 10, 15, 0, TimeSpan.FromHours(-5)),
            ReturnEconomicResolutions.Refund, SalesReturnRefundMethods.CreditCard,
            "Reversión del pago original con tarjeta",
            [new ConfirmSalesReturnLineRequest(
                1, .25m, ReturnInventoryDispositions.Sellable)],
            fixture.WorkSessionId, 1, "Other", null, SalesReturnScopes.Partial);
        using var user = fixture.CreateAdminClient(
            SalesReturnPermissionCodes.Create, SalesReturnPermissionCodes.Confirm);
        using var message = Message(request, $"sales-return-card-{request.ReturnId:N}");
        using var response = await user.SendAsync(message);
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            await response.Content.ReadAsStringAsync());
        Assert.Equal("Completed", await JobStatusAsync(request.ReturnId));

        Assert.Equal("Visa|SALE-APPROVAL-503", await ScalarAsync<string>(
            "SELECT CONCAT(CardFranchiseCode,N'|',ApprovalNumber) FROM dbo.SalesReturns WHERE ReturnId=@Id",
            request.ReturnId));
        Assert.Equal("Visa|SALE-APPROVAL-503", await ScalarAsync<string>(
            "SELECT CONCAT(CardFranchiseCode,N'|',ApprovalNumber) FROM dbo.SalesReturnSettlements WHERE ReturnId=@Id",
            request.ReturnId));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE SourceKey=CONCAT(N'sales-return:',REPLACE(CONVERT(nvarchar(36),@Id),N'-',N''))",
            request.ReturnId));
    }

    [Fact]
    public async Task Return_flows_once_through_api_motor_inventory_and_refund()
    {
        var original = WithUblSnapshot(fixture.CreateValidRequest(9_501));
        using (var pos = fixture.CreateClient())
        using (var upload = fixture.CreateUploadMessage(original))
        using (var response = await pos.SendAsync(upload))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Completed", await JobStatusAsync(original.DocumentId));
        var afterSale = await QuantityAsync();

        var request = new ConfirmSalesReturnRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            original.DocumentId,
            new DateTimeOffset(2026, 8, 1, 9, 30, 0, TimeSpan.FromHours(-5)),
            ReturnEconomicResolutions.Refund, "Cash", "Cliente devuelve parcialmente",
            [new ConfirmSalesReturnLineRequest(
                1, .5m, ReturnInventoryDispositions.Sellable)],
            fixture.WorkSessionId, null, "Other");
        const string idempotencyKey = "sales-return-e2e-001";
        using var user = fixture.CreateAdminClient(
            SalesReturnPermissionCodes.Read, SalesReturnPermissionCodes.Create,
            SalesReturnPermissionCodes.Confirm);
        using (var message = Message(request, idempotencyKey))
        using (var response = await user.SendAsync(message))
        {
            Assert.True(response.StatusCode == HttpStatusCode.Accepted,
                await response.Content.ReadAsStringAsync());
            var accepted = await response.Content.ReadFromJsonAsync<SalesReturnAcceptance>();
            Assert.NotNull(accepted);
            Assert.StartsWith("DVT00-", accepted.DocumentNumber);
            Assert.False(accepted.IdempotentReplay);
        }

        Assert.Equal("Completed", await JobStatusAsync(request.ReturnId));
        Assert.Equal("Processed", await ScalarAsync<string>(
            "SELECT Status FROM dbo.SalesReturns WHERE ReturnId=@Id", request.ReturnId));
        Assert.Equal(request.OriginalDocumentId, await ScalarAsync<Guid>(
            "SELECT OriginalDocumentId FROM dbo.SalesReturns WHERE ReturnId=@Id",
            request.ReturnId));
        Assert.Equal(afterSale + .5m, await QuantityAsync());
        Assert.Equal(await MovementInventoryValueAfterAsync(request.ReturnId),
            await InventoryValueAsync());
        Assert.Equal(5_000m, await ScalarAsync<decimal>(
            "SELECT SUM(UntaxedAmount) FROM dbo.SalesReturnLines WHERE ReturnId=@Id", request.ReturnId));
        Assert.Equal(950m, await ScalarAsync<decimal>(
            "SELECT SUM(TaxAmount) FROM dbo.SalesReturnLines WHERE ReturnId=@Id", request.ReturnId));

        Assert.Equal(5_950m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.SalesReturnSettlements WHERE ReturnId=@Id", request.ReturnId));
        Assert.Equal(1, await CountAsync("InventoryMovements", "DocumentId", request.ReturnId));
        Assert.Equal(1, await CountAsync("SalesReturnSettlements", "ReturnId", request.ReturnId));
        Assert.Equal(1, await CountAsync("ServerOutboxMessages", "DocumentId", request.ReturnId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM reporting.SalesReportingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'SalesReturn'",
            request.ReturnId));
        Assert.Equal(.5m, await ScalarAsync<decimal>(
            "SELECT -Quantity FROM reporting.SalesReportLineFacts WHERE OriginalSaleDocumentId=@Id AND OriginalLineNumber=1 AND SourceDocumentType=N'SalesReturn'",
            request.OriginalDocumentId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE SourceKey=CONCAT(N'sales-return:',REPLACE(CONVERT(nvarchar(36),@Id),N'-',N''))",
            request.ReturnId));
        Assert.Equal(-5_950m, await ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.WorkSessionMovements WHERE SourceKey=CONCAT(N'sales-return:',REPLACE(CONVERT(nvarchar(36),@Id),N'-',N''))",
            request.ReturnId));

        using (var listResponse = await user.GetAsync(
                   "/api/commerce/v1/sales-returns?page=1&pageSize=20&search=DVT00"))
        {
            listResponse.EnsureSuccessStatusCode();
            var page = await listResponse.Content.ReadFromJsonAsync<SalesReturnPage>();
            Assert.NotNull(page);
            Assert.Contains(page.Items, item => item.ReturnId == request.ReturnId);
        }
        using (var detailResponse = await user.GetAsync(
                   $"/api/commerce/v1/sales-returns/{request.ReturnId:D}"))
        {
            detailResponse.EnsureSuccessStatusCode();
            var detail = await detailResponse.Content.ReadFromJsonAsync<SalesReturnDetail>();
            Assert.NotNull(detail);
            Assert.Equal("Other", detail.ReasonCode);
            Assert.Single(detail.Lines);
        }

        using (var salesResponse = await user.GetAsync(
                   $"/api/commerce/v1/sales-returns/sales?businessId={fixture.BusinessId:D}&page=1&pageSize=20&search={Uri.EscapeDataString(original.DocumentNumber.FullNumber)}&withAvailableQuantity=true"))
        {
            Assert.True(salesResponse.IsSuccessStatusCode,
                await salesResponse.Content.ReadAsStringAsync());
            var page = await salesResponse.Content.ReadFromJsonAsync<ReturnableSalePage>();
            Assert.NotNull(page);
            Assert.Contains(page.Items, item => item.DocumentId == original.DocumentId);
        }
        using (var saleResponse = await user.GetAsync(
                   $"/api/commerce/v1/sales-returns/sales/{original.DocumentId:D}?businessId={fixture.BusinessId:D}"))
        {
            saleResponse.EnsureSuccessStatusCode();
            var sale = await saleResponse.Content.ReadFromJsonAsync<ReturnableSale>();
            Assert.NotNull(sale);
            Assert.Equal(.5m, Assert.Single(sale.Lines).AvailableQuantity);
            Assert.Equal(11_900m, Assert.Single(sale.Payments).AvailableAmount);
        }


        using (var replayMessage = Message(request, idempotencyKey))
        using (var replayResponse = await user.SendAsync(replayMessage))
        {
            Assert.Equal(HttpStatusCode.Accepted, replayResponse.StatusCode);
            var replay = await replayResponse.Content.ReadFromJsonAsync<SalesReturnAcceptance>();
            Assert.NotNull(replay);
            Assert.True(replay.IdempotentReplay);
        }
        Assert.Equal(afterSale + .5m, await QuantityAsync());
        Assert.Equal(1, await CountAsync("InventoryMovements", "DocumentId", request.ReturnId));

        var firstConcurrent = request with { ReturnId = Guid.NewGuid() };
        var secondConcurrent = request with { ReturnId = Guid.NewGuid() };
        using var firstConcurrentMessage = Message(
            firstConcurrent, $"concurrent-{firstConcurrent.ReturnId:N}");
        using var secondConcurrentMessage = Message(
            secondConcurrent, $"concurrent-{secondConcurrent.ReturnId:N}");
        var concurrentResponses = await Task.WhenAll(
            user.SendAsync(firstConcurrentMessage),
            user.SendAsync(secondConcurrentMessage));
        try
        {
            Assert.Single(concurrentResponses.Where(
                response => response.StatusCode == HttpStatusCode.Accepted));
            Assert.Single(concurrentResponses.Where(
                response => response.StatusCode == HttpStatusCode.Conflict));
        }
        finally
        {
            foreach (var response in concurrentResponses) response.Dispose();
        }
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.SalesReturnLines WHERE OriginalDocumentId=@Id",
            original.DocumentId));


        var excessive = request with
        {
            ReturnId = Guid.NewGuid(),
            Lines = [new ConfirmSalesReturnLineRequest(
                1, .6m, ReturnInventoryDispositions.Sellable)]
        };
        using var excessiveMessage = Message(excessive, $"excess-{Guid.NewGuid():N}");
        using var excessiveResponse = await user.SendAsync(excessiveMessage);
        Assert.Equal(HttpStatusCode.Conflict, excessiveResponse.StatusCode);
    }

    [Fact]
    public async Task Return_requires_backend_permissions_and_authenticated_business()
    {
        using var denied = fixture.CreateAdminClient(SalesReturnPermissionCodes.Confirm);
        var request = new ConfirmSalesReturnRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, Guid.NewGuid(),
            DateTimeOffset.UtcNow, ReturnEconomicResolutions.Refund, "Cash", "Prueba",
            [new ConfirmSalesReturnLineRequest(1, 1m, ReturnInventoryDispositions.Sellable)],
            ReasonCode: "Other");
        using (var deniedMessage = Message(request, $"denied-{Guid.NewGuid():N}"))
        using (var deniedResponse = await denied.SendAsync(deniedMessage))
            Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        using (var deniedRead = await denied.GetAsync(
                   "/api/commerce/v1/sales-returns?page=1&pageSize=20"))
            Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);

        using var allowed = fixture.CreateAdminClient(SalesReturnPermissionCodes.Create);
        using var wrongScope = Message(
            request with { BusinessId = Guid.NewGuid() }, $"scope-{Guid.NewGuid():N}");
        using var wrongScopeResponse = await allowed.SendAsync(wrongScope);
        Assert.Equal(HttpStatusCode.Forbidden, wrongScopeResponse.StatusCode);
    }

    private static HttpRequestMessage Message(
        ConfirmSalesReturnRequest request, string idempotencyKey)
    {
        var message = new HttpRequestMessage(
            HttpMethod.Post, "/api/commerce/v1/sales-returns/confirm")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return message;
    }

    private PosSaleUploadRequest WithUblSnapshot(PosSaleUploadRequest request)
    {
        var address = new PosSaleUblAddressContract(
            "11001", "Bogotá", "Bogotá D.C.", "11", "CL 1 2 3");
        var supplier = new PosSaleUblPartyContract(
            ServerSliceFixture.SupplierTaxId, "0", "31", "1",
            "EMISOR HISTORICO", "EMISOR HISTORICO", "R-99-PN", "01", "IVA", address);
        var customer = new PosSaleUblPartyContract(
            "222222222", "0", "13", "2", "CLIENTE HISTORICO", "CLIENTE HISTORICO",
            "R-99-PN", "ZZ", "No aplica", address);
        return request with
        {
            UblSnapshot = new PosSaleUblSnapshotContract(
                fixture.FiscalIssuerConfigurationId, "COP", "01", supplier, customer,
                new PosSaleUblAuthorizationContract(
                    ServerSliceFixture.AuthorizationNumber,
                    new DateOnly(2026, 1, 1), new DateOnly(2028, 12, 31),
                    ServerSliceFixture.Prefix, 1, 10000),
                "auraly-test-software",
                [new PosSaleUblLineContract(1, "P-E2E", "999", "EA", "IVA", 19m)],
                "1", "10", DateOnly.FromDateTime(request.FiscalSnapshot!.IssuedAt.Date), null)
        };
    }

    private async Task<decimal> QuantityAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT QuantityOnHand FROM dbo.InventoryBalances
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        return Convert.ToDecimal(await command.ExecuteScalarAsync());
    }

    private async Task<string> JobStatusAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Status FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id";
        command.Parameters.AddWithValue("@Id", documentId);
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }

    private async Task<(string Status, string? LastError)> JobEvidenceAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Status,LastError FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id";
        command.Parameters.AddWithValue("@Id", documentId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
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
                "Banco de prueba", $"{bankAccountId:N}"[..12],
                "Cuenta de devoluciones", true, true, null));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return bankAccountId;
    }

    private async Task<decimal> InventoryValueAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT InventoryValue FROM dbo.InventoryBalances
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        return Convert.ToDecimal(await command.ExecuteScalarAsync());
    }

    private async Task<decimal> MovementInventoryValueAfterAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CAST(QuantityAfter*AverageUnitCostAfter AS DECIMAL(19,4))
            FROM dbo.InventoryMovements
            WHERE DocumentId=@Id AND LineNumber=1;
            """;
        command.Parameters.AddWithValue("@Id", documentId);
        return Convert.ToDecimal(await command.ExecuteScalarAsync());
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Id", id);
        var value = await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Expected SQL value was not returned.");
        return (T)Convert.ChangeType(value, typeof(T));
    }

    private async Task<int> CountAsync(string table, string column, Guid id)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "InventoryMovements:DocumentId",
            "SalesReturnSettlements:ReturnId",
            "ServerOutboxMessages:DocumentId"
        };
        Assert.Contains($"{table}:{column}", allowed);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM dbo.[{table}] WHERE [{column}]=@Id";
        command.Parameters.AddWithValue("@Id", id);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
