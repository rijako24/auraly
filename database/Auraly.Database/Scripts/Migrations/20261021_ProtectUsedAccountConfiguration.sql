-- Protect existing history without rescanning source payloads during account editing.
-- Stop legacy acceptance writers before the cutover; new acceptance protects the
-- same references in its transaction. This derived marker has no financial value.
UPDATE account SET ConfigurationLockedAt=SYSUTCDATETIME()
FROM dbo.AccountingAccounts account
WHERE account.ConfigurationLockedAt IS NULL AND EXISTS(
    SELECT 1 FROM dbo.AccountingEntryLines line WHERE line.AccountId=account.AccountId);

DECLARE @AccountReferenceLastId uniqueidentifier=NULL,@AccountReferenceLastType nvarchar(64)=NULL;
CREATE TABLE #AccountReferenceBatch(DocumentId uniqueidentifier,DocumentType nvarchar(64) COLLATE DATABASE_DEFAULT,TenantId uniqueidentifier,
    PRIMARY KEY(DocumentId,DocumentType));
WHILE 1=1
BEGIN
    INSERT #AccountReferenceBatch
    SELECT TOP(100) SourceDocumentId,SourceDocumentType,TenantId FROM dbo.AccountingSourceDocuments
    WHERE @AccountReferenceLastId IS NULL OR SourceDocumentId>@AccountReferenceLastId
       OR (SourceDocumentId=@AccountReferenceLastId AND SourceDocumentType>@AccountReferenceLastType)
    ORDER BY SourceDocumentId,SourceDocumentType OPTION(RECOMPILE);
    IF @@ROWCOUNT=0 BREAK;

    UPDATE account SET ConfigurationLockedAt=SYSUTCDATETIME()
    FROM dbo.AccountingAccounts account JOIN (
        SELECT DISTINCT batch.TenantId,reference.AccountId FROM #AccountReferenceBatch batch
        JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=batch.DocumentId
            AND source.SourceDocumentType=batch.DocumentType
        CROSS APPLY accounting.AccountingAccountReferences(source.PayloadJson) reference
        WHERE source.AccountingEntryRequired=1 OR source.AccountingEntryRequired IS NULL
    ) reference ON reference.AccountId=account.AccountId AND reference.TenantId=account.TenantId
    WHERE account.ConfigurationLockedAt IS NULL;

    SELECT TOP(1) @AccountReferenceLastId=DocumentId,@AccountReferenceLastType=DocumentType
    FROM #AccountReferenceBatch ORDER BY DocumentId DESC,DocumentType DESC;
    TRUNCATE TABLE #AccountReferenceBatch;
END;
DROP TABLE #AccountReferenceBatch;
