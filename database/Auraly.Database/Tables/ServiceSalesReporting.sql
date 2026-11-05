CREATE TABLE reporting.ServiceSalesReportLineFacts
(
    FactId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    TenantId UNIQUEIDENTIFIER NOT NULL,
    BusinessId UNIQUEIDENTIFIER NOT NULL,
    SourceDocumentId UNIQUEIDENTIFIER NOT NULL,
    SourceDocumentType NVARCHAR(32) NOT NULL,
    SourceLineNumber INT NOT NULL,
    OriginalSaleDocumentId UNIQUEIDENTIFIER NOT NULL,
    MovementType NVARCHAR(24) NOT NULL,
    OccurredAt DATETIMEOFFSET(7) NOT NULL,
    BusinessLocalDate DATE NOT NULL,
    BusinessLocalHour TINYINT NULL,
    SellerId UNIQUEIDENTIFIER NULL,
    CustomerId UNIQUEIDENTIFIER NULL,
    BillableServiceId UNIQUEIDENTIFIER NOT NULL,
    ServiceCode NVARCHAR(80) NOT NULL,
    ServiceName NVARCHAR(500) NOT NULL,
    UnitCode NVARCHAR(24) NOT NULL,
    Quantity DECIMAL(19,6) NOT NULL,
    GrossAmount DECIMAL(19,4) NOT NULL,
    DiscountAmount DECIMAL(19,4) NOT NULL,
    UntaxedAmount DECIMAL(19,4) NOT NULL,
    TaxCode NVARCHAR(16) NOT NULL,
    TaxRate DECIMAL(9,6) NOT NULL,
    TaxAmount DECIMAL(19,4) NOT NULL,
    TotalAmount DECIMAL(19,4) NOT NULL,
    ProjectionVersion SMALLINT NOT NULL,
    ProjectedAt DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT UQ_ServiceSalesReportLineFacts_Source UNIQUE(SourceDocumentId,SourceDocumentType,SourceLineNumber),
    CONSTRAINT FK_ServiceSalesReportLineFacts_Tenant FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(TenantId),
    CONSTRAINT FK_ServiceSalesReportLineFacts_Business FOREIGN KEY(BusinessId) REFERENCES dbo.Businesses(BusinessId),
    CONSTRAINT CK_ServiceSalesReportLineFacts_Sign CHECK(
      (MovementType=N'Sale' AND Quantity>0 AND TotalAmount>=0) OR
      (MovementType=N'Return' AND Quantity<0 AND TotalAmount<=0))
);
GO
CREATE INDEX IX_ServiceSalesReportLineFacts_Business_Date ON reporting.ServiceSalesReportLineFacts(BusinessId,BusinessLocalDate,OriginalSaleDocumentId)
  INCLUDE(CustomerId,SellerId,UntaxedAmount,TaxAmount,TotalAmount,Quantity,MovementType);
GO
