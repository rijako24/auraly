using System.Data;
using System.Text.Json;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingPostingProcessor
{
    // This proves correspondence with the frozen capture. Group inference and
    // permission decisions belong to normalization at acceptance, not posting.
    private static void ValidateAcceptedCreditApplications(ConfirmManualAccountingVoucherRequest request,
        VoucherFinancialEffects effects, IReadOnlyDictionary<Guid, ManualVoucherLineRequest> raw, HashSet<Guid> claimed)
    {
        if (effects.CreditApplications.Count == 0) return;
        if (request.VoucherTypeCode != "Accounting")
            throw new InvalidOperationException("An accepted non-cash application requires its accounting voucher.");
        foreach (var application in effects.CreditApplications)
        {
            var payable = application.SubledgerKind == AccountingSubledgerKinds.Payable;
            if (application.SubledgerKind is not (AccountingSubledgerKinds.Payable or AccountingSubledgerKinds.Receivable) ||
                application.EffectId == Guid.Empty || application.GroupId == Guid.Empty || application.PositionId == Guid.Empty ||
                application.CreditId == Guid.Empty || application.Amount <= 0 || !claimed.Add(application.PositionSourceLineId) ||
                !raw.TryGetValue(application.PositionSourceLineId, out var position) ||
                !raw.TryGetValue(application.CreditSourceLineId, out var credit) ||
                position.ExistingPosition is not { } positionTarget || positionTarget.SubledgerKind != application.SubledgerKind ||
                positionTarget.SubledgerId != application.PositionId || position.PartyId is null || position.PartyId != credit.PartyId ||
                application.Amount != (payable ? position.Debit : position.Credit) || (payable ? position.Credit : position.Debit) != 0 ||
                credit.ExistingPosition is not { } creditTarget || creditTarget.SubledgerId != application.CreditId ||
                creditTarget.SubledgerKind != (payable ? AccountingSubledgerKinds.SupplierCredit : AccountingSubledgerKinds.CustomerCredit) ||
                position.FinancialGroupId is { } positionGroup && positionGroup != application.GroupId ||
                credit.FinancialGroupId is { } creditGroup && creditGroup != application.GroupId ||
                position.PaymentEvidence is not null || credit.PaymentEvidence is not null)
                throw new InvalidOperationException("An accepted credit application does not match its immutable source lines.");
        }
        foreach (var creditApplications in effects.CreditApplications.GroupBy(item => item.CreditSourceLineId))
        {
            var line = raw[creditApplications.Key];
            var payable = creditApplications.First().SubledgerKind == AccountingSubledgerKinds.Payable;
            if (!claimed.Add(creditApplications.Key) || creditApplications.Select(item => item.GroupId).Distinct().Count() != 1 ||
                creditApplications.Sum(item => item.Amount) != (payable ? line.Credit : line.Debit) ||
                (payable ? line.Debit : line.Credit) != 0)
                throw new InvalidOperationException("The accepted credit amount differs from its source line.");
        }
    }

    private static async Task LockManualFinancialPositionsAsync(SqlConnection connection, SqlTransaction transaction,
        SourceEnvelope source, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            DECLARE @Expected TABLE(EffectId uniqueidentifier,Kind nvarchar(16),PositionId uniqueidentifier,
              SourceLineId uniqueidentifier,Amount decimal(19,4),EffectKind nvarchar(24),ReversesTransactionId uniqueidentifier,PRIMARY KEY(EffectId,Kind));
            INSERT @Expected SELECT EffectId,SubledgerKind,SubledgerId,SourceLineId,Amount,EffectKind,ReversesTransactionId
              FROM accounting.ManualVoucherCreditTargets(@Payload);
            IF EXISTS(SELECT EffectId,Kind,PositionId,SourceLineId,Amount,EffectKind,ReversesTransactionId FROM @Expected
                EXCEPT SELECT EffectId,SubledgerKind,SubledgerId,SourceLineId,Amount,EffectKind,ReversesTransactionId FROM accounting.SourcePositionIndex
                  WHERE SourceDocumentId=@DocumentId AND SourceDocumentType=@DocumentType AND BusinessId=@BusinessId AND TenantId=@TenantId)
              OR EXISTS(SELECT EffectId,SubledgerKind,SubledgerId,SourceLineId,Amount,EffectKind,ReversesTransactionId FROM accounting.SourcePositionIndex
                  WHERE SourceDocumentId=@DocumentId AND SourceDocumentType=@DocumentType AND BusinessId=@BusinessId AND TenantId=@TenantId
                EXCEPT SELECT EffectId,Kind,PositionId,SourceLineId,Amount,EffectKind,ReversesTransactionId FROM @Expected)
              THROW 51850,'The accepted credit destination index is incomplete or inconsistent.',1;
            DECLARE @Keys TABLE(Kind nvarchar(16),PositionId uniqueidentifier,PRIMARY KEY(Kind,PositionId));
            INSERT @Keys SELECT Kind,PositionId FROM @Expected
              UNION SELECT payment.SubledgerKind,input.SubledgerId FROM OPENJSON(@Payload,'$.FinancialEffects.Payments')
                WITH(SubledgerKind nvarchar(16),Allocations nvarchar(max) AS JSON) payment
                CROSS APPLY OPENJSON(payment.Allocations) WITH(SubledgerId uniqueidentifier) input;
            DECLARE @Locked uniqueidentifier;
            SELECT @Locked=p.PayableId FROM @Keys input JOIN dbo.Payables p WITH(UPDLOCK,HOLDLOCK)
              ON input.Kind=N'Payable' AND p.PayableId=input.PositionId AND p.BusinessId=@BusinessId
              ORDER BY input.PositionId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
            SELECT @Locked=p.ReceivableId FROM @Keys input JOIN dbo.Receivables p WITH(UPDLOCK,HOLDLOCK)
              ON input.Kind=N'Receivable' AND p.ReceivableId=input.PositionId AND p.BusinessId=@BusinessId
              ORDER BY input.PositionId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
            SELECT @Locked=p.SupplierCreditId FROM @Keys input JOIN dbo.SupplierCredits p WITH(UPDLOCK,HOLDLOCK)
              ON input.Kind=N'SupplierCredit' AND p.SupplierCreditId=input.PositionId AND p.BusinessId=@BusinessId
              ORDER BY input.PositionId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
            SELECT @Locked=p.CustomerCreditId FROM @Keys input JOIN dbo.CustomerCredits p WITH(UPDLOCK,HOLDLOCK)
              ON input.Kind=N'CustomerCredit' AND p.CustomerCreditId=input.PositionId AND p.BusinessId=@BusinessId
              ORDER BY input.PositionId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
            """, connection, transaction);
        command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = source.PayloadJson;
        command.Parameters.AddWithValue("@TenantId", source.TenantId);
        command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", source.DocumentType);
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task<IReadOnlyList<Guid>> ApplyCreditApplicationsAsync(SqlConnection connection, SqlTransaction transaction,
        SourceEnvelope source, IReadOnlyList<VoucherFinancialCreditApplication> applications,
        IReadOnlyDictionary<Guid, ManualVoucherLineRequest> raw, CancellationToken token)
    {
        if (applications.Count == 0) return [];
        var affectedCustomers = new HashSet<Guid>();
        foreach (var kind in new[] { AccountingSubledgerKinds.Payable, AccountingSubledgerKinds.Receivable })
        {
            var rows = applications.Where(item => item.SubledgerKind == kind).ToArray();
            if (rows.Length == 0) continue;
            var receivable = kind == AccountingSubledgerKinds.Receivable;
            var positionTable = receivable ? "Receivables" : "Payables";
            var positionId = receivable ? "ReceivableId" : "PayableId";
            var positionTransactions = receivable ? "ReceivableTransactions" : "PayableTransactions";
            var positionTransactionId = receivable ? "ReceivableTransactionId" : "PayableTransactionId";
            var creditTable = receivable ? "CustomerCredits" : "SupplierCredits";
            var creditId = receivable ? "CustomerCreditId" : "SupplierCreditId";
            var creditTransactions = receivable ? "CustomerCreditTransactions" : "SupplierCreditTransactions";
            var creditTransactionId = receivable ? "CustomerCreditTransactionId" : "SupplierCreditTransactionId";
            var partyJoins = receivable ? """
                LEFT JOIN dbo.Customers debtor ON debtor.CustomerId=position.CustomerId AND debtor.TenantId=@TenantId
                JOIN dbo.Parties debtorParty ON debtorParty.PartyId=COALESCE(position.PartyId,debtor.PartyId)
                  AND debtorParty.TenantId=@TenantId AND debtorParty.PartyId=input.PartyId
                LEFT JOIN dbo.Customers creditCustomer ON creditCustomer.CustomerId=credit.CustomerId AND creditCustomer.TenantId=@TenantId
                JOIN dbo.Parties creditParty ON creditParty.PartyId=COALESCE(credit.PartyId,creditCustomer.PartyId)
                  AND creditParty.TenantId=@TenantId AND creditParty.PartyId=input.PartyId
                """ : """
                JOIN dbo.Suppliers debtor ON debtor.SupplierId=position.SupplierId AND debtor.TenantId=@TenantId AND debtor.PartyId=input.PartyId
                JOIN dbo.Suppliers creditSupplier ON creditSupplier.SupplierId=credit.SupplierId AND creditSupplier.TenantId=@TenantId
                  AND creditSupplier.SupplierId=position.SupplierId
                  AND (credit.PartySiteId=position.PartySiteId OR credit.PartySiteId IS NULL AND position.PartySiteId IS NULL)
                """;
            await using var command = new SqlCommand($"""
                DECLARE @Inputs TABLE(TransactionId uniqueidentifier PRIMARY KEY,PositionId uniqueidentifier,CreditId uniqueidentifier,
                  PositionSourceLineId uniqueidentifier,CreditSourceLineId uniqueidentifier,Amount decimal(19,4),PartyId uniqueidentifier);
                INSERT @Inputs SELECT * FROM OPENJSON(@Rows) WITH(TransactionId uniqueidentifier '$.EffectId',PositionId uniqueidentifier,
                  CreditId uniqueidentifier,PositionSourceLineId uniqueidentifier,CreditSourceLineId uniqueidentifier,Amount decimal(19,4),PartyId uniqueidentifier);
                IF (SELECT COUNT(*) FROM @Inputs)<>@Count THROW 51850,'The credit application batch is incomplete.',1;
                DECLARE @Ready TABLE(TransactionId uniqueidentifier PRIMARY KEY,PositionId uniqueidentifier,CreditId uniqueidentifier,
                  PositionSourceLineId uniqueidentifier,CreditSourceLineId uniqueidentifier,Amount decimal(19,4),
                  PositionCustomerId uniqueidentifier,CreditCustomerId uniqueidentifier);
                INSERT @Ready SELECT input.TransactionId,input.PositionId,input.CreditId,input.PositionSourceLineId,input.CreditSourceLineId,input.Amount,
                  {(receivable ? "position.CustomerId,credit.CustomerId" : "NULL,NULL")}
                FROM @Inputs input JOIN dbo.[{positionTable}] position ON position.[{positionId}]=input.PositionId AND position.BusinessId=@BusinessId
                JOIN dbo.[{creditTable}] credit ON credit.[{creditId}]=input.CreditId AND credit.BusinessId=@BusinessId
                {partyJoins}
                WHERE position.CurrencyCode=N'COP' AND credit.CurrencyCode=N'COP' AND credit.CreditNature IN(N'Credit',N'Advance')
                  AND position.Status IN(N'Open',N'PartiallyPaid') AND credit.Status<>N'Cancelled' AND input.Amount>0;
                IF @@ROWCOUNT<>@Count THROW 51850,'The credit application positions are no longer valid.',1;
                """ + PortfolioBalanceReductionSql(positionTable, positionId) + DeclareCreditBalanceChangesSql + """
                INSERT @CreditChanges SELECT CreditId,-SUM(Amount) FROM @Ready GROUP BY CreditId;
                """ + CreditBalanceChangesSql(creditTable, creditId) + $"""
                INSERT dbo.[{positionTransactions}]([{positionTransactionId}],[{positionId}],TransactionType,Amount,
                  SourceDocumentId,SourceDocumentType,SourceLineId,OccurredAt,CreatedAt)
                SELECT TransactionId,PositionId,N'CreditApplication',Amount,@DocumentId,@DocumentType,PositionSourceLineId,@At,@Now FROM @Ready;
                IF @@ROWCOUNT<>@Count THROW 51850,'The portfolio application history is incomplete.',1;
                INSERT dbo.[{creditTransactions}]([{creditTransactionId}],[{creditId}],TransactionType,Amount,
                  SourceDocumentId,SourceDocumentType,SourceLineId,AppliedPositionTransactionId,OccurredAt,CreatedAt)
                SELECT TransactionId,CreditId,N'Application',Amount,@DocumentId,@DocumentType,CreditSourceLineId,TransactionId,@At,@Now FROM @Ready;
                IF @@ROWCOUNT<>@Count THROW 51850,'The credit application history is incomplete.',1;
                SELECT PositionCustomerId CustomerId FROM @Ready WHERE PositionCustomerId IS NOT NULL
                  UNION SELECT CreditCustomerId FROM @Ready WHERE CreditCustomerId IS NOT NULL;
                """, connection, transaction);
            command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(rows.Select(item => new
            {
                item.EffectId, item.PositionId, item.CreditId, item.PositionSourceLineId, item.CreditSourceLineId, item.Amount,
                PartyId = raw[item.PositionSourceLineId].PartyId
            }));
            command.Parameters.AddWithValue("@Count", rows.Length);
            command.Parameters.AddWithValue("@TenantId", source.TenantId);
            command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
            command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
            command.Parameters.AddWithValue("@DocumentType", source.DocumentType);
            command.Parameters.AddWithValue("@At", source.OccurredAt);
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) affectedCustomers.Add(reader.GetGuid(0));
        }
        return affectedCustomers.ToArray();
    }
}
