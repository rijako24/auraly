using Microsoft.Extensions.Logging;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.BusinessRules;

/// <summary>
/// ImplementaciÃ³n del Business Rule Engine.
/// Encapsula todas las reglas de negocio especÃ­ficas del dominio.
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
        string serviceName,
        DateOnly desiredDate,
        TimeOnly desiredTime,
        CancellationToken cancellationToken = default)
    {
        var result = new BusinessRuleValidationResult { IsValid = true };

        try
        {
            if (!string.IsNullOrWhiteSpace(serviceName))
            {
                var service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, serviceName);
                if (service == null)
                {
                    result.IsValid = false;
                    result.Reason = $"El servicio '{serviceName}' no existe en el catÃ¡logo";
                    result.ErrorCode = "SERVICE_NOT_FOUND";
                    return result;
                }

                if (!service.IsActive)
                {
                    result.IsValid = false;
                    result.Reason = $"El servicio '{serviceName}' no estÃ¡ disponible actualmente";
                    result.ErrorCode = "SERVICE_INACTIVE";
                    return result;
                }
            }

            var today = DateOnly.FromDateTime(DateTime.Today);
            if (desiredDate < today)
            {
                result.IsValid = false;
                result.Reason = "No se pueden hacer reservas en fechas pasadas";
                result.ErrorCode = "DATE_IN_PAST";
                return result;
            }

            var maxAdvanceDate = today.AddMonths(3);
            if (desiredDate > maxAdvanceDate)
            {
                result.Warnings.Add(
                    "La fecha seleccionada es en mÃ¡s de 3 meses. Considera que las polÃ­ticas pueden cambiar");
            }

            var hour = desiredTime.Hour;
            if (hour < 8 || hour >= 20)
                result.Warnings.Add("El horario seleccionado puede estar fuera del horario de atenciÃ³n habitual");

            if (desiredDate.DayOfWeek == DayOfWeek.Sunday)
                result.Warnings.Add("El dÃ­a seleccionado es domingo. Verifica que el negocio estÃ© abierto");

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
            // En una implementaciÃ³n real, aquÃ­ se consultarÃ­a el perfil del cliente
            // y se determinarÃ­an beneficios (cliente frecuente, promociones, etc.)
            
            // Por ahora, retornar contexto vacÃ­o
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
            // NO hardcodear validaciones especÃ­ficas aquÃ­
            
            // TODO: Obtener AttributeDefinition desde configuration provider
            // y validar segÃºn:
            // - definition.Type (Number, Text, Date, etc.)
            // - definition.ValidationPattern (regex)
            // - definition.AllowedValues (lista)
            
            _logger.LogDebug(
                "ValidaciÃ³n genÃ©rica de atributo: {AttributeName} = {Value}",
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

