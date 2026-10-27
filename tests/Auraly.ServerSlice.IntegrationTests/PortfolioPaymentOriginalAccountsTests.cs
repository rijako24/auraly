using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Purchasing;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PayablesVerticalSliceTests
{
    [Fact]
    public async Task History_and_reports_share_the_signed_transaction_projection()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT expected.Type,expected.BalanceChange,effect.BalanceChange FROM (VALUES
              (N'Opening',100,100),(N'Payment',30,-30),(N'CreditApplication',25,-25),
              (N'Credit',20,-20),(N'Reversal',15,-15),(N'Adjustment',-10,-10),(N'Adjustment',10,10)) expected(Type,Amount,BalanceChange)
            CROSS APPLY accounting.PortfolioTransactionEffect(expected.Type,expected.Amount) effect;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var count = 0;
        while (await reader.ReadAsync())
        {
            Assert.Equal(reader.GetInt32(1), reader.GetDecimal(2)); count++;
        }
        Assert.Equal(7, count);
    }

    [Fact]
    public async Task Native_payment_uses_each_original_payable_account_after_the_mapping_changes()
    {
        var product = Guid.NewGuid();
        await ConfigureSeriesAndAccountingAsync(product);
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts, PayablesPermissionCodes.RegisterPayment,
            AccountingPermissionCodes.Configure, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
            AccountingPermissionCodes.Read, AccountingPermissionCodes.Retry);
        var date = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        async Task<Guid> ReceiveAsync()
        {
            var request = new ConfirmGoodsReceiptRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
                fixture.SupplierId, $"ORIGINAL-{Guid.NewGuid():N}", date, date, true, date.AddDays(30), "COP", null,
                [new(1, product, "Compra para probar el auxiliar original", 1m, 10_000m, 0m, "00", 0m,
                    PurchasingTaxTreatments.NotApplicable)]);
            using var response = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", request,
                request.DocumentId.ToString("N"));
            Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
            return await ScalarAsync<Guid>("SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id", request.DocumentId);
        }
        var first = await ReceiveAsync();
        var alternate = await PortfolioOriginalAccountScenario.RemapAsync(fixture, client, false);
        try
        {
            var second = await ReceiveAsync();
            var payment = new ConfirmSupplierPaymentRequest(Guid.NewGuid(), fixture.BusinessId, fixture.SupplierId,
                date.AddDays(1), "COP", "Un pago, dos auxiliares originales", [new(first, 1_000m), new(second, 1_000m)],
                [new(SupplierPaymentMethods.Cash, 2_000m, 2_000m)]);
            using var counter = PortfolioOriginalAccountScenario.CountOriginalReads(fixture, false);
            using var applicationWrites = PortfolioOriginalAccountScenario.CountApplicationWrites(fixture);
            using var accepted = await SendAsync(client, "/api/commerce/v1/payable-payments/confirm", payment,
                payment.PaymentId.ToString("N"));
            Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
            Assert.Equal(1, counter.Count);
            Assert.Equal(1, applicationWrites.Count);
            await PortfolioOriginalAccountScenario.AssertOriginalLinesAsync(fixture, false, payment.PaymentId);
            await PortfolioOriginalAccountScenario.AssertInvalidBindingsAsync(fixture, client, false, first,
                id => SendAsync(client, "/api/commerce/v1/payable-payments/confirm", payment with
                {
                    PaymentId = id, Allocations = [new(first, 100m)],
                    Payments = [new(SupplierPaymentMethods.Cash, 100m, 100m)]
                }, id.ToString("N")));
            await PortfolioOriginalAccountScenario.AssertAdjustmentsAsync(fixture, client, false, first, date.AddDays(2));
            using var replay = await SendAsync(client, "/api/commerce/v1/payable-payments/confirm", payment,
                payment.PaymentId.ToString("N"));
            Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
            await PortfolioOriginalAccountScenario.AssertOriginalLinesAsync(fixture, false, payment.PaymentId);
        }
        finally { await PortfolioOriginalAccountScenario.RestoreAsync(fixture, alternate); }
    }
}

