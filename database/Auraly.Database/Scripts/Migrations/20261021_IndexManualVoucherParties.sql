-- Derived party membership and credit destinations share one bounded traversal
-- of the immutable sources. Their parsers are also the acceptance parsers.
-- The final coverage check uses the same bounded batches. A full-source JSON
-- anti-join would request memory for the whole history during deployment.
DECLARE @SourcePartyLastId uniqueidentifier=NULL;
DECLARE @SourcePartyValidationPass bit=0;
DECLARE @SourcePartyInserted bit=0;
CREATE TABLE #ManualVoucherPartyBatch(SourceDocumentId uniqueidentifier PRIMARY KEY,TenantId uniqueidentifier,BusinessId uniqueidentifier);
CREATE TABLE #ManualVoucherPartyReferences(SourceDocumentId uniqueidentifier,PartyId uniqueidentifier,
    PRIMARY KEY(SourceDocumentId,PartyId));
CREATE TABLE #ManualVoucherCreditTargets(SourceDocumentId uniqueidentifier,EffectId uniqueidentifier,
    Kind nvarchar(16) COLLATE DATABASE_DEFAULT,PositionId uniqueidentifier,SourceLineId uniqueidentifier,Amount decimal(19,4),
    EffectKind nvarchar(24) COLLATE DATABASE_DEFAULT,ReversesTransactionId uniqueidentifier,
    PRIMARY KEY(SourceDocumentId,EffectId,Kind));
