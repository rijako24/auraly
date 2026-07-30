using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Cash;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Cash;
using Auraly.Domain.Cash;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlCashSessionStore : ICashSessionStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SqlServerConnectionFactory _connections;
    private readonly IAuralyIdGenerator _ids;
    private readonly TimeProvider _timeProvider;

    public SqlCashSessionStore(
        SqlServerConnectionFactory connections,
        IAuralyIdGenerator ids,
        TimeProvider timeProvider)
    {
        _connections = connections;
        _ids = ids;
        _timeProvider = timeProvider;
    }

    public async Task<CashSessionView?> CurrentAsync(
        CashUserIdentity actor, Guid registerId, CancellationToken ct)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(ct);
        await ValidateRegisterAsync(connection, null, actor, registerId, null, null, ct);
        return await CurrentAsync(connection, null, registerId, actor.UserId, ct);
    }

    public async Task<CashSessionView> OpenOrResumeAsync(
        CashUserIdentity actor,
        Guid registerId,
        OpenCashSessionRequest request,
        CancellationToken ct)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await LockRegisterAsync(
            connection, transaction, registerId, ct);
        var register = await ValidateRegisterAsync(
            connection, transaction, actor, registerId,
            request.BusinessId, request.LocationId, ct);
        var now = _timeProvider.GetUtcNow();
        var current = await CurrentForUpdateAsync(
            connection, transaction, registerId, actor.UserId, ct);
        if (current is null)
        {
            var sessionId = await OpenSessionIdForUpdateAsync(
                connection, transaction, registerId, ct);
            var shiftId = _ids.NewId();
            if (sessionId is null)
            {
                sessionId = _ids.NewId();
                await ExecuteAsync(connection, transaction, """
                    INSERT dbo.CashSessions
                        (CashSessionId,BusinessId,LocationId,RegisterId,OpenedByUserId,
                         OpenedAt,OpeningFloat,Status,OpenIdempotencyKey)
                    VALUES
                        (@SessionId,@BusinessId,@LocationId,@RegisterId,@UserId,
                         @Now,@OpeningFloat,N'Open',@Key);
                    INSERT dbo.CashierShifts
                        (CashierShiftId,CashSessionId,RegisterId,UserId,StartedAt,Status)
                    VALUES
                        (@ShiftId,@SessionId,@RegisterId,@UserId,@Now,N'Active');
                    """, ct,
                    P("@SessionId", sessionId.Value), P("@ShiftId", shiftId),
                    P("@BusinessId", register.BusinessId), P("@LocationId", register.LocationId),
                    P("@RegisterId", registerId), P("@UserId", actor.UserId),
                    P("@Now", now), Money("@OpeningFloat", request.OpeningFloat),
                    P("@Key", request.IdempotencyKey.Trim()));
            }
            else
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT dbo.CashierShifts
                        (CashierShiftId,CashSessionId,RegisterId,UserId,StartedAt,Status)
                    VALUES
                        (@ShiftId,@SessionId,@RegisterId,@UserId,@Now,N'Active');
                    """, ct,
                    P("@ShiftId", shiftId), P("@SessionId", sessionId.Value),
                    P("@RegisterId", registerId), P("@UserId", actor.UserId), P("@Now", now));
            }
        }

        var result = await CurrentAsync(
            connection, transaction, registerId, actor.UserId, ct)
            ?? throw new DBConcurrencyException("La sesión de caja no quedó disponible.");
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<CashHandoffResult> HandoffAsync(
        CashUserIdentity actor,
        Guid registerId,
        HandoffCashRequest request,
        CancellationToken ct)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await ValidateRegisterAsync(connection, transaction, actor, registerId, null, null, ct);
        await ValidateReceiverAsync(connection, transaction, actor.TenantId, request.ReceivedByUserId, ct);
        var duplicate = await FindHandoffAsync(
            connection, transaction, registerId, request.IdempotencyKey, ct);
        if (duplicate is not null)
        {
            await transaction.CommitAsync(ct);
            return duplicate;
        }
        var current = await CurrentForUpdateAsync(
                connection, transaction, registerId, actor.UserId, ct)
            ?? throw new CashConflictException("La caja no está abierta para este cajero.");
        var authorizedByUserId = await ConsumeSupervisorAuthorizationAsync(
            connection, transaction, actor, current, request.SupervisorAuthorizationToken,
            CommercePermissionCodes.CashHandoffApprove, ct);

        var expected = await ExpectedAsync(
            connection, transaction, current.CashSessionId, null, true, ct);
        var reconciliation = CashReconciliation.Calculate(expected, request.Counts);
        RequireDifferenceReason(reconciliation, request.DifferenceReason);
        var now = _timeProvider.GetUtcNow();
        var countId = _ids.NewId();
        await ExecuteAsync(connection, transaction, """
            INSERT dbo.CashCounts
                (CashCountId,CashSessionId,CashierShiftId,CountType,Status,
                 CountedByUserId,ReceivedByUserId,AuthorizedByUserId,ExpectedCalculatedAt,StartedAt,
                 ConfirmedAt,Observation,DifferenceReason,IdempotencyKey)
            VALUES
                (@CountId,@SessionId,@ShiftId,N'Handoff',N'Confirmed',
                 @UserId,@ReceiverId,@AuthorizedBy,@Now,@Now,@Now,@Observation,@DifferenceReason,@Key);
            """, ct,
            P("@CountId", countId), P("@SessionId", current.CashSessionId),
            P("@ShiftId", current.CashierShiftId), P("@UserId", actor.UserId),
            P("@ReceiverId", request.ReceivedByUserId), P("@Now", now),
            P("@Observation", Db(request.Observation)),
            P("@DifferenceReason", Db(request.DifferenceReason)),
            P("@AuthorizedBy", authorizedByUserId),
            P("@Key", request.IdempotencyKey.Trim()));
        await InsertCountLinesAsync(connection, transaction, countId, reconciliation, ct);
        await EndShiftAndStartAsync(
            connection, transaction, current, request.ReceivedByUserId, now, "Handoff", ct);
        var next = await CurrentAsync(
                connection, transaction, registerId, request.ReceivedByUserId, ct)
            ?? throw new DBConcurrencyException("El relevo no creó el nuevo turno.");
        await transaction.CommitAsync(ct);
        return new CashHandoffResult(countId, next, reconciliation);
    }

    public async Task<CashClosureReceipt> CloseAsync(
        CashUserIdentity actor,
        Guid registerId,
        CloseCashSessionRequest request,
        CancellationToken ct)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        var register = await ValidateRegisterAsync(
            connection, transaction, actor, registerId, null, null, ct);
        var current = await RequireCurrentForActorAsync(
            connection, transaction, actor, registerId, ct);
        var duplicate = await ReceiptByKeyAsync(
            connection, transaction, current.CashSessionId, request.IdempotencyKey, ct);
        if (duplicate is not null)
        {
            await transaction.CommitAsync(ct);
            return duplicate;
        }

        var expected = await ExpectedAsync(
            connection, transaction, current.CashSessionId, null, true, ct);
        var reconciliation = CashReconciliation.Calculate(expected, request.Counts);
        RequireDifferenceReason(reconciliation, request.DifferenceReason);
        var now = _timeProvider.GetUtcNow();
        var countId = _ids.NewId();
        var consecutive = await NextCountNumberAsync(
            connection, transaction, registerId, now, ct);
        var countNumber = $"ARQ{NormalizeRegisterCode(register.RegisterCode)}-{consecutive:D8}";
        var receipt = await BuildReceiptAsync(
            connection, transaction, current, register, countId, countNumber,
            now, reconciliation, request.Observation, ct);
        var snapshot = JsonSerializer.Serialize(receipt, Json);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(snapshot));

        await ExecuteAsync(connection, transaction, """
            INSERT dbo.CashCounts
                (CashCountId,CashSessionId,CashierShiftId,CountType,Status,
                 CountNumber,CountConsecutive,CountedByUserId,
                 ExpectedCalculatedAt,StartedAt,ConfirmedAt,Observation,
                 DifferenceReason,ReceiptSnapshotJson,ReceiptHash,IdempotencyKey)
            VALUES
                (@CountId,@SessionId,@ShiftId,N'Final',N'Confirmed',
                 @CountNumber,@Consecutive,@UserId,@Now,@Now,@Now,@Observation,
                 @DifferenceReason,@Snapshot,@Hash,@Key);
            UPDATE dbo.CashierShifts
            SET Status=N'Ended',EndedAt=@Now,EndReason=N'SessionClosed',EndedByUserId=@UserId
            WHERE CashSessionId=@SessionId AND Status=N'Active';
            UPDATE dbo.CashSessions
            SET Status=N'Closed',ClosedAt=@Now,ClosedByUserId=@UserId
            WHERE CashSessionId=@SessionId AND Status=N'Open';
            """, ct,
            P("@CountId", countId), P("@SessionId", current.CashSessionId),
            P("@ShiftId", current.CashierShiftId), P("@CountNumber", countNumber),
            P("@Consecutive", consecutive), P("@UserId", actor.UserId), P("@Now", now),
            P("@Observation", Db(request.Observation)),
            P("@DifferenceReason", Db(request.DifferenceReason)),
            P("@Snapshot", snapshot), Binary("@Hash", hash), P("@Key", request.IdempotencyKey.Trim()));
        await InsertCountLinesAsync(connection, transaction, countId, reconciliation, ct);
        await transaction.CommitAsync(ct);
        return receipt;
    }

    public async Task<CashClosureReceipt?> ReceiptAsync(
        CashUserIdentity actor, Guid cashCountId, CancellationToken ct)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(ct);
        const string sql = """
            SELECT c.ReceiptSnapshotJson
            FROM dbo.CashCounts c
            INNER JOIN dbo.CashSessions s ON s.CashSessionId=c.CashSessionId
            INNER JOIN dbo.Businesses b ON b.BusinessId=s.BusinessId
            WHERE c.CashCountId=@CountId AND c.CountType=N'Final'
              AND c.Status=N'Confirmed' AND b.TenantId=@TenantId;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange([P("@CountId", cashCountId), P("@TenantId", actor.TenantId)]);
        var value = await command.ExecuteScalarAsync(ct);
        return value is string snapshot
            ? JsonSerializer.Deserialize<CashClosureReceipt>(snapshot, Json)
            : null;
    }

    public async Task<CashDailySummary> DailyAsync(
        CashUserIdentity actor, Guid registerId, DateOnly businessDate, CancellationToken ct)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(ct);
        await ValidateRegisterAsync(connection, null, actor, registerId, null, null, ct);
        const string totalsSql = """
            SELECT COUNT(DISTINCT m.CashSessionId),COUNT(DISTINCT m.DocumentId),
                   COALESCE(SUM(CASE WHEN m.MovementType=N'SalePayment' THEN m.Amount ELSE 0 END),0)
            FROM dbo.CashMovements m
            INNER JOIN dbo.CashSessions s ON s.CashSessionId=m.CashSessionId
            WHERE s.RegisterId=@RegisterId AND m.BusinessDate=@BusinessDate;
            """;
        await using var totals = new SqlCommand(totalsSql, connection);
        totals.Parameters.AddRange([
            P("@RegisterId", registerId),
            new SqlParameter("@BusinessDate", SqlDbType.Date) { Value = businessDate.ToDateTime(TimeOnly.MinValue) }
        ]);
        await using var reader = await totals.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var sessions = reader.GetInt32(0);
        var documents = reader.GetInt32(1);
        var net = reader.GetDecimal(2);
        await reader.DisposeAsync();
        var expected = await DailyPaymentsAsync(connection, registerId, businessDate, ct);
        var payments = expected.Select(pair => new CashDailyPaymentSummary(pair.Key, pair.Value))
            .ToArray();
        return new CashDailySummary(registerId, businessDate, sessions, documents, net, payments);
    }

    private async Task<CashClosureReceipt> BuildReceiptAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CashSessionView current,
        RegisterInfo register,
        Guid countId,
        string countNumber,
        DateTimeOffset closedAt,
        IReadOnlyList<CashReconciliationLine> reconciliation,
        string? observation,
        CancellationToken ct)
    {
        var document = await DocumentSummaryAsync(connection, transaction, current.CashSessionId, ct);
        var cashiers = await CashierSummaryAsync(
            connection, transaction, current.CashSessionId, closedAt, ct);
        var taxes = await TaxSummaryAsync(connection, transaction, current.CashSessionId, ct);
        var days = await DaySummaryAsync(connection, transaction, current.CashSessionId, ct);
        var movements = await MovementSummaryAsync(connection, transaction, current.CashSessionId, ct);
        return new CashClosureReceipt(
            countId, current.CashSessionId, countNumber,
            register.BusinessName, register.LocationName,
            register.RegisterCode, register.RegisterName,
            current.OpenedAt, closedAt,
            await UserNameAsync(connection, transaction, register.OpenedByUserId, ct),
            current.ResponsibleUserName,
            current.OpeningFloat,
            document.FirstDocument, document.LastDocument,
            document.FirstFiscal, document.LastFiscal,
            document.Count, document.Gross, document.Discounts, 0m, document.Net,
            movements.CashIn, movements.CashOut,
            cashiers, reconciliation, taxes, days, NormalizeOptional(observation));
    }

    private static async Task<CashSessionView?> CurrentAsync(
        SqlConnection connection, SqlTransaction? transaction, Guid registerId,
        Guid userId, CancellationToken ct)
    {
        const string sql = """
            SELECT s.CashSessionId,sh.CashierShiftId,s.BusinessId,s.LocationId,s.RegisterId,
                   sh.UserId,CONCAT(u.FirstName,N' ',u.LastName),s.OpenedAt,sh.StartedAt,
                   s.OpeningFloat,s.Status
            FROM dbo.CashSessions s
            INNER JOIN dbo.CashierShifts sh ON sh.CashSessionId=s.CashSessionId AND sh.Status=N'Active'
            INNER JOIN dbo.AppUsers u ON u.UserId=sh.UserId
            WHERE s.RegisterId=@RegisterId AND s.Status=N'Open' AND sh.UserId=@UserId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange([P("@RegisterId", registerId), P("@UserId", userId)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadCurrent(reader);
    }

    private static async Task<CashSessionView?> CurrentForUpdateAsync(
        SqlConnection connection, SqlTransaction transaction, Guid registerId,
        Guid userId, CancellationToken ct)
    {
        const string sql = """
            SELECT s.CashSessionId,sh.CashierShiftId,s.BusinessId,s.LocationId,s.RegisterId,
                   sh.UserId,CONCAT(u.FirstName,N' ',u.LastName),s.OpenedAt,sh.StartedAt,
                   s.OpeningFloat,s.Status
            FROM dbo.CashSessions s WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.CashierShifts sh WITH (UPDLOCK,HOLDLOCK)
              ON sh.CashSessionId=s.CashSessionId AND sh.Status=N'Active'
            INNER JOIN dbo.AppUsers u ON u.UserId=sh.UserId
            WHERE s.RegisterId=@RegisterId AND s.Status=N'Open' AND sh.UserId=@UserId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange([P("@RegisterId", registerId), P("@UserId", userId)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadCurrent(reader) : null;
    }

    private static async Task<Guid?> OpenSessionIdForUpdateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid registerId,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT CashSessionId
            FROM dbo.CashSessions WITH (UPDLOCK,HOLDLOCK)
            WHERE RegisterId=@RegisterId AND Status=N'Open';
            """, connection, transaction);
        command.Parameters.AddWithValue("@RegisterId", registerId);
        var value = await command.ExecuteScalarAsync(ct);
        return value is Guid sessionId ? sessionId : null;
    }

    private static CashSessionView ReadCurrent(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
        reader.GetGuid(4), reader.GetGuid(5), reader.GetString(6),
        reader.GetDateTimeOffset(7), reader.GetDateTimeOffset(8),
        reader.GetDecimal(9), reader.GetString(10));

    private static async Task<CashSessionView> RequireCurrentForActorAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CashUserIdentity actor,
        Guid registerId,
        CancellationToken ct)
    {
        var current = await CurrentForUpdateAsync(
                connection, transaction, registerId, actor.UserId, ct)
            ?? throw new CashConflictException("La caja no está abierta.");
        return current;
    }

    private async Task EndShiftAndStartAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CashSessionView current,
        Guid nextUserId,
        DateTimeOffset now,
        string reason,
        CancellationToken ct)
    {
        var existing = await CurrentForUpdateAsync(
            connection, transaction, current.RegisterId, nextUserId, ct);
        if (existing is not null)
        {
            await ExecuteAsync(connection, transaction, """
                UPDATE dbo.CashierShifts
                SET Status=N'Ended',EndedAt=@Now,EndReason=@Reason,EndedByUserId=@NextUser
                WHERE CashierShiftId=@CurrentShift AND Status=N'Active';
                """, ct, P("@Now", now), P("@Reason", reason), P("@NextUser", nextUserId),
                P("@CurrentShift", current.CashierShiftId));
            return;
        }

        await ExecuteAsync(connection, transaction, """
            UPDATE dbo.CashierShifts
            SET Status=N'Ended',EndedAt=@Now,EndReason=@Reason,EndedByUserId=@NextUser
            WHERE CashierShiftId=@CurrentShift AND Status=N'Active';
            INSERT dbo.CashierShifts
                (CashierShiftId,CashSessionId,RegisterId,UserId,StartedAt,Status)
            VALUES
                (@NextShift,@SessionId,@RegisterId,@NextUser,@Now,N'Active');
            """, ct,
            P("@Now", now), P("@Reason", reason), P("@NextUser", nextUserId),
            P("@CurrentShift", current.CashierShiftId), P("@NextShift", _ids.NewId()),
            P("@SessionId", current.CashSessionId), P("@RegisterId", current.RegisterId));
    }

    private static async Task LockRegisterAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid registerId,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT RegisterId
            FROM dbo.CashRegisters WITH (UPDLOCK,HOLDLOCK)
            WHERE RegisterId=@RegisterId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@RegisterId", registerId);
        if (await command.ExecuteScalarAsync(ct) is not Guid)
            throw new CashNotFoundException("La caja no existe.");
    }

    private static async Task<Dictionary<string, decimal>> ExpectedAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid sessionId,
        Guid? shiftId,
        bool includeOpeningFloat,
        CancellationToken ct)
    {
        const string sql = """
            SELECT PaymentMethodCode,SUM(Amount)
            FROM dbo.CashMovements
            WHERE CashSessionId=@SessionId AND (@ShiftId IS NULL OR CashierShiftId=@ShiftId)
            GROUP BY PaymentMethodCode;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange([P("@SessionId", sessionId), P("@ShiftId", Db(shiftId))]);
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result[reader.GetString(0)] = reader.GetDecimal(1);
        await reader.DisposeAsync();
        if (includeOpeningFloat)
        {
            await using var opening = new SqlCommand(
                "SELECT OpeningFloat FROM dbo.CashSessions WHERE CashSessionId=@SessionId;",
                connection, transaction);
            opening.Parameters.AddWithValue("@SessionId", sessionId);
            var value = Convert.ToDecimal(await opening.ExecuteScalarAsync(ct));
            result["Cash"] = result.GetValueOrDefault("Cash") + value;
        }
        return result;
    }

    private static async Task InsertCountLinesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid countId,
        IReadOnlyList<CashReconciliationLine> lines,
        CancellationToken ct)
    {
        foreach (var line in lines)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT dbo.CashCountLines
                    (CashCountId,PaymentMethodCode,ExpectedAmount,CountedAmount)
                VALUES (@CountId,@Method,@Expected,@Counted);
                """, ct,
                P("@CountId", countId), P("@Method", line.PaymentMethodCode),
                Money("@Expected", line.ExpectedAmount), Money("@Counted", line.CountedAmount));
        }
    }

    private static void RequireDifferenceReason(
        IReadOnlyList<CashReconciliationLine> lines, string? reason)
    {
        if (lines.Any(line => line.DifferenceAmount != 0m) && string.IsNullOrWhiteSpace(reason))
            throw new CashValidationException("Explica la diferencia antes de confirmar el conteo.");
    }

    private static string NormalizeRegisterCode(string value)
    {
        var normalized = new string(value.Trim().ToUpperInvariant()
            .Where(char.IsLetterOrDigit).ToArray());
        if (normalized.Length == 0) throw new CashValidationException("La caja no tiene un código válido.");
        return normalized.Length <= 8 ? normalized : normalized[..8];
    }

    private async Task<long> NextCountNumberAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid registerId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        const string sql = """
            DECLARE @Value BIGINT;
            SELECT @Value=NextConsecutive
            FROM dbo.CashCountNumberCursors WITH (UPDLOCK,HOLDLOCK)
            WHERE RegisterId=@RegisterId;
            IF @Value IS NULL
            BEGIN
                SET @Value=1;
                INSERT dbo.CashCountNumberCursors(RegisterId,NextConsecutive,UpdatedAt)
                VALUES(@RegisterId,2,@Now);
            END
            ELSE
                UPDATE dbo.CashCountNumberCursors
                SET NextConsecutive=@Value+1,UpdatedAt=@Now
                WHERE RegisterId=@RegisterId;
            SELECT @Value;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange([P("@RegisterId", registerId), P("@Now", now)]);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    private static async Task<RegisterInfo> ValidateRegisterAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CashUserIdentity actor,
        Guid registerId,
        Guid? businessId,
        Guid? locationId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT r.BusinessId,r.LocationId,r.Code,r.Name,b.Name,l.Name,s.OpenedByUserId
            FROM dbo.CashRegisters r
            INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId AND b.IsActive=1
            INNER JOIN dbo.BusinessLocations l ON l.LocationId=r.LocationId AND l.IsActive=1
            OUTER APPLY (
                SELECT TOP(1) cs.OpenedByUserId
                FROM dbo.CashSessions cs
                WHERE cs.RegisterId=r.RegisterId AND cs.Status=N'Open'
            ) s
            WHERE r.RegisterId=@RegisterId AND r.IsActive=1 AND b.TenantId=@TenantId
              AND (@BusinessId IS NULL OR r.BusinessId=@BusinessId)
              AND (@LocationId IS NULL OR r.LocationId=@LocationId);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange([
            P("@RegisterId", registerId), P("@TenantId", actor.TenantId),
            P("@BusinessId", Db(businessId)), P("@LocationId", Db(locationId))
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new CashForbiddenException("La caja no pertenece al contexto autenticado o está inactiva.");
        return new RegisterInfo(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5),
            reader.IsDBNull(6) ? actor.UserId : reader.GetGuid(6));
    }

    private static async Task ValidateReceiverAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        Guid userId,
        CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.AppUsers WHERE TenantId=@TenantId AND UserId=@UserId AND IsActive=1;",
            connection, transaction);
        command.Parameters.AddRange([P("@TenantId", tenantId), P("@UserId", userId)]);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 1)
            throw new CashValidationException("El usuario que recibe la caja no existe o está inactivo.");
    }

    private static async Task<CashHandoffResult?> FindHandoffAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid registerId,
        string key,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT TOP(1) c.CashCountId,c.ReceivedByUserId
            FROM dbo.CashCounts c WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.CashSessions s ON s.CashSessionId=c.CashSessionId
            WHERE s.RegisterId=@RegisterId AND s.Status=N'Open'
              AND c.IdempotencyKey=@Key
              AND c.CountType=N'Handoff';
            """, connection, transaction);
        command.Parameters.AddRange([P("@RegisterId", registerId), P("@Key", key.Trim())]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var countId = reader.GetGuid(0);
        var receivedByUserId = reader.GetGuid(1);
        await reader.DisposeAsync();
        var current = await CurrentAsync(
                connection, transaction, registerId, receivedByUserId, ct)
            ?? throw new CashConflictException("El relevo ya fue procesado, pero la caja no está abierta.");
        var lines = await CountLinesAsync(connection, transaction, countId, ct);
        return new CashHandoffResult(countId, current, lines);
    }

    private static async Task<CashClosureReceipt?> ReceiptByKeyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid sessionId,
        string key,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT ReceiptSnapshotJson FROM dbo.CashCounts WITH (UPDLOCK,HOLDLOCK)
            WHERE CashSessionId=@SessionId AND IdempotencyKey=@Key AND CountType=N'Final';
            """, connection, transaction);
        command.Parameters.AddRange([P("@SessionId", sessionId), P("@Key", key.Trim())]);
        var value = await command.ExecuteScalarAsync(ct);
        return value is string json ? JsonSerializer.Deserialize<CashClosureReceipt>(json, Json) : null;
    }

    private static async Task<Guid> RegisterIdAsync(
        SqlConnection connection, SqlTransaction transaction, Guid sessionId, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT RegisterId FROM dbo.CashSessions WHERE CashSessionId=@SessionId;",
            connection, transaction);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        return (Guid)(await command.ExecuteScalarAsync(ct)
            ?? throw new CashNotFoundException("La sesión de caja no existe."));
    }

    private static async Task<IReadOnlyList<CashReconciliationLine>> CountLinesAsync(
        SqlConnection connection, SqlTransaction transaction, Guid countId, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT PaymentMethodCode,ExpectedAmount,CountedAmount,DifferenceAmount
            FROM dbo.CashCountLines WHERE CashCountId=@CountId ORDER BY PaymentMethodCode;
            """, connection, transaction);
        command.Parameters.AddWithValue("@CountId", countId);
        var rows = new List<CashReconciliationLine>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new(reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3)));
        return rows;
    }

    private static async Task<Dictionary<string, decimal>> DailyPaymentsAsync(
        SqlConnection connection, Guid registerId, DateOnly date, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT m.PaymentMethodCode,SUM(m.Amount)
            FROM dbo.CashMovements m
            INNER JOIN dbo.CashSessions s ON s.CashSessionId=m.CashSessionId
            WHERE s.RegisterId=@RegisterId AND m.BusinessDate=@Date
            GROUP BY m.PaymentMethodCode;
            """, connection);
        command.Parameters.AddRange([
            P("@RegisterId", registerId),
            new SqlParameter("@Date", SqlDbType.Date) { Value = date.ToDateTime(TimeOnly.MinValue) }
        ]);
        var rows = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows[reader.GetString(0)] = reader.GetDecimal(1);
        return rows;
    }

    private static async Task<DocumentSummary> DocumentSummaryAsync(
        SqlConnection connection, SqlTransaction transaction, Guid sessionId, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT COUNT_BIG(1),
                   COALESCE(SUM(x.Gross),0),COALESCE(SUM(x.Discounts),0),
                   COALESCE(SUM(d.PayableAmount),0),
                   (SELECT TOP(1) DocumentNumber FROM dbo.SalesDocuments WHERE CashSessionId=@SessionId ORDER BY IssuedAt,DocumentId),
                   (SELECT TOP(1) DocumentNumber FROM dbo.SalesDocuments WHERE CashSessionId=@SessionId ORDER BY IssuedAt DESC,DocumentId DESC),
                   (SELECT TOP(1) FiscalNumber FROM dbo.SalesDocuments WHERE CashSessionId=@SessionId ORDER BY IssuedAt,DocumentId),
                   (SELECT TOP(1) FiscalNumber FROM dbo.SalesDocuments WHERE CashSessionId=@SessionId ORDER BY IssuedAt DESC,DocumentId DESC)
            FROM dbo.SalesDocuments d
            OUTER APPLY (
                SELECT SUM(l.Quantity*l.UnitPrice) Gross,SUM(l.DiscountAmount) Discounts
                FROM dbo.SalesDocumentLines l WHERE l.DocumentId=d.DocumentId
            ) x
            WHERE d.CashSessionId=@SessionId AND d.ProcessingStatus=N'Completed';
            """, connection, transaction);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new(
            checked((int)reader.GetInt64(0)), reader.GetDecimal(1), reader.GetDecimal(2),
            reader.GetDecimal(3), NullableString(reader, 4), NullableString(reader, 5),
            NullableString(reader, 6), NullableString(reader, 7));
    }

    private static async Task<IReadOnlyList<CashierReceiptSummary>> CashierSummaryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid sessionId,
        DateTimeOffset closedAt,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT sh.UserId,CONCAT(u.FirstName,N' ',u.LastName),sh.StartedAt,
                   COALESCE(sh.EndedAt,@ClosedAt),COUNT(DISTINCT d.DocumentId),
                   COALESCE(SUM(d.PayableAmount),0)
            FROM dbo.CashierShifts sh
            INNER JOIN dbo.AppUsers u ON u.UserId=sh.UserId
            LEFT JOIN dbo.SalesDocuments d ON d.CashierShiftId=sh.CashierShiftId
                AND d.ProcessingStatus=N'Completed'
            WHERE sh.CashSessionId=@SessionId
            GROUP BY sh.CashierShiftId,sh.UserId,u.FirstName,u.LastName,sh.StartedAt,sh.EndedAt
            ORDER BY sh.StartedAt;
            """, connection, transaction);
        command.Parameters.AddRange([P("@SessionId", sessionId), P("@ClosedAt", closedAt)]);
        var rows = new List<CashierReceiptSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetDateTimeOffset(2),
                reader.GetDateTimeOffset(3), reader.GetInt32(4), reader.GetDecimal(5)));
        return rows;
    }

    private static async Task<IReadOnlyList<CashTaxReceiptSummary>> TaxSummaryAsync(
        SqlConnection connection, SqlTransaction transaction, Guid sessionId, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT t.TaxCode,t.TaxRate,SUM(t.TaxableAmount),SUM(t.TaxAmount)
            FROM dbo.SalesDocumentTaxSummaries t
            INNER JOIN dbo.SalesDocuments d ON d.DocumentId=t.DocumentId
            WHERE d.CashSessionId=@SessionId AND d.ProcessingStatus=N'Completed'
            GROUP BY t.TaxCode,t.TaxRate ORDER BY t.TaxCode,t.TaxRate;
            """, connection, transaction);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        var rows = new List<CashTaxReceiptSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new(reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3)));
        return rows;
    }

    private static async Task<IReadOnlyList<CashDailyReceiptSummary>> DaySummaryAsync(
        SqlConnection connection, SqlTransaction transaction, Guid sessionId, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT BusinessDate,COUNT(DISTINCT DocumentId),
                   SUM(CASE WHEN MovementType=N'SalePayment' THEN Amount ELSE 0 END)
            FROM dbo.CashMovements WHERE CashSessionId=@SessionId
            GROUP BY BusinessDate ORDER BY BusinessDate;
            """, connection, transaction);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        var rows = new List<CashDailyReceiptSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new(DateOnly.FromDateTime(reader.GetDateTime(0)), reader.GetInt32(1), reader.GetDecimal(2)));
        return rows;
    }

    private static async Task<(decimal CashIn, decimal CashOut)> MovementSummaryAsync(
        SqlConnection connection, SqlTransaction transaction, Guid sessionId, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT COALESCE(SUM(CASE WHEN MovementType=N'CashIn' THEN Amount ELSE 0 END),0),
                   COALESCE(SUM(CASE WHEN MovementType=N'CashOut' THEN -Amount ELSE 0 END),0)
            FROM dbo.CashMovements WHERE CashSessionId=@SessionId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (reader.GetDecimal(0), reader.GetDecimal(1));
    }

    private static async Task<string> UserNameAsync(
        SqlConnection connection, SqlTransaction transaction, Guid userId, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT CONCAT(FirstName,N' ',LastName) FROM dbo.AppUsers WHERE UserId=@UserId;",
            connection, transaction);
        command.Parameters.AddWithValue("@UserId", userId);
        return Convert.ToString(await command.ExecuteScalarAsync(ct)) ?? userId.ToString("D");
    }

    private static async Task ExecuteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        CancellationToken ct,
        params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static SqlParameter P(string name, object? value) =>
        new(name, value ?? DBNull.Value);

    private static SqlParameter Money(string name, decimal value) =>
        new(name, SqlDbType.Decimal) { Precision = 19, Scale = 4, Value = value };

    private static SqlParameter Binary(string name, byte[] value) =>
        new(name, SqlDbType.Binary, 32) { Value = value };

    private static object Db(object? value) => value ?? DBNull.Value;
    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NullableString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private sealed record RegisterInfo(
        Guid BusinessId,
        Guid LocationId,
        string RegisterCode,
        string RegisterName,
        string BusinessName,
        string LocationName,
        Guid OpenedByUserId);

    private sealed record DocumentSummary(
        int Count,
        decimal Gross,
        decimal Discounts,
        decimal Net,
        string? FirstDocument,
        string? LastDocument,
        string? FirstFiscal,
        string? LastFiscal);
}
