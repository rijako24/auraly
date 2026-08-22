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
            SELECT d.DocumentId,d.DocumentNumber,d.FiscalNumber,d.IssuedAt,
                   d.PayableAmount,d.CustomerIdentification,d.FiscalStatus,
                   snapshot.SnapshotJson
            FROM dbo.SalesDocuments d
            JOIN dbo.FiscalSnapshots snapshot
              ON snapshot.DocumentId=d.DocumentId
            WHERE d.BusinessId=@BusinessId AND d.WorkSessionId=@WorkSessionId
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
            P("@WorkSessionId", scope.WorkSessionId),
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
                var payload = PosSaleContractSerializer.Deserialize(
                    reader.GetString(7));
                items.Add(new(
                    reader.GetGuid(0),
                    payload.CommercialSnapshot.DocumentType,
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetDateTimeOffset(3),
                    reader.GetDecimal(4),
                    reader.GetString(5),
                    payload.UblSnapshot?.Customer.RegistrationName
                        ?? "Consumidor final",
                    reader.GetString(6)));
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
            SELECT snapshot.SnapshotJson,document.FiscalStatus
            FROM dbo.SalesDocuments document
            JOIN dbo.FiscalSnapshots snapshot
              ON snapshot.DocumentId=document.DocumentId
            WHERE document.DocumentId=@DocumentId
              AND document.BusinessId=@BusinessId
              AND document.WorkSessionId=@WorkSessionId;
            """;
        command.Parameters.AddRange([
            P("@DocumentId", documentId),
            P("@BusinessId", scope.BusinessId),
            P("@WorkSessionId", scope.WorkSessionId)
        ]);
        StoredOnlineSalesReceipt? result = null;
        await using (var reader =
                     await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
                result = new(
                    PosSaleContractSerializer.Deserialize(reader.GetString(0)),
                    reader.GetString(1));
        }
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
