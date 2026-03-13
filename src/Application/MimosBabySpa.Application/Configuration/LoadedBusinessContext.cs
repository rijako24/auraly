using System.Text.Json;
using System.Text.RegularExpressions;
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Contexto unificado de configuración de negocio.
/// Carga TODA la configuración necesaria en una sola operación.
/// Evita cargas redundantes de EntityExtractionConfig y otras configuraciones.
/// </summary>
public class LoadedBusinessContext
{
    public Guid BusinessId { get; }
    public BusinessInfo Info { get; private set; } = null!;
    public List<ServiceInfo> Services { get; private set; } = null!;
    public Dictionary<string, AttributeDefinition> Attributes { get; private set; } = null!;
    public RequiredFieldsConfiguration RequiredFields { get; private set; } = null!;
    public BusinessPersonality Personality { get; private set; } = null!;

    /// <summary>
    /// Instrucciones de venta configuradas por el tenant (texto libre).
    /// Null cuando el negocio no tiene estrategia definida — el LLM usará criterio propio.
    /// </summary>
    public string? SalesStrategy { get; private set; }

    /// <summary>
    /// Reglas de add-ons: qué extras se pueden ofrecer y con qué categoría de servicio son compatibles.
    /// </summary>
    public List<AddOnRuleInfo> AddOnRules { get; private set; } = null!;

    /// <summary>
    /// Configuración de pago por anticipo. Null cuando el negocio no tiene anticipo configurado.
    /// </summary>
    public PaymentConfiguration? PaymentConfig { get; private set; }

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<LoadedBusinessContext> _logger;

