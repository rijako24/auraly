using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Auraly.Application.Fiscal;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Ubl;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Fiscal")]
public sealed class FiscalGenerationSqlTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Invoice_does_not_enter_DIAN_generation_before_operational_processing_completes()
    {
        var request = WithUblSnapshot(fixture.CreateValidRequest(8901));
        using var client = fixture.CreateClient();
        using var response = await client.SendAsync(fixture.CreateUploadMessage(request));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                UPDATE dbo.SalesDocuments SET ProcessingStatus=N'Received'
                WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId;
                """, connection);
            command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        var connections = new SqlServerConnectionFactory(
            new AuralySqlConnectionSource(fixture.ConnectionString));
        var worker = CreateWorker(
            new SqlFiscalGenerationWorkStore(connections, new TestIds()),
            new FixedTimeProvider(DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.False(await worker.ProcessAsync(fixture.BusinessId, request.DocumentId, "premature-invoice"));
        Assert.Equal(FiscalDocumentStatusCodes.PendingGeneration,
            await ScalarStringAsync("SELECT Status FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@DocumentId", request.DocumentId));
        Assert.Equal(0, await ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.FiscalArtifacts WHERE DocumentId=@DocumentId", request.DocumentId));

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                UPDATE dbo.SalesDocuments SET ProcessingStatus=N'Completed'
                WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId;
                """, connection);
            command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        Assert.True(await worker.ProcessAsync(fixture.BusinessId, request.DocumentId, "completed-invoice"));
    }

    [Fact]
    public async Task Snapshot_is_leased_once_and_persisted_without_reading_changed_master_values()
    {
        var request = WithUblSnapshot(fixture.CreateValidRequest(901));
        using var client = fixture.CreateClient();
        using var response = await client.SendAsync(fixture.CreateUploadMessage(request));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await ChangeMasterNamesAsync();

        var connections = new SqlServerConnectionFactory(
            new AuralySqlConnectionSource(fixture.ConnectionString));
        var store = new SqlFiscalGenerationWorkStore(connections, new TestIds());
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow.AddMinutes(5));
        var first = CreateWorker(store, clock);
        var second = CreateWorker(store, clock);
        var results = await Task.WhenAll(
            first.ProcessAsync(fixture.BusinessId, request.DocumentId, "worker-one"),
            second.ProcessAsync(
                fixture.BusinessId,
                request.DocumentId, "worker-two"));

        Assert.True(results.Count(result => result) == 1,
            $"Se esperaba un único generador fiscal exitoso. {await FiscalProcessStateAsync(request.DocumentId)}");
        Assert.Single(results.Where(result => !result));
        Assert.Equal(FiscalDocumentStatusCodes.PendingSubmission,
            await ScalarStringAsync("SELECT Status FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@DocumentId", request.DocumentId));
        Assert.Equal(2, await ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.FiscalArtifacts WHERE DocumentId=@DocumentId", request.DocumentId));
        var xml = Encoding.UTF8.GetString(await ArtifactAsync(request.DocumentId, FiscalArtifactTypeCodes.UnsignedXml));
        Assert.Contains("EMISOR HISTORICO", xml, StringComparison.Ordinal);
        Assert.Contains("CLIENTE HISTORICO", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("MAESTRO CAMBIADO", xml, StringComparison.Ordinal);
        var signedXml = await ArtifactAsync(request.DocumentId, FiscalArtifactTypeCodes.SignedXml);
        var graphical = new DianInvoicePdfRenderer().RenderHtml(signedXml);
        Assert.Contains("Letter portrait", graphical);
        Assert.Contains("data-auraly-report-version=\"3\"", graphical);
        Assert.Contains("EMISOR HISTORICO", graphical);
        Assert.Contains("CLIENTE HISTORICO", graphical);
        Assert.DoesNotContain("MAESTRO CAMBIADO", graphical);
        Assert.Contains("alt=\"QR DIAN\"", graphical);
    }

    [Fact]
    public async Task Signed_invoice_is_submitted_queried_and_accepted_exactly_once()
    {
        var request = WithUblSnapshot(fixture.CreateValidRequest(902));
        using var client = fixture.CreateClient();
        using var response = await client.SendAsync(fixture.CreateUploadMessage(request));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var connections = new SqlServerConnectionFactory(
            new AuralySqlConnectionSource(fixture.ConnectionString));
        var ids = new TestIds();
        var generatedAt = DateTimeOffset.UtcNow.AddMinutes(5);
        Assert.True(await CreateWorker(
            new SqlFiscalGenerationWorkStore(connections, ids),
            new FixedTimeProvider(generatedAt)).ProcessAsync(
                fixture.BusinessId, request.DocumentId, "generator"),
            await FiscalProcessStateAsync(request.DocumentId));
        await QuarantineOtherPendingFiscalWorkAsync(request.DocumentId);

        var submissionStore = new SqlFiscalSubmissionWorkStore(connections, ids);
        var transport = new SequenceTransport(
            new DianSubmissionResult(
                DianSubmissionDisposition.Received, "track-902", "Received", "Queued",
                null, Encoding.UTF8.GetBytes("received"), true),
            new DianSubmissionResult(
                DianSubmissionDisposition.Accepted, "track-902", "00", "Accepted",
                Encoding.UTF8.GetBytes("<ApplicationResponse />"),
                Encoding.UTF8.GetBytes("accepted"), true));
        var packages = new FiscalSubmissionPackageBuilder();
        var first = new FiscalSubmissionWorker(
            submissionStore, transport, transport, packages, new FixedTimeProvider(generatedAt.AddSeconds(1)));
        Assert.True((await first.ProcessAsync(
            fixture.BusinessId, request.DocumentId, "submitter-one")).WorkFound);
        Assert.Equal(FiscalDocumentStatusCodes.PendingDianResult,
            await ScalarStringAsync(
                "SELECT Status FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@DocumentId",
                request.DocumentId));

        var second = new FiscalSubmissionWorker(
            submissionStore, transport, transport, packages, new FixedTimeProvider(generatedAt.AddSeconds(10)));
        Assert.True((await second.ProcessAsync(
            fixture.BusinessId, request.DocumentId, "submitter-two")).WorkFound);
        Assert.False((await second.ProcessAsync(
            fixture.BusinessId, request.DocumentId, "submitter-two")).WorkFound);

        Assert.Equal(FiscalDocumentStatusCodes.DianAccepted,
            await ScalarStringAsync(
                "SELECT Status FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@DocumentId",
                request.DocumentId));
        Assert.Equal(FiscalDocumentStatusCodes.DianAccepted,
            await ScalarStringAsync(
                "SELECT FiscalStatus FROM dbo.SalesDocuments WHERE DocumentId=@DocumentId",
                request.DocumentId));
        Assert.Equal(2, await ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.FiscalTransmissionAttempts WHERE DocumentId=@DocumentId",
            request.DocumentId));
        Assert.Equal(1, await ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.FiscalArtifacts WHERE DocumentId=@DocumentId AND ArtifactType='SubmissionZip'",
            request.DocumentId));
        Assert.Equal(1, await ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.FiscalArtifacts WHERE DocumentId=@DocumentId AND ArtifactType='DianApplicationResponse'",
            request.DocumentId));
        Assert.Equal(1, await ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.ServerOutboxMessages WHERE DocumentId=@DocumentId AND Type='FiscalDocument.DianAccepted'",
            request.DocumentId));
        Assert.Equal(1, await ScalarIntAsync(
            """
            SELECT COUNT(*)
            FROM dbo.PosSynchronizationOutboxMessages notification
            JOIN dbo.FiscalDocumentProcesses process
              ON process.DocumentId=@DocumentId
             AND notification.BusinessId=process.BusinessId
             AND notification.OccurredAt=process.CompletedAt
            WHERE notification.Stream=N'FiscalStatus'
            """,
            request.DocumentId));
        Assert.Equal(1, transport.SendCalls);
        Assert.Equal(1, transport.QueryCalls);

        using var statusRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/pos/v1/fiscal/statuses?businessId={fixture.BusinessId:D}&pageSize=200");
        statusRequest.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        statusRequest.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        using var statusResponse = await client.SendAsync(statusRequest);
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var statusPage = await statusResponse.Content.ReadFromJsonAsync<PosFiscalStatusPage>();
        Assert.NotNull(statusPage);
        var change = Assert.Single(statusPage.Items.Where(item => item.DocumentId == request.DocumentId));
        Assert.Equal(FiscalDocumentStatusCodes.DianAccepted, change.Status);
        Assert.Equal(request.FiscalSnapshot!.Cufe, change.Cufe);

        using var nextRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/pos/v1/fiscal/statuses?businessId={fixture.BusinessId:D}&pageSize=200&cursor={Uri.EscapeDataString(statusPage.NextCursor)}");
        nextRequest.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        nextRequest.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        using var nextResponse = await client.SendAsync(nextRequest);
        Assert.Equal(HttpStatusCode.OK, nextResponse.StatusCode);
        var nextPage = await nextResponse.Content.ReadFromJsonAsync<PosFiscalStatusPage>();
        Assert.NotNull(nextPage);
        Assert.DoesNotContain(nextPage.Items, item => item.DocumentId == request.DocumentId);

        using var deniedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/pos/v1/fiscal/statuses?businessId={fixture.BusinessId:D}");
        deniedRequest.Headers.Add(
            "X-Auraly-Device-Id", fixture.DeniedDeviceId.ToString("D"));
        deniedRequest.Headers.Add(
            "X-Auraly-Device-Secret", ServerSliceFixture.DeniedDeviceSecret);
        using var deniedResponse = await client.SendAsync(deniedRequest);
        Assert.Equal(HttpStatusCode.OK, deniedResponse.StatusCode);
    }

    [Fact]
    public async Task Processed_return_generates_signs_and_submits_credit_note_once()
    {
        await using var inventory = await InventoryCheckpoint.CaptureAsync(
            fixture.ConnectionString, fixture.BusinessId, fixture.WarehouseId, fixture.ProductId);
        var customerId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var customer = new SqlCommand("""
                DECLARE @ResolvedPartyId uniqueidentifier=(SELECT TOP(1) PartyId FROM dbo.Parties
                  WHERE TenantId=@TenantId AND IdentificationTypeCode=N'CC'
                    AND NormalizedIdentification=N'222222222');
                IF @ResolvedPartyId IS NULL
                BEGIN
                INSERT dbo.Parties
                  (PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,
                   Identification,NormalizedIdentification,DisplayName,CompletionStatus,
                   IsActive,CreatedBy,CreatedAt)
                SELECT @PartyId,@TenantId,N'NaturalPerson',country.CountryId,N'CC',
                  N'222222222',N'222222222',N'CLIENTE HISTORICO',N'Complete',
                  1,@UserId,SYSDATETIMEOFFSET()
                FROM dbo.Countries country WHERE country.Code=N'CO';
                SET @ResolvedPartyId=@PartyId;
                END;
                DECLARE @ResolvedCustomerId uniqueidentifier=(SELECT CustomerId FROM dbo.Customers
                  WHERE PartyId=@ResolvedPartyId AND BusinessId=@BusinessId);
                IF @ResolvedCustomerId IS NULL
                BEGIN
                INSERT dbo.Customers(CustomerId,PartyId,BusinessId,RequiresElectronicInvoice,
                  IsActive,CreatedBy,CreatedAt)
                VALUES(@CustomerId,@ResolvedPartyId,@BusinessId,1,1,@UserId,SYSDATETIMEOFFSET());
                SET @ResolvedCustomerId=@CustomerId;
                END;
                DECLARE @ResolvedSiteId uniqueidentifier=(SELECT TOP(1) PartySiteId FROM dbo.PartySites
                  WHERE PartyId=@ResolvedPartyId AND IsActive=1 ORDER BY IsPrimary DESC,CreatedAt);
                IF @ResolvedSiteId IS NULL
                BEGIN
                INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,
                  AdministrativeDivisionId,CityId,AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
                SELECT TOP(1) @SiteId,@ResolvedPartyId,N'PRINCIPAL',N'Sede principal',
                  country.CountryId,division.AdministrativeDivisionId,city.CityId,
                  N'Calle 1',1,1,@UserId,SYSDATETIMEOFFSET()
                FROM dbo.Countries country
                JOIN dbo.AdministrativeDivisions division ON division.CountryId=country.CountryId
                JOIN dbo.Cities city ON city.AdministrativeDivisionId=division.AdministrativeDivisionId
                WHERE country.IsActive=1 AND division.IsActive=1 AND city.IsActive=1;
                SET @ResolvedSiteId=@SiteId;
                END;
                IF NOT EXISTS(SELECT 1 FROM dbo.PartyContacts
                  WHERE PartyId=@ResolvedPartyId AND ContactType=N'Email' AND IsActive=1)
                BEGIN
                INSERT dbo.PartyContacts
                  (PartyContactId,PartyId,ContactType,Value,NormalizedValue,IsPrimary,IsActive,CreatedAt)
                VALUES(NEWID(),@ResolvedPartyId,N'Email',N'credit-note-test@auraly.test',
                  N'CREDIT-NOTE-TEST@AURALY.TEST',1,1,SYSDATETIMEOFFSET());
                END;
                SELECT @ResolvedCustomerId,@ResolvedSiteId;
                """, connection);
            customer.Parameters.AddWithValue("@PartyId", partyId);
            customer.Parameters.AddWithValue("@CustomerId", customerId);
            customer.Parameters.AddWithValue("@SiteId", siteId);
            customer.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            customer.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            customer.Parameters.AddWithValue("@UserId", fixture.UserId);
            await using var selected = await customer.ExecuteReaderAsync();
            Assert.True(await selected.ReadAsync());
            customerId = selected.GetGuid(0);
            siteId = selected.GetGuid(1);
        }
        var original = WithUblSnapshot(fixture.CreateValidRequest(903) with
        {
            CustomerId = customerId,
            CustomerPartySiteId = siteId
        });
        using var pos = fixture.CreateClient();
        using (var upload = fixture.CreateUploadMessage(original))
        using (var uploadResponse = await pos.SendAsync(upload))
            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);

        var webSessionId = await fixture.OpenWebWorkSessionAsync();
        var returnId = Guid.NewGuid();
        var returnRequest = new ConfirmSalesReturnRequest(
            returnId, fixture.BusinessId, fixture.WarehouseId, original.DocumentId,
            new DateTimeOffset(2026, 8, 1, 11, 0, 0, TimeSpan.FromHours(-5)),
            ReturnEconomicResolutions.Refund, "Cash", "Devolución parcial de bienes",
            [new ConfirmSalesReturnLineRequest(1, .5m, ReturnInventoryDispositions.Sellable)],
            webSessionId, null, "Other");
        using var user = fixture.CreateAdminClient(
            SalesReturnPermissionCodes.Create, SalesReturnPermissionCodes.Confirm);
        using var returnMessage = new HttpRequestMessage(
            HttpMethod.Post, "/api/commerce/v1/sales-returns/confirm")
        {
            Content = JsonContent.Create(returnRequest)
        };
        returnMessage.Headers.Add("Idempotency-Key", $"credit-{returnId:N}");
        using var returnResponse = await user.SendAsync(returnMessage);
        Assert.True(returnResponse.StatusCode == HttpStatusCode.Accepted,
            $"Expected Accepted but received {returnResponse.StatusCode}: {await returnResponse.Content.ReadAsStringAsync()}");
        var accepted = await returnResponse.Content.ReadFromJsonAsync<SalesReturnAcceptance>();
        Assert.NotNull(accepted);

        Assert.Equal(FiscalDocumentStatusCodes.PendingGeneration,
            await ScalarStringAsync(
                "SELECT FiscalStatus FROM dbo.SalesReturns WHERE ReturnId=@DocumentId", returnId));
        Assert.Equal(FiscalDocumentTypeCodes.CreditNote,
            await ScalarStringAsync(
                "SELECT FiscalDocumentType FROM dbo.FiscalDocuments WHERE DocumentId=@DocumentId", returnId));

        var connections = new SqlServerConnectionFactory(
            new AuralySqlConnectionSource(fixture.ConnectionString));
        var ids = new TestIds();
        var generatedAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var generator = CreateWorker(
            new SqlFiscalGenerationWorkStore(connections, ids),
            new FixedTimeProvider(generatedAt));
        Assert.True(await generator.ProcessAsync(fixture.BusinessId, returnId, "credit-generator"));
        Assert.False(await generator.ProcessAsync(fixture.BusinessId, returnId, "credit-generator"));
        Assert.Equal(returnRequest.ReturnedAt, await ScalarDateTimeOffsetAsync(
            "SELECT SignedAt FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@DocumentId", returnId));

        var unsigned = await ArtifactAsync(returnId, FiscalArtifactTypeCodes.UnsignedXml);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(unsigned));
        Assert.Equal("CreditNote", xml.Root!.Name.LocalName);
        var cude = xml.Root.Elements()
            .Single(element => element.Name.LocalName == "UUID").Value;
        Assert.Equal(96, cude.Length);
        Assert.Equal(cude, await ScalarStringAsync(
            "SELECT UniqueCode FROM dbo.FiscalDocuments WHERE DocumentId=@DocumentId", returnId));
        Assert.Contains(original.FiscalSnapshot!.FiscalNumber,
            xml.Descendants().Where(element => element.Name.LocalName == "ID")
                .Select(element => element.Value));
        Assert.Contains(original.FiscalSnapshot.Cufe,
            xml.Descendants().Where(element => element.Name.LocalName == "UUID")
                .Select(element => element.Value));
        Assert.Equal("2", xml.Descendants().Single(element =>
            element.Name.LocalName == "ProfileExecutionID").Value);
        Assert.Equal("91", xml.Descendants().Single(element =>
            element.Name.LocalName == "CreditNoteTypeCode").Value);
        Assert.Equal("1", xml.Descendants().Single(element =>
            element.Name.LocalName == "ResponseCode").Value);
        var creditReceipt = new DianInvoicePdfRenderer().ReadReceipt(
            await ArtifactAsync(returnId, FiscalArtifactTypeCodes.SignedXml));
        Assert.Equal(.5m, Assert.Single(creditReceipt.Lines).Quantity);
        Assert.Contains("Cantidad devuelta", new DianInvoicePdfRenderer().RenderHtml(creditReceipt));
        Assert.Equal(2, await ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.FiscalArtifacts WHERE DocumentId=@DocumentId", returnId));

        var transport = new SequenceTransport(
            new DianSubmissionResult(
                DianSubmissionDisposition.Received, "credit-track-903", "Received", "Queued",
                null, Encoding.UTF8.GetBytes("received"), true),
            new DianSubmissionResult(
                DianSubmissionDisposition.Accepted, "credit-track-903", "2",
                "Set de prueba se encuentra Aceptado.",
                Encoding.UTF8.GetBytes("<ApplicationResponse />"),
                Encoding.UTF8.GetBytes("accepted"), true));
        var submitter = new FiscalSubmissionWorker(
            new SqlFiscalSubmissionWorkStore(connections, ids), transport, transport,
            new FiscalSubmissionPackageBuilder(),
            new FixedTimeProvider(generatedAt.AddSeconds(1)));
        Assert.True((await submitter.ProcessAsync(
            fixture.BusinessId, returnId, "credit-submitter")).WorkFound);
        var statusQuery = new FiscalSubmissionWorker(
            new SqlFiscalSubmissionWorkStore(connections, ids), transport, transport,
            new FiscalSubmissionPackageBuilder(),
            new FixedTimeProvider(generatedAt.AddSeconds(10)));
        Assert.True((await statusQuery.ProcessAsync(
            fixture.BusinessId, returnId, "credit-submitter")).WorkFound);
        Assert.False((await statusQuery.ProcessAsync(
            fixture.BusinessId, returnId, "credit-submitter")).WorkFound);

        Assert.Equal(FiscalDocumentStatusCodes.DianAccepted,
            await ScalarStringAsync(
                "SELECT FiscalStatus FROM dbo.FiscalDocuments WHERE DocumentId=@DocumentId", returnId));
        Assert.Equal(FiscalDocumentStatusCodes.DianAccepted,
            await ScalarStringAsync(
                "SELECT FiscalStatus FROM dbo.SalesReturns WHERE ReturnId=@DocumentId", returnId));
        Assert.Equal(2, await ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.FiscalTransmissionAttempts WHERE DocumentId=@DocumentId", returnId));
        Assert.Equal(1, await ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.FiscalTransmissionAttempts WHERE DocumentId=@DocumentId AND Operation='SendTestSetAsync'", returnId));
        Assert.Equal(1, await ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.FiscalTransmissionAttempts WHERE DocumentId=@DocumentId AND Operation='GetStatusZip' AND StatusCode='2'", returnId));
        Assert.Equal(1, await ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.FiscalArtifacts WHERE DocumentId=@DocumentId AND ArtifactType='SubmissionZip'", returnId));
        Assert.Equal(1, await ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.ServerOutboxMessages WHERE DocumentId=@DocumentId AND Type='FiscalDocument.DianAccepted'", returnId));
        Assert.Equal(1, await ScalarIntAsync("""
            SELECT COUNT(*) FROM dbo.FiscalDocuments fiscal
            JOIN dbo.TenantProvisioningOutboxMessages message
              ON message.MessageId=fiscal.DeliveryOutboxMessageId
            WHERE fiscal.DocumentId=@DocumentId
              AND message.Type=N'FiscalInvoiceDelivery' AND message.ProcessedAt IS NULL
            """, returnId));
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var recipient = new SqlCommand("""
                DECLARE @MessageId uniqueidentifier=(SELECT DeliveryOutboxMessageId
                  FROM dbo.FiscalDocuments WHERE DocumentId=@DocumentId);
                EXEC dbo.FiscalInvoiceDeliveryRecipientGet @DocumentId,@MessageId,@TenantId;
                """, connection);
            recipient.Parameters.AddWithValue("@DocumentId", returnId);
            recipient.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            await using var reader = await recipient.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("SalesReturn", reader.GetString(22));
            Assert.Equal((decimal)accepted.TotalAmount, reader.GetDecimal(5));
            Assert.True(((byte[])reader[6]).Length > 0);
            Assert.False(reader.IsDBNull(21));
            Assert.False(await reader.ReadAsync());
        }
        Assert.Equal(1, transport.SendCalls);
        Assert.Equal(1, transport.TestSetCalls);
        Assert.Equal(0, transport.ProductionCalls);
        Assert.Equal(1, transport.QueryCalls);

        using var fiscalUser = fixture.CreateAdminClient(FiscalPermissionCodes.DocumentsRead);
        using var documentResponse = await fiscalUser.GetAsync(
            $"/api/commerce/v1/fiscal/documents/{returnId}");
        Assert.Equal(HttpStatusCode.OK, documentResponse.StatusCode);
        var document = await documentResponse.Content.ReadFromJsonAsync<FiscalDocumentView>();
        Assert.NotNull(document);
        Assert.Equal("SalesReturn", document.SourceDocumentType);
        Assert.Equal(FiscalDocumentTypeCodes.CreditNote, document.FiscalDocumentType);
        Assert.Equal("CUDE", document.UniqueCodeType);
        Assert.Equal(cude, document.UniqueCode);
        Assert.Null(document.DeviceId);
        using var pageResponse = await fiscalUser.GetAsync(
            $"/api/commerce/v1/fiscal/documents?page=1&pageSize=10&uniqueCode={cude}");
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        var page = await pageResponse.Content.ReadFromJsonAsync<FiscalDocumentPage>();
        Assert.NotNull(page);
        Assert.Contains(page.Items, item => item.DocumentId == returnId);
    }

    private FiscalGenerationWorker CreateWorker(IFiscalGenerationWorkStore store, TimeProvider clock) =>
        new(store, new TestPin(), new DianInvoiceUblBuilder(),
            new DianSupportDocumentUblBuilder(), new DianCreditNoteUblBuilder(),
            new DianDebitNoteUblBuilder(), new DianSchemaValidator(),
            new DianPayrollXmlBuilder(), new DianPayrollSchemaValidator(),
            new TestSigner(), clock);

    private PosSaleUploadRequest WithUblSnapshot(PosSaleUploadRequest request)
    {
        var address = new PosSaleUblAddressContract("11001", "Bogotá", "Bogotá D.C.", "11", "CL 1 2 3");
        var supplier = new PosSaleUblPartyContract(ServerSliceFixture.SupplierTaxId, "0", "31", "1",
            "EMISOR HISTORICO", "EMISOR HISTORICO", "R-99-PN", "01", "IVA", address);
        var customer = new PosSaleUblPartyContract("222222222", "0", "13", "2",
            "CLIENTE HISTORICO", "CLIENTE HISTORICO", "R-99-PN", "ZZ", "No aplica", address);
        return request with
        {
            UblSnapshot = new PosSaleUblSnapshotContract(fixture.FiscalIssuerConfigurationId,
                "COP", "01", supplier, customer,
                new PosSaleUblAuthorizationContract(ServerSliceFixture.AuthorizationNumber,
                    new DateOnly(2026, 1, 1), new DateOnly(2028, 12, 31),
                    ServerSliceFixture.Prefix, 1, 20000),
                "auraly-test-software",
                [new PosSaleUblLineContract(1, "P-E2E", "999", "EA", "IVA", 19m)],
                "1", "10", DateOnly.FromDateTime(request.FiscalSnapshot!.IssuedAt.Date), null)
        };
    }

    private async Task ChangeMasterNamesAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.Businesses SET Name=N'MAESTRO CAMBIADO' WHERE BusinessId=@BusinessId;
            UPDATE dbo.FiscalIssuerConfigurations SET LegalName=N'MAESTRO CAMBIADO',TradeName=N'MAESTRO CAMBIADO'
            WHERE FiscalIssuerConfigurationId=@ConfigurationId;
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@ConfigurationId", fixture.FiscalIssuerConfigurationId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> ScalarStringAsync(string sql, Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        return (string)(await command.ExecuteScalarAsync())!;
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

    private async Task<int> ScalarIntAsync(string sql, Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<DateTimeOffset> ScalarDateTimeOffsetAsync(string sql, Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        return (DateTimeOffset)(await command.ExecuteScalarAsync())!;
    }

    private async Task<byte[]> ArtifactAsync(Guid documentId, string type)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Content FROM dbo.FiscalArtifacts WHERE DocumentId=@DocumentId AND ArtifactType=@Type";
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@Type", type);
        return (byte[])(await command.ExecuteScalarAsync())!;
    }

    private async Task QuarantineOtherPendingFiscalWorkAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.FiscalDocumentProcesses
            SET Status='PermanentFailure',NextAttemptAt=NULL,LockedAt=NULL,LockedBy=NULL
            WHERE DocumentId<>@DocumentId
              AND Status IN ('PendingSubmission','PendingDianResult','RetryScheduled');
            """;
        command.Parameters.AddWithValue("@DocumentId", documentId);
        await command.ExecuteNonQueryAsync();
    }

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
            var hash=Convert.ToHexString(SHA256.HashData(request.UnsignedXml)).ToLowerInvariant();
            return Task.FromResult(new FiscalSigningResult(request.UnsignedXml,hash,"TEST",request.SigningTime));
        }
    }
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {

        public override DateTimeOffset GetUtcNow() => now;
    }
    private sealed class SequenceTransport(params DianSubmissionResult[] results)
        : IDianHabilitationTransport, IDianProductionTransport
    {
        public Task<DianSubmissionResult> GetStatusAsync(
            DianSubmissionRequest request,
            CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            Assert.False(string.IsNullOrWhiteSpace(request.TrackId));
            return Next();
        }

        private int index;
        public int SendCalls { get; private set; }
        public int TestSetCalls { get; private set; }
        public int ProductionCalls { get; private set; }
        public int QueryCalls { get; private set; }

        public Task<DianSubmissionResult> SubmitTestSetAsync(
            DianSubmissionRequest request,
            CancellationToken cancellationToken = default)
        {
            SendCalls++;
            TestSetCalls++;
            return Next();
        }

        public Task<DianSubmissionResult> GetStatusZipAsync(
            DianSubmissionRequest request,
            CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            Assert.False(string.IsNullOrWhiteSpace(request.TrackId));
            return Next();
        }

        public Task<DianSubmissionResult> SubmitBillSyncAsync(
            DianSubmissionRequest request,
            CancellationToken cancellationToken = default)
        {
            SendCalls++;
            ProductionCalls++;
            return Next();
        }

        public Task<DianSubmissionResult> SubmitPayrollSyncAsync(
            DianSubmissionRequest request,
            CancellationToken cancellationToken = default)
        {
            SendCalls++;
            ProductionCalls++;
            return Next();
        }

        private Task<DianSubmissionResult> Next()
        {
            if (index >= results.Length)
                throw new InvalidOperationException("The deterministic DIAN response sequence is exhausted.");
            return Task.FromResult(results[index++]);
        }
    }

    private sealed class InventoryCheckpoint(
        string connectionString,
        Guid businessId,
        Guid warehouseId,
        Guid productId,
        decimal quantity,
        decimal averageCost,
        decimal inventoryValue,
        bool existed) : IAsyncDisposable
    {
        public static async Task<InventoryCheckpoint> CaptureAsync(
            string connectionString, Guid businessId, Guid warehouseId, Guid productId)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT QuantityOnHand,AverageUnitCost,InventoryValue
                FROM dbo.InventoryBalances
                WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId
                  AND ProductId=@ProductId;
                """;
            command.Parameters.AddWithValue("@BusinessId", businessId);
            command.Parameters.AddWithValue("@WarehouseId", warehouseId);
            command.Parameters.AddWithValue("@ProductId", productId);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return new InventoryCheckpoint(
                    connectionString, businessId, warehouseId, productId, 0m, 0m, 0m, false);
            return new InventoryCheckpoint(
                connectionString, businessId, warehouseId, productId,
                reader.GetDecimal(0), reader.GetDecimal(1), reader.GetDecimal(2), true);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = existed
                ? """
                  UPDATE dbo.InventoryBalances
                  SET QuantityOnHand=@Quantity,AverageUnitCost=@AverageCost,
                      InventoryValue=@InventoryValue,UpdatedAt=SYSUTCDATETIME()
                  WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId
                    AND ProductId=@ProductId;
                  """
                : """
                  DELETE dbo.InventoryBalances
                  WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId
                    AND ProductId=@ProductId;
                  """;
            command.Parameters.AddWithValue("@Quantity", quantity);
            command.Parameters.AddWithValue("@AverageCost", averageCost);
            command.Parameters.AddWithValue("@InventoryValue", inventoryValue);
            command.Parameters.AddWithValue("@BusinessId", businessId);
            command.Parameters.AddWithValue("@WarehouseId", warehouseId);
            command.Parameters.AddWithValue("@ProductId", productId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
    }

}
