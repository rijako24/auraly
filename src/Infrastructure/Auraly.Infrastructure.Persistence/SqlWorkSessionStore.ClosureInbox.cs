using System.Data;
using System.Text.Json;
using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Auraly.Contracts.WorkSessions;
using Auraly.Contracts.Dispatching;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlWorkSessionStore
{
    public async Task<CashClosureInboxPage> ListClosureInboxAsync(
        WorkSessionIdentity identity, DateOnly from, DateOnly to, string? status,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var zones = await SqlBusinessLocalDates.ReadTenantTimeZonesAsync(connection, identity.TenantId, cancellationToken);
        var ranges = JsonSerializer.Serialize(zones.Select(item => new {
            BusinessId = item.Key, StartsAt = SqlBusinessLocalDates.StartOfDay(from, item.Value),
            Until = SqlBusinessLocalDates.StartOfDay(to.AddDays(1), item.Value) }));
        // Filter/count/page the union of headers before reading payment snapshots or dispatch details.
        await using var command = new SqlCommand("""
            SELECT headers.* INTO #ClosureHeaders FROM (
              SELECT closure.WorkSessionClosureId ClosureId,N'Cash' Kind,session.BusinessId,closure.ClosedAt,
                closure.ReconciliationStatus ReviewStatus
              FROM dbo.WorkSessionClosures closure
              INNER JOIN dbo.WorkSessions session ON session.WorkSessionId=closure.WorkSessionId
              INNER JOIN dbo.Businesses business ON business.BusinessId=session.BusinessId
              INNER JOIN OPENJSON(@Ranges) WITH(BusinessId uniqueidentifier,StartsAt datetimeoffset(7),Until datetimeoffset(7)) calendar
                ON calendar.BusinessId=business.BusinessId
              WHERE @Cash=1 AND session.Kind=N'Normal' AND business.TenantId=@TenantId
                AND closure.ClosedAt>=calendar.StartsAt AND closure.ClosedAt<calendar.Until
              UNION ALL
              SELECT settlement.DispatchSettlementId,N'Dispatch',dispatch.BusinessId,settlement.TransporterClosedAt,
                settlement.ReconciliationStatus
              FROM dispatch.Closures settlement
              INNER JOIN dbo.Dispatches dispatch ON dispatch.DispatchId=settlement.DispatchId AND dispatch.BusinessId=settlement.BusinessId
              INNER JOIN OPENJSON(@Ranges) WITH(BusinessId uniqueidentifier,StartsAt datetimeoffset(7),Until datetimeoffset(7)) calendar
                ON calendar.BusinessId=dispatch.BusinessId
              WHERE @Dispatch=1 AND dispatch.TenantId=@TenantId AND dispatch.BusinessId=@BusinessId
                AND settlement.TransporterClosedAt>=calendar.StartsAt AND settlement.TransporterClosedAt<calendar.Until
            ) headers WHERE @Status IS NULL OR headers.ReviewStatus=@Status;
            SELECT COUNT(*) FROM #ClosureHeaders;
            SELECT selected.ClosureId,selected.Kind,selected.BusinessId,business.Name,selected.ClosedAt,
              selected.ReviewStatus,closure.WorkSessionId,session.WarehouseId,warehouse.Name,session.UserId,
              LTRIM(RTRIM(CONCAT(cashier.FirstName,N' ',cashier.LastName))),session.OpenedAt,
              closure.SalesCount,closure.CreditSalesCount,closure.ReturnCount,closure.TotalSales,
              closure.TotalRefunds,closure.NetAmount,closure.ExpectedCash+COALESCE((
                SELECT changed.Amount FROM accounting.ClosurePaymentCorrectionChanges changed
                WHERE changed.TenantId=business.TenantId AND changed.BusinessId=selected.BusinessId
                  AND changed.WorkSessionClosureId=closure.WorkSessionClosureId AND changed.PaymentMethodCode=N'Cash'),0),
              COALESCE((SELECT TOP(1) job.Status FROM dbo.AccountingPostingJobs job
                WHERE job.SourceDocumentId IN(closure.WorkSessionClosureId,
                  (SELECT reconciliation.ReconciliationId FROM dbo.WorkSessionClosureReconciliations reconciliation
                   WHERE reconciliation.WorkSessionClosureId=closure.WorkSessionClosureId))
                ORDER BY job.CreatedAt DESC),N'AccountingDisabled'),
              dispatch.DispatchId,dispatch.DispatchNumber,dispatch.DriverName,settlement.Status,
              settlement.ExpectedCash,settlement.DeclaredCash,settlement.CashReceived,settlement.CashDifference,dispatchSession.OpenedAt
            FROM (SELECT * FROM #ClosureHeaders ORDER BY ClosedAt DESC,Kind,ClosureId DESC
              OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY) selected
            INNER JOIN dbo.Businesses business ON business.BusinessId=selected.BusinessId
            LEFT JOIN dbo.WorkSessionClosures closure ON selected.Kind=N'Cash' AND closure.WorkSessionClosureId=selected.ClosureId
            LEFT JOIN dbo.WorkSessions session ON session.WorkSessionId=closure.WorkSessionId
            LEFT JOIN dbo.Warehouses warehouse ON warehouse.WarehouseId=session.WarehouseId
            LEFT JOIN dbo.AppUsers cashier ON cashier.UserId=session.UserId
            LEFT JOIN dispatch.Closures settlement ON selected.Kind=N'Dispatch' AND settlement.DispatchSettlementId=selected.ClosureId
            LEFT JOIN dbo.WorkSessions dispatchSession ON dispatchSession.WorkSessionId=settlement.WorkSessionId
            LEFT JOIN dbo.Dispatches dispatch ON dispatch.DispatchId=settlement.DispatchId
            ORDER BY selected.ClosedAt DESC,selected.Kind,selected.ClosureId DESC;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        command.Parameters.AddWithValue("@BusinessId", (object?)identity.BusinessId ?? DBNull.Value);
        command.Parameters.Add("@Ranges", SqlDbType.NVarChar, -1).Value = ranges;
        command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("@Cash", identity.Permissions.Contains(WorkSessionPermissionCodes.ReadCashDifferences));
        command.Parameters.AddWithValue("@Dispatch", identity.Permissions.Contains(DispatchPermissionCodes.Settle));
        command.Parameters.AddWithValue("@Skip", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@Take", pageSize);
        var items = new List<CashClosureInboxItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var total = reader.GetInt32(0);
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            if (reader.GetString(1) == "Dispatch")
                items.Add(new(id, "Dispatch", null, new(reader.GetGuid(20), reader.GetString(21),
                    reader.GetGuid(2), reader.GetString(3), reader.GetString(22), reader.GetDateTimeOffset(4),
                    reader.GetString(23), reader.GetDecimal(24), reader.GetDecimal(25),
                    reader.IsDBNull(26) ? null : reader.GetDecimal(26), reader.GetDecimal(27), reader.GetDateTimeOffset(28))));
            else
                items.Add(new(id, "Cash", new(id, reader.GetGuid(6), reader.GetGuid(2), reader.GetString(3),
                    reader.IsDBNull(7) ? null : reader.GetGuid(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.GetGuid(9), reader.GetString(10), reader.GetDateTimeOffset(11), reader.GetDateTimeOffset(4),
                    reader.GetInt64(12), reader.GetInt32(13), reader.GetInt64(14), reader.GetDecimal(15),
                    reader.GetDecimal(16), reader.GetDecimal(17), reader.GetString(5), reader.GetString(19), [],
                    reader.GetDecimal(18)), null));
        }
        await reader.CloseAsync();
        var cashIds = items.Where(item => item.CashClosure is not null).Select(item => item.ClosureId).ToArray();
        var totals = await ReadClosurePaymentTotalsAsync(connection, cashIds, cancellationToken);
        for (var index = 0; index < items.Count; index++)
            if (items[index].CashClosure is { } cash)
                items[index] = items[index] with { CashClosure = cash with {
                    PaymentTotals = totals.GetValueOrDefault(cash.WorkSessionClosureId) ?? [] } };
        return new(items, page, pageSize, total);
    }
}
