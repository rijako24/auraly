using System.Data;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Payments;
using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingStore
{
    public async Task<AccountingOpenPositionPage> OpenPositionsAsync(AccountingUserIdentity user,
        AccountingOpenPositionQuery query, CancellationToken cancellationToken)
    {
        // The closed voucher/ledger scope selects a query shape. Fold it before
        // compilation so candidate and page queries never plan unrelated ledgers.
        // All identities, amounts, dates and search values remain parameters.
        var payables = query.VoucherTypeCode != "CashReceipt" &&
            query.SubledgerKind is "All" or AccountingSubledgerKinds.Payable;
        var receivables = query.VoucherTypeCode != "Disbursement" &&
            query.SubledgerKind is "All" or AccountingSubledgerKinds.Receivable;
        var supplierCredits = query.VoucherTypeCode != "CashReceipt" &&
            query.SubledgerKind is "All" or AccountingSubledgerKinds.SupplierCredit;
        var customerCredits = query.VoucherTypeCode != "Disbursement" &&
            query.SubledgerKind is "All" or AccountingSubledgerKinds.CustomerCredit;
        static string Predicate(bool included) => included ? "1=1" : "1=0";
        var kindScope = query.SubledgerKind switch
        {
            AccountingSubledgerKinds.Payable => "N'Payable'",
            AccountingSubledgerKinds.Receivable => "N'Receivable'",
            AccountingSubledgerKinds.SupplierCredit => "N'SupplierCredit'",
            AccountingSubledgerKinds.CustomerCredit => "N'CustomerCredit'",
            "All" => "p.SubledgerKind",
            _ => throw new AccountingValidationException("El tipo de obligación no es válido.")
        };
        var positions = $"""
            SELECT N'Payable' SubledgerKind,p.PayableId SubledgerId,supplier.PartyId,
                p.SourceDocumentId,p.SourceDocumentType,p.DocumentNumber,source.OccurredAt,p.DueDate,
                p.CurrencyCode,p.OriginalAmount,p.OutstandingAmount,p.OpeningEntryId,p.OpeningEntryLineNumber,
                supplier.SupplierId CounterpartyRoleId,p.Status,CONVERT(nvarchar(16),NULL) CreditNature,p.PartySiteId
            FROM dbo.Payables p
            JOIN dbo.Suppliers supplier ON supplier.SupplierId=p.SupplierId AND supplier.TenantId=@TenantId
            JOIN dbo.AccountingPostingJobs source ON source.SourceDocumentId=p.SourceDocumentId
                AND source.SourceDocumentType=p.SourceDocumentType AND source.BusinessId=p.BusinessId
                AND source.TenantId=@TenantId
            WHERE {Predicate(payables)} AND p.BusinessId=@BusinessId AND p.CurrencyCode=N'COP'
                AND (@PartyId IS NULL OR p.SupplierId=@SelectedSupplierId)
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
            WHERE {Predicate(receivables)} AND p.BusinessId=@BusinessId AND p.CurrencyCode=N'COP'
                AND (@PartyId IS NULL OR p.PartyId=@PartyId OR p.CustomerId=@SelectedCustomerId)
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
            WHERE {Predicate(supplierCredits)} AND credit.BusinessId=@BusinessId
                AND (@PartyId IS NULL OR credit.SupplierId=@SelectedSupplierId)
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
            WHERE {Predicate(customerCredits)} AND credit.BusinessId=@BusinessId
                AND (@PartyId IS NULL OR credit.PartyId=@PartyId OR credit.CustomerId=@SelectedCustomerId)
                AND (@Id IS NOT NULL OR credit.AvailableAmount>0)
            """;
        // A selected party is resolved once in this command. Count and page then
        // use its identity directly instead of joining the same Party for every
        // candidate position; the final page still joins to read its details.
        var scopedParty = query.PartyId is not null;
        var selectionPartyJoin = scopedParty ? "" :
            "JOIN dbo.Parties party ON party.PartyId=p.PartyId AND party.TenantId=@TenantId";
        var selectionPartyFilter = scopedParty ? "@PartyExists=1 AND p.PartyId=@PartyId" : "1=1";
        var selectionSearchFilter = scopedParty
            ? "(@Search IS NULL OR p.DocumentNumber LIKE @Search OR @PartyMatchesSearch=1)"
            : """
                (@Search IS NULL OR p.DocumentNumber LIKE @Search
                    OR COALESCE(party.DisplayName,party.LegalName,party.Identification) COLLATE Latin1_General_100_CI_AI LIKE @Search
                    OR party.Identification LIKE @Search)
                """;
        var filters = $"""
            {selectionPartyFilter}
            AND (@Id IS NULL OR p.SubledgerId=@Id)
            {(query.SubledgerIds is null ? "" : "AND EXISTS(SELECT 1 FROM @SelectedIds selected WHERE selected.Id=p.SubledgerId)")}
            AND (@RoleId IS NULL OR p.CounterpartyRoleId=@RoleId)
            AND (@ManualSupportOnly=0 OR p.SourceDocumentType=N'ManualAccountingVoucher' AND EXISTS(
                SELECT 1 FROM dbo.FiscalDocuments fiscal WHERE fiscal.DocumentId=p.SourceDocumentId
                  AND fiscal.SourceDocumentType=p.SourceDocumentType AND fiscal.BusinessId=@BusinessId
                  AND fiscal.FiscalDocumentType=N'SupportDocument'))
            AND {selectionSearchFilter}
            AND (@DueFrom IS NULL OR CONVERT(date,p.DueDate)>=@DueFrom)
            AND (@DueTo IS NULL OR CONVERT(date,p.DueDate)<=@DueTo)
            AND (@AccountId IS NULL OR EXISTS(
                SELECT 1 FROM dbo.AccountingEntryLines line
                JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
                    AND entry.BusinessId=@BusinessId AND entry.TenantId=@TenantId
                WHERE line.EntryId=p.OpeningEntryId AND line.LineNumber=p.OpeningEntryLineNumber
                    AND line.AccountId=@AccountId))
            """;
        // Preview uses the non-locking canonical predicate. First-use update
        // locks belong to posting, and receipt queries do not compile the
        // supplier-only configuration and receipt-line policy at all.
        var includeFinancialDiscounts = payables && query.EffectiveOn is not null;
        var discountReadinessSql = includeFinancialDiscounts ? """
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
                AND EXISTS(SELECT 1 FROM accounting.PostingAccountEligibility(@TenantId,N'SupplierFinancialDiscountEarned',@EarnedMappingId))
              OPTION(RECOMPILE);
              SELECT @LostAccountId=AccountId,@LostCode=Code,@LostName=Name
              FROM dbo.AccountingAccounts WHERE AccountId=@LostMappingId AND TenantId=@TenantId
                AND EXISTS(SELECT 1 FROM accounting.PostingAccountEligibility(@TenantId,N'SupplierFinancialDiscountLost',@LostMappingId))
              OPTION(RECOMPILE);
            """ : "";
        var discountAmountsSql = includeFinancialDiscounts ? """
            SELECT p.SubledgerKind,p.SubledgerId,value.EligibleAmount,value.Anticipated
              INTO #OpenPositionDiscounts
            FROM #OpenPositionPage p
            CROSS APPLY accounting.GoodsReceiptFinancialDiscount(@BusinessId,p.SourceDocumentId) value
            WHERE @EffectiveOn IS NOT NULL AND p.SubledgerKind=N'Payable'
              AND p.SourceDocumentType=N'GoodsReceipt' OPTION(RECOMPILE);
            """ : """
            SELECT TOP(0) p.SubledgerKind,p.SubledgerId,
              CAST(NULL AS decimal(19,4)) EligibleAmount,CAST(NULL AS bit) Anticipated
            INTO #OpenPositionDiscounts FROM #OpenPositionPage p;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        // Filter once into narrow candidate keys. Count and page share that set;
        // only page keys reach source detail, opening-line and reservation enrichment.
        await using var command = new SqlCommand($"""
            {(query.SubledgerIds is null ? "" : "DECLARE @SelectedIds TABLE(Id uniqueidentifier PRIMARY KEY); INSERT @SelectedIds SELECT value FROM OPENJSON(@SelectedIdsJson);")}
            DECLARE @EarnedMappingId uniqueidentifier,@LostMappingId uniqueidentifier,
              @EarnedAccountId uniqueidentifier,@LostAccountId uniqueidentifier,
              @EarnedCode nvarchar(32),@LostCode nvarchar(32),
              @EarnedName nvarchar(200),@LostName nvarchar(200),
              @TotalCount int,@TotalOutstanding decimal(38,4),
              @PartyExists bit=0,@PartyMatchesSearch bit=0,
              @SelectedCustomerId uniqueidentifier,@SelectedSupplierId uniqueidentifier;
            IF @PartyId IS NOT NULL
            BEGIN
              SELECT @PartyExists=1,@PartyMatchesSearch=CONVERT(bit,CASE WHEN
                COALESCE(party.DisplayName,party.LegalName,party.Identification) COLLATE Latin1_General_100_CI_AI LIKE @Search
                OR party.Identification LIKE @Search THEN 1 ELSE 0 END)
              FROM dbo.Parties party WHERE party.PartyId=@PartyId AND party.TenantId=@TenantId;
              -- Resolve the two unique role identities once. Push these indexed
              -- predicates into the branches before their source/history joins;
              -- retain the authoritative party filter on the resulting set.
              SELECT @SelectedCustomerId=CustomerId FROM dbo.Customers WHERE TenantId=@TenantId AND PartyId=@PartyId;
              SELECT @SelectedSupplierId=SupplierId FROM dbo.Suppliers WHERE TenantId=@TenantId AND PartyId=@PartyId;
            END;
            {discountReadinessSql}
            WITH Positions AS ({positions})
            SELECT p.SubledgerKind,p.SubledgerId,p.DueDate,p.OutstandingAmount
              INTO #OpenPositionCandidates
            FROM Positions p {selectionPartyJoin}
            WHERE {filters} OPTION(RECOMPILE);
            SELECT @TotalCount=COUNT(*),@TotalOutstanding=COALESCE(SUM(OutstandingAmount),0)
              FROM #OpenPositionCandidates;
            SELECT @TotalCount,@TotalOutstanding,
              (SELECT TimeZone FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId);
            SELECT SubledgerKind,SubledgerId INTO #OpenPositionPageKeys
              FROM #OpenPositionCandidates
              ORDER BY DueDate,SubledgerId
              OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
            -- Only the selected page reaches the canonical applied-money view.
            -- Capture balance and money in the same statement. Credits and
            -- adjustments are not inferred to be cash payments.
            WITH Positions AS ({positions}), Paid AS (
              SELECT N'Payable' SubledgerKind,applied.PositionId,SUM(applied.Amount) PaidAmount
              FROM accounting.PortfolioAppliedAmounts(@BusinessId,N'Payable') applied
              JOIN #OpenPositionPageKeys selected ON selected.SubledgerKind=N'Payable' AND selected.SubledgerId=applied.PositionId
              WHERE {Predicate(payables)}
              GROUP BY applied.PositionId
              UNION ALL
              SELECT N'Receivable',applied.PositionId,SUM(applied.Amount)
              FROM accounting.PortfolioAppliedAmounts(@BusinessId,N'Receivable') applied
              JOIN #OpenPositionPageKeys selected ON selected.SubledgerKind=N'Receivable' AND selected.SubledgerId=applied.PositionId
              WHERE {Predicate(receivables)}
              GROUP BY applied.PositionId
            )
            SELECT p.*,COALESCE(paid.PaidAmount,0) PaidAmount INTO #OpenPositionPage
              FROM #OpenPositionPageKeys selected
              JOIN Positions p ON p.SubledgerKind=selected.SubledgerKind AND p.SubledgerId=selected.SubledgerId
              LEFT JOIN Paid paid ON paid.SubledgerKind=p.SubledgerKind AND paid.PositionId=p.SubledgerId
              OPTION(RECOMPILE);
            -- Keep independently complex canonical projections on the bounded
            -- page. Inlining reservations, discount and opening-account policy
            -- together forces SQL to optimize their Cartesian plan alternatives.
            SELECT p.SubledgerKind,p.SubledgerId,reserved.* INTO #OpenPositionAvailability
            FROM #OpenPositionPage p
            CROSS APPLY accounting.ObligationAvailability(@BusinessId,{kindScope},p.SubledgerId,p.OutstandingAmount,NULL) reserved
            OPTION(RECOMPILE);
            {discountAmountsSql}
            SELECT p.SubledgerKind,p.SubledgerId,p.PartyId,COALESCE(party.DisplayName,party.LegalName,party.Identification),party.Identification,
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
                @EarnedAccountId,@EarnedCode,@EarnedName,@LostAccountId,@LostCode,@LostName,
                CASE WHEN p.SubledgerKind IN(N'Payable',N'Receivable') THEN p.PaidAmount END,
                p.OriginalAmount-p.OutstandingAmount-p.PaidAmount
            FROM #OpenPositionPage p
            JOIN dbo.Parties party ON party.PartyId=p.PartyId AND party.TenantId=@TenantId
            CROSS APPLY accounting.ObligationOpeningAccount(@TenantId,@BusinessId,{kindScope},
                p.OpeningEntryId,p.OpeningEntryLineNumber,p.PartyId) opening
            JOIN #OpenPositionAvailability reserved ON reserved.SubledgerKind=p.SubledgerKind AND reserved.SubledgerId=p.SubledgerId
            LEFT JOIN #OpenPositionDiscounts discount ON discount.SubledgerKind=p.SubledgerKind AND discount.SubledgerId=p.SubledgerId
            ORDER BY p.DueDate,p.SubledgerId OPTION(RECOMPILE);
            DROP TABLE #OpenPositionPage;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        if (query.SubledgerIds is not null)
            command.Parameters.Add("@SelectedIdsJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(query.SubledgerIds);
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
        command.CommandText = SqlQueryShape.FoldOptionalPredicates(command);
        var referenceInstant = timeProvider.GetUtcNow();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new DBConcurrencyException("The open-position summary was not returned.");
        var count = reader.GetInt32(0);
        var outstanding = reader.GetDecimal(1);
        var zone = reader.IsDBNull(2) ? null : TimeZoneInfo.FindSystemTimeZoneById(reader.GetString(2));
        var referenceDate = zone is null ? (DateOnly?)null
            : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(referenceInstant,zone).DateTime);
        await reader.NextResultAsync(cancellationToken);
        var items = new List<AccountingOpenPosition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var dueAt = reader.IsDBNull(9) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(9);
            var overdueDays = dueAt is { } due && referenceDate is { } currentDate
                ? PortfolioAging.Calculate(reader.GetDecimal(12),due,currentDate,
                    zone ?? throw new AccountingValidationException("La sede de los documentos requiere su zona horaria configurada.")).OverdueDays
                : (int?)null;
            items.Add(new(reader.GetString(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetGuid(5), reader.GetString(6), reader.GetString(7),
                reader.GetDateTimeOffset(8), dueAt, NullableText(reader,10), reader.GetDecimal(11),
                reader.GetDecimal(12), reader.GetDecimal(13), reader.GetDecimal(14), reader.GetDecimal(15),
                reader.IsDBNull(16) ? null : reader.GetGuid(16), reader.IsDBNull(17) ? null : reader.GetInt32(17),
                reader.IsDBNull(18) ? null : reader.GetGuid(18), reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetString(20), reader.IsDBNull(21) ? null : reader.GetBoolean(21),
                reader.IsDBNull(22) ? null : reader.GetDecimal(22), reader.IsDBNull(23) ? null : reader.GetDecimal(23),
                reader.IsDBNull(24) ? null : reader.GetString(24), reader.IsDBNull(25) ? null : reader.GetGuid(25), reader.GetString(26),
                reader.GetDecimal(27),NullableText(reader,28),NullableId(reader,29),
                reader.IsDBNull(30)?null:reader.GetDecimal(30),reader.IsDBNull(31)?null:reader.GetBoolean(31),
                reader.GetBoolean(32),reader.GetBoolean(33),NullableId(reader,34),NullableText(reader,35),NullableText(reader,36),
                NullableId(reader,37),NullableText(reader,38),NullableText(reader,39),
                reader.IsDBNull(40)?null:reader.GetDecimal(40),reader.GetDecimal(41),overdueDays));
        }
        return new(items, query.Page, query.PageSize, count, outstanding, referenceDate);
    }
}
