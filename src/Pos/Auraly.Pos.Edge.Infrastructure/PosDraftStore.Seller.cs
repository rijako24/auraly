using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed partial class PosDraftStore
{
    public async Task<PosDraft> SelectSellerAsync(PosDraftScope scope, DraftId draftId, Guid? sellerId,
        string? sellerName, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = connection.BeginTransaction();
        var stored = (await ReadStoredDraftsAsync(connection, transaction, [draftId], ct)).SingleOrDefault()
            ?? throw new InvalidOperationException("La venta no existe en esta caja.");
        if (stored.Draft.Scope != scope) throw new UnauthorizedAccessException("La venta no pertenece a esta sesión.");
        await RequireActiveAsync(connection, transaction, draftId, ct);
        var now = _timeProvider.GetUtcNow();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE PosDrafts SET SellerId=@Seller,SellerName=@Name,UpdatedAt=@Now WHERE DraftId=@Draft;";
        command.Parameters.AddRange([P("@Seller",sellerId),P("@Name",sellerName),P("@Now",now),P("@Draft",draftId.Value)]);
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return stored.Draft with { SellerId=sellerId, SellerName=sellerName, UpdatedAt=now };
    }
}
