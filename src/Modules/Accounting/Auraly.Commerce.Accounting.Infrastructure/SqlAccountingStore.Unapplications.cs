using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingStore
{
    public async Task<UnapplicationCandidate?> GetUnapplicationAsync(AccountingUserIdentity user,
        string kind, Guid positionId, Guid transactionId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT opening.AccountId,account.Code,account.Name,original.PartyId,original.PartyName,original.PartyIdentification,
              original.PartySiteId,original.DocumentNumber,original.CurrencyCode,available.AvailableAmount,opening.Issue,
              original.CreditId,creditOpening.AccountId,creditAccount.Code,creditAccount.Name,creditOpening.Issue,
              obligation.PendingCancellationCount,obligation.SourceCancelled,
              CASE WHEN @Kind=N'Payable' AND EXISTS(
                SELECT 1 FROM dbo.PayableTransactions transactionValue
                JOIN dbo.SupplierPayments payment ON payment.PaymentId=transactionValue.SourceDocumentId
                  AND payment.BusinessId=@BusinessId
                JOIN dbo.SupplierPaymentApplications application ON application.PaymentId=payment.PaymentId
                  AND application.PayableId=original.PositionId
                WHERE transactionValue.PayableTransactionId=original.TransactionId
                  AND transactionValue.PayableId=original.PositionId
                  AND application.FinancialDiscountEarned IS NOT NULL)
                THEN CONVERT(bit,1) ELSE CONVERT(bit,0) END FinancialDiscountDecided
            FROM accounting.AppliedPortfolioSettlements original
            CROSS APPLY accounting.ObligationOpeningAccount(@TenantId,@BusinessId,@Kind,
              original.OpeningEntryId,original.OpeningEntryLineNumber,original.PartyId) opening
            LEFT JOIN dbo.AccountingAccounts account ON account.AccountId=opening.AccountId AND account.TenantId=@TenantId
            OUTER APPLY (SELECT selected.* FROM accounting.ObligationOpeningAccount(@TenantId,@BusinessId,
              CASE @Kind WHEN N'Payable' THEN N'SupplierCredit' ELSE N'CustomerCredit' END,
              original.CreditOpeningEntryId,original.CreditOpeningEntryLineNumber,original.PartyId) selected
              WHERE original.CreditId IS NOT NULL) creditOpening
            LEFT JOIN dbo.AccountingAccounts creditAccount ON creditAccount.AccountId=creditOpening.AccountId AND creditAccount.TenantId=@TenantId
            CROSS APPLY accounting.UnapplicationAvailability(@BusinessId,@Kind,original.TransactionId,original.Amount) available
            CROSS APPLY accounting.ObligationAvailability(@BusinessId,@Kind,original.PositionId,0,NULL) obligation
            WHERE original.TransactionId=@TransactionId AND original.PositionId=@PositionId AND original.SubledgerKind=@Kind
              AND original.BusinessId=@BusinessId AND original.TenantId=@TenantId AND original.PositionStatus<>N'Cancelled';
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@Kind", kind);
        command.Parameters.AddWithValue("@PositionId", positionId);
        command.Parameters.AddWithValue("@TransactionId", transactionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var result = new UnapplicationCandidate(NullableId(reader, 0),
            NullableText(reader, 1), NullableText(reader, 2), reader.GetGuid(3), reader.GetString(4),
            NullableText(reader, 5), NullableId(reader, 6), reader.GetString(7), reader.GetString(8), reader.GetDecimal(9), NullableText(reader, 10),
            reader.IsDBNull(11) ? null : new(reader.GetGuid(11), NullableId(reader, 12), NullableText(reader, 13), NullableText(reader, 14), NullableText(reader, 15)),
            reader.GetInt32(16) > 0, reader.GetBoolean(17), reader.GetBoolean(18));
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The applied payment transaction has ambiguous opening or application evidence.");
        return result;
    }
}
