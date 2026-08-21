namespace Auraly.Contracts.Fiscal;

public static class FiscalPermissionCodes
{
    public const string DocumentsRead = "fiscal.documents.read";
    public const string ArtifactsRead = "fiscal.artifacts.read";
    public const string Retry = "fiscal.retry";
    public const string ConfigurationRead = "fiscal.configuration.read";
    public const string PosStatusSync = "fiscal.status.sync";
    public const string ConfigurationManage = "fiscal.configuration.manage";
}

public static class FiscalDocumentStatusCodes
{
    public const string LocallyIssuedPendingSync = "LocallyIssuedPendingSync";
    public const string FiscalIntegrityConflict = "FiscalIntegrityConflict";
    public const string MissingMandatoryFiscalData = "MissingMandatoryFiscalData";
    public const string PendingGeneration = "PendingGeneration";
    public const string XmlGenerated = "XmlGenerated";
    public const string SchemaValidationFailed = "SchemaValidationFailed";
    public const string Signed = "Signed";
    public const string SignatureFailed = "SignatureFailed";
    public const string PendingSubmission = "PendingSubmission";
    public const string Submitted = "Submitted";
    public const string PendingDianResult = "PendingDianResult";
    public const string DianAccepted = "DianAccepted";
    public const string DianRejected = "DianRejected";
    public const string RetryScheduled = "RetryScheduled";
    public const string ContingencyPending = "ContingencyPending";
    public const string PermanentFailure = "PermanentFailure";
}

public static class FiscalArtifactTypeCodes
{
    public const string UnsignedXml = "UnsignedXml";
    public const string SignedXml = "SignedXml";
    public const string SubmissionZip = "SubmissionZip";
    public const string DianApplicationResponse = "DianApplicationResponse";
    public const string SanitizedSoapRequest = "SanitizedSoapRequest";
    public const string SanitizedSoapResponse = "SanitizedSoapResponse";
}

public sealed record FiscalCertificateReference(
    Guid BusinessId,
    string Provider,
    string KeyReference,
    string ExpectedThumbprint);

public sealed record DianHabilitationConfiguration(
    Uri Endpoint,
    FiscalCertificateReference Certificate,
    TimeSpan OpenTimeout,
    TimeSpan SendTimeout,
    TimeSpan ReceiveTimeout,
    long MaximumMessageBytes);

public interface IDianHabilitationConfigurationProvider
{
    Task<DianHabilitationConfiguration> ResolveAsync(
        Guid businessId,
        CancellationToken cancellationToken = default);
}

public interface IDianProductionConfigurationProvider
{
    Task<DianHabilitationConfiguration> ResolveAsync(
        Guid businessId,
        CancellationToken cancellationToken = default);
}

public sealed record FiscalSigningRequest(
    Guid BusinessId,
    string SupplierTaxId,
    byte[] UnsignedXml,
    FiscalCertificateReference Certificate,
    DateTimeOffset SigningTime);

public sealed record FiscalSigningResult(
    byte[] SignedXml,
    string Sha256Hex,
    string CertificateThumbprint,
    DateTimeOffset SignedAt);

public interface IFiscalXmlSigner
{
    Task<FiscalSigningResult> SignAsync(
        FiscalSigningRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record DianSubmissionRequest(
    Guid BusinessId,
    Guid DocumentId,
    string FileName,
    byte[] ZipContent,
    string? TestSetId,
    string? TrackId,
    string CorrelationId);

public enum DianSubmissionDisposition
{
    Received,
    Pending,
    Accepted,
    Rejected,
    TransientFailure,
    PermanentFailure
}

public sealed record DianSubmissionResult(
    DianSubmissionDisposition Disposition,
    string? TrackId,
    string? StatusCode,
    string? StatusDescription,
    byte[]? ApplicationResponse,
    byte[] SanitizedResponse,
    bool MayHaveReachedDian);

public interface IDianHabilitationTransport
{
    Task<DianSubmissionResult> SubmitTestSetAsync(
        DianSubmissionRequest request,
        CancellationToken cancellationToken = default);

    Task<DianSubmissionResult> GetStatusZipAsync(
        DianSubmissionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IDianProductionTransport
{
    Task<DianSubmissionResult> SubmitBillSyncAsync(
        DianSubmissionRequest request,
        CancellationToken cancellationToken = default);
}

public static class DianOperationCodes
{
    public const string SendTestSet = "SendTestSetAsync";
    public const string GetStatusZip = "GetStatusZip";
    public const string SendBillSync = "SendBillSync";
}
