using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Time;

/// <summary>
/// Resuelve el instante actual en la zona horaria configurada del negocio.
/// </summary>
public sealed class BusinessClock : IBusinessClock
{
    public const string DefaultTimeZoneId = BusinessTimeZoneResolver.DefaultIanaTimeZoneId;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IIntegrationsConfigProvider _integrations;
    private readonly ILogger<BusinessClock> _logger;

    public BusinessClock(
        IUnitOfWork unitOfWork,
        IIntegrationsConfigProvider integrations,
        ILogger<BusinessClock> logger)
    {
        _unitOfWork = unitOfWork;
        _integrations = integrations;
        _logger = logger;
    }

    public async Task<BusinessClockSnapshot> GetSnapshotAsync(
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId);
        var requestedTimeZoneId = NormalizeTimeZoneId(business?.TimeZone);

        if (business is null)
        {
            _logger.LogWarning("BusinessClock: BusinessId={BusinessId} was not found; using default timezone", businessId);
        }

        if (requestedTimeZoneId is null)
        {
            var integrations = await _integrations.GetAsync(businessId, cancellationToken);
            requestedTimeZoneId = NormalizeTimeZoneId(integrations?.GoogleCalendar?.TimeZone);
        }

        requestedTimeZoneId ??= DefaultTimeZoneId;
        var timeZone = BusinessTimeZoneResolver.Resolve(requestedTimeZoneId);

        if (!string.Equals(timeZone.Id, requestedTimeZoneId, StringComparison.OrdinalIgnoreCase)
            && timeZone.Id != BusinessTimeZoneResolver.DefaultIanaTimeZoneId)
        {
            _logger.LogDebug(
                "BusinessClock: mapped TimeZone {Requested} -> {Resolved} for BusinessId={BusinessId}",
                requestedTimeZoneId,
                timeZone.Id,
                businessId);
        }

        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        var today = DateOnly.FromDateTime(now.DateTime);

        return new BusinessClockSnapshot(businessId, now, today, timeZone);
    }

    private static string? NormalizeTimeZoneId(string? timeZoneId) =>
        string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim();
}
