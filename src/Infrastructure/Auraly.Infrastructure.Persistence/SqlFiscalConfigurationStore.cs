using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlFiscalConfigurationStore(
    SqlServerConnectionFactory connections,
    TimeProvider timeProvider) : IFiscalConfigurationStore
{
    public async Task<FiscalResolutionConfiguration> GetAsync(
        Guid tenantId, Guid businessId, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;
            SELECT TOP(1) a.FiscalAuthorizationId,a.AuthorizationNumber,a.ValidFrom,a.ValidUntil,
                COALESCE(online.Prefix,offline.Prefix),
                COALESCE(a.AuthorizedRangeStart,online.RangeStart,offline.RangeStart),
                COALESCE(a.AuthorizedRangeEnd,online.RangeEnd,offline.RangeEnd),
                CONVERT(bit,CASE WHEN online.SeriesId IS NULL THEN 0 ELSE 1 END),
                CONVERT(bit,CASE WHEN offline.SeriesId IS NULL THEN 0 ELSE 1 END),
                CONVERT(bit,CASE WHEN secret.FiscalTechnicalKeySecretId IS NULL THEN 0 ELSE 1 END),
                seriesCursor.NextConsecutive,
                COALESCE(alerts.ExpirationWarningDays,3),
                COALESCE(alerts.RemainingNumberWarningThreshold,100)
            FROM dbo.FiscalAuthorizations a
            OUTER APPLY(SELECT TOP(1) s.SeriesId,s.Prefix,s.RangeStart,s.RangeEnd
                FROM dbo.FiscalSeries s WHERE s.BusinessId=a.BusinessId
                  AND s.FiscalAuthorizationId=a.FiscalAuthorizationId
                  AND s.DocumentType=N'SalesInvoice' AND s.EmitterKind=N'Server'
                  AND s.DeviceId IS NULL AND s.IsActive=1 ORDER BY s.CreatedAt DESC) online
            OUTER APPLY(SELECT TOP(1) s.SeriesId,s.Prefix,s.RangeStart,s.RangeEnd
                FROM dbo.FiscalSeries s WHERE s.BusinessId=a.BusinessId
                  AND s.FiscalAuthorizationId=a.FiscalAuthorizationId
                  AND s.DocumentType=N'SalesInvoice' AND s.EmitterKind=N'Device'
                  AND s.DeviceId IS NOT NULL AND s.IsActive=1 ORDER BY s.CreatedAt) offline
            OUTER APPLY(SELECT TOP(1) k.FiscalTechnicalKeySecretId
                FROM dbo.FiscalTechnicalKeySecrets k WHERE k.BusinessId=a.BusinessId
                  AND k.FiscalAuthorizationId=a.FiscalAuthorizationId
                  AND k.TechnicalKeyVersion=a.TechnicalKeyVersion
                  AND k.Environment=a.Environment) secret
            OUTER APPLY(SELECT c.NextConsecutive
                FROM dbo.FiscalSeriesCursors c WHERE c.SeriesId=online.SeriesId) seriesCursor
            LEFT JOIN fiscal.FiscalResolutionAlertSettings alerts
              ON alerts.BusinessId=a.BusinessId
            WHERE a.BusinessId=@BusinessId AND a.IsActive=1
              AND EXISTS(SELECT 1 FROM dbo.FiscalSeries salesSeries
                  WHERE salesSeries.FiscalAuthorizationId=a.FiscalAuthorizationId
                    AND salesSeries.BusinessId=a.BusinessId
                    AND salesSeries.DocumentType=N'SalesInvoice')
            ORDER BY CASE WHEN online.SeriesId IS NOT NULL THEN 0 ELSE 1 END,
                     a.CreatedAt DESC,a.FiscalAuthorizationId;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new FiscalResolutionConfiguration(
                businessId, null, null, null, null, null, null, null, false, false, false);

        var validFrom = DateOnly.FromDateTime(reader.GetDateTime(2));
        var validUntil = DateOnly.FromDateTime(reader.GetDateTime(3));
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var isCurrentlyValid = validFrom <= today && today <= validUntil;
        var online = reader.GetBoolean(7);
        var offline = reader.GetBoolean(8);
        var hasTechnicalKey = reader.GetBoolean(9);
        var nextConsecutive = reader.IsDBNull(10) ? (long?)null : reader.GetInt64(10);
        var expirationWarningDays = reader.GetInt32(11);
        var remainingNumberWarningThreshold = reader.GetInt64(12);
        var rangeEnd = reader.IsDBNull(6) ? (long?)null : reader.GetInt64(6);
        var remaining = nextConsecutive.HasValue && rangeEnd.HasValue
            ? Math.Max(0, rangeEnd.Value - nextConsecutive.Value + 1)
            : (long?)null;
        var warnings = new List<string>();
        if (online)
        {
            var daysRemaining = validUntil.DayNumber - today.DayNumber;
            if (daysRemaining < 0)
                warnings.Add($"La resolución DIAN {reader.GetString(1)} está vencida.");
            else if (daysRemaining <= expirationWarningDays)
                warnings.Add(daysRemaining == 0
                    ? $"La resolución DIAN {reader.GetString(1)} vence hoy."
                    : $"La resolución DIAN {reader.GetString(1)} vence en {daysRemaining} días.");
            if (remaining.HasValue && remaining.Value <= remainingNumberWarningThreshold)
                warnings.Add(remaining.Value == 0
                    ? $"La resolución DIAN {reader.GetString(1)} agotó su numeración."
                    : $"A la resolución DIAN {reader.GetString(1)} le quedan {remaining.Value} números disponibles.");
        }
        return new FiscalResolutionConfiguration(
            businessId, reader.GetGuid(0), reader.GetString(1), validFrom, validUntil,
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt64(5),
            rangeEnd,
            true,
            online && hasTechnicalKey && isCurrentlyValid,
            offline && hasTechnicalKey && isCurrentlyValid,
            nextConsecutive,
            remaining,
            expirationWarningDays,
            remainingNumberWarningThreshold,
            warnings);
    }
}
