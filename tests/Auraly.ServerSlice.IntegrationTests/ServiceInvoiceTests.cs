using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Fiscal;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Ubl;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class ServiceInvoiceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Online_service_invoice_is_idempotent_and_has_no_inventory_effects()
    {
        var context = await SeedAsync();
        using var client = fixture.CreateAdminClient(
            ServiceInvoicePermissionCodes.Read,
            ServiceInvoicePermissionCodes.Create,
            ServiceInvoicePermissionCodes.Issue,
            ServiceInvoicePermissionCodes.Discount,
            ServiceInvoicePermissionCodes.Print);
        var request = new IssueServiceInvoiceRequest(
            fixture.BusinessId,
            context.CustomerId,
            [new(context.ServiceId, 2, "Consultoría online", null, "Percentage", 10)],
            "Transfer",
            "TRX-TEST-1");
        client.DefaultRequestHeaders.Add("Idempotency-Key", context.IdempotencyKey);

        using var firstResponse = await client.PostAsJsonAsync(
            "/api/commerce/v1/service-invoices/issue", request);
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        Assert.True(firstResponse.IsSuccessStatusCode, firstBody);
        var first = (await firstResponse.Content.ReadFromJsonAsync<IssuedServiceInvoice>())!;
        using var replayResponse = await client.PostAsJsonAsync(
            "/api/commerce/v1/service-invoices/issue", request);
        replayResponse.EnsureSuccessStatusCode();
        var replay = (await replayResponse.Content.ReadFromJsonAsync<IssuedServiceInvoice>())!;

        Assert.Equal(first.DocumentId, replay.DocumentId);
        Assert.True(replay.IsReplay);
        using var historyResponse = await client.PostAsJsonAsync(
            "/api/commerce/v1/service-invoices/history/search",
            new ServiceInvoiceHistoryRequest(fixture.BusinessId, first.FiscalNumber));
        historyResponse.EnsureSuccessStatusCode();
        var history = (await historyResponse.Content
            .ReadFromJsonAsync<ServiceInvoiceHistoryPage>())!;
        Assert.Contains(history.Items, value => value.DocumentId == first.DocumentId);
        var detail = await client.GetFromJsonAsync<ServiceInvoiceDetail>(
            $"/api/commerce/v1/service-invoices/{first.DocumentId:D}?businessId={fixture.BusinessId:D}");
        Assert.NotNull(detail);
        Assert.Single(detail.Lines);
        Assert.Equal(2, detail.Lines[0].Quantity);
        Assert.Single(detail.Payments);
        var printable = await client.GetFromJsonAsync<ServiceInvoiceDetail>(
            $"/api/commerce/v1/service-invoices/{first.DocumentId:D}/print?businessId={fixture.BusinessId:D}");
        Assert.Equal(first.DocumentId, printable!.DocumentId);
        using var qr = await client.GetAsync(
            $"/api/commerce/v1/service-invoices/{first.DocumentId:D}/qr?businessId={fixture.BusinessId:D}");
        qr.EnsureSuccessStatusCode();
        Assert.Equal("image/svg+xml; charset=utf-8",
            qr.Content.Headers.ContentType?.ToString());
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT
              (SELECT COUNT(*) FROM dbo.SalesDocuments
               WHERE DocumentId=@DocumentId AND DocumentType=N'ServiceInvoice'
                 AND SourceMode=N'Online' AND WarehouseId IS NULL AND DeviceId IS NULL
                 AND WorkSessionId IS NULL),
              (SELECT COUNT(*) FROM sales.SalesDocumentServiceLines WHERE DocumentId=@DocumentId),
              (SELECT COUNT(*) FROM dbo.SalesDocumentLines WHERE DocumentId=@DocumentId),
              (SELECT COUNT(*) FROM dbo.DocumentProcessingJobs WHERE DocumentId=@DocumentId),
              (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@DocumentId),
              (SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@DocumentId),
              (SELECT COUNT(*) FROM reporting.SalesReportingJobs WHERE SourceDocumentId=@DocumentId),
              (SELECT COUNT(*) FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@DocumentId),
              (SELECT DianDocumentsUsed FROM billing.TenantSubscriptionUsagePeriods
               WHERE TenantSubscriptionId=@SubscriptionId);
            """, connection);
        command.Parameters.AddWithValue("@DocumentId", first.DocumentId);
        command.Parameters.AddWithValue("@SubscriptionId", context.SubscriptionId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal(0, reader.GetInt32(2));
        Assert.Equal(0, reader.GetInt32(3));
        Assert.Equal(0, reader.GetInt32(4));
        Assert.Equal(1, reader.GetInt32(5));
        Assert.Equal(1, reader.GetInt32(6));
        Assert.Equal(1, reader.GetInt32(7));
        Assert.Equal(1, reader.GetInt32(8));
        await reader.DisposeAsync();

        await AcceptAtDianAsync(first.DocumentId);
        await using var delivery = new SqlCommand("""
            SELECT COUNT(*),MAX(fiscal.DeliveryEmail),
                   MAX(CASE WHEN outbox.ProcessedAt IS NULL THEN 1 ELSE 0 END)
            FROM dbo.FiscalDocuments fiscal
            JOIN dbo.TenantProvisioningOutboxMessages outbox
              ON outbox.MessageId=fiscal.DeliveryOutboxMessageId
            WHERE fiscal.DocumentId=@DocumentId
              AND outbox.Type=N'FiscalInvoiceDelivery';
            """, connection);
        delivery.Parameters.AddWithValue("@DocumentId", first.DocumentId);
        await using var deliveryReader = await delivery.ExecuteReaderAsync();
        Assert.True(await deliveryReader.ReadAsync());
        Assert.Equal(1, deliveryReader.GetInt32(0));
        Assert.Equal($"service-{context.CustomerId:N}@auraly.test",
            deliveryReader.GetString(1));
        Assert.Equal(1, deliveryReader.GetInt32(2));
    }

    [Fact]
    public async Task Reusing_issue_key_with_different_content_is_rejected()
    {
        var context = await SeedAsync();
        using var client = fixture.CreateAdminClient(
            ServiceInvoicePermissionCodes.Create,
            ServiceInvoicePermissionCodes.Issue);
        client.DefaultRequestHeaders.Add("Idempotency-Key", context.IdempotencyKey);
        var first = new IssueServiceInvoiceRequest(
            fixture.BusinessId, context.CustomerId,
            [new(context.ServiceId, 1)], "Transfer");
        using var firstResponse = await client.PostAsJsonAsync(
            "/api/commerce/v1/service-invoices/issue", first);
        firstResponse.EnsureSuccessStatusCode();

        using var conflict = await client.PostAsJsonAsync(
            "/api/commerce/v1/service-invoices/issue",
            first with { Lines = [new(context.ServiceId, 2)] });

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Dian_acceptance_without_customer_email_does_not_queue_future_delivery()
    {
        var context = await SeedAsync();
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var removeEmail = new SqlCommand("""
                DELETE contact
                FROM dbo.PartyContacts contact
                JOIN dbo.Customers customer ON customer.PartyId=contact.PartyId
                WHERE customer.CustomerId=@CustomerId AND contact.ContactType=N'Email';
                """, connection);
            removeEmail.Parameters.AddWithValue("@CustomerId", context.CustomerId);
            Assert.Equal(1, await removeEmail.ExecuteNonQueryAsync());
        }
        using var client = fixture.CreateAdminClient(
            ServiceInvoicePermissionCodes.Create,
            ServiceInvoicePermissionCodes.Issue);
        client.DefaultRequestHeaders.Add("Idempotency-Key", context.IdempotencyKey);
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/service-invoices/issue",
            new IssueServiceInvoiceRequest(fixture.BusinessId, context.CustomerId,
                [new(context.ServiceId, 1)], "Transfer"));
        response.EnsureSuccessStatusCode();
        var issued = (await response.Content.ReadFromJsonAsync<IssuedServiceInvoice>())!;

        await AcceptAtDianAsync(issued.DocumentId);

        await using var verify = new SqlConnection(fixture.ConnectionString);
        await verify.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.TenantProvisioningOutboxMessages message
            JOIN dbo.FiscalDocuments fiscal
              ON fiscal.DeliveryOutboxMessageId=message.MessageId
            WHERE fiscal.DocumentId=@DocumentId AND message.Type=N'FiscalInvoiceDelivery';
            """, verify);
        command.Parameters.AddWithValue("@DocumentId", issued.DocumentId);
        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    private async Task AcceptAtDianAsync(Guid documentId)
    {
        var connections = new SqlServerConnectionFactory(
            new AuralySqlConnectionSource(fixture.ConnectionString));
        var ids = new TestIds();
        var generatedAt = DateTimeOffset.UtcNow.AddMinutes(1);
        var generator = new FiscalGenerationWorker(
            new SqlFiscalGenerationWorkStore(connections, ids), new TestPin(),
            new DianInvoiceUblBuilder(), new DianCreditNoteUblBuilder(),
            new DianDebitNoteUblBuilder(), new DianSchemaValidator(),
            new DianPayrollXmlBuilder(), new DianPayrollSchemaValidator(),
            new TestSigner(), new FixedTimeProvider(generatedAt));
        Assert.True(await generator.ProcessAsync(
            fixture.BusinessId, documentId, $"service-generator-{documentId:N}"),
            await FiscalProcessStateAsync(documentId));
        var transport = new AcceptedTransport(documentId);
        var worker = new FiscalSubmissionWorker(
            new SqlFiscalSubmissionWorkStore(connections, ids),
            transport, transport, new FiscalSubmissionPackageBuilder(),
            new FixedTimeProvider(generatedAt.AddSeconds(1)));
        Assert.True((await worker.ProcessAsync(
            fixture.BusinessId, documentId, $"service-submitter-{documentId:N}")).WorkFound);
    }

    private async Task<string> FiscalProcessStateAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CONCAT(N'Estado=',Status,N'; Código=',COALESCE(LastErrorCode,N'<null>'),
              N'; Error=',COALESCE(LastErrorMessage,N'<null>'))
            FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@DocumentId;
            """;
        command.Parameters.AddWithValue("@DocumentId", documentId);
        return (string?)await command.ExecuteScalarAsync() ?? "No existe el proceso fiscal.";
    }

    private async Task<Context> SeedAsync()
    {
        var serviceId = Guid.NewGuid();
        var taxProfileId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var customerPartyId = Guid.NewGuid();
        var serviceSeriesId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var usageId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.TaxProfiles
              (TaxProfileId,BusinessId,Code,DianTaxCode,Name,Rate,IsActive,CreatedAt)
            VALUES(@TaxProfileId,@BusinessId,@TaxCode,N'01',N'IVA 19%',19,1,@Now);
            INSERT dbo.Parties
              (PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,
               Identification,NormalizedIdentification,VerificationDigit,DisplayName,LegalName,
               CompletionStatus,IsActive,CreatedBy,CreatedAt)
            SELECT @CustomerPartyId,@TenantId,N'Organization',country.CountryId,N'31',
               @Identification,@Identification,N'1',N'Cliente servicio',N'Cliente servicio SAS',
               N'Complete',1,@UserId,@Now
            FROM dbo.Countries country WHERE country.Code='CO';
            INSERT dbo.Customers
              (CustomerId,PartyId,BusinessId,RequiresElectronicInvoice,IsActive,CreatedBy,CreatedAt)
            VALUES(@CustomerId,@CustomerPartyId,@BusinessId,1,1,@UserId,@Now);
            INSERT dbo.PartyContacts
              (PartyContactId,PartyId,ContactType,Value,NormalizedValue,IsPrimary,IsActive,CreatedAt)
            VALUES(NEWID(),@CustomerPartyId,N'Email',@Email,UPPER(@Email),1,1,@Now);
            IF NOT EXISTS(SELECT 1 FROM dbo.DocumentSeries
                          WHERE BusinessId=@BusinessId AND DocumentType=N'ServiceInvoice'
                            AND DeviceId IS NULL AND IsActive=1)
              INSERT dbo.DocumentSeries
                (DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,Padding,
                 RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
              VALUES(@ServiceSeriesId,@BusinessId,NULL,N'ServiceInvoice',N'FSV',N'00',8,
                 1,99999999,0,1,@Now);
            INSERT billing.BillableServices
              (BillableServiceId,BusinessId,Code,Name,Description,UnitLabel,UblUnitCode,
               UnitSize,CurrencyCode,UnitPrice,SalesTaxProfileId,IsActive,CreatedAt,UpdatedAt)
            VALUES(@ServiceId,@BusinessId,@Code,N'Consultoría online',N'Servicio sin inventario',
               N'hora',N'94',1,'COP',100000,@TaxProfileId,1,@Now,@Now);
            IF NOT EXISTS(SELECT 1 FROM billing.TenantSubscriptions WHERE TenantId=@TenantId)
            BEGIN
              INSERT billing.TenantSubscriptions
                (TenantSubscriptionId,TenantId,TenantCommercialPlanId,BillingCustomerId,
                 BillingPeriod,Status,CurrentPeriodStart,CurrentPeriodEnd,BillingAnchorDay,
                 FullUserLimit,SellerUserLimit,PosDeviceLimit,DianDocumentMonthlyLimit,
                 PayrollEmployeeLimit,CreatedAt,UpdatedAt)
              VALUES(@SubscriptionId,@TenantId,'11000000-0000-0000-0000-000000000000',
                 @CustomerId,N'Monthly',N'Active',DATEADD(day,-1,@Now),DATEADD(month,1,@Now),
                 DAY(@Now),10,10,10,100,100,@Now,@Now);
              INSERT billing.TenantSubscriptionUsagePeriods
                (TenantSubscriptionUsagePeriodId,TenantSubscriptionId,PeriodStart,PeriodEnd,
                 DianDocumentsUsed,CreatedAt,UpdatedAt)
              VALUES(@UsageId,@SubscriptionId,DATEADD(day,-1,@Now),DATEADD(month,1,@Now),0,@Now,@Now);
            END
            ELSE
            BEGIN
              SELECT @SubscriptionId=TenantSubscriptionId FROM billing.TenantSubscriptions
              WHERE TenantId=@TenantId;
              UPDATE billing.TenantSubscriptionUsagePeriods SET DianDocumentsUsed=0,UpdatedAt=@Now
              WHERE TenantSubscriptionId=@SubscriptionId AND PeriodStart<=@Now AND PeriodEnd>@Now;
            END
            SELECT @CustomerId,@SubscriptionId;
            """, connection);
        command.Parameters.AddWithValue("@ServiceId", serviceId);
        command.Parameters.AddWithValue("@TaxProfileId", taxProfileId);
        command.Parameters.AddWithValue("@TaxCode", $"IVA19-{taxProfileId:N}"[..32]);
        command.Parameters.AddWithValue("@CustomerId", customerId);
        command.Parameters.AddWithValue("@CustomerPartyId", customerPartyId);
        command.Parameters.AddWithValue("@ServiceSeriesId", serviceSeriesId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@Code", $"SVC-{serviceId:N}");
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        command.Parameters.AddWithValue("@Identification", $"90{Random.Shared.Next(10000000, 99999999)}");
        command.Parameters.AddWithValue("@Email", $"service-{customerId:N}@auraly.test");
        command.Parameters.AddWithValue("@SubscriptionId", subscriptionId);
        command.Parameters.AddWithValue("@UsageId", usageId);
        command.Parameters.AddWithValue("@Now", now);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new(reader.GetGuid(0), serviceId, reader.GetGuid(1), Guid.NewGuid().ToString("N"));
    }

    private sealed record Context(
        Guid CustomerId,
        Guid ServiceId,
        Guid SubscriptionId,
        string IdempotencyKey);

    private sealed class TestIds : IAuralyIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }

    private sealed class TestPin : IFiscalSoftwarePinProvider
    {
        public Task<string> ResolveAsync(Guid businessId, string secretReference,
            CancellationToken cancellationToken) => Task.FromResult("test-pin");
    }

    private sealed class TestSigner : IFiscalXmlSigner
    {
        public Task<FiscalSigningResult> SignAsync(FiscalSigningRequest request,
            CancellationToken cancellationToken = default)
        {
            var hash = Convert.ToHexString(SHA256.HashData(request.UnsignedXml))
                .ToLowerInvariant();
            return Task.FromResult(new FiscalSigningResult(
                request.UnsignedXml, hash, "TEST", request.SigningTime));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AcceptedTransport(Guid documentId) :
        IDianHabilitationTransport, IDianProductionTransport
    {
        private Task<DianSubmissionResult> Accepted() => Task.FromResult(
            new DianSubmissionResult(DianSubmissionDisposition.Accepted,
                $"track-service-{documentId:N}", "00", "Accepted",
                Encoding.UTF8.GetBytes("<ApplicationResponse />"),
                Encoding.UTF8.GetBytes("accepted"), true));

        public Task<DianSubmissionResult> SubmitTestSetAsync(
            DianSubmissionRequest request, CancellationToken cancellationToken = default) => Accepted();
        public Task<DianSubmissionResult> GetStatusZipAsync(
            DianSubmissionRequest request, CancellationToken cancellationToken = default) => Accepted();
        public Task<DianSubmissionResult> SubmitBillSyncAsync(
            DianSubmissionRequest request, CancellationToken cancellationToken = default) => Accepted();
        public Task<DianSubmissionResult> SubmitPayrollSyncAsync(
            DianSubmissionRequest request, CancellationToken cancellationToken = default) => Accepted();
    }
}
