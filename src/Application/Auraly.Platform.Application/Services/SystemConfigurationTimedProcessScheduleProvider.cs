using System.Text.Json;
using Microsoft.Extensions.Logging;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Services;

public sealed class SystemConfigurationTimedProcessScheduleProvider : ITimedProcessScheduleProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SystemConfigurationTimedProcessScheduleProvider> _logger;

    public SystemConfigurationTimedProcessScheduleProvider(
        IUnitOfWork unitOfWork,
        ILogger<SystemConfigurationTimedProcessScheduleProvider> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<TimedProcessScheduleSnapshot> GetScheduleAsync(CancellationToken ct = default)
    {
        var configuration = await _unitOfWork.SystemConfigurations
            .GetByKeyAsync(SystemConfigurationKey.BackgroundJobs);

        if (configuration is null || string.IsNullOrWhiteSpace(configuration.Value))
            return TimedProcessScheduleSnapshot.Empty;

        try
        {
            var configured = JsonSerializer.Deserialize<Dictionary<string, TimedProcessSchedule>>(
                configuration.Value,
                JsonOptions);

            if (configured is null || configured.Count == 0)
                return TimedProcessScheduleSnapshot.Empty;

            var schedules = configured
                .Where(item => !string.IsNullOrWhiteSpace(item.Key) && item.Value is not null)
                .ToDictionary(
                    item => item.Key.Trim(),
                    item => item.Value,
                    StringComparer.OrdinalIgnoreCase);

            return schedules.Count == 0
                ? TimedProcessScheduleSnapshot.Empty
                : new TimedProcessScheduleSnapshot(schedules);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "SystemConfigurationTimedProcessScheduleProvider: invalid BackgroundJobs configuration");
            return TimedProcessScheduleSnapshot.Empty;
        }
    }
}
