CREATE PROCEDURE dbo.DispatchSettlementPaymentsGet
    @BusinessId UNIQUEIDENTIFIER,
    @DispatchId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @PrimaryBankAccountId UNIQUEIDENTIFIER;
    SELECT @PrimaryBankAccountId = bank.BankAccountId
    FROM dbo.Businesses business
    INNER JOIN accounting.BankAccounts bank ON bank.TenantId = business.TenantId
        AND bank.IsPrimary = 1 AND bank.IsActive = 1
    WHERE business.BusinessId = @BusinessId;
    SELECT source.SourceDocumentId,
           sale.CustomerId,
           receivable.ReceivableId,
           payment.PaymentMethod,
           SUM(payment.Amount),
           MAX(payment.Reference),
           CASE WHEN payment.PaymentMethod = N'Deposit' THEN @PrimaryBankAccountId ELSE NULL END
    FROM dbo.DispatchDeliveryPayments payment
    INNER JOIN dbo.DispatchSourceDocuments source
        ON source.DispatchSourceDocumentId = payment.DispatchSourceDocumentId
    INNER JOIN dbo.SalesDocuments sale ON sale.DocumentId = source.SourceDocumentId
    INNER JOIN dbo.Receivables receivable
        ON receivable.SourceDocumentId = source.SourceDocumentId
       AND receivable.BusinessId = @BusinessId
       AND receivable.Status IN (N'Open', N'PartiallyPaid')
    WHERE payment.DispatchId = @DispatchId
      AND payment.ApplicationType IN (N'InvoicePayment', N'CreditAdvance')
      AND payment.PaymentMethod IN (N'Cash', N'Deposit')
      AND sale.CustomerId IS NOT NULL
    GROUP BY source.SourceDocumentId, sale.CustomerId, receivable.ReceivableId, payment.PaymentMethod;
END
