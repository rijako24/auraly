using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Taxation.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed record PreparedManualVoucherFiscal(
    string FiscalNumber, Guid IssuerId, int Environment, string SnapshotJson,
    WithholdingCalculationSnapshot Withholding, string FiscalDocumentType);

// Existing fiscal persistence participates in the journal's transaction. Quota
// protection precedes shared account/configuration reads; independent tax reads
// remain compatible with that update lock. Number allocation follows the proof.
public interface IManualVoucherFiscalParticipant
{
    Task<VoucherDraftPreparation> PrepareCancellationCaptureAsync(SqlConnection connection, SqlTransaction transaction,
        AccountingUserIdentity user, Guid payableId, CancellationToken token);
    Task ReserveAsync(SqlConnection connection, SqlTransaction transaction,
        AccountingUserIdentity user, ConfirmManualAccountingVoucherRequest request, CancellationToken token);
    Task<VoucherFiscalCalculation> ReviewAsync(SqlConnection connection, SqlTransaction? transaction,
        AccountingUserIdentity user, ConfirmManualAccountingVoucherRequest request, CancellationToken token);
    Task<PreparedManualVoucherFiscal> PrepareAsync(SqlConnection connection, SqlTransaction transaction,
        AccountingUserIdentity user, ConfirmManualAccountingVoucherRequest request,
        VoucherFiscalCalculation calculation, VoucherFiscalPreview preview, CancellationToken token);
    Task PersistAsync(SqlConnection connection, SqlTransaction transaction, AccountingUserIdentity user,
        ConfirmManualAccountingVoucherRequest request, PreparedManualVoucherFiscal prepared, CancellationToken token);
}
