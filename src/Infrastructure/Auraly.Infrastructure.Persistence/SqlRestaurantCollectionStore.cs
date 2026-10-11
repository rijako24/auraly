using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlRestaurantCollectionStore(SqlServerConnectionFactory connections)
    : IRestaurantCollectionStore
{
    public async Task<RestaurantCollectionSummary> GetAsync(
        RestaurantAccountActor actor, Guid accountId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        return await LoadSummaryAsync(connection, null, actor, accountId, ct);
    }

    public async Task<RestaurantCollectionSummary> CaptureAsync(
        RestaurantAccountActor actor, Guid accountId,
        CaptureRestaurantCollectionRequest request, string idempotencyKey,
        DateTimeOffset now, CancellationToken ct)
    {
        var allocations = request.Allocations.OrderBy(line => line.AdditionId).ToArray();
        var allocationJson = JsonSerializer.Serialize(allocations);
        var paymentsJson = JsonSerializer.Serialize(request.Payments);
        var tipPaymentsJson = JsonSerializer.Serialize(request.TipPayment is null
            ? [] : new[] { request.TipPayment });
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            accountId, request.WorkSessionId, request.ExpectedAccountVersion,
            allocations, request.Payments, request.TipPayment
        })));
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using (var replay = new SqlCommand("""
                SELECT AccountId,RequestHash
                FROM sales.RestaurantAccountCollections WITH(UPDLOCK,HOLDLOCK)
                WHERE BusinessId=@BusinessId AND IdempotencyKey=@Key;
                """, connection, transaction))
            {
                Scope(replay, actor, accountId);
                replay.Parameters.AddWithValue("@Key", idempotencyKey);
                await using var reader = await replay.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    if (reader.GetGuid(0) != accountId ||
                        !hash.SequenceEqual((byte[])reader[1]))
                        throw new RestaurantAccountConflictException(
                            "La identidad de cobro ya pertenece a otra operación.");
                    await reader.DisposeAsync();
                    await transaction.CommitAsync(ct);
                    return await GetAsync(actor, accountId, ct);
                }
            }

            await using (var guard = new SqlCommand("""
                SELECT account.Version,business.RestaurantSuggestedTipPercent,
                       (SELECT TOP(1) prior.WorkSessionId
                        FROM sales.RestaurantAccountCollections prior
                        WHERE prior.AccountId=account.AccountId
                          AND prior.Status=N'Captured'
                        ORDER BY prior.CapturedAt,prior.CollectionId)
                FROM sales.RestaurantAccounts account WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.Businesses business
                  ON business.BusinessId=account.BusinessId
                 AND business.TenantId=account.TenantId
                 AND business.IsActive=1
                 AND business.PointOfSaleMode=N'RestaurantBar'
                JOIN dbo.WorkSessions sessionRow
                  ON sessionRow.WorkSessionId=@WorkSessionId
                 AND sessionRow.BusinessId=account.BusinessId
                 AND sessionRow.TenantId=account.TenantId
                 AND sessionRow.UserId=@UserId
                 AND sessionRow.Status=N'Open'
                 AND sessionRow.DeviceId IS NULL
                WHERE account.AccountId=@AccountId
                  AND account.BusinessId=@BusinessId
                  AND account.TenantId=@TenantId
                  AND account.Status=N'Open'
                  AND NOT EXISTS(
                    SELECT 1 FROM sales.RestaurantAccountAdditions pending
                    WHERE pending.AccountId=account.AccountId
                      AND pending.Status=N'Pending')
                  AND NOT EXISTS(
                    SELECT 1 FROM sales.RestaurantCancellationRequests cancellation
                    WHERE cancellation.AccountId=account.AccountId
                      AND cancellation.Status IN(N'Requested',N'Confirmed'));
                """, connection, transaction))
            {
                Scope(guard, actor, accountId);
                guard.Parameters.AddWithValue("@WorkSessionId", request.WorkSessionId);
                guard.Parameters.AddWithValue("@UserId", actor.UserId);
                await using var reader = await guard.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    throw new RestaurantAccountConflictException(
                        "La mesa, la caja o sus comandas ya no están disponibles para cobrar.");
                if (reader.GetInt64(0) != request.ExpectedAccountVersion)
                    throw new RestaurantAccountConflictException(
                        "La cuenta cambió. Revisa el saldo antes de cobrar.");
                if (request.TipPayment is not null && reader.GetDecimal(1) <= 0)
                    throw new RestaurantAccountValidationException(
                        "Esta sede no tiene habilitada la propina sugerida.");
                if (!reader.IsDBNull(2) && reader.GetGuid(2) != request.WorkSessionId)
                    throw new RestaurantAccountConflictException(
                        "Los cobros de esta mesa pertenecen a otra sesión de caja.");
            }

            await using (var methodLimit = new SqlCommand("""
                SELECT COUNT(*)
                FROM sales.RestaurantAccountCollections collection
                CROSS APPLY OPENJSON(collection.PaymentsJson) WITH(
                    MethodCode nvarchar(32) '$.MethodCode') payment
                WHERE collection.AccountId=@AccountId
                  AND collection.BusinessId=@BusinessId
                  AND collection.TenantId=@TenantId
                  AND collection.Status=N'Captured'
                  AND payment.MethodCode<>N'Cash';
                """, connection, transaction))
            {
                Scope(methodLimit, actor, accountId);
                var existingNonCash = (int)(await methodLimit.ExecuteScalarAsync(ct) ?? 0);
                var nextNonCash = request.Payments.Count(payment => payment.MethodCode != "Cash");
                if (existingNonCash + nextNonCash > 9)
                    throw new RestaurantAccountValidationException(
                        "Esta mesa alcanzó el límite de medios de pago de la factura.");
            }

            var amount = 0m;
            await using (var lines = new SqlCommand("""
                SELECT requested.AdditionId,requested.Quantity,
                       addition.Quantity,addition.PublicUnitPrice,
                       addition.PromotionDiscountPerUnit,
                       COALESCE(covered.Quantity,0)
                FROM OPENJSON(@Allocations) WITH(
                    AdditionId uniqueidentifier '$.AdditionId',
                    Quantity decimal(18,4) '$.Quantity') requested
                JOIN sales.RestaurantAccountAdditions addition WITH(UPDLOCK,HOLDLOCK)
                  ON addition.AdditionId=requested.AdditionId
                 AND addition.AccountId=@AccountId
                 AND addition.BusinessId=@BusinessId
                 AND addition.TenantId=@TenantId
                 AND addition.Status IN(N'Sent',N'Direct')
                OUTER APPLY(
                  SELECT SUM(allocation.Quantity) Quantity
                  FROM sales.RestaurantCollectionAllocations allocation
                  JOIN sales.RestaurantAccountCollections prior
                    ON prior.CollectionId=allocation.CollectionId
                   AND prior.AccountId=allocation.AccountId
                   AND prior.Status=N'Captured'
                  WHERE allocation.AccountId=addition.AccountId
                    AND allocation.AdditionId=addition.AdditionId
                ) covered;
                """, connection, transaction))
            {
                Scope(lines, actor, accountId);
                lines.Parameters.Add("@Allocations", SqlDbType.NVarChar, -1)
                    .Value = allocationJson;
                var count = 0;
                await using var reader = await lines.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    count++;
                    var quantity = reader.GetDecimal(1);
                    if (quantity > reader.GetDecimal(2) - reader.GetDecimal(5))
                        throw new RestaurantAccountConflictException(
                            "Una cantidad ya fue cubierta por otro cobro.");
                    amount += quantity * (reader.GetDecimal(3) - reader.GetDecimal(4));
                }
                if (count != allocations.Length)
                    throw new RestaurantAccountConflictException(
                        "Los productos elegidos cambiaron o ya no están enviados.");
            }
            amount = decimal.Round(amount, 4);
            if ((amount <= 0 && request.TipPayment is null) ||
                (amount == 0 && allocations.Length != 0) ||
                request.Payments.Sum(payment => payment.CollectedAmount) != amount)
                throw new RestaurantAccountValidationException(
                    "El valor recibido debe coincidir con los productos seleccionados.");

            await using (var insert = new SqlCommand("""
                INSERT sales.RestaurantAccountCollections
                  (CollectionId,TenantId,BusinessId,AccountId,WorkSessionId,
                   CashierUserId,IdempotencyKey,RequestHash,PaymentsJson,
                   Amount,TipAmount,TipPaymentsJson,CapturedAt)
                VALUES(@CollectionId,@TenantId,@BusinessId,@AccountId,
                       @WorkSessionId,@UserId,@Key,@Hash,@Payments,
                       @Amount,@TipAmount,@TipPayments,@Now);
                INSERT sales.RestaurantCollectionAllocations
                  (CollectionId,AccountId,AdditionId,Quantity)
                SELECT @CollectionId,@AccountId,AdditionId,Quantity
                FROM OPENJSON(@Allocations) WITH(
                    AdditionId uniqueidentifier '$.AdditionId',
                    Quantity decimal(18,4) '$.Quantity');
                UPDATE sales.RestaurantAccounts
                SET Version=Version+1,UpdatedAt=@Now
                WHERE AccountId=@AccountId AND BusinessId=@BusinessId
                  AND TenantId=@TenantId AND Version=@Version AND Status=N'Open';
                IF @@ROWCOUNT<>1
                  THROW 51130,N'La cuenta cambió durante el cobro.',1;
                INSERT dbo.AuditLogs
                  (AuditLogId,UserId,TenantId,BusinessId,Action,EntityType,
                   EntityId,NewValues,CorrelationId,Timestamp)
                VALUES(NEWID(),@UserId,@TenantId,@BusinessId,
                       N'RestaurantCollectionCaptured',N'RestaurantAccount',
                       CONVERT(nvarchar(100),@AccountId),@Audit,
                       CONVERT(nvarchar(100),@CollectionId),@AuditTime);
                """, connection, transaction))
            {
                Scope(insert, actor, accountId);
                insert.Parameters.AddWithValue("@CollectionId", request.CollectionId);
                insert.Parameters.AddWithValue("@WorkSessionId", request.WorkSessionId);
                insert.Parameters.AddWithValue("@UserId", actor.UserId);
                insert.Parameters.AddWithValue("@Key", idempotencyKey);
                insert.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = hash;
                insert.Parameters.Add("@Payments", SqlDbType.NVarChar, -1).Value = paymentsJson;
                insert.Parameters.Add("@TipPayments", SqlDbType.NVarChar, -1).Value = tipPaymentsJson;
                insert.Parameters.Add("@Allocations", SqlDbType.NVarChar, -1).Value = allocationJson;
                insert.Parameters.AddWithValue("@Amount", amount);
                insert.Parameters.AddWithValue("@TipAmount", request.TipPayment?.Amount ?? 0m);
                insert.Parameters.AddWithValue("@Now", now);
                insert.Parameters.AddWithValue("@Version", request.ExpectedAccountVersion);
                insert.Parameters.Add("@Audit", SqlDbType.NVarChar, -1).Value =
                    JsonSerializer.Serialize(new { request.CollectionId, amount,
                        TipAmount = request.TipPayment?.Amount ?? 0m,
                        request.WorkSessionId, allocations });
                insert.Parameters.AddWithValue("@AuditTime", now.UtcDateTime);
                await insert.ExecuteNonQueryAsync(ct);
            }
            if (request.TipPayment is { } tipPayment)
                await QueueTipAccountingAsync(connection, transaction, actor,
                    accountId, request.CollectionId, request.WorkSessionId,
                    tipPayment, false, now, ct);
            await transaction.CommitAsync(ct);
            return await GetAsync(actor, accountId, ct);
        }
        catch (SqlException error) when (error.Number is 2601 or 2627 or 51130)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new RestaurantAccountConflictException(
                "El cobro ya existe o la cuenta cambió. Actualiza la mesa antes de reintentar.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<RestaurantCollectionSummary> ReverseAsync(
        RestaurantAccountActor actor, Guid accountId, Guid collectionId,
        long expectedVersion, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        OnlineSalesPayment? reversedTip = null;
        Guid tipWorkSessionId = Guid.Empty;
        await using (var tip = new SqlCommand("""
            SELECT WorkSessionId,TipPaymentsJson
            FROM sales.RestaurantAccountCollections WITH(UPDLOCK,HOLDLOCK)
            WHERE CollectionId=@CollectionId AND AccountId=@AccountId
              AND BusinessId=@BusinessId AND TenantId=@TenantId
              AND Status=N'Captured';
            """, connection, transaction))
        {
            Scope(tip, actor, accountId);
            tip.Parameters.AddWithValue("@CollectionId", collectionId);
            await using var reader = await tip.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                tipWorkSessionId = reader.GetGuid(0);
                reversedTip = JsonSerializer.Deserialize<OnlineSalesPayment[]>(
                    reader.GetString(1))?.SingleOrDefault();
            }
        }
        await using var command = new SqlCommand("""
            UPDATE account SET Version=Version+1,UpdatedAt=@Now
            FROM sales.RestaurantAccounts account WITH(UPDLOCK,HOLDLOCK)
            JOIN sales.RestaurantAccountCollections collection
              ON collection.AccountId=account.AccountId
             AND collection.BusinessId=account.BusinessId
             AND collection.CollectionId=@CollectionId
             AND collection.Status=N'Captured'
            JOIN dbo.WorkSessions sessionRow
              ON sessionRow.WorkSessionId=collection.WorkSessionId
             AND sessionRow.Status=N'Open'
             AND sessionRow.UserId=@UserId
            WHERE account.AccountId=@AccountId
              AND account.BusinessId=@BusinessId
              AND account.TenantId=@TenantId
              AND account.Status=N'Open'
              AND account.Version=@Version;
            IF @@ROWCOUNT<>1
              THROW 51131,N'El cobro ya cambió o su caja no está abierta.',1;
            UPDATE sales.RestaurantAccountCollections
            SET Status=N'Reversed',ReversedAt=@Now,ReversedByUserId=@UserId
            WHERE CollectionId=@CollectionId AND AccountId=@AccountId
              AND BusinessId=@BusinessId AND TenantId=@TenantId
              AND Status=N'Captured';
            INSERT dbo.AuditLogs
              (AuditLogId,UserId,TenantId,BusinessId,Action,EntityType,
               EntityId,NewValues,CorrelationId,Timestamp)
            VALUES(NEWID(),@UserId,@TenantId,@BusinessId,
                   N'RestaurantCollectionReversed',N'RestaurantAccount',
                   CONVERT(nvarchar(100),@AccountId),
                   CONVERT(nvarchar(100),@CollectionId),
                   CONVERT(nvarchar(100),@CollectionId),@AuditTime);
            """, connection, transaction);
        Scope(command, actor, accountId);
        command.Parameters.AddWithValue("@CollectionId", collectionId);
        command.Parameters.AddWithValue("@UserId", actor.UserId);
        command.Parameters.AddWithValue("@Version", expectedVersion);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@AuditTime", now.UtcDateTime);
        try
        {
            await command.ExecuteNonQueryAsync(ct);
            if (reversedTip is not null)
                await QueueTipAccountingAsync(connection, transaction, actor,
                    accountId, collectionId, tipWorkSessionId,
                    reversedTip, true, now, ct);
            await transaction.CommitAsync(ct);
            return await GetAsync(actor, accountId, ct);
        }
        catch (SqlException error) when (error.Number == 51131)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new RestaurantAccountConflictException(error.Message);
        }
    }

    private static async Task<RestaurantCollectionSummary> LoadSummaryAsync(
        SqlConnection connection, SqlTransaction? transaction,
        RestaurantAccountActor actor, Guid accountId, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT account.Version,
                   COALESCE(SUM(CASE WHEN addition.Status<>N'Cancelled'
                     THEN addition.Quantity*(addition.PublicUnitPrice-
                       addition.PromotionDiscountPerUnit) ELSE 0 END),0),
                   business.RestaurantSuggestedTipPercent
            FROM sales.RestaurantAccounts account
            JOIN dbo.Businesses business
              ON business.BusinessId=account.BusinessId
             AND business.TenantId=account.TenantId
            LEFT JOIN sales.RestaurantAccountAdditions addition
              ON addition.AccountId=account.AccountId
            WHERE account.AccountId=@AccountId
              AND account.BusinessId=@BusinessId
              AND account.TenantId=@TenantId
            GROUP BY account.Version,business.RestaurantSuggestedTipPercent;
            SELECT CollectionId,WorkSessionId,CashierUserId,Amount,
                   CapturedAt,PaymentsJson,TipAmount,TipPaymentsJson
            FROM sales.RestaurantAccountCollections
            WHERE AccountId=@AccountId AND BusinessId=@BusinessId
              AND TenantId=@TenantId AND Status=N'Captured'
            ORDER BY CapturedAt,CollectionId;
            SELECT allocation.CollectionId,allocation.AdditionId,
                   allocation.Quantity
            FROM sales.RestaurantCollectionAllocations allocation
            JOIN sales.RestaurantAccountCollections collection
              ON collection.CollectionId=allocation.CollectionId
             AND collection.AccountId=allocation.AccountId
             AND collection.Status=N'Captured'
            WHERE allocation.AccountId=@AccountId
              AND collection.BusinessId=@BusinessId
              AND collection.TenantId=@TenantId;
            """, connection, transaction);
        Scope(command, actor, accountId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new RestaurantAccountConflictException(
                "La cuenta no está disponible en esta sede.");
        var version = reader.GetInt64(0);
        var total = reader.GetDecimal(1);
        var suggestedTipPercent = reader.GetDecimal(2);
        await reader.NextResultAsync(ct);
        var rows = new List<(Guid Id, Guid Session, Guid Cashier, decimal Amount,
            DateTimeOffset At, IReadOnlyList<OnlineSalesPayment> Payments,
            OnlineSalesPayment? TipPayment)>();
        while (await reader.ReadAsync(ct))
            rows.Add((reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),
                reader.GetDecimal(3),reader.GetDateTimeOffset(4),
                JsonSerializer.Deserialize<OnlineSalesPayment[]>(reader.GetString(5)) ?? [],
                JsonSerializer.Deserialize<OnlineSalesPayment[]>(reader.GetString(7))?.SingleOrDefault()));
        await reader.NextResultAsync(ct);
        var allocationMap = new Dictionary<Guid,List<RestaurantCollectionAllocation>>();
        var covered = new Dictionary<Guid,decimal>();
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            var line = new RestaurantCollectionAllocation(reader.GetGuid(1),
                reader.GetDecimal(2));
            if (!allocationMap.TryGetValue(id, out var list))
                allocationMap[id] = list = [];
            list.Add(line);
            covered[line.AdditionId] = covered.GetValueOrDefault(line.AdditionId) +
                line.Quantity;
        }
        var collections = rows.Select(row => new RestaurantCollection(
            row.Id, accountId, row.Session, row.Cashier, row.Amount, row.At,
            allocationMap.GetValueOrDefault(row.Id) ?? [],row.Payments,
            row.TipPayment)).ToArray();
        var collected = collections.Sum(row => row.Amount);
        return new(accountId,version,total,collected,
            Math.Max(0,total-collected),collections,
            covered.Select(item => new RestaurantCollectionAllocation(
                item.Key,item.Value)).ToArray(),
            collections.Sum(item => item.TipPayment?.Amount ?? 0m),
            suggestedTipPercent);
    }

    private static void Scope(SqlCommand command,
        RestaurantAccountActor actor, Guid accountId)
    {
        command.Parameters.AddWithValue("@TenantId", actor.TenantId);
        command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
        command.Parameters.AddWithValue("@AccountId", accountId);
    }

    private static async Task QueueTipAccountingAsync(
        SqlConnection connection, SqlTransaction transaction,
        RestaurantAccountActor actor, Guid accountId, Guid collectionId,
        Guid workSessionId, OnlineSalesPayment payment, bool isReversal,
        DateTimeOffset now, CancellationToken ct)
    {
        var documentType = isReversal ? "RestaurantTipReversal" : "RestaurantTip";
        var payload = JsonSerializer.Serialize(new RestaurantTipAccountingPayload(
            collectionId, accountId, actor.TenantId, actor.BusinessId,
            workSessionId, payment, isReversal));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        await using var command = new SqlCommand("""
            IF EXISTS(SELECT 1 FROM dbo.AccountingTenantSettings settings
                      WHERE settings.TenantId=@TenantId
                        AND settings.Status=N'Ready'
                        AND settings.EffectiveFrom<=CONVERT(date,@Now))
            BEGIN
                INSERT dbo.AccountingSourceDocuments
                  (SourceDocumentId,SourceDocumentType,TenantId,BusinessId,
                   PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
                VALUES(@CollectionId,@DocumentType,@TenantId,@BusinessId,
                       @Payload,@Hash,@Now,@Now,1);
                INSERT dbo.AccountingPostingJobs
                  (AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
                   SourceDocumentType,SourcePayloadHash,OccurredAt,Status,
                   AttemptCount,CreatedAt)
                VALUES(NEWID(),@TenantId,@BusinessId,@CollectionId,
                       @DocumentType,@Hash,@Now,N'Pending',0,@Now);
            END;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", actor.TenantId);
        command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
        command.Parameters.AddWithValue("@CollectionId", collectionId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = payload;
        command.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = hash;
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(ct);
    }
}
