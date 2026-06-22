using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Infrastructure.Configuration;

/// <summary>
/// Reads business scheduling rules from BusinessSchedulingSettings.
/// Working hours live in BusinessWorkingHours and EmployeeWorkingHours.
/// </summary>
public class SchedulingPolicyProvider : ISchedulingPolicyProvider
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SchedulingPolicyProvider> _logger;

    public SchedulingPolicyProvider(
        IUnitOfWork unitOfWork,
        ILogger<SchedulingPolicyProvider> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<AvailabilityParams> GetAsync(Guid businessId, CancellationToken cancellationToken = default)
    {
        var settings = await _unitOfWork.BusinessSchedulingSettings
            .GetByBusinessIdAsync(businessId, cancellationToken);

        if (settings is null)
        {
            _logger.LogWarning(
                "SchedulingPolicy: missing BusinessSchedulingSettings for BusinessId={BusinessId}; using defaults",
                businessId);
            return AvailabilityParams.Default;
        }

        return new AvailabilityParams
        {
            SlotIntervalMinutes = settings.SlotIntervalMinutes,
            BufferBetweenAppointmentsMinutes = settings.BufferBetweenAppointmentsMinutes,
            RequireEmployee = settings.RequireEmployee,
            EmployeeStrategy = settings.EmployeeStrategy
        };
    }
}
