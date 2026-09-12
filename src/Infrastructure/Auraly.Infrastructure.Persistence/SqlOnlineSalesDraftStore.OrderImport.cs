using System.Data;
using Auraly.Application.Sales;
using Auraly.Contracts.Orders;
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
                $"{line.ProductId:D}:{Invariant(line.Quantity)}:{Invariant(line.UnitPrice)}:{Invariant(line.DiscountAmount)}:{NormalizePriceSource(line.PriceSource)}"));
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
        var draftLineCount = await CountDraftLinesAsync(
            connection, transaction, draftId, cancellationToken);
        if (state.SourceOrderId == request.SourceOrderId && draftLineCount != 0)
        {
            var current = await ReadDraftAsync(
                connection, transaction, draftId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return current;
        }
        if (draftLineCount != 0 && state.SourceOrderId is null)
            throw new OnlineSalesDraftValidationException(
                "Pausa o reinicia la venta actual antes de recuperar un pedido.");

        if (state.SourceOrderId is not null)
            await ExecuteAsync(
                connection,
                transaction,
                "DELETE dbo.SalesDraftLines WHERE SalesDraftId=@DraftId;",
                [P("@DraftId", draftId)],
                cancellationToken);

        await DemandOrderAsync(
            connection,
            transaction,
            user,
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

        var products = await ReadProductsAsync(
            connection,
            transaction,
            state.BusinessId,
            state.WarehouseId,
            request.Lines.Select(line => line.ProductId).Distinct().ToArray(),
            cancellationToken);
        var importedLines = request.Lines.Select((line, index) =>
        {
            if (!products.TryGetValue(line.ProductId, out var product))
                throw new OnlineSalesDraftValidationException(
                    "El producto no está disponible para este negocio.");
            return new
            {
                LineId = ids.NewId(),
                line.ProductId,
                ProductCode = product.Code,
                Description = product.Name,
                product.UnitCode,
                product.TaxCode,
                product.TaxRate,
                line.Quantity,
                BaseUnitPrice = product.UnitPrice,
                line.UnitPrice,
                DocumentUnitCost = product.UnitCost,
                product.CurrencyCode,
                PriceSource = NormalizePriceSource(line.PriceSource),
                Discount = line.DiscountAmount,
                Position = index + 1
            };
        }).ToArray();
        // A confirmed order already owns these quantities in the system
        // warehouse "Pedidos". Recovery imports the complete snapshot in one
        // statement and must not demand the same stock again from sales.
        await ExecuteAsync(connection, transaction, """
            INSERT dbo.SalesDraftLines(
              SalesDraftLineId,SalesDraftId,ProductId,ProductCode,Description,
              UnitCode,TaxCode,TaxRate,Quantity,BaseUnitPrice,UnitPrice,DocumentUnitCost,
              CurrencyCode,PriceSource,DiscountAmount,Position)
            SELECT input.LineId,@DraftId,input.ProductId,input.ProductCode,input.Description,
                   input.UnitCode,input.TaxCode,input.TaxRate,input.Quantity,input.BaseUnitPrice,
                   input.UnitPrice,input.DocumentUnitCost,input.CurrencyCode,input.PriceSource,
                   input.Discount,input.Position
            FROM OPENJSON(@LinesJson) WITH(
              LineId uniqueidentifier '$.LineId',ProductId uniqueidentifier '$.ProductId',
              ProductCode nvarchar(64) '$.ProductCode',Description nvarchar(250) '$.Description',
              UnitCode nvarchar(24) '$.UnitCode',TaxCode nvarchar(16) '$.TaxCode',
              TaxRate decimal(9,4) '$.TaxRate',Quantity decimal(18,4) '$.Quantity',
              BaseUnitPrice decimal(18,2) '$.BaseUnitPrice',UnitPrice decimal(18,2) '$.UnitPrice',
              DocumentUnitCost decimal(19,6) '$.DocumentUnitCost',CurrencyCode nvarchar(3) '$.CurrencyCode',
              PriceSource nvarchar(64) '$.PriceSource',Discount decimal(18,2) '$.Discount',
              Position int '$.Position') input;
            """,
            [P("@DraftId", draftId), P("@LinesJson", System.Text.Json.JsonSerializer.Serialize(importedLines))],
            cancellationToken);

        await ExecuteAsync(connection, transaction, """
            UPDATE staleDraft
            SET Status=N'Deleted',SourceOrderId=NULL,DeletedAt=@Now,
                UpdatedAt=@Now,Version=Version+1
            FROM dbo.SalesDrafts staleDraft
            INNER JOIN dbo.WorkSessions staleSession
              ON staleSession.WorkSessionId=staleDraft.WorkSessionId
            WHERE staleDraft.SourceOrderId=@OrderId
              AND staleDraft.SalesDraftId<>@DraftId
              AND staleSession.Status<>N'Open';

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

    private static string NormalizePriceSource(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Order" : value.Trim();

    private static async Task DemandOrderAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OnlineSalesUserIdentity user,
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
        var status = reader.GetInt32(0);
        var canEditReview = status == 5 &&
            (user.Permissions.Contains(OrderPermissionCodes.Update) ||
             user.Permissions.Contains(OrderPermissionCodes.Review));
        if (!reader.GetBoolean(1) ||
            (status is not (2 or 4) && !canEditReview) ||
            !reader.IsDBNull(2))
            throw new OnlineSalesDraftValidationException(
                "El pedido ya no está disponible para facturar o corregir.");
    }
}
