using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PortfolioManualAdvanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_business_payment_day_keeps_its_accounting_year_and_replays_without_resolving_the_zone_again(bool supplier)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
            PayablesPermissionCodes.RegisterPayment, ReceivablesPermissionCodes.RegisterPayment);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null))
            defaults.EnsureSuccessStatusCode();
        using (var activation = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared")))
            activation.EnsureSuccessStatusCode();
        var party = supplier ? fixture.SupplierPartyId : await CreateCustomerPartyAsync(true);
        var control = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            supplier ? "2297" : "1397", supplier ? "Liability" : "Asset", supplier ? "Payable" : "Receivable",
            supplier ? "Credit" : "Debit");
        var counterpart = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            supplier ? "5197" : "4197", supplier ? "Expense" : "Revenue", "General", supplier ? "Debit" : "Credit");
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using var command = new SqlCommand("SELECT TimeZone FROM dbo.Businesses WHERE BusinessId=@Business AND TenantId=@Tenant", connection);
        command.Parameters.AddWithValue("@Business", fixture.BusinessId);
        command.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        var originalZone = (string)(await command.ExecuteScalarAsync())!;
        command.Parameters.AddWithValue("@PaymentType", supplier ? "PayablePayment" : "ReceivablePayment");
        command.Parameters.AddWithValue("@Prefix", AuralyDocumentTypes.DefaultPrefix((string)command.Parameters["@PaymentType"].Value));
        command.CommandText = """
            IF NOT EXISTS(SELECT 1 FROM dbo.DocumentSeries WHERE BusinessId=@Business AND DocumentType=@PaymentType AND IsActive=1)
              INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
                Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
              VALUES(NEWID(),@Business,NULL,@PaymentType,@Prefix,N'00',8,1,99999999,0,1,SYSDATETIMEOFFSET());
            """;
        await command.ExecuteNonQueryAsync();
        command.Parameters.AddWithValue("@Party", party);
        command.CommandText = supplier
            ? "SELECT SupplierId FROM dbo.Suppliers WHERE PartyId=@Party AND TenantId=@Tenant"
            : "SELECT CustomerId FROM dbo.Customers WHERE PartyId=@Party AND TenantId=@Tenant";
        var role = (Guid)(await command.ExecuteScalarAsync())!;
        try
        {
            await SetZone("Pacific/Kiritimati");
            var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(14));
            var opening = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
                at, "MANUAL_VOUCHER", "Obligación en el inicio del año local", null, [
                    new(control.AccountId, party, null, "Obligación", supplier ? 0m : 100m, supplier ? 100m : 0m,
                        Reference: "CALENDARIO-ANUAL", LineId: Guid.NewGuid(), DueDate: at.AddDays(30)),
                    new(counterpart.AccountId, party, null, "Contrapartida", supplier ? 100m : 0m, supplier ? 0m : 100m,
                        LineId: Guid.NewGuid())], null, null, MainPartyId: party, PayloadVersion: 4, VoucherTypeCode: "Accounting");
            var path = $"/api/commerce/v1/accounting/manual/drafts/{opening.DocumentId}";
            using var saved = await client.PutAsJsonAsync(path, opening);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var draft = (await saved.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            using var sent = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(draft.RowVersion));
            Assert.True(sent.IsSuccessStatusCode, await sent.Content.ReadAsStringAsync());
            var opened = (await sent.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            Assert.Equal("Posted", opened.Status);
            var position = Assert.Single(opened.FinancialEffects!.Positions).PositionId;
            var paymentId = Guid.NewGuid();
            object payment = supplier
                ? new ConfirmSupplierPaymentRequest(paymentId, fixture.BusinessId, role, at, "COP", null,
                    [new(position, 100m)], [new("Cash", 100m, 100m)], PaidOn: new(2026, 1, 1))
                : new ConfirmCustomerPaymentRequest(paymentId, fixture.BusinessId, role, null, at, "COP", null,
                    [new(position, 100m)], [new("Cash", 100m, 100m)], PaidOn: new(2026, 1, 1));
            for (var replay = 0; replay < 2; replay++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    $"/api/commerce/v1/{(supplier ? "payable" : "receivable")}-payments/confirm")
                { Content = JsonContent.Create(payment) };
                request.Headers.Add("Idempotency-Key", paymentId.ToString("N"));
                using var response = await client.SendAsync(request);
                Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
                using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(replay == 1, result.RootElement.GetProperty("idempotentReplay").GetBoolean());
                var returnedAt = result.RootElement.GetProperty("paidAt").GetDateTimeOffset();
                Assert.Equal(TimeSpan.Zero, returnedAt.Offset);
                Assert.Equal(new DateTimeOffset(2025, 12, 31, 22, 0, 0, TimeSpan.Zero), returnedAt);
                command.Parameters.Clear();
                command.Parameters.AddWithValue("@Business", fixture.BusinessId);
                command.Parameters.AddWithValue("@Tenant", fixture.TenantId);
                command.Parameters.AddWithValue("@Payment", paymentId);
                command.CommandText = """
                    SELECT job.Status,job.AccountingEntryRequired,CONVERT(date,job.OccurredAt),
                      (SELECT COUNT(*) FROM dbo.AccountingEntries entry WHERE entry.SourceDocumentId=@Payment),
                      (SELECT COUNT(*) FROM dbo.AccountingEntries entry WHERE entry.SourceDocumentId=@Payment AND entry.DebitTotal=100 AND entry.CreditTotal=100),
                      (SELECT COUNT(*) FROM dbo.AccountingPeriods period JOIN dbo.AccountingEntries entry ON entry.PeriodId=period.PeriodId
                        WHERE entry.SourceDocumentId=@Payment AND period.StartsOn<='20260101' AND period.EndsOn>='20260101')
                    FROM dbo.AccountingPostingJobs job WHERE job.SourceDocumentId=@Payment AND job.BusinessId=@Business AND job.TenantId=@Tenant;
                    """;
                await using (var reader = await command.ExecuteReaderAsync())
                {
                    Assert.True(await reader.ReadAsync());
                    Assert.Equal("Posted", reader.GetString(0));
                    Assert.True(reader.GetBoolean(1));
                    Assert.Equal(new DateTime(2026, 1, 1), reader.GetDateTime(2));
                    Assert.Equal(1, reader.GetInt32(3));
                    Assert.Equal(1, reader.GetInt32(4));
                    Assert.Equal(1, reader.GetInt32(5));
                }
                await SetZone("Etc/UTC");
            }
        }
        finally { await SetZone(originalZone); }

        async Task SetZone(string zone)
        {
            command.CommandText = "UPDATE dbo.Businesses SET TimeZone=@Zone WHERE BusinessId=@Business AND TenantId=@Tenant";
            if (command.Parameters.Contains("@Zone")) command.Parameters["@Zone"].Value = zone;
            else command.Parameters.AddWithValue("@Zone", zone);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
    }
}
