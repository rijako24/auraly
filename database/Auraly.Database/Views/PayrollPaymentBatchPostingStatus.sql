-- Read-only payment outcome shared by payroll history, replay and reports.
-- Confirmation reserves members; only the canonical posting job proves payment.
CREATE VIEW payroll.PaymentBatchPostingStatus AS
SELECT batch.PaymentBatchId,batch.TenantId,batch.BusinessId,job.Status AccountingStatus,
    CONVERT(bit,CASE WHEN batch.Status=N'Confirmed' AND job.Status=N'Posted' THEN 1 ELSE 0 END) IsPaid
FROM payroll.PaymentBatches batch
LEFT JOIN dbo.AccountingPostingJobs job ON job.TenantId=batch.TenantId AND job.BusinessId=batch.BusinessId
    AND job.SourceDocumentId=batch.PaymentBatchId AND job.SourceDocumentType=N'PayrollPayment';
GO
