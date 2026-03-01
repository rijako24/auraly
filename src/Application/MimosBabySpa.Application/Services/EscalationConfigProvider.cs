using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public class EscalationConfigProvider : IEscalationConfigProvider
{
    private const int DefaultThreshold = 2;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<EscalationConfigProvider> _logger;

    public EscalationConfigProvider(
        IUnitOfWork unitOfWork,
        ILogger<EscalationConfigProvider> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<int> GetConsecutiveDegradedThresholdAsync(CancellationToken ct = default)
    {
        var config = await _unitOfWork.SystemConfigurations
            .GetByKeyAsync(SystemConfigurationKey.HumanEscalationErrorThreshold);
        if (config == null || string.IsNullOrWhiteSpace(config.Value))
            return DefaultThreshold;

        return int.TryParse(config.Value.Trim(), out var t) && t > 0 ? t : DefaultThreshold;
    }
}
