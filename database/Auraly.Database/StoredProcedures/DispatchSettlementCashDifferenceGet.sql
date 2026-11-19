CREATE PROCEDURE dbo.DispatchSettlementCashDifferenceGet
    @SettlementId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @DispatchId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ExpectedCash, CashReceived, ReceivedAt, Notes,
      (SELECT CashDifferencePaymentMethodId FROM dbo.DispatchSettlementOperations WHERE DispatchId=@DispatchId AND BusinessId=@BusinessId) AS CashDifferencePaymentMethodId,
      (SELECT TimeZone FROM dbo.Businesses WHERE BusinessId=@BusinessId) AS TimeZone
    FROM dispatch.Closures WITH (UPDLOCK, HOLDLOCK)
    WHERE DispatchSettlementId = @SettlementId
      AND BusinessId = @BusinessId
      AND DispatchId = @DispatchId
      AND Status = N'Processing';

    SELECT payment.PaymentId,source.SourceDocumentId,payment.PaymentMethodId,payment.MethodCode,payment.Amount
    FROM dispatch.EffectivePayments(@DispatchId) payment
    JOIN dbo.DispatchSourceDocuments source ON source.DispatchSourceDocumentId=payment.DispatchSourceDocumentId
      AND source.DispatchId=@DispatchId
    JOIN dbo.SalesDocuments sale ON sale.DocumentId=source.SourceDocumentId AND sale.BusinessId=@BusinessId
      AND sale.CollectionMode=N'Dispatch'
    WHERE payment.ApplicationType=N'InvoicePayment' AND payment.PaymentMethodId IS NOT NULL
    ORDER BY source.SourceDocumentId,payment.PaymentId;
END
