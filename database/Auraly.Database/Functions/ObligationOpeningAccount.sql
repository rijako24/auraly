CREATE FUNCTION accounting.ObligationOpeningAccount
(
    @TenantId uniqueidentifier,
    @BusinessId uniqueidentifier,
    @SubledgerKind nvarchar(16),
    @OpeningEntryId uniqueidentifier,
    @OpeningLineNumber int,
    @PartyId uniqueidentifier
)
RETURNS TABLE
AS RETURN
(
    -- Always one row, including a missing/corrupt historical binding. Callers
    -- expose the diagnosis instead of replacing the original with a mapping.
    SELECT entry.EntryId,line.LineNumber,account.AccountId,account.Code,account.Name,
        account.IsActive,line.Debit,line.Credit,line.CostCenterId,
        CASE WHEN @OpeningEntryId IS NULL OR @OpeningLineNumber IS NULL
                THEN N'Falta enlazar la cuenta de apertura de esta obligación.'
            WHEN entry.EntryId IS NULL OR account.AccountId IS NULL OR @PartyId IS NULL
                OR line.PartyId IS NULL OR line.PartyId<>@PartyId
                OR @SubledgerKind IS NULL OR @SubledgerKind NOT IN(N'Payable',N'Receivable',N'SupplierCredit',N'CustomerCredit')
                OR (@SubledgerKind=N'Payable' AND (line.Credit<=0 OR line.Debit<>0 OR account.AccountType<>N'Liability'))
                OR (@SubledgerKind=N'Receivable' AND (line.Debit<=0 OR line.Credit<>0 OR account.AccountType<>N'Asset'))
                OR (@SubledgerKind=N'SupplierCredit' AND (line.Debit<=0 OR line.Credit<>0 OR account.AccountType<>N'Asset'))
                OR (@SubledgerKind=N'CustomerCredit' AND (line.Credit<=0 OR line.Debit<>0 OR account.AccountType<>N'Liability'))
                OR (account.FinancialFunction IS NOT NULL AND account.FinancialFunction<>
                  CASE @SubledgerKind WHEN N'SupplierCredit' THEN N'SupplierAdvance' WHEN N'CustomerCredit' THEN N'CustomerAdvance' ELSE @SubledgerKind END)
                THEN N'El vínculo de apertura requiere revisión antes de aplicar esta obligación.'
            WHEN account.IsActive=0 OR account.AllowsPosting=0
                OR (account.BusinessId IS NOT NULL AND account.BusinessId<>@BusinessId)
                THEN N'La cuenta de apertura no está activa y disponible para movimientos en esta sede.'
        END Issue,entry.OccurredAt OpeningOccurredAt
    FROM (VALUES(0)) anchor(Value)
    LEFT JOIN dbo.AccountingEntries entry ON entry.EntryId=@OpeningEntryId
        AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId
    LEFT JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId AND line.LineNumber=@OpeningLineNumber
    LEFT JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId AND account.TenantId=@TenantId
);
