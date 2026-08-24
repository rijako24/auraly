namespace Auraly.Contracts.Organization;

public sealed record SalesWorkspaceOption(
    Guid BusinessId,
    string BusinessName,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    bool WarehouseAllowsNegativeStockSales,
    bool HasActiveEdgeEnrollment);

public sealed record SalesWorkspaceBootstrap(
    Guid TenantId,
    string TenantName,
    Guid UserId,
    string UserDisplayName,
    IReadOnlyList<SalesWorkspaceOption> Options,
    bool CanEnrollPosDevice);

public sealed record SalesWorkspaceSelection(
    Guid BusinessId,
    Guid WarehouseId);

public sealed record SalesWorkspaceContext(
    Guid BusinessId,
    string BusinessName,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    bool WarehouseAllowsNegativeStockSales);

public sealed record CreatePosEnrollmentRequest(
    Guid BusinessId,
    Guid WarehouseId,
    string DeviceName);

public sealed record PosEnrollmentAuthorization(
    Guid EnrollmentSessionId,
    string RedemptionCode,
    DateTimeOffset ExpiresAt,
    SalesWorkspaceContext Workspace);

public sealed record RedeemPosEnrollmentRequest(
    Guid EnrollmentSessionId,
    string RedemptionCode,
    string InstallationId,
    Guid? ExistingDeviceId = null);

public sealed record PosEnrollmentDocumentSeries(
    Guid SeriesId,
    string DocumentType,
    string Prefix,
    string SeriesCode,
    int Padding,
    long RangeStart,
    long RangeEnd);

public sealed record PosEnrollmentFiscalSeries(
    Guid SeriesId,
    Guid FiscalAuthorizationId,
    string Prefix,
    string AuthorizationNumber,
    long RangeStart,
    long RangeEnd,
    DateOnly ValidUntil,
    int Environment,
    string SupplierTaxId,
    string TechnicalKey,
    string TechnicalKeyVersion,
    string QrValidationUrl,
    DateOnly? ValidFrom = null);

public sealed record PosEnrollmentPackage(
    Guid DeviceId,
    string DeviceSecret,
    Guid TenantId,
    Guid BusinessId,
    Guid WarehouseId,
    string BusinessName,
    string WarehouseCode,
    string WarehouseName,
    bool WarehouseAllowsNegativeStock,
    Guid InitialUserId,
    string InitialUserDisplayName,
    IReadOnlyList<string> Permissions,
    PosEnrollmentDocumentSeries DocumentSeries,
    PosEnrollmentFiscalSeries? FiscalSeries,
    PosEnrollmentDocumentSeries ReceiptDocumentSeries,
    IReadOnlyDictionary<string, string>? OfflineLeaseTrustedPublicKeys,
    DateTimeOffset EnrolledAt);
