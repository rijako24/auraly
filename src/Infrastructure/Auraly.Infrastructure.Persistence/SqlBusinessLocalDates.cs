using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal static class SqlBusinessLocalDates
{
    public static async Task<TimeZoneInfo?> ReadTimeZoneAsync(SqlConnection connection,
        Guid tenantId, Guid businessId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT TimeZone FROM dbo.Businesses
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        var zoneId = await command.ExecuteScalarAsync(cancellationToken) as string;
        return zoneId is null ? null : TimeZoneInfo.FindSystemTimeZoneById(zoneId);
    }

    public static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo zone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), zone));
}
