CREATE PROCEDURE accounting.PortfolioPaymentAccept
    @TenantId uniqueidentifier,
    @BusinessId uniqueidentifier,
    @UserId uniqueidentifier,
    @SubledgerKind nvarchar(16),
    @Payload nvarchar(max),
    @IdempotencyKey nvarchar(160),
    @PayloadHash binary(32),
    @AcceptedAt datetimeoffset(7)
AS
BEGIN
    SET NOCOUNT ON;
    -- One participant for native payments and the groups of a manual source.
    -- The caller owns the transaction; this procedure creates no job or journal.
    IF @@TRANCOUNT=0 THROW 51400,'Payment acceptance requires a transaction.',1;
    IF @SubledgerKind IS NULL OR @SubledgerKind NOT IN(N'Payable',N'Receivable') OR ISNULL(ISJSON(@Payload),0)<>1
      THROW 51400,'The payment acceptance contract is invalid.',1;
    DECLARE @Grouped bit=CASE WHEN EXISTS(SELECT 1 FROM OPENJSON(@Payload) WHERE [key]=N'groups') THEN 1 ELSE 0 END;
    DECLARE @Groups nvarchar(max)=CASE WHEN @Grouped=1 THEN JSON_QUERY(@Payload,'$.groups') ELSE CONCAT(N'[',@Payload,N']') END;
    IF @Groups IS NULL OR LEFT(LTRIM(@Groups),1)<>N'[' OR (SELECT COUNT(*) FROM OPENJSON(@Groups)) NOT BETWEEN 1 AND 250
      THROW 51400,'The payment group batch is invalid.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
      THROW 51401,'The business is outside the authenticated tenant.',1;

    DECLARE @Headers TABLE(PaymentId uniqueidentifier PRIMARY KEY,RoleId uniqueidentifier,PartyId uniqueidentifier,PartySiteId uniqueidentifier,
      TenantId uniqueidentifier,BusinessId uniqueidentifier,ConfirmedBy uniqueidentifier,SessionId uniqueidentifier,
      Number nvarchar(40),SeriesId uniqueidentifier,Prefix nvarchar(8),SeriesCode nvarchar(16),Consecutive bigint,
      PaidAt datetimeoffset(7),Currency char(3),Notes nvarchar(1000),Total decimal(19,4),
      AdvancePositionId uniqueidentifier,AdvanceAmount decimal(19,4),
      ParentId uniqueidentifier,ParentType nvarchar(64),GroupId uniqueidentifier,
      Allocations nvarchar(max),Tenders nvarchar(max));
    INSERT @Headers
    SELECT paymentId,CASE WHEN @SubledgerKind=N'Payable' THEN supplierId ELSE customerId END,partyId,partySiteId,
      tenantId,businessId,confirmedByUserId,workSessionId,documentNumber,documentSeriesId,documentPrefix,
      documentSeriesCode,documentConsecutive,paidAt,currencyCode,notes,totalAmount,advancePositionId,COALESCE(advanceAmount,0),
      parentSourceDocumentId,parentSourceDocumentType,sourceGroupId,allocations,payments
    FROM OPENJSON(@Groups) WITH(paymentId uniqueidentifier,supplierId uniqueidentifier,customerId uniqueidentifier,
      partyId uniqueidentifier,partySiteId uniqueidentifier,tenantId uniqueidentifier,businessId uniqueidentifier,confirmedByUserId uniqueidentifier,
      workSessionId uniqueidentifier,documentNumber nvarchar(40),documentSeriesId uniqueidentifier,
      documentPrefix nvarchar(8),documentSeriesCode nvarchar(16),documentConsecutive bigint,
      paidAt datetimeoffset(7),currencyCode char(3),notes nvarchar(1000),totalAmount decimal(19,4),
      advancePositionId uniqueidentifier,advanceAmount decimal(19,4),
      parentSourceDocumentId uniqueidentifier,parentSourceDocumentType nvarchar(64),sourceGroupId uniqueidentifier,
      allocations nvarchar(max) AS JSON,payments nvarchar(max) AS JSON);
    IF EXISTS(SELECT 1 FROM @Headers WHERE TenantId IS NULL OR TenantId<>@TenantId OR BusinessId IS NULL OR BusinessId<>@BusinessId
        OR ConfirmedBy IS NULL OR ConfirmedBy<>@UserId)
      THROW 51401,'The payment does not match its authenticated scope.',1;
    IF EXISTS(SELECT 1 FROM @Headers WHERE Currency IS NULL OR Currency<>N'COP' OR Total IS NULL OR Total<=0 OR PaidAt IS NULL)
      THROW 51400,'The payment amount, currency or date is invalid.',1;
    IF @Grouped=0 AND EXISTS(SELECT 1 FROM @Headers WHERE ParentId IS NOT NULL OR ParentType IS NOT NULL OR GroupId IS NOT NULL
        OR Number IS NULL OR SeriesId IS NULL OR Prefix IS NULL OR SeriesCode IS NULL OR Consecutive IS NULL
        OR AdvancePositionId IS NOT NULL OR AdvanceAmount<>0 OR PartySiteId IS NOT NULL)
      THROW 51400,'A native payment requires its own document number.',1;
    IF @Grouped=1
    BEGIN
      IF (SELECT COUNT(DISTINCT ParentId) FROM @Headers)<>1 OR (SELECT COUNT(DISTINCT ParentType) FROM @Headers)<>1
        OR EXISTS(SELECT 1 FROM @Headers h WHERE h.ParentId IS NULL OR h.ParentType IS NULL OR h.GroupId IS NULL
          OR h.GroupId='00000000-0000-0000-0000-000000000000' OR h.Number IS NOT NULL OR h.SeriesId IS NOT NULL
          OR h.Prefix IS NOT NULL OR h.SeriesCode IS NOT NULL OR h.Consecutive IS NOT NULL)
        THROW 51401,'The payment groups require their accepted financial source, without child numbering.',1;
      -- All headers have proved one parent key. Parse its immutable JSON once,
      -- rather than extracting the complete effects object for every group.
      DECLARE @ParentId uniqueidentifier,@ParentType nvarchar(64),@ParentAt datetimeoffset(7),@ParentPayload nvarchar(max);
      SELECT TOP(1) @ParentId=ParentId,@ParentType=ParentType FROM @Headers;
      SELECT @ParentAt=source.OccurredAt,@ParentPayload=source.PayloadJson FROM dbo.AccountingSourceDocuments source
        WHERE source.SourceDocumentId=@ParentId AND source.SourceDocumentType=@ParentType
          AND source.TenantId=@TenantId AND source.BusinessId=@BusinessId AND source.PayloadHash=@PayloadHash
          AND JSON_VALUE(source.PayloadJson,'$.PayloadVersion')=N'4'
          AND JSON_QUERY(source.PayloadJson,'$.FinancialEffects') IS NOT NULL;
      IF @ParentAt IS NULL OR EXISTS(SELECT 1 FROM @Headers WHERE PaidAt<>@ParentAt)
        THROW 51401,'The payment groups require their accepted financial source, without child numbering.',1;
      DECLARE @FrozenGroups TABLE(GroupId uniqueidentifier PRIMARY KEY,PaymentId uniqueidentifier,
        Kind nvarchar(16),PartyId uniqueidentifier,RoleId uniqueidentifier,PartySiteId uniqueidentifier,
        AdvancePositionId uniqueidentifier,AdvanceAmount decimal(19,4),Allocations nvarchar(max),Tenders nvarchar(max));
      INSERT @FrozenGroups SELECT GroupId,PaymentId,SubledgerKind,PartyId,CounterpartyRoleId,PartySiteId,AdvancePositionId,COALESCE(AdvanceAmount,0),Allocations,Tenders
        FROM OPENJSON(@ParentPayload,'$.FinancialEffects.Payments') WITH(GroupId uniqueidentifier,PaymentId uniqueidentifier,
          SubledgerKind nvarchar(16),PartyId uniqueidentifier,CounterpartyRoleId uniqueidentifier,PartySiteId uniqueidentifier,
          AdvancePositionId uniqueidentifier,AdvanceAmount decimal(19,4),Allocations nvarchar(max) AS JSON,Tenders nvarchar(max) AS JSON);
      IF EXISTS(SELECT 1 FROM @Headers h LEFT JOIN @FrozenGroups frozen ON frozen.GroupId=h.GroupId
          AND frozen.PaymentId=h.PaymentId AND frozen.Kind=@SubledgerKind
        WHERE frozen.GroupId IS NULL OR frozen.PartyId IS NULL OR h.PartyId IS NULL OR h.PartyId<>frozen.PartyId
          OR EXISTS(SELECT h.RoleId EXCEPT SELECT frozen.RoleId)
          OR EXISTS(SELECT h.PartySiteId EXCEPT SELECT frozen.PartySiteId)
          OR h.AdvanceAmount<>frozen.AdvanceAmount
          OR h.AdvanceAmount<0 OR h.AdvanceAmount>h.Total
          OR (h.AdvanceAmount=0 AND h.AdvancePositionId IS NOT NULL)
          OR (h.AdvanceAmount>0 AND (h.AdvancePositionId IS NULL OR frozen.AdvancePositionId IS NULL
            OR h.AdvancePositionId<>frozen.AdvancePositionId)))
        THROW 51401,N'El grupo de pago no coincide con el comprobante padre.',1;
      IF EXISTS(SELECT SessionId FROM @Headers EXCEPT
          SELECT TRY_CONVERT(uniqueidentifier,JSON_VALUE(@ParentPayload,'$.WorkSessionId')))
        THROW 51401,N'La sesión de pago no coincide con el comprobante padre.',1;
    END;
    IF EXISTS(SELECT 1 FROM @Headers h WHERE
        (@SubledgerKind=N'Payable' AND NOT EXISTS(SELECT 1 FROM dbo.Suppliers
          WHERE SupplierId=h.RoleId AND TenantId=@TenantId AND IsActive=1 AND (h.PartyId IS NULL OR PartyId=h.PartyId)))
        OR (@SubledgerKind=N'Receivable' AND h.RoleId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Customers
          WHERE CustomerId=h.RoleId AND TenantId=@TenantId AND (h.PartyId IS NULL OR PartyId=h.PartyId)))
        OR (@SubledgerKind=N'Receivable' AND h.RoleId IS NULL AND (@Grouped=0 OR h.PartyId IS NULL OR NOT EXISTS(
          SELECT 1 FROM dbo.Parties WHERE PartyId=h.PartyId AND TenantId=@TenantId AND IsActive=1))))
      THROW 51401,'The selected party is not available in this tenant.',1;
    IF EXISTS(SELECT 1 FROM @Headers h WHERE h.PartySiteId IS NOT NULL AND NOT EXISTS(
        SELECT 1 FROM dbo.PartySites site JOIN dbo.Parties party ON party.PartyId=site.PartyId AND party.TenantId=@TenantId
        WHERE site.PartySiteId=h.PartySiteId AND site.PartyId=h.PartyId AND site.IsActive=1))
      THROW 51401,N'La sede de pago no corresponde al tercero del comprobante.',1;
    IF EXISTS(SELECT 1 FROM @Headers h WHERE h.SessionId IS NOT NULL AND NOT EXISTS(
        SELECT 1 FROM worksessions.OpenSessionForUser(@TenantId,@BusinessId,@UserId,h.SessionId)))
      THROW 51401,'The work session is not open for the authenticated user.',1;

    DECLARE @Input TABLE(PaymentId uniqueidentifier,LineNumber int,PositionId uniqueidentifier,Amount decimal(19,4),
      SourceLineId uniqueidentifier,FinancialDiscountEarned bit,FinancialDiscountAdjustment decimal(19,4),
      PRIMARY KEY(PaymentId,LineNumber),UNIQUE(PaymentId,PositionId));
    INSERT @Input
    SELECT h.PaymentId,input.lineNumber,CASE WHEN @SubledgerKind=N'Payable' THEN input.payableId ELSE input.receivableId END,
      input.amount,input.sourceLineId,input.financialDiscountEarned,NULL FROM @Headers h CROSS APPLY OPENJSON(h.Allocations)
      WITH(lineNumber int,payableId uniqueidentifier,receivableId uniqueidentifier,amount decimal(19,4),sourceLineId uniqueidentifier,
        financialDiscountEarned bit) input;
    IF EXISTS(SELECT 1 FROM @Headers h OUTER APPLY(SELECT COUNT(*) Count,SUM(Amount) Total FROM @Input WHERE PaymentId=h.PaymentId) lines
        WHERE lines.Count NOT BETWEEN CASE WHEN h.AdvanceAmount>0 THEN 0 ELSE 1 END AND 100
          OR COALESCE(lines.Total,0)+h.AdvanceAmount<>h.Total)
      OR EXISTS(SELECT 1 FROM @Input WHERE Amount IS NULL OR Amount<=0 OR LineNumber<1 OR PositionId IS NULL)
      OR (@Grouped=1 AND ((SELECT COUNT(*) FROM @Input)>500 OR EXISTS(SELECT 1 FROM @Input WHERE SourceLineId IS NULL)))
      THROW 51400,'The payment allocation batch is invalid.',1;
    IF @Grouped=1
    BEGIN
      -- Prove the transient adapter against the immutable source before it can
      -- reserve a different position/amount. Parse each frozen array once.
      DECLARE @ExpectedInput TABLE(PaymentId uniqueidentifier,LineNumber int,PositionId uniqueidentifier,
        Amount decimal(19,4),SourceLineId uniqueidentifier,FinancialDiscountEarned bit,
        FinancialDiscountAdjustment decimal(19,4),PRIMARY KEY(PaymentId,LineNumber));
      INSERT @ExpectedInput SELECT frozen.PaymentId,CONVERT(int,item.[key])+1,value.SubledgerId,value.Amount,value.SourceLineId,
        value.FinancialDiscountEarned,COALESCE(value.FinancialDiscountAdjustment,0)
      FROM @FrozenGroups frozen JOIN @Headers header ON header.PaymentId=frozen.PaymentId AND header.GroupId=frozen.GroupId
      CROSS APPLY OPENJSON(frozen.Allocations) item
      CROSS APPLY OPENJSON(item.value) WITH(SubledgerId uniqueidentifier,Amount decimal(19,4),SourceLineId uniqueidentifier,
        FinancialDiscountEarned bit,FinancialDiscountAdjustment decimal(19,4)) value;
      IF EXISTS(SELECT PaymentId,LineNumber,PositionId,Amount,SourceLineId,FinancialDiscountEarned FROM @Input EXCEPT
                SELECT PaymentId,LineNumber,PositionId,Amount,SourceLineId,FinancialDiscountEarned FROM @ExpectedInput)
        OR EXISTS(SELECT PaymentId,LineNumber,PositionId,Amount,SourceLineId,FinancialDiscountEarned FROM @ExpectedInput EXCEPT
                  SELECT PaymentId,LineNumber,PositionId,Amount,SourceLineId,FinancialDiscountEarned FROM @Input)
        THROW 51401,N'Las aplicaciones de pago no coinciden con el comprobante padre.',1;
    END;
    DECLARE @Keys TABLE(PositionId uniqueidentifier PRIMARY KEY);
    INSERT @Keys SELECT DISTINCT PositionId FROM @Input;
    DECLARE @Positions TABLE(PositionId uniqueidentifier PRIMARY KEY,RoleId uniqueidentifier,PartyId uniqueidentifier,
      PartySiteId uniqueidentifier,CurrencyCode char(3),Status nvarchar(24),DocumentNumber nvarchar(80),
      OutstandingAmount decimal(19,4),SourceDocumentId uniqueidentifier,SourceDocumentType nvarchar(64));
    IF @SubledgerKind=N'Payable'
      INSERT @Positions SELECT p.PayableId,p.SupplierId,s.PartyId,p.PartySiteId,p.CurrencyCode,p.Status,p.DocumentNumber,
        p.OutstandingAmount,p.SourceDocumentId,p.SourceDocumentType
      FROM @Keys input JOIN dbo.Payables p WITH(UPDLOCK,HOLDLOCK) ON p.PayableId=input.PositionId AND p.BusinessId=@BusinessId
      JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId AND s.TenantId=@TenantId
      ORDER BY input.PositionId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
    ELSE
      INSERT @Positions SELECT p.ReceivableId,p.CustomerId,COALESCE(p.PartyId,c.PartyId),p.PartySiteId,p.CurrencyCode,p.Status,p.DocumentNumber,
        p.OutstandingAmount,p.SourceDocumentId,p.SourceDocumentType
      FROM @Keys input JOIN dbo.Receivables p WITH(UPDLOCK,HOLDLOCK) ON p.ReceivableId=input.PositionId AND p.BusinessId=@BusinessId
      LEFT JOIN dbo.Customers c ON c.CustomerId=p.CustomerId AND c.TenantId=@TenantId
      ORDER BY input.PositionId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
    IF @SubledgerKind=N'Receivable' AND EXISTS(SELECT 1 FROM @Input WHERE FinancialDiscountEarned IS NOT NULL)
      THROW 51400,N'El descuento financiero de compra solo corresponde a cuentas por pagar.',1;
    IF @SubledgerKind=N'Payable' AND EXISTS(SELECT 1 FROM @Input WHERE FinancialDiscountEarned IS NOT NULL)
    BEGIN
      IF EXISTS(SELECT 1 FROM @Input WHERE FinancialDiscountEarned IS NOT NULL
        GROUP BY PositionId HAVING COUNT(*)>1)
        THROW 51402,N'El pago no se realizó. El descuento financiero se decide una sola vez por obligación.',1;
      IF EXISTS(SELECT 1 FROM @Input input JOIN @Positions position ON position.PositionId=input.PositionId
        JOIN dbo.PurchaseReturns purchaseReturn ON purchaseReturn.OriginalGoodsReceiptId=position.SourceDocumentId
          AND purchaseReturn.BusinessId=@BusinessId AND purchaseReturn.Status=N'Accepted'
        WHERE input.FinancialDiscountEarned IS NOT NULL)
        THROW 51402,N'El pago no se realizó. Termina primero la devolución de compra pendiente para calcular el descuento.',1;
      DECLARE @ReceiptDiscounts TABLE(PositionId uniqueidentifier PRIMARY KEY,Anticipated bit NOT NULL,
        EligibleAmount decimal(19,4) NOT NULL);
      INSERT @ReceiptDiscounts
      SELECT position.PositionId,discount.Anticipated,discount.EligibleAmount
      FROM @Positions position
      JOIN @Input input ON input.PositionId=position.PositionId AND input.FinancialDiscountEarned IS NOT NULL
      CROSS APPLY accounting.GoodsReceiptFinancialDiscount(@BusinessId,position.SourceDocumentId) discount
      WHERE position.SourceDocumentType=N'GoodsReceipt';
      IF EXISTS(SELECT 1 FROM @Input input LEFT JOIN @ReceiptDiscounts discount ON discount.PositionId=input.PositionId
        WHERE input.FinancialDiscountEarned IS NOT NULL AND
          (discount.PositionId IS NULL OR discount.EligibleAmount<=0 OR EXISTS(
            SELECT 1 FROM dbo.SupplierPaymentApplications prior WITH(UPDLOCK,HOLDLOCK)
            WHERE prior.PayableId=input.PositionId AND prior.FinancialDiscountEarned IS NOT NULL)))
        THROW 51402,N'El pago no se realizó. El descuento financiero de esta compra no está disponible o ya fue decidido.',1;
      UPDATE input SET FinancialDiscountAdjustment=
        CASE WHEN input.FinancialDiscountEarned=1 THEN discount.EligibleAmount ELSE 0 END
        -CASE WHEN discount.Anticipated=1 THEN discount.EligibleAmount ELSE 0 END
      FROM @Input input JOIN @ReceiptDiscounts discount ON discount.PositionId=input.PositionId
      WHERE input.FinancialDiscountEarned IS NOT NULL;
    END;
    IF @Grouped=1 AND (EXISTS(SELECT PaymentId,LineNumber,COALESCE(FinancialDiscountAdjustment,0) FROM @Input EXCEPT
        SELECT PaymentId,LineNumber,FinancialDiscountAdjustment FROM @ExpectedInput)
      OR EXISTS(SELECT PaymentId,LineNumber,FinancialDiscountAdjustment FROM @ExpectedInput EXCEPT
        SELECT PaymentId,LineNumber,COALESCE(FinancialDiscountAdjustment,0) FROM @Input))
      THROW 51401,N'La decisión del descuento financiero no coincide con el comprobante padre.',1;
    DECLARE @Error nvarchar(2048);
    SELECT TOP(1) @Error=validation.Reason
    FROM @Input input JOIN @Headers h ON h.PaymentId=input.PaymentId LEFT JOIN @Positions p ON p.PositionId=input.PositionId
    CROSS APPLY(SELECT CASE
      WHEN p.PositionId IS NULL OR (h.RoleId IS NOT NULL AND (p.RoleId IS NULL OR p.RoleId<>h.RoleId))
        OR (h.RoleId IS NULL AND (p.RoleId IS NOT NULL OR p.PartyId IS NULL OR p.PartyId<>h.PartyId)) THEN
        CASE WHEN @SubledgerKind=N'Payable' THEN N'Una obligación no corresponde al proveedor seleccionado.'
          ELSE N'Una factura no corresponde al cliente seleccionado.' END
      WHEN p.CurrencyCode<>h.Currency THEN N'La moneda del abono no coincide con la obligación.'
      WHEN p.Status=N'Cancelled' THEN CONCAT(N'El documento ',p.DocumentNumber,N' está cancelado.')
      WHEN p.Status=N'Paid' THEN CONCAT(N'El documento ',p.DocumentNumber,N' ya está pagado.')
      WHEN @SubledgerKind=N'Payable' AND p.SourceDocumentType=N'Expense' AND NOT EXISTS(
        SELECT 1 FROM dbo.Expenses e WITH(UPDLOCK,HOLDLOCK) WHERE e.ExpenseId=p.SourceDocumentId AND e.BusinessId=@BusinessId AND e.Status=N'Processed')
        THEN CONCAT(N'El gasto ',p.DocumentNumber,N' aún no terminó de procesarse.') END Reason) validation
    WHERE validation.Reason IS NOT NULL ORDER BY input.PositionId;
    -- Several groups may target the same position. Validate their combined
    -- reservation once, before inserting any accepted application of this source.
    IF @Error IS NULL
    SELECT TOP(1) @Error=CASE WHEN requested.Amount<=0
      THEN CONCAT(N'El abono neto a ',p.DocumentNumber,
        N' debe ser positivo después de la decisión del descuento financiero.')
      ELSE CONCAT(N'El abono a ',p.DocumentNumber,N' supera el saldo disponible: solicitado ',requested.Amount,
        N', disponible ',pending.AvailableAmount,N'.') END
    FROM @Positions p JOIN(SELECT PositionId,SUM(Amount+COALESCE(FinancialDiscountAdjustment,0)) Amount
      FROM @Input GROUP BY PositionId) requested ON requested.PositionId=p.PositionId
    CROSS APPLY accounting.ObligationAvailability(@BusinessId,@SubledgerKind,p.PositionId,p.OutstandingAmount,NULL) pending
    WHERE requested.Amount>pending.AvailableAmount OR requested.Amount<=0 ORDER BY p.PositionId;
    IF @Error IS NOT NULL
    BEGIN
      SET @Error=CONCAT(N'El pago no se realizó. ',@Error);
      THROW 51402,@Error,1;
    END;
    IF @SubledgerKind=N'Payable' AND EXISTS(SELECT input.PaymentId FROM @Input input JOIN @Positions p ON p.PositionId=input.PositionId
        GROUP BY input.PaymentId HAVING COUNT(DISTINCT p.PartySiteId)<>1 OR COUNT(*)<>COUNT(p.PartySiteId))
      THROW 51401,N'Selecciona obligaciones de una sola sede del proveedor.',1;

    DECLARE @Methods TABLE(PaymentId uniqueidentifier,LineNumber int,MethodCode nvarchar(32),Amount decimal(19,4),
      TenderedAmount decimal(19,4),BankAccountId uniqueidentifier,Reference nvarchar(160),Notes nvarchar(500),
      CardFranchiseCode nvarchar(64),ApprovalNumber nvarchar(100),AccountId uniqueidentifier,SourceLineId uniqueidentifier,
      PRIMARY KEY(PaymentId,LineNumber));
    INSERT @Methods SELECT h.PaymentId,method.* FROM @Headers h CROSS APPLY OPENJSON(h.Tenders)
      WITH(lineNumber int,methodCode nvarchar(32),amount decimal(19,4),tenderedAmount decimal(19,4),bankAccountId uniqueidentifier,
        reference nvarchar(160),notes nvarchar(500),cardFranchiseCode nvarchar(64),approvalNumber nvarchar(100),
        accountId uniqueidentifier,sourceLineId uniqueidentifier) method;
    IF EXISTS(SELECT 1 FROM @Headers h OUTER APPLY(SELECT COUNT(*) Count,SUM(Amount) Total FROM @Methods WHERE PaymentId=h.PaymentId) methods
        WHERE methods.Count NOT BETWEEN 1 AND 10 OR methods.Total<>h.Total)
      OR EXISTS(SELECT 1 FROM @Methods WHERE Amount IS NULL OR Amount<=0 OR LineNumber<1)
      OR (@Grouped=1 AND EXISTS(SELECT 1 FROM @Methods WHERE AccountId IS NULL OR SourceLineId IS NULL))
      OR (@Grouped=0 AND EXISTS(SELECT 1 FROM @Methods WHERE AccountId IS NOT NULL OR SourceLineId IS NOT NULL))
      THROW 51400,'The payment methods do not match the accepted amount.',1;
    IF @Grouped=1
    BEGIN
      DECLARE @ExpectedMethods TABLE(PaymentId uniqueidentifier,LineNumber int,MethodCode nvarchar(32),Amount decimal(19,4),
        TenderedAmount decimal(19,4),BankAccountId uniqueidentifier,Reference nvarchar(160),Notes nvarchar(500),
        CardFranchiseCode nvarchar(64),ApprovalNumber nvarchar(100),AccountId uniqueidentifier,SourceLineId uniqueidentifier,
        PRIMARY KEY(PaymentId,LineNumber));
      INSERT @ExpectedMethods SELECT frozen.PaymentId,CONVERT(int,item.[key])+1,value.*
      FROM @FrozenGroups frozen JOIN @Headers header ON header.PaymentId=frozen.PaymentId AND header.GroupId=frozen.GroupId
      CROSS APPLY OPENJSON(frozen.Tenders) item
      CROSS APPLY OPENJSON(item.value) WITH(MethodCode nvarchar(32),Amount decimal(19,4),TenderedAmount decimal(19,4),
        BankAccountId uniqueidentifier,Reference nvarchar(160),Notes nvarchar(500),CardFranchiseCode nvarchar(64),
        ApprovalNumber nvarchar(100),AccountId uniqueidentifier,SourceLineId uniqueidentifier) value;
      IF EXISTS(SELECT * FROM @Methods EXCEPT SELECT * FROM @ExpectedMethods)
        OR EXISTS(SELECT * FROM @ExpectedMethods EXCEPT SELECT * FROM @Methods)
        THROW 51401,N'La evidencia de pago no coincide con el comprobante padre.',1;
    END;
    IF EXISTS(SELECT 1 FROM @Methods input JOIN @Headers h ON h.PaymentId=input.PaymentId
        WHERE input.MethodCode=N'Transfer' AND input.BankAccountId IS NULL AND EXISTS(
          SELECT 1 FROM dbo.AccountingTenantSettings WHERE TenantId=@TenantId AND Status=N'Ready' AND EffectiveFrom<=CONVERT(date,h.PaidAt)))
      THROW 51401,'Select a bank account for every transfer.',1;
    IF EXISTS(SELECT 1 FROM @Methods input WHERE input.BankAccountId IS NOT NULL AND NOT EXISTS(
        SELECT 1 FROM accounting.BankAccounts bank WHERE bank.BankAccountId=input.BankAccountId AND bank.TenantId=@TenantId AND bank.IsActive=1))
      THROW 51401,'A selected bank account is not active for this tenant.',1;
    IF EXISTS(SELECT 1 FROM @Methods input WHERE input.AccountId IS NOT NULL AND NOT EXISTS(
        SELECT 1 FROM dbo.AccountingAccounts account WHERE account.AccountId=input.AccountId AND account.TenantId=@TenantId
          AND account.IsActive=1 AND account.AllowsPosting=1 AND (account.BusinessId IS NULL OR account.BusinessId=@BusinessId)))
      THROW 51401,'A selected treasury auxiliary is not active for this business.',1;

    -- Stable source identity marks the new native contract. Historical tenders
    -- without it keep their original contract; replay never backfills identities.
    IF @Grouped=0 UPDATE @Methods SET SourceLineId=NEWID();

    IF @SubledgerKind=N'Payable'
    BEGIN
      INSERT dbo.SupplierPayments(PaymentId,BusinessId,SupplierId,WorkSessionId,DocumentSeriesId,DocumentNumber,
        DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,IdempotencyKey,PayloadHash,PaidAt,CurrencyCode,
        Notes,TotalAmount,Status,ConfirmedByUserId,AcceptedAt,ParentSourceDocumentId,ParentSourceDocumentType,SourceGroupId,PartySiteId)
      SELECT PaymentId,@BusinessId,RoleId,SessionId,SeriesId,Number,Prefix,SeriesCode,Consecutive,
        CASE WHEN @Grouped=1 THEN CONCAT(N'manual:',CONVERT(nvarchar(36),ParentId),N':',CONVERT(nvarchar(36),GroupId)) ELSE @IdempotencyKey END,
        @PayloadHash,PaidAt,Currency,Notes,Total,N'Accepted',@UserId,@AcceptedAt,ParentId,ParentType,GroupId,PartySiteId FROM @Headers;
      INSERT dbo.SupplierPaymentTenders(PaymentId,LineNumber,MethodCode,Amount,TenderedAmount,BankAccountId,
        Reference,Notes,CardFranchiseCode,ApprovalNumber,AccountId,SourceLineId)
      SELECT PaymentId,LineNumber,MethodCode,Amount,TenderedAmount,BankAccountId,Reference,Notes,CardFranchiseCode,ApprovalNumber,AccountId,SourceLineId FROM @Methods;
      INSERT dbo.SupplierPaymentApplications(PaymentId,LineNumber,PayableId,Amount,
        FinancialDiscountEarned,FinancialDiscountAdjustment)
      SELECT PaymentId,LineNumber,PositionId,Amount,FinancialDiscountEarned,FinancialDiscountAdjustment FROM @Input;
    END
    ELSE
    BEGIN
      INSERT dbo.CustomerPayments(PaymentId,BusinessId,CustomerId,PartyId,WorkSessionId,DocumentSeriesId,DocumentNumber,
        DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,IdempotencyKey,PayloadHash,PaidAt,CurrencyCode,
        Notes,TotalAmount,Status,ConfirmedByUserId,AcceptedAt,ParentSourceDocumentId,ParentSourceDocumentType,SourceGroupId,PartySiteId)
      SELECT PaymentId,@BusinessId,RoleId,CASE WHEN RoleId IS NULL THEN PartyId END,SessionId,SeriesId,Number,Prefix,SeriesCode,Consecutive,
        CASE WHEN @Grouped=1 THEN CONCAT(N'manual:',CONVERT(nvarchar(36),ParentId),N':',CONVERT(nvarchar(36),GroupId)) ELSE @IdempotencyKey END,
        @PayloadHash,PaidAt,Currency,Notes,Total,N'Accepted',@UserId,@AcceptedAt,ParentId,ParentType,GroupId,PartySiteId FROM @Headers;
      INSERT dbo.CustomerPaymentTenders(PaymentId,LineNumber,MethodCode,Amount,TenderedAmount,BankAccountId,
        Reference,Notes,CardFranchiseCode,ApprovalNumber,AccountId,SourceLineId)
      SELECT PaymentId,LineNumber,MethodCode,Amount,TenderedAmount,BankAccountId,Reference,Notes,CardFranchiseCode,ApprovalNumber,AccountId,SourceLineId FROM @Methods;
      INSERT dbo.CustomerPaymentApplications(PaymentId,LineNumber,ReceivableId,Amount)
      SELECT PaymentId,LineNumber,PositionId,Amount FROM @Input;
    END
END;
GO
