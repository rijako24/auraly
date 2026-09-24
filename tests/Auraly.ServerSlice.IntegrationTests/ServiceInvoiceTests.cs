using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Fiscal;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Ubl;
using Auraly.Infrastructure.Persistence;
using Auraly.Platform.Application.Identity.Services;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class ServiceInvoiceTests(ServerSliceFixture fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delivery_reads_the_immutable_snapshot_without_persisting_a_pdf(bool creditOnly)
    {
        var context = await SeedAsync();
        using var client = fixture.CreateAdminClient(ServiceInvoicePermissionCodes.Create, ServiceInvoicePermissionCodes.Issue);
        client.DefaultRequestHeaders.Add("Idempotency-Key", context.IdempotencyKey);
        var due = DateTimeOffset.UtcNow.AddDays(30);
        var credit = creditOnly ? 119_000m : 50_000m;
        using var response = await client.PostAsJsonAsync("/api/commerce/v1/service-invoices/issue",
            new IssueServiceInvoiceRequest(fixture.BusinessId, context.CustomerId,
                [new(context.ServiceId, 1)], "Transfer", "SNAPSHOT-PAYMENT",
                credit, due));
        response.EnsureSuccessStatusCode();
        var issued = (await response.Content.ReadFromJsonAsync<IssuedServiceInvoice>())!;
        await AcceptAtDianAsync(issued.DocumentId);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var query = new SqlCommand("""
            DECLARE @MessageId uniqueidentifier=(SELECT DeliveryOutboxMessageId FROM dbo.FiscalDocuments WHERE DocumentId=@DocumentId);
            EXEC dbo.FiscalInvoiceDeliveryRecipientGet @DocumentId,@MessageId,@TenantId;
            """, connection);
        query.Parameters.AddWithValue("@DocumentId", issued.DocumentId);
        query.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await using (var reader = await query.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.IsDBNull(10));
            Assert.Equal(ServiceInvoiceDocumentTypes.ServiceInvoice, reader.GetString(22));
            var receipt = SalesInvoicePresentationMapper.FromSnapshot(
                reader.GetString(22), reader.GetString(21), "DianAccepted");
            Assert.Equal(issued.DocumentId, receipt.DocumentId);
            Assert.Equal(issued.PayableAmount, receipt.NetPayableAmount);
            Assert.Equal(credit, Assert.Single(receipt.Payments
                .Where(payment => payment.MethodCode == "Credit")).Amount);
            Assert.Equal(issued.PayableAmount,
                receipt.Payments.Sum(payment => payment.CollectedAmount));
            if (creditOnly)
                Assert.DoesNotContain(receipt.Payments,
                    payment => payment.MethodCode != "Credit");
            Assert.False(await reader.ReadAsync());
            Assert.False(await reader.NextResultAsync());
        }
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), $"Delivery projection took {timer.Elapsed}.");
        query.Parameters["@TenantId"].Value = Guid.NewGuid();
        await using (var denied = await query.ExecuteReaderAsync())
            Assert.False(await denied.ReadAsync());
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        var leaseId = Guid.NewGuid();
        var attachedDocument = Encoding.UTF8.GetBytes("<AttachedDocument />");
        var transientPdf = Encoding.UTF8.GetBytes("transient-pdf");
        await using var save = new SqlCommand("""
            DECLARE @MessageId uniqueidentifier=(SELECT DeliveryOutboxMessageId FROM dbo.FiscalDocuments WHERE DocumentId=@DocumentId);
            UPDATE dbo.TenantProvisioningOutboxMessages SET LeaseId=@LeaseId
            WHERE MessageId=@MessageId;
            EXEC dbo.FiscalInvoiceDeliveryArtifactSave
              @DocumentId,@MessageId,@TenantId,@LeaseId,
              @AttachedDocument,@AttachedHash,N'AttachedDocument.xml',
              @TransientPdf,@TransientPdfHash,N'Representacion.pdf';
            SELECT
              (SELECT COUNT(*) FROM dbo.FiscalArtifacts WHERE DocumentId=@DocumentId AND ArtifactType=N'SignedAttachedDocument'),
              (SELECT COUNT(*) FROM dbo.FiscalArtifacts WHERE DocumentId=@DocumentId AND ArtifactType=N'GraphicalRepresentationPdf');
            """, connection, transaction);
        save.Parameters.AddWithValue("@DocumentId", issued.DocumentId);
        save.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        save.Parameters.AddWithValue("@LeaseId", leaseId);
        save.Parameters.AddWithValue("@AttachedDocument", attachedDocument);
        save.Parameters.Add("@AttachedHash", System.Data.SqlDbType.Binary, 32).Value =
            SHA256.HashData(attachedDocument);
        save.Parameters.AddWithValue("@TransientPdf", transientPdf);
        save.Parameters.Add("@TransientPdfHash", System.Data.SqlDbType.Binary, 32).Value =
            SHA256.HashData(transientPdf);
        await using (var saved = await save.ExecuteReaderAsync())
        {
            Assert.True(await saved.ReadAsync());
            Assert.Equal(1, saved.GetInt32(0));
            Assert.Equal(0, saved.GetInt32(1));
        }
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Service_invoice_customer_search_returns_independent_sites_with_and_tokens()
    {
        var context = await SeedAsync();
        var secondSiteId = Guid.NewGuid();
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                INSERT dbo.PartySites(
                  PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,
                  AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
                SELECT @SiteId,site.PartyId,N'SECUNDARIA',N'Sede secundaria',site.CountryId,
                  site.AdministrativeDivisionId,site.CityId,N'Dirección secundaria',0,1,
                  @UserId,SYSDATETIMEOFFSET()
                FROM dbo.Customers customer
                JOIN dbo.PartySites site ON site.PartyId=customer.PartyId AND site.IsPrimary=1
                WHERE customer.CustomerId=@CustomerId;
                """, connection);
            command.Parameters.AddWithValue("@SiteId", secondSiteId);
            command.Parameters.AddWithValue("@UserId", fixture.UserId);
            command.Parameters.AddWithValue("@CustomerId", context.CustomerId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        using var client = fixture.CreateAdminClient(
            ServiceInvoicePermissionCodes.Read,
            ServiceInvoicePermissionCodes.Create,
            ServiceInvoicePermissionCodes.Issue);
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/service-invoices/customers/search",
            new ServiceInvoiceSearchRequest(
                fixture.BusinessId, $"secundaria {context.CustomerIdentification}", 1, 20));
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<ServiceInvoiceCustomerPage>();
        var match = Assert.Single(page!.Items);
        Assert.Equal(secondSiteId, match.PartySiteId);
        Assert.Equal("Sede secundaria", match.PartySiteName);
    }

    [Fact]
    public async Task Credit_service_invoice_preserves_receivable_without_posting_when_accounting_is_disabled()
    {
        var context = await SeedAsync();
        using var client = fixture.CreateAdminClient(
            ServiceInvoicePermissionCodes.Create,
            ServiceInvoicePermissionCodes.Issue);
        client.DefaultRequestHeaders.Add("Idempotency-Key", context.IdempotencyKey);
        var originalAccounting = await SetAccountingDisabledAsync();
        try
        {
            using var response = await client.PostAsJsonAsync(
                "/api/commerce/v1/service-invoices/issue",
                new IssueServiceInvoiceRequest(
                    fixture.BusinessId, context.CustomerId,
                    [new(context.ServiceId, 1)], "Transfer", "SERVICE-CREDIT",
                    CreditAmount: 50_000m,
                    CreditDueDate: DateTimeOffset.UtcNow.AddDays(30)));
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            var issued = (await response.Content
                .ReadFromJsonAsync<IssuedServiceInvoice>())!;

            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var verify = new SqlCommand("""
                SELECT source.AccountingEntryRequired,job.AccountingEntryRequired,job.Status,
                  (SELECT COUNT(*) FROM dbo.Receivables WHERE SourceDocumentId=@DocumentId),
                  (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@DocumentId),
                  (SELECT OutstandingAmount FROM dbo.Receivables WHERE SourceDocumentId=@DocumentId),
                  (SELECT PartySiteId FROM dbo.Receivables WHERE SourceDocumentId=@DocumentId),
                  (SELECT CustomerPartySiteId FROM dbo.SalesDocuments WHERE DocumentId=@DocumentId)
                FROM dbo.AccountingSourceDocuments source
                JOIN dbo.AccountingPostingJobs job
                  ON job.SourceDocumentId=source.SourceDocumentId
                 AND job.SourceDocumentType=source.SourceDocumentType
                WHERE source.SourceDocumentId=@DocumentId;
                """, connection);
            verify.Parameters.AddWithValue("@DocumentId", issued.DocumentId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.False(reader.GetBoolean(0));
            Assert.False(reader.GetBoolean(1));
            Assert.Equal("CommercialEffectsApplied", reader.GetString(2));
            Assert.Equal(1, reader.GetInt32(3));
            Assert.Equal(0, reader.GetInt32(4));
            Assert.Equal(50_000m, reader.GetDecimal(5));
            Assert.Equal(context.CustomerSiteId, reader.GetGuid(6));
            Assert.Equal(context.CustomerSiteId, reader.GetGuid(7));
        }
        finally
        {
            await RestoreAccountingAsync(originalAccounting);
        }
    }

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
        Assert.Equal(0, reader.GetInt32(6));
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
        await deliveryReader.DisposeAsync();

        var deliveryLeaseId = Guid.NewGuid();
        Guid deliveryMessageId;
        await using (var lease = new SqlCommand("""
            UPDATE message
            SET LeaseId=@LeaseId,LeaseExpiresAt=DATEADD(MINUTE,5,SYSDATETIMEOFFSET())
            OUTPUT inserted.MessageId
            FROM dbo.TenantProvisioningOutboxMessages message
            JOIN dbo.FiscalDocuments fiscal
              ON fiscal.DeliveryOutboxMessageId=message.MessageId
            WHERE fiscal.DocumentId=@DocumentId AND message.ProcessedAt IS NULL;
            """, connection))
        {
            lease.Parameters.AddWithValue("@LeaseId", deliveryLeaseId);
            lease.Parameters.AddWithValue("@DocumentId", first.DocumentId);
            deliveryMessageId = (Guid)(await lease.ExecuteScalarAsync())!;
        }

        var attachedDocument = Encoding.UTF8.GetBytes("<AttachedDocument>signed</AttachedDocument>");
        var attachedHash = SHA256.HashData(attachedDocument);
        var graphicalRepresentation = Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF");
        var graphicalRepresentationHash = SHA256.HashData(graphicalRepresentation);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var save = new SqlCommand("""
                EXEC dbo.FiscalInvoiceDeliveryArtifactSave
                  @DocumentId,@MessageId,@TenantId,@LeaseId,
                  @AttachedDocument,@AttachedDocumentHash,@AttachedDocumentFileName,
                  @GraphicalRepresentation,@GraphicalRepresentationHash,
                  @GraphicalRepresentationFileName;
                """, connection);
            save.Parameters.AddWithValue("@DocumentId", first.DocumentId);
            save.Parameters.AddWithValue("@MessageId", deliveryMessageId);
            save.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            save.Parameters.AddWithValue("@LeaseId", deliveryLeaseId);
            save.Parameters.AddWithValue("@AttachedDocument", attachedDocument);
            save.Parameters.AddWithValue("@AttachedDocumentHash", attachedHash);
            save.Parameters.AddWithValue("@AttachedDocumentFileName", "AttachedDocument-test.xml");
            save.Parameters.AddWithValue("@GraphicalRepresentation", graphicalRepresentation);
            save.Parameters.AddWithValue("@GraphicalRepresentationHash", graphicalRepresentationHash);
            save.Parameters.AddWithValue("@GraphicalRepresentationFileName", "RepresentacionGrafica-test.pdf");
            await save.ExecuteNonQueryAsync();
        }

        await using var storedArtifact = new SqlCommand("""
            SELECT
              (SELECT COUNT(*) FROM dbo.FiscalArtifacts
               WHERE DocumentId=@DocumentId AND ArtifactType=N'SignedAttachedDocument'),
              (SELECT COUNT(*) FROM dbo.FiscalArtifacts
               WHERE DocumentId=@DocumentId AND ArtifactType=N'GraphicalRepresentationPdf');
            """, connection);
        storedArtifact.Parameters.AddWithValue("@DocumentId", first.DocumentId);
        await using var artifactReader = await storedArtifact.ExecuteReaderAsync();
        Assert.True(await artifactReader.ReadAsync());
        Assert.Equal(1, artifactReader.GetInt32(0));
        Assert.Equal(0, artifactReader.GetInt32(1));
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
            new DianInvoiceUblBuilder(), new DianSupportDocumentUblBuilder(),
            new DianCreditNoteUblBuilder(),
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
        var customerSiteId = Guid.NewGuid();
        var serviceSeriesId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var usageId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var customerIdentification = $"90{Random.Shared.Next(10000000, 99999999)}";
        var customerVerificationDigit = TenantProvisioningRequestValidator
            .CalculateNitVerificationDigit(customerIdentification).ToString();
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
               @Identification,@Identification,@VerificationDigit,N'Cliente servicio',N'Cliente servicio SAS',
               N'Complete',1,@UserId,@Now
            FROM dbo.Countries country WHERE country.Code='CO';
            INSERT dbo.Customers
              (CustomerId,PartyId,BusinessId,RequiresElectronicInvoice,IsActive,CreatedBy,CreatedAt)
            VALUES(@CustomerId,@CustomerPartyId,@BusinessId,1,1,@UserId,@Now);
            INSERT dbo.PartySites(
              PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,
              AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
            SELECT TOP(1) @CustomerSiteId,@CustomerPartyId,N'PRINCIPAL',N'Sede principal',
              country.CountryId,division.AdministrativeDivisionId,city.CityId,
              N'Dirección principal',1,1,@UserId,@Now
            FROM dbo.Countries country
            JOIN dbo.AdministrativeDivisions division ON division.CountryId=country.CountryId
            JOIN dbo.Cities city ON city.AdministrativeDivisionId=division.AdministrativeDivisionId
            WHERE country.IsActive=1 AND division.IsActive=1 AND city.IsActive=1;
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
        command.Parameters.AddWithValue("@CustomerSiteId", customerSiteId);
        command.Parameters.AddWithValue("@ServiceSeriesId", serviceSeriesId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@Code", $"SVC-{serviceId:N}");
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        command.Parameters.AddWithValue("@Identification", customerIdentification);
        command.Parameters.AddWithValue("@VerificationDigit", customerVerificationDigit);
        command.Parameters.AddWithValue("@Email", $"service-{customerId:N}@auraly.test");
        command.Parameters.AddWithValue("@SubscriptionId", subscriptionId);
        command.Parameters.AddWithValue("@UsageId", usageId);
        command.Parameters.AddWithValue("@Now", now);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new(reader.GetGuid(0), serviceId, reader.GetGuid(1), Guid.NewGuid().ToString("N"),
            customerSiteId, customerIdentification);
    }

    private async Task<AccountingSettingsState> SetAccountingDisabledAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var read = new SqlCommand("""
            SELECT Status,FunctionalCurrencyCode,EffectiveFrom,OpeningBalanceMode,
              ActivationRequestedAt,ActivationRequestedByUserId,ActivatedAt,ActivatedByUserId
            FROM dbo.AccountingTenantSettings WITH(UPDLOCK,HOLDLOCK)
            WHERE TenantId=@TenantId;
            """, connection, (SqlTransaction)transaction);
        read.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        await using var reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var state = new AccountingSettingsState(
            reader.GetString(0), reader.GetString(1),
            reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2)),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7));
        await reader.DisposeAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.AccountingTenantSettings
            SET Status=N'Disabled',EffectiveFrom=NULL,OpeningBalanceMode=NULL,
                ActivationRequestedAt=NULL,ActivationRequestedByUserId=NULL,
                ActivatedAt=NULL,ActivatedByUserId=NULL,UpdatedAt=SYSDATETIMEOFFSET()
            WHERE TenantId=@TenantId;
            """, connection, (SqlTransaction)transaction);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        await transaction.CommitAsync();
        return state;
    }

    private async Task RestoreAccountingAsync(AccountingSettingsState state)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.AccountingTenantSettings
            SET Status=@Status,FunctionalCurrencyCode=@Currency,
                EffectiveFrom=@EffectiveFrom,OpeningBalanceMode=@OpeningMode,
                ActivationRequestedAt=@RequestedAt,
                ActivationRequestedByUserId=@RequestedBy,
                ActivatedAt=@ActivatedAt,ActivatedByUserId=@ActivatedBy,
                UpdatedAt=SYSDATETIMEOFFSET()
            WHERE TenantId=@TenantId;
            """;
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@Status", state.Status);
        command.Parameters.AddWithValue("@Currency", state.FunctionalCurrencyCode);
        command.Parameters.AddWithValue("@EffectiveFrom",
            state.EffectiveFrom?.ToDateTime(TimeOnly.MinValue) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@OpeningMode", state.OpeningBalanceMode ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@RequestedAt", state.ActivationRequestedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@RequestedBy", state.ActivationRequestedByUserId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ActivatedAt", state.ActivatedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ActivatedBy", state.ActivatedByUserId ?? (object)DBNull.Value);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private sealed record AccountingSettingsState(
        string Status,
        string FunctionalCurrencyCode,
        DateOnly? EffectiveFrom,
        string? OpeningBalanceMode,
        DateTimeOffset? ActivationRequestedAt,
        Guid? ActivationRequestedByUserId,
        DateTimeOffset? ActivatedAt,
        Guid? ActivatedByUserId);

    private sealed record Context(
        Guid CustomerId,
        Guid ServiceId,
        Guid SubscriptionId,
        string IdempotencyKey,
        Guid CustomerSiteId,
        string CustomerIdentification);

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
        public Task<DianSubmissionResult> GetStatusAsync(
            DianSubmissionRequest request, CancellationToken cancellationToken = default) => Accepted();

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