public sealed partial class ReceivablesVerticalSliceTests
{
    [Fact]
    public async Task Native_payment_uses_each_original_receivable_account_after_the_mapping_changes()
    {
        var (customerId, userId, _) = await ConfigureAsync();
        using var client = fixture.CreateUserClient(userId, ReceivablesPermissionCodes.ManageCredit,
            ReceivablesPermissionCodes.RegisterPayment, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
            AccountingPermissionCodes.Read, AccountingPermissionCodes.Retry);
        var date = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var counterpart = await AccountIdByCodeAsync("413595");
        async Task<Guid> ImportAsync()
        {
            var id = Guid.NewGuid();
            using var response = await client.PostAsJsonAsync("/api/commerce/v1/receivables/preexisting/import",
                new ImportPreexistingReceivablesRequest(fixture.BusinessId,
                    [new(id, customerId, null, null, $"ORIGINAL-{id:N}", date, date.AddDays(30), 10_000m, counterpart, null)]));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return id;
        }
        var first = await ImportAsync();
        var alternate = await PortfolioOriginalAccountScenario.RemapAsync(fixture, client, true);
        try
        {
            var second = await ImportAsync();
            var payment = new ConfirmCustomerPaymentRequest(Guid.NewGuid(), fixture.BusinessId, customerId, null,
                date.AddDays(1), "COP", "Un recaudo, dos auxiliares originales", [new(first, 1_000m), new(second, 1_000m)],
                [new("Cash", 2_000m, 2_000m)]);
            using var counter = PortfolioOriginalAccountScenario.CountOriginalReads(fixture, true);
            using var applicationWrites = PortfolioOriginalAccountScenario.CountApplicationWrites(fixture);
            using var accepted = await SendAsync(client, "/api/commerce/v1/receivable-payments/confirm", payment,
                payment.PaymentId.ToString("N"));
            Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
            Assert.Equal(1, counter.Count);
            Assert.Equal(1, applicationWrites.Count);
            await PortfolioOriginalAccountScenario.AssertOriginalLinesAsync(fixture, true, payment.PaymentId);
            await PortfolioOriginalAccountScenario.AssertInvalidBindingsAsync(fixture, client, true, first,
                id => SendAsync(client, "/api/commerce/v1/receivable-payments/confirm", payment with
                {
                    PaymentId = id, Allocations = [new(first, 100m)], Payments = [new("Cash", 100m, 100m)]
                }, id.ToString("N")));
            await PortfolioOriginalAccountScenario.AssertAdjustmentsAsync(fixture, client, true, first, date.AddDays(2));
            using var replay = await SendAsync(client, "/api/commerce/v1/receivable-payments/confirm", payment,
                payment.PaymentId.ToString("N"));
            Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
            await PortfolioOriginalAccountScenario.AssertOriginalLinesAsync(fixture, true, payment.PaymentId);
        }
        finally { await PortfolioOriginalAccountScenario.RestoreAsync(fixture, alternate); }
    }
}

