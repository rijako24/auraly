using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Application.Payables;
using Auraly.Application.Receivables;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PortfolioLocalDateTests(ServerSliceFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_nonexistent_payment_day_is_rejected_without_accepting_financial_effects(bool supplier)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var original = new SqlCommand("SELECT TimeZone FROM dbo.Businesses WHERE BusinessId=@Business", connection);
        original.Parameters.AddWithValue("@Business", fixture.BusinessId);
        var originalZone = (string)(await original.ExecuteScalarAsync())!;
        var paymentId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-11-12T12:00:00Z");
        var paidOn = new DateOnly(2011, 12, 30);
        object payment = supplier
            ? new ConfirmSupplierPaymentRequest(paymentId, fixture.BusinessId, fixture.SupplierId, at, "COP", null,
                [new(Guid.NewGuid(), 100m)], [new("Cash", 100m, 100m)], PaidOn: paidOn)
            : new ConfirmCustomerPaymentRequest(paymentId, fixture.BusinessId, Guid.NewGuid(), null, at, "COP", null,
                [new(Guid.NewGuid(), 100m)], [new("Cash", 100m, 100m)], PaidOn: paidOn);
        try
        {
            await SetZoneAsync(connection, "Pacific/Apia");
            using var client = fixture.CreateAdminClient(supplier
                ? PayablesPermissionCodes.RegisterPayment : ReceivablesPermissionCodes.RegisterPayment);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    $"/api/commerce/v1/{(supplier ? "payable" : "receivable")}-payments/confirm")
                { Content = JsonContent.Create(payment) };
                request.Headers.Add("Idempotency-Key", paymentId.ToString("N"));
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Contains("La fecha del pago no existe", await response.Content.ReadAsStringAsync());
            }
            await using var verify = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM dbo.CustomerPayments WHERE PaymentId=@Payment)
                  +(SELECT COUNT(*) FROM dbo.SupplierPayments WHERE PaymentId=@Payment)
                  +(SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Payment)
                  +(SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Payment)
                  +(SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Payment);
                """, connection);
            verify.Parameters.AddWithValue("@Payment", paymentId);
            Assert.Equal(0, Convert.ToInt32(await verify.ExecuteScalarAsync()));
        }
        finally { await SetZoneAsync(connection, originalZone); }
    }

    [Theory]
    [InlineData("America/Bogota", "2026-11-11", 24)]
    [InlineData("Asia/Tokyo", "2026-11-11", 24)]
    [InlineData("America/New_York", "2026-03-08", 23)]
    [InlineData("America/New_York", "2026-11-01", 25)]
    public async Task Registration_filters_share_local_boundaries_and_preserve_legacy_UTC(
        string zoneId, string dayText, int hours)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var day = DateOnly.Parse(dayText);
        var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), zone));
        var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), zone));
        Assert.Equal(hours, (end - start).TotalHours);
        var utcStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        DateTimeOffset[] instants = [start.AddTicks(-1), start, end.AddTicks(-1), end,
            utcStart.AddHours(1), utcStart.AddHours(23)];
        var prefix = "ZONE-" + Guid.NewGuid().ToString("N")[..8] + "-";
        var customerId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using var readZone = new SqlCommand("SELECT TimeZone FROM dbo.Businesses WHERE BusinessId=@Business", connection);
        readZone.Parameters.AddWithValue("@Business", fixture.BusinessId);
        var originalZone = (string)(await readZone.ExecuteScalarAsync())!;
        await SeedAsync(connection, customerId, prefix, instants);
        try
        {
            await SetZoneAsync(connection, zoneId);
            using var scope = fixture.CreateScope();
            var receivables = scope.ServiceProvider.GetRequiredService<IReceivablesStore>();
            var payables = scope.ServiceProvider.GetRequiredService<IPayablesStore>();
            var permissions = new HashSet<string> { ReceivablesPermissionCodes.Read, PayablesPermissionCodes.Read };
            var customerUser = new ReceivablesUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId, permissions);
            var supplierUser = new PayablesUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId, permissions);
            foreach (var version in new[] { 1, 2 })
            {
                var first = version == 1 ? utcStart : start;
                var last = version == 1 ? utcStart.AddDays(1) : end;
                var expected = instants.Count(instant => instant >= first && instant < last);
                var invoiceQuery = new ReceivableQuery(1, 20, prefix, customerId, null, null,
                    From: day, To: day, DateRangeVersion: version);
                var payableQuery = new PayableQuery(1, 20, prefix, fixture.SupplierId, null, null,
                    From: day, To: day, DateRangeVersion: version);
                Assert.Equal(expected, (await receivables.ListAsync(customerUser, invoiceQuery, default)).TotalCount);
                Assert.Equal(expected, (await payables.ListAsync(supplierUser, payableQuery, default)).TotalCount);
                var customers = await receivables.ListCustomersAsync(customerUser,
                    new(1, 20, null, null, customerId, From: day, To: day, DateRangeVersion: version), default);
                Assert.Equal(expected, customers.TotalInvoiceCount);
                var suppliers = await payables.ListSuppliersAsync(supplierUser,
                    new(1, 20, prefix, null, fixture.SupplierId, From: day, To: day, DateRangeVersion: version), default);
                Assert.Equal(expected, suppliers.TotalInvoiceCount);
                using (var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
                {
                    var payments = await receivables.ListPaymentsAsync(customerUser,
                        new(1, 20, prefix, customerId, From: day, To: day, DateRangeVersion: version), default);
                    Assert.Equal(expected, payments.TotalCount);
                    Assert.Equal(version == 2 ? 2 : 1, counter.Count);
                }
                Assert.Equal(expected, (await payables.ListPaymentsAsync(supplierUser,
                    new(1, 20, prefix, fixture.SupplierId, From: day, To: day, DateRangeVersion: version), default)).TotalCount);
                var customerReport = await receivables.ReportAsync(customerUser,
                    new(1, 20, false, day.AddDays(2), customerId, From: day, To: day, Search: prefix,
                        IncludeApplications: false, DateRangeVersion: version), default);
                Assert.Equal(expected, customerReport.TotalInvoiceCount);
                var supplierReport = await payables.ReportAsync(supplierUser,
                    new(1, 20, false, day.AddDays(2), fixture.SupplierId, From: day, To: day, Search: prefix,
                        IncludeApplications: false, DateRangeVersion: version), default);
                Assert.Equal(expected, supplierReport.Items.Count);
            }
            using var client = fixture.CreateAdminClient(ReceivablesPermissionCodes.Read, PayablesPermissionCodes.Read);
            foreach (var resource in new[] { "receivables", "payables" })
            {
                var suffixes = new[] { "", resource == "payables" ? "/suppliers" : "/customers", "-payments",
                    "/report", "/report/export", "/report/print" };
                foreach (var suffix in suffixes)
                {
                    var report = suffix.StartsWith("/report", StringComparison.Ordinal);
                    var path = suffix == "-payments" ? resource[..^1] + suffix : resource + suffix;
                    var endpoint = $"/api/commerce/v1/{path}?page=1&pageSize=20&search={prefix}&from={dayText}&to={dayText}" +
                        (report ? $"&consolidated=false&cutoff={day.AddDays(2):yyyy-MM-dd}" : "");
                    foreach (var version in new[] { 1, 2 })
                    {
                        using var response = await client.GetAsync(endpoint + (version == 1 ? "" : "&dateRangeVersion=2"));
                        var content = await response.Content.ReadAsStringAsync();
                        Assert.True(response.IsSuccessStatusCode, content);
                        if (suffix == "/report/print")
                        {
                            for (var index = 0; index < instants.Length; index++)
                            {
                                var included = instants[index] >= (version == 1 ? utcStart : start) &&
                                    instants[index] < (version == 1 ? utcStart.AddDays(1) : end);
                                Assert.Equal(included, content.Contains(prefix + index, StringComparison.Ordinal));
                            }
                            continue;
                        }
                        using var json = JsonDocument.Parse(content);
                        var expected = instants.Count(instant => instant >= (version == 1 ? utcStart : start) &&
                            instant < (version == 1 ? utcStart.AddDays(1) : end));
                        var directory = suffix is "/suppliers" or "/customers";
                        Assert.Equal(expected, json.RootElement.GetProperty(directory ? "totalInvoiceCount" : "totalCount").GetInt32());
                    }
                    using var invalid = await client.GetAsync(endpoint + "&dateRangeVersion=3");
                    Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
                }
            }
        }
        finally { await SetZoneAsync(connection, originalZone); }
    }

    private async Task SetZoneAsync(SqlConnection connection, string zone)
    {
        await using var command = new SqlCommand("UPDATE dbo.Businesses SET TimeZone=@Zone WHERE BusinessId=@Business AND TenantId=@Tenant", connection);
        command.Parameters.AddWithValue("@Zone", zone);
        command.Parameters.AddWithValue("@Business", fixture.BusinessId);
        command.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedAsync(SqlConnection connection, Guid customerId, string prefix, DateTimeOffset[] instants)
    {
        // Read-model fixtures only: no posting, inventory, or live data is changed.
        await using var seed = new SqlCommand("""
            DECLARE @Party uniqueidentifier=NEWID(),@Site uniqueidentifier=NEWID();
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES(@Party,@Tenant,N'Organization',@Prefix,N'Incomplete',1,@User,SYSDATETIMEOFFSET());
            INSERT dbo.Customers(CustomerId,PartyId,TenantId,RequiresElectronicInvoice,IsActive,CreatedBy,CreatedAt)
            VALUES(@Customer,@Party,@Tenant,0,1,@User,SYSDATETIMEOFFSET());
            INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
            SELECT @Site,@Party,N'PRINCIPAL',N'Principal',CountryId,AdministrativeDivisionId,CityId,AddressLine,1,1,@User,SYSDATETIMEOFFSET()
              FROM dbo.PartySites WHERE PartyId=@SupplierParty AND IsPrimary=1 AND IsActive=1;
            SELECT @Site;
            """, connection);
        seed.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        seed.Parameters.AddWithValue("@Customer", customerId);
        seed.Parameters.AddWithValue("@Prefix", prefix);
        seed.Parameters.AddWithValue("@User", fixture.UserId);
        seed.Parameters.AddWithValue("@SupplierParty", fixture.SupplierPartyId);
        var site = (Guid)(await seed.ExecuteScalarAsync())!;
        await using var readSite = new SqlCommand("SELECT PartySiteId FROM dbo.PartySites WHERE PartyId=@Party AND IsPrimary=1 AND IsActive=1", connection);
        readSite.Parameters.AddWithValue("@Party", fixture.SupplierPartyId);
        var supplierSite = (Guid)(await readSite.ExecuteScalarAsync())!;
        foreach (var supplier in new[] { false, true })
        {
            var position = supplier ? "Payable" : "Receivable";
            var role = supplier ? "Supplier" : "Customer";
            for (var index = 0; index < instants.Length; index++)
            {
                await using var command = new SqlCommand($"""
                    SET XACT_ABORT ON; BEGIN TRANSACTION;
                    DECLARE @Source uniqueidentifier=NEWID(),@Position uniqueidentifier=NEWID(),@Payment uniqueidentifier=NEWID(),
                      @Hash binary(32)=HASHBYTES('SHA2_256',CONVERT(varbinary(max),'[]'));
                    INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
                    VALUES(@Source,N'HistoryVolumeFixture',@Tenant,@Business,N'[]',@Hash,@At,@At,0);
                    INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt,CompletedAt)
                    VALUES(NEWID(),@Tenant,@Business,@Source,N'HistoryVolumeFixture',@Hash,@At,0,N'Posted',1,@At,@At);
                    INSERT dbo.{position}s({position}Id,BusinessId,{role}Id,PartySiteId,SourceDocumentId,SourceDocumentType,DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
                    VALUES(@Position,@Business,@Role,@Site,@Source,N'HistoryVolumeFixture',@Number,N'COP',100,100,DATEADD(day,30,@At),N'Open',@At);
                    INSERT dbo.{position}Transactions({position}TransactionId,{position}Id,TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
                    VALUES(NEWID(),@Position,N'Opening',100,@Source,@At,@At);
                    INSERT dbo.{role}Payments(PaymentId,BusinessId,{role}Id,DocumentSeriesId,DocumentNumber,DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,IdempotencyKey,PayloadHash,PaidAt,CurrencyCode,TotalAmount,Status,ConfirmedByUserId,AcceptedAt)
                    VALUES(@Payment,@Business,@Role,@Series,@Number,N'ZONE',N'ZONE',@Consecutive,@Number,@Hash,@At,N'COP',1,N'Accepted',@User,@At);
                    INSERT dbo.{role}PaymentTenders(PaymentId,LineNumber,MethodCode,Amount) VALUES(@Payment,1,N'Cash',1);
                    INSERT dbo.{role}PaymentApplications(PaymentId,LineNumber,{position}Id,Amount) VALUES(@Payment,1,@Position,1);
                    COMMIT;
                    """, connection);
                command.Parameters.AddWithValue("@Tenant", fixture.TenantId);
                command.Parameters.AddWithValue("@Business", fixture.BusinessId);
                command.Parameters.AddWithValue("@Role", supplier ? fixture.SupplierId : customerId);
                command.Parameters.AddWithValue("@Site", supplier ? supplierSite : site);
                command.Parameters.AddWithValue("@Number", prefix + index);
                command.Parameters.AddWithValue("@At", instants[index]);
                command.Parameters.AddWithValue("@Series", fixture.OnlineDocumentSeriesId);
                command.Parameters.AddWithValue("@Consecutive", index + 1);
                command.Parameters.AddWithValue("@User", fixture.UserId);
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
