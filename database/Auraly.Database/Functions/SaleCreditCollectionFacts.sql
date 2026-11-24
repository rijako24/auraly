CREATE FUNCTION accounting.SaleCreditCollectionFacts(@TenantId UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER,@DocumentsJson NVARCHAR(MAX))
RETURNS TABLE
AS RETURN
(
    WITH SelectedSales AS
    (
      SELECT sale.DocumentId,sale.WorkSessionId,sale.CollectionMode
      FROM dbo.SalesDocuments sale
      JOIN dbo.Businesses business ON business.BusinessId=sale.BusinessId AND business.TenantId=@TenantId
      JOIN(SELECT DISTINCT DocumentId FROM OPENJSON(@DocumentsJson) WITH(DocumentId uniqueidentifier)) selected ON selected.DocumentId=sale.DocumentId
      WHERE sale.BusinessId=@BusinessId
    ), SelectedSessions AS
    (
      SELECT DISTINCT WorkSessionId FROM SelectedSales WHERE CollectionMode=N'Session'
    ), SelectedDispatches AS
    (
      SELECT DISTINCT document.DispatchId FROM SelectedSales sale
      JOIN dbo.DispatchSourceDocuments document ON document.SourceDocumentId=sale.DocumentId
      JOIN dbo.Dispatches dispatch ON dispatch.DispatchId=document.DispatchId
        AND dispatch.TenantId=@TenantId AND dispatch.BusinessId=@BusinessId
    )
    -- Only completed financial sources change the displayed tender. Ordinary
    -- portfolio payments do not reclassify the original sale's payment method.
    SELECT sale.DocumentId,changed.CorrectionId PaymentId,changed.MethodCode,changed.Amount,
      COALESCE(changed.ApprovalNumber,changed.Reference) Reference,
      COALESCE(binding.Name,changed.PaymentMethodName) FormName,sale.WorkSessionId
    FROM SelectedSessions selected
    JOIN dbo.WorkSessionClosures closure ON closure.WorkSessionId=selected.WorkSessionId
    JOIN dbo.WorkSessionClosureReconciliations correction ON correction.WorkSessionClosureId=closure.WorkSessionClosureId
    JOIN dbo.AccountingPostingJobs financial ON financial.SourceDocumentId=correction.ReconciliationId
      AND financial.SourceDocumentType=N'WorkSessionClosureReconciliation' AND financial.TenantId=@TenantId
      AND financial.BusinessId=@BusinessId AND financial.Status IN(N'Posted',N'CommercialEffectsApplied')
    CROSS APPLY OPENJSON(correction.SnapshotJson,N'$.paymentCorrections') WITH(
      SourceId uniqueidentifier '$.sourceId',MovementType nvarchar(32) '$.movementType',
      CorrectionId uniqueidentifier '$.correctionId',MethodCode nvarchar(32) '$.tenderMethodCode',
      Amount decimal(19,4) '$.amount',Reference nvarchar(160) '$.reference',ApprovalNumber nvarchar(100) '$.approvalNumber',
      PaymentMethodName nvarchar(160) '$.paymentMethodName') changed
    JOIN SelectedSales sale ON sale.DocumentId=changed.SourceId AND sale.WorkSessionId=selected.WorkSessionId
      AND sale.CollectionMode=N'Session'
    OUTER APPLY(SELECT applied.Name FROM OPENJSON(financial.AppliedPaymentMethodsJson)
      WITH(SourceLineId uniqueidentifier,Name nvarchar(160)) applied WHERE applied.SourceLineId=changed.CorrectionId) binding
    WHERE changed.MovementType=N'CreditSale'
    UNION ALL
    SELECT sale.DocumentId,correction.CorrectionId,correction.TenderMethodCode,correction.Amount,
      COALESCE(correction.ApprovalNumber,correction.Reference),correction.PaymentMethodName,sale.WorkSessionId
    FROM SelectedSales sale
    JOIN accounting.SalePaymentCorrections correction ON correction.DocumentId=sale.DocumentId
      AND correction.TenantId=@TenantId AND correction.BusinessId=@BusinessId AND correction.MovementType=N'CreditSale'
      AND correction.PostingStatus IN(N'Posted',N'CommercialEffectsApplied')
    UNION ALL
    SELECT sale.DocumentId,payment.PaymentId,payment.MethodCode,payment.Amount,payment.Reference,payment.FormName,session.WorkSessionId
    FROM SelectedDispatches selected
    JOIN dbo.WorkSessions session ON session.DispatchId=selected.DispatchId AND session.Kind=N'Dispatch'
    JOIN dbo.WorkSessionClosures closure ON closure.WorkSessionId=session.WorkSessionId
    JOIN dbo.AccountingPostingJobs financial ON financial.SourceDocumentId=closure.WorkSessionClosureId
      AND financial.SourceDocumentType=N'DispatchCashDifference' AND financial.TenantId=@TenantId
      AND financial.BusinessId=@BusinessId AND financial.Status IN(N'Posted',N'CommercialEffectsApplied')
    CROSS APPLY dispatch.EffectivePayments(selected.DispatchId) payment
    JOIN dbo.DispatchSourceDocuments document ON document.DispatchSourceDocumentId=payment.DispatchSourceDocumentId
      AND document.DispatchId=selected.DispatchId
    JOIN SelectedSales sale ON sale.DocumentId=document.SourceDocumentId
    WHERE payment.ApplicationType=N'CreditAdvance' AND payment.Amount>0
);
GO
