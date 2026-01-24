using System.Reflection;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Implementación del servicio de contexto de conversación.
/// Proporciona métodos tipados para gestionar el estado de conversación sin usar strings mágicos.
/// </summary>
public class ConversationContextService : IConversationContextService
{
    private readonly IConversationStateRepository _stateRepository;
    private readonly IConversationRepository _conversationRepository;
    private readonly ILogger<ConversationContextService> _logger;

    public ConversationContextService(
        IConversationStateRepository stateRepository,
        IConversationRepository conversationRepository,
        ILogger<ConversationContextService> logger)
    {
        _stateRepository = stateRepository;
        _conversationRepository = conversationRepository;
        _logger = logger;
    }

    public async Task<ConversationState> GetAsync(Guid conversationId)
    {
        return await _stateRepository.GetAsync(conversationId);
    }

    public async Task SetIdentityAsync(Guid conversationId, string? name = null, string? phone = null, string? email = null)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Conversación {ConversationId} no encontrada", conversationId);
            return;
        }

        var state = await _stateRepository.GetAsync(conversationId);
        
        if (!string.IsNullOrWhiteSpace(name))
            state.CustomerName = name;
        if (!string.IsNullOrWhiteSpace(phone))
            state.Phone = phone;
        if (!string.IsNullOrWhiteSpace(email))
            state.Email = email;

        await _stateRepository.SaveAsync(conversationId, conversation.BusinessId, state);
        
        _logger.LogDebug("Identidad actualizada para conversación {ConversationId}", conversationId);
    }

    public async Task SetIntentAsync(Guid conversationId, IntentType intent)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Conversación {ConversationId} no encontrada", conversationId);
            return;
        }

        var state = await _stateRepository.GetAsync(conversationId);
        state.LastIntent = state.CurrentIntent;
        state.CurrentIntent = intent;

        await _stateRepository.SaveAsync(conversationId, conversation.BusinessId, state);
        
        _logger.LogDebug("Intención actualizada para conversación {ConversationId}: {Intent}", conversationId, intent);
    }


    public async Task SetAttributeAsync(Guid conversationId, string key, string value)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Conversación {ConversationId} no encontrada", conversationId);
            return;
        }

        var state = await _stateRepository.GetAsync(conversationId);
        state.Attributes[key] = value;

        await _stateRepository.SaveAsync(conversationId, conversation.BusinessId, state);
        
        _logger.LogDebug("Atributo '{Key}' establecido para conversación {ConversationId}: {Value}", key, conversationId, value);
    }

    public async Task RemoveAttributeAsync(Guid conversationId, string key)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Conversación {ConversationId} no encontrada", conversationId);
            return;
        }

        var state = await _stateRepository.GetAsync(conversationId);
        state.Attributes.Remove(key);

        await _stateRepository.SaveAsync(conversationId, conversation.BusinessId, state);
        
        _logger.LogDebug("Atributo '{Key}' eliminado de conversación {ConversationId}", key, conversationId);
    }

    public async Task SetScheduleAsync(Guid conversationId, DateOnly? date = null, TimeOnly? time = null, int? durationMinutes = null)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Conversación {ConversationId} no encontrada", conversationId);
            return;
        }

        var state = await _stateRepository.GetAsync(conversationId);
        
        if (date.HasValue)
            state.DesiredDate = date;
        if (time.HasValue)
            state.DesiredTime = time;
        if (durationMinutes.HasValue)
            state.DurationMinutes = durationMinutes;

        await _stateRepository.SaveAsync(conversationId, conversation.BusinessId, state);
        
        _logger.LogDebug("Programación actualizada para conversación {ConversationId}: {Date} {Time} ({Duration} min)", 
            conversationId, date, time, durationMinutes);
    }

    public async Task SetServiceAsync(Guid conversationId, string? service = null)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Conversación {ConversationId} no encontrada", conversationId);
            return;
        }

        var state = await _stateRepository.GetAsync(conversationId);
        
        if (!string.IsNullOrWhiteSpace(service))
            state.Service = service;

        await _stateRepository.SaveAsync(conversationId, conversation.BusinessId, state);
        
        _logger.LogDebug("Servicio actualizado para conversación {ConversationId}: {Service}", 
            conversationId, service);
    }

    public async Task SetAvailabilityAsync(Guid conversationId, bool result)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Conversación {ConversationId} no encontrada", conversationId);
            return;
        }

        var state = await _stateRepository.GetAsync(conversationId);
        state.AvailabilityChecked = true;
        state.LastAvailabilityResult = result;
        state.LastAvailabilityCheckAt = DateTime.UtcNow;

        await _stateRepository.SaveAsync(conversationId, conversation.BusinessId, state);
        
        _logger.LogDebug("Disponibilidad registrada para conversación {ConversationId}: {Result}", conversationId, result);
    }

    public async Task ClearAvailabilityAsync(Guid conversationId)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Conversación {ConversationId} no encontrada", conversationId);
            return;
        }

        var state = await _stateRepository.GetAsync(conversationId);
        state.AvailabilityChecked = false;
        state.LastAvailabilityResult = null;
        state.LastAvailabilityCheckAt = null;

        await _stateRepository.SaveAsync(conversationId, conversation.BusinessId, state);
        
        _logger.LogDebug("Disponibilidad limpiada para conversación {ConversationId}", conversationId);
    }

    public async Task MarkReservationConfirmedAsync(Guid conversationId, string reservationId)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Conversación {ConversationId} no encontrada", conversationId);
            return;
        }

        var state = await _stateRepository.GetAsync(conversationId);
        state.ReservationConfirmed = true;
        state.ReservationId = reservationId;

        await _stateRepository.SaveAsync(conversationId, conversation.BusinessId, state);
        
        _logger.LogInformation("Reserva confirmada para conversación {ConversationId}: {ReservationId}", conversationId, reservationId);
    }

    public async Task ResetFlowAsync(Guid conversationId)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Conversación {ConversationId} no encontrada", conversationId);
            return;
        }

        var state = await _stateRepository.GetAsync(conversationId);
        
        // Mantener identidad básica
        var customerName = state.CustomerName;
        var phone = state.Phone;
        var email = state.Email;

        // Crear nuevo estado limpio
        state = new ConversationState
        {
            CustomerName = customerName,
            Phone = phone,
            Email = email
        };

        await _stateRepository.SaveAsync(conversationId, conversation.BusinessId, state);
        
        _logger.LogInformation("Flujo reseteado para conversación {ConversationId}", conversationId);
    }

    public async Task SetFieldAsync(Guid conversationId, string field, string value)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            _logger.LogWarning("Intento de establecer campo vacío para conversación {ConversationId}", conversationId);
            return;
        }

        var conversation = await _conversationRepository.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Conversación {ConversationId} no encontrada", conversationId);
            return;
        }

        var state = await _stateRepository.GetAsync(conversationId);
        
        // Intentar mapear el campo a una propiedad del modelo usando reflexión (100% genérico)
        var fieldMapped = TryMapFieldToProperty(state, field, value);

        // Si no se mapeó a ninguna propiedad conocida, guardarlo como atributo dinámico
        if (!fieldMapped)
        {
            var normalizedField = NormalizeFieldName(field);
            state.Attributes[normalizedField] = value;
            _logger.LogDebug("Campo '{Field}' guardado como atributo dinámico para conversación {ConversationId}", normalizedField, conversationId);
        }
        else
        {
            _logger.LogDebug("Campo '{Field}' mapeado a propiedad del estado para conversación {ConversationId}", field, conversationId);
        }

        await _stateRepository.SaveAsync(conversationId, conversation.BusinessId, state);
    }

    /// <summary>
    /// Intenta mapear un campo a una propiedad del modelo ConversationState usando reflexión.
    /// 100% genérico: no requiere hardcoding de nombres de campos.
    /// </summary>
    private bool TryMapFieldToProperty(ConversationState state, string fieldName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Normalizar nombre del campo para comparación
        var normalizedField = NormalizeFieldName(fieldName);

        // Obtener todas las propiedades públicas del modelo ConversationState
        var properties = typeof(ConversationState).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            // Ignorar propiedades que no son settables o son de tipo Dictionary
            if (!property.CanWrite || property.PropertyType == typeof(Dictionary<string, string>))
                continue;

            // Normalizar nombre de la propiedad para comparación
            var normalizedPropertyName = NormalizeFieldName(property.Name);

            // Verificar si el campo coincide con el nombre de la propiedad (case-insensitive, sin guiones bajos)
            if (normalizedField == normalizedPropertyName || 
                normalizedField == property.Name.ToLowerInvariant() ||
                normalizedField == property.Name.Replace("_", "").ToLowerInvariant())
            {
                try
                {
                    // Intentar convertir y asignar el valor según el tipo de la propiedad
                    object? convertedValue = null;

                    if (property.PropertyType == typeof(string))
                    {
                        convertedValue = value;
                    }
                    else if (property.PropertyType == typeof(string))
                    {
                        // Ya manejado arriba, pero por si acaso
                        convertedValue = value;
                    }
                    else if (property.PropertyType == typeof(int?))
                    {
                        if (int.TryParse(value, out var intValue))
                            convertedValue = intValue;
                        else
                            return false;
                    }
                    else if (property.PropertyType == typeof(bool?))
                    {
                        if (bool.TryParse(value, out var boolValue))
                            convertedValue = boolValue;
                        else
                            return false;
                    }
                    else if (property.PropertyType == typeof(DateOnly?))
                    {
                        if (DateOnly.TryParse(value, out var dateValue))
                            convertedValue = dateValue;
                        else
                            return false;
                    }
                    else if (property.PropertyType == typeof(TimeOnly?))
                    {
                        if (TimeOnly.TryParse(value, out var timeValue))
                            convertedValue = timeValue;
                        else
                            return false;
                    }
                    else if (property.PropertyType == typeof(DateTime?))
                    {
                        if (DateTime.TryParse(value, out var dateTimeValue))
                            convertedValue = dateTimeValue;
                        else
                            return false;
                    }
                    else if (property.PropertyType.IsEnum)
                    {
                        if (Enum.TryParse(property.PropertyType, value, true, out var enumValue))
                            convertedValue = enumValue;
                        else
                            return false;
                    }
                    else if (property.PropertyType.IsGenericType && 
                             property.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                             Nullable.GetUnderlyingType(property.PropertyType)?.IsEnum == true)
                    {
                        var enumType = Nullable.GetUnderlyingType(property.PropertyType);
                        if (enumType != null && Enum.TryParse(enumType, value, true, out var enumValue))
                            convertedValue = enumValue;
                        else
                            return false;
                    }
                    else
                    {
                        // Tipo no soportado, no mapear
                        return false;
                    }

                    // Asignar el valor convertido a la propiedad
                    if (convertedValue != null)
                    {
                        property.SetValue(state, convertedValue);

                        // Casos especiales: si se establece LastAvailabilityResult, también actualizar flags relacionados
                        if (property.Name == nameof(ConversationState.LastAvailabilityResult) && convertedValue is bool availabilityResult)
                        {
                            state.AvailabilityChecked = true;
                            state.LastAvailabilityCheckAt = DateTime.UtcNow;
                        }

                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error al mapear campo '{Field}' a propiedad '{Property}'", fieldName, property.Name);
                    return false;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Normaliza el nombre del campo para comparación (case-insensitive, convierte snake_case a camelCase).
    /// </summary>
    private static string NormalizeFieldName(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return string.Empty;

        // Convertir a minúsculas y reemplazar guiones bajos y guiones
        var normalized = field.ToLowerInvariant()
            .Replace("_", string.Empty)
            .Replace("-", string.Empty);
        return normalized;
    }
}
