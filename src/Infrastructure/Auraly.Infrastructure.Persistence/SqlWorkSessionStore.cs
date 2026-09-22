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
            connection, null, identity,
            identity.BusinessId ?? throw new WorkSessionForbiddenException(
                "The authenticated web context lacks a valid business."),
            deviceId: null,
            lockRow: false, cancellationToken);
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
                connection, transaction, identity, request.BusinessId, request.DeviceId,
                lockRow: true, cancellationToken);
            if (current is not null)
            {
                if (current.BusinessId != request.BusinessId)
                    throw new WorkSessionConflictException(
                        "El contexto operativo ya tiene una sesión abierta en otra sede.");

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
                  (@WorkSessionId,@TenantId,@BusinessId,NULL,@UserId,@DeviceId,
                   @OpenedAt,@OpenedAt,N'Open');
                """, connection, transaction);
            insert.Parameters.AddWithValue("@WorkSessionId", workSessionId);
            insert.Parameters.AddWithValue("@TenantId", identity.TenantId);
            insert.Parameters.AddWithValue("@BusinessId", request.BusinessId);
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
                null,
                null,
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
            if (request.DeviceId is not null)
                winner = await CurrentForDeviceAsync(
                    identity, request.BusinessId, request.DeviceId.Value, cancellationToken);
            if (winner is not null &&
                winner.BusinessId == request.BusinessId &&
                winner.DeviceId == request.DeviceId)
                return winner;
            throw new WorkSessionConflictException(
                "El usuario web o el equipo enrolado ya tiene una sesión de trabajo abierta en otra sede.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<WorkSessionView> RegisterDeviceOpenAsync(
        WorkSessionIdentity identity,
        RegisterDeviceWorkSessionRequest request,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var scope = await ValidateScopeAsync(
                connection,
                transaction,
                identity,
                new OpenWorkSessionRequest(request.BusinessId, DeviceId: deviceId),
                cancellationToken);
            var existing = await ReadByExactIdAsync(
                connection, transaction, identity, request.WorkSessionId,
                deviceId, cancellationToken);
            if (existing is not null)
            {
                if (existing.BusinessId != request.BusinessId)
                    throw new WorkSessionConflictException(
                        "La sesión local ya existe con otra sede.");
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }

            var current = await ReadOpenAsync(
                connection, transaction, identity, request.BusinessId, deviceId,
                lockRow: true, cancellationToken);
            if (current is not null)
                throw new WorkSessionConflictException(
                    "El equipo enrolado ya tiene otra sesión de trabajo abierta.");

            await using var insert = new SqlCommand("""
                INSERT dbo.WorkSessions
                  (WorkSessionId,TenantId,BusinessId,WarehouseId,UserId,DeviceId,
                   OpenedAt,LastActivityAt,Status)
                VALUES
                  (@WorkSessionId,@TenantId,@BusinessId,NULL,@UserId,@DeviceId,
                   @OpenedAt,@OpenedAt,N'Open');
                """, connection, transaction);
            insert.Parameters.AddWithValue("@WorkSessionId", request.WorkSessionId);
            insert.Parameters.AddWithValue("@TenantId", identity.TenantId);
            insert.Parameters.AddWithValue("@BusinessId", request.BusinessId);
            insert.Parameters.AddWithValue("@UserId", identity.UserId);
            insert.Parameters.AddWithValue("@DeviceId", deviceId);
            insert.Parameters.AddWithValue("@OpenedAt", request.OpenedAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new WorkSessionView(
                request.WorkSessionId, request.BusinessId, scope.BusinessName,
                null, null, identity.UserId, scope.UserName, deviceId,
                request.OpenedAt, request.OpenedAt, "Open", identity.TenantId);
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<WorkSessionView?> CurrentForDeviceAsync(
        WorkSessionIdentity identity,
        Guid businessId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        return await ReadOpenAsync(
            connection, null, identity, businessId, deviceId,
            lockRow: false, cancellationToken);
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

            if (await HasPausedSalesAsync(
                    connection, transaction, identity, workSessionId,
                    lockRange: true, cancellationToken) &&
                !identity.Permissions.Contains(WorkSessionPermissionCodes.CloseWithPausedSales))
                throw new WorkSessionForbiddenException(
                    $"Permission '{WorkSessionPermissionCodes.CloseWithPausedSales}' is required.");

            var expectedTotals = await ReadTotalsAsync(
                connection, transaction, identity, workSessionId, cancellationToken);
            var metrics = await ReadSalesMetricsAsync(
                connection, transaction, identity, workSessionId, cancellationToken);
            if (metrics.CreditSales.Count != metrics.CreditSalesCount ||
                metrics.CreditSales.Sum(value => value.Amount) != metrics.CreditSalesAmount)
                throw new InvalidDataException(
                    "The work-session credit-sale detail does not reconcile with its frozen total.");
            expectedTotals = ApplyCashMovementDetailTotals(
                expectedTotals, metrics.CashMovements);
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
                metrics.ReturnCount,
                metrics.CreditSales,
                request.ReceiptTemplateVersion,
                metrics.CashMovements, metrics.InvoiceCharges,
                metrics.ReceivablePayments,metrics.PayablePayments);
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
        totals = ApplyCashMovementDetailTotals(totals, metrics.CashMovements);
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
            metrics.ReturnCount,
            metrics.CreditSales,
            metrics.CashMovements, metrics.InvoiceCharges,
            metrics.ReceivablePayments,metrics.PayablePayments);
    }

    public async Task<bool> HasPausedSalesAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        return await HasPausedSalesAsync(
            connection, null, identity, workSessionId,
            lockRange: false, cancellationToken);
    }

    private static async Task<bool> HasPausedSalesAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        WorkSessionIdentity identity,
        Guid workSessionId,
        bool lockRange,
        CancellationToken cancellationToken)
    {
        var locking = lockRange ? " WITH (UPDLOCK,HOLDLOCK)" : string.Empty;
        await using var command = new SqlCommand($"""
            SELECT TOP(1) 1
            FROM dbo.SalesDrafts draft{locking}
            INNER JOIN dbo.WorkSessions session
              ON session.WorkSessionId=draft.WorkSessionId
            INNER JOIN dbo.Businesses businessValue
              ON businessValue.BusinessId=session.BusinessId
            WHERE draft.WorkSessionId=@WorkSessionId
              AND draft.UserId=@UserId
              AND draft.Status=N'Temporary'
              AND session.TenantId=@TenantId
              AND businessValue.TenantId=@TenantId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        command.Parameters.AddWithValue("@UserId", identity.UserId);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task<WorkSessionClosureView?> GetClosureAsync(
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var result = await ReadClosureAsync(
            connection, null, identity, workSessionId, cancellationToken,
            allowTenantRead: identity.Permissions.Contains(WorkSessionPermissionCodes.ReadCashDifferences));
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
            SELECT b.Name,CONCAT(u.FirstName,N' ',u.LastName),
                   CASE WHEN @DeviceId IS NULL OR EXISTS
                   (
                       SELECT 1 FROM dbo.EnrolledDevices d
                       WHERE d.DeviceId=@DeviceId AND d.TenantId=b.TenantId
                         AND d.IsActive=1
                   ) THEN 1 ELSE 0 END
            FROM dbo.AppUsers u
            INNER JOIN dbo.Businesses b
              ON b.BusinessId=@BusinessId AND b.TenantId=@TenantId AND b.IsActive=1
            WHERE u.UserId=@UserId AND u.TenantId=@TenantId AND u.IsActive=1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@UserId", identity.UserId);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue(
            "@DeviceId", (object?)request.DeviceId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new WorkSessionForbiddenException(
                "The business or user is outside the authenticated tenant.");
        if (reader.GetInt32(2) != 1)
            throw new WorkSessionForbiddenException(
                "The enrolled device is not active in the authenticated tenant.");
        return new ScopeNames(reader.GetString(0), reader.GetString(1).Trim());
    }

    private static async Task<WorkSessionView?> ReadOpenAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        WorkSessionIdentity identity,
        Guid businessId,
        Guid? deviceId,
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
            LEFT JOIN dbo.Warehouses w ON w.WarehouseId=s.WarehouseId
            INNER JOIN dbo.AppUsers u ON u.UserId=s.UserId
            WHERE s.TenantId=@TenantId AND s.UserId=@UserId AND s.Status=N'Open'
              AND s.BusinessId=@BusinessId
              AND ((@DeviceId IS NULL AND s.DeviceId IS NULL)
                OR (@DeviceId IS NOT NULL AND s.DeviceId=@DeviceId));
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@UserId", identity.UserId);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DeviceId", (object?)deviceId ?? DBNull.Value);
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
            LEFT JOIN dbo.Warehouses w ON w.WarehouseId=s.WarehouseId
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

    private static async Task<WorkSessionView?> ReadByExactIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        WorkSessionIdentity identity,
        Guid workSessionId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT s.WorkSessionId,s.BusinessId,b.Name,s.WarehouseId,w.Name,
                   s.UserId,CONCAT(u.FirstName,N' ',u.LastName),s.DeviceId,
                   s.OpenedAt,s.LastActivityAt,s.Status,s.TenantId
            FROM dbo.WorkSessions s WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Businesses b
              ON b.BusinessId=s.BusinessId AND b.TenantId=s.TenantId
            LEFT JOIN dbo.Warehouses w ON w.WarehouseId=s.WarehouseId
            INNER JOIN dbo.AppUsers u
              ON u.UserId=s.UserId AND u.TenantId=s.TenantId
            WHERE s.WorkSessionId=@WorkSessionId AND s.TenantId=@TenantId
              AND s.UserId=@UserId AND s.DeviceId=@DeviceId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        command.Parameters.AddWithValue("@UserId", identity.UserId);
        command.Parameters.AddWithValue("@DeviceId", deviceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapSession(reader) : null;
    }

    private static WorkSessionView MapSession(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetGuid(3),
        reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetGuid(5),
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
                       N'SalePayment' AS MovementType,
                       payment.Amount+COALESCE(payment.RoundingAdjustment,0) AS Amount
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
                    Amount DECIMAL(19,4) N'$.amount',
                    RoundingAdjustment DECIMAL(19,4) N'$.roundingAdjustment'
                  ) payment
                LEFT JOIN worksessions.CashClosurePaymentMethodMappings mapping
                  ON mapping.PaymentMethodCode=payment.MethodCode
                WHERE d.WorkSessionId=@WorkSessionId
                UNION ALL
                SELECT COALESCE(mapping.ClosureMethodCode,p.MethodCode),
                       N'SalePayment',p.Amount+p.RoundingAdjustment
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
                SELECT COALESCE(mapping.ClosureMethodCode,tender.MethodCode),N'ReceivablePayment',tender.Amount
                FROM dbo.CustomerPayments payment
                INNER JOIN SessionScope session ON session.WorkSessionId=payment.WorkSessionId
                  AND session.BusinessId=payment.BusinessId
                INNER JOIN dbo.CustomerPaymentTenders tender ON tender.PaymentId=payment.PaymentId
                LEFT JOIN worksessions.CashClosurePaymentMethodMappings mapping
                  ON mapping.PaymentMethodCode=tender.MethodCode
                WHERE payment.Status IN(N'Accepted',N'Processed')
                UNION ALL
                SELECT COALESCE(mapping.ClosureMethodCode,tender.MethodCode),N'PayablePayment',-tender.Amount
                FROM dbo.SupplierPayments payment
                INNER JOIN SessionScope session ON session.WorkSessionId=payment.WorkSessionId
                  AND session.BusinessId=payment.BusinessId
                INNER JOIN dbo.SupplierPaymentTenders tender ON tender.PaymentId=payment.PaymentId
                LEFT JOIN worksessions.CashClosurePaymentMethodMappings mapping
                  ON mapping.PaymentMethodCode=tender.MethodCode
                WHERE payment.Status IN(N'Accepted',N'Processed')
                UNION ALL
                SELECT COALESCE(mapping.ClosureMethodCode,movement.PaymentMethodCode),movement.MovementType,movement.Amount
                FROM dbo.WorkSessionMovements movement
                INNER JOIN SessionScope session
                  ON session.WorkSessionId=movement.WorkSessionId
                LEFT JOIN worksessions.CashClosurePaymentMethodMappings mapping
                  ON mapping.PaymentMethodCode=movement.PaymentMethodCode
                WHERE movement.MovementType NOT IN(N'SalePayment',N'Refund',N'PayablePayment',N'ReceivablePayment')
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
                  COALESCE(SUM(Amount),0) NetAmount,
                  COALESCE(SUM(CASE WHEN MovementType=N'CashIn' THEN Amount ELSE 0 END),0) CashEntryAmount,
                  COALESCE(SUM(CASE WHEN MovementType=N'CashOut' THEN ABS(Amount) ELSE 0 END),0) CashExitAmount
                FROM PaymentMovements
                GROUP BY PaymentMethodCode
            ),
            AllTotals AS
            (
                SELECT options.Code AS PaymentMethodCode,
                       COALESCE(totals.SalesAmount,0) SalesAmount,
                       COALESCE(totals.RefundAmount,0) RefundAmount,
                       COALESCE(totals.OtherAmount,0) OtherAmount,
                       COALESCE(totals.NetAmount,0) NetAmount,
                       COALESCE(totals.CashEntryAmount,0) CashEntryAmount,
                       COALESCE(totals.CashExitAmount,0) CashExitAmount
                FROM reference.Options options
                LEFT JOIN Totals totals ON totals.PaymentMethodCode=options.Code
                WHERE options.CatalogCode=N'cash-closure-method' AND options.IsActive=1
                UNION ALL
                SELECT totals.PaymentMethodCode,totals.SalesAmount,totals.RefundAmount,
                       totals.OtherAmount,totals.NetAmount,
                       totals.CashEntryAmount,totals.CashExitAmount
                FROM Totals totals
                WHERE NOT EXISTS
                (
                    SELECT 1 FROM reference.Options options
                    WHERE options.CatalogCode=N'cash-closure-method' AND options.IsActive=1
                      AND options.Code=totals.PaymentMethodCode
                )
            )
            SELECT total.PaymentMethodCode,total.SalesAmount,total.RefundAmount,total.OtherAmount,total.NetAmount,
                   CAST(CASE WHEN closureOption.OptionId IS NOT NULL THEN 1 ELSE 0 END AS bit),
                   total.CashEntryAmount,total.CashExitAmount
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
                reader.GetDecimal(3), reader.GetDecimal(4), RequiresCount: reader.GetBoolean(5),
                CashEntryAmount: reader.GetDecimal(6), CashExitAmount: reader.GetDecimal(7)));
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

            SELECT COALESCE(p.DisplayName,p.LegalName,p.Identification,N'Cliente'),
                   d.DocumentNumber,d.CreditAmount
            FROM dbo.SalesDocuments d
            LEFT JOIN dbo.Customers c ON c.CustomerId=d.CustomerId
            LEFT JOIN dbo.Parties p ON p.PartyId=c.PartyId
            WHERE d.WorkSessionId=@WorkSessionId AND d.BusinessId IN(
                SELECT BusinessId FROM dbo.WorkSessions
                WHERE WorkSessionId=@WorkSessionId AND TenantId=@TenantId AND UserId=@UserId)
              AND d.CreditAmount>0
            ORDER BY d.IssuedAt,d.DocumentId;

            SELECT detail.DocumentId,detail.Direction,detail.DocumentNumber,
                   detail.ReasonName,detail.Amount,detail.OccurredAt,
                   detail.ResponsibleName,detail.Reference,detail.Notes
            FROM
            (
                SELECT document.DocumentId,document.Direction,document.DocumentNumber,
                       reason.Name ReasonName,document.Amount,document.OccurredAt,
                       LTRIM(RTRIM(CONCAT(users.FirstName,N' ',users.LastName))) ResponsibleName,
                       document.Reference,document.Notes
                FROM dbo.CashMovementDocuments document
                INNER JOIN dbo.WorkSessions session
                  ON session.WorkSessionId=document.WorkSessionId
                 AND session.BusinessId=document.BusinessId
                INNER JOIN dbo.CashMovementReasons reason
                  ON reason.BusinessId=document.BusinessId
                 AND reason.ReasonId=document.ReasonId
                INNER JOIN dbo.AppUsers users
                  ON users.UserId=document.ConfirmedByUserId
                 AND users.TenantId=session.TenantId
                WHERE document.WorkSessionId=@WorkSessionId
                  AND session.TenantId=@TenantId AND session.UserId=@UserId
                  AND document.Status IN(N'Accepted',N'Processed')

                UNION ALL

                SELECT COALESCE(supplierPayment.PaymentId,customerPayment.PaymentId,movement.WorkSessionMovementId),
                       CASE WHEN movement.Amount>0 THEN N'In' ELSE N'Out' END,
                       COALESCE(supplierPayment.DocumentNumber,customerPayment.DocumentNumber,movement.SourceKey),
                       COALESCE(documentType.Label,CASE WHEN movement.MovementType=N'CashIn'
                            THEN N'Entrada de dinero' ELSE N'Salida de dinero' END),
                       ABS(movement.Amount),movement.OccurredAt,
                       LTRIM(RTRIM(CONCAT(users.FirstName,N' ',users.LastName))),
                       movement.Reference,COALESCE(supplierPayment.Notes,customerPayment.Notes)
                FROM dbo.WorkSessionMovements movement
                INNER JOIN dbo.WorkSessions session
                  ON session.WorkSessionId=movement.WorkSessionId
                INNER JOIN dbo.AppUsers users
                  ON users.UserId=movement.RecordedByUserId
                 AND users.TenantId=session.TenantId
                LEFT JOIN dbo.SupplierPayments supplierPayment ON supplierPayment.PaymentId=movement.DocumentId
                  AND movement.MovementType=N'PayablePayment' AND supplierPayment.BusinessId=session.BusinessId
                LEFT JOIN dbo.CustomerPayments customerPayment ON customerPayment.PaymentId=
                    COALESCE(movement.DocumentId,TRY_CONVERT(uniqueidentifier,RIGHT(movement.SourceKey,36)))
                  AND movement.MovementType=N'ReceivablePayment' AND customerPayment.BusinessId=session.BusinessId
                LEFT JOIN reference.Options documentType ON documentType.CatalogCode=N'accounting-document-type'
                  AND documentType.Code=movement.MovementType
                LEFT JOIN worksessions.CashClosurePaymentMethodMappings mapping
                  ON mapping.PaymentMethodCode=movement.PaymentMethodCode
                WHERE movement.WorkSessionId=@WorkSessionId
                  AND session.TenantId=@TenantId AND session.UserId=@UserId
                  AND movement.MovementType IN(N'CashIn',N'CashOut',N'PayablePayment',N'ReceivablePayment')
                  AND COALESCE(mapping.ClosureMethodCode,movement.PaymentMethodCode)=N'Cash'
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM dbo.CashMovementDocuments document
                      WHERE document.DocumentId=movement.DocumentId
                        AND document.WorkSessionId=movement.WorkSessionId
                  )
            ) detail
            ORDER BY detail.OccurredAt,detail.DocumentId;
            SELECT payload.PayloadJson FROM dbo.SalesDocuments d
            JOIN dbo.DocumentProcessingPayloads payload ON payload.DocumentId=d.DocumentId AND payload.DocumentType=d.DocumentType
            JOIN dbo.WorkSessions session ON session.WorkSessionId=d.WorkSessionId AND session.BusinessId=d.BusinessId
            WHERE session.WorkSessionId=@WorkSessionId AND session.TenantId=@TenantId AND session.UserId=@UserId
              AND JSON_QUERY(payload.PayloadJson,'$.charges') IS NOT NULL
            ORDER BY d.IssuedAt,d.DocumentId;
            SELECT PaymentMethodCode,ClosureMethodCode FROM worksessions.CashClosurePaymentMethodMappings;
            SELECT payment.PaymentId,payment.DocumentNumber,
              COALESCE(party.DisplayName,party.LegalName,party.Identification,N'Cliente'),
              payment.TotalAmount,payment.PaidAt,receivable.DocumentNumber,application.Amount
            FROM dbo.CustomerPayments payment
            JOIN dbo.WorkSessions session ON session.WorkSessionId=payment.WorkSessionId
              AND session.BusinessId=payment.BusinessId
            JOIN dbo.Customers customer ON customer.CustomerId=payment.CustomerId
            JOIN dbo.Parties party ON party.PartyId=customer.PartyId
            JOIN dbo.CustomerPaymentApplications application ON application.PaymentId=payment.PaymentId
            JOIN dbo.Receivables receivable ON receivable.ReceivableId=application.ReceivableId
            WHERE payment.WorkSessionId=@WorkSessionId AND session.TenantId=@TenantId
              AND session.UserId=@UserId AND payment.Status IN(N'Accepted',N'Processed')
            ORDER BY payment.PaidAt,payment.PaymentId,application.LineNumber;
            SELECT payment.PaymentId,payment.DocumentNumber,supplier.Name,payment.TotalAmount,
              payment.PaidAt,payable.DocumentNumber,application.Amount
            FROM dbo.SupplierPayments payment
            JOIN dbo.WorkSessions session ON session.WorkSessionId=payment.WorkSessionId
              AND session.BusinessId=payment.BusinessId
            JOIN dbo.Suppliers supplier ON supplier.SupplierId=payment.SupplierId
            JOIN dbo.SupplierPaymentApplications application ON application.PaymentId=payment.PaymentId
            JOIN dbo.Payables payable ON payable.PayableId=application.PayableId
            WHERE payment.WorkSessionId=@WorkSessionId AND session.TenantId=@TenantId
              AND session.UserId=@UserId AND payment.Status IN(N'Accepted',N'Processed')
            ORDER BY payment.PaidAt,payment.PaymentId,application.LineNumber;
            """, connection, transaction);
        command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        command.Parameters.AddWithValue("@UserId", identity.UserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var metrics = new SalesMetrics(
            reader.GetInt64(0), reader.GetInt32(1), reader.GetDecimal(2),
            reader.GetInt64(3), [], [], [], [], []);
        await reader.NextResultAsync(cancellationToken);
        var creditSales = new List<WorkSessionCreditSale>();
        while (await reader.ReadAsync(cancellationToken))
            creditSales.Add(new WorkSessionCreditSale(
                reader.GetString(0), reader.GetString(1), reader.GetDecimal(2)));
        await reader.NextResultAsync(cancellationToken);
        var cashMovements = new List<WorkSessionCashMovementDetail>();
        while (await reader.ReadAsync(cancellationToken))
            cashMovements.Add(new WorkSessionCashMovementDetail(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetDecimal(4),
                reader.GetDateTimeOffset(5), reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        await reader.NextResultAsync(cancellationToken);
        var invoiceCharges = new List<WorkSessionInvoiceCharge>();
        while (await reader.ReadAsync(cancellationToken))
            invoiceCharges.AddRange(Auraly.Application.Sales.InvoiceChargeClosureProjection.FromSale(
                Auraly.Contracts.Sales.PosSaleContractSerializer.Deserialize(reader.GetString(0))));
        await reader.NextResultAsync(cancellationToken);
        var closureMethods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken)) closureMethods.Add(reader.GetString(0), reader.GetString(1));
        await reader.NextResultAsync(cancellationToken);
        var receivablePayments=await ReadPortfolioPaymentsAsync(reader,cancellationToken);
        await reader.NextResultAsync(cancellationToken);
        var payablePayments=await ReadPortfolioPaymentsAsync(reader,cancellationToken);
        return metrics with
        {
            CreditSales = creditSales,
            CashMovements = cashMovements,
            InvoiceCharges = Auraly.Application.Sales.InvoiceChargeClosureProjection.MapPayments(invoiceCharges,
                method => closureMethods.GetValueOrDefault(method, method)),
            ReceivablePayments=receivablePayments,
            PayablePayments=payablePayments
        };
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
            LEFT JOIN dbo.Warehouses w ON w.WarehouseId=s.WarehouseId
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
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetGuid(6), reader.GetString(7),
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
        if (totals.Count == 0) return;
        var sql = new StringBuilder("""
            INSERT dbo.WorkSessionClosurePaymentTotals
              (WorkSessionClosureId,PaymentMethodCode,SalesAmount,
               RefundAmount,OtherAmount,NetAmount,CountedAmount,Difference)
            VALUES
            """);
        await using var command = new SqlCommand { Connection = connection, Transaction = transaction };
        command.Parameters.AddWithValue("@ClosureId", closureId);
        for (var index = 0; index < totals.Count; index++)
        {
            if (index > 0) sql.Append(',');
            sql.Append($"(@ClosureId,@Method{index},@Sales{index},@Refund{index},@Other{index},@Net{index},@Counted{index},@Difference{index})");
            var total = totals[index];
            command.Parameters.AddWithValue($"@Method{index}", total.PaymentMethodCode);
            AddMoney(command, $"@Sales{index}", total.SalesAmount);
            AddMoney(command, $"@Refund{index}", total.RefundAmount);
            AddMoney(command, $"@Other{index}", total.OtherAmount);
            AddMoney(command, $"@Net{index}", total.NetAmount);
            command.Parameters.Add(new SqlParameter($"@Counted{index}", SqlDbType.Decimal)
            { Precision = 19, Scale = 4, Value = (object?)total.CountedAmount ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter($"@Difference{index}", SqlDbType.Decimal)
            { Precision = 19, Scale = 4, Value = (object?)total.Difference ?? DBNull.Value });
        }
        command.CommandText = sql.Append(';').ToString();
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<WorkSessionClosureDifferenceLine>> LoadClosureDifferenceLinesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyList<WorkSessionPaymentTotal> totals,
        CancellationToken cancellationToken)
    {
        var differences = totals
            .Where(value => value.Difference is not null and not 0)
            .ToArray();
        if (differences.Length == 0) return [];
        var values = new StringBuilder();
        await using var command = new SqlCommand { Connection = connection, Transaction = transaction };
        for (var index = 0; index < differences.Length; index++)
        {
            if (index > 0) values.Append(',');
            values.Append($"(@Method{index},@Expected{index},@Counted{index},@Difference{index})");
            var total = differences[index];
            command.Parameters.AddWithValue($"@Method{index}", total.PaymentMethodCode);
            AddMoney(command, $"@Expected{index}", total.NetAmount);
            AddMoney(command, $"@Counted{index}", total.CountedAmount!.Value);
            AddMoney(command, $"@Difference{index}", total.Difference!.Value);
        }
        command.CommandText = $"""
            SELECT requested.PaymentMethodCode,mapping.Category,
                   requested.ExpectedAmount,requested.CountedAmount,requested.Difference
            FROM (VALUES {values}) requested(
                PaymentMethodCode,ExpectedAmount,CountedAmount,Difference)
            LEFT JOIN dbo.AccountingConfigurationProfiles profile
              ON profile.IsDefault=1 AND profile.IsActive=1
            LEFT JOIN dbo.AccountingSourceCategoryMappings mapping
              ON mapping.ProfileCode=profile.ProfileCode
             AND mapping.SourceType=N'ClosurePaymentMethod'
             AND mapping.SourceCode=requested.PaymentMethodCode;
            """;
        var result = new List<WorkSessionClosureDifferenceLine>(differences.Length);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(1))
                throw new WorkSessionValidationException(
                    $"El medio de pago '{reader.GetString(0)}' no tiene configuración contable para cierres.");
            result.Add(new WorkSessionClosureDifferenceLine(
                reader.GetString(0), reader.GetString(1), reader.GetDecimal(2),
                reader.GetDecimal(3), reader.GetDecimal(4)));
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
                   PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
                VALUES
                  (@DocumentId,N'WorkSessionCashDifference',@TenantId,@BusinessId,
                   @Payload,@Hash,@OccurredAt,@OccurredAt,1);

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
        long SalesCount, int CreditSalesCount, decimal CreditSalesAmount, long ReturnCount,
        IReadOnlyList<WorkSessionCreditSale> CreditSales,
        IReadOnlyList<WorkSessionCashMovementDetail> CashMovements,
        IReadOnlyList<WorkSessionInvoiceCharge> InvoiceCharges,
        IReadOnlyList<WorkSessionPortfolioPayment> ReceivablePayments,
        IReadOnlyList<WorkSessionPortfolioPayment> PayablePayments);

    private static async Task<IReadOnlyList<WorkSessionPortfolioPayment>> ReadPortfolioPaymentsAsync(
        SqlDataReader reader,CancellationToken cancellationToken)
    {
        var rows=new List<(Guid Id,string Number,string Party,decimal Total,DateTimeOffset PaidAt,string Invoice,decimal Amount)>();
        while(await reader.ReadAsync(cancellationToken))rows.Add((reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetDecimal(3),reader.GetDateTimeOffset(4),reader.GetString(5),reader.GetDecimal(6)));
        return rows.GroupBy(x=>new{x.Id,x.Number,x.Party,x.Total,x.PaidAt})
            .Select(group=>new WorkSessionPortfolioPayment(group.Key.Id,group.Key.Number,group.Key.Party,
                group.Key.Total,group.Key.PaidAt,group.Select(x=>new WorkSessionPortfolioApplication(x.Invoice,x.Amount)).ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<WorkSessionPaymentTotal> ApplyCashMovementDetailTotals(
        IReadOnlyList<WorkSessionPaymentTotal> totals,
        IReadOnlyList<WorkSessionCashMovementDetail> details)
    {
        var entries = details
            .Where(value => value.Direction == CashMovementDirections.In)
            .Sum(value => value.Amount);
        var exits = details
            .Where(value => value.Direction == CashMovementDirections.Out)
            .Sum(value => value.Amount);
        return totals
            .Select(value => string.Equals(
                value.PaymentMethodCode, "Cash", StringComparison.OrdinalIgnoreCase)
                ? value with
                {
                    CashEntryAmount = entries,
                    CashExitAmount = exits
                }
                : value)
            .ToArray();
    }

    private static async Task<(string IdempotencyKey, WorkSessionClosureView Closure)?> ReadClosureAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        WorkSessionIdentity identity,
        Guid workSessionId,
        CancellationToken cancellationToken, bool allowTenantRead = false)
    {
        await using var command = new SqlCommand("""
            SELECT c.IdempotencyKey,c.ReceiptSnapshotJson,c.ReceiptHash
            FROM dbo.WorkSessionClosures c
            INNER JOIN dbo.WorkSessions s ON s.WorkSessionId=c.WorkSessionId
            INNER JOIN dbo.Businesses b ON b.BusinessId=s.BusinessId
            WHERE c.WorkSessionId=@WorkSessionId AND (@AllowTenantRead=1 OR s.UserId=@UserId)
              AND b.TenantId=@TenantId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        command.Parameters.AddWithValue("@AllowTenantRead", allowTenantRead);
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
        string UserName);
}
