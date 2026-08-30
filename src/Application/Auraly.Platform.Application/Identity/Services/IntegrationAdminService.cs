using System.Text.Json;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

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
        var settings = ReadJson(connection.SettingsJson);

        connection.Name = "Google Calendar";
        connection.AccountIdentifier = string.IsNullOrWhiteSpace(request.CalendarId) ? null : request.CalendarId.Trim();
        connection.SettingsJson = Serialize(new
        {
            calendarId = connection.AccountIdentifier ?? string.Empty,
            platformConfigurationId = (int)SystemConfigurationKey.GoogleCalendarPlatformCredentials,
            autoCreateCalendar = true,
            calendarSummary = GetNullable(settings, "calendarSummary"),
            timeZone = string.IsNullOrWhiteSpace(request.TimeZone) ? "America/Bogota" : request.TimeZone.Trim(),
            sharedWithEmail = GetNullable(settings, "sharedWithEmail"),
            sharedRole = Get(settings, "sharedRole", "writer"),
            sendSharingNotifications = GetBool(settings, "sendSharingNotifications", true),
            insertIntoSharedCalendarList = GetBool(settings, "insertIntoSharedCalendarList", false)
        });
        connection.SecretsJson = null;
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

        ValidateWompiConfiguration(request, mode, secrets);

        connection.Name = "Wompi";
        connection.AccountIdentifier = null;
        connection.SettingsJson = Serialize(new
        {
            mode,
            sandboxBaseUrl = NormalizeOfficialWompiUrl(request.SandboxBaseUrl, "https://sandbox.wompi.co/v1", "sandboxBaseUrl"),
            productionBaseUrl = NormalizeOfficialWompiUrl(request.ProductionBaseUrl, "https://production.wompi.co/v1", "productionBaseUrl"),
            requestTimeoutSeconds = request.RequestTimeoutSeconds <= 0 ? 30 : request.RequestTimeoutSeconds,
            checkoutBaseUrl = NormalizeOfficialWompiUrl(request.CheckoutBaseUrl, "https://checkout.wompi.co/l/", "checkoutBaseUrl")
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
            partnerId = string.IsNullOrWhiteSpace(request.PartnerId) ? string.Empty : request.PartnerId.Trim(),
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

    public async Task<IntegrationSettingsDto> UpdateMantisAsync(
        Guid tenantId,
        Guid businessId,
        UpdateMantisIntegrationRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var baseUrl = request.BaseUrl?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBaseUrl)
            || (!parsedBaseUrl.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !parsedBaseUrl.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            throw new DomainValidationException("BaseUrl", "Mantis requiere una URL base HTTP o HTTPS valida.");

        var connection = await GetOrCreateCommerceAsync(
            businessId, CommerceProvider.Mantis, "Mantis Commerce", ct);
        var settings = ReadJson(connection.SettingsJson);
        var catalog = ReadNested(connection.SettingsJson, "catalog");
        var customer = ReadNested(connection.SettingsJson, "customer");
        var genericCustomer = ReadNested(connection.SettingsJson, "genericCustomer");
        var order = ReadNested(connection.SettingsJson, "order");
        var existingSecrets = ReadJson(connection.SecretsJson);

        connection.Name = "Mantis Commerce";
        connection.SettingsJson = Serialize(new
        {
            baseUrl,
            requestTimeoutSeconds = request.RequestTimeoutSeconds <= 0 ? 30 : request.RequestTimeoutSeconds,
            currency = string.IsNullOrWhiteSpace(request.Currency) ? "COP" : request.Currency.Trim(),
            genericCustomer = new
            {
                llaveNit = Get(genericCustomer, "llaveNit", string.Empty),
                llaveCliente = Get(genericCustomer, "llaveCliente", string.Empty)
            },
            catalog = new
            {
                searchEndpoint = Get(catalog, "searchEndpoint", "pwsConsultarArticuloCasalins"),
                defaultPageSize = GetInt(catalog, "defaultPageSize", 5),
                maxPageSize = GetInt(catalog, "maxPageSize", 20),
                warehouse = Get(catalog, "warehouse", Get(settings, "warehouse", string.Empty))
            },
            customer = new
            {
                searchEndpoint = Get(customer, "searchEndpoint", "pwsConsultarClientesCasalins"),
                countryCode = Get(customer, "countryCode", "57"),
                nationalPhoneLength = GetInt(customer, "nationalPhoneLength", 10)
            },
            order = new
            {
                createEndpoint = Get(order, "createEndpoint", "pwsCrearPedidoCasalins"),
                queryEndpoint = Get(order, "queryEndpoint", "pwsConsultarPedidoCasalins"),
                warehouse = Get(order, "warehouse", Get(settings, "warehouse", string.Empty))
            }
        });
        connection.SecretsJson = Serialize(new
        {
            authorizationToken = request.AuthorizationToken is null
                ? GetNullable(existingSecrets, "authorizationToken")
                    ?? GetNullable(settings, "authorizationToken")
                : request.AuthorizationToken.Trim()
        });
        connection.IsEnabled = request.IsEnabled;
        connection.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.IntegrationConnections.UpdateAsync(connection, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return await GetSettingsAsync(tenantId, businessId, ct);
    }

    public async Task<IntegrationSettingsDto> UpdateXionAsync(
        Guid tenantId,
        Guid businessId,
        UpdateXionIntegrationRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        if (request.IsEnabled && new[]
            {
                request.SucursalId, request.VendedorId, request.EquipoId, request.BodegaId,
                request.EmpresaId, request.CentroDeCostoId, request.UsuarioId
            }.Any(value => value <= 0))
        {
            throw new InvalidOperationException("Sucursal, vendedor, equipo, bodega, empresa, centro de costo y usuario deben ser mayores que cero.");
        }

        var connection = await GetOrCreateCommerceAsync(
            businessId, CommerceProvider.Xion, "Xion - Andina Santander", ct);
        connection.Name = "Xion - Andina Santander";
        connection.SettingsJson = Serialize(new
        {
            baseUrl = string.IsNullOrWhiteSpace(request.BaseUrl)
                ? "http://api.andinasantander.com:9091/"
                : request.BaseUrl.Trim(),
            requestTimeoutSeconds = request.RequestTimeoutSeconds <= 0 ? 120 : request.RequestTimeoutSeconds,
            currency = string.IsNullOrWhiteSpace(request.Currency) ? "COP" : request.Currency.Trim(),
            sucursalId = request.SucursalId,
            vendedorId = request.VendedorId,
            equipoId = request.EquipoId,
            bodegaId = request.BodegaId,
            empresaId = request.EmpresaId,
            centroDeCostoId = request.CentroDeCostoId,
            usuarioId = request.UsuarioId,
            rutaId = Math.Max(0, request.RutaId),
            validateStockOnCreate = request.ValidateStockOnCreate,
            orderHistoryDays = request.OrderHistoryDays <= 0 ? 365 : request.OrderHistoryDays,
            catalogDiscoveryMaxQueries = 512,
            catalogDiscoveryConcurrency = 8,
            endpoints = new
            {
                customerSync = "WebApi/Vendedores/Sync/Clientes/{vendedorId}/{sucursalId}",
                productSearch = "WebApi/Vendedores/Consulta/ProductosABuscar/{sucursalId}/{vendedorId}/{criterio}/{busqueda}/{bodegaId}/{equipoId}/{clienteId}",
                productSearchWithoutCustomer = "WebApi/Vendedores/Consulta/ProductosABuscarSinCliente/{sucursalId}/{vendedorId}/{criterio}/{busqueda}/{bodegaId}/{equipoId}",
                productDetail = "WebApi/Vendedores/Consulta/InfoProducto/{productoId}/{sucursalId}/{vendedorId}/{bodegaId}/{equipoId}/{clienteId}",
                productDetailWithoutCustomer = "WebApi/Vendedores/Consulta/InfoProductoSinCliente/{productoId}/{sucursalId}/{vendedorId}/{bodegaId}/{equipoId}",
                nextOrderNumber = "WebApi/Vendedores/Consulta/Pedido/SiguienteConsecutivo/{equipoId}",
                createOrder = "WebApi/Vendedores/Nuevo/Pedido/{validarExistencia}",
                orderHistory = "WebApi/Vendedores/Consulta/Pedidos/{vendedorId}/{fechaInicial}/{fechaFin}/{clienteId}/{rutaId}/{criterio}",
                verifyOrder = "WebApi/Vendedores/Consulta/VerificarPedido/{pedidoId}"
            }
        });
        connection.SecretsJson = null;
        connection.IsEnabled = request.IsEnabled;
        connection.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.IntegrationConnections.UpdateAsync(connection, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return await GetSettingsAsync(tenantId, businessId, ct);
    }

    private async Task<IntegrationConnection> GetOrCreateCommerceAsync(
        Guid businessId,
        CommerceProvider provider,
        string name,
        CancellationToken ct)
    {
        var connection = await _unitOfWork.IntegrationConnections.GetCommerceConnectionAsync(
            businessId, provider, CommerceCapability.CatalogAndOrders, ct);
        if (connection is not null)
            return connection;

        connection = new IntegrationConnection
        {
            IntegrationConnectionId = Guid.NewGuid(),
            BusinessId = businessId,
            ConnectionType = ConnectionType.Commerce,
            Provider = (int)provider,
            Capability = (int)CommerceCapability.CatalogAndOrders,
            Name = name,
            SettingsJson = "{}",
            IsEnabled = false,
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.IntegrationConnections.CreateAsync(connection, ct);
        return connection;
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

    public async Task<IReadOnlyList<MantisChannelWarehouseDto>> GetMantisChannelWarehousesAsync(
        Guid tenantId,
        Guid businessId,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var connection = await _unitOfWork.IntegrationConnections.GetCommerceConnectionAsync(
            businessId,
            CommerceProvider.Mantis,
            CommerceCapability.CatalogAndOrders,
            ct);
        if (connection is null)
            return [];

        var numbers = (await _unitOfWork.BusinessWhatsAppNumbers.GetByBusinessIdAsync(businessId))
            .Where(number => number.IsActive)
            .OrderBy(number => number.PhoneNumber)
            .ToList();
        var mappings = await _unitOfWork.IntegrationConnections.GetChannelWarehousesAsync(
            businessId, connection.IntegrationConnectionId, ct);
        var settings = ReadJson(connection.SettingsJson);
        var legacyWarehouse = GetNullable(settings, "warehouse")
            ?? GetNullable(ReadNested(connection.SettingsJson, "catalog"), "warehouse")
            ?? GetNullable(ReadNested(connection.SettingsJson, "order"), "warehouse");

        return numbers.Select(number =>
        {
            var mapping = mappings.FirstOrDefault(candidate =>
                candidate.BusinessWhatsAppNumberId == number.BusinessWhatsAppNumberId);
            return new MantisChannelWarehouseDto(
                number.BusinessWhatsAppNumberId,
                number.PhoneNumber,
                number.WhatsAppPhoneNumberId,
                mapping?.WarehouseCode ?? legacyWarehouse,
                mapping?.WarehouseName,
                mapping?.IsActive ?? !string.IsNullOrWhiteSpace(legacyWarehouse));
        }).ToList();
    }

    public async Task<IReadOnlyList<MantisChannelWarehouseDto>> UpdateMantisChannelWarehousesAsync(
        Guid tenantId,
        Guid businessId,
        UpdateMantisChannelWarehousesRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var connection = await _unitOfWork.IntegrationConnections.GetCommerceConnectionAsync(
            businessId,
            CommerceProvider.Mantis,
            CommerceCapability.CatalogAndOrders,
            ct) ?? throw new InvalidOperationException("The Mantis commerce connection is not configured.");
        var numbers = (await _unitOfWork.BusinessWhatsAppNumbers.GetByBusinessIdAsync(businessId))
            .ToDictionary(number => number.BusinessWhatsAppNumberId);

        foreach (var channel in request.Channels ?? [])
        {
            if (!numbers.TryGetValue(channel.BusinessWhatsAppNumberId, out var number)
                || number.BusinessId != businessId)
            {
                throw new InvalidOperationException("A WhatsApp number does not belong to this business.");
            }
            if (channel.IsActive && string.IsNullOrWhiteSpace(channel.WarehouseCode))
                throw new InvalidOperationException("An active Mantis channel warehouse requires a warehouse code.");

            await _unitOfWork.IntegrationConnections.UpsertChannelWarehouseAsync(
                new IntegrationChannelWarehouse
                {
                    IntegrationChannelWarehouseId = Guid.NewGuid(),
                    BusinessId = businessId,
                    IntegrationConnectionId = connection.IntegrationConnectionId,
                    BusinessWhatsAppNumberId = channel.BusinessWhatsAppNumberId,
                    WarehouseCode = channel.WarehouseCode.Trim(),
                    WarehouseName = string.IsNullOrWhiteSpace(channel.WarehouseName)
                        ? null
                        : channel.WarehouseName.Trim(),
                    IsActive = channel.IsActive,
                    CreatedAt = DateTime.UtcNow
                },
                ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return await GetMantisChannelWarehousesAsync(tenantId, businessId, ct);
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

        var mantis = connections.FirstOrDefault(c =>
            c.ConnectionType == ConnectionType.Commerce &&
            c.Provider == (int)CommerceProvider.Mantis &&
            c.Capability == (int)CommerceCapability.CatalogAndOrders);
        var xion = connections.FirstOrDefault(c =>
            c.ConnectionType == ConnectionType.Commerce &&
            c.Provider == (int)CommerceProvider.Xion &&
            c.Capability == (int)CommerceCapability.CatalogAndOrders);
        return new IntegrationSettingsDto(
            MapGoogle(google),
            MapWompi(wompi),
            MapSiigoCommerce(siigo),
            MapMantis(mantis),
            MapXion(xion));
    }

    private static MantisIntegrationDto MapMantis(IntegrationConnection? connection)
    {
        var settings = ReadJson(connection?.SettingsJson);
        var secrets = ReadJson(connection?.SecretsJson);
        return new MantisIntegrationDto(
            connection is not null,
            connection?.IsEnabled ?? false,
            Get(settings, "baseUrl", string.Empty),
            GetInt(settings, "requestTimeoutSeconds", 30),
            Get(settings, "currency", "COP"),
            Has(secrets, "authorizationToken") || Has(settings, "authorizationToken"),
            connection?.LastError,
            connection?.LastSyncAt);
    }

    private static XionIntegrationDto MapXion(IntegrationConnection? connection)
    {
        var settings = ReadJson(connection?.SettingsJson);
        var vendedorId = GetInt(settings, "vendedorId", 1);
        return new XionIntegrationDto(
            connection is not null,
            connection?.IsEnabled ?? false,
            Get(settings, "baseUrl", "http://api.andinasantander.com:9091/"),
            GetInt(settings, "requestTimeoutSeconds", 120),
            Get(settings, "currency", "COP"),
            GetInt(settings, "sucursalId", 1),
            vendedorId,
            GetInt(settings, "equipoId", 1),
            GetInt(settings, "bodegaId", 1),
            GetInt(settings, "empresaId", 1),
            GetInt(settings, "centroDeCostoId", 1),
            GetInt(settings, "usuarioId", vendedorId),
            GetInt(settings, "rutaId", 0),
            GetBool(settings, "validateStockOnCreate", true),
            GetInt(settings, "orderHistoryDays", 365),
            connection?.LastError,
            connection?.LastSyncAt);
    }
    private static GoogleCalendarIntegrationDto MapGoogle(IntegrationConnection? connection)
    {
        var settings = ReadJson(connection?.SettingsJson);
        return new GoogleCalendarIntegrationDto(
            connection?.IsEnabled ?? false,
            Get(settings, "calendarId", string.Empty),
            Get(settings, "timeZone", "America/Bogota"),
            GetNullable(settings, "scopes"),
            false,
            false,
            false,
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
            Get(settings, "partnerId", string.Empty),
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

    private static void ValidateWompiConfiguration(
        UpdateWompiIntegrationRequest request,
        string mode,
        IReadOnlyDictionary<string, string?> secrets)
    {
        if (request.RequestTimeoutSeconds is < 0 or > 120)
            throw new DomainValidationException("requestTimeoutSeconds", "El timeout de Wompi debe estar entre 1 y 120 segundos.");

        if (!request.IsEnabled)
            return;

        foreach (var key in new[] { "privateKey", "publicKey", "eventsSecret", "integritySecret" })
        {
            if (!secrets.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                throw new DomainValidationException(key, $"{key} es obligatorio para activar Wompi en modo {mode}.");
        }

        var prefix = mode == "production" ? "prod" : "test";
        if (!secrets["privateKey"]!.StartsWith($"prv_{prefix}_", StringComparison.Ordinal))
            throw new DomainValidationException("privateKey", $"La llave privada no corresponde al modo {mode}.");
        if (!secrets["publicKey"]!.StartsWith($"pub_{prefix}_", StringComparison.Ordinal))
            throw new DomainValidationException("publicKey", $"La llave pública no corresponde al modo {mode}.");
    }

    private static string NormalizeOfficialWompiUrl(string? candidate, string expected, string field)
    {
        var value = string.IsNullOrWhiteSpace(candidate) ? expected : candidate.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var actualUri)
            || !Uri.TryCreate(expected, UriKind.Absolute, out var expectedUri)
            || actualUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(actualUri.Host, expectedUri.Host, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(actualUri.AbsolutePath.TrimEnd('/'), expectedUri.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal))
        {
            throw new DomainValidationException(field, $"Usa el endpoint oficial de Wompi: {expected}");
        }

        return expected;
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
