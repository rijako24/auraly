using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Security;
using System.Text;
using System.Text.Json;
using Auraly.Contracts.Fiscal;

namespace Auraly.Infrastructure.Fiscal;


public interface IDianWcfClient : IAsyncDisposable
{
    Task<DianUploadDocumentResponse> SendTestSetAsync(
        string fileName,
        byte[] contentFile,
        string testSetId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DianDocumentResponse>> GetStatusZipAsync(
        string trackId,
        CancellationToken cancellationToken);
}

public interface IDianWcfClientFactory
{
    Task<IDianWcfClient> CreateAsync(
        DianHabilitationConfiguration configuration,
        CancellationToken cancellationToken = default);
}

public sealed class DianWcfClientFactory(IFiscalSigningCertificateProvider certificates)
    : IDianWcfClientFactory
{
    public async Task<IDianWcfClient> CreateAsync(
        DianHabilitationConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The DIAN endpoint must use HTTPS.");
        var material = await certificates.ResolveAsync(configuration.Certificate, cancellationToken);
        var binding = new WSHttpBinding(SecurityMode.TransportWithMessageCredential)
        {
            OpenTimeout = configuration.OpenTimeout,
            SendTimeout = configuration.SendTimeout,
            ReceiveTimeout = configuration.ReceiveTimeout,
            MaxReceivedMessageSize = configuration.MaximumMessageBytes,
            ReaderQuotas = System.Xml.XmlDictionaryReaderQuotas.Max
        };
        binding.Security.Transport.ClientCredentialType = HttpClientCredentialType.None;
        binding.Security.Message.ClientCredentialType = MessageCredentialType.Certificate;
        binding.Security.Message.AlgorithmSuite = SecurityAlgorithmSuite.Basic256Sha256;
        binding.Security.Message.EstablishSecurityContext = false;
        binding.Security.Message.NegotiateServiceCredential = false;
        var factory = new ChannelFactory<IDianCustomerServices>(
            binding,
            new EndpointAddress(configuration.Endpoint));
        factory.Credentials.ClientCertificate.Certificate = material.Certificate;
        var channel = factory.CreateChannel();
        ((ICommunicationObject)channel).Open();
        return new DianWcfClient(factory, channel);
    }
}

public sealed class DianHabilitationTransport(
    IDianHabilitationConfigurationProvider configurations,
    IDianWcfClientFactory clients) : IDianHabilitationTransport
{
    public async Task<DianSubmissionResult> SubmitTestSetAsync(
        DianSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateSubmission(request);
        try
        {
            var configuration = await configurations.ResolveAsync(request.BusinessId, cancellationToken);
            await using var client = await clients.CreateAsync(configuration, cancellationToken);
            var response = await client.SendTestSetAsync(
                request.FileName,
                request.ZipContent,
                request.TestSetId,
                cancellationToken);
            var errors = response.ErrorMessageList
                .Where(item => !item.Success || !string.IsNullOrWhiteSpace(item.ProcessedMessage))
                .Select(item => item.ProcessedMessage ?? "DIAN rejected the upload during initial validation.")
                .ToArray();
            if (errors.Length > 0 || string.IsNullOrWhiteSpace(response.ZipKey))
                return Result(DianSubmissionDisposition.Rejected, null, "InitialValidationRejected",
                    string.Join(" | ", errors), null, response, mayHaveReachedDian: true);
            return Result(DianSubmissionDisposition.Received, response.ZipKey, "Received",
                "DIAN assigned a ZipKey. GetStatusZip must be queried.", null, response, mayHaveReachedDian: true);
        }
        catch (TimeoutException exception)
        {
            return Failure(exception, mayHaveReachedDian: true);
        }
        catch (CommunicationException exception)
        {
            return Failure(exception, mayHaveReachedDian: false);
        }
    }

    public async Task<DianSubmissionResult> GetStatusZipAsync(
        DianSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.TrackId))
            throw new ArgumentException("A TrackId is required to query GetStatusZip.", nameof(request));
        try
        {
            var configuration = await configurations.ResolveAsync(request.BusinessId, cancellationToken);
            await using var client = await clients.CreateAsync(configuration, cancellationToken);
            var documents = await client.GetStatusZipAsync(request.TrackId, cancellationToken);
            if (documents.Count == 0)
                return Result(DianSubmissionDisposition.Pending, request.TrackId, "Pending",
                    "DIAN has not returned a document result yet.", null, documents, mayHaveReachedDian: true);
            var rejected = documents.FirstOrDefault(item => !item.IsValid);
            var response = rejected ?? documents[0];
            var disposition = rejected is null
                ? DianSubmissionDisposition.Accepted
                : DianSubmissionDisposition.Rejected;
            var applicationResponse = response.XmlBytes is { Length: > 0 }
                ? response.XmlBytes
                : response.XmlBase64Bytes;
            return Result(disposition, request.TrackId, response.StatusCode,
                response.StatusDescription ?? response.StatusMessage,
                applicationResponse, documents, mayHaveReachedDian: true);
        }
        catch (TimeoutException exception)
        {
            return Failure(exception, mayHaveReachedDian: true);
        }
        catch (CommunicationException exception)
        {
            return Failure(exception, mayHaveReachedDian: false);
        }
    }

    private static DianSubmissionResult Failure(Exception exception, bool mayHaveReachedDian) =>
        new(DianSubmissionDisposition.TransientFailure, null, exception.GetType().Name,
            "The DIAN transport failed transiently. The document number and CUFE must be preserved.",
            null, Array.Empty<byte>(), mayHaveReachedDian);

    private static DianSubmissionResult Result(
        DianSubmissionDisposition disposition,
        string? trackId,
        string? statusCode,
        string? description,
        byte[]? applicationResponse,
        object response,
        bool mayHaveReachedDian) =>
        new(disposition, trackId, statusCode, description, applicationResponse,
            JsonSerializer.SerializeToUtf8Bytes(response), mayHaveReachedDian);

    private static void ValidateSubmission(DianSubmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BusinessId == Guid.Empty || request.DocumentId == Guid.Empty)
            throw new ArgumentException("BusinessId and DocumentId are required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.FileName) ||
            !request.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The DIAN payload file name must end in .zip.", nameof(request));
        if (request.ZipContent.Length == 0 || request.ZipContent.Length > 50 * 1024 * 1024)
            throw new ArgumentException("The DIAN ZIP must contain between 1 byte and 50 MB.", nameof(request));
        if (!Guid.TryParse(request.TestSetId, out _))
            throw new ArgumentException("TestSetId must be a valid DIAN identifier.", nameof(request));
    }
}

