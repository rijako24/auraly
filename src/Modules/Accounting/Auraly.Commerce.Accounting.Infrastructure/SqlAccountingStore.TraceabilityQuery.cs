namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingStore
{
    private const string TraceabilityDocumentsSql = """
            WITH posting AS (
              SELECT SourceDocumentId,SourceDocumentType,OccurredAt,Status,AttemptCount,
                     LastErrorCode,LastErrorMessage,TenantId,BusinessId
              FROM dbo.AccountingPostingJobs
              UNION ALL
              SELECT source.SourceDocumentId,source.SourceDocumentType,source.OccurredAt,
                     N'MissingAccountingJob',0,N'MissingAccountingJob',
                     N'The immutable accounting source has no durable posting job.',
                     source.TenantId,source.BusinessId
              FROM dbo.AccountingSourceDocuments source
              WHERE NOT EXISTS(
                  SELECT 1 FROM dbo.AccountingPostingJobs job
                  WHERE job.SourceDocumentId=source.SourceDocumentId
                    AND job.SourceDocumentType=source.SourceDocumentType
                    AND job.BusinessId=source.BusinessId
                    AND job.TenantId=source.TenantId)
              UNION ALL
              SELECT DocumentId,DocumentType,OccurredAt,N'Created',0,NULL,NULL,TenantId,BusinessId
              FROM accounting.VoucherDrafts WHERE SentAt IS NULL
            )
            SELECT job.SourceDocumentId,job.SourceDocumentType,COALESCE(sourceNumber.DocumentNumber,draft.Reference) DocumentNumber,
                   job.OccurredAt,job.Status,job.AttemptCount,job.LastErrorCode,
                   job.LastErrorMessage,entry.EntryId,entry.EntryNumber,
                   COALESCE(entry.DebitTotal,draftTotals.DebitTotal,draft.AdjustmentAmount) DebitTotal,COALESCE(entry.CreditTotal,draftTotals.CreditTotal,draft.AdjustmentAmount) CreditTotal,entry.PostedAt,
                   fiscal.FiscalDocumentType,fiscal.FiscalNumber,fiscal.UniqueCodeType,
                   fiscal.UniqueCode,fiscal.FiscalStatus,CONVERT(bit,CASE WHEN draft.DocumentId IS NOT NULL THEN 1 ELSE 0 END) HasManualDraft
            INTO #Documents
            FROM posting job
            LEFT JOIN accounting.VoucherDrafts draft ON draft.DocumentId=job.SourceDocumentId AND draft.DocumentType=job.SourceDocumentType
            OUTER APPLY(SELECT SUM(Debit) DebitTotal,SUM(Credit) CreditTotal FROM accounting.VoucherDraftLines
              WHERE DocumentId=draft.DocumentId) draftTotals
            LEFT JOIN dbo.AccountingEntries entry
              ON entry.SourceDocumentId=job.SourceDocumentId
             AND entry.SourceDocumentType=job.SourceDocumentType
            LEFT JOIN dbo.FiscalDocuments fiscal
              ON fiscal.DocumentId=job.SourceDocumentId
             AND fiscal.BusinessId=job.BusinessId
            OUTER APPLY(SELECT TOP(1) candidate.DocumentNumber FROM (
              SELECT sale.DocumentNumber FROM dbo.SalesDocuments sale
               WHERE sale.DocumentId=job.SourceDocumentId
              UNION ALL SELECT saleReturn.DocumentNumber FROM dbo.SalesReturns saleReturn
               WHERE saleReturn.ReturnId=job.SourceDocumentId
              UNION ALL SELECT debitNote.DocumentNumber FROM dbo.SalesDebitNotes debitNote
               WHERE debitNote.DebitNoteId=job.SourceDocumentId
              UNION ALL SELECT receipt.DocumentNumber FROM dbo.GoodsReceipts receipt
               WHERE receipt.GoodsReceiptId=job.SourceDocumentId
              UNION ALL SELECT purchaseReturn.DocumentNumber FROM dbo.PurchaseReturns purchaseReturn
               WHERE purchaseReturn.PurchaseReturnId=job.SourceDocumentId
              UNION ALL SELECT expense.DocumentNumber FROM dbo.Expenses expense
               WHERE expense.ExpenseId=job.SourceDocumentId
              UNION ALL SELECT supplierPayment.DocumentNumber FROM dbo.SupplierPayments supplierPayment
               WHERE supplierPayment.PaymentId=job.SourceDocumentId
              UNION ALL SELECT customerPayment.DocumentNumber FROM dbo.CustomerPayments customerPayment
               WHERE customerPayment.PaymentId=job.SourceDocumentId
              UNION ALL SELECT cashMovement.DocumentNumber FROM dbo.CashMovementDocuments cashMovement
               WHERE cashMovement.DocumentId=job.SourceDocumentId
              UNION ALL SELECT operation.DocumentNumber FROM dbo.InventoryOperations operation
               WHERE operation.InventoryOperationId=job.SourceDocumentId
              UNION ALL SELECT cost.DocumentNumber FROM purchasing.GoodsReceiptCostDocuments cost
               WHERE cost.CostDocumentId=job.SourceDocumentId
            ) candidate) sourceNumber
            WHERE job.TenantId=@TenantId AND job.BusinessId=@BusinessId
              AND CAST(job.OccurredAt AS date) BETWEEN @From AND @To
              AND (@DocumentType IS NULL OR job.SourceDocumentType=@DocumentType)
              AND (@Status IS NULL OR job.Status=@Status)
              AND (@Search IS NULL OR sourceNumber.DocumentNumber LIKE N'%'+@Search+N'%'
                   OR draft.Reference LIKE N'%'+@Search+N'%'
                   OR draft.Description LIKE N'%'+@Search+N'%'
                   OR entry.EntryNumber LIKE N'%'+@Search+N'%'
                   OR CONVERT(nvarchar(36),job.SourceDocumentId) LIKE N'%'+@Search+N'%')
              AND (@PartyId IS NULL OR EXISTS(SELECT 1 FROM dbo.AccountingEntryLines l WHERE l.EntryId=entry.EntryId AND l.PartyId=@PartyId)
                   OR EXISTS(SELECT 1 FROM accounting.VoucherDraftLines l WHERE l.DocumentId=draft.DocumentId AND l.PartyId=@PartyId)
                   OR EXISTS(SELECT 1 FROM dbo.AccountingSourceDocuments source
                     CROSS APPLY OPENJSON(source.PayloadJson,'$.Lines') WITH(PartyId uniqueidentifier) l
                     WHERE source.SourceDocumentId=job.SourceDocumentId AND source.SourceDocumentType=job.SourceDocumentType AND source.TenantId=@TenantId AND l.PartyId=@PartyId)
                   OR EXISTS(SELECT 1 FROM dbo.Customers customer WHERE customer.PartyId=@PartyId AND customer.TenantId=@TenantId AND (
                     EXISTS(SELECT 1 FROM dbo.SalesDocuments sale WHERE sale.DocumentId=job.SourceDocumentId AND sale.CustomerId=customer.CustomerId)
                     OR EXISTS(SELECT 1 FROM dbo.SalesReturns ret WHERE ret.ReturnId=job.SourceDocumentId AND ret.CustomerId=customer.CustomerId)
                     OR EXISTS(SELECT 1 FROM dbo.CustomerPayments payment WHERE payment.PaymentId=job.SourceDocumentId AND payment.CustomerId=customer.CustomerId)
                     OR EXISTS(SELECT 1 FROM dbo.Receivables balance WHERE balance.ReceivableId=draft.SubledgerId AND draft.SubledgerKind=N'Receivable' AND balance.CustomerId=customer.CustomerId)))
                   OR EXISTS(SELECT 1 FROM dbo.Suppliers supplier WHERE supplier.PartyId=@PartyId AND supplier.TenantId=@TenantId AND (
                     EXISTS(SELECT 1 FROM dbo.Expenses expense WHERE expense.ExpenseId=job.SourceDocumentId AND expense.SupplierId=supplier.SupplierId)
                     OR EXISTS(SELECT 1 FROM dbo.GoodsReceipts receipt WHERE receipt.GoodsReceiptId=job.SourceDocumentId AND receipt.SupplierId=supplier.SupplierId)
                     OR EXISTS(SELECT 1 FROM dbo.SupplierPayments payment WHERE payment.PaymentId=job.SourceDocumentId AND payment.SupplierId=supplier.SupplierId)
                     OR EXISTS(SELECT 1 FROM dbo.Payables balance WHERE balance.PayableId=draft.SubledgerId AND draft.SubledgerKind=N'Payable' AND balance.SupplierId=supplier.SupplierId))));
            """;
}
