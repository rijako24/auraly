using Microsoft.Extensions.Logging;
using System.Text.Json;
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Domain.Enums;

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
    public SalesGuidance SalesGuidance { get; private set; } = null!;
    public BusinessPersonality Personality { get; private set; } = null!;

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
            
            _logger.LogDebug("Cargando Attributes...");
            Attributes = await LoadAttributesAsync(cancellationToken);

            _logger.LogDebug("Cargando SalesGuidance...");
            SalesGuidance = await LoadSalesGuidanceAsync(cancellationToken);

            _logger.LogDebug("Cargando Personality...");
            Personality = LoadPersonalityFromBusiness();

            // Construir RequiredFields basado en Attributes cargados
            RequiredFields = BuildRequiredFields();

            var elapsed = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "✅ Configuración cargada para BusinessId={BusinessId} en {Elapsed}ms: " +
                "Services={ServiceCount}, Attributes={AttributeCount}",
                BusinessId, elapsed.TotalMilliseconds, Services.Count, Attributes.Count);
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

            // Deserializar horarios y métodos de pago desde JSON
            var schedule = DeserializeSchedule(_cachedBusiness.OperatingHoursJson);
            var paymentMethods = DeserializePaymentMethods(_cachedBusiness.PaymentMethodsJson);

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
                    Name = s.ServiceName,
                    Description = s.Description,
                    DurationMinutes = s.DurationMinutes,
                    Price = s.Price,
                    IsActive = s.IsActive
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cargando servicios");
            return new List<ServiceInfo>();
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
    /// Carga la configuración de guía de ventas desde BusinessConfiguration.
    /// </summary>
    private async Task<SalesGuidance> LoadSalesGuidanceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var config = await _unitOfWork.BusinessConfigurations.GetByBusinessIdAndKeyAsync(
                BusinessId,
                BusinessConfigurationKey.SalesGuidance);

            if (config == null || string.IsNullOrWhiteSpace(config.Value))
            {
                _logger.LogDebug("No se encontró configuración de SalesGuidance, usando valor por defecto");
                return SalesGuidance.Default();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var guidance = JsonSerializer.Deserialize<SalesGuidance>(config.Value, options);

            if (guidance == null)
            {
                _logger.LogWarning("Deserialización de SalesGuidance retornó null");
                return SalesGuidance.Default();
            }

            _logger.LogDebug(
                "SalesGuidance cargada correctamente: {CriticalAttributesCount} atributos críticos, IsEnabled={IsEnabled}",
                guidance.CriticalAttributes.Count,
                guidance.IsEnabled);

            return guidance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cargando SalesGuidance, usando valor por defecto");
            return SalesGuidance.Default();
        }
    }

    /// <summary>
    /// Carga la personalidad del asistente desde Business.PersonalityJson.
    /// Usa la entidad Business ya cargada en LoadBusinessInfoAsync.
    /// </summary>
    private BusinessPersonality LoadPersonalityFromBusiness()
    {
        try
        {
            if (_cachedBusiness == null || string.IsNullOrWhiteSpace(_cachedBusiness.PersonalityJson) || _cachedBusiness.PersonalityJson == "{}")
            {
                _logger.LogDebug("No se encontró configuración de personalidad, usando valor por defecto");
                return BusinessPersonality.Default();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var personality = JsonSerializer.Deserialize<BusinessPersonality>(_cachedBusiness.PersonalityJson, options);

            if (personality == null)
            {
                _logger.LogWarning("Deserialización de Personality retornó null");
                return BusinessPersonality.Default();
            }

            _logger.LogDebug(
                "Personality cargada correctamente: AssistantName={AssistantName}, UseEmojis={UseEmojis}",
                personality.AssistantName,
                personality.UseEmojis);

            return personality;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deserializando Personality, usando valor por defecto");
            return BusinessPersonality.Default();
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
