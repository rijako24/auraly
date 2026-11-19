CREATE PROCEDURE dbo.DispatchSettlementPaymentsGet
    @BusinessId UNIQUEIDENTIFIER,
    @DispatchId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    -- Paid obligations remain visible so retry returns the same accepted payment.
    SELECT source.SourceDocumentId,sale.CustomerId,receivable.ReceivableId,
           payment.MethodCode,payment.Amount,payment.Reference,payment.BankAccountId,
           payment.PaymentMethodId,payment.PaymentMethodVersion,
           payment.CardFranchiseCode,payment.ApprovalNumber,
           payment.OriginalMethod PaymentKeyMethod
    FROM dispatch.EffectivePayments(@DispatchId) payment
    INNER JOIN dbo.DispatchSourceDocuments source
        ON source.DispatchSourceDocumentId = payment.DispatchSourceDocumentId
    INNER JOIN dbo.SalesDocuments sale ON sale.DocumentId = source.SourceDocumentId AND sale.BusinessId=@BusinessId
    INNER JOIN dbo.Receivables receivable
        ON receivable.SourceDocumentId = source.SourceDocumentId
       AND receivable.BusinessId = @BusinessId
       AND receivable.SourceDocumentType = source.SourceDocumentType
       AND receivable.SourceLineId IS NULL
    WHERE payment.DispatchId = @DispatchId
      AND payment.ApplicationType IN (N'InvoicePayment', N'CreditAdvance')
      AND (payment.ApplicationType=N'CreditAdvance' OR sale.CollectionMode=N'Session')
      AND payment.BusinessId=@BusinessId
      AND payment.MethodCode IN (N'Cash', N'Transfer', N'DebitCard', N'CreditCard')
      AND sale.CustomerId IS NOT NULL
    ORDER BY source.SourceDocumentId,PaymentKeyMethod,payment.PaymentId;
END
