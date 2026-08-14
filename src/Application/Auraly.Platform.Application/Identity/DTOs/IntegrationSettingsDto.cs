namespace Auraly.Platform.Application.Identity.DTOs;

public record IntegrationSettingsDto(
    GoogleCalendarIntegrationDto GoogleCalendar,
    WompiIntegrationDto Wompi,
    SiigoCommerceIntegrationDto SiigoCommerce,
    MantisIntegrationDto Mantis,
    XionIntegrationDto Xion);

public record MantisIntegrationDto(
    bool IsConfigured,
    bool IsEnabled,
    string BaseUrl,
    int RequestTimeoutSeconds,
    string Currency,
    bool HasAuthorizationToken,
    string? LastError,
    DateTime? LastSyncAt);

public record UpdateMantisIntegrationRequest(
    bool IsEnabled,
    string BaseUrl,
    int RequestTimeoutSeconds,
    string Currency,
    string? AuthorizationToken);

public record XionIntegrationDto(
    bool IsConfigured,
    bool IsEnabled,
    string BaseUrl,
    int RequestTimeoutSeconds,
    string Currency,
    int SucursalId,
    int VendedorId,
    int EquipoId,
    int BodegaId,
    int EmpresaId,
    int CentroDeCostoId,
    int UsuarioId,
    int RutaId,
    bool ValidateStockOnCreate,
    int OrderHistoryDays,
    string? LastError,
    DateTime? LastSyncAt);

public record UpdateXionIntegrationRequest(
    bool IsEnabled,
    string BaseUrl,
    int RequestTimeoutSeconds,
    string Currency,
    int SucursalId,
    int VendedorId,
    int EquipoId,
    int BodegaId,
    int EmpresaId,
    int CentroDeCostoId,
    int UsuarioId,
    int RutaId,
    bool ValidateStockOnCreate,
    int OrderHistoryDays);
public record GoogleCalendarIntegrationDto(
    bool IsEnabled,
    string CalendarId,
    string TimeZone,
    string? Scopes,
    bool HasClientId,
    bool HasClientSecret,
    bool HasRefreshToken,
    string? LastError,
    DateTime? LastSyncAt);

public record WompiIntegrationDto(
    bool IsEnabled,
    string Mode,
    string SandboxBaseUrl,
    string ProductionBaseUrl,
    int RequestTimeoutSeconds,
    string CheckoutBaseUrl,
    bool HasPrivateKey,
    bool HasPublicKey,
    bool HasEventsSecret,
    bool HasIntegritySecret,
    string? LastError,
    DateTime? LastSyncAt);

public record UpdateGoogleCalendarIntegrationRequest(
    bool IsEnabled,
    string CalendarId,
    string TimeZone,
    string? Scopes,
    string? ClientId,
    string? ClientSecret,
    string? RefreshToken);

public record UpdateWompiIntegrationRequest(
    bool IsEnabled,
    string Mode,
    string SandboxBaseUrl,
    string ProductionBaseUrl,
    int RequestTimeoutSeconds,
    string CheckoutBaseUrl,
    string? PrivateKey,
    string? PublicKey,
    string? EventsSecret,
    string? IntegritySecret);

public record UpdateOperationalModeRequest(string Mode);

public record SiigoCommerceIntegrationDto(
    bool IsEnabled,
    string BaseUrl,
    string PartnerId,
    int RequestTimeoutSeconds,
    int DefaultPageSize,
    bool CacheProducts,
    int PriceListPosition,
    int DocumentId,
    int PaymentTypeId,
    int? SellerId,
    int? CostCenterId,
    bool StampSend,
    bool MailSend,
    string DefaultCurrencyCode,
    IReadOnlyList<int> DefaultTaxIds,
    string DefaultCustomerPersonType,
    string DefaultCustomerIdType,
    string DefaultCustomerIdentification,
    int DefaultCustomerBranchOffice,
    bool HasUsername,
    bool HasAccessKey,
    string? LastError,
    DateTime? LastSyncAt);

public record UpdateSiigoCommerceIntegrationRequest(
    bool IsEnabled,
    string BaseUrl,
    string PartnerId,
    int RequestTimeoutSeconds,
    int DefaultPageSize,
    bool CacheProducts,
    int PriceListPosition,
    int DocumentId,
    int PaymentTypeId,
    int? SellerId,
    int? CostCenterId,
    bool StampSend,
    bool MailSend,
    string DefaultCurrencyCode,
    IReadOnlyList<int>? DefaultTaxIds,
    string DefaultCustomerPersonType,
    string DefaultCustomerIdType,
    string DefaultCustomerIdentification,
    int DefaultCustomerBranchOffice,
    string? Username,
    string? AccessKey);

public record MantisChannelWarehouseDto(
    Guid BusinessWhatsAppNumberId,
    string PhoneNumber,
    string WhatsAppPhoneNumberId,
    string? WarehouseCode,
    string? WarehouseName,
    bool IsActive);

public record UpdateMantisChannelWarehousesRequest(
    IReadOnlyList<UpdateMantisChannelWarehouseRequest> Channels);

public record UpdateMantisChannelWarehouseRequest(
    Guid BusinessWhatsAppNumberId,
    string WarehouseCode,
    string? WarehouseName,
    bool IsActive = true);
