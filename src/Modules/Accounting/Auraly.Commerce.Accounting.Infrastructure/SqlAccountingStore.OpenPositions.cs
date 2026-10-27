using System.Data;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingStore
{
    public async Task<AccountingOpenPositionPage> OpenPositionsAsync(AccountingUserIdentity user,
        AccountingOpenPositionQuery query, CancellationToken cancellationToken)
    {
        const string positions = """
            SELECT N'Payable' SubledgerKind,p.PayableId SubledgerId,supplier.PartyId,
                p.SourceDocumentId,p.SourceDocumentType,p.DocumentNumber,source.OccurredAt,p.DueDate,
                p.CurrencyCode,p.OriginalAmount,p.OutstandingAmount,p.OpeningEntryId,p.OpeningEntryLineNumber,
                supplier.SupplierId CounterpartyRoleId,p.Status,CONVERT(nvarchar(16),NULL) CreditNature,p.PartySiteId
            FROM dbo.Payables p
            JOIN dbo.Suppliers supplier ON supplier.SupplierId=p.SupplierId AND supplier.TenantId=@TenantId
            JOIN dbo.AccountingPostingJobs source ON source.SourceDocumentId=p.SourceDocumentId
                AND source.SourceDocumentType=p.SourceDocumentType AND source.BusinessId=p.BusinessId
                AND source.TenantId=@TenantId
            WHERE @Kind=N'Payable' AND p.BusinessId=@BusinessId AND p.CurrencyCode=N'COP'
                AND (@Id IS NOT NULL OR (p.OutstandingAmount>0 AND p.Status IN(N'Open',N'PartiallyPaid')))
            UNION ALL
            SELECT N'Receivable',p.ReceivableId,COALESCE(p.PartyId,customer.PartyId),
                p.SourceDocumentId,p.SourceDocumentType,p.DocumentNumber,source.OccurredAt,p.DueDate,
                p.CurrencyCode,p.OriginalAmount,p.OutstandingAmount,p.OpeningEntryId,p.OpeningEntryLineNumber,
                p.CustomerId,p.Status,NULL,p.PartySiteId
            FROM dbo.Receivables p
            LEFT JOIN dbo.Customers customer ON customer.CustomerId=p.CustomerId AND customer.TenantId=@TenantId
            JOIN dbo.AccountingPostingJobs source ON source.SourceDocumentId=p.SourceDocumentId
                AND source.SourceDocumentType=p.SourceDocumentType AND source.BusinessId=p.BusinessId
                AND source.TenantId=@TenantId
            WHERE @Kind=N'Receivable' AND p.BusinessId=@BusinessId AND p.CurrencyCode=N'COP'
                AND (@Id IS NOT NULL OR (p.OutstandingAmount>0 AND p.Status IN(N'Open',N'PartiallyPaid')))
            UNION ALL
            SELECT N'SupplierCredit',credit.SupplierCreditId,supplier.PartyId,
                COALESCE(entry.SourceDocumentId,history.SourceDocumentId,credit.SourceDocumentId,credit.SourcePurchaseReturnId),
                COALESCE(entry.SourceDocumentType,history.SourceDocumentType,credit.SourceDocumentType,N'PurchaseReturn'),
                COALESCE(entry.EntryNumber,CONVERT(nvarchar(36),COALESCE(history.SourceDocumentId,credit.SourceDocumentId,credit.SourcePurchaseReturnId))),
                COALESCE(history.OccurredAt,entry.OccurredAt,credit.CreatedAt),NULL,
                credit.CurrencyCode,credit.OriginalAmount,credit.AvailableAmount,credit.OpeningEntryId,credit.OpeningEntryLineNumber,
                credit.SupplierId,credit.Status,credit.CreditNature,credit.PartySiteId
            FROM dbo.SupplierCredits credit JOIN dbo.Suppliers supplier ON supplier.SupplierId=credit.SupplierId AND supplier.TenantId=@TenantId
            LEFT JOIN dbo.SupplierCreditTransactions history ON history.SupplierCreditId=credit.SupplierCreditId AND history.TransactionType=N'Opening'
            LEFT JOIN dbo.AccountingEntries entry ON entry.EntryId=credit.OpeningEntryId AND entry.BusinessId=@BusinessId AND entry.TenantId=@TenantId
            WHERE @Kind=N'SupplierCredit' AND credit.BusinessId=@BusinessId
                AND (@Id IS NOT NULL OR credit.AvailableAmount>0 AND credit.Status<>N'Cancelled')
            UNION ALL
            SELECT N'CustomerCredit',credit.CustomerCreditId,COALESCE(credit.PartyId,customer.PartyId),
                COALESCE(entry.SourceDocumentId,history.SourceDocumentId,credit.SourceDocumentId,credit.SourceReturnId),
                COALESCE(entry.SourceDocumentType,history.SourceDocumentType,credit.SourceDocumentType,N'SalesReturn'),
                COALESCE(entry.EntryNumber,CONVERT(nvarchar(36),COALESCE(history.SourceDocumentId,credit.SourceDocumentId,credit.SourceReturnId))),
                COALESCE(history.OccurredAt,entry.OccurredAt,credit.CreatedAt),NULL,
                credit.CurrencyCode,credit.OriginalAmount,credit.AvailableAmount,credit.OpeningEntryId,credit.OpeningEntryLineNumber,
                credit.CustomerId,credit.Status,credit.CreditNature,credit.PartySiteId
            FROM dbo.CustomerCredits credit LEFT JOIN dbo.Customers customer ON customer.CustomerId=credit.CustomerId AND customer.TenantId=@TenantId
            LEFT JOIN dbo.CustomerCreditTransactions history ON history.CustomerCreditId=credit.CustomerCreditId AND history.TransactionType=N'Opening'
            LEFT JOIN dbo.AccountingEntries entry ON entry.EntryId=credit.OpeningEntryId AND entry.BusinessId=@BusinessId AND entry.TenantId=@TenantId
            WHERE @Kind=N'CustomerCredit' AND credit.BusinessId=@BusinessId
                AND (@Id IS NOT NULL OR credit.AvailableAmount>0)
            """;
        const string filters = """
            (@PartyId IS NULL OR p.PartyId=@PartyId)
            AND (@Id IS NULL OR p.SubledgerId=@Id)
            AND (@RoleId IS NULL OR p.CounterpartyRoleId=@RoleId)
            AND (@ManualSupportOnly=0 OR p.SourceDocumentType=N'ManualAccountingVoucher' AND EXISTS(
                SELECT 1 FROM dbo.FiscalDocuments fiscal WHERE fiscal.DocumentId=p.SourceDocumentId
                  AND fiscal.SourceDocumentType=p.SourceDocumentType AND fiscal.BusinessId=@BusinessId
                  AND fiscal.FiscalDocumentType=N'SupportDocument'))
            AND (@Search IS NULL OR p.DocumentNumber LIKE @Search
                OR party.DisplayName COLLATE Latin1_General_100_CI_AI LIKE @Search
                OR party.Identification LIKE @Search)
            AND (@DueFrom IS NULL OR CONVERT(date,p.DueDate)>=@DueFrom)
            AND (@DueTo IS NULL OR CONVERT(date,p.DueDate)<=@DueTo)
            AND (@AccountId IS NULL OR EXISTS(
                SELECT 1 FROM dbo.AccountingEntryLines line
                JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
                    AND entry.BusinessId=@BusinessId AND entry.TenantId=@TenantId
                WHERE line.EntryId=p.OpeningEntryId AND line.LineNumber=p.OpeningEntryLineNumber
                    AND line.AccountId=@AccountId))
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        // Count and page use the same filters. Only page keys reach account,
        // opening-line and reservation enrichment; no source payload is loaded.
        await using var command = new SqlCommand($"""
            DECLARE @EarnedMappingId uniqueidentifier,@LostMappingId uniqueidentifier,
              @EarnedAccountId uniqueidentifier,@LostAccountId uniqueidentifier,
              @EarnedCode nvarchar(32),@LostCode nvarchar(32),
              @EarnedName nvarchar(200),@LostName nvarchar(200),
              @TotalCount int,@TotalOutstanding decimal(38,4);
            IF @Kind=N'Payable' AND @EffectiveOn IS NOT NULL
            BEGIN
              SELECT TOP(1) @EarnedMappingId=AccountId FROM dbo.AccountingAccountMappings
              WHERE TenantId=@TenantId AND Category=N'SupplierFinancialDiscountEarned'
                AND (BusinessId=@BusinessId OR BusinessId IS NULL)
                AND EffectiveFrom<=@EffectiveOn AND (EffectiveTo IS NULL OR EffectiveTo>=@EffectiveOn)
              ORDER BY CASE WHEN BusinessId=@BusinessId THEN 0 ELSE 1 END,EffectiveFrom DESC;
              SELECT TOP(1) @LostMappingId=AccountId FROM dbo.AccountingAccountMappings
              WHERE TenantId=@TenantId AND Category=N'SupplierFinancialDiscountLost'
                AND (BusinessId=@BusinessId OR BusinessId IS NULL)
                AND EffectiveFrom<=@EffectiveOn AND (EffectiveTo IS NULL OR EffectiveTo>=@EffectiveOn)
              ORDER BY CASE WHEN BusinessId=@BusinessId THEN 0 ELSE 1 END,EffectiveFrom DESC;
              SELECT @EarnedAccountId=AccountId,@EarnedCode=Code,@EarnedName=Name
              FROM dbo.AccountingAccounts WHERE AccountId=@EarnedMappingId AND TenantId=@TenantId
                AND IsActive=1 AND AllowsPosting=1 AND FinancialFunction=N'General'
                AND (BusinessId IS NULL OR BusinessId=@BusinessId)
                AND EXISTS(SELECT 1 FROM accounting.PostingAccountReadiness(@TenantId,@BusinessId,
                  N'SupplierFinancialDiscountEarned',@EarnedMappingId));
              SELECT @LostAccountId=AccountId,@LostCode=Code,@LostName=Name
              FROM dbo.AccountingAccounts WHERE AccountId=@LostMappingId AND TenantId=@TenantId
                AND IsActive=1 AND AllowsPosting=1 AND FinancialFunction=N'General'
                AND (BusinessId IS NULL OR BusinessId=@BusinessId)
                AND EXISTS(SELECT 1 FROM accounting.PostingAccountReadiness(@TenantId,@BusinessId,
                  N'SupplierFinancialDiscountLost',@LostMappingId));
            END;
            WITH Positions AS ({positions})
            SELECT @TotalCount=COUNT(*),@TotalOutstanding=COALESCE(SUM(p.OutstandingAmount),0)
            FROM Positions p JOIN dbo.Parties party ON party.PartyId=p.PartyId AND party.TenantId=@TenantId
            WHERE {filters} OPTION(RECOMPILE);
            SELECT @TotalCount,@TotalOutstanding;
            WITH Positions AS ({positions})
            SELECT TOP(0) p.* INTO #OpenPositionPage FROM Positions p;
            IF @Offset<@TotalCount
            BEGIN
              WITH Positions AS ({positions})
              INSERT #OpenPositionPage
              SELECT p.*
              FROM Positions p JOIN dbo.Parties party ON party.PartyId=p.PartyId AND party.TenantId=@TenantId
              WHERE {filters}
              ORDER BY p.DueDate,p.SubledgerId
              OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY OPTION(RECOMPILE);
            END;
            SELECT p.SubledgerKind,p.SubledgerId,p.PartyId,party.DisplayName,party.Identification,
                p.SourceDocumentId,p.SourceDocumentType,p.DocumentNumber,p.OccurredAt,p.DueDate,
                p.CurrencyCode,p.OriginalAmount,p.OutstandingAmount,
                reserved.PendingPaymentAmount,reserved.PendingAdjustmentAmount,
                reserved.AvailableAmount,
                opening.EntryId,opening.LineNumber,opening.AccountId,opening.Code,opening.Name,opening.IsActive,
                opening.Debit,opening.Credit,CASE WHEN p.SubledgerKind IN(N'SupplierCredit',N'CustomerCredit')
                  AND (p.CreditNature IS NULL OR p.CurrencyCode IS NULL)
                  THEN N'El saldo a favor requiere conciliar su naturaleza y moneda antes de cruzarlo.' ELSE opening.Issue END,
                p.CounterpartyRoleId,p.Status,reserved.PendingApplicationAmount,p.CreditNature,p.PartySiteId,
                discount.EligibleAmount,discount.Anticipated,
                CONVERT(bit,CASE WHEN @EffectiveOn IS NOT NULL AND EXISTS(SELECT 1 FROM dbo.SupplierPaymentApplications application
                  WHERE application.PayableId=p.SubledgerId AND application.FinancialDiscountEarned IS NOT NULL)
                  THEN 1 ELSE 0 END),
                CONVERT(bit,CASE WHEN @EffectiveOn IS NOT NULL AND EXISTS(SELECT 1 FROM dbo.PurchaseReturns purchaseReturn
                  WHERE purchaseReturn.OriginalGoodsReceiptId=p.SourceDocumentId AND purchaseReturn.BusinessId=@BusinessId
                    AND purchaseReturn.Status=N'Accepted') THEN 1 ELSE 0 END),
                @EarnedAccountId,@EarnedCode,@EarnedName,@LostAccountId,@LostCode,@LostName
            FROM #OpenPositionPage p
            JOIN dbo.Parties party ON party.PartyId=p.PartyId AND party.TenantId=@TenantId
            CROSS APPLY accounting.ObligationOpeningAccount(@TenantId,@BusinessId,@Kind,
                p.OpeningEntryId,p.OpeningEntryLineNumber,p.PartyId) opening
            CROSS APPLY accounting.ObligationAvailability(@BusinessId,p.SubledgerKind,p.SubledgerId,p.OutstandingAmount,NULL) reserved
            OUTER APPLY (SELECT value.EligibleAmount,value.Anticipated
              FROM accounting.GoodsReceiptFinancialDiscount(@BusinessId,p.SourceDocumentId) value
              WHERE @EffectiveOn IS NOT NULL AND p.SubledgerKind=N'Payable'
                AND p.SourceDocumentType=N'GoodsReceipt') discount
            ORDER BY p.DueDate,p.SubledgerId OPTION(RECOMPILE);
            DROP TABLE #OpenPositionPage;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@Kind", query.SubledgerKind);
        command.Parameters.AddWithValue("@ManualSupportOnly", query.ManualSupportOnly);
        command.Parameters.Add("@PartyId", SqlDbType.UniqueIdentifier).Value = (object?)query.PartyId ?? DBNull.Value;
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = (object?)query.SubledgerId ?? DBNull.Value;
        command.Parameters.Add("@RoleId", SqlDbType.UniqueIdentifier).Value = (object?)query.CounterpartyRoleId ?? DBNull.Value;
        command.Parameters.Add("@AccountId", SqlDbType.UniqueIdentifier).Value = (object?)query.AccountId ?? DBNull.Value;
        command.Parameters.Add("@Search", SqlDbType.NVarChar, 242).Value = query.Search is null ? DBNull.Value : $"%{query.Search}%";
        command.Parameters.Add("@DueFrom", SqlDbType.Date).Value = (object?)query.DueFrom?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value;
        command.Parameters.Add("@DueTo", SqlDbType.Date).Value = (object?)query.DueTo?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value;
        command.Parameters.Add("@EffectiveOn", SqlDbType.Date).Value = (object?)query.EffectiveOn?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value;
        command.Parameters.AddWithValue("@Offset", ((long)query.Page - 1) * query.PageSize);
        command.Parameters.AddWithValue("@Limit", query.PageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new DBConcurrencyException("The open-position summary was not returned.");
        var count = reader.GetInt32(0);
        var outstanding = reader.GetDecimal(1);
        await reader.NextResultAsync(cancellationToken);
        var items = new List<AccountingOpenPosition>();
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new(reader.GetString(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetGuid(5), reader.GetString(6), reader.GetString(7),
                reader.GetDateTimeOffset(8), reader.IsDBNull(9) ? null : reader.GetDateTimeOffset(9), NullableText(reader,10), reader.GetDecimal(11),
                reader.GetDecimal(12), reader.GetDecimal(13), reader.GetDecimal(14), reader.GetDecimal(15),
                reader.IsDBNull(16) ? null : reader.GetGuid(16), reader.IsDBNull(17) ? null : reader.GetInt32(17),
                reader.IsDBNull(18) ? null : reader.GetGuid(18), reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetString(20), reader.IsDBNull(21) ? null : reader.GetBoolean(21),
                reader.IsDBNull(22) ? null : reader.GetDecimal(22), reader.IsDBNull(23) ? null : reader.GetDecimal(23),
                reader.IsDBNull(24) ? null : reader.GetString(24), reader.IsDBNull(25) ? null : reader.GetGuid(25), reader.GetString(26),
                reader.GetDecimal(27),NullableText(reader,28),NullableId(reader,29),
                reader.IsDBNull(30)?null:reader.GetDecimal(30),reader.IsDBNull(31)?null:reader.GetBoolean(31),
                reader.GetBoolean(32),reader.GetBoolean(33),NullableId(reader,34),NullableText(reader,35),NullableText(reader,36),
                NullableId(reader,37),NullableText(reader,38),NullableText(reader,39)));
        return new(items, query.Page, query.PageSize, count, outstanding);
    }
}
