using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Taxation.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed record PreparedManualVoucherFiscal(
    string FiscalNumber, Guid IssuerId, int Environment, string SnapshotJson,
    WithholdingCalculationSnapshot Withholding, string FiscalDocumentType);

// Fiscal snapshot persistence participates in journal acceptance. DIAN quota is
// reserved only when the user explicitly releases that snapshot for transmission.
public interface IManualVoucherFiscalParticipant
{
    Task<VoucherDraftPreparation> PrepareCancellationCaptureAsync(SqlConnection connection, SqlTransaction transaction,
        AccountingUserIdentity user, Guid payableId, CancellationToken token);
    Task<VoucherFiscalCalculation> ReviewAsync(SqlConnection connection, SqlTransaction? transaction,
        AccountingUserIdentity user, ConfirmManualAccountingVoucherRequest request, CancellationToken token);
    Task<PreparedManualVoucherFiscal> PrepareAsync(SqlConnection connection, SqlTransaction transaction,
        AccountingUserIdentity user, ConfirmManualAccountingVoucherRequest request,
        VoucherFiscalCalculation calculation, VoucherFiscalPreview preview, CancellationToken token);
    Task PersistAsync(SqlConnection connection, SqlTransaction transaction, AccountingUserIdentity user,
        ConfirmManualAccountingVoucherRequest request, PreparedManualVoucherFiscal prepared, CancellationToken token);
}
