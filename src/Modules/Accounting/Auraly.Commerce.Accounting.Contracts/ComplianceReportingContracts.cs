namespace Auraly.Commerce.Accounting.Contracts;

public sealed record ComplianceReportDefinitionView(
    string AuthorityCode, short TaxYear, string FormatCode, short FormatVersion,
    string Name, string ReportKind, string ResolutionNumber, DateOnly ResolutionDate,
    string TechnicalAnnex, string SourceUrl, string SourceSha256);

public sealed record ComplianceConceptMappingView(
    Guid MappingId, Guid TenantId, Guid? BusinessId, string AuthorityCode,
    short TaxYear, string FormatCode, short FormatVersion, Guid AccountId,
    string AccountCode, string AccountName, string ConceptCode, string TargetField);

public sealed record SetComplianceConceptMappingRequest(
    Guid? BusinessId, string AuthorityCode, short TaxYear, string FormatCode,
    short FormatVersion, Guid AccountId, string ConceptCode, string TargetField);

public sealed record GenerateComplianceReportRequest(
    string AuthorityCode, short TaxYear, string FormatCode, short FormatVersion,
    DateOnly PeriodFrom, DateOnly PeriodTo);

public sealed record ComplianceValidationView(
    string Severity, string Code, string Message, Guid? PartyId, Guid? AccountId);

public sealed record ComplianceReportRunView(
    Guid RunId, string AuthorityCode, short TaxYear, string FormatCode,
    short FormatVersion, string Name, string ReportKind, DateOnly PeriodFrom,
    DateOnly PeriodTo, string Status, string ResolutionNumber, string SourceUrl,
    string SourceSha256, int RowCount, decimal ControlTotal,
    DateTimeOffset CreatedAt, IReadOnlyList<ComplianceValidationView> Validations);

public sealed record ComplianceReportArtifact(
    Guid RunId, string FileName, string MediaType, byte[] Content, string ContentSha256);
