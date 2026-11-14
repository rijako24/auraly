using System.Data;
using System.Text.Json;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingPostingProcessor
{
    private sealed record PortfolioAdjustment(Guid EffectId, Guid PositionId, decimal Delta, Guid? SourceLineId = null);

    // Individual adjustments and adjustments embedded in a voucher use the same
    // balance writer and history batch, inside their existing posting transaction.
    private async Task ApplyPortfolioAdjustmentsAsync(SqlConnection connection, SqlTransaction transaction,
        SourceEnvelope source, string kind, IReadOnlyList<PortfolioAdjustment> adjustments, CancellationToken token)
    {
        if (adjustments.Count == 0) return;
        if (kind is not (AccountingSubledgerKinds.Payable or AccountingSubledgerKinds.Receivable) || adjustments.Count > 500)
            throw new InvalidOperationException("The accepted portfolio adjustment batch is invalid.");
        var receivable = kind == AccountingSubledgerKinds.Receivable;
        var table = receivable ? "Receivables" : "Payables";
        var key = receivable ? "ReceivableId" : "PayableId";
        var history = receivable ? "ReceivableTransactions" : "PayableTransactions";
        var historyKey = receivable ? "ReceivableTransactionId" : "PayableTransactionId";
        await using var command = new SqlCommand(DeclarePortfolioBalanceChangesSql + """
            DECLARE @Adjustments TABLE(EffectId uniqueidentifier PRIMARY KEY,PositionId uniqueidentifier,
              Delta decimal(19,4),SourceLineId uniqueidentifier);
            INSERT @Adjustments SELECT EffectId,PositionId,Delta,SourceLineId FROM OPENJSON(@Rows)
              WITH(EffectId uniqueidentifier,PositionId uniqueidentifier,Delta decimal(19,4),SourceLineId uniqueidentifier);
            IF (SELECT COUNT(*) FROM @Adjustments)<>@Count OR EXISTS(SELECT 1 FROM @Adjustments
              WHERE EffectId IS NULL OR PositionId IS NULL OR Delta IS NULL OR Delta=0)
              THROW 51607,'The accepted portfolio adjustments are incomplete.',1;
            INSERT @PortfolioChanges SELECT PositionId,SUM(Delta) FROM @Adjustments GROUP BY PositionId;
            """ + PortfolioBalanceChangesSql(table, key, PortfolioBalanceChangeKind.Adjustment) + $"""
            INSERT dbo.[{history}]([{historyKey}],[{key}],TransactionType,Amount,
              SourceDocumentId,SourceDocumentType,SourceLineId,OccurredAt,CreatedAt)
            SELECT EffectId,PositionId,N'Adjustment',Delta,@DocumentId,
              CASE WHEN SourceLineId IS NOT NULL THEN @DocumentType END,SourceLineId,@OccurredAt,@Now FROM @Adjustments;
            IF @@ROWCOUNT<>@Count THROW 51607,'The portfolio adjustment history is incomplete.',1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        command.Parameters.AddWithValue("@Count", adjustments.Count);
        command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", source.DocumentType);
        command.Parameters.AddWithValue("@OccurredAt", source.OccurredAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(adjustments);
        await command.ExecuteNonQueryAsync(token);
    }

    private static Dictionary<Guid, decimal> ValidateAcceptedConceptAdjustments(ConfirmManualAccountingVoucherRequest request,
        VoucherFinancialEffects effects, IReadOnlyDictionary<Guid, ManualVoucherLineRequest> raw,
        HashSet<Guid> claimed, IReadOnlySet<Guid> conceptIds)
    {
        var amounts = new Dictionary<Guid, decimal>();
        foreach (var effect in effects.Positions.Where(item => item.EffectKind == "Adjustment"))
        {
            var payable = effect.SubledgerKind == AccountingSubledgerKinds.Payable;
            if (request.VoucherTypeCode is not ("Accounting" or "CashReceipt" or "Disbursement") ||
                effect.SubledgerKind is not (AccountingSubledgerKinds.Payable or AccountingSubledgerKinds.Receivable) ||
                effect.EffectId == Guid.Empty || effect.PositionId == Guid.Empty || effect.GroupId == Guid.Empty || effect.Amount <= 0m ||
                effect.DueDate is not null || effect.DocumentReference is not null || effect.TargetSourceLineId is not { } targetId ||
                !claimed.Add(effect.SourceLineId) || !raw.TryGetValue(effect.SourceLineId, out var concept) ||
                !raw.TryGetValue(targetId, out var target) || concept.FinancialConceptId is not { } conceptId || !conceptIds.Contains(conceptId) ||
                concept.FinancialConceptTargetLineId != targetId || concept.ExistingPosition is not null || concept.PaymentEvidence is not null ||
                concept.DueDate is not null || concept.FinancialDiscountEarned is not null ||
                concept.FinancialGroupId != effect.GroupId || target.FinancialGroupId != effect.GroupId ||
                concept.PartyId != effect.PartyId || target.PartyId != effect.PartyId || target.AccountId != effect.AccountId ||
                target.ExistingPosition is not { ReversesTransactionId: null } binding ||
                binding.SubledgerKind != effect.SubledgerKind || binding.SubledgerId != effect.PositionId ||
                effect.Amount != (payable ? concept.Credit : concept.Debit) || (payable ? concept.Debit : concept.Credit) != 0m)
                throw new InvalidOperationException("An accepted concept adjustment differs from its immutable source lines.");
            amounts[targetId] = amounts.GetValueOrDefault(targetId) + effect.Amount;
        }
        return amounts;
    }
}
