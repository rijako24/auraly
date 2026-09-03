using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.WorkSessions;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlWorkSessionStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IWorkSessionStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<WorkSessionView?> CurrentAsync(
        WorkSessionIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        return await ReadOpenAsync(
            connection, null, identity, lockRow: false, cancellationToken);
    }

    public async Task<WorkSessionView> OpenOrResumeAsync(
        WorkSessionIdentity identity,
        OpenWorkSessionRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var scope = await ValidateScopeAsync(
                connection, transaction, identity, request, cancellationToken);
            var current = await ReadOpenAsync(
                connection, transaction, identity, lockRow: true, cancellationToken);
            if (current is not null)
            {
                if (current.BusinessId != request.BusinessId ||
                    current.WarehouseId != request.WarehouseId)
                    throw new WorkSessionConflictException(
                        "El usuario ya tiene una sesión de trabajo abierta en otra sede o bodega.");
                if (current.DeviceId is not null && request.DeviceId is not null &&
                    current.DeviceId != request.DeviceId)
                    throw new WorkSessionConflictException(
                        "La sesión de trabajo ya está vinculada a otro equipo enrolado.");

                var now = timeProvider.GetUtcNow();
                await using var touch = new SqlCommand("""
                    UPDATE dbo.WorkSessions
                    SET LastActivityAt=@Now,
                        DeviceId=COALESCE(DeviceId,@DeviceId)
                    WHERE WorkSessionId=@WorkSessionId
                      AND TenantId=@TenantId AND UserId=@UserId
                      AND Status=N'Open';
                    """, connection, transaction);
                touch.Parameters.AddWithValue("@Now", now);
                touch.Parameters.AddWithValue("@WorkSessionId", current.WorkSessionId);
                touch.Parameters.AddWithValue("@TenantId", identity.TenantId);
                touch.Parameters.AddWithValue("@UserId", identity.UserId);
                touch.Parameters.AddWithValue(
                    "@DeviceId", (object?)request.DeviceId ?? DBNull.Value);
                if (await touch.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new DBConcurrencyException("The work session changed concurrently.");
                await transaction.CommitAsync(cancellationToken);
                return current with
                {
                    LastActivityAt = now,
                    DeviceId = current.DeviceId ?? request.DeviceId
                };
            }

            var workSessionId = ids.NewId();
            var openedAt = timeProvider.GetUtcNow();
            await using var insert = new SqlCommand("""
                INSERT dbo.WorkSessions
                  (WorkSessionId,TenantId,BusinessId,WarehouseId,UserId,DeviceId,
                   OpenedAt,LastActivityAt,Status)
                VALUES
                  (@WorkSessionId,@TenantId,@BusinessId,@WarehouseId,@UserId,@DeviceId,
                   @OpenedAt,@OpenedAt,N'Open');
                """, connection, transaction);
            insert.Parameters.AddWithValue("@WorkSessionId", workSessionId);
            insert.Parameters.AddWithValue("@TenantId", identity.TenantId);
            insert.Parameters.AddWithValue("@BusinessId", request.BusinessId);
            insert.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
            insert.Parameters.AddWithValue("@UserId", identity.UserId);
            insert.Parameters.AddWithValue(
                "@DeviceId", (object?)request.DeviceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("@OpenedAt", openedAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            if (request.OpeningCash > 0)
            {
                await using var opening = new SqlCommand("""
                    INSERT dbo.WorkSessionMovements
                      (WorkSessionMovementId,WorkSessionId,DocumentId,PaymentNumber,
                       BusinessDate,MovementType,PaymentMethodCode,Amount,Reference,
                       SourceKey,OccurredAt,RecordedByUserId)
                    VALUES
                      (@MovementId,@WorkSessionId,NULL,NULL,@BusinessDate,N'OpeningFloat',
                       N'Cash',@Amount,N'Fondo inicial',@SourceKey,@OpenedAt,@UserId);
                    """, connection, transaction);
                opening.Parameters.AddWithValue("@MovementId", ids.NewId());
                opening.Parameters.AddWithValue("@WorkSessionId", workSessionId);
                opening.Parameters.AddWithValue("@BusinessDate", openedAt.Date);
                AddMoney(opening, "@Amount", request.OpeningCash);
                opening.Parameters.AddWithValue("@SourceKey", $"opening-float:{workSessionId:N}");
                opening.Parameters.AddWithValue("@OpenedAt", openedAt);
                opening.Parameters.AddWithValue("@UserId", identity.UserId);
                await opening.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new WorkSessionView(
                workSessionId,
                request.BusinessId,
                scope.BusinessName,
                request.WarehouseId,
                scope.WarehouseName,
                identity.UserId,
                scope.UserName,
                request.DeviceId,
                openedAt,
                openedAt,
                "Open",
                identity.TenantId);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            // A second tab or POS bootstrap can win the unique open-session insert
            // after this transaction checked for the current user. Recover that
            // canonical winner instead of surfacing a false conflict. A session
            // owned by another user/device remains a real conflict because it is
            // not visible through CurrentAsync(identity).
            var winner = await CurrentAsync(identity, cancellationToken);
            if (winner is not null &&
                winner.BusinessId == request.BusinessId &&
                winner.WarehouseId == request.WarehouseId &&
                (request.DeviceId is null || winner.DeviceId == request.DeviceId))
                return winner;
            throw new WorkSessionConflictException(
                "El usuario o el equipo ya tiene una sesión de trabajo abierta en otra sede o bodega.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<WorkSessionClosureView> CloseAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        string idempotencyKey,
        CloseWorkSessionRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var session = await ReadByIdAsync(
                connection, transaction, identity, workSessionId, cancellationToken)
                ?? throw new WorkSessionNotFoundException(
                    "The work session does not exist in the authenticated tenant.");
            var replay = await ReadClosureAsync(
                connection, transaction, identity, workSessionId, cancellationToken);
            if (replay is not null)
            {
                if (!string.Equals(replay.Value.IdempotencyKey, idempotencyKey,
                        StringComparison.Ordinal))
                    throw new WorkSessionConflictException(
                        "The work session was already closed with another idempotency key.");
                await transaction.CommitAsync(cancellationToken);
                return replay.Value.Closure;
            }
            if (!string.Equals(session.Status, "Open", StringComparison.Ordinal))
                throw new WorkSessionConflictException(
                    "The work session is not open and has no closure receipt.");

            var expectedTotals = await ReadTotalsAsync(
                connection, transaction, identity, workSessionId, cancellationToken);
            var metrics = await ReadSalesMetricsAsync(
                connection, transaction, identity, workSessionId, cancellationToken);
            var totals = ReconcileTotals(expectedTotals, request);
            var totalSales = totals.Sum(value => value.SalesAmount);
            var totalRefunds = totals.Sum(value => value.RefundAmount);
            var totalOther = totals.Sum(value => value.OtherAmount);
            var netAmount = totals.Sum(value => value.NetAmount);
            var expectedCash = totals
                .Where(value => string.Equals(
                    value.PaymentMethodCode, "Cash", StringComparison.OrdinalIgnoreCase))
                .Sum(value => value.NetAmount);
            var countedCash = totals.FirstOrDefault(value => string.Equals(
                value.PaymentMethodCode, "Cash", StringComparison.OrdinalIgnoreCase))?.CountedAmount
                ?? request.CountedCash;
            var difference = countedCash is null
                ? (decimal?)null
                : countedCash.Value - expectedCash;
            var closedAt = timeProvider.GetUtcNow();
            var closure = new WorkSessionClosureView(
                ids.NewId(),
                session.WorkSessionId,
                session.BusinessId,
                session.BusinessName,
                session.WarehouseId,
                session.WarehouseName,
                session.UserId,
                session.UserName,
                session.DeviceId,
                session.OpenedAt,
                closedAt,
                totalSales,
                totalRefunds,
                totalOther,
                netAmount,
                expectedCash,
                countedCash,
                difference,
                request.Note,
                totals,
                metrics.SalesCount,
                metrics.CreditSalesCount,
                metrics.CreditSalesAmount,
                metrics.ReturnCount);
            var snapshot = JsonSerializer.Serialize(closure, Json);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(snapshot));

            await InsertClosureAsync(
                connection, transaction,
                request.ClosedByUserId ?? identity.UserId,
                closure, idempotencyKey,
                snapshot, hash, cancellationToken);
            await InsertTotalsAsync(
                connection, transaction, closure.WorkSessionClosureId,
                totals, cancellationToken);
            var differenceLines = await LoadClosureDifferenceLinesAsync(
                connection, transaction, totals, cancellationToken);
            if (differenceLines.Count > 0)
            {
                var accountingPayload = JsonSerializer.Serialize(
                    new WorkSessionClosureDifferencePayload(
                        closure.WorkSessionClosureId,
                        closure.WorkSessionId,
                        identity.TenantId,
                        closure.BusinessId,
                        closure.WarehouseId,
                        closure.UserId,
                        closure.UserName,
                        differenceLines,
                        closure.ClosedAt),
                    Json);
                await InsertCashDifferenceAccountingJobAsync(
                    connection,
                    transaction,
                    identity.TenantId,
                    closure,
                    accountingPayload,
                    cancellationToken);
            }
            await using var close = new SqlCommand("""
                UPDATE dbo.WorkSessions
                SET Status=N'Closed',ClosedAt=@ClosedAt,LastActivityAt=@ClosedAt
                WHERE WorkSessionId=@WorkSessionId
                  AND TenantId=@TenantId AND UserId=@UserId
                  AND Status=N'Open';
                """, connection, transaction);
            close.Parameters.AddWithValue("@ClosedAt", closedAt);
            close.Parameters.AddWithValue("@WorkSessionId", workSessionId);
            close.Parameters.AddWithValue("@TenantId", identity.TenantId);
            close.Parameters.AddWithValue("@UserId", identity.UserId);
            if (await close.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new DBConcurrencyException("The work session changed concurrently.");
            await transaction.CommitAsync(cancellationToken);
            return closure;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new WorkSessionConflictException(
                "The work session closure was already recorded.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<WorkSessionClosurePreviewView> PreviewClosureAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var session = await ReadByIdAsync(
            connection, transaction, identity, workSessionId, cancellationToken)
            ?? throw new WorkSessionNotFoundException(
                "The work session does not exist in the authenticated tenant.");
        if (!string.Equals(session.Status, "Open", StringComparison.Ordinal))
            throw new WorkSessionConflictException("The work session is not open.");
        var totals = await ReadTotalsAsync(
            connection, transaction, identity, workSessionId, cancellationToken);
        var metrics = await ReadSalesMetricsAsync(
            connection, transaction, identity, workSessionId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var totalSales = totals.Sum(value => value.SalesAmount);
        var totalRefunds = totals.Sum(value => value.RefundAmount);
        var totalOther = totals.Sum(value => value.OtherAmount);
        return new WorkSessionClosurePreviewView(
            session.WorkSessionId,
            session.BusinessId,
            session.BusinessName,
            session.WarehouseId,
            session.WarehouseName,
            session.UserId,
            session.UserName,
            session.OpenedAt,
            session.LastActivityAt,
            totalSales,
            totalRefunds,
            totalOther,
            totals.Sum(value => value.NetAmount),
            totals.Where(value => string.Equals(
                    value.PaymentMethodCode, "Cash", StringComparison.OrdinalIgnoreCase))
                .Sum(value => value.NetAmount),
            totals,
            metrics.SalesCount,
            metrics.CreditSalesCount,
            metrics.CreditSalesAmount,
            metrics.ReturnCount);
    }

    public async Task<WorkSessionClosureView?> GetClosureAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var result = await ReadClosureAsync(
            connection, null, identity, workSessionId, cancellationToken);
        return result?.Closure;
    }

    private static async Task<ScopeNames> ValidateScopeAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        WorkSessionIdentity identity,
        OpenWorkSessionRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT b.Name,w.Name,CONCAT(u.FirstName,N' ',u.LastName),
                   CASE WHEN @DeviceId IS NULL OR EXISTS
                   (
                       SELECT 1 FROM dbo.EnrolledDevices d
                       WHERE d.DeviceId=@DeviceId AND d.TenantId=b.TenantId
                         AND d.IsActive=1
                   ) THEN 1 ELSE 0 END
            FROM dbo.AppUsers u
            INNER JOIN dbo.Businesses b
              ON b.BusinessId=@BusinessId AND b.TenantId=@TenantId AND b.IsActive=1
            INNER JOIN dbo.Warehouses w
              ON w.BusinessId=b.BusinessId AND w.WarehouseId=@WarehouseId AND w.IsActive=1
            WHERE u.UserId=@UserId AND u.IsActive=1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@UserId", identity.UserId);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
        command.Parameters.AddWithValue(
            "@DeviceId", (object?)request.DeviceId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new WorkSessionForbiddenException(
                "The business, warehouse or user is outside the authenticated tenant.");
        if (reader.GetInt32(3) != 1)
            throw new WorkSessionForbiddenException(
                "The enrolled device is not active in the authenticated tenant.");
        return new ScopeNames(reader.GetString(0), reader.GetString(1), reader.GetString(2).Trim());
    }

    private static async Task<WorkSessionView?> ReadOpenAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        WorkSessionIdentity identity,
        bool lockRow,
        CancellationToken cancellationToken)
    {
        var hint = lockRow ? " WITH (UPDLOCK,HOLDLOCK)" : string.Empty;
        var sql = $"""
            SELECT s.WorkSessionId,s.BusinessId,b.Name,s.WarehouseId,w.Name,
                   s.UserId,CONCAT(u.FirstName,N' ',u.LastName),s.DeviceId,
                   s.OpenedAt,s.LastActivityAt,s.Status,b.TenantId
            FROM dbo.WorkSessions s{hint}
            INNER JOIN dbo.Businesses b ON b.BusinessId=s.BusinessId
            INNER JOIN dbo.Warehouses w ON w.WarehouseId=s.WarehouseId
            INNER JOIN dbo.AppUsers u ON u.UserId=s.UserId
            WHERE s.TenantId=@TenantId AND s.UserId=@UserId AND s.Status=N'Open';
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@UserId", identity.UserId);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapSession(reader) : null;
    }

    private static async Task<WorkSessionView?> ReadByIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT s.WorkSessionId,s.BusinessId,b.Name,s.WarehouseId,w.Name,
                   s.UserId,CONCAT(u.FirstName,N' ',u.LastName),s.DeviceId,
                   s.OpenedAt,s.LastActivityAt,s.Status,b.TenantId
            FROM dbo.WorkSessions s WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Businesses b ON b.BusinessId=s.BusinessId
            INNER JOIN dbo.Warehouses w ON w.WarehouseId=s.WarehouseId
            INNER JOIN dbo.AppUsers u ON u.UserId=s.UserId
            WHERE s.WorkSessionId=@WorkSessionId
              AND s.TenantId=@TenantId AND s.UserId=@UserId
              AND b.TenantId=s.TenantId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        command.Parameters.AddWithValue("@UserId", identity.UserId);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapSession(reader) : null;
    }

    private static WorkSessionView MapSession(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
        reader.GetGuid(3), reader.GetString(4), reader.GetGuid(5),
        reader.GetString(6).Trim(), reader.IsDBNull(7) ? null : reader.GetGuid(7),
        reader.GetFieldValue<DateTimeOffset>(8),
        reader.GetFieldValue<DateTimeOffset>(9), reader.GetString(10),
        reader.GetGuid(11));

    private static async Task<IReadOnlyList<WorkSessionPaymentTotal>> ReadTotalsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken)
    {
        var values = new List<WorkSessionPaymentTotal>();
        await using var command = new SqlCommand("""
            WITH SessionScope AS
            (
                SELECT WorkSessionId,BusinessId
                FROM dbo.WorkSessions
                WHERE WorkSessionId=@WorkSessionId
                  AND TenantId=@TenantId
                  AND UserId=@UserId
            ),
            PaymentMovements AS
            (
                SELECT COALESCE(mapping.ClosureMethodCode,payment.MethodCode) AS PaymentMethodCode,
                       N'SalePayment' AS MovementType,payment.Amount
                FROM dbo.SalesDocuments d
                INNER JOIN SessionScope session
                  ON session.WorkSessionId=d.WorkSessionId
                 AND session.BusinessId=d.BusinessId
                INNER JOIN dbo.DocumentProcessingPayloads payload
                  ON payload.DocumentId=d.DocumentId AND payload.DocumentType=d.DocumentType
                CROSS APPLY OPENJSON(payload.PayloadJson,N'$.payments')
                  WITH
                  (
                    MethodCode NVARCHAR(32) N'$.methodCode',
                    Amount DECIMAL(19,4) N'$.amount'
                  ) payment
                LEFT JOIN worksessions.CashClosurePaymentMethodMappings mapping
                  ON mapping.PaymentMethodCode=payment.MethodCode
                WHERE d.WorkSessionId=@WorkSessionId
                UNION ALL
                SELECT COALESCE(mapping.ClosureMethodCode,p.MethodCode),
                       N'SalePayment',p.Amount
                FROM dbo.SalesPayments p
                INNER JOIN dbo.SalesDocuments d ON d.DocumentId=p.DocumentId
                INNER JOIN SessionScope session
                  ON session.WorkSessionId=d.WorkSessionId
                 AND session.BusinessId=d.BusinessId
                LEFT JOIN worksessions.CashClosurePaymentMethodMappings mapping
                  ON mapping.PaymentMethodCode=p.MethodCode
                WHERE d.WorkSessionId=@WorkSessionId
                  AND NOT EXISTS
                  (
                    SELECT 1 FROM dbo.DocumentProcessingPayloads payload
                    WHERE payload.DocumentId=d.DocumentId
                      AND payload.DocumentType=d.DocumentType
                  )
                UNION ALL
                SELECT N'Credit',N'SalePayment',CreditAmount
                FROM dbo.SalesDocuments d
                INNER JOIN SessionScope session
                  ON session.WorkSessionId=d.WorkSessionId
                 AND session.BusinessId=d.BusinessId
                WHERE d.CreditAmount>0
                UNION ALL
                SELECT COALESCE(mapping.ClosureMethodCode,r.RefundMethodCode),
                       N'Refund',-r.TotalAmount
                FROM dbo.SalesReturns r
                INNER JOIN SessionScope session
                  ON session.WorkSessionId=r.WorkSessionId
                 AND session.BusinessId=r.BusinessId
                LEFT JOIN worksessions.CashClosurePaymentMethodMappings mapping
                  ON mapping.PaymentMethodCode=r.RefundMethodCode
                WHERE r.EconomicResolution=N'Refund'
                  AND r.Status IN(N'Accepted',N'Processed')
                UNION ALL
                SELECT N'Cash',
                       CASE WHEN document.Direction=N'In' THEN N'CashIn' ELSE N'CashOut' END,
                       CASE WHEN document.Direction=N'In' THEN document.Amount ELSE -document.Amount END
                FROM dbo.CashMovementDocuments document
                INNER JOIN SessionScope session
                  ON session.WorkSessionId=document.WorkSessionId
                 AND session.BusinessId=document.BusinessId
                WHERE document.Status IN(N'Accepted',N'Processed')
                UNION ALL
                SELECT COALESCE(mapping.ClosureMethodCode,movement.PaymentMethodCode),movement.MovementType,movement.Amount
                FROM dbo.WorkSessionMovements movement
                INNER JOIN SessionScope session
                  ON session.WorkSessionId=movement.WorkSessionId
                LEFT JOIN worksessions.CashClosurePaymentMethodMappings mapping
                  ON mapping.PaymentMethodCode=movement.PaymentMethodCode
                WHERE movement.MovementType NOT IN(N'SalePayment',N'Refund')
                  AND NOT EXISTS
                  (
                    SELECT 1 FROM dbo.CashMovementDocuments document
                    WHERE document.DocumentId=movement.DocumentId
                      AND document.WorkSessionId=movement.WorkSessionId
                  )
            ),
            Totals AS
            (
                SELECT PaymentMethodCode,
                  COALESCE(SUM(CASE WHEN MovementType=N'SalePayment' THEN Amount ELSE 0 END),0) SalesAmount,
                  COALESCE(SUM(CASE WHEN MovementType=N'Refund' THEN ABS(Amount) ELSE 0 END),0) RefundAmount,
                  COALESCE(SUM(CASE WHEN MovementType NOT IN (N'SalePayment',N'Refund') THEN Amount ELSE 0 END),0) OtherAmount,
                  COALESCE(SUM(Amount),0) NetAmount
                FROM PaymentMovements
                GROUP BY PaymentMethodCode
            ),
            AllTotals AS
            (
                SELECT options.Code AS PaymentMethodCode,
                       COALESCE(totals.SalesAmount,0) SalesAmount,
                       COALESCE(totals.RefundAmount,0) RefundAmount,
                       COALESCE(totals.OtherAmount,0) OtherAmount,
                       COALESCE(totals.NetAmount,0) NetAmount
                FROM reference.Options options
                LEFT JOIN Totals totals ON totals.PaymentMethodCode=options.Code
                WHERE options.CatalogCode=N'cash-closure-method' AND options.IsActive=1
                UNION ALL
                SELECT totals.PaymentMethodCode,totals.SalesAmount,totals.RefundAmount,
                       totals.OtherAmount,totals.NetAmount
                FROM Totals totals
                WHERE NOT EXISTS
                (
                    SELECT 1 FROM reference.Options options
                    WHERE options.CatalogCode=N'cash-closure-method' AND options.IsActive=1
                      AND options.Code=totals.PaymentMethodCode
                )
            )
            SELECT total.PaymentMethodCode,total.SalesAmount,total.RefundAmount,total.OtherAmount,total.NetAmount,
                   CAST(CASE WHEN closureOption.OptionId IS NOT NULL THEN 1 ELSE 0 END AS bit)
            FROM AllTotals total
            LEFT JOIN reference.Options closureOption ON closureOption.CatalogCode=N'cash-closure-method'
              AND closureOption.Code=total.PaymentMethodCode AND closureOption.IsActive=1
            ORDER BY COALESCE(closureOption.SortOrder,1000),total.PaymentMethodCode;
            """, connection, transaction);
        command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        command.Parameters.AddWithValue("@UserId", identity.UserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new WorkSessionPaymentTotal(
                reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2),
                reader.GetDecimal(3), reader.GetDecimal(4), RequiresCount: reader.GetBoolean(5)));
        return values;
    }

    private static async Task<SalesMetrics> ReadSalesMetricsAsync(
        SqlConnection connection, SqlTransaction transaction, WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT COUNT_BIG(d.DocumentId),
                   COALESCE(SUM(CASE WHEN d.CreditAmount>0 THEN 1 ELSE 0 END),0),
                   COALESCE(SUM(d.CreditAmount),0),
                   (SELECT COUNT_BIG(*) FROM dbo.SalesReturns r
                    WHERE r.WorkSessionId=s.WorkSessionId
                      AND r.BusinessId=s.BusinessId
                      AND r.Status IN(N'Accepted',N'Processed'))
            FROM dbo.WorkSessions s
            LEFT JOIN dbo.SalesDocuments d
              ON d.WorkSessionId=s.WorkSessionId AND d.BusinessId=s.BusinessId
            WHERE s.WorkSessionId=@WorkSessionId
              AND s.TenantId=@TenantId
              AND s.UserId=@UserId
            GROUP BY s.WorkSessionId,s.BusinessId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        command.Parameters.AddWithValue("@UserId", identity.UserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new SalesMetrics(
            reader.GetInt64(0), reader.GetInt32(1), reader.GetDecimal(2), reader.GetInt64(3));
    }

    public async Task<IReadOnlyList<WorkSessionCashDifferenceView>> ListCashDifferencesAsync(
        WorkSessionIdentity identity,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT c.WorkSessionClosureId,c.WorkSessionId,s.BusinessId,b.Name,
                   s.WarehouseId,w.Name,s.UserId,
                   LTRIM(RTRIM(CONCAT(u.FirstName,N' ',u.LastName))),c.ClosedAt,
                   c.ExpectedCash,c.CountedCash,c.CashDifference,
                   CASE WHEN c.CashDifference>0 THEN N'SurplusIncome'
                        ELSE N'ShortageExpense' END,
                   COALESCE(j.Status,N'AccountingDisabled'),e.EntryId,e.EntryNumber
            FROM dbo.WorkSessionClosures c
            INNER JOIN dbo.WorkSessions s ON s.WorkSessionId=c.WorkSessionId
            INNER JOIN dbo.Businesses b ON b.BusinessId=s.BusinessId
            INNER JOIN dbo.Warehouses w ON w.WarehouseId=s.WarehouseId
            INNER JOIN dbo.AppUsers u ON u.UserId=s.UserId
            LEFT JOIN dbo.AccountingPostingJobs j
              ON j.SourceDocumentId=c.WorkSessionClosureId
             AND j.SourceDocumentType=N'WorkSessionCashDifference'
            LEFT JOIN dbo.AccountingEntries e
              ON e.SourceDocumentId=j.SourceDocumentId
             AND e.SourceDocumentType=j.SourceDocumentType
            WHERE b.TenantId=@TenantId AND c.CashDifference<>0
              AND c.ClosedAt>=@From AND c.ClosedAt<@Until
            ORDER BY c.ClosedAt DESC,c.WorkSessionClosureId DESC;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        command.Parameters.AddWithValue(
            "@From", new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        command.Parameters.AddWithValue(
            "@Until", new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var rows = new List<WorkSessionCashDifferenceView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new WorkSessionCashDifferenceView(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                reader.GetGuid(4), reader.GetString(5), reader.GetGuid(6), reader.GetString(7),
                reader.GetDateTimeOffset(8), reader.GetDecimal(9), reader.GetDecimal(10),
                reader.GetDecimal(11), reader.GetString(12), reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetGuid(14),
                reader.IsDBNull(15) ? null : reader.GetString(15)));
        return rows;
    }

    private static async Task InsertClosureAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid closedByUserId,
        WorkSessionClosureView closure,
        string idempotencyKey,
        string snapshot,
        byte[] hash,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.WorkSessionClosures
              (WorkSessionClosureId,WorkSessionId,ClosedByUserId,IdempotencyKey,
               TotalSales,TotalRefunds,TotalOther,NetAmount,ExpectedCash,
               CountedCash,CashDifference,SalesCount,CreditSalesCount,CreditSalesAmount,ReturnCount,
               Note,ReceiptSnapshotJson,ReceiptHash,ClosedAt)
            VALUES
              (@ClosureId,@SessionId,@UserId,@IdempotencyKey,
               @TotalSales,@TotalRefunds,@TotalOther,@NetAmount,@ExpectedCash,
               @CountedCash,@Difference,@SalesCount,@CreditSalesCount,@CreditSalesAmount,@ReturnCount,
               @Note,@Snapshot,@Hash,@ClosedAt);
            """, connection, transaction);
        command.Parameters.AddWithValue("@ClosureId", closure.WorkSessionClosureId);
        command.Parameters.AddWithValue("@SessionId", closure.WorkSessionId);
        command.Parameters.AddWithValue("@UserId", closedByUserId);
        command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
        AddMoney(command, "@TotalSales", closure.TotalSales);
        AddMoney(command, "@TotalRefunds", closure.TotalRefunds);
        AddMoney(command, "@TotalOther", closure.TotalOther);
        AddMoney(command, "@NetAmount", closure.NetAmount);
        AddMoney(command, "@ExpectedCash", closure.ExpectedCash);
        command.Parameters.Add(new SqlParameter("@CountedCash", SqlDbType.Decimal)
        { Precision = 19, Scale = 4, Value = (object?)closure.CountedCash ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@Difference", SqlDbType.Decimal)
        { Precision = 19, Scale = 4, Value = (object?)closure.CashDifference ?? DBNull.Value });
        command.Parameters.AddWithValue("@SalesCount", closure.SalesCount);
        command.Parameters.AddWithValue("@CreditSalesCount", closure.CreditSalesCount);
        AddMoney(command, "@CreditSalesAmount", closure.CreditSalesAmount);
        command.Parameters.AddWithValue("@ReturnCount", closure.ReturnCount);
        command.Parameters.AddWithValue("@Note", (object?)closure.Note ?? DBNull.Value);
        command.Parameters.AddWithValue("@Snapshot", snapshot);
        command.Parameters.Add("@Hash", SqlDbType.VarBinary, 32).Value = hash;
        command.Parameters.AddWithValue("@ClosedAt", closure.ClosedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertTotalsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid closureId,
        IReadOnlyList<WorkSessionPaymentTotal> totals,
        CancellationToken cancellationToken)
    {
        foreach (var total in totals)
        {
            await using var command = new SqlCommand("""
                INSERT dbo.WorkSessionClosurePaymentTotals
                  (WorkSessionClosureId,PaymentMethodCode,SalesAmount,
                   RefundAmount,OtherAmount,NetAmount,CountedAmount,Difference)
                VALUES
                  (@ClosureId,@Method,@Sales,@Refund,@Other,@Net,@Counted,@Difference);
                """, connection, transaction);
            command.Parameters.AddWithValue("@ClosureId", closureId);
            command.Parameters.AddWithValue("@Method", total.PaymentMethodCode);
            AddMoney(command, "@Sales", total.SalesAmount);
            AddMoney(command, "@Refund", total.RefundAmount);
            AddMoney(command, "@Other", total.OtherAmount);
            AddMoney(command, "@Net", total.NetAmount);
            command.Parameters.Add(new SqlParameter("@Counted", SqlDbType.Decimal)
            { Precision = 19, Scale = 4, Value = (object?)total.CountedAmount ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@Difference", SqlDbType.Decimal)
            { Precision = 19, Scale = 4, Value = (object?)total.Difference ?? DBNull.Value });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<WorkSessionClosureDifferenceLine>> LoadClosureDifferenceLinesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyList<WorkSessionPaymentTotal> totals,
        CancellationToken cancellationToken)
    {
        var result = new List<WorkSessionClosureDifferenceLine>();
        foreach (var total in totals.Where(value => value.Difference is not null and not 0))
        {
            await using var command = new SqlCommand("""
                SELECT mapping.Category
                FROM dbo.AccountingConfigurationProfiles profile
                INNER JOIN dbo.AccountingSourceCategoryMappings mapping
                  ON mapping.ProfileCode=profile.ProfileCode
                 AND mapping.SourceType=N'ClosurePaymentMethod'
                 AND mapping.SourceCode=@Method
                WHERE profile.IsDefault=1 AND profile.IsActive=1;
                """, connection, transaction);
            command.Parameters.AddWithValue("@Method", total.PaymentMethodCode);
            var category = await command.ExecuteScalarAsync(cancellationToken) as string
                ?? throw new WorkSessionValidationException(
                    $"El medio de pago '{total.PaymentMethodCode}' no tiene configuración contable para cierres.");
            result.Add(new WorkSessionClosureDifferenceLine(
                total.PaymentMethodCode, category, total.NetAmount,
                total.CountedAmount!.Value, total.Difference!.Value));
        }
        return result;
    }

    private async Task InsertCashDifferenceAccountingJobAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        WorkSessionClosureView closure,
        string payload,
        CancellationToken cancellationToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        await using var command = new SqlCommand("""
            IF EXISTS
            (
                SELECT 1 FROM dbo.AccountingTenantSettings settings
                WHERE settings.TenantId=@TenantId AND settings.Status=N'Ready'
                  AND settings.EffectiveFrom<=CONVERT(date,@OccurredAt)
            )
            AND NOT EXISTS
            (
                SELECT 1 FROM dbo.AccountingSourceDocuments WITH(UPDLOCK,HOLDLOCK)
                WHERE SourceDocumentId=@DocumentId
                  AND SourceDocumentType=N'WorkSessionCashDifference'
            )
            BEGIN
                INSERT dbo.AccountingSourceDocuments
                  (SourceDocumentId,SourceDocumentType,TenantId,BusinessId,
                   PayloadJson,PayloadHash,OccurredAt,AcceptedAt)
                VALUES
                  (@DocumentId,N'WorkSessionCashDifference',@TenantId,@BusinessId,
                   @Payload,@Hash,@OccurredAt,@OccurredAt);

                INSERT dbo.AccountingPostingJobs
                  (AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
                   SourceDocumentType,SourcePayloadHash,OccurredAt,Status,
                   AttemptCount,CreatedAt)
                VALUES
                  (@JobId,@TenantId,@BusinessId,@DocumentId,
                   N'WorkSessionCashDifference',@Hash,@OccurredAt,N'Pending',0,@OccurredAt);
            END;
            """, connection, transaction);
        command.Parameters.AddWithValue("@JobId", ids.NewId());
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", closure.BusinessId);
        command.Parameters.AddWithValue("@DocumentId", closure.WorkSessionClosureId);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = hash;
        command.Parameters.AddWithValue("@OccurredAt", closure.ClosedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyList<WorkSessionPaymentTotal> ReconcileTotals(
        IReadOnlyList<WorkSessionPaymentTotal> expected,
        CloseWorkSessionRequest request)
    {
        if (request.PaymentCounts is null)
        {
            return expected.Select(value =>
                string.Equals(value.PaymentMethodCode, "Cash", StringComparison.OrdinalIgnoreCase) &&
                request.CountedCash is decimal counted
                    ? value with
                    {
                        CountedAmount = counted,
                        Difference = counted - value.NetAmount
                    }
                    : value).ToArray();
        }

        var counts = request.PaymentCounts.ToDictionary(
            value => value.PaymentMethodCode.Trim(),
            value => value.CountedAmount,
            StringComparer.OrdinalIgnoreCase);
        var expectedCodes = expected
            .Where(value => value.RequiresCount)
            .Select(value => value.PaymentMethodCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allExpectedCodes = expected.Select(value => value.PaymentMethodCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (expectedCodes.Any(code => !counts.ContainsKey(code))
            || counts.Keys.Any(code => !allExpectedCodes.Contains(code)))
            throw new WorkSessionValidationException(
                "The count must include cash, card and transfer exactly once.");
        return expected.Select(value => value.RequiresCount
            ? value with
            {
                CountedAmount = counts[value.PaymentMethodCode],
                Difference = counts[value.PaymentMethodCode] - value.NetAmount
            }
            : value).ToArray();
    }

    private sealed record SalesMetrics(
        long SalesCount, int CreditSalesCount, decimal CreditSalesAmount, long ReturnCount);

    private static async Task<(string IdempotencyKey, WorkSessionClosureView Closure)?> ReadClosureAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT c.IdempotencyKey,c.ReceiptSnapshotJson,c.ReceiptHash
            FROM dbo.WorkSessionClosures c
            INNER JOIN dbo.WorkSessions s ON s.WorkSessionId=c.WorkSessionId
            INNER JOIN dbo.Businesses b ON b.BusinessId=s.BusinessId
            WHERE c.WorkSessionId=@WorkSessionId AND s.UserId=@UserId
              AND b.TenantId=@TenantId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        command.Parameters.AddWithValue("@UserId", identity.UserId);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var key = reader.GetString(0);
        var snapshot = reader.GetString(1);
        var storedHash = (byte[])reader[2];
        var calculatedHash = SHA256.HashData(Encoding.UTF8.GetBytes(snapshot));
        if (!CryptographicOperations.FixedTimeEquals(storedHash, calculatedHash))
            throw new InvalidDataException(
                "The work session closure snapshot failed its integrity check.");
        var closure = JsonSerializer.Deserialize<WorkSessionClosureView>(snapshot, Json)
            ?? throw new InvalidDataException("The work session closure snapshot is invalid.");
        return (key, closure);
    }

    private static void AddMoney(SqlCommand command, string name, decimal value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Decimal)
        { Precision = 19, Scale = 4, Value = value });

    private sealed record ScopeNames(
        string BusinessName,
        string WarehouseName,
        string UserName);
}
