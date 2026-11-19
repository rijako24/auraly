CREATE FUNCTION dispatch.SettlementDocuments(@DispatchId UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER)
RETURNS TABLE
AS RETURN
(
    SELECT returned.ReturnId DocumentId,CONVERT(NVARCHAR(64),N'SalesReturn') DocumentType
    FROM dbo.SalesReturns returned
    WHERE returned.BusinessId=@BusinessId AND returned.IdempotencyKey LIKE
      CONCAT(N'dispatch-settlement:',LOWER(REPLACE(CONVERT(NVARCHAR(36),@DispatchId),N'-',N'')),N':%')
    UNION ALL
    SELECT payment.PaymentId,CONVERT(NVARCHAR(64),N'ReceivablePayment')
    FROM dbo.CustomerPayments payment
    WHERE payment.BusinessId=@BusinessId AND payment.IdempotencyKey LIKE
      CONCAT(N'dispatch-settlement:',LOWER(REPLACE(CONVERT(NVARCHAR(36),@DispatchId),N'-',N'')),N':%')
    UNION ALL
    SELECT returned.ReturnId,CONVERT(NVARCHAR(64),N'SalesReturn')
    FROM dbo.DispatchSettlementOperations operation
    CROSS APPLY OPENJSON(operation.ReturnIdsJson) WITH(ReturnId uniqueidentifier '$') selected
    JOIN dbo.SalesReturns returned ON returned.ReturnId=selected.ReturnId AND returned.BusinessId=operation.BusinessId
    WHERE operation.DispatchId=@DispatchId AND operation.BusinessId=@BusinessId
);
