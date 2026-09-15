using System.Data;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore
{
    public async Task<OnlineSalesCustomer?> GetCustomerAsync(
        OnlineSalesUserIdentity user,
        GetOnlineSalesCustomerRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        var scope = await ResolveOnlineContextAsync(
            connection,
            transaction,
            user,
            request.Context,
            cancellationToken);
        var customer = await ReadCustomerAsync(
            connection,
            transaction,
            scope.BusinessId,
            request.CustomerId,
            request.PartySiteId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return customer;
    }

    public async Task<OnlineSalesIssuedSalePage> SearchAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesIssuedSalesRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        var scope = await ResolveOnlineContextAsync(
            connection,
            transaction,
            user,
            request.Context,
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT d.DocumentId,d.DocumentType,d.DocumentNumber,d.FiscalNumber,d.IssuedAt,
                   d.PayableAmount,d.CustomerIdentification,d.FiscalStatus,
                   COALESCE(
                     NULLIF(JSON_VALUE(payload.PayloadJson,'$.commercialSnapshot.customerName'),N''),
                     NULLIF(JSON_VALUE(payload.PayloadJson,'$.ublSnapshot.customer.registrationName'),N''),
                     N'Consumidor final') CustomerName
            FROM dbo.SalesDocuments d
            JOIN dbo.DocumentProcessingPayloads payload
              ON payload.DocumentId=d.DocumentId
             AND payload.DocumentType=d.DocumentType
             AND payload.BusinessId=d.BusinessId
            WHERE d.BusinessId=@BusinessId AND d.WarehouseId=@WarehouseId
              AND ISNULL(JSON_VALUE(payload.PayloadJson,'$.fiscalHabilitationOnly'),N'false')<>N'true'
              AND (@CustomerId IS NULL OR
                   (d.CustomerId=@CustomerId AND d.CustomerPartySiteId=@PartySiteId))
              AND (@From IS NULL OR d.IssuedAt>=@From)
              AND (@ToExclusive IS NULL OR d.IssuedAt<@ToExclusive)
              AND (@MinimumTotal IS NULL OR d.PayableAmount>=@MinimumTotal)
              AND (@MaximumTotal IS NULL OR d.PayableAmount<=@MaximumTotal)
              AND (@ProductId IS NULL OR EXISTS(
                    SELECT 1 FROM dbo.SalesDocumentLines line
                    WHERE line.DocumentId=d.DocumentId AND line.ProductId=@ProductId))
              AND (@Search=N'' OR d.DocumentNumber LIKE @Contains
                   OR d.FiscalNumber LIKE @Contains
                   OR d.CufeReceived LIKE @Contains
                   OR d.CustomerIdentification LIKE @Contains)
            ORDER BY d.IssuedAt DESC,d.DocumentId DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;
        var search = request.Search?.Trim() ?? string.Empty;
        command.Parameters.AddRange([
            P("@BusinessId", scope.BusinessId),
            P("@WarehouseId", scope.WarehouseId),
            P("@CustomerId", request.CustomerId),
            P("@PartySiteId", request.PartySiteId),
            P("@From", request.From?.ToDateTime(TimeOnly.MinValue)),
            P("@ToExclusive", request.To?.AddDays(1).ToDateTime(TimeOnly.MinValue)),
            P("@ProductId", request.ProductId),
            P("@MinimumTotal", request.MinimumTotal),
            P("@MaximumTotal", request.MaximumTotal),
            P("@Search", search),
            P("@Contains", $"%{search}%"),
            P("@Skip", request.Skip),
            P("@Take", request.Take + 1)
        ]);
        var items = new List<OnlineSalesIssuedSale>();
        await using (var reader =
                     await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetDateTimeOffset(4),
                    reader.GetDecimal(5),
                    reader.GetString(6),
                    reader.GetString(8),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }
        var hasMore = items.Count > request.Take;
        if (hasMore)
            items.RemoveAt(items.Count - 1);
        await transaction.CommitAsync(cancellationToken);
        return new(
            items,
            hasMore,
            hasMore ? request.Skip + items.Count : null);
    }

    public async Task<StoredOnlineSalesReceipt?> GetReceiptAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesDraftContext context,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        var scope = await ResolveOnlineContextAsync(
            connection,
            transaction,
            user,
            context,
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT payload.PayloadJson,document.FiscalStatus
            FROM dbo.SalesDocuments document
            JOIN dbo.DocumentProcessingPayloads payload
              ON payload.DocumentId=document.DocumentId
             AND payload.DocumentType=document.DocumentType
             AND payload.BusinessId=document.BusinessId
            WHERE document.DocumentId=@DocumentId
              AND document.BusinessId=@BusinessId
              AND document.WarehouseId=@WarehouseId
              AND ISNULL(JSON_VALUE(payload.PayloadJson,'$.fiscalHabilitationOnly'),N'false')<>N'true';
            """;
        command.Parameters.AddRange([
            P("@DocumentId", documentId),
            P("@BusinessId", scope.BusinessId),
            P("@WarehouseId", scope.WarehouseId)
        ]);
        StoredOnlineSalesReceipt? result = null;
        await using (var reader =
                     await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
                result = new(
                    PosSaleContractSerializer.Deserialize(reader.GetString(0)),
                    reader.IsDBNull(1) ? null : reader.GetString(1));
        }
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
