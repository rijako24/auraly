CREATE PROCEDURE accounting.OpeningPositionsValidate
    @TenantId uniqueidentifier,
    @BusinessId uniqueidentifier,
    @EffectiveOn date,
    @LinesJson nvarchar(max),
    @HistoricalCatchup bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    IF @@TRANCOUNT=0 OR ISNULL(ISJSON(@LinesJson),0)<>1 OR LEFT(LTRIM(@LinesJson),1)<>N'['
      THROW 51408,N'La conciliación de apertura requiere una transacción y sus partidas.',1;
    DECLARE @Lines TABLE(LineNumber int PRIMARY KEY,AccountId uniqueidentifier,PartyId uniqueidentifier,
      Debit decimal(19,4),Credit decimal(19,4),LineId uniqueidentifier,
      ExistingPayableId uniqueidentifier,ExistingReceivableId uniqueidentifier);
    INSERT @Lines SELECT LineNumber,AccountId,PartyId,Debit,Credit,LineId,ExistingPayableId,ExistingReceivableId
    FROM OPENJSON(@LinesJson) WITH(LineNumber int,AccountId uniqueidentifier,PartyId uniqueidentifier,
      Debit decimal(19,4),Credit decimal(19,4),LineId uniqueidentifier,
      ExistingPayableId uniqueidentifier,ExistingReceivableId uniqueidentifier);
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
        AND account.IsActive=1 AND account.AllowsPosting=1 AND (account.BusinessId IS NULL OR account.BusinessId=@BusinessId)
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
        OR (@HistoricalCatchup=1 AND (position.Kind<>N'Receivable' OR line.Credit<>0
          OR line.Debit<>position.OriginalAmount OR account.AccountType<>N'Asset'
          OR account.FinancialFunction IS NOT NULL AND account.FinancialFunction<>N'Receivable'
          OR account.BalanceSide IS NOT NULL AND account.BalanceSide<>N'Debit'
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

    -- A late, commercial-only imported sale can have posted collections before
    -- its missing original journal is recognized. Those collections prove the
    -- control account; they are not replayed or subtracted a second time.
    IF EXISTS(SELECT 1 FROM @Transactions movement WHERE movement.TransactionType NOT IN(N'Opening',N'Payment',N'Adjustment')
        OR movement.TransactionType=N'Opening' AND CONVERT(date,movement.OccurredAt)>=@EffectiveOn
        OR movement.TransactionType=N'Payment' AND (movement.Amount<=0 OR CONVERT(date,movement.OccurredAt)<@EffectiveOn)
        OR movement.TransactionType=N'Adjustment' AND (movement.Amount=0 OR CONVERT(date,movement.OccurredAt)<@EffectiveOn))
      OR EXISTS(SELECT 1 FROM @Positions position LEFT JOIN
        (SELECT movement.PositionId,
          SUM(CASE movement.TransactionType WHEN N'Opening' THEN movement.Amount
            WHEN N'Adjustment' THEN movement.Amount ELSE -movement.Amount END) Balance,
          SUM(CASE WHEN movement.TransactionType=N'Opening' THEN 1 ELSE 0 END) OpeningCount,
          SUM(CASE WHEN movement.TransactionType=N'Opening' THEN movement.Amount ELSE 0 END) OpeningAmount
         FROM @Transactions movement GROUP BY movement.PositionId) history ON history.PositionId=position.PositionId
        WHERE ISNULL(history.OpeningCount,0)<>1 OR history.OpeningAmount IS NULL
          OR history.OpeningAmount<>position.OriginalAmount OR history.Balance IS NULL
          OR history.Balance<>position.Amount)
      THROW 51408,N'La cartera histórica requiere una sola apertura y movimientos posteriores que expliquen exactamente su saldo.',1;
    IF EXISTS(SELECT 1 FROM @Transactions movement JOIN @Lines selected
        ON selected.ExistingReceivableId=movement.PositionId
      JOIN @Positions position ON position.PositionId=movement.PositionId
        AND position.Kind=N'Receivable'
      LEFT JOIN dbo.CustomerPayments payment ON payment.PaymentId=movement.SourceDocumentId
        AND payment.BusinessId=@BusinessId AND payment.Status=N'Processed'
      LEFT JOIN dbo.CustomerPaymentApplications application ON application.PaymentId=payment.PaymentId
        AND application.ReceivableId=movement.PositionId AND application.Amount=movement.Amount
      LEFT JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=application.PaymentId
        AND job.SourceDocumentType=N'ReceivablePayment' AND job.TenantId=@TenantId
        AND job.BusinessId=@BusinessId AND job.Status=N'Posted' AND job.AccountingEntryRequired=1
      LEFT JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=job.SourceDocumentId
        AND entry.SourceDocumentType=job.SourceDocumentType AND entry.TenantId=@TenantId
        AND entry.BusinessId=@BusinessId AND entry.SourcePayloadHash=job.SourcePayloadHash
      LEFT JOIN dbo.AccountingEntryLines journal ON journal.EntryId=entry.EntryId
        AND journal.AccountId=selected.AccountId AND journal.PartyId=position.PartyId
        AND journal.Debit=0 AND journal.Credit>0
      WHERE movement.TransactionType=N'Payment'
      GROUP BY movement.PositionId,movement.SourceDocumentId,movement.Amount
      HAVING COUNT_BIG(application.PaymentId)=0
        OR ISNULL(SUM(journal.Credit),0)<>ISNULL(MAX(payment.TotalAmount),0))
      OR EXISTS(SELECT 1 FROM @Transactions movement JOIN @Lines selected
          ON selected.ExistingReceivableId=movement.PositionId
        JOIN @Positions position ON position.PositionId=movement.PositionId
          AND position.Kind=N'Receivable'
        LEFT JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=movement.SourceDocumentId
          AND job.SourceDocumentType=N'AccountAdjustment' AND job.TenantId=@TenantId
          AND job.BusinessId=@BusinessId AND job.Status=N'Posted' AND job.AccountingEntryRequired=1
        LEFT JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=job.SourceDocumentId
          AND entry.SourceDocumentType=job.SourceDocumentType AND entry.TenantId=@TenantId
          AND entry.BusinessId=@BusinessId AND entry.SourcePayloadHash=job.SourcePayloadHash
        LEFT JOIN dbo.AccountingEntryLines journal ON journal.EntryId=entry.EntryId
          AND journal.AccountId=selected.AccountId AND journal.PartyId=position.PartyId
        WHERE movement.TransactionType=N'Adjustment'
        GROUP BY movement.PositionId,movement.SourceDocumentId,movement.Amount
        HAVING COUNT_BIG(entry.EntryId)=0
          OR ISNULL(SUM(journal.Debit-journal.Credit),0)<>MAX(movement.Amount))
      OR EXISTS(SELECT 1 FROM @Lines selected JOIN dbo.AccountingAccounts account
        ON account.AccountId=selected.AccountId AND account.TenantId=@TenantId
        WHERE selected.ExistingReceivableId IS NOT NULL AND account.FinancialFunction IS NULL
          AND NOT EXISTS(SELECT 1 FROM @Transactions movement
            WHERE movement.PositionId=selected.ExistingReceivableId
              AND movement.TransactionType IN(N'Payment',N'Adjustment')))
      THROW 51408,N'Los recaudos o ajustes previos no prueban una única cuenta de cartera y asiento por su valor. Revisa la conciliación histórica.',1;
END;
