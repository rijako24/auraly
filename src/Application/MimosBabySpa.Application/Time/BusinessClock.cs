using MimosBabySpa.Application.Configuration;

using Microsoft.Extensions.Logging;



namespace MimosBabySpa.Application.Time;



/// <summary>

/// Resuelve el instante actual en la zona horaria del negocio.

/// Fuente de TZ: Integrations.GoogleCalendar.TimeZone, fallback America/Bogota.

/// </summary>

public sealed class BusinessClock : IBusinessClock

{

    public const string DefaultTimeZoneId = BusinessTimeZoneResolver.DefaultIanaTimeZoneId;



    private readonly IIntegrationsConfigProvider _integrations;

    private readonly ILogger<BusinessClock> _logger;



    public BusinessClock(

        IIntegrationsConfigProvider integrations,

        ILogger<BusinessClock> logger)

    {

        _integrations = integrations;

        _logger = logger;

    }



    public async Task<BusinessClockSnapshot> GetSnapshotAsync(

        Guid businessId,

        CancellationToken cancellationToken = default)

    {

        var integrations = await _integrations.GetAsync(businessId, cancellationToken);

        var requestedTimeZoneId = integrations?.GoogleCalendar?.TimeZone;

        var timeZone = BusinessTimeZoneResolver.Resolve(requestedTimeZoneId);



        if (!string.IsNullOrWhiteSpace(requestedTimeZoneId) &&

            !string.Equals(timeZone.Id, requestedTimeZoneId, StringComparison.OrdinalIgnoreCase) &&

            timeZone.Id != BusinessTimeZoneResolver.DefaultIanaTimeZoneId)

        {

            _logger.LogDebug(

                "BusinessClock: mapped TimeZone {Requested} → {Resolved} for BusinessId={BusinessId}",

                requestedTimeZoneId, timeZone.Id, businessId);

        }



        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);

        var today = DateOnly.FromDateTime(now.DateTime);



        return new BusinessClockSnapshot(businessId, now, today, timeZone);

    }

}


