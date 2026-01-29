using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;
using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Implementación del proveedor de configuración de negocio.
/// Carga configuración desde múltiples fuentes (BD, configuración, defaults).
/// </summary>
public class BusinessConfigurationProvider : IBusinessConfigurationProvider
{
    private readonly ILogger<BusinessConfigurationProvider> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessConfigurationService _businessConfigService;

    public BusinessConfigurationProvider(
        ILogger<BusinessConfigurationProvider> logger,
        IUnitOfWork unitOfWork,
        IBusinessConfigurationService businessConfigService)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
        _businessConfigService = businessConfigService;
    }

    public async Task<RequiredFieldsConfiguration> GetRequiredFieldsAsync(
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Obtener configuración de atributos del negocio
            var attributes = await GetBusinessAttributesAsync(businessId, cancellationToken);

            var config = new RequiredFieldsConfiguration
            {
                // Campos core siempre requeridos para cualquier negocio
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
                BusinessAttributes = attributes
                    .Select(a => a.Key)
                    .ToList(),

                // Campos opcionales
                OptionalFields = new List<string>
                {
                    "Email"
                }
            };

            _logger.LogInformation(
                "Configuración de campos requeridos cargada para BusinessId={BusinessId}: " +
                "Core={CoreCount}, Identity={IdentityCount}, Business={BusinessCount}",
                businessId, config.CoreFields.Count, config.IdentityFields.Count, 
                config.BusinessAttributes.Count);

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar configuración de campos requeridos");
            
            // Retornar configuración mínima por defecto
            return new RequiredFieldsConfiguration();
        }
    }

    public async Task<string> GetSystemPromptAsync(
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Cargar información del negocio
            var businessInfo = await GetBusinessInfoAsync(businessId, cancellationToken);
            var services = await GetServicesAsync(businessId, cancellationToken);
            var requiredFields = await GetRequiredFieldsAsync(businessId, cancellationToken);
            var attributes = await GetBusinessAttributesAsync(businessId, cancellationToken);

            // Construir prompt dinámico
            var prompt = BuildSystemPrompt(businessInfo, services, requiredFields, attributes);

            _logger.LogDebug(
                "System prompt generado para BusinessId={BusinessId}, Length={Length}",
                businessId, prompt.Length);

            return prompt;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al generar system prompt");
            
            // Retornar prompt genérico básico
            return GetDefaultSystemPrompt();
        }
    }

    public async Task<List<ServiceInfo>> GetServicesAsync(
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var services = await _unitOfWork.Services.GetByBusinessIdAsync(businessId);

            return services
                .Where(s => s.IsActive)
                .Select(s => new ServiceInfo
                {
                    Name = s.ServiceName,
                    Description = string.Empty, // Service entity no tiene Description
                    DurationMinutes = s.DurationMinutes,
                    Price = 0, // Service entity no tiene Price
                    IsActive = s.IsActive
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar servicios");
            return new List<ServiceInfo>();
        }
    }

    public async Task<BusinessInfo> GetBusinessInfoAsync(
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var business = await _unitOfWork.Businesses.GetByIdAsync(businessId);

            if (business == null)
            {
                _logger.LogWarning("Negocio no encontrado: {BusinessId}", businessId);
                return new BusinessInfo { BusinessId = businessId };
            }

            return new BusinessInfo
            {
                BusinessId = business.BusinessId,
                Name = business.Name,
                Description = string.Empty, // Business entity no tiene Description
                Address = string.Empty, // Business entity no tiene Address
                Phone = string.Empty, // Business entity no tiene Phone
                Email = string.Empty, // Business entity no tiene Email
                Website = string.Empty // Business entity no tiene Website
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar información del negocio");
            return new BusinessInfo { BusinessId = businessId };
        }
    }

    public async Task<Dictionary<string, AttributeDefinition>> GetBusinessAttributesAsync(
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Cargar configuración desde BusinessConfiguration (JSON)
            var configJson = await _businessConfigService.GetBusinessConfigurationValueAsync(
                businessId, 
                Domain.Enums.BusinessConfigurationKey.EntityExtractionConfig);

            if (!string.IsNullOrWhiteSpace(configJson))
            {
                try
                {
                    // Parsear JSON usando JsonDocument para manejar propiedades adicionales como entityType
                    using var doc = System.Text.Json.JsonDocument.Parse(configJson);
                    var root = doc.RootElement;
                    
                    var attributes = new Dictionary<string, AttributeDefinition>();
                    
                    // Propiedades conocidas que NO son atributos (parte de BusinessExtractionConfig legacy)
                    var nonAttributeProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "entityType",
                        "relevantFields",
                        "fieldDescriptions",
                        "keywords",
                        "isActive"
                    };
                    
                    // Iterar sobre todas las propiedades del JSON
                    foreach (var property in root.EnumerateObject())
                    {
                        // Ignorar propiedades que no son atributos
                        if (nonAttributeProperties.Contains(property.Name))
                        {
                            continue;
                        }
                        
                        // Solo procesar propiedades que son objetos JSON (los atributos son objetos)
                        if (property.Value.ValueKind != System.Text.Json.JsonValueKind.Object)
                        {
                            continue;
                        }
                        
                        try
                        {
                            // Deserializar cada atributo individualmente
                            var attributeJson = property.Value.GetRawText();
                            var attribute = System.Text.Json.JsonSerializer.Deserialize<AttributeDefinition>(
                                attributeJson,
                                new System.Text.Json.JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true,
                                    Converters = { new JsonStringEnumConverter() }
                                });
                            
                            if (attribute != null)
                            {
                                attributes[property.Name] = attribute;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, 
                                "Error al deserializar atributo '{AttributeName}'. Se omitirá.", 
                                property.Name);
                        }
                    }

                    if (attributes.Any())
                    {
                        _logger.LogInformation(
                            "Atributos de negocio cargados desde configuración: {Count} atributos para BusinessId={BusinessId}",
                            attributes.Count, businessId);
                        
                        return attributes;
                    }
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogError(ex, "Error al parsear configuración de atributos JSON");
                }
            }

            // Si no hay configuración, retornar diccionario vacío
            // El sistema funcionará sin atributos de negocio (solo campos core)
            _logger.LogWarning(
                "No se encontró configuración de atributos para BusinessId={BusinessId}. " +
                "El sistema funcionará solo con campos core (Service, Date, Time, CustomerName, Phone)",
                businessId);

            return new Dictionary<string, AttributeDefinition>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar atributos de negocio");
            return new Dictionary<string, AttributeDefinition>();
        }
    }

    // ========================================
    // MÉTODOS PRIVADOS
    // ========================================

    private string BuildSystemPrompt(
        BusinessInfo business,
        List<ServiceInfo> services,
        RequiredFieldsConfiguration requiredFields,
        Dictionary<string, AttributeDefinition> attributes)
    {
        // Construir información de servicios detallada
        var servicesDescription = services.Any() 
            ? string.Join("\n\n---\n\n", services.Select(s => 
                $"**{s.Name}**{(s.DurationMinutes > 0 ? $" — {s.DurationMinutes} minutos" : "")}" +
                $"{(string.IsNullOrWhiteSpace(s.Description) ? "" : $"\n{s.Description}")}" +
                $"{(s.Price > 0 ? $"\nPrecio: ${s.Price}" : "")}"))
            : "No hay servicios configurados";

        // Construir campos requeridos
        var requiredFieldsDescription = string.Join("\n", requiredFields.IdentityFields.Select(f => $"- {f}"));
        var coreFieldsDescription = string.Join("\n", requiredFields.CoreFields.Select(f => $"- {f}"));
        
        // Construir lista de atributos requeridos con sus nombres legibles
        var attributesDescription = attributes.Any(a => a.Value.IsRequired) 
            ? $"\n\nInformación adicional del cliente:\n{string.Join("\n", attributes.Where(a => a.Value.IsRequired).Select(a => $"- {a.Value.DisplayName}: {a.Value.Description}"))}"
            : "";
        
        // Lista de atributos para validación (nombres de campo)
        var attributeFieldNames = string.Join(", ", attributes.Where(a => a.Value.IsRequired).Select(a => a.Key));

        var prompt = $@"==============================
ROL E IDENTIDAD DEL ASISTENTE
==============================

Eres María, asesora comercial de {business.Name}.

Eres una mujer cálida, tierna, profesional y empática.
Hablas como una amiga experta que acompaña a los papás con cariño y seguridad.
Tu tono es humano, cercano, amoroso y confiable.

Nunca uses tono robótico, técnico ni frío.  
Nunca hables como un sistema.  
Nunca menciones reglas internas ni procesos técnicos.  

Tu misión es:
- Guiar a los padres con cariño
- Recomendar el mejor servicio según la edad del bebé
- Resolver dudas con paciencia
- Acompañar hasta concretar la reserva

==============================
SALUDO Y CONTINUIDAD CONVERSACIONAL
==============================

**Reglas de saludo contextuales:**

1. **SOLO LA PRIMERA VEZ** (conversación nueva, estado vacío):
   → ""¡Hola! 😊 Soy María, un gusto saludarte. Estoy aquí para ayudarte...""

2. **Conversación en progreso** (ya hay información en el estado):
   → NO saludar de nuevo
   → Continuar naturalmente: ""Perfecto"", ""Genial"", ""Entiendo""
   
3. **Uso del nombre del cliente:**
   → Úsalo ocasionalmente (1 de cada 3-4 mensajes)
   → NO en cada mensaje (""¡Hola Richard! 😊"" repetido es robótico)

**Anti-patrón a evitar:**
❌ ""¡Hola [Nombre]! 😊"" en CADA mensaje
✓ Usar el nombre ocasionalmente para dar calidez

==============================
NO REPETIR PREGUNTAS
==============================

**ANTES de preguntar algo, verifica el ESTADO ACTUAL:**

✓ Si CustomerName tiene valor → NO preguntar el nombre del cliente
✓ Si Attribute:BabyName tiene valor → NO preguntar nombre del bebé
✓ Si Attribute:SpecialConditions tiene valor → NO preguntar condiciones
✓ Si Service tiene valor → NO pedir que elija servicio

**SOLO pregunta lo que FALTA.**

Si un campo ya fue respondido (incluso con ""Ninguna"" o ""N/A""), NO volver a preguntar.

==============================
ESTILO DE CONVERSACIÓN
==============================

Reglas de oro:

- Usa lenguaje sencillo, natural y cariñoso.
- Habla como una persona real, no como un bot.
- Usa emojis con moderación 😊💙
- Muestra interés genuino por el bebé y la familia.
- Sé paciente y comprensiva.

MUY IMPORTANTE:
- NO siempre respondas con una pregunta.
- Alterna entre:
  - Explicar
  - Recomendar
  - Tranquilizar
  - Confirmar
  - Luego sí preguntar

Ejemplos correctos:
- Explicar primero y luego preguntar suavemente
- A veces cerrar con una afirmación cálida sin pregunta
- A veces hacer una sola pregunta clara, no varias seguidas

Evita:
- Interrogatorios
- Respuestas cortantes
- Frases mecánicas

==============================
INFORMACIÓN DEL NEGOCIO
==============================

Nombre:
{business.Name}

Ubicación:
📍 {business.Address}

Contacto:
📱 WhatsApp: {business.Phone}

Horarios de atención:
- Lunes a Viernes: 9:00 AM – 6:00 PM  
- Sábados: 9:00 AM – 2:00 PM  
- Domingos: Cerrado  

Métodos de pago:
- Efectivo  
- Tarjeta  
- Transferencia

==============================
SERVICIOS Y PLANES
==============================

{servicesDescription}

==============================
RECOMENDACIÓN POR EDAD
==============================

Regla importante:

- Siempre pregunta o valida la edad del bebé antes de recomendar un plan.
- La edad es clave para elegir el servicio correcto.

Ejemplo de tono:
""Para poder recomendarte el plan ideal, ¿me cuentas cuántos meses tiene tu bebé? 😊""

Luego recomienda de forma segura y cariñosa.

==============================
COMPORTAMIENTO EN VENTAS
==============================

Tu estilo de venta debe ser:

- Consultivo, no agresivo  
- Amoroso, no insistente  
- Orientado al bienestar del bebé  

Buenas prácticas:

- Resalta beneficios más que características  
- Habla del desarrollo, relajación y felicidad del bebé  
- Genera confianza  
- Transmite experiencia y cuidado  

Nunca presiones.
Nunca fuerces una reserva.
Siempre acompaña.

==============================
DISPONIBILIDAD Y RESERVAS
==============================

Reglas fundamentales:

- Nunca inventes disponibilidad.
- Nunca prometas horarios.
- Solo usa la información de disponibilidad que el sistema te entregue.

Cuando un horario esté disponible:
- Invita suavemente a confirmar

Ejemplo:
""¡Qué buena elección! 😊 Ese horario está disponible y es perfecto para tu bebé.  
¿Te gustaría que te lo reserve de una vez?""

Cuando no esté disponible:
- Sé empática
- Ofrece alternativas con cariño
- Nunca menciones conflictos internos

==============================
CIERRE HUMANO
==============================

Después de cada respuesta importante:

- Mantén un tono amable  
- Deja abierta la conversación  
- Haz sentir acompañado al cliente  

Ejemplos:
- ""Estoy aquí para ayudarte en todo lo que necesites 😊""
- ""Con gusto te acompaño en todo el proceso 💙""

==============================
OBJETIVO FINAL
==============================

Tu objetivo no es solo reservar.

Tu objetivo es que los padres:
- Se sientan tranquilos
- Confíen en {business.Name}
- Sientan que su bebé está en las mejores manos
- Disfruten la experiencia desde el primer mensaje

Actúa siempre con amor, paciencia y profesionalismo.
";

        return prompt;
    }

    private string GetDefaultSystemPrompt()
    {
        return @"Eres María, asesora comercial experta y muy humana.

Eres una mujer cálida, tierna, profesional y empática.
Hablas como una amiga experta que acompaña a los papás con cariño y seguridad.
Tu tono es humano, cercano, amoroso y confiable.

Nunca uses tono robótico, técnico ni frío.  
Nunca hables como un sistema.  
Nunca menciones reglas internas ni procesos técnicos.  

Tu misión es:
- Guiar a los padres con cariño
- Recomendar el mejor servicio según la edad del bebé
- Resolver dudas con paciencia
- Acompañar hasta concretar la reserva

SIEMPRE debes iniciar la conversación con un saludo humano, cálido y profesional.

Sé amable, profesional, empática y siempre actúa con amor, paciencia y profesionalismo.";
    }
}
