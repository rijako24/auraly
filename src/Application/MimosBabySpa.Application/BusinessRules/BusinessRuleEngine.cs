using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.BusinessRules;

/// <summary>
/// Implementación del Business Rule Engine.
/// Encapsula todas las reglas de negocio específicas del dominio.
/// </summary>
public class BusinessRuleEngine : IBusinessRuleEngine
{
    private readonly ILogger<BusinessRuleEngine> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public BusinessRuleEngine(
        ILogger<BusinessRuleEngine> logger,
        IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public async Task<BusinessRuleValidationResult> ValidateReservationAsync(
        Guid businessId,
        ConversationState state,
        CancellationToken cancellationToken = default)
    {
        var result = new BusinessRuleValidationResult { IsValid = true };

        try
        {
            // 1. Validar que el servicio existe
            if (!string.IsNullOrWhiteSpace(state.Service))
            {
                var service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(
                    businessId, state.Service);

                if (service == null)
                {
                    result.IsValid = false;
                    result.Reason = $"El servicio '{state.Service}' no existe en el catálogo";
                    result.ErrorCode = "SERVICE_NOT_FOUND";
                    return result;
                }

                // Validar que el servicio esté activo
                if (!service.IsActive)
                {
                    result.IsValid = false;
                    result.Reason = $"El servicio '{state.Service}' no está disponible actualmente";
                    result.ErrorCode = "SERVICE_INACTIVE";
                    return result;
                }

                // Agregar duración al contexto si no está establecida
                if (!state.DurationMinutes.HasValue && service.DurationMinutes > 0)
                {
                    result.Context["suggested_duration"] = service.DurationMinutes;
                }
            }

            // 2. Validar que la fecha no sea en el pasado
            if (state.DesiredDate.HasValue)
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                if (state.DesiredDate.Value < today)
                {
                    result.IsValid = false;
                    result.Reason = "No se pueden hacer reservas en fechas pasadas";
                    result.ErrorCode = "DATE_IN_PAST";
                    return result;
                }

                // Advertencia si la fecha es muy lejana (más de 3 meses)
                var maxAdvanceDate = today.AddMonths(3);
                if (state.DesiredDate.Value > maxAdvanceDate)
                {
                    result.Warnings.Add(
                        $"La fecha seleccionada es en más de 3 meses. " +
                        $"Considera que las políticas pueden cambiar");
                }
            }

            // 3. Validar horarios de operación
            if (state.DesiredDate.HasValue && state.DesiredTime.HasValue)
            {
                var dayOfWeek = state.DesiredDate.Value.DayOfWeek;
                var hour = state.DesiredTime.Value.Hour;

                // Validación genérica: horario típico de negocio (8am - 8pm)
                // En producción, esto debería venir de configuración del negocio
                if (hour < 8 || hour >= 20)
                {
                    result.Warnings.Add(
                        "El horario seleccionado puede estar fuera del horario de atención habitual");
                }

                // Advertencia para domingos (común que esté cerrado)
                if (dayOfWeek == DayOfWeek.Sunday)
                {
                    result.Warnings.Add(
                        "El día seleccionado es domingo. Verifica que el negocio esté abierto");
                }
            }

            // 4. Validar atributos de negocio de forma genérica
            // Las validaciones específicas deben venir de la configuración
            // No hardcodear nombres de atributos específicos aquí

            _logger.LogInformation(
                "Validación de reglas de negocio completada: IsValid={IsValid}, Warnings={WarningCount}",
                result.IsValid, result.Warnings.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al validar reglas de negocio");
            
            return new BusinessRuleValidationResult
            {
                IsValid = false,
                Reason = "Error interno al validar reglas de negocio",
                ErrorCode = "INTERNAL_ERROR"
            };
        }
    }

    public Task<BusinessRuleContext> GetBusinessContextAsync(
        Guid businessId,
        string phone,
        string? service,
        CancellationToken cancellationToken = default)
    {
        var context = new BusinessRuleContext();

        try
        {
            // Buscar historial del cliente
            // En una implementación real, aquí se consultaría el perfil del cliente
            // y se determinarían beneficios (cliente frecuente, promociones, etc.)
            
            // Por ahora, retornar contexto vacío
            _logger.LogDebug(
                "Contexto de negocio obtenido para cliente {Phone}",
                phone);

            return Task.FromResult(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener contexto de negocio");
            return Task.FromResult(context);
        }
    }

    public BusinessRuleValidationResult ValidateBusinessAttribute(
        Guid businessId,
        string attributeName,
        string attributeValue)
    {
        var result = new BusinessRuleValidationResult { IsValid = true };

        try
        {
            // IMPORTANTE: Las validaciones deben venir de AttributeDefinition configurado
            // NO hardcodear validaciones específicas aquí
            
            // TODO: Obtener AttributeDefinition desde BusinessConfigurationProvider
            // y validar según:
            // - definition.Type (Number, Text, Date, etc.)
            // - definition.ValidationPattern (regex)
            // - definition.AllowedValues (lista)
            
            _logger.LogDebug(
                "Validación genérica de atributo: {AttributeName} = {Value}",
                attributeName, attributeValue);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al validar atributo de negocio");
            
            return new BusinessRuleValidationResult
            {
                IsValid = false,
                Reason = "Error al validar el atributo",
                ErrorCode = "VALIDATION_ERROR"
            };
        }
    }
}
