using Microsoft.Extensions.Logging;
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
                    result.Reason = $"El servicio '{serviceName}' no existe en el catálogo";
                    result.ErrorCode = "SERVICE_NOT_FOUND";
                    return result;
                }

                if (!service.IsActive)
                {
                    result.IsValid = false;
                    result.Reason = $"El servicio '{serviceName}' no está disponible actualmente";
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
                    "La fecha seleccionada es en más de 3 meses. Considera que las políticas pueden cambiar");
            }

            var hour = desiredTime.Hour;
            if (hour < 8 || hour >= 20)
                result.Warnings.Add("El horario seleccionado puede estar fuera del horario de atención habitual");

            if (desiredDate.DayOfWeek == DayOfWeek.Sunday)
                result.Warnings.Add("El día seleccionado es domingo. Verifica que el negocio esté abierto");

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
