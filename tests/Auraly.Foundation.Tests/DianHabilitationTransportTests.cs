using Auraly.Contracts.Fiscal;
using Auraly.Application.Fiscal;
using Auraly.Infrastructure.Fiscal;

namespace Auraly.Foundation.Tests;

public sealed class DianHabilitationTransportTests
{
    private static readonly Guid BusinessId = Guid.Parse("01981d13-43b7-7d0f-a3c1-1b4f90a26550");
    private static readonly Guid DocumentId = Guid.Parse("01981d13-4d9c-7aa1-b54d-7a1df23e57e1");

    [Fact]
    public async Task Upload_returns_track_id_and_requires_status_query()
    {
        var client = new DeterministicClient
        {
            Upload = new DianUploadDocumentResponse { ZipKey = "track-001" }
        };
        var transport = CreateTransport(client);

        var result = await transport.SubmitTestSetAsync(Request());

        Assert.Equal(DianSubmissionDisposition.Received, result.Disposition);
        Assert.Equal("track-001", result.TrackId);
        Assert.True(result.MayHaveReachedDian);
        Assert.Equal(1, client.UploadCalls);
    }

    [Fact]
    public async Task Upload_accepts_DIAN_null_error_list_when_zip_key_is_present()
    {
        var client = new DeterministicClient
        {
            Upload = new DianUploadDocumentResponse
            {
                ZipKey = "track-null-errors",
                ErrorMessageList = null!
            }
        };

        var result = await CreateTransport(client).SubmitTestSetAsync(Request());

        Assert.Equal(DianSubmissionDisposition.Received, result.Disposition);
        Assert.Equal("track-null-errors", result.TrackId);
    }

