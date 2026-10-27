-- Compatibility metadata only: copy the site's immutable group value, never
-- today's preferred PartySite. No balances, sources, payments or jobs are made.
DECLARE @ManualPaymentSiteLastId uniqueidentifier=NULL;
CREATE TABLE #ManualPaymentSiteBatch(SourceDocumentId uniqueidentifier PRIMARY KEY,TenantId uniqueidentifier,BusinessId uniqueidentifier);
CREATE TABLE #ManualPaymentSites(PaymentId uniqueidentifier,Kind nvarchar(16) COLLATE DATABASE_DEFAULT,
  GroupId uniqueidentifier,SourceDocumentId uniqueidentifier,PartyId uniqueidentifier,PartySiteId uniqueidentifier,
  TenantId uniqueidentifier,BusinessId uniqueidentifier,PRIMARY KEY(PaymentId,Kind));
WHILE 1=1
BEGIN
  INSERT #ManualPaymentSiteBatch
  SELECT TOP(100) SourceDocumentId,TenantId,BusinessId FROM dbo.AccountingSourceDocuments
  WHERE SourceDocumentType=N'ManualAccountingVoucher'
    AND (@ManualPaymentSiteLastId IS NULL OR SourceDocumentId>@ManualPaymentSiteLastId)
  ORDER BY SourceDocumentId OPTION(RECOMPILE);
  IF @@ROWCOUNT=0 BREAK;
  INSERT #ManualPaymentSites
  SELECT value.PaymentId,value.SubledgerKind,value.GroupId,batch.SourceDocumentId,value.PartyId,value.PartySiteId,
    batch.TenantId,batch.BusinessId
  FROM #ManualPaymentSiteBatch batch JOIN dbo.AccountingSourceDocuments source
    ON source.SourceDocumentId=batch.SourceDocumentId AND source.SourceDocumentType=N'ManualAccountingVoucher'
  CROSS APPLY OPENJSON(source.PayloadJson,'$.FinancialEffects.Payments') WITH(PaymentId uniqueidentifier,
    SubledgerKind nvarchar(16),GroupId uniqueidentifier,PartyId uniqueidentifier,PartySiteId uniqueidentifier) value
  WHERE JSON_VALUE(source.PayloadJson,'$.PayloadVersion')=N'4' AND value.PartySiteId IS NOT NULL;
  IF EXISTS(SELECT 1 FROM #ManualPaymentSites value LEFT JOIN dbo.PartySites site
      ON site.PartySiteId=value.PartySiteId AND site.PartyId=value.PartyId
    LEFT JOIN dbo.Parties party ON party.PartyId=site.PartyId AND party.TenantId=value.TenantId
    WHERE party.PartyId IS NULL)
    THROW 51852,N'The immutable payment site is outside its party/tenant. Review before release.',1;
  IF EXISTS(SELECT 1 FROM #ManualPaymentSites value JOIN dbo.SupplierPayments payment ON value.Kind=N'Payable'
      AND payment.PaymentId=value.PaymentId AND payment.ParentSourceDocumentId=value.SourceDocumentId
      AND payment.ParentSourceDocumentType=N'ManualAccountingVoucher' AND payment.SourceGroupId=value.GroupId
    WHERE payment.BusinessId<>value.BusinessId OR (payment.PartySiteId IS NOT NULL AND payment.PartySiteId<>value.PartySiteId))
    OR EXISTS(SELECT 1 FROM #ManualPaymentSites value JOIN dbo.CustomerPayments payment ON value.Kind=N'Receivable'
      AND payment.PaymentId=value.PaymentId AND payment.ParentSourceDocumentId=value.SourceDocumentId
      AND payment.ParentSourceDocumentType=N'ManualAccountingVoucher' AND payment.SourceGroupId=value.GroupId
    WHERE payment.BusinessId<>value.BusinessId OR (payment.PartySiteId IS NOT NULL AND payment.PartySiteId<>value.PartySiteId))
    THROW 51852,N'The payment site differs from its immutable source. Review before release.',1;
  UPDATE payment SET PartySiteId=value.PartySiteId FROM #ManualPaymentSites value JOIN dbo.SupplierPayments payment
    ON value.Kind=N'Payable' AND payment.PaymentId=value.PaymentId AND payment.BusinessId=value.BusinessId
      AND payment.ParentSourceDocumentId=value.SourceDocumentId AND payment.ParentSourceDocumentType=N'ManualAccountingVoucher'
      AND payment.SourceGroupId=value.GroupId WHERE payment.PartySiteId IS NULL;
  UPDATE payment SET PartySiteId=value.PartySiteId FROM #ManualPaymentSites value JOIN dbo.CustomerPayments payment
    ON value.Kind=N'Receivable' AND payment.PaymentId=value.PaymentId AND payment.BusinessId=value.BusinessId
      AND payment.ParentSourceDocumentId=value.SourceDocumentId AND payment.ParentSourceDocumentType=N'ManualAccountingVoucher'
      AND payment.SourceGroupId=value.GroupId WHERE payment.PartySiteId IS NULL;
  SELECT TOP(1) @ManualPaymentSiteLastId=SourceDocumentId FROM #ManualPaymentSiteBatch ORDER BY SourceDocumentId DESC;
  TRUNCATE TABLE #ManualPaymentSites;
  TRUNCATE TABLE #ManualPaymentSiteBatch;
END;
DROP TABLE #ManualPaymentSites;
DROP TABLE #ManualPaymentSiteBatch;
GO