internal static class PortfolioOriginalAccountScenario
{
    internal static async Task AssertReturnOriginalLineAsync(ServerSliceFixture fixture, bool receivable,
        Guid originalDocumentId, Guid returnId, decimal amount)
    {
        var table = receivable ? "dbo.Receivables" : "dbo.Payables";
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"""
            SELECT COUNT(*) FROM {table} position
            JOIN dbo.AccountingEntries original ON original.EntryId=position.OpeningEntryId
              AND original.TenantId=@TenantId AND original.BusinessId=@BusinessId
            JOIN dbo.AccountingEntryLines opening ON opening.EntryId=original.EntryId
              AND opening.LineNumber=position.OpeningEntryLineNumber
            JOIN dbo.AccountingEntries returned ON returned.SourceDocumentId=@ReturnId
              AND returned.SourceDocumentType=@SourceType AND returned.TenantId=@TenantId AND returned.BusinessId=@BusinessId
            JOIN dbo.AccountingEntryLines line ON line.EntryId=returned.EntryId AND line.AccountId=opening.AccountId
              AND line.PartyId=opening.PartyId AND line.Debit=@Debit AND line.Credit=@Credit
            WHERE position.BusinessId=@BusinessId AND position.SourceDocumentId=@OriginalDocumentId AND position.SourceLineId IS NULL;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@OriginalDocumentId", originalDocumentId);
        command.Parameters.AddWithValue("@ReturnId", returnId);
        command.Parameters.AddWithValue("@SourceType", receivable ? "SalesReturn" : "PurchaseReturn");
        command.Parameters.AddWithValue("@Debit", receivable ? 0m : amount);
        command.Parameters.AddWithValue("@Credit", receivable ? amount : 0m);
        Assert.Equal(1, (int)(await command.ExecuteScalarAsync())!);
    }

    internal static Task AssertSupplierCreditOpeningAsync(ServerSliceFixture fixture, Guid sourceId, string sourceType, int expectedCount = 1) =>
        AssertCreditOpeningAsync(fixture, sourceId, sourceType, expectedCount, true);

    internal static Task AssertCustomerCreditOpeningAsync(ServerSliceFixture fixture, Guid sourceId, string sourceType, int expectedCount = 1) =>
        AssertCreditOpeningAsync(fixture, sourceId, sourceType, expectedCount, false);

    private static async Task AssertCreditOpeningAsync(ServerSliceFixture fixture, Guid sourceId, string sourceType, int expectedCount, bool supplierCredit)
    {
        var role = supplierCredit ? "Supplier" : "Customer";
        var party = supplierCredit ? "counterparty.PartyId" : "COALESCE(credit.PartyId,counterparty.PartyId)";
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"""
            SELECT credit.OriginalAmount,credit.CreditNature,credit.CurrencyCode,opening.Amount,
              credit.OpeningEntryId,credit.OpeningEntryLineNumber,line.Debit,line.Credit,line.PartyId,
              {party},entry.SourceDocumentId,entry.SourceDocumentType,job.AccountingEntryRequired
            FROM dbo.{role}CreditTransactions opening
            JOIN dbo.{role}Credits credit ON credit.{role}CreditId=opening.{role}CreditId AND credit.BusinessId=@BusinessId
            LEFT JOIN dbo.{role}s counterparty ON counterparty.{role}Id=credit.{role}Id AND counterparty.TenantId=@TenantId
            JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=opening.SourceDocumentId
              AND job.SourceDocumentType=opening.SourceDocumentType AND job.BusinessId=@BusinessId AND job.TenantId=@TenantId
            LEFT JOIN dbo.AccountingEntryLines line ON line.EntryId=credit.OpeningEntryId AND line.LineNumber=credit.OpeningEntryLineNumber
            LEFT JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId AND entry.BusinessId=@BusinessId AND entry.TenantId=@TenantId
            WHERE opening.SourceDocumentId=@SourceId AND opening.SourceDocumentType=@SourceType AND opening.TransactionType=N'Opening';
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@SourceId", sourceId);
        command.Parameters.AddWithValue("@SourceType", sourceType);
        await using var reader = await command.ExecuteReaderAsync();
        var count = 0;
        while (await reader.ReadAsync())
        {
            count++;
            Assert.Equal("Credit", reader.GetString(1));
            Assert.Equal("COP", reader.GetString(2));
            Assert.Equal(reader.GetDecimal(0), reader.GetDecimal(3));
            if (reader.GetBoolean(12))
            {
                Assert.False(reader.IsDBNull(4));
                Assert.False(reader.IsDBNull(5));
                Assert.Equal(supplierCredit ? reader.GetDecimal(0) : 0m, reader.GetDecimal(6));
                Assert.Equal(supplierCredit ? 0m : reader.GetDecimal(0), reader.GetDecimal(7));
                Assert.Equal(reader.GetGuid(9), reader.GetGuid(8));
                Assert.Equal(sourceId, reader.GetGuid(10));
                Assert.Equal(sourceType, reader.GetString(11));
            }
            else
            {
                Assert.True(reader.IsDBNull(4));
                Assert.True(reader.IsDBNull(5));
            }
        }
        Assert.Equal(expectedCount, count);
    }

