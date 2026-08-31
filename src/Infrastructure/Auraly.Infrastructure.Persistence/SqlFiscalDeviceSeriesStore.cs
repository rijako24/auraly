using System.Data;
using Auraly.Application.Fiscal;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Auraly.Fiscal.Core;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlFiscalDeviceSeriesStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    IFiscalTechnicalKeyProvider technicalKeys,
    TimeProvider timeProvider) : IFiscalDeviceSeriesStore
{
    public async Task<FiscalDeviceSeriesWorkspace> ListAsync(
        Guid tenantId, Guid businessId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = StoredProcedure("fiscal.FiscalDeviceSeriesWorkspaceGet", connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var resolutions = new List<FiscalAssignableResolution>();
        while (await reader.ReadAsync(cancellationToken))
            resolutions.Add(new FiscalAssignableResolution(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt64(3), reader.GetInt64(4),
                DateOnly.FromDateTime(reader.GetDateTime(5)),
                DateOnly.FromDateTime(reader.GetDateTime(6))));

        await reader.NextResultAsync(cancellationToken);
        var devices = new List<FiscalDeviceSeriesAssignment>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var seriesId = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6);
            devices.Add(new FiscalDeviceSeriesAssignment(
                reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2),
                reader.IsDBNull(3) ? null : reader.GetDateTimeOffset(3),
                reader.GetGuid(4), reader.GetString(5), seriesId,
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetInt64(10),
                reader.IsDBNull(11) ? null : reader.GetInt64(11),
                seriesId.HasValue));
        }

        await reader.NextResultAsync(cancellationToken);
        FiscalOnlineSeriesAssignment? online = null;
        if (await reader.ReadAsync(cancellationToken))
            online = new FiscalOnlineSeriesAssignment(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetString(3), reader.GetInt64(4), reader.GetInt64(5),
                reader.GetInt64(6), reader.GetInt64(7),
                DateOnly.FromDateTime(reader.GetDateTime(8)),
                DateOnly.FromDateTime(reader.GetDateTime(9)));

        await reader.NextResultAsync(cancellationToken);
        var expirationWarningDays = 3;
        long remainingNumberWarningThreshold = 100;
        if (await reader.ReadAsync(cancellationToken))
        {
            expirationWarningDays = reader.GetInt32(0);
            remainingNumberWarningThreshold = reader.GetInt64(1);
        }

        return new FiscalDeviceSeriesWorkspace(
            businessId,
            resolutions.Sum(item => item.RangeEnd - item.RangeStart + 1),
            resolutions,
            devices,
            online,
            expirationWarningDays,
            remainingNumberWarningThreshold);
    }

    public async Task<FiscalDeviceSeriesWorkspace> AssignAsync(
        Guid tenantId, Guid businessId, Guid userId,
        AssignFiscalDeviceSeriesRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await using var command = StoredProcedure(
            "fiscal.FiscalDeviceSeriesAssign", connection, transaction);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        Add(command, "@UserId", userId);
        Add(command, "@DeviceId", request.DeviceId);
        Add(command, "@DianNumberingRangeId", request.DianNumberingRangeId);
        Add(command, "@FiscalAuthorizationId", ids.NewId());
        Add(command, "@FiscalTechnicalKeySecretId", ids.NewId());
        Add(command, "@SeriesId", ids.NewId());
        Add(command, "@NotificationId", ids.NewId());
        Add(command, "@QrValidationUrl", DianFiscalDefaults.ProductionQrValidationUrl);
        Add(command, "@TechnicalKeyVersion", DianFiscalDefaults.NumberingRangeTechnicalKeyVersion);
        Add(command, "@Now", timeProvider.GetUtcNow());
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 51023 or 51027)
        {
            throw new FiscalConfigurationValidationException(exception.Message);
        }
        return await ListAsync(tenantId, businessId, cancellationToken);
    }

    public async Task<FiscalDeviceSeriesWorkspace> SaveAlertSettingsAsync(
        Guid tenantId, Guid businessId, Guid userId,
        SaveFiscalResolutionAlertSettingsRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = StoredProcedure(
            "fiscal.FiscalResolutionAlertSettingsSave", connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        Add(command, "@UserId", userId);
        Add(command, "@ExpirationWarningDays", request.ExpirationWarningDays);
        Add(command, "@RemainingNumberWarningThreshold",
            request.RemainingNumberWarningThreshold);
        Add(command, "@Now", timeProvider.GetUtcNow());
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 51021 or 51027)
        {
            throw new FiscalConfigurationValidationException(exception.Message);
        }
        return await ListAsync(tenantId, businessId, cancellationToken);
    }

    public async Task<IReadOnlyList<PosFiscalSeriesProvisioning>> GetProvisioningAsync(
        Guid tenantId, Guid businessId, Guid deviceId, Guid? currentSeriesId,
        long? nextConsecutive, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = StoredProcedure(
            "fiscal.FiscalDeviceSeriesProvisioningGet", connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        Add(command, "@DeviceId", deviceId);
        command.Parameters.AddWithValue("@CurrentSeriesId", (object?)currentSeriesId ?? DBNull.Value);
        command.Parameters.AddWithValue("@NextConsecutive", (object?)nextConsecutive ?? DBNull.Value);

        var rows = new List<ProvisioningRow>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new ProvisioningRow(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                    reader.GetString(3), reader.GetInt64(4), reader.GetInt64(5),
                    DateOnly.FromDateTime(reader.GetDateTime(6)),
                    DateOnly.FromDateTime(reader.GetDateTime(7)), reader.GetByte(8),
                    reader.GetString(9), reader.GetString(10), reader.GetString(11),
                    reader.GetInt64(12), reader.GetInt64(13),
                    reader.GetInt32(14), reader.GetInt64(15), reader.GetBoolean(16)));
        }
        catch (SqlException exception) when (exception.Number is 51023 or 51027)
        {
            throw new FiscalConfigurationValidationException(exception.Message);
        }

        var result = new List<PosFiscalSeriesProvisioning>(rows.Count);
        foreach (var row in rows)
        {
            var material = await technicalKeys.ResolveAsync(
                new FiscalKeyReference(tenantId, businessId, row.AuthorizationNumber,
                    row.TechnicalKeyVersion, (FiscalEnvironment)row.Environment),
                cancellationToken)
                ?? throw new FiscalConfigurationValidationException(
                    "La clave técnica de la resolución asignada no está disponible.");
            result.Add(new PosFiscalSeriesProvisioning(
                row.SeriesId, row.FiscalAuthorizationId, row.Prefix,
                row.AuthorizationNumber, row.RangeStart, row.RangeEnd,
                row.ValidUntil, row.Environment, material.SupplierTaxId,
                new string(material.TechnicalKey.Reveal()), row.TechnicalKeyVersion,
                material.QrValidationUrl, row.ValidFrom,
                row.AuthorizationRangeStart, row.AuthorizationRangeEnd,
                row.ExpirationWarningDays,
                row.RemainingNumberWarningThreshold,
                row.ProductionActive));
        }
        return result;
    }

    private sealed record ProvisioningRow(
        Guid SeriesId, Guid FiscalAuthorizationId, string Prefix,
        string AuthorizationNumber, long RangeStart, long RangeEnd,
        DateOnly ValidFrom, DateOnly ValidUntil, byte Environment,
        string SupplierTaxId, string TechnicalKeyVersion, string QrValidationUrl,
        long AuthorizationRangeStart, long AuthorizationRangeEnd,
        int ExpirationWarningDays, long RemainingNumberWarningThreshold,
        bool ProductionActive);

    private static SqlCommand StoredProcedure(
        string name, SqlConnection connection, SqlTransaction? transaction = null) =>
        new(name, connection, transaction) { CommandType = CommandType.StoredProcedure };

    private static void Add(SqlCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);
}
