using System.Data;
using System.Data.Common;
using Auraly.BuildingBlocks.Domain.Time;

namespace Auraly.BuildingBlocks.Infrastructure.Persistence;

public static class SqlBusinessLocalDates
{
    public static void BindDateRange(DbCommand command, DateOnly? from, DateOnly? to, TimeZoneInfo zone,
        int dateRangeVersion = 2)
    {
        if (dateRangeVersion is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(dateRangeVersion));
        var rangeZone = dateRangeVersion == 1 ? TimeZoneInfo.Utc : zone;
        Add(command, "@From", DbType.DateTimeOffset, from is { } first ? StartOfDay(first, rangeZone) : DBNull.Value);
        Add(command, "@To", DbType.DateTimeOffset, to is { } last ? StartOfDay(last.AddDays(1), rangeZone) : DBNull.Value);
    }

    // These APIs published an offset-calendar contract before adding business days.
    // The selected predicate remains sargable for the business-calendar version.
    public static string SourceCalendarPredicate(string column, int version) => version switch
    {
        1 => $"CAST({column} AS date) BETWEEN @From AND @To",
        2 => $"({column} >= @From AND {column} < @To)",
        _ => throw new ArgumentOutOfRangeException(nameof(version))
    };

    public static async Task<bool> BindSourceDateRangeAsync(DbCommand command, Guid tenantId, Guid businessId,
        DateOnly? from, DateOnly? to, int version, CancellationToken cancellationToken)
    {
        if (version is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(version));
        if (from.HasValue != to.HasValue) throw new ArgumentException("El rango requiere ambas fechas.");
        if (version == 1 || !from.HasValue)
        {
            var type = version == 1 ? DbType.DateTime : DbType.DateTimeOffset;
            Add(command, "@From", type, (object?)from?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
            Add(command, "@To", type, (object?)to?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
            return true;
        }
        var connection = command.Connection ?? throw new InvalidOperationException("Falta la conexión de consulta.");
        var zone = await ReadTimeZoneAsync(connection, tenantId, businessId, cancellationToken, command.Transaction);
        if (zone is null) return false;
        BindDateRange(command, from, to, zone, version);
        return true;
    }

    public static async Task<IReadOnlyDictionary<Guid, TimeZoneInfo>> ReadTenantTimeZonesAsync(
        DbConnection connection, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT BusinessId,TimeZone FROM dbo.Businesses WHERE TenantId=@TenantId;";
        Add(command, "@TenantId", DbType.Guid, tenantId);
        var zones = new Dictionary<Guid, TimeZoneInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            zones.Add(reader.GetGuid(0), TimeZoneInfo.FindSystemTimeZoneById(reader.GetString(1)));
        return zones;
    }

    public static async Task<TimeZoneInfo?> ReadTimeZoneAsync(DbConnection connection,
        Guid tenantId, Guid businessId, CancellationToken cancellationToken, DbTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT TimeZone FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId;";
        Add(command, "@BusinessId", DbType.Guid, businessId);
        Add(command, "@TenantId", DbType.Guid, tenantId);
        var zoneId = await command.ExecuteScalarAsync(cancellationToken) as string;
        return zoneId is null ? null : TimeZoneInfo.FindSystemTimeZoneById(zoneId);
    }

    public static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo zone) => BusinessCalendar.StartOfDay(date, zone);

    public static async Task<DateTimeOffset> EffectivePaymentDateAsync(DbConnection connection,
        DbTransaction transaction, Guid tenantId, Guid businessId, DateOnly day, CancellationToken cancellationToken)
    {
        var zone = await ReadTimeZoneAsync(connection, tenantId, businessId, cancellationToken, transaction)
            ?? throw new InvalidOperationException("Falta la sede del pago.");
        // The source's offset-calendar date selects activation, mappings and
        // period. Normalizing it here can move a selected day into another year.
        // Operational payment headers/responses normalize the same instant to UTC.
        return BusinessCalendar.AtNoon(day, zone);
    }

    private static void Add(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
