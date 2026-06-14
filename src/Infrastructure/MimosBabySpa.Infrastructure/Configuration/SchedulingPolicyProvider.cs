using System.Text.Json;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Infrastructure.Configuration;

/// <summary>
/// Lee BusinessConfiguration (Key=SchedulingPolicy) y deserializa a <see cref="AvailabilityParams"/>.
/// </summary>
public class SchedulingPolicyProvider : ISchedulingPolicyProvider
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SchedulingPolicyProvider> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SchedulingPolicyProvider(
        IUnitOfWork unitOfWork,
        ILogger<SchedulingPolicyProvider> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AvailabilityParams> GetAsync(Guid businessId, CancellationToken cancellationToken = default)
    {
        var config = await _unitOfWork.BusinessConfigurations
            .GetByBusinessIdAndKeyAsync(businessId, BusinessConfigurationKey.SchedulingPolicy);

        if (config == null || string.IsNullOrWhiteSpace(config.Value) || config.Value == "{}")
        {
            _logger.LogWarning(
                "SchedulingPolicy: sin configuración para BusinessId={BusinessId} — no se generarán slots",
                businessId);
            return AvailabilityParams.Default;
        }

        try
        {
            var policy = JsonSerializer.Deserialize<AvailabilityParams>(config.Value, JsonOptions);
            if (policy == null)
            {
                _logger.LogWarning(
                    "SchedulingPolicy: JSON vacío para BusinessId={BusinessId}",
                    businessId);
                return AvailabilityParams.Default;
            }

            return policy;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "SchedulingPolicy: JSON inválido para BusinessId={BusinessId}", businessId);
            return AvailabilityParams.Default;
        }
    }
}
