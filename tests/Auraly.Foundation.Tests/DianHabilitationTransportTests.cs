using Auraly.Contracts.Fiscal;
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
        public int UploadCalls { get; private set; }

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

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}