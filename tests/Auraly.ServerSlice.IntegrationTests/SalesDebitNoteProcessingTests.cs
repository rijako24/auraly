using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Fiscal")]
public sealed class SalesDebitNoteProcessingTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Debit_note_references_invoice_and_creates_fiscal_accounting_and_receivable_work_once()
    {
        var customerId = await CreateCustomerAsync();
        var original = WithUblSnapshot(fixture.CreateValidRequest(9_701) with { CustomerId = customerId });
        using (var pos = fixture.CreateClient())
        using (var upload = fixture.CreateUploadMessage(original))
        using (var response = await pos.SendAsync(upload))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var debitNoteId = Guid.NewGuid();
        var request = new ConfirmSalesDebitNoteRequest(
            debitNoteId, fixture.BusinessId, original.DocumentId,
            new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.FromHours(-5)),
            DianDebitNoteConcepts.Charge, "Flete adicional facturado",
            [new ConfirmSalesDebitNoteLineRequest("Flete adicional", 1m, 10_000m, "01", 19m)],
            "Cargo acordado con el cliente");
        const string key = "sales-debit-note-e2e-001";
        using var client = fixture.CreateAdminClient(
            SalesDebitNotePermissionCodes.Read, SalesDebitNotePermissionCodes.Create);
        using (var message = Message(request, key))
        using (var response = await client.SendAsync(message))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var acceptance = await response.Content.ReadFromJsonAsync<SalesDebitNoteAcceptance>();
            Assert.NotNull(acceptance);
            Assert.StartsWith("NDB00-", acceptance.DocumentNumber);
            Assert.False(acceptance.IdempotentReplay);
        }

        Assert.Equal("Completed", await ScalarAsync<string>(
            "SELECT Status FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id", debitNoteId));
        Assert.Equal("Processed", await ScalarAsync<string>(
            "SELECT Status FROM dbo.SalesDebitNotes WHERE DebitNoteId=@Id", debitNoteId));
        Assert.Equal("DebitNote", await ScalarAsync<string>(
            "SELECT FiscalDocumentType FROM dbo.FiscalDocuments WHERE DocumentId=@Id", debitNoteId));
        Assert.Equal("CUDE", await ScalarAsync<string>(
            "SELECT UniqueCodeType FROM dbo.FiscalDocuments WHERE DocumentId=@Id", debitNoteId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.SalesDebitNoteFiscalSnapshots WHERE DocumentId=@Id", debitNoteId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", debitNoteId));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ServerOutboxMessages WHERE DocumentId=@Id", debitNoteId));

        using (var response = await client.GetAsync(
                   "/api/commerce/v1/sales-debit-notes?page=1&pageSize=20&search=NDB00"))
        {
            response.EnsureSuccessStatusCode();
            var page = await response.Content.ReadFromJsonAsync<SalesDebitNotePage>();
            Assert.NotNull(page);
            Assert.Contains(page.Items, item => item.DebitNoteId == debitNoteId);
        }
        using (var response = await client.GetAsync(
                   $"/api/commerce/v1/sales-debit-notes/{debitNoteId:D}"))
        {
            response.EnsureSuccessStatusCode();
            var detail = await response.Content.ReadFromJsonAsync<SalesDebitNoteDetail>();
            Assert.NotNull(detail);
            Assert.Equal(original.DocumentNumber.FullNumber, detail.Header.OriginalDocumentNumber);
            Assert.Equal(10_000m, detail.UntaxedAmount);
            Assert.Equal(1_900m, detail.TaxAmount);
            Assert.Equal(11_900m, detail.Header.TotalAmount);
            Assert.Single(detail.Lines);
        }

        using (var message = Message(request, key))
        using (var response = await client.SendAsync(message))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var replay = await response.Content.ReadFromJsonAsync<SalesDebitNoteAcceptance>();
            Assert.NotNull(replay);
            Assert.True(replay.IdempotentReplay);
        }
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.SalesDebitNotes WHERE DebitNoteId=@Id", debitNoteId));
    }

    private async Task<Guid> CreateCustomerAsync()
    {
        var partyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,LegalName,
              CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES(@PartyId,@TenantId,N'Organization',N'Cliente nota débito',N'Cliente nota débito',
              N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.Customers(CustomerId,PartyId,BusinessId,RequiresElectronicInvoice,
              IsActive,CreatedBy,CreatedAt)
            VALUES(@CustomerId,@PartyId,@BusinessId,1,1,@UserId,SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddWithValue("@PartyId", partyId);
        command.Parameters.AddWithValue("@CustomerId", customerId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        await command.ExecuteNonQueryAsync();
        return customerId;
    }

    private PosSaleUploadRequest WithUblSnapshot(PosSaleUploadRequest request)
    {
        var address = new PosSaleUblAddressContract("11001", "Bogotá", "Bogotá D.C.", "11", "CL 1 2 3");
        var supplier = new PosSaleUblPartyContract(ServerSliceFixture.SupplierTaxId, "7", "31", "1",
            "EMISOR HISTORICO", "EMISOR HISTORICO", "R-99-PN", "01", "IVA", address);
        var customer = new PosSaleUblPartyContract("222222222", "0", "31", "1",
            "CLIENTE NOTA DEBITO", "CLIENTE NOTA DEBITO", "R-99-PN", "01", "IVA", address);
        return request with
        {
            UblSnapshot = new PosSaleUblSnapshotContract(
                fixture.FiscalIssuerConfigurationId, "COP", "01", supplier, customer,
                new PosSaleUblAuthorizationContract(ServerSliceFixture.AuthorizationNumber,
                    new DateOnly(2026, 1, 1), new DateOnly(2028, 12, 31),
                    ServerSliceFixture.Prefix, 1, 10000),
                "auraly-test-software",
                [new PosSaleUblLineContract(1, "P-E2E", "999", "EA", "IVA", 19m)],
                "1", "10", DateOnly.FromDateTime(request.FiscalSnapshot!.IssuedAt.Date), null)
        };
    }

    private static HttpRequestMessage Message(ConfirmSalesDebitNoteRequest request, string key)
    {
        var message = new HttpRequestMessage(HttpMethod.Post,
            "/api/commerce/v1/sales-debit-notes/confirm") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key);
        return message;
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        return (T)Convert.ChangeType(await command.ExecuteScalarAsync() ??
            throw new InvalidOperationException("The expected database value does not exist."), typeof(T));
    }
}
