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
            ExpenseAccountId uniqueidentifier '$.ExpenseAccountId',
            WebExpenseAccountId uniqueidentifier '$.expenseAccountId',
            Lines nvarchar(max) '$.Lines' AS JSON,
            WebLines nvarchar(max) '$.lines' AS JSON
        ) header
        CROSS APPLY
        (
            SELECT header.CounterpartAccountId AccountId
            UNION ALL SELECT header.WebCounterpartAccountId
            UNION ALL SELECT header.ExpenseAccountId
            UNION ALL SELECT header.WebExpenseAccountId
            UNION ALL
            SELECT lineValue.AccountId
            FROM (VALUES(header.Lines),(header.WebLines)) payload(Lines)
            CROSS APPLY OPENJSON(payload.Lines) WITH
            (
                AccountId uniqueidentifier '$.AccountId',
                WebAccountId uniqueidentifier '$.accountId',
                AccountingAccountId uniqueidentifier '$.AccountingAccountId',
                WebAccountingAccountId uniqueidentifier '$.accountingAccountId',
                ExpenseAccountId uniqueidentifier '$.ExpenseAccountId',
                WebExpenseAccountId uniqueidentifier '$.expenseAccountId'
            ) line
            CROSS APPLY (VALUES(line.AccountId),(line.WebAccountId),
                (line.AccountingAccountId),(line.WebAccountingAccountId),
                (line.ExpenseAccountId),(line.WebExpenseAccountId)) lineValue(AccountId)
        ) value
    ) accounts
    WHERE AccountId IS NOT NULL
    GROUP BY AccountId
);
GO