    // Constructor privado - usar factory method LoadAsync
    private LoadedBusinessContext(
        Guid businessId,
        IUnitOfWork unitOfWork,
        ILogger<LoadedBusinessContext> logger)
    {
        BusinessId = businessId;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Factory Method: Carga toda la configuración del negocio de una vez.
    /// ✅ UNA SOLA CARGA - todas las queries en paralelo.
    /// </summary>
    public static async Task<LoadedBusinessContext> LoadAsync(
        Guid businessId,
        IUnitOfWork unitOfWork,
        ILogger<LoadedBusinessContext> logger,
        CancellationToken cancellationToken = default)
    {
        var context = new LoadedBusinessContext(businessId, unitOfWork, logger);
        await context.LoadAllAsync(cancellationToken);
        return context;
    }

    /// <summary>
    /// Carga todas las configuraciones de forma secuencial.
    /// NOTA: Secuencial porque EF Core no permite operaciones concurrentes en el mismo DbContext.
    /// </summary>
    private async Task LoadAllAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Cargando configuración completa para BusinessId={BusinessId}...", BusinessId);

        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogDebug("Cargando BusinessInfo...");
            Info = await LoadBusinessInfoAsync(cancellationToken);
            
            _logger.LogDebug("Cargando Services...");
            Services = await LoadServicesAsync(cancellationToken);

            _logger.LogDebug("Cargando AddOnRules...");
            AddOnRules = await LoadAddOnRulesAsync(cancellationToken);
            
            _logger.LogDebug("Cargando Attributes...");
            Attributes = await LoadAttributesAsync(cancellationToken);

            _logger.LogDebug("Cargando Personality...");
            Personality = await LoadPersonalityAsync(cancellationToken);

            _logger.LogDebug("Cargando SalesStrategy...");
            SalesStrategy = await LoadSalesStrategyAsync(cancellationToken);

            _logger.LogDebug("Cargando PaymentConfig...");
            PaymentConfig = await LoadPaymentConfigAsync(cancellationToken);

            // Construir RequiredFields basado en Attributes cargados
            RequiredFields = BuildRequiredFields();

            var elapsed = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "✅ Configuración cargada para BusinessId={BusinessId} en {Elapsed}ms: " +
                "Services={ServiceCount}, AddOnRules={AddOnRuleCount}, Attributes={AttributeCount}",
                BusinessId, elapsed.TotalMilliseconds, Services.Count, AddOnRules.Count, Attributes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cargando configuración para BusinessId={BusinessId}", BusinessId);
            throw;
        }
    }

    private Domain.Entities.Business? _cachedBusiness; // Caché para reutilizar la entidad Business

    private async Task<BusinessInfo> LoadBusinessInfoAsync(CancellationToken cancellationToken)
    {
        try
        {
            _cachedBusiness = await _unitOfWork.Businesses.GetByIdAsync(BusinessId);

            if (_cachedBusiness == null)
            {
                _logger.LogWarning("Negocio no encontrado: {BusinessId}", BusinessId);
                return new BusinessInfo { BusinessId = BusinessId, Name = "Negocio Desconocido" };
            }

            var operatingHoursConfig = await _unitOfWork.BusinessConfigurations
                .GetByBusinessIdAndKeyAsync(BusinessId, BusinessConfigurationKey.OperatingHours);
            var paymentMethodsConfig = await _unitOfWork.BusinessConfigurations
                .GetByBusinessIdAndKeyAsync(BusinessId, BusinessConfigurationKey.PaymentMethods);

            var schedule = DeserializeSchedule(operatingHoursConfig?.Value ?? "{}");
            var paymentMethods = DeserializePaymentMethods(paymentMethodsConfig?.Value ?? "[]");

            return new BusinessInfo
            {
                BusinessId = _cachedBusiness.BusinessId,
                Name = _cachedBusiness.Name,
                Description = _cachedBusiness.Description,
                Address = _cachedBusiness.Address,
                Phone = _cachedBusiness.Phone,
                Email = _cachedBusiness.Email,
                Website = _cachedBusiness.Website,
                Schedule = schedule,
                PaymentMethods = paymentMethods,
                LogoUrl = _cachedBusiness.LogoUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cargando información del negocio");
            return new BusinessInfo { BusinessId = BusinessId, Name = "Negocio Desconocido" };
        }
    }

    private Dictionary<string, List<TimeBlock>> DeserializeSchedule(string json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json) || json == "{}")
            {
                _logger.LogDebug("Horarios de negocio vacíos");
                return new Dictionary<string, List<TimeBlock>>();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var schedule = JsonSerializer.Deserialize<Dictionary<string, List<TimeBlock>>>(json, options);
            
            if (schedule == null)
            {
                _logger.LogWarning("Deserialización de horarios retornó null");
                return new Dictionary<string, List<TimeBlock>>();
            }

            _logger.LogDebug("Horarios deserializados correctamente: {DaysCount} días configurados", schedule.Count);
            return schedule;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deserializando horarios de negocio. JSON: {Json}", json);
            return new Dictionary<string, List<TimeBlock>>();
        }
    }

    private List<PaymentMethod> DeserializePaymentMethods(string json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json) || json == "[]")
            {
                _logger.LogDebug("Métodos de pago vacíos");
                return new List<PaymentMethod>();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var methods = JsonSerializer.Deserialize<List<PaymentMethod>>(json, options);
            
            if (methods == null)
            {
                _logger.LogWarning("Deserialización de métodos de pago retornó null");
                return new List<PaymentMethod>();
            }

            _logger.LogDebug("Métodos de pago deserializados correctamente: {Count} métodos", methods.Count);
            return methods;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deserializando métodos de pago. JSON: {Json}", json);
            return new List<PaymentMethod>();
        }
    }

    private async Task<List<ServiceInfo>> LoadServicesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var services = await _unitOfWork.Services.GetByBusinessIdAsync(BusinessId);

            return services
                .Where(s => s.IsActive)
                .Select(s => new ServiceInfo
                {
                    Name            = s.ServiceName,
                    Description     = s.Description,
                    DurationMinutes = s.DurationMinutes,
                    Price           = s.Price,
                    IsActive        = s.IsActive,
                    Category        = s.Category,
                    Tier            = s.Tier,
                    ServiceType     = s.ServiceType,
                    BundleItems     = s.BundleItems
                        .OrderBy(b => b.DisplayOrder)
                        .Select(b => new BundleItemInfo
                        {
                            Name         = b.IncludedService.ServiceName,
                            Description  = b.IncludedService.Description,
                            Price        = b.IncludedService.Price,
                            DisplayOrder = b.DisplayOrder
                        })
                        .ToList()
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cargando servicios");
            return new List<ServiceInfo>();
        }
    }

    private async Task<List<AddOnRuleInfo>> LoadAddOnRulesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var rules = await _unitOfWork.ServiceAddOnRules.GetByBusinessIdAsync(BusinessId);
            return rules
                .Select(r => new AddOnRuleInfo
                {
                    AddOnName                = r.AddOnService.ServiceName,
                    AddOnDescription         = r.AddOnService.Description,
                    AddOnPrice               = r.AddOnService.Price,
                    DisplayOrder             = r.DisplayOrder,
                    CompatibleWithServiceName = r.CompatibleService?.ServiceName,
                    CompatibleServiceCategory = r.CompatibleService?.Category
                })
                .OrderBy(r => r.DisplayOrder)
                .ThenBy(r => r.AddOnName)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cargando reglas de add-ons");
            return new List<AddOnRuleInfo>();
        }
    }

    /// <summary>
    /// Carga los atributos de negocio desde EntityExtractionConfig.
    /// ✅ UNA SOLA CARGA - no se volverá a llamar.
    /// </summary>
    private async Task<Dictionary<string, AttributeDefinition>> LoadAttributesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            // ✅ ÚNICA CARGA de EntityExtractionConfig
            var config = await _unitOfWork.BusinessConfigurations
                .GetByBusinessIdAndKeyAsync(BusinessId, BusinessConfigurationKey.EntityExtractionConfig);
            
            var configJson = config?.Value ?? string.Empty;

            if (string.IsNullOrWhiteSpace(configJson))
            {
                _logger.LogWarning(
                    "No se encontró configuración de atributos para BusinessId={BusinessId}. " +
                    "El sistema funcionará solo con campos core.",
                    BusinessId);
                return new Dictionary<string, AttributeDefinition>();
            }

            return ParseAttributesFromJson(configJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cargando atributos de negocio");
            return new Dictionary<string, AttributeDefinition>();
        }
    }

    private Dictionary<string, AttributeDefinition> ParseAttributesFromJson(string configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;

            var attributes = new Dictionary<string, AttributeDefinition>();

            // Propiedades conocidas que NO son atributos
            var nonAttributeProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "entityType",
                "relevantFields",
                "fieldDescriptions",
                "keywords",
                "isActive"
            };

            foreach (var property in root.EnumerateObject())
            {
                // Ignorar propiedades que no son atributos
                if (nonAttributeProperties.Contains(property.Name))
                {
                    continue;
                }

                // Solo procesar propiedades que son objetos JSON
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                try
                {
                    var attributeJson = property.Value.GetRawText();
                    var attribute = JsonSerializer.Deserialize<AttributeDefinition>(
                        attributeJson,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                        });

                    if (attribute != null)
                    {
                        attributes[property.Name] = attribute;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Error deserializando atributo '{AttributeName}'. Se omitirá.",
                        property.Name);
                }
            }

            if (attributes.Any())
            {
                _logger.LogInformation(
                    "Atributos de negocio parseados: {Count} atributos para BusinessId={BusinessId}",
                    attributes.Count, BusinessId);
            }

            return attributes;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parseando JSON de configuración de atributos");
            return new Dictionary<string, AttributeDefinition>();
        }
    }

    /// <summary>
    /// Carga la personalidad del asistente. Origen: BusinessConfiguration key=Personality (0).
    /// Si no hay config por negocio, usa SystemConfiguration.ToneAndStyle como fallback.
    /// Todo es texto libre — identidad, tono y estilo en un solo bloque.
    /// </summary>
    private async Task<BusinessPersonality> LoadPersonalityAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 1. Intentar BusinessConfiguration (por tenant)
            var config = await _unitOfWork.BusinessConfigurations
                .GetByBusinessIdAndKeyAsync(BusinessId, BusinessConfigurationKey.Personality);

            if (config != null && !string.IsNullOrWhiteSpace(config.Value))
            {
                var text = config.Value.Trim();
                _logger.LogDebug("Personality cargada desde BusinessConfiguration para BusinessId={BusinessId}", BusinessId);
                return new BusinessPersonality
                {
                    PersonalityText = text,
                    AssistantName = BusinessPersonality.ExtractAssistantName(text)
                };
            }

            // 2. Fallback: SystemConfiguration.ToneAndStyle
            var sysTone = await _unitOfWork.SystemConfigurations
                .GetByKeyAsync(SystemConfigurationKey.ToneAndStyle);

            if (sysTone != null && !string.IsNullOrWhiteSpace(sysTone.Value))
            {
                _logger.LogDebug(
                    "Personality: fallback a SystemConfiguration.ToneAndStyle para BusinessId={BusinessId}",
                    BusinessId);
                return new BusinessPersonality
                {
                    PersonalityText = sysTone.Value.Trim(),
                    AssistantName = "Asistente"
                };
            }

            // 3. Sin config
            _logger.LogWarning(
                "No se encontró Personality (key=0) ni SystemConfiguration.ToneAndStyle para BusinessId={BusinessId}. " +
                "El asistente usará default genérico.",
                BusinessId);
            return new BusinessPersonality();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cargando Personality para BusinessId={BusinessId}", BusinessId);
            return new BusinessPersonality();
        }
    }

    /// <summary>
    /// Carga la configuración de pago desde BusinessConfiguration key=PaymentConfig (3).
    /// JSON: RequiresAnticipo, AnticipoPorcentaje, Provider, LinkExpirationMinutes, Currency.
    /// </summary>
    private async Task<PaymentConfiguration?> LoadPaymentConfigAsync(CancellationToken cancellationToken)
    {
        try
        {
            var config = await _unitOfWork.BusinessConfigurations
                .GetByBusinessIdAndKeyAsync(BusinessId, BusinessConfigurationKey.PaymentConfig);

            if (config == null || string.IsNullOrWhiteSpace(config.Value))
            {
                _logger.LogDebug(
                    "No se encontró PaymentConfig (key=3) para BusinessId={BusinessId}. Sin anticipo.",
                    BusinessId);
                return null;
            }

            var trimmed = config.Value.TrimStart();
            if (!trimmed.StartsWith("{"))
            {
                _logger.LogWarning(
                    "PaymentConfig para BusinessId={BusinessId} no es JSON válido. Ignorando.",
                    BusinessId);
                return null;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var paymentConfig = JsonSerializer.Deserialize<PaymentConfiguration>(config.Value, options);

            if (paymentConfig != null && paymentConfig.RequiresAnticipo)
            {
                _logger.LogInformation(
                    "PaymentConfig cargada para BusinessId={BusinessId}: Anticipo {Pct:P0}, Provider={Provider}",
                    BusinessId, paymentConfig.AnticipoPorcentaje, paymentConfig.Provider);
            }

            return paymentConfig;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Error deserializando PaymentConfig para BusinessId={BusinessId}. Sin anticipo.",
                BusinessId);
            return null;
        }
    }

    /// <summary>
    /// Carga la estrategia de ventas desde BusinessConfiguration key=SalesStrategy (2).
    /// El valor es texto libre que se inyecta directamente en el system prompt del LLM.
    /// </summary>
    private async Task<string?> LoadSalesStrategyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var config = await _unitOfWork.BusinessConfigurations
                .GetByBusinessIdAndKeyAsync(BusinessId, BusinessConfigurationKey.SalesStrategy);

            if (config == null || string.IsNullOrWhiteSpace(config.Value))
            {
                _logger.LogDebug(
                    "No se encontró SalesStrategy (key=2) para BusinessId={BusinessId}. El LLM usará criterio propio.",
                    BusinessId);
                return null;
            }

            _logger.LogDebug("SalesStrategy cargada para BusinessId={BusinessId}", BusinessId);
            return config.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cargando SalesStrategy para BusinessId={BusinessId}", BusinessId);
            return null;
        }
    }

    private RequiredFieldsConfiguration BuildRequiredFields()
    {
        return new RequiredFieldsConfiguration
        {
            // Campos core siempre requeridos
            CoreFields = new List<string>
            {
                "Service",
                "DesiredDate",
                "DesiredTime"
            },

            // Campos de identidad siempre requeridos
            IdentityFields = new List<string>
            {
                "CustomerName",
                "Phone"
            },

            // Atributos específicos del negocio que son requeridos
            BusinessAttributes = Attributes
                .Where(a => a.Value.IsRequired)
                .Select(a => a.Key)
                .ToList(),

            // Campos opcionales
            OptionalFields = new List<string>
            {
                "Email"
            }
        };
    }
}
