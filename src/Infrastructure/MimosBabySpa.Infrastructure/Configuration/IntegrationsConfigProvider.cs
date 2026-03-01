using System.Text.Json;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Infrastructure.Configuration;

/// <summary>
/// Implementación de IIntegrationsConfigProvider.
/// Lee BusinessConfiguration (Key=Integrations) y deserializa a IntegrationsConfiguration.
/// </summary>
public class IntegrationsConfigProvider : IIntegrationsConfigProvider
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<IntegrationsConfigProvider> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IntegrationsConfigProvider(
        IUnitOfWork unitOfWork,
        ILogger<IntegrationsConfigProvider> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IntegrationsConfiguration?> GetAsync(Guid businessId, CancellationToken cancellationToken = default)
    {
        var config = await _unitOfWork.BusinessConfigurations
            .GetByBusinessIdAndKeyAsync(businessId, BusinessConfigurationKey.Integrations);

        if (config == null || string.IsNullOrWhiteSpace(config.Value) || config.Value == "{}")
        {
            _logger.LogDebug("Integrations: sin configuración para BusinessId={BusinessId}", businessId);
            return null;
        }

        try
        {
            var integrations = JsonSerializer.Deserialize<IntegrationsConfiguration>(config.Value, JsonOptions);
            return integrations;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Integrations: JSON inválido para BusinessId={BusinessId}", businessId);
            return null;
        }
    }
}
