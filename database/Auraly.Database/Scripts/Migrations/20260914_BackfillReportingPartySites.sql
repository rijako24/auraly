SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

UPDATE document
SET CustomerPartySiteId=orders.PartySiteId
FROM dbo.SalesDocuments document
JOIN dbo.OrderInvoiceLinks link
  ON link.DocumentId=document.DocumentId
 AND link.BusinessId=document.BusinessId
JOIN dbo.Orders orders
  ON orders.OrderId=link.OrderId
 AND orders.BusinessId=document.BusinessId
 AND orders.CustomerId=document.CustomerId
WHERE document.CustomerPartySiteId IS NULL
  AND orders.PartySiteId IS NOT NULL;

UPDATE receivable
SET PartySiteId=document.CustomerPartySiteId
FROM dbo.Receivables receivable
JOIN dbo.SalesDocuments document
  ON document.DocumentId=receivable.SourceDocumentId
 AND document.BusinessId=receivable.BusinessId
 AND document.CustomerId=receivable.CustomerId
WHERE receivable.PartySiteId IS NULL
  AND document.CustomerPartySiteId IS NOT NULL;

UPDATE report
SET PartySiteId=document.CustomerPartySiteId
FROM reporting.SalesReportDocuments report
JOIN dbo.SalesDocuments document ON document.DocumentId=report.DocumentId
WHERE report.PartySiteId IS NULL AND document.CustomerPartySiteId IS NOT NULL;

UPDATE fact
SET PartySiteId=document.CustomerPartySiteId
FROM reporting.SalesReportLineFacts fact
JOIN dbo.SalesDocuments document ON document.DocumentId=fact.OriginalSaleDocumentId
WHERE fact.PartySiteId IS NULL AND document.CustomerPartySiteId IS NOT NULL;

UPDATE fact
SET PartySiteId=document.CustomerPartySiteId
FROM reporting.ServiceInvoiceFacts fact
JOIN dbo.SalesDocuments document ON document.DocumentId=fact.DocumentId
WHERE fact.PartySiteId IS NULL AND document.CustomerPartySiteId IS NOT NULL;

UPDATE line
SET PartySiteId=header.PartySiteId
FROM reporting.ServiceInvoiceLineFacts line
JOIN reporting.ServiceInvoiceFacts header ON header.DocumentId=line.DocumentId
WHERE line.PartySiteId IS NULL AND header.PartySiteId IS NOT NULL;

UPDATE fact
SET PartySiteId=orders.PartySiteId
FROM reporting.CommercialReportOrderFacts fact
JOIN dbo.Orders orders ON orders.OrderId=fact.OrderId
WHERE fact.PartySiteId IS NULL AND orders.PartySiteId IS NOT NULL;

IF EXISTS(
    SELECT 1
    FROM reporting.CommercialReportOrderFacts fact
    JOIN dbo.Orders orders ON orders.OrderId=fact.OrderId
    WHERE orders.Source=1 AND fact.PartySiteId IS NULL)
    THROW 51322, 'Seller order reporting facts still exist without a customer site.', 1;

IF EXISTS(
    SELECT 1
    FROM dbo.OrderInvoiceLinks link
    JOIN dbo.Orders orders ON orders.OrderId=link.OrderId AND orders.BusinessId=link.BusinessId
    JOIN dbo.SalesDocuments document
      ON document.DocumentId=link.DocumentId AND document.BusinessId=link.BusinessId
    WHERE orders.Source=1 AND document.CustomerPartySiteId IS NULL)
    THROW 51323, 'An invoiced seller order still has no customer site on its sales document.', 1;

IF EXISTS(
    SELECT 1
    FROM dbo.Receivables receivable
    JOIN dbo.OrderInvoiceLinks link
      ON link.DocumentId=receivable.SourceDocumentId AND link.BusinessId=receivable.BusinessId
    JOIN dbo.Orders orders ON orders.OrderId=link.OrderId AND orders.BusinessId=link.BusinessId
    WHERE orders.Source=1 AND receivable.PartySiteId IS NULL)
    THROW 51324, 'A seller-order receivable still has no customer site.', 1;

COMMIT TRANSACTION;
