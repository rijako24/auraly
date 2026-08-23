using System.Data;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore : IOnlineSalesOrderImportStore
{
    public async Task<OnlineSalesDraft> ImportOrderAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        ImportOnlineSalesOrderRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "ImportOrder";
        var payload = string.Join(
            "|",
            request.Lines.Select(line =>
                $"{line.ProductId:D}:{Invariant(line.Quantity)}:{Invariant(line.UnitPrice)}:{Invariant(line.DiscountAmount)}"));
        var requestHash = Hash(
            $"{operation}|{draftId:D}|{request.SourceOrderId:D}|{request.CustomerId:D}|{request.ExpectedVersion}|{payload}");

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        var state = await LockDraftAsync(
            connection, transaction, user, draftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, state.BusinessId, idempotencyKey,
            operation, requestHash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        DemandActiveVersion(state, request.ExpectedVersion);
        if (await CountDraftLinesAsync(
                connection, transaction, draftId, cancellationToken) != 0)
            throw new OnlineSalesDraftValidationException(
                "Pausa o reinicia la venta actual antes de recuperar un pedido.");

        await DemandOrderAsync(
            connection,
            transaction,
            state.BusinessId,
            request.SourceOrderId,
            cancellationToken);
        if (request.CustomerId is not null)
        {
            await ReadCustomerAsync(
                connection,
                transaction,
                state.BusinessId,
                request.CustomerId.Value,
                cancellationToken);
        }

        var position = 0;
        foreach (var line in request.Lines)
        {
            var product = await ReadProductAsync(
                connection,
                transaction,
                state.BusinessId,
                line.ProductId,
                cancellationToken);
            // A confirmed order already owns this quantity in the system
            // warehouse "Pedidos". Recovery edits the commercial draft; it
            // must not demand the same stock again from the sales warehouse.
            position++;
            await ExecuteAsync(connection, transaction, """
                INSERT dbo.SalesDraftLines(
                  SalesDraftLineId,SalesDraftId,ProductId,ProductCode,Description,
                  UnitCode,TaxCode,TaxRate,Quantity,BaseUnitPrice,UnitPrice,
                  CurrencyCode,PriceSource,DiscountAmount,Position)
                VALUES(
                  @LineId,@DraftId,@ProductId,@ProductCode,@Description,
                  @UnitCode,@TaxCode,@TaxRate,@Quantity,@BaseUnitPrice,@UnitPrice,
                  @CurrencyCode,N'Order',@Discount,@Position);
                """,
                [
                    P("@LineId", ids.NewId()),
                    P("@DraftId", draftId),
                    P("@ProductId", line.ProductId),
                    P("@ProductCode", product.Code),
                    P("@Description", product.Name),
                    P("@UnitCode", product.UnitCode),
                    P("@TaxCode", product.TaxCode),
                    P("@TaxRate", product.TaxRate),
                    P("@Quantity", line.Quantity),
                    P("@BaseUnitPrice", product.UnitPrice),
                    P("@UnitPrice", line.UnitPrice),
                    P("@CurrencyCode", product.CurrencyCode),
                    P("@Discount", line.DiscountAmount),
                    P("@Position", position)
                ],
                cancellationToken);
        }

        await ExecuteAsync(connection, transaction, """
            UPDATE dbo.SalesDrafts
            SET CustomerId=@CustomerId,SourceOrderId=@OrderId,
                Reference=@Reference,UpdatedAt=@Now
            WHERE SalesDraftId=@DraftId;
            """,
            [
                P("@CustomerId", request.CustomerId),
                P("@OrderId", request.SourceOrderId),
                P("@Reference", request.OrderNumber.Trim()),
                P("@Now", time.GetUtcNow()),
                P("@DraftId", draftId)
            ],
            cancellationToken);
        var version = await AdvanceVersionAsync(
            connection,
            transaction,
            draftId,
            request.ExpectedVersion,
            cancellationToken);
        await SaveReceiptAsync(
            connection,
            transaction,
            state.BusinessId,
            draftId,
            idempotencyKey,
            operation,
            requestHash,
            version,
            cancellationToken);
        var result = await ReadDraftAsync(
            connection, transaction, draftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<int> CountDraftLinesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid draftId,
        CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.SalesDraftLines WITH(UPDLOCK,HOLDLOCK) WHERE SalesDraftId=@DraftId;",
            connection,
            transaction);
        command.Parameters.Add(P("@DraftId", draftId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    private static async Task DemandOrderAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid orderId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT o.Status,o.CustomerConfirmed,link.DocumentId
            FROM dbo.Orders o WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.OrderInvoiceLinks link ON link.OrderId=o.OrderId
            WHERE o.OrderId=@OrderId AND o.BusinessId=@BusinessId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange([
            P("@OrderId", orderId), P("@BusinessId", businessId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftValidationException(
                "El pedido no pertenece a esta sede.");
        if (!reader.GetBoolean(1) ||
            reader.GetInt32(0) is not (2 or 4) ||
            !reader.IsDBNull(2))
            throw new OnlineSalesDraftValidationException(
                "El pedido ya no está disponible para facturar.");
    }
}
