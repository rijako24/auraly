-- Acceptance and explicit retry share this participant. It only fills the
-- account of a new native tender; applied balances and journals belong to posting.
CREATE PROCEDURE accounting.PortfolioPaymentAccountsFreeze
    @TenantId uniqueidentifier,@SourcesJson nvarchar(max),@Now datetimeoffset(7)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @@TRANCOUNT=0 OR ISNULL(ISJSON(@SourcesJson),0)<>1
      OR LEFT(LTRIM(@SourcesJson),1)<>N'['
      OR (SELECT COUNT(*) FROM OPENJSON(@SourcesJson)) NOT BETWEEN 1 AND 100
      THROW 51731,N'La resolución de cuentas requiere un lote aceptado y su transacción.',1;

    DECLARE @Sources TABLE(DocumentId uniqueidentifier,DocumentType nvarchar(64),BusinessId uniqueidentifier,
      OccurredOn date,Banks nvarchar(max),PRIMARY KEY(DocumentId,DocumentType));
    INSERT @Sources SELECT job.SourceDocumentId,job.SourceDocumentType,job.BusinessId,
      CONVERT(date,job.OccurredAt),job.ResolvedBankAccountsJson
    FROM OPENJSON(@SourcesJson) WITH(DocumentId uniqueidentifier,DocumentType nvarchar(64),BusinessId uniqueidentifier) input
    JOIN dbo.AccountingPostingJobs job WITH(UPDLOCK,HOLDLOCK)
      ON job.TenantId=@TenantId AND job.BusinessId=input.BusinessId
        AND job.SourceDocumentId=input.DocumentId AND job.SourceDocumentType=input.DocumentType
    WHERE job.SourceDocumentType IN(N'PayablePayment',N'ReceivablePayment')
      AND job.AccountingEntryRequired=1 AND job.Status IN(N'Pending',N'AccountingPendingConfiguration');

    DECLARE @Accounts TABLE(Kind nvarchar(16),PaymentId uniqueidentifier,LineNumber int,AccountId uniqueidentifier,
      PRIMARY KEY(Kind,PaymentId,LineNumber));
    INSERT @Accounts
    SELECT input.Kind,input.PaymentId,input.LineNumber,
      CASE WHEN input.BankAccountId IS NOT NULL THEN bank.AccountId ELSE configured.AccountId END
    FROM
    (
      SELECT N'Payable' Kind,source.*,tender.PaymentId,tender.LineNumber,tender.MethodCode,tender.BankAccountId
      FROM @Sources source JOIN dbo.SupplierPayments payment ON source.DocumentType=N'PayablePayment'
        AND payment.PaymentId=source.DocumentId AND payment.BusinessId=source.BusinessId AND payment.ParentSourceDocumentId IS NULL
      JOIN dbo.SupplierPaymentTenders tender WITH(UPDLOCK,HOLDLOCK) ON tender.PaymentId=payment.PaymentId
      WHERE tender.SourceLineId IS NOT NULL AND tender.AccountId IS NULL
      UNION ALL
      SELECT N'Receivable',source.*,tender.PaymentId,tender.LineNumber,tender.MethodCode,tender.BankAccountId
      FROM @Sources source JOIN dbo.CustomerPayments payment ON source.DocumentType=N'ReceivablePayment'
        AND payment.PaymentId=source.DocumentId AND payment.BusinessId=source.BusinessId AND payment.ParentSourceDocumentId IS NULL
      JOIN dbo.CustomerPaymentTenders tender WITH(UPDLOCK,HOLDLOCK) ON tender.PaymentId=payment.PaymentId
      WHERE tender.SourceLineId IS NOT NULL AND tender.AccountId IS NULL
    ) input
    OUTER APPLY accounting.PaymentPostingCategory(
      CASE WHEN input.Kind=N'Payable' THEN N'SupplierPaymentMethod' ELSE N'CustomerPaymentMethod' END,input.MethodCode) category
    OUTER APPLY accounting.ConfiguredPostingAccount(@TenantId,input.BusinessId,category.Category,input.OccurredOn) configured
    OUTER APPLY(SELECT value.AccountId FROM OPENJSON(input.Banks)
      WITH(BankAccountId uniqueidentifier,AccountId uniqueidentifier) value WHERE value.BankAccountId=input.BankAccountId) bank;

    UPDATE tender SET AccountId=input.AccountId FROM dbo.SupplierPaymentTenders tender
    JOIN @Accounts input ON input.Kind=N'Payable' AND input.PaymentId=tender.PaymentId AND input.LineNumber=tender.LineNumber
    WHERE tender.AccountId IS NULL AND input.AccountId IS NOT NULL;
    UPDATE tender SET AccountId=input.AccountId FROM dbo.CustomerPaymentTenders tender
    JOIN @Accounts input ON input.Kind=N'Receivable' AND input.PaymentId=tender.PaymentId AND input.LineNumber=tender.LineNumber
    WHERE tender.AccountId IS NULL AND input.AccountId IS NOT NULL;
    UPDATE account SET ConfigurationLockedAt=@Now FROM dbo.AccountingAccounts account
    WHERE account.TenantId=@TenantId AND account.ConfigurationLockedAt IS NULL
      AND EXISTS(SELECT 1 FROM @Accounts input WHERE input.AccountId=account.AccountId);
END;
GO
