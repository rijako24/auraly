namespace Auraly.Contracts.Fiscal;

public static class FiscalOnboardingStages
{
    public const string NotConfigured = nameof(NotConfigured);
    public const string HabilitationReady = nameof(HabilitationReady);
    public const string HabilitationAccepted = nameof(HabilitationAccepted);
    public const string ProductionReady = nameof(ProductionReady);
    public const string ProductionActive = nameof(ProductionActive);
}

public sealed record FiscalOnboardingConfiguration(
    Guid BusinessId,
    string BusinessName,
    string LegalName,
    string SupplierTaxId,
    string SupplierCheckDigit,
    string Stage,
    string? SoftwareIdentificationCode,
    Guid? TestSetId,
    bool HasCertificate,
    string? CertificateThumbprintSuffix,
    DateTimeOffset? CertificateValidFrom,
    DateTimeOffset? CertificateValidTo,
    bool HabilitationAccepted,
    DateTimeOffset? HabilitationAcceptedAt,
    bool ProductionActive,
    DianNumberingRangeOption? AssignedRange,
    IReadOnlyList<DianNumberingRangeOption> AvailableRanges,
    IReadOnlyList<string> MissingRequirements,
    FiscalHabilitationAttempt? LatestHabilitationAttempt,
    DianNumberingRangeOption? AssignedSupportDocumentRange = null);

public sealed record FiscalHabilitationAttempt(
    Guid DocumentId,
    string Status,
    bool IsTerminalFailure,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset UpdatedAt);

public sealed record DianNumberingRangeOption(
    Guid DianNumberingRangeId,
    string AuthorizationNumber,
    DateOnly? ResolutionDate,
    string Prefix,
    long RangeStart,
    long RangeEnd,
    DateOnly ValidFrom,
    DateOnly ValidUntil,
    bool IsAvailable,
    Guid? AssignedBusinessId,
    string? AssignedBusinessName);

public sealed record SaveDianHabilitationConfiguration(
    string SoftwareIdentificationCode,
    string SoftwarePin,
    Guid TestSetId,
    string CertificatePassword,
    byte[] CertificatePfx);

public sealed record ImportedDianNumberingRange(
    string AuthorizationNumber,
    DateOnly? ResolutionDate,
    string Prefix,
    long RangeStart,
    long RangeEnd,
    DateOnly ValidFrom,
    DateOnly ValidUntil,
    string TechnicalKey);

public sealed record FiscalCredentialReference(
    string Provider,
    string SoftwarePinReference,
    string CertificateKeyReference,
    string CertificateThumbprint,
    DateTimeOffset CertificateValidFrom,
    DateTimeOffset CertificateValidTo);

public sealed record DianNumberingRangeContext(
    Guid BusinessId,
    string SupplierTaxId,
    string SoftwareOwnerTaxId,
    string SoftwareIdentificationCode,
    FiscalCertificateReference Certificate);
