using System.Data;
using Auraly.Application.Fiscal;
using Auraly.Application.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Api;

public sealed class FiscalSaleRecovery(
    ReceivePosSaleService receiver) : IFiscalSaleRecovery
{
    public Task<bool> RecoverAsync(
        Guid businessId,
        Guid documentId,
        CancellationToken cancellationToken) =>
        RecoverCoreAsync(businessId, documentId, cancellationToken);

    private async Task<bool> RecoverCoreAsync(
        Guid businessId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await receiver.RecoverStoredFiscalIntegrityConflictAsync(
                businessId, documentId, cancellationToken);
        }
        catch (PosSaleInvalidException exception)
        {
            throw new FiscalOperationException(exception.Message);
        }
    }

    public async Task<AcceptedSaleRecoveryResult?> RecoverAcceptedSaleAsync(
        Guid businessId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var recovered = await receiver.RecoverAcceptedBlockedOnlineSaleAsync(
                businessId, documentId, cancellationToken);
            return recovered is null ? null : new AcceptedSaleRecoveryResult(
                recovered.DocumentId, recovered.ProcessingStatus, recovered.Enqueued);
        }
        catch (Exception exception) when (exception is PosSaleInvalidException or
            InvalidOperationException or DBConcurrencyException)
        {
            throw new FiscalOperationException(exception.Message);
        }
        catch (SqlException exception) when (exception.Number is 51022 or 2601 or 2627)
        {
            throw new FiscalOperationException(
                "El pedido de origen ya está facturado o no está disponible para la recuperación.");
        }
    }
}
