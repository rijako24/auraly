CREATE PROCEDURE accounting.OpeningPositionsValidate
    @TenantId uniqueidentifier,
    @BusinessId uniqueidentifier,
    @EffectiveOn date,
    @LinesJson nvarchar(max)
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
        OR CONVERT(date,origin.OccurredAt)>=@EffectiveOn
        OR line.PartyId IS NULL OR line.PartyId<>position.PartyId OR account.AccountId IS NULL
        OR ISNULL(account.FinancialFunction,N'')<>position.Kind OR account.RequiresParty<>1
        OR (position.Kind=N'Payable' AND (line.Debit<>0 OR line.Credit<>position.Amount OR account.AccountType<>N'Liability' OR ISNULL(account.BalanceSide,N'')<>N'Credit'))
        OR (position.Kind=N'Receivable' AND (line.Credit<>0 OR line.Debit<>position.Amount OR account.AccountType<>N'Asset' OR ISNULL(account.BalanceSide,N'')<>N'Debit'))
        OR pending.PendingPaymentAmount<>0 OR pending.PendingAdjustmentCount<>0)
      THROW 51408,N'La obligación debe estar conciliada en COP, sin apertura contable ni aplicaciones pendientes, y coincidir con cuenta, tercero y saldo al corte.',1;

    DECLARE @Transactions TABLE(Kind nvarchar(16),PositionId uniqueidentifier,
      SourceDocumentId uniqueidentifier,OccurredAt datetimeoffset(7),TransactionType nvarchar(32),Amount decimal(19,4));
    INSERT @Transactions SELECT N'Payable',position.PositionId,movement.SourceDocumentId,movement.OccurredAt,movement.TransactionType,movement.Amount
    FROM @Positions position JOIN dbo.PayableTransactions movement ON movement.PayableId=position.PositionId
    WHERE position.Kind=N'Payable';
    INSERT @Transactions SELECT N'Receivable',position.PositionId,movement.SourceDocumentId,movement.OccurredAt,movement.TransactionType,movement.Amount
    FROM @Positions position JOIN dbo.ReceivableTransactions movement ON movement.ReceivableId=position.PositionId
    WHERE position.Kind=N'Receivable';
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
END;
