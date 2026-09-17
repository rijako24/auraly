SET XACT_ABORT ON;
BEGIN TRANSACTION;

DELETE reporting.SalesReportingJobs
WHERE SourceDocumentType IN
(
    N'ServiceInvoice',
    N'SellerOrder',
    N'RouteVisit',
    N'CommercialCoveragePlan'
);

DROP TABLE IF EXISTS reporting.CommercialCoverageAssignmentFacts;
DROP TABLE IF EXISTS reporting.CommercialReportOrderFacts;
DROP TABLE IF EXISTS reporting.CommercialReportVisitFacts;
DROP TABLE IF EXISTS reporting.ServiceInvoiceLineFacts;
DROP TABLE IF EXISTS reporting.ServiceInvoiceFacts;

COMMIT TRANSACTION;
GO
