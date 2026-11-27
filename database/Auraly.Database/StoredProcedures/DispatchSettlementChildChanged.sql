CREATE PROCEDURE dbo.DispatchSettlementChildChanged
    @BusinessId UNIQUEIDENTIFIER,
    @DocumentId UNIQUEIDENTIFIER,
    @DocumentType NVARCHAR(64),
    @ReadyOperationId UNIQUEIDENTIFIER = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Key nvarchar(160);
    IF @DocumentType=N'SalesReturn'
      SELECT @Key=IdempotencyKey FROM dbo.SalesReturns WHERE ReturnId=@DocumentId AND BusinessId=@BusinessId;
    ELSE IF @DocumentType=N'ReceivablePayment'
      SELECT @Key=IdempotencyKey FROM dbo.CustomerPayments WHERE PaymentId=@DocumentId AND BusinessId=@BusinessId;
    IF @Key IS NULL OR @Key NOT LIKE N'dispatch-settlement:%' RETURN;
    DECLARE @DispatchText nvarchar(36)=SUBSTRING(@Key,21,32);
    SET @DispatchText=STUFF(STUFF(STUFF(STUFF(@DispatchText,21,0,N'-'),17,0,N'-'),13,0,N'-'),9,0,N'-');
    DECLARE @DispatchId uniqueidentifier=TRY_CONVERT(uniqueidentifier,@DispatchText);
    IF @DispatchId IS NULL RETURN;

    DECLARE @Changed TABLE(OperationId UNIQUEIDENTIFIER, Status NVARCHAR(32));
    UPDATE operation
    SET ActivationAvailableAt=CASE WHEN ready.PendingCount=0 AND ready.AttentionCount=0 THEN SYSUTCDATETIME() END,
        Status=CASE WHEN ready.AttentionCount>0 THEN N'NeedsAttention' ELSE N'Processing' END,
        LastError=CASE WHEN ready.AttentionCount>0 THEN N'Un documento de la conciliación requiere atención contable o de inventario.' END
    OUTPUT inserted.DispatchSettlementOperationId,inserted.Status INTO @Changed
    FROM dbo.DispatchSettlementOperations operation
    CROSS APPLY dispatch.SettlementPrerequisites(operation.BusinessId,operation.PrerequisitesJson) ready
    WHERE operation.DispatchId=@DispatchId AND operation.BusinessId=@BusinessId
      AND operation.PreparationPhase IN(1,2) AND operation.PrerequisitesJson IS NOT NULL
      AND operation.PreparationLeaseId IS NULL AND operation.ActivationAvailableAt IS NULL
      AND operation.Status IN(N'Processing',N'NeedsAttention')
      AND (ready.PendingCount=0 OR ready.AttentionCount>0);
    SELECT @ReadyOperationId=OperationId FROM @Changed WHERE Status=N'Processing';
END
