using System.Data;
using System.Text.Json;
using Auraly.Application.Orders;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Orders;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlOrderBatchStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider time) : IOrderBatchStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<OrderBatchLease> BeginAsync(
        OrderActor actor,
        InvoiceOrdersRequest request,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = """
            SELECT OperationId,RequestHash,Status,ResultJson,LeaseExpiresAt
            FROM dbo.OrderInvoiceBatchReceipts WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND IdempotencyKey=@IdempotencyKey;
            """;
        read.Parameters.AddRange([
            P("@BusinessId", actor.BusinessId),
            P("@IdempotencyKey", idempotencyKey)
        ]);
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var operationId = reader.GetGuid(0);
            if (!string.Equals(reader.GetString(1), requestHash, StringComparison.Ordinal))
                throw new OrderConflictException(
                    "La clave idempotente ya fue usada con otra selección de pedidos.");
            var status = reader.GetString(2);
            var resultJson = reader.IsDBNull(3) ? null : reader.GetString(3);
            var leaseExpiresAt = reader.GetDateTimeOffset(4);
            await reader.DisposeAsync();
            if (status != "Processing")
            {
                var replay = resultJson is null
                    ? throw new OrderConflictException(
                        "El lote finalizado no conserva una respuesta válida.")
                    : JsonSerializer.Deserialize<InvoiceOrdersResponse>(
                        resultJson,
                        JsonOptions)
                      ?? throw new OrderConflictException(
                          "La respuesta durable del lote no es válida.");
                await transaction.CommitAsync(cancellationToken);
                return new(operationId, Guid.Empty, replay);
            }
            if (leaseExpiresAt > time.GetUtcNow())
                throw new OrderConflictException(
                    "La facturación seleccionada ya se está procesando.");

            var renewedToken = ids.NewId();
            await ExecuteAsync(connection, transaction, """
                UPDATE dbo.OrderInvoiceBatchReceipts
                SET LeaseToken=@LeaseToken,LeaseExpiresAt=@LeaseExpiresAt,UpdatedAt=@Now
                WHERE OperationId=@OperationId;
                """,
                [
                    P("@LeaseToken", renewedToken),
                    P("@LeaseExpiresAt", time.GetUtcNow().AddMinutes(5)),
                    P("@Now", time.GetUtcNow()),
                    P("@OperationId", operationId)
                ],
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(operationId, renewedToken, null);
        }
        await reader.DisposeAsync();

        var newOperationId = ids.NewId();
        var leaseToken = ids.NewId();
        var now = time.GetUtcNow();
        await ExecuteAsync(connection, transaction, """
            INSERT dbo.OrderInvoiceBatchReceipts(
              OperationId,BusinessId,RegisterId,UserId,IdempotencyKey,RequestHash,
              Status,RequestedCount,CompletedCount,FailedCount,ResultJson,
              LeaseToken,LeaseExpiresAt,CreatedAt,UpdatedAt)
            VALUES(
              @OperationId,@BusinessId,@RegisterId,@UserId,@IdempotencyKey,@RequestHash,
              N'Processing',@RequestedCount,0,0,NULL,
              @LeaseToken,@LeaseExpiresAt,@Now,@Now);
            """,
            [
                P("@OperationId", newOperationId),
                P("@BusinessId", actor.BusinessId),
                P("@RegisterId", request.RegisterId),
                P("@UserId", actor.UserId),
                P("@IdempotencyKey", idempotencyKey),
                P("@RequestHash", requestHash),
                P("@RequestedCount", request.OrderIds.Count),
                P("@LeaseToken", leaseToken),
                P("@LeaseExpiresAt", now.AddMinutes(5)),
                P("@Now", now)
            ],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(newOperationId, leaseToken, null);
    }

    public async Task SaveProgressAsync(
        OrderActor actor,
        Guid operationId,
        Guid leaseToken,
        InvoiceOrdersResponse response,
        bool completed,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.OrderInvoiceBatchReceipts
            SET Status=CASE WHEN @Completed=1 THEN @Status ELSE N'Processing' END,
                CompletedCount=@CompletedCount,
                FailedCount=@FailedCount,
                ResultJson=@ResultJson,
                LeaseExpiresAt=CASE WHEN @Completed=1 THEN @Now ELSE @LeaseExpiresAt END,
                UpdatedAt=@Now,
                CompletedAt=CASE WHEN @Completed=1 THEN @Now ELSE NULL END
            WHERE OperationId=@OperationId
              AND BusinessId=@BusinessId
              AND UserId=@UserId
              AND LeaseToken=@LeaseToken;

            UPDATE link
            SET OperationId=@OperationId
            FROM dbo.OrderInvoiceLinks link
            WHERE link.BusinessId=@BusinessId
              AND link.OperationId IS NULL
              AND EXISTS(
                SELECT 1
                FROM OPENJSON(@ResultJson,N'$.results')
                WITH(orderId uniqueidentifier N'$.orderId',
                     documentId uniqueidentifier N'$.documentId') item
                WHERE item.orderId=link.OrderId
                  AND item.documentId=link.DocumentId);
            """;
        command.Parameters.AddRange([
            P("@Completed", completed),
            P("@Status", response.Status),
            P("@CompletedCount", response.CompletedCount),
            P("@FailedCount", response.FailedCount),
            P("@ResultJson", JsonSerializer.Serialize(response, JsonOptions)),
            P("@LeaseExpiresAt", time.GetUtcNow().AddMinutes(5)),
            P("@Now", time.GetUtcNow()),
            P("@OperationId", operationId),
            P("@BusinessId", actor.BusinessId),
            P("@UserId", actor.UserId),
            P("@LeaseToken", leaseToken)
        ]);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
            throw new OrderConflictException(
                "La reserva del lote expiró antes de guardar su progreso.");
    }

    private static async Task ExecuteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        SqlParameter[] parameters,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqlParameter P(string name, object? value) =>
        new(name, value ?? DBNull.Value);
}