WHILE 1=1
BEGIN
    INSERT #ManualVoucherPartyBatch
    SELECT TOP(100) SourceDocumentId,TenantId,BusinessId FROM dbo.AccountingSourceDocuments
    WHERE SourceDocumentType=N'ManualAccountingVoucher'
      AND (@SourcePartyLastId IS NULL OR SourceDocumentId>@SourcePartyLastId)
    ORDER BY SourceDocumentId OPTION (RECOMPILE);
    IF @@ROWCOUNT=0
    BEGIN
        -- A no-op pass already resolved, scoped and checked every reference.
        -- Revalidate writes separately, without parsing unchanged history twice.
        IF @SourcePartyValidationPass=1 OR @SourcePartyInserted=0 BREAK;
        SET @SourcePartyValidationPass=1;
        SET @SourcePartyLastId=NULL;
        CONTINUE;
    END;

    INSERT #ManualVoucherPartyReferences
    SELECT batch.SourceDocumentId,reference.PartyId FROM #ManualVoucherPartyBatch batch
    JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=batch.SourceDocumentId
      AND source.SourceDocumentType=N'ManualAccountingVoucher'
    CROSS APPLY accounting.ManualVoucherPartyReferences(source.PayloadJson) reference;
    IF EXISTS(SELECT 1 FROM #ManualVoucherPartyReferences reference
        JOIN #ManualVoucherPartyBatch batch ON batch.SourceDocumentId=reference.SourceDocumentId
        LEFT JOIN dbo.Parties party ON party.PartyId=reference.PartyId AND party.TenantId=batch.TenantId
        WHERE party.PartyId IS NULL)
        THROW 51734,N'A manual-voucher source references a party outside its tenant. Review the source before release.',1;

    INSERT #ManualVoucherCreditTargets
    SELECT batch.SourceDocumentId,target.EffectId,target.SubledgerKind,target.SubledgerId,target.SourceLineId,target.Amount,
      target.EffectKind,target.ReversesTransactionId
    FROM #ManualVoucherPartyBatch batch JOIN dbo.AccountingSourceDocuments source
      ON source.SourceDocumentId=batch.SourceDocumentId AND source.SourceDocumentType=N'ManualAccountingVoucher'
    CROSS APPLY accounting.ManualVoucherCreditTargets(source.PayloadJson) target;
    IF EXISTS(SELECT 1 FROM #ManualVoucherCreditTargets)
    BEGIN
        IF EXISTS(SELECT 1 FROM #ManualVoucherCreditTargets target JOIN #ManualVoucherPartyBatch batch
            ON batch.SourceDocumentId=target.SourceDocumentId
          WHERE NOT EXISTS(SELECT 1 FROM dbo.Payables WHERE target.Kind=N'Payable' AND PayableId=target.PositionId AND BusinessId=batch.BusinessId)
            AND NOT EXISTS(SELECT 1 FROM dbo.Receivables WHERE target.Kind=N'Receivable' AND ReceivableId=target.PositionId AND BusinessId=batch.BusinessId)
            AND NOT EXISTS(SELECT 1 FROM dbo.SupplierCredits WHERE target.Kind=N'SupplierCredit' AND SupplierCreditId=target.PositionId AND BusinessId=batch.BusinessId)
            AND NOT EXISTS(SELECT 1 FROM dbo.CustomerCredits WHERE target.Kind=N'CustomerCredit' AND CustomerCreditId=target.PositionId AND BusinessId=batch.BusinessId))
          THROW 51851,N'A manual credit destination is outside its accepted business. Review the immutable source before release.',1;
        IF EXISTS(SELECT 1 FROM #ManualVoucherCreditTargets target JOIN #ManualVoucherPartyBatch batch
            ON batch.SourceDocumentId=target.SourceDocumentId
          JOIN accounting.SourcePositionIndex currentIndex ON currentIndex.SourceDocumentId=target.SourceDocumentId
            AND currentIndex.SourceDocumentType=N'ManualAccountingVoucher' AND currentIndex.EffectId=target.EffectId AND currentIndex.SubledgerKind=target.Kind
          WHERE currentIndex.BusinessId<>batch.BusinessId OR currentIndex.TenantId<>batch.TenantId
            OR currentIndex.SubledgerId<>target.PositionId OR currentIndex.SourceLineId<>target.SourceLineId OR currentIndex.Amount<>target.Amount
            OR currentIndex.EffectKind<>target.EffectKind
            OR ISNULL(currentIndex.ReversesTransactionId,'00000000-0000-0000-0000-000000000000')<>ISNULL(target.ReversesTransactionId,'00000000-0000-0000-0000-000000000000'))
          THROW 51851,N'The financial destination index differs from its immutable source. Stop release and review the inconsistency.',1;
        IF @SourcePartyValidationPass=0
        BEGIN
          INSERT accounting.SourcePositionIndex(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,EffectId,
            SubledgerKind,SubledgerId,SourceLineId,Amount,EffectKind,ReversesTransactionId)
          SELECT target.SourceDocumentId,N'ManualAccountingVoucher',batch.TenantId,batch.BusinessId,target.EffectId,
            target.Kind,target.PositionId,target.SourceLineId,target.Amount,target.EffectKind,target.ReversesTransactionId
          FROM #ManualVoucherCreditTargets target JOIN #ManualVoucherPartyBatch batch ON batch.SourceDocumentId=target.SourceDocumentId
          WHERE NOT EXISTS(SELECT 1 FROM accounting.SourcePositionIndex currentIndex WHERE currentIndex.SourceDocumentId=target.SourceDocumentId
            AND currentIndex.SourceDocumentType=N'ManualAccountingVoucher' AND currentIndex.EffectId=target.EffectId AND currentIndex.SubledgerKind=target.Kind);
          IF @@ROWCOUNT>0 SET @SourcePartyInserted=1;
        END
        ELSE IF EXISTS(SELECT 1 FROM #ManualVoucherCreditTargets target WHERE NOT EXISTS(SELECT 1 FROM accounting.SourcePositionIndex currentIndex
          WHERE currentIndex.SourceDocumentId=target.SourceDocumentId AND currentIndex.SourceDocumentType=N'ManualAccountingVoucher'
            AND currentIndex.EffectId=target.EffectId AND currentIndex.SubledgerKind=target.Kind))
          THROW 51851,N'The manual credit destination index is incomplete. Stop legacy writers and complete the backfill before enabling readers.',1;
    END;

    IF @SourcePartyValidationPass=0
    BEGIN
        INSERT accounting.SourcePartyIndex(PartyId,SourceDocumentId,SourceDocumentType)
        SELECT reference.PartyId,reference.SourceDocumentId,N'ManualAccountingVoucher'
        FROM #ManualVoucherPartyReferences reference
        WHERE NOT EXISTS(SELECT 1 FROM accounting.SourcePartyIndex currentIndex
            WHERE currentIndex.PartyId=reference.PartyId AND currentIndex.SourceDocumentId=reference.SourceDocumentId
              AND currentIndex.SourceDocumentType=N'ManualAccountingVoucher');
        IF @@ROWCOUNT>0 SET @SourcePartyInserted=1;
    END
    ELSE IF EXISTS(SELECT 1 FROM #ManualVoucherPartyReferences reference
        WHERE NOT EXISTS(SELECT 1 FROM accounting.SourcePartyIndex currentIndex
            WHERE currentIndex.PartyId=reference.PartyId AND currentIndex.SourceDocumentId=reference.SourceDocumentId
              AND currentIndex.SourceDocumentType=N'ManualAccountingVoucher'))
        THROW 51735,N'The manual-voucher party index is incomplete. Stop legacy source writers and rerun the backfill before enabling readers.',1;

    SELECT TOP(1) @SourcePartyLastId=SourceDocumentId FROM #ManualVoucherPartyBatch ORDER BY SourceDocumentId DESC;
    TRUNCATE TABLE #ManualVoucherPartyReferences;
    TRUNCATE TABLE #ManualVoucherCreditTargets;
    TRUNCATE TABLE #ManualVoucherPartyBatch;
END;
DROP TABLE #ManualVoucherPartyReferences;
DROP TABLE #ManualVoucherCreditTargets;
DROP TABLE #ManualVoucherPartyBatch;
GO
