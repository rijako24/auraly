CREATE PROCEDURE dbo.AccountingSourcesAccept
    @TenantId uniqueidentifier,
    @SourcesJson nvarchar(max),
    @Now datetimeoffset(7),
    @Requirement tinyint,
    @QueuedCount int = NULL OUTPUT,
    @NewSourceCount int = NULL OUTPUT,
    @SingleSourceStatus nvarchar(32) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- The caller owns acceptance and its transaction. This procedure only writes
    -- the immutable input, work and derived configuration protection for the
    -- existing accounting engine. It does not apply balances or journal entries.
    IF @@TRANCOUNT=0
      THROW 51730,N'Accounting acceptance requires the caller transaction.',1;
    IF @Requirement IS NULL OR @Requirement NOT IN(0,1,2,3)
      OR ISNULL(ISJSON(@SourcesJson),0)<>1 OR LEFT(LTRIM(@SourcesJson),1)<>N'['
      OR (SELECT COUNT(*) FROM OPENJSON(@SourcesJson)) NOT BETWEEN 1 AND 100
      THROW 51731,N'The accounting acceptance contract is invalid.',1;

    -- Manual book commands serialize against configuration before locking
    -- their obligations. Keep that lock order inside the same acceptance owner.
    IF @Requirement=3
    BEGIN
      DECLARE @LockedSettingsTenant uniqueidentifier;
      SELECT @LockedSettingsTenant=TenantId FROM dbo.AccountingTenantSettings WITH(UPDLOCK,HOLDLOCK)
      WHERE TenantId=@TenantId;
    END;

    DECLARE @Sources TABLE
    (
      DocumentId uniqueidentifier NOT NULL,
      DocumentType nvarchar(64) NOT NULL,
      BusinessId uniqueidentifier NOT NULL,
      Payload nvarchar(max) NULL,
      PayloadHash binary(32) NULL,
      OccurredAt datetimeoffset(7) NOT NULL,
      JobId uniqueidentifier NOT NULL,
      EntryRequired bit NOT NULL,
      PRIMARY KEY(DocumentId,DocumentType)
    );
    INSERT @Sources
    SELECT j.DocumentId,j.DocumentType,j.BusinessId,
      CASE WHEN j.Payload IS NULL THEN p.PayloadJson ELSE j.Payload END,
      CASE WHEN j.Payload IS NULL THEN p.PayloadHash ELSE CONVERT(binary(32),j.Hash,2) END,
      j.OccurredAt,j.JobId,CONVERT(bit,CASE WHEN @Requirement=2 OR EXISTS(
        SELECT 1 FROM dbo.AccountingTenantSettings WITH(HOLDLOCK)
        WHERE TenantId=@TenantId AND Status=N'Ready'
          AND EffectiveFrom<=CONVERT(date,j.OccurredAt)) THEN 1 ELSE 0 END)
    FROM OPENJSON(@SourcesJson) WITH(DocumentId uniqueidentifier,DocumentType nvarchar(64),
      BusinessId uniqueidentifier,Payload nvarchar(max),Hash varchar(64),
      OccurredAt datetimeoffset(7),JobId uniqueidentifier) j
    LEFT JOIN dbo.DocumentProcessingPayloads p ON j.Payload IS NULL
      AND p.DocumentId=j.DocumentId AND p.DocumentType=j.DocumentType AND p.BusinessId=j.BusinessId;

    IF EXISTS(SELECT 1 FROM @Sources i
      JOIN dbo.AccountingSourceDocuments s WITH(UPDLOCK,HOLDLOCK)
        ON s.SourceDocumentId=i.DocumentId AND s.SourceDocumentType=i.DocumentType
      WHERE s.TenantId<>@TenantId OR s.BusinessId<>i.BusinessId
        OR s.PayloadHash<>i.PayloadHash)
      THROW 51732,N'The accounting source was reused with different data.',1;

    IF (SELECT COUNT(*) FROM @Sources) NOT BETWEEN 1 AND 100 OR EXISTS(
      SELECT 1 FROM @Sources s
      WHERE s.Payload IS NULL OR s.PayloadHash IS NULL OR ISJSON(s.Payload)<>1
        OR NOT EXISTS(SELECT 1 FROM dbo.Businesses b
          WHERE b.BusinessId=s.BusinessId AND b.TenantId=@TenantId)
        OR (@Requirement=3 AND s.DocumentType NOT IN(N'ManualAccountingVoucher',N'AccountAdjustment'))
        OR (@Requirement=2 AND NOT EXISTS(SELECT 1 FROM dbo.AccountingSourceDocuments accepted
          WHERE accepted.SourceDocumentId=s.DocumentId AND accepted.SourceDocumentType=s.DocumentType)
          AND (s.DocumentType<>N'AccountingOpeningBalance' OR NOT EXISTS(
          SELECT 1 FROM dbo.AccountingTenantSettings a WHERE a.TenantId=@TenantId
            AND a.Status=N'Configuring' AND a.OpeningBalanceMode=N'ImportedAndApproved'
            AND a.EffectiveFrom=CONVERT(date,s.OccurredAt)))))
      THROW 51731,N'The accounting source is missing or outside its acceptance scope.',1;

    -- A replay returns the accepted input unchanged. It never repairs or repeats
    -- an already accepted operation by creating another job.
    -- Read only the input keys. Source locks above serialize their acceptance;
    -- this check does not update jobs. An update-range lock on a missing job
    -- can conflict with the neighboring job's processor while it waits for a
    -- position already held by this acceptance.
    DECLARE @InvalidSourceJob bit=0;
    SELECT @InvalidSourceJob=1 FROM @Sources i
      LEFT JOIN dbo.AccountingSourceDocuments s WITH(FORCESEEK)
        ON s.SourceDocumentId=i.DocumentId AND s.SourceDocumentType=i.DocumentType
      LEFT JOIN dbo.AccountingPostingJobs j WITH(FORCESEEK)
        ON j.SourceDocumentId=i.DocumentId AND j.SourceDocumentType=i.DocumentType
      WHERE (s.SourceDocumentId IS NOT NULL AND NOT EXISTS(SELECT 1
        WHERE j.TenantId=s.TenantId AND j.BusinessId=s.BusinessId
          AND j.SourcePayloadHash=s.PayloadHash AND j.OccurredAt=s.OccurredAt
          AND j.AccountingEntryRequired=s.AccountingEntryRequired))
        OR (j.AccountingPostingJobId IS NOT NULL AND s.SourceDocumentId IS NULL)
      OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
    IF @InvalidSourceJob=1
      THROW 51733,N'The accepted accounting source has no matching processing job.',1;

    -- Reserve non-cash applications from their accepted snapshot, never from
    -- mutable draft rows. Replays do not parse, validate or repair this index.
    DECLARE @NewManualSources TABLE(DocumentId uniqueidentifier PRIMARY KEY,BusinessId uniqueidentifier,Payload nvarchar(max));
    INSERT @NewManualSources SELECT input.DocumentId,input.BusinessId,input.Payload FROM @Sources input
      WHERE input.DocumentType=N'ManualAccountingVoucher' AND NOT EXISTS(SELECT 1 FROM dbo.AccountingSourceDocuments existing
        WHERE existing.SourceDocumentId=input.DocumentId AND existing.SourceDocumentType=input.DocumentType);
    DECLARE @CreditTargets TABLE(DocumentId uniqueidentifier,BusinessId uniqueidentifier,EffectId uniqueidentifier,
      Kind nvarchar(16),PositionId uniqueidentifier,SourceLineId uniqueidentifier,Amount decimal(19,4),
      EffectKind nvarchar(24),ReversesTransactionId uniqueidentifier,
      PRIMARY KEY(DocumentId,EffectId,Kind));
    INSERT @CreditTargets SELECT source.DocumentId,source.BusinessId,target.EffectId,target.SubledgerKind,
      target.SubledgerId,target.SourceLineId,target.Amount,target.EffectKind,target.ReversesTransactionId FROM @NewManualSources source
      CROSS APPLY accounting.ManualVoucherCreditTargets(source.Payload) target;
    IF EXISTS(SELECT 1 FROM @CreditTargets WHERE Kind NOT IN(N'Payable',N'Receivable',N'SupplierCredit',N'CustomerCredit')
        OR PositionId IS NULL OR SourceLineId IS NULL OR Amount IS NULL OR Amount<=0)
      OR EXISTS(SELECT DocumentId FROM @CreditTargets GROUP BY DocumentId HAVING COUNT(*)>1000)
      THROW 51849,N'The financial application destinations are invalid.',1;
    IF EXISTS(SELECT 1 FROM @CreditTargets)
    BEGIN
      DECLARE @CreditKeys TABLE(BusinessId uniqueidentifier,Kind nvarchar(16),PositionId uniqueidentifier,
        PRIMARY KEY(BusinessId,Kind,PositionId));
      INSERT @CreditKeys SELECT DISTINCT BusinessId,Kind,PositionId FROM @CreditTargets;
      DECLARE @CreditPositions TABLE(BusinessId uniqueidentifier,Kind nvarchar(16),PositionId uniqueidentifier,
        Outstanding decimal(19,4),Currency char(3),Status nvarchar(24),PRIMARY KEY(BusinessId,Kind,PositionId));
      -- Same lock order as manual acceptance and the financial processor.
      INSERT @CreditPositions SELECT input.BusinessId,input.Kind,p.PayableId,p.OutstandingAmount,p.CurrencyCode,p.Status
      FROM @CreditKeys input JOIN dbo.Payables p WITH(UPDLOCK,HOLDLOCK)
        ON input.Kind=N'Payable' AND p.PayableId=input.PositionId AND p.BusinessId=input.BusinessId
      JOIN dbo.Suppliers supplier ON supplier.SupplierId=p.SupplierId AND supplier.TenantId=@TenantId
      ORDER BY input.BusinessId,input.PositionId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
      INSERT @CreditPositions SELECT input.BusinessId,input.Kind,p.ReceivableId,p.OutstandingAmount,p.CurrencyCode,p.Status
      FROM @CreditKeys input JOIN dbo.Receivables p WITH(UPDLOCK,HOLDLOCK)
        ON input.Kind=N'Receivable' AND p.ReceivableId=input.PositionId AND p.BusinessId=input.BusinessId
      LEFT JOIN dbo.Customers customer ON customer.CustomerId=p.CustomerId AND customer.TenantId=@TenantId
      JOIN dbo.Parties party ON party.PartyId=COALESCE(p.PartyId,customer.PartyId) AND party.TenantId=@TenantId
      ORDER BY input.BusinessId,input.PositionId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
      INSERT @CreditPositions SELECT input.BusinessId,input.Kind,p.SupplierCreditId,p.AvailableAmount,p.CurrencyCode,p.Status
      FROM @CreditKeys input JOIN dbo.SupplierCredits p WITH(UPDLOCK,HOLDLOCK)
        ON input.Kind=N'SupplierCredit' AND p.SupplierCreditId=input.PositionId AND p.BusinessId=input.BusinessId
      JOIN dbo.Suppliers supplier ON supplier.SupplierId=p.SupplierId AND supplier.TenantId=@TenantId
      WHERE p.CreditNature IN(N'Credit',N'Advance')
      ORDER BY input.BusinessId,input.PositionId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
      INSERT @CreditPositions SELECT input.BusinessId,input.Kind,p.CustomerCreditId,p.AvailableAmount,p.CurrencyCode,p.Status
      FROM @CreditKeys input JOIN dbo.CustomerCredits p WITH(UPDLOCK,HOLDLOCK)
        ON input.Kind=N'CustomerCredit' AND p.CustomerCreditId=input.PositionId AND p.BusinessId=input.BusinessId
      LEFT JOIN dbo.Customers customer ON customer.CustomerId=p.CustomerId AND customer.TenantId=@TenantId
      JOIN dbo.Parties party ON party.PartyId=COALESCE(p.PartyId,customer.PartyId) AND party.TenantId=@TenantId
      WHERE p.CreditNature IN(N'Credit',N'Advance')
      ORDER BY input.BusinessId,input.PositionId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
      IF (SELECT COUNT(*) FROM @CreditKeys)<>(SELECT COUNT(*) FROM @CreditPositions)
        OR EXISTS(SELECT 1 FROM @CreditPositions WHERE Currency IS NULL OR Currency<>N'COP' OR Status=N'Cancelled')
        THROW 51849,N'The application requires positions in scope with their currency and credit nature reconciled.',1;
      DECLARE @ApplicationError nvarchar(2048);
      SELECT TOP(1) @ApplicationError=CASE WHEN available.SourceCancelled=1
          THEN N'No se aplicó el cruce: el documento de origen fue anulado. Sus saldos se conservan.'
          WHEN available.PendingCancellationCount>0
          THEN N'No se aplicó el cruce: la obligación tiene una anulación aceptada pendiente. Sus saldos se conservan.'
          ELSE N'No se aplicó el cruce: el importe supera el saldo disponible de la obligación o del saldo a favor, incluidas sus reservas.' END
        FROM @CreditPositions position
        JOIN(SELECT BusinessId,Kind,PositionId,SUM(Amount) Amount FROM @CreditTargets
          WHERE EffectKind IN(N'CreditApplication',N'Cancellation') GROUP BY BusinessId,Kind,PositionId) requested
          ON requested.BusinessId=position.BusinessId AND requested.Kind=position.Kind AND requested.PositionId=position.PositionId
        CROSS APPLY accounting.ObligationAvailability(position.BusinessId,position.Kind,position.PositionId,position.Outstanding,NULL) available
        WHERE available.SourceCancelled=1 OR available.PendingCancellationCount>0 OR requested.Amount>available.AvailableAmount
        ORDER BY position.BusinessId,position.Kind,position.PositionId;
      IF @ApplicationError IS NOT NULL THROW 51848,@ApplicationError,1;

      -- A settled obligation can be reopened, but its original application
      -- cannot be unapplied twice. Position locks serialize captures; the same
      -- immutable destination index reserves the original transaction amount.
      DECLARE @Reversals TABLE(BusinessId uniqueidentifier,Kind nvarchar(16),TransactionId uniqueidentifier,
        PositionId uniqueidentifier,Amount decimal(19,4),PRIMARY KEY(BusinessId,Kind,TransactionId,PositionId));
      INSERT @Reversals SELECT BusinessId,Kind,ReversesTransactionId,PositionId,SUM(Amount)
        FROM @CreditTargets WHERE EffectKind IN(N'Unapplication',N'CreditUnapplication') AND Kind IN(N'Payable',N'Receivable')
        GROUP BY BusinessId,Kind,ReversesTransactionId,PositionId;
      DECLARE @ReversalError nvarchar(2048);
      SELECT TOP(1) @ReversalError=CASE WHEN available.SourceCancelled=1
          THEN N'No se desaplicó el abono: el documento de origen fue anulado. El abono y sus saldos se conservan.'
          ELSE N'No se desaplicó el abono: la obligación tiene una anulación aceptada pendiente. El abono y sus saldos se conservan.' END
        FROM @Reversals input
        CROSS APPLY accounting.ObligationAvailability(input.BusinessId,input.Kind,input.PositionId,0,NULL) available
        WHERE available.SourceCancelled=1 OR available.PendingCancellationCount>0
        ORDER BY input.BusinessId,input.Kind,input.PositionId;
      IF @ReversalError IS NOT NULL THROW 51848,@ReversalError,1;
      DECLARE @OriginalSettlements TABLE(BusinessId uniqueidentifier,Kind nvarchar(16),TransactionId uniqueidentifier,
        PositionId uniqueidentifier,Amount decimal(19,4),OccurredAt datetimeoffset(7),PaymentId uniqueidentifier,WorkSessionId uniqueidentifier,
        TransactionType nvarchar(24),CreditId uniqueidentifier,
        PRIMARY KEY(BusinessId,Kind,TransactionId,PositionId));
      INSERT @OriginalSettlements
      SELECT input.BusinessId,input.Kind,original.TransactionId,original.PositionId,original.Amount,original.OccurredAt,original.PaymentId,original.WorkSessionId,
        original.TransactionType,original.CreditId
      FROM @Reversals input JOIN accounting.AppliedPortfolioSettlements original
        ON original.SubledgerKind=input.Kind AND original.TransactionId=input.TransactionId AND original.PositionId=input.PositionId
          AND original.BusinessId=input.BusinessId AND original.TenantId=@TenantId;
      IF (SELECT COUNT(*) FROM @OriginalSettlements)<>(SELECT COUNT(*) FROM @Reversals)
        OR EXISTS(SELECT 1 FROM @CreditTargets target JOIN @OriginalSettlements original
          ON original.BusinessId=target.BusinessId AND original.Kind=target.Kind AND original.TransactionId=target.ReversesTransactionId
            AND original.PositionId=target.PositionId
          WHERE (target.EffectKind=N'Unapplication' AND original.TransactionType<>N'Payment')
            OR (target.EffectKind=N'CreditUnapplication' AND (original.TransactionType<>N'CreditApplication'
              OR NOT EXISTS(SELECT 1 FROM @CreditTargets credit WHERE credit.DocumentId=target.DocumentId
                AND credit.EffectId=target.EffectId AND credit.EffectKind=N'CreditUnapplication'
                AND credit.Kind=CASE target.Kind WHEN N'Payable' THEN N'SupplierCredit' ELSE N'CustomerCredit' END
                AND credit.PositionId=original.CreditId AND credit.Amount=target.Amount
                AND credit.ReversesTransactionId=target.ReversesTransactionId))))
        OR EXISTS(SELECT 1 FROM @CreditTargets target JOIN @OriginalSettlements original
          ON original.BusinessId=target.BusinessId AND original.Kind=target.Kind AND original.TransactionId=target.ReversesTransactionId
            AND original.PositionId=target.PositionId
          JOIN @Sources source ON source.DocumentId=target.DocumentId AND source.DocumentType=N'ManualAccountingVoucher'
          -- Compare calendar dates in the voucher's offset. Native payment
          -- captures use UTC; a local evening must not become tomorrow here.
          WHERE CONVERT(date,source.OccurredAt)<CONVERT(date,
            SWITCHOFFSET(original.OccurredAt,DATEPART(TZOFFSET,source.OccurredAt))))
        THROW 51851,N'No se desaplicó el movimiento: selecciona la aplicación original de esta obligación, su contrapartida y una fecha posterior o igual.',1;
      IF EXISTS(SELECT 1 FROM (SELECT DISTINCT BusinessId,WorkSessionId FROM @OriginalSettlements WHERE WorkSessionId IS NOT NULL) session
        JOIN dbo.WorkSessions closedSession ON closedSession.WorkSessionId=session.WorkSessionId AND closedSession.BusinessId=session.BusinessId
        JOIN dbo.WorkSessionClosures closure ON closure.WorkSessionId=closedSession.WorkSessionId
        JOIN dbo.WorkSessionClosureReconciliations reconciliation ON reconciliation.WorkSessionClosureId=closure.WorkSessionClosureId
        CROSS APPLY OPENJSON(reconciliation.SnapshotJson,'$.paymentCorrections')
          WITH(SourceId uniqueidentifier '$.sourceId',MovementType nvarchar(32) '$.movementType',SubledgerId uniqueidentifier '$.subledgerId',
            Amount decimal(19,4) '$.amount',OriginalAmount decimal(19,4) '$.originalAmount') correction
        -- Applied corrections identify the session movement, or the payment
        -- itself in historical receipts; they do not store SourceDocumentType.
        LEFT JOIN dbo.WorkSessionMovements movement ON movement.MovementType=correction.MovementType
          AND movement.WorkSessionMovementId=correction.SourceId AND movement.WorkSessionId=session.WorkSessionId
        JOIN @OriginalSettlements original ON original.BusinessId=session.BusinessId
          AND original.PositionId=correction.SubledgerId
          AND correction.MovementType=CASE WHEN original.Kind=N'Payable' THEN N'PayablePayment' ELSE N'ReceivablePayment' END
          AND original.PaymentId=COALESCE(movement.DocumentId,correction.SourceId)
        WHERE correction.Amount<>correction.OriginalAmount)
        THROW 51851,N'No se desaplicó el abono: su importe fue corregido en el cierre y requiere conciliar esa corrección antes de desaplicarlo. El pago y la obligación se conservan.',1;
      IF EXISTS(SELECT 1 FROM @Reversals input JOIN @OriginalSettlements original
          ON original.BusinessId=input.BusinessId AND original.Kind=input.Kind
            AND original.TransactionId=input.TransactionId AND original.PositionId=input.PositionId
        CROSS APPLY accounting.UnapplicationAvailability(input.BusinessId,input.Kind,input.TransactionId,original.Amount) available
        WHERE input.Amount>available.AvailableAmount)
        THROW 51848,N'No se desaplicó el movimiento: el importe supera lo disponible de la aplicación original, incluidas las desaplicaciones pendientes.',1;
    END;

    DECLARE @Accepted TABLE(DocumentId uniqueidentifier,DocumentType nvarchar(64),
      PRIMARY KEY(DocumentId,DocumentType));
    INSERT dbo.AccountingSourceDocuments
      (SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,PayloadHash,
       OccurredAt,AcceptedAt,AccountingEntryRequired)
    OUTPUT inserted.SourceDocumentId,inserted.SourceDocumentType INTO @Accepted
    SELECT i.DocumentId,i.DocumentType,@TenantId,i.BusinessId,i.Payload,i.PayloadHash,
      i.OccurredAt,@Now,i.EntryRequired
    FROM @Sources i
    WHERE (i.EntryRequired=1 OR @Requirement=1)
      AND NOT EXISTS(SELECT 1 FROM dbo.AccountingSourceDocuments s WITH(UPDLOCK,HOLDLOCK)
        WHERE s.SourceDocumentId=i.DocumentId AND s.SourceDocumentType=i.DocumentType);

    INSERT dbo.AccountingPostingJobs
      (AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,
       SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt)
    SELECT i.JobId,s.TenantId,s.BusinessId,s.SourceDocumentId,s.SourceDocumentType,
      s.PayloadHash,s.OccurredAt,s.AccountingEntryRequired,N'Pending',0,@Now
    FROM @Accepted a JOIN @Sources i ON i.DocumentId=a.DocumentId AND i.DocumentType=a.DocumentType
    JOIN dbo.AccountingSourceDocuments s
      ON s.SourceDocumentId=a.DocumentId AND s.SourceDocumentType=a.DocumentType;

    UPDATE source SET AccountingPostingJobId=job.AccountingPostingJobId
    FROM @Accepted accepted
    JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=accepted.DocumentId
      AND source.SourceDocumentType=accepted.DocumentType
    JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=source.SourceDocumentId
      AND job.SourceDocumentType=source.SourceDocumentType
      AND job.TenantId=source.TenantId AND job.BusinessId=source.BusinessId;

    IF EXISTS(SELECT 1 FROM @Accepted)
    BEGIN
      DECLARE @BankSources nvarchar(max)=(SELECT accepted.DocumentId,accepted.DocumentType,input.BusinessId
        FROM @Accepted accepted JOIN @Sources input ON input.DocumentId=accepted.DocumentId
          AND input.DocumentType=accepted.DocumentType FOR JSON PATH);
      EXEC accounting.BankAccountsFreeze @TenantId,@BankSources,@Now;
      EXEC accounting.PortfolioPaymentAccountsFreeze @TenantId,@BankSources,@Now;
      EXEC accounting.AccountMappingsFreeze @TenantId,@BankSources;
    END;

    DECLARE @PartyReferences TABLE(DocumentId uniqueidentifier,DocumentType nvarchar(64),PartyId uniqueidentifier,
      PRIMARY KEY(DocumentId,DocumentType,PartyId));
    INSERT @PartyReferences
    SELECT a.DocumentId,a.DocumentType,reference.PartyId FROM @Accepted a JOIN @Sources i
      ON i.DocumentId=a.DocumentId AND i.DocumentType=a.DocumentType
      CROSS APPLY accounting.ManualVoucherPartyReferences(
        CASE WHEN a.DocumentType=N'ManualAccountingVoucher' THEN i.Payload ELSE N'{}' END) reference
      WHERE a.DocumentType=N'ManualAccountingVoucher';
    IF EXISTS(SELECT 1 FROM @PartyReferences reference
      LEFT JOIN dbo.Parties party ON party.PartyId=reference.PartyId AND party.TenantId=@TenantId
      WHERE party.PartyId IS NULL)
      THROW 51731,N'The manual-voucher party is outside its acceptance scope.',1;

    INSERT accounting.SourcePartyIndex(PartyId,SourceDocumentId,SourceDocumentType)
    SELECT PartyId,DocumentId,DocumentType FROM @PartyReferences;

    INSERT accounting.SourcePositionIndex(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,EffectId,
      SubledgerKind,SubledgerId,SourceLineId,Amount,EffectKind,ReversesTransactionId)
    SELECT target.DocumentId,N'ManualAccountingVoucher',@TenantId,target.BusinessId,target.EffectId,
      target.Kind,target.PositionId,target.SourceLineId,target.Amount,target.EffectKind,target.ReversesTransactionId FROM @CreditTargets target
    JOIN @Accepted accepted ON accepted.DocumentId=target.DocumentId AND accepted.DocumentType=N'ManualAccountingVoucher';

    -- Configuration protection is derived from first acceptance, including pending
    -- jobs. It has no amounts or independent writer and is never repeated on replay.
    DECLARE @ReferencedAccounts TABLE(AccountId uniqueidentifier PRIMARY KEY);
    INSERT @ReferencedAccounts SELECT DISTINCT reference.AccountId
      FROM @Accepted accepted JOIN @Sources source
        ON source.DocumentId=accepted.DocumentId AND source.DocumentType=accepted.DocumentType
        CROSS APPLY accounting.AccountingAccountReferences(source.Payload) reference
        WHERE source.EntryRequired=1;
    UPDATE account SET ConfigurationLockedAt=@Now
    FROM @ReferencedAccounts reference JOIN dbo.AccountingAccounts account ON account.AccountId=reference.AccountId
    WHERE account.TenantId=@TenantId AND account.ConfigurationLockedAt IS NULL;

    -- Return the authoritative accepted work, including replay and inputs
    -- skipped while accounting is inactive. Callers need no second lookup.
    SELECT @QueuedCount=COUNT(*),
      @SingleSourceStatus=CASE WHEN (SELECT COUNT(*) FROM @Sources)=1 THEN MAX(job.Status) ELSE NULL END
    FROM @Sources input
    JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=input.DocumentId
      AND job.SourceDocumentType=input.DocumentType
      AND job.TenantId=@TenantId AND job.BusinessId=input.BusinessId;
    SELECT @NewSourceCount=COUNT(*) FROM @Accepted;
END;
