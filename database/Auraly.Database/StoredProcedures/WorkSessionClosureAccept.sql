CREATE PROCEDURE worksessions.ClosureAccept
    @ClosureId UNIQUEIDENTIFIER,
    @SessionId UNIQUEIDENTIFIER,
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @IdempotencyKey NVARCHAR(128),
    @TotalSales DECIMAL(19,4),
    @TotalRefunds DECIMAL(19,4),
    @TotalOther DECIMAL(19,4),
    @NetAmount DECIMAL(19,4),
    @ExpectedCash DECIMAL(19,4),
    @CountedCash DECIMAL(19,4),
    @SalesCount BIGINT,
    @CreditSalesCount INT,
    @CreditSalesAmount DECIMAL(19,4),
    @ReturnCount BIGINT,
    @Note NVARCHAR(500),
    @Snapshot NVARCHAR(MAX),
    @Hash VARBINARY(32),
    @ClosedAt DATETIMEOFFSET(7),
    @Totals NVARCHAR(MAX),
    @DispatchTransferTotal DECIMAL(19,4)=NULL,
    @DispatchCreditAdvanceTotal DECIMAL(19,4)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @@TRANCOUNT=0 THROW 51000,N'El cierre requiere la transacción de su sesión.',1;
    DECLARE @Kind NVARCHAR(16);
    SELECT @Kind=Kind FROM dbo.WorkSessions WITH(UPDLOCK,HOLDLOCK)
    WHERE WorkSessionId=@SessionId AND TenantId=@TenantId AND BusinessId=@BusinessId AND Status=N'Open';
    IF @Kind IS NULL THROW 51000,N'La sesión no está abierta en la empresa y sede autorizadas.',1;
    IF COALESCE(ISJSON(@Snapshot),0)<>1 OR COALESCE(ISJSON(@Totals),0)<>1 OR @Hash IS NULL OR DATALENGTH(@Hash)<>32
      OR @Hash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),@Snapshot COLLATE Latin1_General_100_CI_AS_SC_UTF8))
      THROW 51000,N'El cierre no conserva un snapshot y totales válidos.',1;
    IF (@Kind=N'Dispatch' AND (@DispatchTransferTotal IS NULL OR @DispatchCreditAdvanceTotal IS NULL))
      OR (@Kind=N'Normal' AND (@DispatchTransferTotal IS NOT NULL OR @DispatchCreditAdvanceTotal IS NOT NULL))
      THROW 51000,N'Los datos de cierre no corresponden al tipo de sesión.',1;

    IF @Kind=N'Dispatch' AND NOT EXISTS(SELECT 1 FROM dbo.WorkSessions WHERE WorkSessionId=@SessionId AND UserId=@UserId)
      THROW 51000,N'El cierre ciego debe confirmarlo el transportador de la sesión.',1;
    INSERT dbo.WorkSessionClosures(WorkSessionClosureId,WorkSessionId,ClosedByUserId,IdempotencyKey,
      TotalSales,TotalRefunds,TotalOther,NetAmount,ExpectedCash,CountedCash,CashDifference,
      SalesCount,CreditSalesCount,CreditSalesAmount,ReturnCount,Note,ReceiptSnapshotJson,ReceiptHash,ClosedAt,
      DispatchStatus,DispatchExpectedCash,DispatchTransferTotal,DispatchCreditDocumentTotal,
      DispatchCreditAdvanceTotal,DispatchReturnTotal)
    VALUES(@ClosureId,@SessionId,@UserId,@IdempotencyKey,@TotalSales,@TotalRefunds,@TotalOther,@NetAmount,
      @ExpectedCash,@CountedCash,@CountedCash-@ExpectedCash,@SalesCount,@CreditSalesCount,@CreditSalesAmount,
      @ReturnCount,@Note,@Snapshot,@Hash,@ClosedAt,
      CASE WHEN @Kind=N'Dispatch' THEN N'PendingReview' END,
      CASE WHEN @Kind=N'Dispatch' THEN @ExpectedCash END,
      @DispatchTransferTotal,CASE WHEN @Kind=N'Dispatch' THEN @CreditSalesAmount END,
      @DispatchCreditAdvanceTotal,CASE WHEN @Kind=N'Dispatch' THEN @TotalRefunds END);

    INSERT dbo.WorkSessionClosurePaymentTotals(WorkSessionClosureId,PaymentMethodCode,SalesAmount,
      RefundAmount,OtherAmount,NetAmount,CountedAmount,Difference)
    SELECT @ClosureId,PaymentMethodCode,SalesAmount,RefundAmount,OtherAmount,NetAmount,CountedAmount,Difference
    FROM OPENJSON(@Totals) WITH(PaymentMethodCode NVARCHAR(32) '$.paymentMethodCode',
      SalesAmount DECIMAL(19,4) '$.salesAmount',RefundAmount DECIMAL(19,4) '$.refundAmount',
      OtherAmount DECIMAL(19,4) '$.otherAmount',NetAmount DECIMAL(19,4) '$.netAmount',
      CountedAmount DECIMAL(19,4) '$.countedAmount',Difference DECIMAL(19,4) '$.difference');
    UPDATE dbo.WorkSessions SET Status=N'Closed',ClosedAt=@ClosedAt,LastActivityAt=@ClosedAt
    WHERE WorkSessionId=@SessionId AND TenantId=@TenantId AND BusinessId=@BusinessId AND Status=N'Open';
    IF @@ROWCOUNT<>1 THROW 51000,N'La sesión cambió mientras se confirmaba su cierre.',1;
END
