-- Supported immutable treasury contracts: header, single payment, and tender arrays.
CREATE FUNCTION accounting.AccountingBankReferences(@PayloadJson nvarchar(max))
RETURNS TABLE
AS RETURN
(
    SELECT BankAccountId FROM
    (
        SELECT value.BankAccountId
        FROM OPENJSON(@PayloadJson) WITH(
          BankAccountId uniqueidentifier '$.BankAccountId',WebBankAccountId uniqueidentifier '$.bankAccountId',
          FormId uniqueidentifier '$.PaymentMethodId',WebFormId uniqueidentifier '$.paymentMethodId',
          PaymentBankId uniqueidentifier '$.Payment.BankAccountId',WebPaymentBankId uniqueidentifier '$.payment.bankAccountId',
          PaymentFormId uniqueidentifier '$.Payment.PaymentMethodId',WebPaymentFormId uniqueidentifier '$.payment.paymentMethodId',
          Tenders nvarchar(max) '$.Tenders' AS JSON,WebTenders nvarchar(max) '$.tenders' AS JSON,
          Payments nvarchar(max) '$.Payments' AS JSON,WebPayments nvarchar(max) '$.payments' AS JSON) header
        CROSS APPLY(
          SELECT header.BankAccountId WHERE COALESCE(header.FormId,header.WebFormId) IS NULL
          UNION ALL SELECT header.WebBankAccountId WHERE COALESCE(header.FormId,header.WebFormId) IS NULL
          UNION ALL SELECT header.PaymentBankId WHERE COALESCE(header.PaymentFormId,header.WebPaymentFormId) IS NULL
          UNION ALL SELECT header.WebPaymentBankId WHERE COALESCE(header.PaymentFormId,header.WebPaymentFormId) IS NULL
          UNION ALL
          SELECT COALESCE(payment.BankAccountId,payment.WebBankAccountId)
          FROM (VALUES(header.Tenders),(header.WebTenders),(header.Payments),(header.WebPayments)) array(Payments)
          CROSS APPLY OPENJSON(array.Payments) WITH(
            BankAccountId uniqueidentifier '$.BankAccountId',WebBankAccountId uniqueidentifier '$.bankAccountId',
            FormId uniqueidentifier '$.PaymentMethodId',WebFormId uniqueidentifier '$.paymentMethodId') payment
          WHERE COALESCE(payment.FormId,payment.WebFormId) IS NULL
        ) value
    ) banks WHERE BankAccountId IS NOT NULL GROUP BY BankAccountId
);
GO
