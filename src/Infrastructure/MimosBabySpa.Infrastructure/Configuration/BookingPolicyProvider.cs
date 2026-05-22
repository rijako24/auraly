using System.Text.Json;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Infrastructure.Configuration;

/// <summary>
/// Lee BusinessConfiguration (Key=BookingPolicy) y deserializa a <see cref="BookingPolicyParams"/>.
/// </summary>
public class BookingPolicyProvider : IBookingPolicyProvider
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BookingPolicyProvider> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public BookingPolicyProvider(
        IUnitOfWork unitOfWork,
        ILogger<BookingPolicyProvider> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BookingPolicyParams> GetAsync(Guid businessId, CancellationToken cancellationToken = default)
    {
        var config = await _unitOfWork.BusinessConfigurations
            .GetByBusinessIdAndKeyAsync(businessId, BusinessConfigurationKey.BookingPolicy);

        if (config == null || string.IsNullOrWhiteSpace(config.Value) || config.Value == "{}")
        {
            _logger.LogWarning(
                "BookingPolicy: sin configuración para BusinessId={BusinessId} — anticipo deshabilitado",
                businessId);
            return BookingPolicyParams.Default;
        }

        try
        {
            var policy = JsonSerializer.Deserialize<BookingPolicyParams>(config.Value, JsonOptions);
            if (policy == null)
            {
                _logger.LogWarning(
                    "BookingPolicy: JSON vacío para BusinessId={BusinessId}",
                    businessId);
                return BookingPolicyParams.Default;
            }

            return policy;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "BookingPolicy: JSON inválido para BusinessId={BusinessId}", businessId);
            return BookingPolicyParams.Default;
        }
    }
}
