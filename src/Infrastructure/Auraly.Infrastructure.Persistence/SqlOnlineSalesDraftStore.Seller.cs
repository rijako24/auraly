using System.Data;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore
{
    public async Task<OnlineSalesDraft> SelectSellerAsync(OnlineSalesUserIdentity user, Guid draftId,
        Guid? sellerId, long expectedVersion, string idempotencyKey, CancellationToken ct)
    {
        const string operation = "SelectSeller";
        var hash = Hash($"{operation}|{draftId:D}|{sellerId?.ToString("D") ?? "Automatic"}");
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var state = await LockDraftAsync(connection, transaction, user, draftId, ct);
        var replay = await ReplayAsync(connection, transaction, state.BusinessId, idempotencyKey, operation, hash, ct);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct);
            return replay;
        }
        DemandActiveVersion(state, expectedVersion);
        var updated = await ExecuteAsync(connection, transaction, """
            UPDATE dbo.SalesDrafts SET SellerId=@Seller WHERE SalesDraftId=@Draft
              AND (@Seller IS NULL OR EXISTS(
                SELECT 1 FROM dbo.CommerceSellers seller JOIN dbo.Parties party
                  ON party.PartyId=seller.PartyId AND party.TenantId=seller.TenantId
                WHERE seller.SellerId=@Seller AND seller.TenantId=@Tenant AND seller.IsActive=1 AND party.IsActive=1));
            """, [P("@Seller", sellerId), P("@Draft", draftId), P("@Tenant", user.TenantId)], ct);
        if (updated != 1)
            throw new OnlineSalesDraftValidationException("El vendedor no está activo en esta empresa.");
        var version = await AdvanceVersionAsync(connection, transaction, draftId, expectedVersion, ct);
        await SaveReceiptAsync(connection, transaction, state.BusinessId, draftId, idempotencyKey, operation, hash, version, ct);
        var result = await ReadDraftAsync(connection, transaction, draftId, ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}
