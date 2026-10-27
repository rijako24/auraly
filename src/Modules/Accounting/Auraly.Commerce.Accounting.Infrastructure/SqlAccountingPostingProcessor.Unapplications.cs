using System.Data;
using System.Text.Json;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingPostingProcessor
{
    private static bool HasAcceptedAdvanceFunding(VoucherFinancialPositionEffect opening, VoucherFinancialEffects effects) =>
        effects.Payments.Any(group => group.GroupId == opening.GroupId && group.AdvancePositionId == opening.PositionId &&
            group.AdvanceAmount == opening.Amount && group.PartyId == opening.PartyId &&
            group.CounterpartyRoleId == opening.CounterpartyRoleId && group.PartySiteId == opening.PartySiteId &&
            group.SubledgerKind == (opening.SubledgerKind == "SupplierCredit" ? AccountingSubledgerKinds.Payable : AccountingSubledgerKinds.Receivable)) ||
        (effects.Unapplications ?? []).Any(item => item.ReversedTransactionType is null &&
            item.AdvancePositionId == opening.PositionId && item.GroupId == opening.GroupId);

    private static void ValidateAcceptedUnapplications(ConfirmManualAccountingVoucherRequest request,
        VoucherFinancialEffects effects, IReadOnlyDictionary<Guid, ManualVoucherLineRequest> raw, HashSet<Guid> claimed)
    {
        var reversals = effects.Unapplications ?? [];
        if (reversals.Count == 0) return;
        if (request.VoucherTypeCode != "Accounting")
            throw new InvalidOperationException("Unapplying real money requires an accounting voucher, not another disbursement.");
        foreach (var item in reversals)
        {
            var payable = item.SubledgerKind == AccountingSubledgerKinds.Payable;
            if (item.ReversedTransactionType is not (null or "CreditApplication" or "PaymentReturn") ||
                item.SubledgerKind is not (AccountingSubledgerKinds.Payable or AccountingSubledgerKinds.Receivable) ||
                item.EffectId == Guid.Empty || item.GroupId == Guid.Empty || item.ReversesTransactionId == Guid.Empty || item.Amount <= 0 ||
                !claimed.Add(item.PositionSourceLineId) || !raw.TryGetValue(item.PositionSourceLineId, out var line) ||
                line.ExistingPosition is not { } target || target.SubledgerKind != item.SubledgerKind || target.SubledgerId != item.PositionId ||
                target.ReversesTransactionId != item.ReversesTransactionId || line.PaymentEvidence is not null ||
                item.Amount != (payable ? line.Credit : line.Debit) || (payable ? line.Debit : line.Credit) != 0 ||
                line.FinancialGroupId is { } group && group != item.GroupId ||
                (item.ReversedTransactionType is null && !effects.Positions.Any(advance => advance.PositionId == item.AdvancePositionId && advance.GroupId == item.GroupId &&
                    advance.SourceLineId == item.AdvanceSourceLineId && advance.PartyId == line.PartyId &&
                    advance.SubledgerKind == (payable ? AccountingSubledgerKinds.SupplierCredit : AccountingSubledgerKinds.CustomerCredit))) ||
                (item.ReversedTransactionType == "CreditApplication" &&
                    (item.AdvanceSourceLineId is not { } creditLineId || !raw.TryGetValue(creditLineId, out var credit) || credit.PartyId != line.PartyId ||
                     credit.ExistingPosition is not { ReversesTransactionId: null } creditTarget || creditTarget.SubledgerId != item.AdvancePositionId ||
                     creditTarget.SubledgerKind != (payable ? AccountingSubledgerKinds.SupplierCredit : AccountingSubledgerKinds.CustomerCredit))) ||
                item.ReversedTransactionType == "PaymentReturn" && (item.AdvancePositionId is not null || item.AdvanceSourceLineId is not null))
                throw new InvalidOperationException("The accepted unapplication does not match its original transaction and advance lines.");
        }
        foreach (var group in reversals.GroupBy(item => (item.AdvancePositionId, item.GroupId)))
        {
            if (group.First().ReversedTransactionType == "PaymentReturn")
            {
                var first = group.First();
                var payable = first.SubledgerKind == AccountingSubledgerKinds.Payable;
                var partyId = raw[first.PositionSourceLineId].PartyId;
                var movements = effects.DirectMovements.Where(item => item.GroupId == first.GroupId).ToArray();
                if (group.Any(item => item.ReversedTransactionType != "PaymentReturn" || item.SubledgerKind != first.SubledgerKind ||
                        raw[item.PositionSourceLineId].PartyId != partyId) || movements.Length == 0 ||
                    movements.Any(item => payable ? item.Amount <= 0 : item.Amount >= 0) ||
                    movements.Sum(item => payable ? item.Amount : -item.Amount) != group.Sum(item => item.Amount) ||
                    effects.Positions.Any(item => item.GroupId == first.GroupId) ||
                    effects.Payments.Any(item => item.GroupId == first.GroupId) ||
                    effects.CreditApplications.Any(item => item.GroupId == first.GroupId))
                    throw new InvalidOperationException("Returning an applied payment requires its exact debt and money counterparts, without a new advance or payment.");
                continue;
            }
            if (group.First().ReversedTransactionType == "CreditApplication")
            {
                var first = group.First();
                var credit = raw[first.AdvanceSourceLineId!.Value];
                var payable = first.SubledgerKind == AccountingSubledgerKinds.Payable;
                if (group.Any(item => item.ReversedTransactionType != "CreditApplication" || item.GroupId != first.GroupId ||
                        item.AdvanceSourceLineId != first.AdvanceSourceLineId || item.SubledgerKind != first.SubledgerKind) ||
                    !claimed.Add(first.AdvanceSourceLineId.Value) || credit.PaymentEvidence is not null ||
                    credit.FinancialGroupId is { } creditGroup && creditGroup != first.GroupId ||
                    group.Sum(item => item.Amount) != (payable ? credit.Debit : credit.Credit) || (payable ? credit.Credit : credit.Debit) != 0 ||
                    effects.Positions.Any(item => item.GroupId == first.GroupId) ||
                    effects.Payments.Any(item => item.GroupId == first.GroupId) ||
                    effects.CreditApplications.Any(item => item.GroupId == first.GroupId) ||
                    effects.DirectMovements.Any(item => item.GroupId == first.GroupId))
                    throw new InvalidOperationException("An unapplied credit must be restored to its original position, without a new credit or money movement.");
                continue;
            }
            var opening = effects.Positions.Single(item => item.PositionId == group.Key.AdvancePositionId);
            if (group.Sum(item => item.Amount) != opening.Amount ||
                effects.Payments.Any(item => item.GroupId == opening.GroupId) ||
                effects.CreditApplications.Any(item => item.GroupId == opening.GroupId) ||
                effects.DirectMovements.Any(item => item.GroupId == opening.GroupId))
                throw new InvalidOperationException("An unapplied payment must be conserved entirely as its advance, without another money movement.");
        }
    }

    private async Task<IReadOnlyList<Guid>> ApplyUnapplicationsAsync(SqlConnection connection, SqlTransaction transaction,
        SourceEnvelope source, IReadOnlyList<VoucherFinancialUnapplication> reversals, CancellationToken token)
    {
        var customers = new HashSet<Guid>();
        foreach (var kind in new[] { AccountingSubledgerKinds.Payable, AccountingSubledgerKinds.Receivable })
        {
            var inputs = reversals.Where(item => item.SubledgerKind == kind).ToArray();
            if (inputs.Length == 0) continue;
            var receivable = kind == AccountingSubledgerKinds.Receivable;
            var positions = receivable ? "Receivables" : "Payables";
            var positionId = receivable ? "ReceivableId" : "PayableId";
            var transactions = receivable ? "ReceivableTransactions" : "PayableTransactions";
            var transactionId = receivable ? "ReceivableTransactionId" : "PayableTransactionId";
            var creditTable = receivable ? "CustomerCredits" : "SupplierCredits";
            var creditId = receivable ? "CustomerCreditId" : "SupplierCreditId";
            var creditTransactions = receivable ? "CustomerCreditTransactions" : "SupplierCreditTransactions";
            var creditTransactionId = receivable ? "CustomerCreditTransactionId" : "SupplierCreditTransactionId";
            await using var command = new SqlCommand(DeclarePortfolioBalanceChangesSql + DeclareCreditBalanceChangesSql + $"""
                DECLARE @Unapplications TABLE(EffectId uniqueidentifier PRIMARY KEY,PositionId uniqueidentifier,
                  ReversesTransactionId uniqueidentifier,PositionSourceLineId uniqueidentifier,Amount decimal(19,4),
                  ReversedTransactionType nvarchar(24),AdvancePositionId uniqueidentifier,AdvanceSourceLineId uniqueidentifier);
                INSERT @Unapplications SELECT EffectId,PositionId,ReversesTransactionId,PositionSourceLineId,Amount,
                  ReversedTransactionType,AdvancePositionId,AdvanceSourceLineId
                  FROM OPENJSON(@Rows) WITH(EffectId uniqueidentifier,PositionId uniqueidentifier,ReversesTransactionId uniqueidentifier,
                    PositionSourceLineId uniqueidentifier,Amount decimal(19,4),ReversedTransactionType nvarchar(24),
                    AdvancePositionId uniqueidentifier,AdvanceSourceLineId uniqueidentifier);
                IF @@ROWCOUNT<>@Count THROW 51850,'The unapplication batch is incomplete.',1;
                DECLARE @Ready TABLE(EffectId uniqueidentifier PRIMARY KEY,PositionId uniqueidentifier,
                  ReversesTransactionId uniqueidentifier,PositionSourceLineId uniqueidentifier,Amount decimal(19,4),CustomerId uniqueidentifier,
                  ReversedTransactionType nvarchar(24),CreditId uniqueidentifier,CreditSourceLineId uniqueidentifier,
                  CreditTransactionId uniqueidentifier,CreditCustomerId uniqueidentifier);
                INSERT @Ready SELECT input.EffectId,input.PositionId,input.ReversesTransactionId,input.PositionSourceLineId,input.Amount,
                  CASE WHEN @Kind=N'Receivable' THEN original.CounterpartyRoleId END,original.TransactionType,
                  original.CreditId,input.AdvanceSourceLineId,original.CreditTransactionId,original.CreditCustomerId
                FROM @Unapplications input JOIN accounting.AppliedPortfolioSettlements original
                  ON original.TransactionId=input.ReversesTransactionId AND original.PositionId=input.PositionId AND original.SubledgerKind=@Kind
                    AND original.BusinessId=@BusinessId AND original.TenantId=@TenantId AND original.CurrencyCode=N'COP'
                    AND original.TransactionType=CASE WHEN input.ReversedTransactionType=N'CreditApplication' THEN N'CreditApplication' ELSE N'Payment' END
                    AND (input.ReversedTransactionType<>N'CreditApplication' OR input.ReversedTransactionType IS NULL OR original.CreditId=input.AdvancePositionId)
                    AND original.Amount>=input.Amount AND CONVERT(date,
                      SWITCHOFFSET(original.OccurredAt,DATEPART(TZOFFSET,@At)))<=CONVERT(date,@At);
                IF @@ROWCOUNT<>@Count
                  THROW 51850,'The accepted unapplication no longer matches its original applied payment.',1;
                IF @Kind=N'Payable' AND EXISTS(
                  SELECT 1 FROM @Ready ready
                  JOIN dbo.PayableTransactions original ON original.PayableTransactionId=ready.ReversesTransactionId
                    AND original.PayableId=ready.PositionId AND original.TransactionType=N'Payment'
                  JOIN dbo.SupplierPayments payment ON payment.PaymentId=original.SourceDocumentId
                    AND payment.BusinessId=@BusinessId
                  JOIN dbo.SupplierPaymentApplications application ON application.PaymentId=payment.PaymentId
                    AND application.PayableId=ready.PositionId AND application.FinancialDiscountEarned IS NOT NULL
                  WHERE ready.ReversedTransactionType=N'Payment')
                  THROW 51850,N'No se desaplicó el pago: tiene una decisión de descuento financiero. El pago y la obligación se conservan.',1;
                INSERT @PortfolioChanges SELECT PositionId,SUM(Amount) FROM @Ready GROUP BY PositionId;
                """ + PortfolioBalanceChangesSql(positions, positionId, PortfolioBalanceChangeKind.Adjustment) + $"""
                INSERT dbo.[{transactions}]([{transactionId}],[{positionId}],TransactionType,Amount,
                  SourceDocumentId,SourceDocumentType,SourceLineId,ReversesTransactionId,OccurredAt,CreatedAt)
                SELECT EffectId,PositionId,CASE ReversedTransactionType WHEN N'CreditApplication' THEN N'CreditUnapplication' ELSE N'Unapplication' END,
                  Amount,@DocumentId,@DocumentType,PositionSourceLineId,ReversesTransactionId,@At,@Now
                  FROM @Ready;
                IF @@ROWCOUNT<>@Count THROW 51850,'The unapplication history is incomplete.',1;
                INSERT @CreditChanges SELECT CreditId,SUM(Amount) FROM @Ready WHERE ReversedTransactionType=N'CreditApplication' GROUP BY CreditId;
                """ + CreditBalanceChangesSql(creditTable, creditId) + $"""
                INSERT dbo.[{creditTransactions}]([{creditTransactionId}],[{creditId}],TransactionType,Amount,
                  SourceDocumentId,SourceDocumentType,SourceLineId,AppliedPositionTransactionId,ReversesTransactionId,OccurredAt,CreatedAt)
                SELECT EffectId,CreditId,N'Unapplication',Amount,@DocumentId,@DocumentType,CreditSourceLineId,EffectId,CreditTransactionId,@At,@Now
                  FROM @Ready WHERE ReversedTransactionType=N'CreditApplication';
                IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @Ready WHERE ReversedTransactionType=N'CreditApplication')
                  THROW 51850,'The restored credit history is incomplete.',1;
                SELECT CustomerId FROM @Ready WHERE CustomerId IS NOT NULL
                  UNION SELECT CreditCustomerId FROM @Ready WHERE CreditCustomerId IS NOT NULL;
                """, connection, transaction);
            command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(inputs);
            command.Parameters.AddWithValue("@Count", inputs.Length);
            command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
            command.Parameters.AddWithValue("@TenantId", source.TenantId);
            command.Parameters.AddWithValue("@Kind", kind);
            command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
            command.Parameters.AddWithValue("@DocumentType", source.DocumentType);
            command.Parameters.AddWithValue("@At", source.OccurredAt);
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) if (!reader.IsDBNull(0)) customers.Add(reader.GetGuid(0));
        }
        return customers.ToArray();
    }
}
