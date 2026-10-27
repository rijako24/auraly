using System.Data;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingStore
{
    public async Task<VoucherDraftPreparation> GetSupportCancellationCaptureAsync(AccountingUserIdentity user,
        Guid payableId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var capture = await fiscalParticipant.PrepareCancellationCaptureAsync(connection, transaction, user, payableId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return capture;
    }

    public async Task<VoucherFiscalPreview> PreviewFiscalVoucherAsync(AccountingUserIdentity user,
        ConfirmManualAccountingVoucherRequest request, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var calculation = await fiscalParticipant.ReviewAsync(connection, transaction, user, request, cancellationToken);
        // This existing validator is read-only. Source acceptance owns first-use
        // account protection; preview never accepts a source or reserves a value.
        var context = await ValidateVoucherDimensionsAsync(connection, transaction, user,
            request.Lines.Select(line => new VoucherDraftLine(line.AccountId, line.PartyId, line.CostCenterId,
                line.Description, line.Debit, line.Credit, line.Reference, line.LineId, line.BaseAmount,
                line.ExistingPosition, line.FinancialGroupId, line.DueDate, line.PartySiteId, line.PaymentEvidence)).ToArray(),
            request.OccurredAt, request.ConceptCode, final: true, cancellationToken,
            mainPartyId: request.MainPartyId, financialCapture: true, voucherTypeCode: request.VoucherTypeCode,
            workSessionId: request.WorkSessionId);
        var preview = AccountingVoucherFinancialValidation.PreviewFiscal(request, calculation, context!);
        await transaction.CommitAsync(cancellationToken);
        return preview;
    }
}