[ServiceContract(Namespace = "http://wcf.dian.colombia")]
public interface IDianCustomerServices
{
    [OperationContract(
        Name = "SendTestSetAsync",
        Action = "http://wcf.dian.colombia/IWcfDianCustomerServices/SendTestSetAsync",
        ReplyAction = "http://wcf.dian.colombia/IWcfDianCustomerServices/SendTestSetAsyncResponse")]
    Task<DianUploadDocumentResponse> SendTestSetAsync(
        string fileName,
        byte[] contentFile,
        string testSetId);

    [OperationContract(
        Name = "GetStatusZip",
        Action = "http://wcf.dian.colombia/IWcfDianCustomerServices/GetStatusZip",
        ReplyAction = "http://wcf.dian.colombia/IWcfDianCustomerServices/GetStatusZipResponse")]
    Task<DianDocumentResponse[]> GetStatusZipAsync(string trackId);
}

internal sealed class DianWcfClient(
    ChannelFactory<IDianCustomerServices> factory,
    IDianCustomerServices channel) : IDianWcfClient
{
    public async Task<DianUploadDocumentResponse> SendTestSetAsync(
        string fileName,
        byte[] contentFile,
        string testSetId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await channel.SendTestSetAsync(fileName, contentFile, testSetId)
            .WaitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DianDocumentResponse>> GetStatusZipAsync(
        string trackId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await channel.GetStatusZipAsync(trackId).WaitAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        var communication = (ICommunicationObject)channel;
        try
        {
            if (communication.State != CommunicationState.Faulted)
                communication.Close(TimeSpan.FromSeconds(10));
            else
                communication.Abort();
            factory.Close();
        }
        catch (CommunicationException)
        {
            communication.Abort();
            factory.Abort();
        }
        catch (TimeoutException)
        {
            communication.Abort();
            factory.Abort();
        }
        return ValueTask.CompletedTask;
    }
}

[DataContract(Name = "UploadDocumentResponse", Namespace = "http://schemas.datacontract.org/2004/07/UploadDocumentResponse")]
public sealed class DianUploadDocumentResponse
{
    [DataMember(Order = 0)]
    public DianUploadError[] ErrorMessageList { get; set; } = Array.Empty<DianUploadError>();

    [DataMember(Order = 1)]
    public string? ZipKey { get; set; }
}

[DataContract(Name = "XmlParamsResponseTrackId", Namespace = "http://schemas.datacontract.org/2004/07/XmlParamsResponseTrackId")]
public sealed class DianUploadError
{
    [DataMember(Order = 0)] public string? DocumentKey { get; set; }
    [DataMember(Order = 1)] public string? ProcessedMessage { get; set; }
    [DataMember(Order = 2)] public string? SenderCode { get; set; }
    [DataMember(Order = 3)] public bool Success { get; set; }
    [DataMember(Order = 4)] public string? XmlFileName { get; set; }
}

[DataContract(Name = "DianResponse", Namespace = "http://schemas.datacontract.org/2004/07/DianResponse")]
public sealed class DianDocumentResponse
{
    [DataMember(Order = 0)] public string[] ErrorMessage { get; set; } = Array.Empty<string>();
    [DataMember(Order = 1)] public bool IsValid { get; set; }
    [DataMember(Order = 2)] public string? StatusCode { get; set; }
    [DataMember(Order = 3)] public string? StatusDescription { get; set; }
    [DataMember(Order = 4)] public string? StatusMessage { get; set; }
    [DataMember(Order = 5)] public byte[]? XmlBase64Bytes { get; set; }
    [DataMember(Order = 6)] public byte[]? XmlBytes { get; set; }
    [DataMember(Order = 7)] public string? XmlDocumentKey { get; set; }
    [DataMember(Order = 8)] public string? XmlFileName { get; set; }
}