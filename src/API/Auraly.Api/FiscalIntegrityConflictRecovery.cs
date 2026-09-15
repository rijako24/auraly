using Auraly.Application.Fiscal;
using Auraly.Application.Sales;

namespace Auraly.Api;

public sealed class FiscalIntegrityConflictRecovery(
    ReceivePosSaleService receiver) : IFiscalIntegrityConflictRecovery
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
}
