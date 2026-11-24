CREATE PROCEDURE accounting.OpeningPositionsValidate
    @TenantId uniqueidentifier,
    @BusinessId uniqueidentifier,
    @EffectiveOn date,
    @LinesJson nvarchar(max),
    @HistoricalCatchup bit = 0,
    @EffectiveAt datetimeoffset(7) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @@TRANCOUNT=0 OR ISNULL(ISJSON(@LinesJson),0)<>1 OR LEFT(LTRIM(@LinesJson),1)<>N'['
      THROW 51408,N'La conciliación de apertura requiere una transacción y sus partidas.',1;
    DECLARE @Lines TABLE(LineNumber int PRIMARY KEY,AccountId uniqueidentifier,PartyId uniqueidentifier,
      Debit decimal(19,4),Credit decimal(19,4),LineId uniqueidentifier,
      ExistingPayableId uniqueidentifier,ExistingReceivableId uniqueidentifier,
      OpeningDocumentNumber nvarchar(80),OpeningIssuedOn date,OpeningDueOn date,
      OpeningPartySiteId uniqueidentifier,OpeningRoleId uniqueidentifier,OpeningKind nvarchar(16),
      ExistingSupplierCreditId uniqueidentifier,ExistingCustomerCreditId uniqueidentifier,
      OpeningAdvanceReference nvarchar(80),OpeningAdvanceOn date,OpeningAdvancePartySiteId uniqueidentifier,
      OpeningAdvanceRoleId uniqueidentifier,OpeningAdvanceKind nvarchar(16));
    INSERT @Lines SELECT LineNumber,AccountId,PartyId,Debit,Credit,LineId,ExistingPayableId,ExistingReceivableId,
      OpeningDocumentNumber,OpeningIssuedOn,OpeningDueOn,OpeningPartySiteId,OpeningRoleId,OpeningKind,
      ExistingSupplierCreditId,ExistingCustomerCreditId,OpeningAdvanceReference,OpeningAdvanceOn,OpeningAdvancePartySiteId,
      OpeningAdvanceRoleId,OpeningAdvanceKind
    FROM OPENJSON(@LinesJson) WITH(LineNumber int,AccountId uniqueidentifier,PartyId uniqueidentifier,
      Debit decimal(19,4),Credit decimal(19,4),LineId uniqueidentifier,
      ExistingPayableId uniqueidentifier,ExistingReceivableId uniqueidentifier,
      OpeningDocumentNumber nvarchar(80),OpeningIssuedOn date,OpeningDueOn date,
      OpeningPartySiteId uniqueidentifier,OpeningRoleId uniqueidentifier,OpeningKind nvarchar(16),
      ExistingSupplierCreditId uniqueidentifier,ExistingCustomerCreditId uniqueidentifier,
      OpeningAdvanceReference nvarchar(80),OpeningAdvanceOn date,OpeningAdvancePartySiteId uniqueidentifier,
      OpeningAdvanceRoleId uniqueidentifier,OpeningAdvanceKind nvarchar(16));
    -- Initial advances share the opening aggregate and native credit ledgers.
    -- Their evidence carries no due date and creates no new cash movement.
    IF EXISTS(SELECT 1 FROM @Lines line JOIN dbo.AccountingAccounts account
      ON account.AccountId=line.AccountId AND account.TenantId=@TenantId
      WHERE account.FinancialFunction IN(N'SupplierAdvance',N'CustomerAdvance')
        AND line.OpeningAdvanceReference IS NULL AND line.ExistingSupplierCreditId IS NULL AND line.ExistingCustomerCreditId IS NULL)
      THROW 51408,N'La cuenta de anticipo requiere su referencia inicial o vincular un anticipo existente para conservar su saldo disponible.',1;
    IF EXISTS(SELECT 1 FROM @Lines line
      LEFT JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId AND account.TenantId=@TenantId
        AND account.IsActive=1 AND account.AllowsPosting=1 AND account.RequiresParty=1
      LEFT JOIN dbo.Parties party ON party.PartyId=line.PartyId AND party.TenantId=@TenantId AND party.IsActive=1
      LEFT JOIN dbo.PartySites site ON site.PartySiteId=line.OpeningAdvancePartySiteId AND site.PartyId=party.PartyId AND site.IsActive=1
      LEFT JOIN dbo.Customers customer ON customer.PartyId=party.PartyId AND customer.TenantId=@TenantId AND customer.IsActive=1
      LEFT JOIN dbo.Suppliers supplier ON supplier.PartyId=party.PartyId AND supplier.TenantId=@TenantId AND supplier.IsActive=1
      WHERE (line.OpeningAdvanceReference IS NOT NULL OR line.OpeningAdvanceOn IS NOT NULL OR line.OpeningAdvancePartySiteId IS NOT NULL)
        AND (@HistoricalCatchup=1 OR line.ExistingPayableId IS NOT NULL OR line.ExistingReceivableId IS NOT NULL
          OR line.ExistingSupplierCreditId IS NOT NULL OR line.ExistingCustomerCreditId IS NOT NULL OR line.OpeningDocumentNumber IS NOT NULL
          OR line.LineId IS NULL OR party.PartyId IS NULL OR site.PartySiteId IS NULL OR account.AccountId IS NULL
          OR NULLIF(LTRIM(RTRIM(line.OpeningAdvanceReference)),N'') IS NULL OR line.OpeningAdvanceOn IS NULL OR line.OpeningAdvanceOn>=@EffectiveOn
          OR ISNULL(account.FinancialFunction,N'') NOT IN(N'SupplierAdvance',N'CustomerAdvance')
          OR account.FinancialFunction=N'SupplierAdvance' AND (account.AccountType<>N'Asset' OR ISNULL(account.BalanceSide,N'')<>N'Debit'
            OR line.Debit<=0 OR line.Credit<>0 OR supplier.SupplierId IS NULL
            OR line.OpeningAdvanceRoleId IS NOT NULL AND line.OpeningAdvanceRoleId<>supplier.SupplierId
            OR line.OpeningAdvanceKind IS NOT NULL AND line.OpeningAdvanceKind<>N'SupplierCredit')
          OR account.FinancialFunction=N'CustomerAdvance' AND (account.AccountType<>N'Liability' OR ISNULL(account.BalanceSide,N'')<>N'Credit'
            OR line.Credit<=0 OR line.Debit<>0 OR customer.CustomerId IS NULL
            OR line.OpeningAdvanceRoleId IS NOT NULL AND line.OpeningAdvanceRoleId<>customer.CustomerId
            OR line.OpeningAdvanceKind IS NOT NULL AND line.OpeningAdvanceKind<>N'CustomerCredit')))
      THROW 51408,N'El anticipo inicial no coincide con la cuenta, tercero, sede, referencia, fecha o saldo al corte.',1;
    IF EXISTS(SELECT 1 FROM @Lines line JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId
      WHERE line.OpeningAdvanceReference IS NOT NULL
      GROUP BY account.FinancialFunction,line.PartyId,line.OpeningAdvancePartySiteId,line.OpeningAdvanceReference HAVING COUNT(*)>1)
      THROW 51408,N'El anticipo inicial está repetido en las partidas de apertura.',1;
    IF EXISTS(SELECT 1 FROM @Lines WHERE
      (CASE WHEN ExistingPayableId IS NULL THEN 0 ELSE 1 END+CASE WHEN ExistingReceivableId IS NULL THEN 0 ELSE 1 END
       +CASE WHEN ExistingSupplierCreditId IS NULL THEN 0 ELSE 1 END+CASE WHEN ExistingCustomerCreditId IS NULL THEN 0 ELSE 1 END)>1
      OR (ExistingSupplierCreditId IS NOT NULL OR ExistingCustomerCreditId IS NOT NULL) AND
        (LineId IS NULL OR OpeningDocumentNumber IS NOT NULL OR OpeningAdvanceReference IS NOT NULL))
      OR EXISTS(SELECT 1 FROM @Lines WHERE ExistingSupplierCreditId IS NOT NULL GROUP BY ExistingSupplierCreditId HAVING COUNT(*)>1)
      OR EXISTS(SELECT 1 FROM @Lines WHERE ExistingCustomerCreditId IS NOT NULL GROUP BY ExistingCustomerCreditId HAVING COUNT(*)>1)
      THROW 51408,N'Cada anticipo e identidad debe vincularse una sola vez, sin abrir otro saldo en la misma partida.',1;
    DECLARE @Credits TABLE(Kind nvarchar(16),CreditId uniqueidentifier,PartyId uniqueidentifier,
      Amount decimal(19,4),OriginalAmount decimal(19,4),CurrencyCode char(3),CreditNature nvarchar(16),Status nvarchar(24),
      OpeningEntryId uniqueidentifier,OpeningLine int,PRIMARY KEY(Kind,CreditId));
    INSERT @Credits
    SELECT N'SupplierCredit',credit.SupplierCreditId,supplier.PartyId,credit.AvailableAmount,credit.OriginalAmount,
      credit.CurrencyCode,credit.CreditNature,credit.Status,credit.OpeningEntryId,credit.OpeningEntryLineNumber
    FROM @Lines line JOIN dbo.SupplierCredits credit WITH(UPDLOCK,HOLDLOCK) ON credit.SupplierCreditId=line.ExistingSupplierCreditId AND credit.BusinessId=@BusinessId
    JOIN dbo.Suppliers supplier ON supplier.SupplierId=credit.SupplierId AND supplier.TenantId=@TenantId;
    INSERT @Credits
    SELECT N'CustomerCredit',credit.CustomerCreditId,COALESCE(credit.PartyId,customer.PartyId),credit.AvailableAmount,credit.OriginalAmount,
      credit.CurrencyCode,credit.CreditNature,credit.Status,credit.OpeningEntryId,credit.OpeningEntryLineNumber
    FROM @Lines line JOIN dbo.CustomerCredits credit WITH(UPDLOCK,HOLDLOCK) ON credit.CustomerCreditId=line.ExistingCustomerCreditId AND credit.BusinessId=@BusinessId
    LEFT JOIN dbo.Customers customer ON customer.CustomerId=credit.CustomerId AND customer.TenantId=@TenantId
    JOIN dbo.Parties party ON party.PartyId=COALESCE(credit.PartyId,customer.PartyId) AND party.TenantId=@TenantId;
    IF (SELECT COUNT(*) FROM @Credits)<>(SELECT COUNT(*) FROM @Lines WHERE ExistingSupplierCreditId IS NOT NULL OR ExistingCustomerCreditId IS NOT NULL)
      THROW 51408,N'Hay anticipos fuera de la sede o empresa autorizada.',1;
    DECLARE @CreditHistory TABLE(Kind nvarchar(16),CreditId uniqueidentifier,SourceDocumentId uniqueidentifier,
      SourceDocumentType nvarchar(64),OccurredAt datetimeoffset(7),TransactionType nvarchar(24),Amount decimal(19,4));
    INSERT @CreditHistory SELECT credit.Kind,credit.CreditId,m.SourceDocumentId,m.SourceDocumentType,m.OccurredAt,m.TransactionType,m.Amount
    FROM @Credits credit JOIN dbo.SupplierCreditTransactions m ON m.SupplierCreditId=credit.CreditId WHERE credit.Kind=N'SupplierCredit';
    INSERT @CreditHistory SELECT credit.Kind,credit.CreditId,m.SourceDocumentId,m.SourceDocumentType,m.OccurredAt,m.TransactionType,m.Amount
    FROM @Credits credit JOIN dbo.CustomerCreditTransactions m ON m.CustomerCreditId=credit.CreditId WHERE credit.Kind=N'CustomerCredit';
    IF EXISTS(SELECT 1 FROM @Lines line JOIN @Credits credit ON credit.CreditId=COALESCE(line.ExistingSupplierCreditId,line.ExistingCustomerCreditId)
        AND credit.Kind=CASE WHEN line.ExistingSupplierCreditId IS NOT NULL THEN N'SupplierCredit' ELSE N'CustomerCredit' END
      LEFT JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId AND account.TenantId=@TenantId
        AND account.IsActive=1 AND account.AllowsPosting=1 AND account.RequiresParty=1
      CROSS APPLY accounting.ObligationAvailability(@BusinessId,credit.Kind,credit.CreditId,credit.Amount,NULL) pending
      WHERE @HistoricalCatchup=1 OR ISNULL(credit.CurrencyCode,N'')<>N'COP' OR ISNULL(credit.CreditNature,N'')<>N'Advance'
        OR credit.Amount<=0 OR credit.OpeningEntryId IS NOT NULL OR credit.OpeningLine IS NOT NULL
        OR credit.Kind=N'SupplierCredit' AND credit.Status NOT IN(N'Open',N'PartiallyApplied')
        OR credit.Kind=N'CustomerCredit' AND credit.Status<>N'Open'
        OR line.PartyId IS NULL OR credit.PartyId<>line.PartyId OR account.AccountId IS NULL
        OR pending.AvailableAmount<>credit.Amount OR pending.PendingAdjustmentCount<>0 OR pending.PendingCancellationCount<>0 OR pending.SourceCancelled=1
        OR credit.Kind=N'SupplierCredit' AND (ISNULL(account.FinancialFunction,N'')<>N'SupplierAdvance' OR account.AccountType<>N'Asset'
          OR ISNULL(account.BalanceSide,N'')<>N'Debit' OR line.Credit<>0 OR line.Debit<>credit.Amount)
        OR credit.Kind=N'CustomerCredit' AND (ISNULL(account.FinancialFunction,N'')<>N'CustomerAdvance' OR account.AccountType<>N'Liability'
          OR ISNULL(account.BalanceSide,N'')<>N'Credit' OR line.Debit<>0 OR line.Credit<>credit.Amount))
      OR EXISTS(SELECT 1 FROM @CreditHistory m
        LEFT JOIN dbo.AccountingPostingJobs origin ON origin.SourceDocumentId=m.SourceDocumentId AND origin.SourceDocumentType=m.SourceDocumentType
          AND origin.TenantId=@TenantId AND origin.BusinessId=@BusinessId
        WHERE @EffectiveAt IS NULL OR m.OccurredAt>=@EffectiveAt OR origin.AccountingPostingJobId IS NULL
          OR origin.AccountingEntryRequired<>0 OR origin.Status<>N'CommercialEffectsApplied')
      OR EXISTS(SELECT 1 FROM @Credits credit LEFT JOIN
        (SELECT Kind,CreditId,SUM(CASE WHEN TransactionType=N'Application' THEN -Amount ELSE Amount END) Balance,
          SUM(CASE WHEN TransactionType=N'Opening' THEN 1 ELSE 0 END) OpeningCount,
          SUM(CASE WHEN TransactionType=N'Opening' THEN Amount ELSE 0 END) OpeningAmount
         FROM @CreditHistory GROUP BY Kind,CreditId) history ON history.Kind=credit.Kind AND history.CreditId=credit.CreditId
        WHERE ISNULL(history.OpeningCount,0)<>1 OR history.Balance IS NULL OR history.Balance<>credit.Amount OR history.OpeningAmount<>credit.OriginalAmount)
      THROW 51408,N'El anticipo existente requiere saldo y movimientos conciliados anteriores al corte, sin asiento ni aplicaciones pendientes, y cuenta y tercero coincidentes.',1;
    IF EXISTS(SELECT 1 FROM @Lines WHERE OpeningDocumentNumber IS NOT NULL OR OpeningIssuedOn IS NOT NULL
      OR OpeningDueOn IS NOT NULL OR OpeningPartySiteId IS NOT NULL)
    BEGIN
    -- New invoice evidence is validated in the same transaction and owner as
    -- existing links. It never creates a sale, purchase or fiscal document.
    IF EXISTS(SELECT 1 FROM @Lines line
      LEFT JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId AND account.TenantId=@TenantId
        AND account.IsActive=1 AND account.AllowsPosting=1 AND account.RequiresParty=1
      LEFT JOIN dbo.Parties party ON party.PartyId=line.PartyId AND party.TenantId=@TenantId AND party.IsActive=1
      LEFT JOIN dbo.PartySites site ON site.PartySiteId=line.OpeningPartySiteId AND site.PartyId=party.PartyId AND site.IsActive=1
      LEFT JOIN dbo.Customers customer ON customer.PartyId=party.PartyId AND customer.TenantId=@TenantId AND customer.IsActive=1
      LEFT JOIN dbo.Suppliers supplier ON supplier.PartyId=party.PartyId AND supplier.TenantId=@TenantId AND supplier.IsActive=1
      WHERE (line.OpeningDocumentNumber IS NOT NULL OR line.OpeningIssuedOn IS NOT NULL OR line.OpeningDueOn IS NOT NULL OR line.OpeningPartySiteId IS NOT NULL)
        AND (@HistoricalCatchup=1 OR line.ExistingPayableId IS NOT NULL OR line.ExistingReceivableId IS NOT NULL
          OR line.LineId IS NULL OR party.PartyId IS NULL OR site.PartySiteId IS NULL
          OR NULLIF(LTRIM(RTRIM(line.OpeningDocumentNumber)),N'') IS NULL
          OR line.OpeningIssuedOn IS NULL OR line.OpeningDueOn IS NULL OR line.OpeningIssuedOn>=@EffectiveOn OR line.OpeningDueOn<line.OpeningIssuedOn
          OR account.AccountId IS NULL OR account.FinancialFunction IS NULL OR account.FinancialFunction NOT IN(N'Receivable',N'Payable')
          OR line.OpeningKind IS NOT NULL AND line.OpeningKind<>account.FinancialFunction
          OR account.FinancialFunction=N'Receivable' AND (account.AccountType<>N'Asset' OR ISNULL(account.BalanceSide,N'')<>N'Debit'
            OR line.Debit<=0 OR line.Credit<>0 OR customer.CustomerId IS NULL OR LEN(line.OpeningDocumentNumber)>64
            OR line.OpeningRoleId IS NOT NULL AND line.OpeningRoleId<>customer.CustomerId)
          OR account.FinancialFunction=N'Payable' AND (account.AccountType<>N'Liability' OR ISNULL(account.BalanceSide,N'')<>N'Credit'
            OR line.Credit<=0 OR line.Debit<>0 OR supplier.SupplierId IS NULL
            OR line.OpeningRoleId IS NOT NULL AND line.OpeningRoleId<>supplier.SupplierId)))
      THROW 51408,N'La factura inicial no coincide con la cuenta de cartera, tercero, sede, fechas o saldo al corte.',1;
    IF EXISTS(SELECT 1 FROM @Lines line JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId
      WHERE line.OpeningDocumentNumber IS NOT NULL
      GROUP BY line.PartyId,line.OpeningPartySiteId,line.OpeningDocumentNumber,account.FinancialFunction HAVING COUNT(*)>1)
      THROW 51408,N'La factura inicial está repetida en las partidas de apertura.',1;
    IF EXISTS(SELECT 1 FROM @Lines line JOIN dbo.Customers customer ON customer.PartyId=line.PartyId AND customer.TenantId=@TenantId
      JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId AND account.FinancialFunction=N'Receivable'
      JOIN dbo.Receivables position WITH(UPDLOCK,HOLDLOCK) ON position.BusinessId=@BusinessId AND position.CustomerId=customer.CustomerId
        AND position.PartySiteId=line.OpeningPartySiteId AND position.DocumentNumber=line.OpeningDocumentNumber
      WHERE line.OpeningDocumentNumber IS NOT NULL)
      OR EXISTS(SELECT 1 FROM @Lines line JOIN dbo.Suppliers supplier ON supplier.PartyId=line.PartyId AND supplier.TenantId=@TenantId
      JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId AND account.FinancialFunction=N'Payable'
      JOIN dbo.Payables position WITH(UPDLOCK,HOLDLOCK) ON position.BusinessId=@BusinessId AND position.SupplierId=supplier.SupplierId
        AND position.PartySiteId=line.OpeningPartySiteId AND position.DocumentNumber=line.OpeningDocumentNumber
      WHERE line.OpeningDocumentNumber IS NOT NULL)
      THROW 51408,N'La factura ya existe en cartera. Vincula la obligación existente para no duplicarla.',1;
    END;
    IF EXISTS(SELECT 1 FROM @Lines WHERE LineId='00000000-0000-0000-0000-000000000000'
        OR ExistingPayableId IS NOT NULL AND ExistingReceivableId IS NOT NULL
        OR (ExistingPayableId IS NOT NULL OR ExistingReceivableId IS NOT NULL) AND LineId IS NULL)
      OR EXISTS(SELECT 1 FROM @Lines WHERE LineId IS NOT NULL GROUP BY LineId HAVING COUNT(*)>1)
      OR EXISTS(SELECT 1 FROM @Lines WHERE ExistingPayableId IS NOT NULL GROUP BY ExistingPayableId HAVING COUNT(*)>1)
      OR EXISTS(SELECT 1 FROM @Lines WHERE ExistingReceivableId IS NOT NULL GROUP BY ExistingReceivableId HAVING COUNT(*)>1)
      THROW 51408,N'Cada obligación e identidad de partida debe aparecer una sola vez en la apertura.',1;
    IF NOT EXISTS(SELECT 1 FROM @Lines WHERE ExistingPayableId IS NOT NULL OR ExistingReceivableId IS NOT NULL) RETURN;

    -- Read only the explicitly selected positions. Save, approval and posting
    -- share this validator; it never creates obligations or changes balances.
    DECLARE @Positions TABLE(Kind nvarchar(16),PositionId uniqueidentifier,PartyId uniqueidentifier,
      Amount decimal(19,4),OriginalAmount decimal(19,4),CurrencyCode char(3),Status nvarchar(24),
      OpeningEntryId uniqueidentifier,OpeningLine int,SourceDocumentId uniqueidentifier,
      SourceDocumentType nvarchar(64),PRIMARY KEY(Kind,PositionId));
    INSERT @Positions
    SELECT N'Payable',balance.PayableId,party.PartyId,balance.OutstandingAmount,balance.OriginalAmount,balance.CurrencyCode,
      balance.Status,balance.OpeningEntryId,balance.OpeningEntryLineNumber,balance.SourceDocumentId,balance.SourceDocumentType
    FROM @Lines line JOIN dbo.Payables balance WITH(UPDLOCK,HOLDLOCK) ON balance.PayableId=line.ExistingPayableId
      AND balance.BusinessId=@BusinessId
    JOIN dbo.Suppliers supplier ON supplier.SupplierId=balance.SupplierId AND supplier.TenantId=@TenantId
    JOIN dbo.Parties party ON party.PartyId=supplier.PartyId AND party.TenantId=@TenantId;
    INSERT @Positions
    SELECT N'Receivable',balance.ReceivableId,COALESCE(balance.PartyId,customer.PartyId),
      balance.OutstandingAmount,balance.OriginalAmount,balance.CurrencyCode,balance.Status,balance.OpeningEntryId,
      balance.OpeningEntryLineNumber,balance.SourceDocumentId,balance.SourceDocumentType
    FROM @Lines line JOIN dbo.Receivables balance WITH(UPDLOCK,HOLDLOCK) ON balance.ReceivableId=line.ExistingReceivableId
      AND balance.BusinessId=@BusinessId
    LEFT JOIN dbo.Customers customer ON customer.CustomerId=balance.CustomerId AND customer.TenantId=@TenantId
    JOIN dbo.Parties party ON party.PartyId=COALESCE(balance.PartyId,customer.PartyId) AND party.TenantId=@TenantId;
    IF (SELECT COUNT(*) FROM @Positions)<>(SELECT COUNT(*) FROM @Lines
      WHERE ExistingPayableId IS NOT NULL OR ExistingReceivableId IS NOT NULL)
      THROW 51408,N'Hay obligaciones de apertura fuera de la sede o empresa autorizada.',1;
    IF EXISTS(SELECT 1 FROM @Lines line JOIN @Positions position
      ON position.PositionId=COALESCE(line.ExistingPayableId,line.ExistingReceivableId)
        AND position.Kind=CASE WHEN line.ExistingPayableId IS NOT NULL THEN N'Payable' ELSE N'Receivable' END
      LEFT JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId AND account.TenantId=@TenantId
        AND account.IsActive=1 AND account.AllowsPosting=1
      LEFT JOIN dbo.AccountingPostingJobs origin ON origin.SourceDocumentId=position.SourceDocumentId
        AND origin.SourceDocumentType=position.SourceDocumentType AND origin.TenantId=@TenantId AND origin.BusinessId=@BusinessId
      CROSS APPLY accounting.ObligationAvailability(@BusinessId,position.Kind,position.PositionId,position.Amount,NULL) pending
      WHERE position.CurrencyCode<>N'COP' OR position.Amount<=0 OR position.Status NOT IN(N'Open',N'PartiallyPaid')
        OR position.OpeningEntryId IS NOT NULL OR position.OpeningLine IS NOT NULL
        OR origin.AccountingPostingJobId IS NULL OR ISNULL(origin.AccountingEntryRequired,1)<>0 OR origin.Status<>N'CommercialEffectsApplied'
        OR (@HistoricalCatchup=0 AND CONVERT(date,origin.OccurredAt)>=@EffectiveOn)
        OR line.PartyId IS NULL OR line.PartyId<>position.PartyId OR account.AccountId IS NULL
        OR account.RequiresParty<>1
        OR (@HistoricalCatchup=0 AND (ISNULL(account.FinancialFunction,N'')<>position.Kind
          OR position.Kind=N'Payable' AND (line.Debit<>0 OR line.Credit<>position.Amount OR account.AccountType<>N'Liability' OR ISNULL(account.BalanceSide,N'')<>N'Credit')
          OR position.Kind=N'Receivable' AND (line.Credit<>0 OR line.Debit<>position.Amount OR account.AccountType<>N'Asset' OR ISNULL(account.BalanceSide,N'')<>N'Debit')
          OR pending.PendingPaymentAmount<>0))
        OR (@HistoricalCatchup=1 AND (
          position.Kind=N'Receivable' AND (line.Credit<>0 OR line.Debit<>position.OriginalAmount
            OR account.AccountType<>N'Asset' OR account.BalanceSide IS NOT NULL AND account.BalanceSide<>N'Debit')
          OR position.Kind=N'Payable' AND (line.Debit<>0 OR line.Credit<>position.OriginalAmount
            OR account.AccountType<>N'Liability' OR account.BalanceSide IS NOT NULL AND account.BalanceSide<>N'Credit')
          OR account.FinancialFunction IS NOT NULL AND account.FinancialFunction<>position.Kind
          OR EXISTS(SELECT 1 FROM dbo.AccountingEntries entry
            WHERE entry.SourceDocumentId=position.SourceDocumentId
              AND entry.SourceDocumentType=position.SourceDocumentType)))
        OR pending.PendingAdjustmentCount<>0)
      THROW 51408,N'La obligación debe estar conciliada en COP, sin apertura contable ni aplicaciones pendientes, y coincidir con cuenta, tercero y saldo al corte.',1;

    DECLARE @Transactions TABLE(Kind nvarchar(16),PositionId uniqueidentifier,
      SourceDocumentId uniqueidentifier,OccurredAt datetimeoffset(7),TransactionType nvarchar(32),Amount decimal(19,4));
    INSERT @Transactions SELECT N'Payable',position.PositionId,movement.SourceDocumentId,movement.OccurredAt,movement.TransactionType,movement.Amount
    FROM @Positions position JOIN dbo.PayableTransactions movement ON movement.PayableId=position.PositionId
    WHERE position.Kind=N'Payable';
    INSERT @Transactions SELECT N'Receivable',position.PositionId,movement.SourceDocumentId,movement.OccurredAt,movement.TransactionType,movement.Amount
    FROM @Positions position JOIN dbo.ReceivableTransactions movement ON movement.ReceivableId=position.PositionId
    WHERE position.Kind=N'Receivable';
    IF @HistoricalCatchup=0
    BEGIN
      IF EXISTS(SELECT 1 FROM @Transactions WHERE CONVERT(date,OccurredAt)>=@EffectiveOn)
        OR EXISTS(SELECT 1 FROM @Transactions movement JOIN dbo.AccountingEntries entry
          ON entry.SourceDocumentId=movement.SourceDocumentId AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId)
        THROW 51408,N'La obligación tiene movimientos posteriores al corte o ya contabilizados; requiere revisión antes de incorporarla a la apertura.',1;
      IF EXISTS(SELECT 1 FROM @Positions position LEFT JOIN
        (SELECT SUM(movement.Amount) Balance,
          SUM(CASE WHEN movement.TransactionType=N'Opening' THEN 1 ELSE 0 END) OpeningCount,
          SUM(CASE WHEN movement.TransactionType=N'Opening' THEN movement.Amount ELSE 0 END) OpeningAmount,
          movement.Kind,movement.PositionId
         FROM @Transactions movement GROUP BY movement.Kind,movement.PositionId) history
         ON history.Kind=position.Kind AND history.PositionId=position.PositionId
        WHERE ISNULL(history.OpeningCount,0)<>1 OR history.Balance IS NULL OR history.Balance<>position.Amount
          OR history.OpeningAmount<>position.OriginalAmount)
        THROW 51408,N'El saldo de la obligación no coincide con su apertura y movimientos; concilia esa diferencia antes de incorporarlo al libro.',1;
      RETURN;
    END;

    -- A commercial-only imported obligation can have posted payments before
    -- its missing original journal is recognized. Those payments prove the
    -- control account; they are not replayed or subtracted a second time.
    IF EXISTS(SELECT 1 FROM @Transactions movement WHERE movement.TransactionType NOT IN(N'Opening',N'Payment',N'Adjustment')
        OR movement.TransactionType=N'Opening' AND CONVERT(date,movement.OccurredAt)>=@EffectiveOn
        OR movement.TransactionType=N'Payment' AND (movement.Amount<=0 OR CONVERT(date,movement.OccurredAt)<@EffectiveOn)
        OR movement.TransactionType=N'Adjustment' AND (movement.Amount=0 OR CONVERT(date,movement.OccurredAt)<@EffectiveOn))
      OR EXISTS(SELECT 1 FROM @Positions position LEFT JOIN
        (SELECT movement.Kind,movement.PositionId,
          SUM(CASE movement.TransactionType WHEN N'Opening' THEN movement.Amount
            WHEN N'Adjustment' THEN movement.Amount ELSE -movement.Amount END) Balance,
          SUM(CASE WHEN movement.TransactionType=N'Opening' THEN 1 ELSE 0 END) OpeningCount,
          SUM(CASE WHEN movement.TransactionType=N'Opening' THEN movement.Amount ELSE 0 END) OpeningAmount
         FROM @Transactions movement GROUP BY movement.Kind,movement.PositionId) history
         ON history.Kind=position.Kind AND history.PositionId=position.PositionId
        WHERE ISNULL(history.OpeningCount,0)<>1 OR history.OpeningAmount IS NULL
          OR history.OpeningAmount<>position.OriginalAmount OR history.Balance IS NULL
          OR history.Balance<>position.Amount)
      THROW 51408,N'La cartera histórica requiere una sola apertura y movimientos posteriores que expliquen exactamente su saldo.',1;
    IF EXISTS(SELECT 1 FROM @Transactions movement JOIN @Lines selected
        ON selected.ExistingReceivableId=movement.PositionId AND movement.Kind=N'Receivable'
          OR selected.ExistingPayableId=movement.PositionId AND movement.Kind=N'Payable'
      JOIN @Positions position ON position.PositionId=movement.PositionId AND position.Kind=movement.Kind
      OUTER APPLY
      (
        SELECT payment.PaymentId,payment.TotalAmount,application.PaymentId AppliedPaymentId
        FROM dbo.CustomerPayments payment
        JOIN dbo.CustomerPaymentApplications application ON application.PaymentId=payment.PaymentId
          AND application.ReceivableId=movement.PositionId AND application.Amount=movement.Amount
        WHERE movement.Kind=N'Receivable' AND payment.PaymentId=movement.SourceDocumentId
          AND payment.BusinessId=@BusinessId AND payment.Status=N'Processed' AND payment.ParentSourceDocumentId IS NULL
        UNION ALL
        SELECT payment.PaymentId,payment.TotalAmount,application.PaymentId
        FROM dbo.SupplierPayments payment
        JOIN dbo.SupplierPaymentApplications application ON application.PaymentId=payment.PaymentId
          AND application.PayableId=movement.PositionId AND application.Amount=movement.Amount
        WHERE movement.Kind=N'Payable' AND payment.PaymentId=movement.SourceDocumentId
          AND payment.BusinessId=@BusinessId AND payment.Status=N'Processed' AND payment.ParentSourceDocumentId IS NULL
      ) payment
      LEFT JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=payment.PaymentId
        AND job.SourceDocumentType=CASE movement.Kind WHEN N'Receivable' THEN N'ReceivablePayment' ELSE N'PayablePayment' END AND job.TenantId=@TenantId
        AND job.BusinessId=@BusinessId AND job.Status=N'Posted' AND job.AccountingEntryRequired=1
      LEFT JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=job.SourceDocumentId
        AND entry.SourceDocumentType=job.SourceDocumentType AND entry.TenantId=@TenantId
        AND entry.BusinessId=@BusinessId AND entry.SourcePayloadHash=job.SourcePayloadHash
      LEFT JOIN dbo.AccountingEntryLines journal ON journal.EntryId=entry.EntryId
        AND journal.AccountId=selected.AccountId AND journal.PartyId=position.PartyId
        AND (movement.Kind=N'Receivable' AND journal.Debit=0 AND journal.Credit>0
          OR movement.Kind=N'Payable' AND journal.Credit=0 AND journal.Debit>0)
      WHERE movement.TransactionType=N'Payment'
      GROUP BY movement.Kind,movement.PositionId,movement.SourceDocumentId,movement.Amount
      HAVING COUNT_BIG(payment.AppliedPaymentId)=0
        OR ISNULL(SUM(CASE movement.Kind WHEN N'Receivable' THEN journal.Credit ELSE journal.Debit END),0)<>ISNULL(MAX(payment.TotalAmount),0))
      OR EXISTS(SELECT 1 FROM @Transactions movement JOIN @Lines selected
          ON selected.ExistingReceivableId=movement.PositionId AND movement.Kind=N'Receivable'
            OR selected.ExistingPayableId=movement.PositionId AND movement.Kind=N'Payable'
        JOIN @Positions position ON position.PositionId=movement.PositionId AND position.Kind=movement.Kind
        LEFT JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=movement.SourceDocumentId
          AND job.SourceDocumentType=N'AccountAdjustment' AND job.TenantId=@TenantId
          AND job.BusinessId=@BusinessId AND job.Status=N'Posted' AND job.AccountingEntryRequired=1
        LEFT JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=job.SourceDocumentId
          AND entry.SourceDocumentType=job.SourceDocumentType AND entry.TenantId=@TenantId
          AND entry.BusinessId=@BusinessId AND entry.SourcePayloadHash=job.SourcePayloadHash
        LEFT JOIN dbo.AccountingEntryLines journal ON journal.EntryId=entry.EntryId
          AND journal.AccountId=selected.AccountId AND journal.PartyId=position.PartyId
        WHERE movement.TransactionType=N'Adjustment'
        GROUP BY movement.Kind,movement.PositionId,movement.SourceDocumentId,movement.Amount
        HAVING COUNT_BIG(entry.EntryId)=0
          OR ISNULL(SUM(CASE movement.Kind WHEN N'Receivable' THEN journal.Debit-journal.Credit ELSE journal.Credit-journal.Debit END),0)<>MAX(movement.Amount))
      OR EXISTS(SELECT 1 FROM @Lines selected JOIN dbo.AccountingAccounts account
        ON account.AccountId=selected.AccountId AND account.TenantId=@TenantId
        WHERE (selected.ExistingReceivableId IS NOT NULL OR selected.ExistingPayableId IS NOT NULL) AND account.FinancialFunction IS NULL
          AND NOT EXISTS(SELECT 1 FROM @Transactions movement
            WHERE (movement.PositionId=selected.ExistingReceivableId AND movement.Kind=N'Receivable'
              OR movement.PositionId=selected.ExistingPayableId AND movement.Kind=N'Payable')
              AND movement.TransactionType IN(N'Payment',N'Adjustment')))
      THROW 51408,N'Los pagos, recaudos o ajustes previos no prueban una única cuenta de cartera y asiento por su valor. Revisa la conciliación histórica.',1;
END;
