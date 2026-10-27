using System.Data;
using System.Text.Json;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingPostingProcessor
{
    private async Task ApplyCashShortageClaimAsync(
        SqlConnection connection, SqlTransaction transaction,
        WorkSessionClosureReconciliationPayload payload, WorkSessionCashShortageClaim claim,
        CancellationToken token)
    {
        await using var command = new SqlCommand(DeclareReceivableOpeningsSql + """
            IF NOT EXISTS(SELECT 1 FROM dbo.Parties
              WHERE PartyId=@PartyId AND TenantId=@TenantId)
              THROW 51607,'The accepted shortage debtor is outside the tenant.',1;

            IF EXISTS(SELECT 1 FROM dbo.Receivables WITH(UPDLOCK,HOLDLOCK)
              WHERE ReceivableId=@ReceivableId)
            BEGIN
              -- Before this cutover the closure acceptance wrote this pair.
              -- Preserve a matching historical obligation and its later payments.
              IF EXISTS(SELECT 1 FROM dbo.Receivables
                WHERE ReceivableId=@ReceivableId AND
                  (BusinessId<>@BusinessId OR PartyId IS NULL OR PartyId<>@PartyId
                   OR CustomerId IS NOT NULL OR SourceDocumentId<>@ReconciliationId
                   OR SourceDocumentType<>N'WorkSessionClosureReconciliation'
                   OR CurrencyCode<>N'COP' OR OriginalAmount<>@Amount))
                OR NOT EXISTS(SELECT 1 FROM dbo.ReceivableTransactions
                  WHERE ReceivableId=@ReceivableId AND TransactionType=N'Opening'
                    AND SourceDocumentId=@ReconciliationId AND Amount=@Amount)
                THROW 51607,'The historical shortage obligation does not match its source.',1;
            END
            ELSE
            BEGIN
              INSERT @ReceivableOpenings(ReceivableId,TransactionId,PartyId,DocumentId,DocumentType,Number,Amount,DueDate,OccurredAt)
              VALUES(@ReceivableId,@TransactionId,@PartyId,@ReconciliationId,N'WorkSessionClosureReconciliation',@DocumentNumber,@Amount,@At,@At);
            END;
            """ + OpenReceivablesSql, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", payload.TenantId);
        command.Parameters.AddWithValue("@BusinessId", payload.BusinessId);
        command.Parameters.AddWithValue("@ReceivableId", claim.ReceivableId);
        command.Parameters.AddWithValue("@PartyId", claim.EmployeePartyId);
        command.Parameters.AddWithValue("@ReconciliationId", payload.ReconciliationId);
        command.Parameters.AddWithValue("@DocumentNumber", $"FALT-{payload.WorkSessionClosureId:N}");
        AddMoney(command, "@Amount", claim.Amount);
        command.Parameters.AddWithValue("@At", payload.ReconciledAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        command.Parameters.AddWithValue("@TransactionId", ids.NewId());
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task<IReadOnlyList<Guid>> ApplyClosureCorrectionFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        WorkSessionClosureReconciliationPayload payload, CancellationToken token)
    {
        if (payload.CashShortageClaim is { } claim)
            await ApplyCashShortageClaimAsync(connection, transaction, payload, claim, token);
        var changes = (payload.PaymentCorrections ?? [])
            .Where(item => item.Amount != item.OriginalAmount)
            .Select(item => new
            {
                item.MovementType, item.SourceId, item.SubledgerId, item.CustomerId,
                Delta = item.Amount - item.OriginalAmount,
                AdjustmentId = ids.NewId(), NewReceivableId = ids.NewId(),
                NewOpeningId = ids.NewId(), NewCreditId = ids.NewId(), NewCreditTransactionId = ids.NewId()
            }).ToArray();
        if (changes.Length == 0) return [];

        await using var command = new SqlCommand(DeclareReceivableOpeningsSql + DeclareCustomerCreditOpeningsSql +
            DeclarePortfolioBalanceChangesSql + """
            CREATE TABLE #Changes(MovementType nvarchar(32),SourceId uniqueidentifier,
              SubledgerId uniqueidentifier,CustomerId uniqueidentifier,Delta decimal(19,4),
              AdjustmentId uniqueidentifier,NewReceivableId uniqueidentifier,
              NewOpeningId uniqueidentifier,NewCreditId uniqueidentifier,NewCreditTransactionId uniqueidentifier);
            INSERT #Changes SELECT MovementType,SourceId,SubledgerId,CustomerId,Delta,
              AdjustmentId,NewReceivableId,NewOpeningId,NewCreditId,NewCreditTransactionId
            FROM OPENJSON(@Rows) WITH(MovementType nvarchar(32),SourceId uniqueidentifier,
              SubledgerId uniqueidentifier,CustomerId uniqueidentifier,Delta decimal(19,4),
              AdjustmentId uniqueidentifier,NewReceivableId uniqueidentifier,
              NewOpeningId uniqueidentifier,NewCreditId uniqueidentifier,NewCreditTransactionId uniqueidentifier);
            IF @@ROWCOUNT<>@Count THROW 51607,'The closure correction payload is incomplete.',1;
            IF EXISTS(SELECT 1 FROM #Changes WHERE Delta=0 OR MovementType NOT IN
              (N'Sale',N'ReceivablePayment',N'PayablePayment',N'Refund',N'CashIn',N'CashOut'))
              THROW 51607,'The closure correction type is invalid.',1;

            CREATE TABLE #Receivable(ReceivableId uniqueidentifier PRIMARY KEY,
              Adjustment decimal(19,4),AdjustmentId uniqueidentifier);
            INSERT #Receivable SELECT SubledgerId,-SUM(Delta),
              CONVERT(uniqueidentifier,MIN(CONVERT(nvarchar(36),AdjustmentId)))
            FROM #Changes WHERE MovementType IN(N'Sale',N'ReceivablePayment')
              AND SubledgerId IS NOT NULL GROUP BY SubledgerId;
            DELETE FROM #Receivable WHERE Adjustment=0;
            INSERT @PortfolioChanges SELECT ReceivableId,Adjustment FROM #Receivable;
            """ + PortfolioBalanceChangesSql("Receivables", "ReceivableId", PortfolioBalanceChangeKind.Adjustment) + """
            INSERT dbo.ReceivableTransactions(ReceivableTransactionId,ReceivableId,
              TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
            SELECT AdjustmentId,ReceivableId,N'Adjustment',Adjustment,@ReconciliationId,@At,@Now
            FROM #Receivable;

            CREATE TABLE #NewReceivable(SourceId uniqueidentifier PRIMARY KEY,
              CustomerId uniqueidentifier,Adjustment decimal(19,4),
              ReceivableId uniqueidentifier,OpeningId uniqueidentifier);
            INSERT #NewReceivable SELECT SourceId,
              CONVERT(uniqueidentifier,MIN(CONVERT(nvarchar(36),CustomerId))),-SUM(Delta),
              CONVERT(uniqueidentifier,MIN(CONVERT(nvarchar(36),NewReceivableId))),
              CONVERT(uniqueidentifier,MIN(CONVERT(nvarchar(36),NewOpeningId)))
            FROM #Changes WHERE MovementType=N'Sale' AND SubledgerId IS NULL
            GROUP BY SourceId;
            DELETE FROM #NewReceivable WHERE Adjustment=0;
            IF EXISTS(SELECT 1 FROM #NewReceivable WHERE Adjustment<=0 OR CustomerId IS NULL)
              THROW 51607,'The sale cannot open an unidentified customer balance.',1;
            INSERT @ReceivableOpenings(ReceivableId,TransactionId,CustomerId,PartySiteId,DocumentId,DocumentType,
              Number,Amount,DueDate,OccurredAt,TransactionSourceId)
            SELECT input.ReceivableId,input.OpeningId,input.CustomerId,sale.CustomerPartySiteId,
              sale.DocumentId,sale.DocumentType,sale.DocumentNumber,
              input.Adjustment,@At,@At,@ReconciliationId
            FROM #NewReceivable input JOIN dbo.SalesDocuments sale WITH(UPDLOCK,HOLDLOCK)
              ON sale.DocumentId=input.SourceId AND sale.BusinessId=@BusinessId
                AND sale.CustomerId=input.CustomerId AND sale.CustomerPartySiteId IS NOT NULL
            WHERE NOT EXISTS(SELECT 1 FROM dbo.Receivables existing WITH(UPDLOCK,HOLDLOCK)
              WHERE existing.SourceDocumentId=sale.DocumentId AND existing.SourceDocumentType=sale.DocumentType);
            IF @@ROWCOUNT<>(SELECT COUNT(*) FROM #NewReceivable)
              THROW 51607,'The sale balance changed after closure acceptance.',1;
            """ + OpenReceivablesSql + """
            CREATE TABLE #Payable(PayableId uniqueidentifier PRIMARY KEY,
              Adjustment decimal(19,4),AdjustmentId uniqueidentifier);
            INSERT #Payable SELECT SubledgerId,SUM(Delta),
              CONVERT(uniqueidentifier,MIN(CONVERT(nvarchar(36),AdjustmentId)))
            FROM #Changes WHERE MovementType=N'PayablePayment' GROUP BY SubledgerId;
            DELETE FROM #Payable WHERE Adjustment=0;
            DELETE FROM @PortfolioChanges;
            INSERT @PortfolioChanges SELECT PayableId,Adjustment FROM #Payable;
            """ + PortfolioBalanceChangesSql("Payables", "PayableId", PortfolioBalanceChangeKind.Adjustment) + """
            INSERT dbo.PayableTransactions(PayableTransactionId,PayableId,
              TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
            SELECT AdjustmentId,PayableId,N'Adjustment',Adjustment,@ReconciliationId,@At,@Now
            FROM #Payable;

            INSERT @CustomerCreditOpenings(CreditId,TransactionId,CustomerId,ReturnId,Nature,Currency,Amount)
            SELECT input.NewCreditId,input.NewCreditTransactionId,input.CustomerId,returned.ReturnId,
              N'Credit',N'COP',input.Delta
            FROM #Changes input JOIN dbo.SalesReturns returned WITH(UPDLOCK,HOLDLOCK)
              ON input.MovementType=N'Refund' AND returned.ReturnId=input.SourceId
                AND returned.BusinessId=@BusinessId AND returned.CustomerId=input.CustomerId
                AND returned.TotalAmount>=input.Delta
            WHERE input.Delta>0 AND NOT EXISTS(SELECT 1 FROM dbo.CustomerCredits credit WITH(UPDLOCK,HOLDLOCK)
              WHERE credit.SourceReturnId=returned.ReturnId);
            IF @@ROWCOUNT<>(SELECT COUNT(*) FROM #Changes WHERE MovementType=N'Refund')
              THROW 51607,'The return credit changed after closure acceptance.',1;

            """ + OpenCustomerCreditsSql, connection, transaction);
        command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(changes);
        command.Parameters.AddWithValue("@Count", changes.Length);
        command.Parameters.AddWithValue("@BusinessId", payload.BusinessId);
        command.Parameters.AddWithValue("@ReconciliationId", payload.ReconciliationId);
        command.Parameters.AddWithValue("@CreditParentDocumentId", payload.ReconciliationId);
        command.Parameters.AddWithValue("@CreditParentDocumentType", WorkSessionAccountingDocumentTypes.ClosureReconciliation);
        command.Parameters.AddWithValue("@CreditAt", payload.ReconciledAt);
        command.Parameters.AddWithValue("@At", payload.ReconciledAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(token);
        return changes.Where(item => item.CustomerId.HasValue && item.MovementType is "Sale" or "ReceivablePayment" or "Refund")
            .Select(item => item.CustomerId!.Value).Distinct().ToArray();
    }
}