    internal static Task<AccountingAccountView> CreateCashAuxiliaryAsync(ServerSliceFixture fixture, HttpClient client) =>
        CreateConfiguredAuxiliaryAsync(fixture, client, "1109", "Asset", "Cash", "Debit");

    internal static async Task<AccountingAccountView> CreateConfiguredAuxiliaryAsync(ServerSliceFixture fixture, HttpClient client,
        string prefix, string type, string function, string side, Guid? bankAccountId = null, Guid? paymentMethodOptionId = null,
        bool sharedAcrossBusinesses = false)
    {
        var parentId = Guid.NewGuid();
        var parentCode = prefix + BitConverter.ToUInt64(parentId.ToByteArray()).ToString("D20");
        using (var response = await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
            new CreateAccountingAccountRequest(parentId, fixture.TenantId, parentCode, "Agrupación de comprobantes de prueba", type, false, false)))
            response.EnsureSuccessStatusCode();
        using var auxiliary = await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
            new CreateAccountingAccountRequest(Guid.NewGuid(), fixture.TenantId, parentCode + "01", "Auxiliar de comprobantes de prueba", type, true,
                function is "Payable" or "Receivable" or "SupplierAdvance" or "CustomerAdvance",
                new(parentId, function, side, sharedAcrossBusinesses ? null : fixture.BusinessId, bankAccountId, paymentMethodOptionId)));
        Assert.True(auxiliary.IsSuccessStatusCode, await auxiliary.Content.ReadAsStringAsync());
        return (await auxiliary.Content.ReadFromJsonAsync<AccountingAccountView>())!;
    }

    internal static InventoryBalanceProcessingTests.CommandCounter CountApplicationWrites(ServerSliceFixture fixture) =>
        new(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog, commandFilter: command =>
            command.CommandText.Contains("DECLARE @Payments TABLE", StringComparison.Ordinal)
            && command.CommandText.Contains("INSERT dbo.WorkSessionMovements", StringComparison.Ordinal));

    internal static InventoryBalanceProcessingTests.CommandCounter CountOriginalReads(ServerSliceFixture fixture, bool receivable) =>
        new(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog, commandFilter: command =>
            command.CommandText.Contains($"FROM dbo.{(receivable ? "Customer" : "Supplier")}PaymentApplications application", StringComparison.Ordinal)
            && command.CommandText.Contains("accounting.ObligationOpeningAccount", StringComparison.Ordinal));

    internal static async Task AssertInvalidBindingsAsync(ServerSliceFixture fixture, HttpClient client,
        bool receivable, Guid positionId, Func<Guid, Task<HttpResponseMessage>> submit)
    {
        var table = receivable ? "Receivables" : "Payables";
        var key = receivable ? "ReceivableId" : "PayableId";
        var prefix = receivable ? "Customer" : "Supplier";
        var kind = receivable ? "Receivable" : "Payable";
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"""
            SELECT OpeningEntryId,OpeningEntryLineNumber FROM dbo.{table} WHERE {key}=@Id;
            """, connection);
        command.Parameters.AddWithValue("@Id", positionId);
        Guid entryId;
        int lineNumber;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            entryId = reader.GetGuid(0);
            lineNumber = reader.GetInt32(1);
        }
        command.Parameters.AddWithValue("@EntryId", entryId);
        command.Parameters.AddWithValue("@LineNumber", lineNumber);
        foreach (var missing in new[] { true, false })
        {
            command.CommandText = missing
                ? $"UPDATE dbo.{table} SET OpeningEntryId=NULL,OpeningEntryLineNumber=NULL WHERE {key}=@Id;"
                : $"""
                  UPDATE dbo.{table} SET OpeningEntryLineNumber=(SELECT MIN(LineNumber) FROM dbo.AccountingEntryLines
                    WHERE EntryId=@EntryId AND {(receivable ? "Credit" : "Debit")}>0) WHERE {key}=@Id;
                  """;
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
            var paymentId = Guid.NewGuid();
            AccountingOpenPosition position;
            try
            {
                var before = await client.GetFromJsonAsync<AccountingOpenPositionPage>(
                    $"/api/commerce/v1/accounting/open-positions?subledgerKind={kind}&subledgerId={positionId}&page=1&pageSize=10");
                position = Assert.Single(before!.Items);
                Assert.False(string.IsNullOrWhiteSpace(position.OpeningLinkIssue));
                using var response = await submit(paymentId);
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                command.CommandText = $"""
                    SELECT payment.Status,job.Status,job.LastErrorCode,job.LastErrorMessage,
                      position.OutstandingAmount,
                      (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@PaymentId),
                      (SELECT COUNT(*) FROM dbo.{kind}Transactions WHERE SourceDocumentId=@PaymentId)
                    FROM dbo.{prefix}Payments payment JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=payment.PaymentId
                    CROSS JOIN dbo.{table} position WHERE payment.PaymentId=@PaymentId AND position.{key}=@Id;
                    """;
                command.Parameters.AddWithValue("@PaymentId", paymentId);
                await using (var reader = await command.ExecuteReaderAsync())
                {
                    Assert.True(await reader.ReadAsync());
                    Assert.Equal("Accepted", reader.GetString(0));
                    Assert.Equal("AccountingPendingConfiguration", reader.GetString(1));
                    Assert.Equal("OriginalPositionAccountMissing", reader.GetString(2));
                    Assert.Contains(receivable ? "No se aplicó el recaudo" : "No se aplicó el pago", reader.GetString(3));
                    Assert.Contains(position.OpeningLinkIssue!, reader.GetString(3));
                    Assert.Equal(position.OutstandingAmount, reader.GetDecimal(4));
                    Assert.Equal(0, reader.GetInt32(5));
                    Assert.Equal(0, reader.GetInt32(6));
                }
                var pending = await client.GetFromJsonAsync<AccountingOpenPositionPage>(
                    $"/api/commerce/v1/accounting/open-positions?subledgerKind={kind}&subledgerId={positionId}&page=1&pageSize=10");
                Assert.Equal(100m, Assert.Single(pending!.Items).PendingPaymentAmount);
                Assert.Equal(position.AvailableAmount - 100m, pending.Items[0].AvailableAmount);
            }
            finally
            {
                command.CommandText = $"UPDATE dbo.{table} SET OpeningEntryId=@EntryId,OpeningEntryLineNumber=@LineNumber WHERE {key}=@Id;";
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
            }
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var retried = await client.PostAsync($"/api/commerce/v1/accounting/postings/{paymentId}/retry", null);
                Assert.True(retried.IsSuccessStatusCode, await retried.Content.ReadAsStringAsync());
            }
            command.CommandText = $"""
                SELECT OutstandingAmount,(SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@PaymentId),
                  (SELECT COUNT(*) FROM dbo.{kind}Transactions WHERE SourceDocumentId=@PaymentId)
                FROM dbo.{table} WHERE {key}=@Id;
                """;
            await using (var reader = await command.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(position.OutstandingAmount - 100m, reader.GetDecimal(0));
                Assert.Equal(1, reader.GetInt32(1));
                Assert.Equal(1, reader.GetInt32(2));
            }
            command.Parameters.RemoveAt("@PaymentId");
        }
    }

    internal static async Task AssertAdjustmentsAsync(ServerSliceFixture fixture, HttpClient client,
        bool receivable, Guid positionId, DateTimeOffset date)
    {
        var kind = receivable ? "Receivable" : "Payable";
        var selected = await client.GetFromJsonAsync<AccountingOpenPositionPage>(
            $"/api/commerce/v1/accounting/open-positions?subledgerKind={kind}&subledgerId={positionId}&page=1&pageSize=10");
        var partyId = Assert.Single(selected!.Items).PartyId;
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var counterpart = new SqlCommand(
            "SELECT AccountId FROM dbo.AccountingAccounts WHERE TenantId=@TenantId AND Code=N'519595'", connection);
        counterpart.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        var counterpartId = (Guid)(await counterpart.ExecuteScalarAsync())!;
        foreach (var direction in new[] { AccountingAdjustmentDirections.Increase, AccountingAdjustmentDirections.Decrease })
        {
            var request = new ConfirmAccountAdjustmentRequest(Guid.NewGuid(), fixture.BusinessId,
                receivable ? "Receivable" : "Payable", positionId, direction, 100m, counterpartId, null,
                date, "ACCOUNT_ADJUSTMENT", "Ajuste en el auxiliar de origen");
            using var accepted = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/account-adjustments", request);
            Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
            var table = receivable ? "dbo.Receivables" : "dbo.Payables";
            var key = receivable ? "ReceivableId" : "PayableId";
            await using var verify = new SqlCommand($"""
                SELECT COUNT(*) FROM {table} position
                JOIN dbo.AccountingEntryLines opening ON opening.EntryId=position.OpeningEntryId
                  AND opening.LineNumber=position.OpeningEntryLineNumber
                JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=@AdjustmentId AND entry.TenantId=@TenantId
                  AND entry.BusinessId=@BusinessId
                JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId AND line.AccountId=opening.AccountId
                  AND line.PartyId=opening.PartyId
                WHERE position.{key}=@PositionId
                  AND line.Debit=@Debit AND line.Credit=@Credit;
                """, connection);
            verify.Parameters.AddWithValue("@AdjustmentId", request.AdjustmentId);
            verify.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            verify.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            verify.Parameters.AddWithValue("@PositionId", positionId);
            var debit = receivable == (direction == AccountingAdjustmentDirections.Increase);
            verify.Parameters.AddWithValue("@Debit", debit ? 100m : 0m);
            verify.Parameters.AddWithValue("@Credit", debit ? 0m : 100m);
            Assert.Equal(1, (int)(await verify.ExecuteScalarAsync())!);
            var traceability = await client.GetFromJsonAsync<AccountingDocumentPage>(
                $"/api/commerce/v1/accounting/documents?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}&documentType=AccountAdjustment&partyId={partyId}&search={request.AdjustmentId:D}");
            var row = Assert.Single(traceability!.Items);
            Assert.Equal(request.AdjustmentId, row.SourceDocumentId);
            Assert.Equal(partyId, row.MainPartyId);
            Assert.False(string.IsNullOrWhiteSpace(row.MainPartyName));
        }
    }

    internal static async Task<(Guid AccountId, string Previous)> RemapAsync(ServerSliceFixture fixture, HttpClient client, bool receivable)
    {
        var accountId = Guid.NewGuid();
        var code = (receivable ? "1" : "2") + BitConverter.ToUInt64(accountId.ToByteArray()).ToString("D20");
        string previous;
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var snapshot = new SqlCommand("""
                SELECT COALESCE((SELECT * FROM dbo.AccountingAccountMappings WHERE TenantId=@TenantId AND BusinessId=@BusinessId
                  AND Category=@Category AND EffectiveFrom='2026-01-01' FOR JSON PATH),N'[]');
                """, connection);
            snapshot.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            snapshot.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            snapshot.Parameters.AddWithValue("@Category", receivable ? AccountingCategories.AccountsReceivable : AccountingCategories.AccountsPayable);
            previous = (string)(await snapshot.ExecuteScalarAsync())!;
        }
        using var account = await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
            new CreateAccountingAccountRequest(accountId, fixture.TenantId, code, "Otro auxiliar de la obligación",
                receivable ? "Asset" : "Liability", true, true));
        Assert.True(account.IsSuccessStatusCode, await account.Content.ReadAsStringAsync());
        using var mapping = await client.PutAsJsonAsync("/api/commerce/v1/accounting/account-mappings",
            new SetAccountMappingRequest(fixture.TenantId, fixture.BusinessId,
                receivable ? AccountingCategories.AccountsReceivable : AccountingCategories.AccountsPayable,
                accountId, new DateOnly(2026, 1, 1), null));
        Assert.True(mapping.IsSuccessStatusCode, await mapping.Content.ReadAsStringAsync());
        return (accountId, previous);
    }

    internal static async Task RestoreAsync(ServerSliceFixture fixture, (Guid AccountId, string Previous) alternate)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var restore = new SqlCommand("""
            DELETE dbo.AccountingAccountMappings WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND AccountId=@AccountId;
            INSERT dbo.AccountingAccountMappings(MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo,CreatedAt)
            SELECT MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo,CreatedAt
            FROM OPENJSON(@Previous) WITH(MappingId uniqueidentifier,TenantId uniqueidentifier,BusinessId uniqueidentifier,
              Category nvarchar(64),AccountId uniqueidentifier,EffectiveFrom date,EffectiveTo date,CreatedAt datetimeoffset(7));
            """, connection);
        restore.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        restore.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        restore.Parameters.AddWithValue("@AccountId", alternate.AccountId);
        restore.Parameters.AddWithValue("@Previous", alternate.Previous);
        await restore.ExecuteNonQueryAsync();
    }

    internal static async Task AssertOriginalLinesAsync(ServerSliceFixture fixture, bool receivable, Guid paymentId, int expectedAccountCount = 2)
    {
        // The expectation comes from the actual opening lines, never from a
        // current mapping or a copy of the production account resolver.
        var positions = receivable
            ? "dbo.CustomerPaymentApplications application JOIN dbo.Receivables position ON position.ReceivableId=application.ReceivableId"
            : "dbo.SupplierPaymentApplications application JOIN dbo.Payables position ON position.PayableId=application.PayableId";
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var verify = new SqlCommand($"""
            SELECT opening.AccountId,opening.PartyId,SUM(application.Amount) Amount INTO #Expected
            FROM {positions}
            JOIN dbo.AccountingEntries origin ON origin.EntryId=position.OpeningEntryId
              AND origin.TenantId=@TenantId AND origin.BusinessId=@BusinessId
            JOIN dbo.AccountingEntryLines opening ON opening.EntryId=origin.EntryId
              AND opening.LineNumber=position.OpeningEntryLineNumber
            WHERE application.PaymentId=@PaymentId GROUP BY opening.AccountId,opening.PartyId;
            SELECT COUNT(*) FROM #Expected;
            SELECT COUNT(*) FROM (
              SELECT AccountId,PartyId,Amount FROM #Expected
              EXCEPT SELECT line.AccountId,line.PartyId,SUM(CASE WHEN @Receivable=1 THEN line.Credit ELSE line.Debit END)
              FROM dbo.AccountingEntryLines line JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
              WHERE entry.SourceDocumentId=@PaymentId AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId
              GROUP BY line.AccountId,line.PartyId) difference;
            SELECT COUNT(*),SUM(CASE WHEN entry.DebitTotal<>payment.TotalAmount OR entry.CreditTotal<>payment.TotalAmount THEN 1 ELSE 0 END)
            FROM dbo.AccountingEntries entry JOIN dbo.{(receivable ? "Customer" : "Supplier")}Payments payment
              ON payment.PaymentId=entry.SourceDocumentId
            WHERE entry.SourceDocumentId=@PaymentId AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId;
            DROP TABLE #Expected;
            """, connection);
        verify.Parameters.AddWithValue("@PaymentId", paymentId);
        verify.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        verify.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        verify.Parameters.AddWithValue("@Receivable", receivable);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(expectedAccountCount, reader.GetInt32(0));
        Assert.True(await reader.NextResultAsync() && await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.True(await reader.NextResultAsync() && await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(0, reader.GetInt32(1));
    }
}