    [Fact]
    public async Task Initial_validation_error_is_permanent_rejection_not_transient_retry()
    {
        var client = new DeterministicClient
        {
            Upload = new DianUploadDocumentResponse
            {
                ErrorMessageList =
                [
                    new DianUploadError
                    {
                        Success = false,
                        ProcessedMessage = "ZIP invalid",
                        XmlFileName = "fv.zip"
                    }
                ]
            }
        };

        var result = await CreateTransport(client).SubmitTestSetAsync(Request());

        Assert.Equal(DianSubmissionDisposition.Rejected, result.Disposition);
        Assert.Contains("ZIP invalid", result.StatusDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_query_maps_acceptance_and_preserves_application_response()
    {
        var response = new byte[] { 1, 2, 3 };
        var client = new DeterministicClient
        {
            Status =
            [
                new DianDocumentResponse
                {
                    IsValid = true,
                    StatusCode = "00",
                    StatusDescription = "Procesado correctamente",
                    XmlBytes = response
                }
            ]
        };

        var result = await CreateTransport(client).GetStatusZipAsync(Request("track-001"));

        Assert.Equal(DianSubmissionDisposition.Accepted, result.Disposition);
        Assert.Equal("00", result.StatusCode);
        Assert.Equal(response, result.ApplicationResponse);
    }

    [Fact]
    public async Task Empty_status_response_remains_pending()
    {
        var result = await CreateTransport(new DeterministicClient())
            .GetStatusZipAsync(Request("track-001"));

        Assert.Equal(DianSubmissionDisposition.Pending, result.Disposition);
    }

    [Fact]
    public async Task Accepted_test_set_status_is_not_misclassified_as_document_rejection()
    {
        var client = new DeterministicClient
        {
            Status =
            [
                new DianDocumentResponse
                {
                    IsValid = false,
                    StatusCode = "2",
                    StatusDescription = "Set de prueba con identificador test se encuentra Aceptado."
                }
            ]
        };

        var result = await CreateTransport(client).GetStatusZipAsync(Request("track-001"));

        Assert.Equal(DianSubmissionDisposition.Accepted, result.Disposition);
        Assert.Equal("2", result.StatusCode);
    }

    [Fact]
    public async Task Upload_timeout_is_ambiguous_and_must_not_trigger_blind_resubmission()
    {
        var client = new DeterministicClient { UploadException = new TimeoutException("socket timeout") };

        var result = await CreateTransport(client).SubmitTestSetAsync(Request());

        Assert.Equal(DianSubmissionDisposition.TransientFailure, result.Disposition);
        Assert.True(result.MayHaveReachedDian);
        Assert.Equal(1, client.UploadCalls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public async Task Invalid_test_set_is_rejected_before_network(string testSetId)
    {
        var client = new DeterministicClient();
        var request = Request() with { TestSetId = testSetId };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateTransport(client).SubmitTestSetAsync(request));
        Assert.Equal(0, client.UploadCalls);
    }

    [Fact]
    public async Task Numbering_range_client_maps_the_production_DIAN_response()
    {
        var client = new DeterministicClient
        {
            Numbering = new DianNumberRangeResponseList
            {
                OperationCode = "100",
                ResponseList =
                [
                    new DianNumberRangeResponse
                    {
                        ResolutionNumber = "18764000000123",
                        ResolutionDate = "2026-08-01",
                        Prefix = "FV",
                        FromNumber = 1,
                        ToNumber = 5000,
                        ValidDateFrom = "2026-08-01",
                        ValidDateTo = "2027-08-01",
                        TechnicalKey = "technical-key"
                    }
                ]
            }
        };
        var context = new DianNumberingRangeContext(
            BusinessId, "900123456", "900123456", "software-id",
            new FiscalCertificateReference(BusinessId, "Test", "ephemeral", string.Empty));

        var result = await new DianNumberingRangeClient(new FixedClientFactory(client))
            .GetAsync(context, CancellationToken.None);

        var range = Assert.Single(result);
        Assert.Equal("18764000000123", range.AuthorizationNumber);
        Assert.Equal("FV", range.Prefix);
        Assert.Equal(5000, range.RangeEnd);
        Assert.Equal(new DateOnly(2027, 8, 1), range.ValidUntil);
    }

    [Fact]
    public async Task Numbering_range_client_rejects_a_non_success_operation_even_with_rows()
    {
        var client = new DeterministicClient
        {
            Numbering = new DianNumberRangeResponseList
            {
                OperationCode = "500",
                OperationDescription = "Consulta rechazada",
                ResponseList =
                [
                    new DianNumberRangeResponse
                    {
                        ResolutionNumber = "18764000000123",
                        Prefix = "FV",
                        FromNumber = 1,
                        ToNumber = 5000,
                        ValidDateFrom = "2026-08-01",
                        ValidDateTo = "2027-08-01",
                        TechnicalKey = "must-not-be-imported"
                    }
                ]
            }
        };
        var context = new DianNumberingRangeContext(
            BusinessId, "900123456", "900123456", "software-id",
            new FiscalCertificateReference(BusinessId, "Test", "ephemeral", string.Empty));

        var exception = await Assert.ThrowsAsync<FiscalConfigurationValidationException>(() =>
            new DianNumberingRangeClient(new FixedClientFactory(client))
                .GetAsync(context, CancellationToken.None));

        Assert.Contains("Consulta rechazada", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_transport_uses_SendBillSync_and_maps_acceptance()
    {
        var applicationResponse = new byte[] { 7, 8, 9 };
        var client = new DeterministicClient
        {
            Bill = new DianDocumentResponse
            {
                IsValid = true,
                StatusCode = "00",
                StatusDescription = "Procesado correctamente",
                XmlDocumentKey = "cufe-001",
                XmlBytes = applicationResponse
            }
        };
        var transport = new DianProductionTransport(
            new FixedProductionConfigurationProvider(), new FixedClientFactory(client));

        var result = await transport.SubmitBillSyncAsync(Request() with { TestSetId = null });

        Assert.Equal(DianSubmissionDisposition.Accepted, result.Disposition);
        Assert.Equal("cufe-001", result.TrackId);
        Assert.Equal(applicationResponse, result.ApplicationResponse);
        Assert.Equal(1, client.BillCalls);
    }

    private static DianHabilitationTransport CreateTransport(DeterministicClient client) =>
        new(new FixedConfigurationProvider(), new FixedClientFactory(client));

    private static DianSubmissionRequest Request(string? trackId = null) =>
        new(BusinessId, DocumentId, "fv090012345600001.zip", [80, 75, 3, 4],
            "4de36cb4-9973-4ea4-a156-34e909aa24dc", trackId, "corr-001");

    private sealed class FixedConfigurationProvider : IDianHabilitationConfigurationProvider
    {
        public Task<DianHabilitationConfiguration> ResolveAsync(
            Guid businessId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DianHabilitationConfiguration(
                new Uri("https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc"),
                new FiscalCertificateReference(businessId, "Test", "ephemeral", string.Empty),
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), 60 * 1024 * 1024));
    }

    private sealed class FixedProductionConfigurationProvider : IDianProductionConfigurationProvider
    {
        public Task<DianHabilitationConfiguration> ResolveAsync(
            Guid businessId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DianHabilitationConfiguration(
                new Uri("https://vpfe.dian.gov.co/WcfDianCustomerServices.svc"),
                new FiscalCertificateReference(businessId, "Test", "ephemeral", string.Empty),
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30),
                60 * 1024 * 1024));
    }

    private sealed class FixedClientFactory(IDianWcfClient client) : IDianWcfClientFactory
    {
        public Task<IDianWcfClient> CreateAsync(
            DianHabilitationConfiguration configuration,
            CancellationToken cancellationToken = default) => Task.FromResult(client);
    }

    private sealed class DeterministicClient : IDianWcfClient
    {
        public DianUploadDocumentResponse Upload { get; init; } = new();
        public IReadOnlyList<DianDocumentResponse> Status { get; init; } = [];
        public Exception? UploadException { get; init; }
        public DianNumberRangeResponseList Numbering { get; init; } = new();
        public DianDocumentResponse Bill { get; init; } = new();
        public int UploadCalls { get; private set; }
        public int BillCalls { get; private set; }

        public Task<DianUploadDocumentResponse> SendTestSetAsync(
            string fileName,
            byte[] contentFile,
            string testSetId,
            CancellationToken cancellationToken)
        {
            UploadCalls++;
            return UploadException is null
                ? Task.FromResult(Upload)
                : Task.FromException<DianUploadDocumentResponse>(UploadException);
        }

        public Task<IReadOnlyList<DianDocumentResponse>> GetStatusZipAsync(
            string trackId,
            CancellationToken cancellationToken) => Task.FromResult(Status);

        public Task<DianNumberRangeResponseList> GetNumberingRangeAsync(
            string accountCode,
            string accountCodeT,
            string softwareCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(Numbering);

        public Task<DianDocumentResponse> SendBillSyncAsync(
            string fileName,
            byte[] contentFile,
            CancellationToken cancellationToken)
        {
            BillCalls++;
            return Task.FromResult(Bill);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
