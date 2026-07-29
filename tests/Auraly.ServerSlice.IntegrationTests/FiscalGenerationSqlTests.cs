using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Fiscal;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Ubl;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class FiscalGenerationSqlTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Snapshot_is_leased_once_and_persisted_without_reading_changed_master_values()
    {
        var request = WithUblSnapshot(fixture.CreateValidRequest(901));
        using var client = fixture.CreateClient();
        using var response = await client.SendAsync(fixture.CreateUploadMessage(request));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await ChangeMasterNamesAsync();

        var connections = new SqlServerConnectionFactory(fixture.ConnectionString);
        var store = new SqlFiscalGenerationWorkStore(connections, new TestIds());
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 29, 15, 0, 0, TimeSpan.Zero));
        var first = CreateWorker(store, clock);
        var second = CreateWorker(store, clock);
        var results = await Task.WhenAll(
            first.ProcessNextAsync("worker-one"),
            second.ProcessNextAsync("worker-two"));

        Assert.Single(results.Where(result => result));
        Assert.Single(results.Where(result => !result));
        Assert.Equal(FiscalDocumentStatusCodes.PendingSubmission,
            await ScalarStringAsync("SELECT Status FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@DocumentId", request.DocumentId));
        Assert.Equal(2, await ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.FiscalArtifacts WHERE DocumentId=@DocumentId", request.DocumentId));
        var xml = Encoding.UTF8.GetString(await ArtifactAsync(request.DocumentId, FiscalArtifactTypeCodes.UnsignedXml));
        Assert.Contains("EMISOR HISTORICO", xml, StringComparison.Ordinal);
        Assert.Contains("CLIENTE HISTORICO", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("MAESTRO CAMBIADO", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Signed_invoice_is_submitted_queried_and_accepted_exactly_once()
    {
        var request = WithUblSnapshot(fixture.CreateValidRequest(902));
        using var client = fixture.CreateClient();
        using var response = await client.SendAsync(fixture.CreateUploadMessage(request));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var connections = new SqlServerConnectionFactory(fixture.ConnectionString);
        var ids = new TestIds();
        var generatedAt = new DateTimeOffset(2026, 7, 29, 16, 0, 0, TimeSpan.Zero);
        Assert.True(await CreateWorker(
            new SqlFiscalGenerationWorkStore(connections, ids),
            new FixedTimeProvider(generatedAt)).ProcessNextAsync("generator"));
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
            submissionStore, transport, packages, new FixedTimeProvider(generatedAt.AddSeconds(1)));
        Assert.True(await first.ProcessNextAsync("submitter-one"));
        Assert.Equal(FiscalDocumentStatusCodes.PendingDianResult,
            await ScalarStringAsync(
                "SELECT Status FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@DocumentId",
                request.DocumentId));

        var second = new FiscalSubmissionWorker(
            submissionStore, transport, packages, new FixedTimeProvider(generatedAt.AddSeconds(10)));
        Assert.True(await second.ProcessNextAsync("submitter-two"));
        Assert.False(await second.ProcessNextAsync("submitter-two"));

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
        Assert.Equal(1, transport.SendCalls);
        Assert.Equal(1, transport.QueryCalls);

        using var statusRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/pos/v1/fiscal/statuses?pageSize=200");
        statusRequest.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        statusRequest.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        using var statusResponse = await client.SendAsync(statusRequest);
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var statusPage = await statusResponse.Content.ReadFromJsonAsync<PosFiscalStatusPage>();
        Assert.NotNull(statusPage);
        var change = Assert.Single(statusPage.Items.Where(item => item.DocumentId == request.DocumentId));
        Assert.Equal(FiscalDocumentStatusCodes.DianAccepted, change.Status);
        Assert.Equal(request.FiscalSnapshot.Cufe, change.Cufe);

        using var nextRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/pos/v1/fiscal/statuses?pageSize=200&cursor={Uri.EscapeDataString(statusPage.NextCursor)}");
        nextRequest.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        nextRequest.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        using var nextResponse = await client.SendAsync(nextRequest);
        Assert.Equal(HttpStatusCode.OK, nextResponse.StatusCode);
        var nextPage = await nextResponse.Content.ReadFromJsonAsync<PosFiscalStatusPage>();
        Assert.NotNull(nextPage);
        Assert.DoesNotContain(nextPage.Items, item => item.DocumentId == request.DocumentId);

        using var deniedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/pos/v1/fiscal/statuses");
        deniedRequest.Headers.Add(
            "X-Auraly-Device-Id", fixture.DeniedDeviceId.ToString("D"));
        deniedRequest.Headers.Add(
            "X-Auraly-Device-Secret", ServerSliceFixture.DeniedDeviceSecret);
        using var deniedResponse = await client.SendAsync(deniedRequest);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
    }

    private FiscalGenerationWorker CreateWorker(IFiscalGenerationWorkStore store, TimeProvider clock) =>
        new(store, new TestPin(), new DianInvoiceUblBuilder(), new DianSchemaValidator(),
            new TestSigner(), clock);

    private PosSaleUploadRequest WithUblSnapshot(PosSaleUploadRequest request)
    {
        var address = new PosSaleUblAddressContract("11001", "Bogotá", "Bogotá D.C.", "11", "CL 1 2 3");
        var supplier = new PosSaleUblPartyContract(ServerSliceFixture.SupplierTaxId, "7", "31", "1",
            "EMISOR HISTORICO", "EMISOR HISTORICO", "R-99-PN", "01", "IVA", address);
        var customer = new PosSaleUblPartyContract("222222222", "0", "13", "2",
            "CLIENTE HISTORICO", "CLIENTE HISTORICO", "R-99-PN", "ZZ", "No aplica", address);
        return request with
        {
            UblSnapshot = new PosSaleUblSnapshotContract(fixture.FiscalIssuerConfigurationId,
                "COP", "01", supplier, customer,
                new PosSaleUblAuthorizationContract(ServerSliceFixture.AuthorizationNumber,
                    new DateOnly(2026, 1, 1), new DateOnly(2028, 12, 31),
                    ServerSliceFixture.Prefix, 1, 10000),
                "auraly-test-software",
                [new PosSaleUblLineContract(1, "P-E2E", "999", "EA", "IVA", 19m)],
                "1", "10", DateOnly.FromDateTime(request.FiscalSnapshot.IssuedAt.Date), null)
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

    private async Task<int> ScalarIntAsync(string sql, Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
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
        : IDianHabilitationTransport
    {
        private int index;
        public int SendCalls { get; private set; }
        public int QueryCalls { get; private set; }

        public Task<DianSubmissionResult> SubmitTestSetAsync(
            DianSubmissionRequest request,
            CancellationToken cancellationToken = default)
        {
            SendCalls++;
            return Next();
        }

        public Task<DianSubmissionResult> GetStatusZipAsync(
            DianSubmissionRequest request,
            CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            Assert.Equal("track-902", request.TrackId);
            return Next();
        }

        private Task<DianSubmissionResult> Next()
        {
            if (index >= results.Length)
                throw new InvalidOperationException("The deterministic DIAN response sequence is exhausted.");
            return Task.FromResult(results[index++]);
        }
    }

}