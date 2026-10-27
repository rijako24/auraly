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
          PaymentBankId uniqueidentifier '$.Payment.BankAccountId',WebPaymentBankId uniqueidentifier '$.payment.bankAccountId',
          Tenders nvarchar(max) '$.Tenders' AS JSON,WebTenders nvarchar(max) '$.tenders' AS JSON,
          Payments nvarchar(max) '$.Payments' AS JSON,WebPayments nvarchar(max) '$.payments' AS JSON) header
        CROSS APPLY(
          SELECT header.BankAccountId UNION ALL SELECT header.WebBankAccountId
          UNION ALL SELECT header.PaymentBankId UNION ALL SELECT header.WebPaymentBankId
          UNION ALL
          SELECT COALESCE(payment.BankAccountId,payment.WebBankAccountId)
          FROM (VALUES(header.Tenders),(header.WebTenders),(header.Payments),(header.WebPayments)) array(Payments)
          CROSS APPLY OPENJSON(array.Payments) WITH(
            BankAccountId uniqueidentifier '$.BankAccountId',WebBankAccountId uniqueidentifier '$.bankAccountId') payment
        ) value
    ) banks WHERE BankAccountId IS NOT NULL GROUP BY BankAccountId
);
GO
