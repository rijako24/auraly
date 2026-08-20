using Microsoft.Extensions.Logging;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Application.Time;

namespace Auraly.Platform.Application.BusinessRules;

/// <summary>
/// ImplementaciÃ³n del Business Rule Engine.
/// Encapsula todas las reglas de negocio especÃ­ficas del dominio.
/// </summary>
public class BusinessRuleEngine : IBusinessRuleEngine
{
    private readonly ILogger<BusinessRuleEngine> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _businessClock;

    public BusinessRuleEngine(
        ILogger<BusinessRuleEngine> logger,
        IUnitOfWork unitOfWork,
        IBusinessClock businessClock)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
        _businessClock = businessClock;
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

            var clock = await _businessClock.GetSnapshotAsync(
                businessId,
                cancellationToken);
            var today = clock.Today;
            if (desiredDate < today)
            {
                result.IsValid = false;
                result.Reason = "No se pueden hacer reservas en fechas pasadas";
                result.ErrorCode = "DATE_IN_PAST";
                return result;
            }

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
}

