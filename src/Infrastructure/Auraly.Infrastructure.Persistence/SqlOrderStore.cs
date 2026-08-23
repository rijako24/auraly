using System.Data;
using Auraly.Application.Orders;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Orders;
using Auraly.Domain.Orders;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlOrderStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider time) : IOrderStore
{
    public async Task<OrderPage> PageAsync(
        OrderActor actor,
        OrderPageRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);

        var filters = new List<string>
        {
            "o.BusinessId=@BusinessId",
            "b.TenantId=@TenantId"
        };
        var parameters = new List<SqlParameter>
        {
            P("@BusinessId", actor.BusinessId),
            P("@TenantId", actor.TenantId)
        };

        AddContains(filters, parameters, request.OrderNumber,
            "(o.ExternalDocumentNumber LIKE @OrderNumber OR CONVERT(nvarchar(36),o.OrderId) LIKE @OrderNumber)",
            "@OrderNumber");
        AddContains(filters, parameters, request.Customer,
            "(o.CustomerNameSnapshot LIKE @Customer OR o.CustomerDocumentSnapshot LIKE @Customer OR o.CustomerPhoneSnapshot LIKE @Customer)",
            "@Customer");
        if (!string.IsNullOrWhiteSpace(request.Product))
        {
            filters.Add("""
                EXISTS (
                  SELECT 1
                  FROM dbo.OrderItems oi
                  WHERE oi.OrderId=o.OrderId
                    AND (oi.ProductNameSnapshot LIKE @Product
                      OR oi.ProductCodeSnapshot LIKE @Product
                      OR oi.Sku LIKE @Product))
                """);
            parameters.Add(P("@Product", $"%{EscapeLike(request.Product.Trim())}%"));
        }
        if (request.Source is not null)
        {
            filters.Add("o.Source=@Source");
            parameters.Add(P("@Source", request.Source.Value));
        }
        if (request.CreatedFrom is not null)
        {
            filters.Add("o.CreatedAt>=@CreatedFrom");
            parameters.Add(P("@CreatedFrom", request.CreatedFrom.Value.UtcDateTime));
        }
        if (request.CreatedTo is not null)
        {
            filters.Add("o.CreatedAt<@CreatedTo");
            parameters.Add(P("@CreatedTo", request.CreatedTo.Value.UtcDateTime));
        }
        if (request.HasPendingBalance is true)
            filters.Add("(o.PaymentTransactionId IS NULL OR ISNULL(pt.Status,-1)<>2)");
        else if (request.HasPendingBalance is false)
            filters.Add("(o.PaymentTransactionId IS NOT NULL AND pt.Status=2)");

        if (request.WarehouseId is not null)
        {
            filters.Add("ISJSON(o.CustomAttributesJson)=1 AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(o.CustomAttributesJson,'$.WarehouseId'))=@WarehouseId");
            parameters.Add(P("@WarehouseId", request.WarehouseId.Value));
        }
        if (request.RouteId is not null)
        {
            filters.Add("ISJSON(o.CustomAttributesJson)=1 AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(o.CustomAttributesJson,'$.RouteId'))=@RouteId");
            parameters.Add(P("@RouteId", request.RouteId.Value));
        }
        if (request.OnlyCreatedByActor)
        {
            filters.Add("ISJSON(o.CustomAttributesJson)=1 AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(o.CustomAttributesJson,'$.createdBy'))=@CreatedByActor");
            parameters.Add(P("@CreatedByActor", actor.UserId));
        }

        AddStatusFilter(filters, parameters, request.Status);
        if (!request.IncludeClaimedByOthers)
        {
            filters.Add("""
                (claim.OrderClaimId IS NULL
                 OR (claim.UserId=@ActorUserId AND claim.WorkSessionId=COALESCE(@ActorWorkSessionId,claim.WorkSessionId)))
                """);
            parameters.Add(P("@ActorUserId", actor.UserId));
            parameters.Add(P("@ActorWorkSessionId", actor.WorkSessionId));
        }

        parameters.Add(P("@Now", time.GetUtcNow()));
        parameters.Add(P("@Offset", (request.Page - 1) * request.PageSize));
        parameters.Add(P("@Take", request.PageSize));
        var sql = $"""
            SELECT
              o.OrderId,
              COALESCE(NULLIF(o.ExternalDocumentNumber,N''),CONCAT(N'PED-',LEFT(CONVERT(nvarchar(36),o.OrderId),8))) OrderNumber,
              o.Status,o.Source,o.CustomerNameSnapshot,o.CustomerDocumentSnapshot,
              o.CustomerPhoneSnapshot,o.Currency,o.Total,
              (SELECT COUNT_BIG(1) FROM dbo.OrderItems oi WHERE oi.OrderId=o.OrderId) LineCount,
              o.CreatedAt,o.CustomerConfirmed,link.DocumentId,
              claim.OrderClaimId,claim.WorkSessionId,claim.DeviceId,claim.UserId,claim.ExpiresAt,
              COUNT_BIG(1) OVER() TotalRows
            FROM dbo.Orders o
            INNER JOIN dbo.Businesses b ON b.BusinessId=o.BusinessId
            LEFT JOIN dbo.PaymentTransactions pt
              ON pt.PaymentTransactionId=o.PaymentTransactionId
            LEFT JOIN dbo.OrderInvoiceLinks link ON link.OrderId=o.OrderId
            OUTER APPLY (
              SELECT TOP(1) c.OrderClaimId,c.WorkSessionId,c.DeviceId,c.UserId,c.ExpiresAt
              FROM dbo.OrderClaims c
              WHERE c.OrderId=o.OrderId AND c.ReleasedAt IS NULL AND c.ExpiresAt>@Now
              ORDER BY c.ClaimedAt DESC
            ) claim
            WHERE {string.Join(" AND ", filters)}
            ORDER BY o.CreatedAt DESC,o.OrderId DESC
            OFFSET @Offset ROWS FETCH NEXT @Take ROWS ONLY;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<OrderListItem>();
        var total = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            var hasInvoice = !reader.IsDBNull(12);
            var storedStatus = reader.GetInt32(2);
            var claim = ReadClaim(reader, 13, actor);
            total = checked((int)reader.GetInt64(18));
            items.Add(new OrderListItem(
                reader.GetGuid(0),
                reader.GetString(1),
                OrderRules.CanonicalStatus(storedStatus, hasInvoice),
                reader.GetInt32(3),
                NullableString(reader, 4),
                NullableString(reader, 5),
                NullableString(reader, 6),
                reader.GetString(7),
                reader.GetDecimal(8),
                checked((int)reader.GetInt64(9)),
                DateTime.SpecifyKind(reader.GetDateTime(10), DateTimeKind.Utc),
                OrderRules.CanInvoice(storedStatus, reader.GetBoolean(11), hasInvoice),
                hasInvoice ? reader.GetGuid(12) : null,
                claim));
        }

        return new OrderPage(
            items,
            request.Page,
            request.PageSize,
            total,
            request.Page * request.PageSize < total);
    }

    public async Task<OrderDetail?> GetAsync(
        OrderActor actor,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string headerSql = """
            SELECT
              o.OrderId,o.BusinessId,
              COALESCE(NULLIF(o.ExternalDocumentNumber,N''),CONCAT(N'PED-',LEFT(CONVERT(nvarchar(36),o.OrderId),8))),
              o.Status,o.Source,o.CustomerId,o.CustomerNameSnapshot,o.CustomerDocumentSnapshot,
              o.CustomerPhoneSnapshot,o.CustomerEmailSnapshot,o.DeliveryAddressSnapshot,
              o.Notes,o.Currency,o.Subtotal,o.DiscountTotal,o.Total,
              o.PaymentTransactionId,
              CASE pt.Status WHEN 2 THEN N'Confirmed' WHEN 3 THEN N'Failed'
                   WHEN 1 THEN N'Pending' ELSE NULL END,
              o.CreatedAt,o.CustomerConfirmed,link.DocumentId,
              claim.OrderClaimId,claim.WorkSessionId,claim.DeviceId,claim.UserId,claim.ExpiresAt,
              TRY_CONVERT(uniqueidentifier,JSON_VALUE(o.CustomAttributesJson,'$.WarehouseId'))
            FROM dbo.Orders o
            INNER JOIN dbo.Businesses b ON b.BusinessId=o.BusinessId
            LEFT JOIN dbo.PaymentTransactions pt
              ON pt.PaymentTransactionId=o.PaymentTransactionId
            LEFT JOIN dbo.OrderInvoiceLinks link ON link.OrderId=o.OrderId
            OUTER APPLY (
              SELECT TOP(1) c.OrderClaimId,c.WorkSessionId,c.DeviceId,c.UserId,c.ExpiresAt
              FROM dbo.OrderClaims c
              WHERE c.OrderId=o.OrderId AND c.ReleasedAt IS NULL AND c.ExpiresAt>@Now
              ORDER BY c.ClaimedAt DESC
            ) claim
            WHERE o.OrderId=@OrderId AND o.BusinessId=@BusinessId AND b.TenantId=@TenantId;
            """;
        await using var headerCommand = new SqlCommand(headerSql, connection);
        headerCommand.Parameters.AddRange([
            P("@OrderId", orderId), P("@BusinessId", actor.BusinessId),
            P("@TenantId", actor.TenantId), P("@Now", time.GetUtcNow())
        ]);
        await using var header = await headerCommand.ExecuteReaderAsync(cancellationToken);
        if (!await header.ReadAsync(cancellationToken))
            return null;

        var storedStatus = header.GetInt32(3);
        var hasInvoice = !header.IsDBNull(20);
        var claim = ReadClaim(header, 21, actor);
        var values = new
        {
            Id = header.GetGuid(0),
            BusinessId = header.GetGuid(1),
            Number = header.GetString(2),
            Status = OrderRules.CanonicalStatus(storedStatus, hasInvoice),
            Source = header.GetInt32(4),
            CustomerId = header.IsDBNull(5) ? (Guid?)null : header.GetGuid(5),
            CustomerName = NullableString(header, 6),
            CustomerDocument = NullableString(header, 7),
            CustomerPhone = NullableString(header, 8),
            CustomerEmail = NullableString(header, 9),
            Address = NullableString(header, 10),
            Notes = NullableString(header, 11),
            Currency = header.GetString(12),
            Subtotal = header.GetDecimal(13),
            Discount = header.GetDecimal(14),
            Total = header.GetDecimal(15),
            PaymentId = header.IsDBNull(16) ? (Guid?)null : header.GetGuid(16),
            PaymentStatus = NullableString(header, 17),
            CreatedAt = DateTime.SpecifyKind(header.GetDateTime(18), DateTimeKind.Utc),
            Confirmed = header.GetBoolean(19),
            DocumentId = hasInvoice ? header.GetGuid(20) : (Guid?)null,
            WarehouseId = header.IsDBNull(26) ? (Guid?)null : header.GetGuid(26)
        };
        await header.CloseAsync();

        const string lineSql = """
            SELECT OrderItemId,ProductId,ProductCodeSnapshot,Sku,
                   ProductNameSnapshot,COALESCE(NULLIF(UnitCodeSnapshot,N''),N'EA'),
                   Quantity,UnitPrice,DiscountAmount,LineTotal
            FROM dbo.OrderItems
            WHERE OrderId=@OrderId AND BusinessId=@BusinessId
            ORDER BY CreatedAt,OrderItemId;
            """;
        await using var lineCommand = new SqlCommand(lineSql, connection);
        lineCommand.Parameters.AddRange([
            P("@OrderId", orderId), P("@BusinessId", actor.BusinessId)
        ]);
        await using var lineReader = await lineCommand.ExecuteReaderAsync(cancellationToken);
        var lines = new List<OrderLine>();
        while (await lineReader.ReadAsync(cancellationToken))
        {
            lines.Add(new OrderLine(
                lineReader.GetGuid(0),
                lineReader.IsDBNull(1) ? null : lineReader.GetGuid(1),
                NullableString(lineReader, 2),
                NullableString(lineReader, 3),
                lineReader.GetString(4),
                lineReader.GetString(5),
                lineReader.GetDecimal(6),
                lineReader.GetDecimal(7),
                lineReader.GetDecimal(8),
                lineReader.GetDecimal(9)));
        }

        return new OrderDetail(
            values.Id, values.BusinessId, values.Number, values.Status, values.Source,
            values.CustomerId,
            values.CustomerName, values.CustomerDocument, values.CustomerPhone,
            values.CustomerEmail, values.Address, values.Notes, values.Currency,
            values.Subtotal, values.Discount, values.Total, values.PaymentId,
            values.PaymentStatus, values.CreatedAt,
            OrderRules.CanInvoice(storedStatus, values.Confirmed, hasInvoice),
            values.DocumentId, claim, lines, values.WarehouseId);
    }

    public async Task<OrderClaimSummary> ClaimAsync(
        OrderActor actor,
        Guid orderId,
        Guid workSessionId,
        int leaseMinutes,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var expires = now.AddMinutes(leaseMinutes);
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
        await DemandContextAsync(
            connection, transaction, actor, orderId, workSessionId, cancellationToken);

        await ExecuteAsync(connection, transaction, """
            UPDATE dbo.OrderClaims
            SET ReleasedAt=@Now
            WHERE OrderId=@OrderId AND ReleasedAt IS NULL AND ExpiresAt<=@Now;
            """,
            [P("@Now", now), P("@OrderId", orderId)],
            cancellationToken);

        const string lockSql = """
            SELECT TOP(1) OrderClaimId,WorkSessionId,DeviceId,UserId,ExpiresAt
            FROM dbo.OrderClaims WITH(UPDLOCK,HOLDLOCK)
            WHERE OrderId=@OrderId AND ReleasedAt IS NULL;
            """;
        await using var lockCommand = new SqlCommand(lockSql, connection, transaction);
        lockCommand.Parameters.Add(P("@OrderId", orderId));
        await using var reader = await lockCommand.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var claimId = reader.GetGuid(0);
            var ownerWorkSession = reader.GetGuid(1);
            var ownerDevice = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
            var ownerUser = reader.GetGuid(3);
            await reader.CloseAsync();
            if (ownerWorkSession != workSessionId || ownerUser != actor.UserId)
                throw new OrderConflictException(
                    "El pedido está siendo preparado en otra sesión.");
            await ExecuteAsync(connection, transaction, """
                UPDATE dbo.OrderClaims SET ExpiresAt=@ExpiresAt
                WHERE OrderClaimId=@ClaimId;
                """,
                [P("@ExpiresAt", expires), P("@ClaimId", claimId)],
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new OrderClaimSummary(
                claimId, workSessionId, ownerDevice, actor.UserId, expires, true);
        }
        await reader.CloseAsync();

        var id = ids.NewId();
        await ExecuteAsync(connection, transaction, """
            INSERT dbo.OrderClaims(
              OrderClaimId,BusinessId,WarehouseId,OrderId,WorkSessionId,DeviceId,UserId,
              ClaimedAt,ExpiresAt)
            SELECT
              @ClaimId,@BusinessId,ws.WarehouseId,@OrderId,@WorkSessionId,@DeviceId,@UserId,
              @Now,@ExpiresAt
            FROM dbo.WorkSessions ws
            WHERE ws.WorkSessionId=@WorkSessionId
              AND ws.BusinessId=@BusinessId
              AND ws.UserId=@UserId
              AND ws.Status=N'Open';
            """,
            [
                P("@ClaimId", id), P("@BusinessId", actor.BusinessId),
                P("@OrderId", orderId), P("@WorkSessionId", workSessionId),
                P("@DeviceId", actor.DeviceId), P("@UserId", actor.UserId),
                P("@Now", now), P("@ExpiresAt", expires)
            ],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OrderClaimSummary(
            id, workSessionId, actor.DeviceId, actor.UserId, expires, true);
    }

    public async Task ReleaseClaimAsync(
        OrderActor actor,
        Guid orderId,
        Guid workSessionId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var affected = await ExecuteAsync(connection, null, """
            UPDATE dbo.OrderClaims
            SET ReleasedAt=@Now
            WHERE OrderId=@OrderId AND BusinessId=@BusinessId
              AND WorkSessionId=@WorkSessionId AND UserId=@UserId
              AND ReleasedAt IS NULL;
            """,
            [
                P("@Now", time.GetUtcNow()), P("@OrderId", orderId),
                P("@BusinessId", actor.BusinessId), P("@WorkSessionId", workSessionId),
                P("@UserId", actor.UserId)
            ],
            cancellationToken);
        if (affected == 0)
            throw new OrderConflictException(
                "No existe una recuperación activa del pedido para esta sesión.");
    }

    public async Task ReleaseOtherClaimsAsync(
        OrderActor actor,
        Guid retainedOrderId,
        Guid workSessionId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, null, """
            UPDATE dbo.OrderClaims
            SET ReleasedAt=@Now
            WHERE BusinessId=@BusinessId
              AND WorkSessionId=@WorkSessionId AND UserId=@UserId
              AND OrderId<>@RetainedOrderId AND ReleasedAt IS NULL;
            """,
            [
                P("@Now", time.GetUtcNow()), P("@BusinessId", actor.BusinessId),
                P("@WorkSessionId", workSessionId), P("@UserId", actor.UserId),
                P("@RetainedOrderId", retainedOrderId)
            ],
            cancellationToken);
    }

    private static async Task DemandContextAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OrderActor actor,
        Guid orderId,
        Guid workSessionId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT o.Status,o.CustomerConfirmed,
                   CASE WHEN link.OrderId IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END
            FROM dbo.Orders o
            INNER JOIN dbo.Businesses b ON b.BusinessId=o.BusinessId
            INNER JOIN dbo.WorkSessions ws
              ON ws.WorkSessionId=@WorkSessionId AND ws.BusinessId=o.BusinessId
             AND ws.UserId=@UserId AND ws.Status=N'Open'
            LEFT JOIN dbo.OrderInvoiceLinks link ON link.OrderId=o.OrderId
            WHERE o.OrderId=@OrderId AND o.BusinessId=@BusinessId AND b.TenantId=@TenantId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange([
            P("@WorkSessionId", workSessionId), P("@OrderId", orderId),
            P("@BusinessId", actor.BusinessId), P("@TenantId", actor.TenantId),
            P("@UserId", actor.UserId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OrderNotFoundException(
                "El pedido o la sesión no pertenecen a esta sede.");
        if (!OrderRules.CanInvoice(
                reader.GetInt32(0), reader.GetBoolean(1), reader.GetBoolean(2)))
            throw new OrderConflictException(
                "El pedido no está disponible para facturar.");
    }

    private static void AddStatusFilter(
        ICollection<string> filters,
        ICollection<SqlParameter> parameters,
        string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return;
        var normalized = status.Trim();
        switch (normalized.ToUpperInvariant())
        {
            case "INVOICED":
                filters.Add("link.OrderId IS NOT NULL");
                break;
            case "AVAILABLE":
                filters.Add("link.OrderId IS NULL AND o.CustomerConfirmed=1 AND o.Status IN(2,4)");
                break;
            case "CANCELLED":
                filters.Add("link.OrderId IS NULL AND o.Status=6");
                break;
            case "AWAITINGPAYMENT":
                filters.Add("link.OrderId IS NULL AND o.Status=7");
                break;
            case "EXPIRED":
                filters.Add("link.OrderId IS NULL AND o.Status=91");
                break;
            case "PENDING":
                filters.Add("link.OrderId IS NULL AND o.Status NOT IN(2,4,6,7,91)");
                break;
            default:
                filters.Add("1=0");
                parameters.Add(P("@IgnoredStatus", normalized));
                break;
        }
    }

    private static OrderClaimSummary? ReadClaim(
        SqlDataReader reader,
        int start,
        OrderActor actor)
    {
        if (reader.IsDBNull(start))
            return null;
        var workSessionId = reader.GetGuid(start + 1);
        var deviceId = reader.IsDBNull(start + 2)
            ? (Guid?)null
            : reader.GetGuid(start + 2);
        var userId = reader.GetGuid(start + 3);
        return new OrderClaimSummary(
            reader.GetGuid(start),
            workSessionId,
            deviceId,
            userId,
            reader.GetDateTimeOffset(start + 4),
            actor.WorkSessionId is not null &&
            userId == actor.UserId &&
            actor.WorkSessionId == workSessionId);
    }

    private static void AddContains(
        ICollection<string> filters,
        ICollection<SqlParameter> parameters,
        string? value,
        string predicate,
        string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        filters.Add(predicate);
        parameters.Add(P(parameter, $"%{EscapeLike(value.Trim())}%"));
    }

    private static string EscapeLike(string value) =>
        value.Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);

    private static string? NullableString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static async Task<int> ExecuteAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string sql,
        SqlParameter[] parameters,
        CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static SqlParameter P(string name, object? value) =>
        new(name, value ?? DBNull.Value);
}
