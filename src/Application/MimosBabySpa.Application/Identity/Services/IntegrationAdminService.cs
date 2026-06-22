using System.Text.Json;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public class IntegrationAdminService : IIntegrationAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public IntegrationAdminService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IntegrationSettingsDto> GetSettingsAsync(Guid tenantId, Guid businessId, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var connections = await _unitOfWork.IntegrationConnections.GetByBusinessIdAsync(businessId, ct);
        return MapSettings(connections);
    }

    public async Task<IntegrationSettingsDto> UpdateGoogleCalendarAsync(
        Guid tenantId,
        Guid businessId,
        UpdateGoogleCalendarIntegrationRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var connection = await GetOrCreateAsync(
            businessId,
            IntegrationProvider.GoogleCalendar,
            IntegrationCapability.Calendar,
            "Google Calendar",
            ct);

        var existingSecrets = ReadJson(connection.SecretsJson);
        var secrets = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["clientId"] = UseNewOrExisting(request.ClientId, existingSecrets, "clientId"),
            ["clientSecret"] = UseNewOrExisting(request.ClientSecret, existingSecrets, "clientSecret"),
            ["refreshToken"] = UseNewOrExisting(request.RefreshToken, existingSecrets, "refreshToken")
        };

        connection.Name = "Google Calendar";
        connection.AccountIdentifier = string.IsNullOrWhiteSpace(request.CalendarId) ? "primary" : request.CalendarId.Trim();
        connection.SettingsJson = Serialize(new
        {
            calendarId = connection.AccountIdentifier,
            timeZone = string.IsNullOrWhiteSpace(request.TimeZone) ? "America/Bogota" : request.TimeZone.Trim(),
            scopes = request.Scopes?.Trim()
        });
        connection.SecretsJson = Serialize(secrets);
        connection.IsEnabled = request.IsEnabled;
        connection.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.IntegrationConnections.UpdateAsync(connection, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return await GetSettingsAsync(tenantId, businessId, ct);
    }

    public async Task<IntegrationSettingsDto> UpdateWompiAsync(
        Guid tenantId,
        Guid businessId,
        UpdateWompiIntegrationRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var connection = await GetOrCreateAsync(
            businessId,
            IntegrationProvider.Wompi,
            IntegrationCapability.Payments,
            "Wompi",
            ct);

        var settings = ReadJson(connection.SettingsJson);
        var mode = NormalizeWompiMode(string.IsNullOrWhiteSpace(request.Mode)
            ? Get(settings, "mode", "test")
            : request.Mode);
        var existingSecrets = ReadJson(connection.SecretsJson);
        var existingModeSecrets = ReadNested(connection.SecretsJson, mode);
        var secrets = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["privateKey"] = UseNewOrExisting(request.PrivateKey, existingModeSecrets, "privateKey") ?? GetNullable(existingSecrets, "privateKey"),
            ["publicKey"] = UseNewOrExisting(request.PublicKey, existingModeSecrets, "publicKey") ?? GetNullable(existingSecrets, "publicKey"),
            ["eventsSecret"] = UseNewOrExisting(request.EventsSecret, existingModeSecrets, "eventsSecret") ?? GetNullable(existingSecrets, "eventsSecret"),
            ["integritySecret"] = UseNewOrExisting(request.IntegritySecret, existingModeSecrets, "integritySecret") ?? GetNullable(existingSecrets, "integritySecret")
        };
        var allSecrets = new Dictionary<string, Dictionary<string, string?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["test"] = ReadNested(connection.SecretsJson, "test"),
            ["production"] = ReadNested(connection.SecretsJson, "production")
        };
        allSecrets[mode] = secrets;

        connection.Name = "Wompi";
        connection.AccountIdentifier = null;
        connection.SettingsJson = Serialize(new
        {
            mode,
            sandboxBaseUrl = string.IsNullOrWhiteSpace(request.SandboxBaseUrl) ? "https://sandbox.wompi.co/v1" : request.SandboxBaseUrl.Trim(),
            productionBaseUrl = string.IsNullOrWhiteSpace(request.ProductionBaseUrl) ? "https://production.wompi.co/v1" : request.ProductionBaseUrl.Trim(),
            requestTimeoutSeconds = request.RequestTimeoutSeconds <= 0 ? 30 : request.RequestTimeoutSeconds,
            checkoutBaseUrl = string.IsNullOrWhiteSpace(request.CheckoutBaseUrl) ? "https://checkout.wompi.co/l/" : request.CheckoutBaseUrl.Trim()
        });
        connection.SecretsJson = Serialize(allSecrets);
        connection.IsEnabled = request.IsEnabled;
        connection.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.IntegrationConnections.UpdateAsync(connection, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return await GetSettingsAsync(tenantId, businessId, ct);
    }

    public async Task<IntegrationSettingsDto> UpdateOperationalModeAsync(
        Guid tenantId,
        Guid businessId,
        UpdateOperationalModeRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var mode = NormalizeWompiMode(request.Mode);
        var connection = await GetOrCreateAsync(
            businessId,
            IntegrationProvider.Wompi,
            IntegrationCapability.Payments,
            "Wompi",
            ct);

        var settings = ReadJson(connection.SettingsJson);

        connection.Name = "Wompi";
        connection.AccountIdentifier = null;
        connection.SettingsJson = Serialize(new
        {
            mode,
            sandboxBaseUrl = Get(settings, "sandboxBaseUrl", "https://sandbox.wompi.co/v1"),
            productionBaseUrl = Get(settings, "productionBaseUrl", "https://production.wompi.co/v1"),
            requestTimeoutSeconds = GetInt(settings, "requestTimeoutSeconds", 30),
            checkoutBaseUrl = Get(settings, "checkoutBaseUrl", "https://checkout.wompi.co/l/")
        });
        connection.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.IntegrationConnections.UpdateAsync(connection, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return await GetSettingsAsync(tenantId, businessId, ct);
    }

    public async Task<IntegrationSettingsDto> UpdateSiigoCommerceAsync(
        Guid tenantId,
        Guid businessId,
        UpdateSiigoCommerceIntegrationRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var connection = await _unitOfWork.IntegrationConnections.GetCommerceConnectionAsync(
            businessId,
            CommerceProvider.Siigo,
            CommerceCapability.CatalogAndOrders,
            ct);

        if (connection is null)
        {
            connection = new IntegrationConnection
            {
                IntegrationConnectionId = Guid.NewGuid(),
                BusinessId = businessId,
                ConnectionType = ConnectionType.Commerce,
                Provider = (int)CommerceProvider.Siigo,
                Capability = (int)CommerceCapability.CatalogAndOrders,
                Name = "Siigo Commerce",
                SettingsJson = "{}",
                IsEnabled = false,
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.IntegrationConnections.CreateAsync(connection, ct);
        }

        var existingSecrets = ReadJson(connection.SecretsJson);
        var secrets = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["username"] = UseNewOrExisting(request.Username, existingSecrets, "username"),
            ["accessKey"] = UseNewOrExisting(request.AccessKey, existingSecrets, "accessKey")
        };

        connection.Name = "Siigo Commerce";
        connection.AccountIdentifier = secrets["username"];
        connection.SettingsJson = Serialize(new
        {
            baseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? "https://api.siigo.com/" : request.BaseUrl.Trim(),
            partnerId = string.IsNullOrWhiteSpace(request.PartnerId) ? "auraly" : request.PartnerId.Trim(),
            requestTimeoutSeconds = request.RequestTimeoutSeconds <= 0 ? 30 : request.RequestTimeoutSeconds,
            catalog = new
            {
                defaultPageSize = request.DefaultPageSize <= 0 ? 25 : request.DefaultPageSize,
                cacheProducts = request.CacheProducts,
                priceListPosition = request.PriceListPosition <= 0 ? 1 : request.PriceListPosition
            },
            order = new
            {
                documentType = "FV",
                documentId = request.DocumentId,
                paymentTypeId = request.PaymentTypeId,
                sellerId = request.SellerId,
                costCenterId = request.CostCenterId,
                stampSend = request.StampSend,
                mailSend = request.MailSend,
                defaultCurrencyCode = string.IsNullOrWhiteSpace(request.DefaultCurrencyCode) ? "COP" : request.DefaultCurrencyCode.Trim(),
                defaultTaxIds = request.DefaultTaxIds ?? [],
                defaultCustomer = new
                {
                    personType = string.IsNullOrWhiteSpace(request.DefaultCustomerPersonType) ? "Person" : request.DefaultCustomerPersonType.Trim(),
                    idType = string.IsNullOrWhiteSpace(request.DefaultCustomerIdType) ? "13" : request.DefaultCustomerIdType.Trim(),
                    identification = string.IsNullOrWhiteSpace(request.DefaultCustomerIdentification) ? "222222222222" : request.DefaultCustomerIdentification.Trim(),
                    branchOffice = request.DefaultCustomerBranchOffice
                }
            }
        });
        connection.SecretsJson = Serialize(secrets);
        connection.IsEnabled = request.IsEnabled;
        connection.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.IntegrationConnections.UpdateAsync(connection, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return await GetSettingsAsync(tenantId, businessId, ct);
    }

    private async Task<IntegrationConnection> GetOrCreateAsync(
        Guid businessId,
        IntegrationProvider provider,
        IntegrationCapability capability,
        string name,
        CancellationToken ct)
    {
        var existing = await _unitOfWork.IntegrationConnections.GetByBusinessProviderCapabilityAsync(
            businessId,
            provider,
            capability,
            ct);

        if (existing is not null)
            return existing;

        var connection = new IntegrationConnection
        {
            IntegrationConnectionId = Guid.NewGuid(),
            BusinessId = businessId,
            ConnectionType = ConnectionType.Integration,
            Provider = (int)provider,
            Capability = (int)capability,
            Name = name,
            SettingsJson = "{}",
            IsEnabled = false,
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.IntegrationConnections.CreateAsync(connection, ct);
        return connection;
    }

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }

    private static IntegrationSettingsDto MapSettings(IReadOnlyList<IntegrationConnection> connections)
    {
        var google = connections.FirstOrDefault(c =>
            c.ConnectionType == ConnectionType.Integration &&
            c.Provider == (int)IntegrationProvider.GoogleCalendar &&
            c.Capability == (int)IntegrationCapability.Calendar);
        var wompi = connections.FirstOrDefault(c =>
            c.ConnectionType == ConnectionType.Integration &&
            c.Provider == (int)IntegrationProvider.Wompi &&
            c.Capability == (int)IntegrationCapability.Payments);
        var siigo = connections.FirstOrDefault(c =>
            c.ConnectionType == ConnectionType.Commerce &&
            c.Provider == (int)CommerceProvider.Siigo &&
            c.Capability == (int)CommerceCapability.CatalogAndOrders);

        return new IntegrationSettingsDto(
            MapGoogle(google),
            MapWompi(wompi),
            MapSiigoCommerce(siigo));
    }

    private static GoogleCalendarIntegrationDto MapGoogle(IntegrationConnection? connection)
    {
        var settings = ReadJson(connection?.SettingsJson);
        var secrets = ReadJson(connection?.SecretsJson);
        return new GoogleCalendarIntegrationDto(
            connection?.IsEnabled ?? false,
            Get(settings, "calendarId", "primary"),
            Get(settings, "timeZone", "America/Bogota"),
            GetNullable(settings, "scopes"),
            Has(secrets, "clientId"),
            Has(secrets, "clientSecret"),
            Has(secrets, "refreshToken"),
            connection?.LastError,
            connection?.LastSyncAt);
    }

    private static WompiIntegrationDto MapWompi(IntegrationConnection? connection)
    {
        var settings = ReadJson(connection?.SettingsJson);
        var mode = NormalizeWompiMode(Get(settings, "mode", "test"));
        var rootSecrets = ReadJson(connection?.SecretsJson);
        var secrets = ReadNested(connection?.SecretsJson, mode);
        return new WompiIntegrationDto(
            connection?.IsEnabled ?? false,
            mode,
            Get(settings, "sandboxBaseUrl", "https://sandbox.wompi.co/v1"),
            Get(settings, "productionBaseUrl", "https://production.wompi.co/v1"),
            GetInt(settings, "requestTimeoutSeconds", 30),
            Get(settings, "checkoutBaseUrl", "https://checkout.wompi.co/l/"),
            Has(secrets, "privateKey") || Has(rootSecrets, "privateKey"),
            Has(secrets, "publicKey") || Has(rootSecrets, "publicKey"),
            Has(secrets, "eventsSecret") || Has(rootSecrets, "eventsSecret"),
            Has(secrets, "integritySecret") || Has(rootSecrets, "integritySecret"),
            connection?.LastError,
            connection?.LastSyncAt);
    }

    private static SiigoCommerceIntegrationDto MapSiigoCommerce(IntegrationConnection? connection)
    {
        var settings = ReadJson(connection?.SettingsJson);
        var secrets = ReadJson(connection?.SecretsJson);
        var catalog = ReadNested(connection?.SettingsJson, "catalog");
        var order = ReadNested(connection?.SettingsJson, "order");
        var defaultCustomer = ReadNested(connection?.SettingsJson, "order", "defaultCustomer");

        return new SiigoCommerceIntegrationDto(
            connection?.IsEnabled ?? false,
            Get(settings, "baseUrl", "https://api.siigo.com/"),
            Get(settings, "partnerId", "auraly"),
            GetInt(settings, "requestTimeoutSeconds", 30),
            GetInt(catalog, "defaultPageSize", 25),
            GetBool(catalog, "cacheProducts", true),
            GetInt(catalog, "priceListPosition", 1),
            GetInt(order, "documentId", 0),
            GetInt(order, "paymentTypeId", 0),
            GetNullableInt(order, "sellerId"),
            GetNullableInt(order, "costCenterId"),
            GetBool(order, "stampSend", false),
            GetBool(order, "mailSend", false),
            Get(order, "defaultCurrencyCode", "COP"),
            GetIntList(order, "defaultTaxIds"),
            Get(defaultCustomer, "personType", "Person"),
            Get(defaultCustomer, "idType", "13"),
            Get(defaultCustomer, "identification", "222222222222"),
            GetInt(defaultCustomer, "branchOffice", 0),
            Has(secrets, "username"),
            Has(secrets, "accessKey"),
            connection?.LastError,
            connection?.LastSyncAt);
    }

    private static string? UseNewOrExisting(string? incoming, Dictionary<string, string?> existing, string key)
    {
        return incoming is null ? GetNullable(existing, key) : incoming.Trim();
    }

    private static Dictionary<string, string?> ReadJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.EnumerateObject()
                .ToDictionary(
                    p => p.Name,
                    p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    private static bool Has(Dictionary<string, string?> values, string key) => !string.IsNullOrWhiteSpace(GetNullable(values, key));
    private static string? GetNullable(Dictionary<string, string?> values, string key) => values.TryGetValue(key, out var value) ? value : null;
    private static string Get(Dictionary<string, string?> values, string key, string fallback) => GetNullable(values, key) ?? fallback;
    private static bool GetBool(Dictionary<string, string?> values, string key, bool fallback) =>
        bool.TryParse(GetNullable(values, key), out var parsed) ? parsed : fallback;
    private static int GetInt(Dictionary<string, string?> values, string key, int fallback) =>
        int.TryParse(GetNullable(values, key), out var parsed) ? parsed : fallback;

    private static int? GetNullableInt(Dictionary<string, string?> values, string key) =>
        int.TryParse(GetNullable(values, key), out var parsed) ? parsed : null;

    private static IReadOnlyList<int> GetIntList(Dictionary<string, string?> values, string key)
    {
        var raw = GetNullable(values, key);
        if (string.IsNullOrWhiteSpace(raw))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];
            return doc.RootElement.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out _))
                .Select(e => e.GetInt32())
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static Dictionary<string, string?> ReadNested(string? json, params string[] path)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(json);
            var current = document.RootElement;
            foreach (var segment in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                    return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            }

            if (current.ValueKind != JsonValueKind.Object)
                return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            return current.EnumerateObject()
                .ToDictionary(
                    p => p.Name,
                    p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string NormalizeWompiMode(string? mode)
    {
        return string.Equals(mode, "production", StringComparison.OrdinalIgnoreCase)
            ? "production"
            : "test";
    }
}
