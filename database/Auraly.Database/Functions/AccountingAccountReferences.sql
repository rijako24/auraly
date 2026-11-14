CREATE FUNCTION accounting.AccountingAccountReferences(@PayloadJson nvarchar(max))
RETURNS TABLE
AS RETURN
(
    SELECT AccountId FROM
    (
        SELECT value.AccountId
        FROM OPENJSON(@PayloadJson) WITH
        (
            CounterpartAccountId uniqueidentifier '$.CounterpartAccountId',
            WebCounterpartAccountId uniqueidentifier '$.counterpartAccountId',
            ConceptId uniqueidentifier '$.ConceptId',WebConceptId uniqueidentifier '$.conceptId',
            ExpenseAccountId uniqueidentifier '$.ExpenseAccountId',
            WebExpenseAccountId uniqueidentifier '$.expenseAccountId',
            Lines nvarchar(max) '$.Lines' AS JSON,
            WebLines nvarchar(max) '$.lines' AS JSON
        ) header
        CROSS APPLY
        (
            SELECT header.CounterpartAccountId AccountId
            UNION ALL SELECT header.WebCounterpartAccountId
            UNION ALL SELECT header.ExpenseAccountId WHERE COALESCE(header.ConceptId,header.WebConceptId) IS NULL AND header.Lines IS NULL AND header.WebLines IS NULL
            UNION ALL SELECT header.WebExpenseAccountId WHERE COALESCE(header.ConceptId,header.WebConceptId) IS NULL AND header.Lines IS NULL AND header.WebLines IS NULL
            UNION ALL
            SELECT lineValue.AccountId
            FROM (VALUES(header.Lines),(header.WebLines)) payload(Lines)
            CROSS APPLY OPENJSON(payload.Lines) WITH
            (
                ConceptId uniqueidentifier '$.ConceptId',WebConceptId uniqueidentifier '$.conceptId',
                FormId uniqueidentifier '$.PaymentEvidence.PaymentMethodId',WebFormId uniqueidentifier '$.paymentEvidence.paymentMethodId',
                AccountId uniqueidentifier '$.AccountId',
                WebAccountId uniqueidentifier '$.accountId',
                AccountingAccountId uniqueidentifier '$.AccountingAccountId',
                WebAccountingAccountId uniqueidentifier '$.accountingAccountId',
                ExpenseAccountId uniqueidentifier '$.ExpenseAccountId',
                WebExpenseAccountId uniqueidentifier '$.expenseAccountId'
            ) line
            CROSS APPLY (
                SELECT line.AccountId AccountId WHERE COALESCE(line.FormId,line.WebFormId) IS NULL
                UNION ALL SELECT line.WebAccountId WHERE COALESCE(line.FormId,line.WebFormId) IS NULL
                UNION ALL SELECT line.AccountingAccountId
                UNION ALL SELECT line.WebAccountingAccountId
                UNION ALL SELECT line.ExpenseAccountId WHERE COALESCE(line.ConceptId,line.WebConceptId) IS NULL
                UNION ALL SELECT line.WebExpenseAccountId WHERE COALESCE(line.ConceptId,line.WebConceptId) IS NULL
            ) lineValue
        ) value
    ) accounts
    WHERE AccountId IS NOT NULL
    GROUP BY AccountId
);
GO
