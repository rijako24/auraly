namespace Auraly.Contracts.Organization;

public sealed record OnlineRegisterOption(
    Guid BusinessId, string BusinessName,
    Guid RegisterId, string RegisterCode, string RegisterName,
    Guid WarehouseId, string WarehouseCode, string WarehouseName,
    bool WarehouseAllowsNegativeStockSales,
    bool HasActiveEdgeEnrollment);

public sealed record OnlineRegisterBootstrap(
    string UserDisplayName,
    IReadOnlyList<OnlineRegisterOption> Options,
    bool CanEnrollPosDevice);

public sealed record OnlineRegisterSelection(
    Guid BusinessId,
    Guid RegisterId);

public sealed record OnlineRegisterContext(
    Guid BusinessId, string BusinessName,
    Guid RegisterId, string RegisterCode, string RegisterName,
    Guid WarehouseId, string WarehouseCode, string WarehouseName,
    bool WarehouseAllowsNegativeStockSales);

public sealed record CreatePosEnrollmentRequest(
    Guid BusinessId,
    Guid RegisterId,
    string DeviceName);

public sealed record PosEnrollmentAuthorization(
    Guid EnrollmentSessionId,
    string RedemptionCode,
    DateTimeOffset ExpiresAt,
    OnlineRegisterContext Register);

public sealed record RedeemPosEnrollmentRequest(
    Guid EnrollmentSessionId,
    string RedemptionCode,
    string InstallationId);

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
    string QrValidationUrl);

public sealed record PosEnrollmentPackage(
    Guid DeviceId,
    string DeviceSecret,
    Guid TenantId,
    Guid BusinessId,
    Guid WarehouseId,
    Guid RegisterId,
    string RegisterCode,
    string RegisterName,
    bool WarehouseAllowsNegativeStock,
    Guid InitialUserId,
    string InitialUserDisplayName,
    IReadOnlyList<string> Permissions,
    PosEnrollmentDocumentSeries DocumentSeries,
    PosEnrollmentFiscalSeries FiscalSeries,
    DateTimeOffset EnrolledAt);

public sealed record PosEnrollmentReceipt(
    Guid DeviceId,
    Guid RegisterId,
    string RegisterCode,
    string RegisterName,
    DateTimeOffset EnrolledAt);
