using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingStore
{
    public async Task<PaymentCorrectionCandidate?> GetPaymentCorrectionAsync(AccountingUserIdentity user,
        string kind, Guid paymentId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT TOP (101) original.PositionId,original.TransactionId,opening.AccountId,account.Code,account.Name,
              original.PartyId,original.PartyName,original.PartyIdentification,original.PartySiteId,
              original.DocumentNumber,original.CurrencyCode,available.AvailableAmount,opening.Issue,
              obligation.PendingCancellationCount,obligation.SourceCancelled,
              CASE WHEN @Kind=N'Payable' AND EXISTS(
                SELECT 1 FROM dbo.SupplierPaymentApplications application
                WHERE application.PaymentId=@PaymentId AND application.PayableId=original.PositionId
                  AND application.FinancialDiscountEarned IS NOT NULL)
                THEN CONVERT(bit,1) ELSE CONVERT(bit,0) END FinancialDiscountDecided,
              original.Amount,origin.DocumentNumber
            FROM accounting.AppliedPortfolioPayments original
            JOIN accounting.PortfolioPaymentOrigins origin ON origin.PaymentId=original.PaymentId
              AND origin.SubledgerKind=original.SubledgerKind AND origin.BusinessId=original.BusinessId
              AND origin.TenantId=original.TenantId
            CROSS APPLY accounting.ObligationOpeningAccount(@TenantId,@BusinessId,@Kind,
              original.OpeningEntryId,original.OpeningEntryLineNumber,original.PartyId) opening
            LEFT JOIN dbo.AccountingAccounts account ON account.AccountId=opening.AccountId AND account.TenantId=@TenantId
            CROSS APPLY accounting.UnapplicationAvailability(@BusinessId,@Kind,original.TransactionId,original.Amount) available
            CROSS APPLY accounting.ObligationAvailability(@BusinessId,@Kind,original.PositionId,0,NULL) obligation
            WHERE original.PaymentId=@PaymentId AND original.SubledgerKind=@Kind
              AND original.BusinessId=@BusinessId AND original.TenantId=@TenantId
              AND original.PositionStatus<>N'Cancelled'
            ORDER BY original.PositionId;

            SELECT TOP (101) tender.AccountId,account.Code,account.Name,tender.MethodCode,tender.Amount,tender.LineNumber
            FROM dbo.SupplierPaymentTenders tender
            JOIN dbo.SupplierPayments payment ON payment.PaymentId=tender.PaymentId AND payment.BusinessId=@BusinessId
            LEFT JOIN dbo.AccountingAccounts account ON account.AccountId=tender.AccountId AND account.TenantId=@TenantId
            WHERE @Kind=N'Payable' AND tender.PaymentId=@PaymentId
            UNION ALL
            SELECT TOP (101) tender.AccountId,account.Code,account.Name,tender.MethodCode,tender.Amount,tender.LineNumber
            FROM dbo.CustomerPaymentTenders tender
            JOIN dbo.CustomerPayments payment ON payment.PaymentId=tender.PaymentId AND payment.BusinessId=@BusinessId
            LEFT JOIN dbo.AccountingAccounts account ON account.AccountId=tender.AccountId AND account.TenantId=@TenantId
            WHERE @Kind=N'Receivable' AND tender.PaymentId=@PaymentId
            ORDER BY LineNumber;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@Kind", kind);
        command.Parameters.AddWithValue("@PaymentId", paymentId);
        var applications = new List<(Guid PositionId, Guid TransactionId, UnapplicationCandidate Original)>();
        var tenders = new List<PaymentCorrectionTender>();
        decimal originalTotal = 0;
        string? documentNumber = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var original = new UnapplicationCandidate(NullableId(reader, 2), NullableText(reader, 3), NullableText(reader, 4),
                reader.GetGuid(5), reader.GetString(6), NullableText(reader, 7), NullableId(reader, 8),
                reader.GetString(9), reader.GetString(10), reader.GetDecimal(11), NullableText(reader, 12),
                CancellationPending: reader.GetInt32(13) > 0, SourceCancelled: reader.GetBoolean(14),
                FinancialDiscountDecided: reader.GetBoolean(15));
            applications.Add((reader.GetGuid(0), reader.GetGuid(1), original));
            originalTotal += reader.GetDecimal(16);
            documentNumber = reader.GetString(17);
        }
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            tenders.Add(new(NullableId(reader, 0), NullableText(reader, 1), NullableText(reader, 2),
                reader.GetString(3), reader.GetDecimal(4)));
        return applications.Count == 0 ? null : new(paymentId, documentNumber!, originalTotal, applications, tenders);
    }

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
