-- Read-only gate: the release pipeline runs this before calculating or
-- publishing schema changes. The retirement reuses it before changing forms.
-- New installations may not have these tables yet.
IF OBJECT_ID(N'dbo.SalesPayments',N'U') IS NOT NULL
  AND COL_LENGTH(N'dbo.SalesPayments',N'MethodCode') IS NOT NULL
  AND COL_LENGTH(N'dbo.SalesDocuments',N'ProcessingStatus') IS NOT NULL
  AND COL_LENGTH(N'dbo.AccountingPostingJobs',N'Status') IS NOT NULL
  EXEC sys.sp_executesql N'
    IF EXISTS(SELECT 1 FROM dbo.SalesPayments payment
      JOIN dbo.SalesDocuments sale ON sale.DocumentId=payment.DocumentId
      LEFT JOIN dbo.AccountingPostingJobs financial ON financial.SourceDocumentId=sale.DocumentId
        AND financial.SourceDocumentType=sale.DocumentType AND financial.BusinessId=sale.BusinessId
      WHERE payment.MethodCode=N''PendingCollection'' AND
        (sale.ProcessingStatus<>N''Completed'' OR financial.Status IS NULL OR financial.Status NOT IN(N''Posted'',N''CommercialEffectsApplied'')))
      THROW 51923,N''Completa los comprobantes anteriores por recaudar antes de retirar su tipo. No se modificó su historia.'',1;';
