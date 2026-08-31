CREATE TABLE sales.SalesDocumentServiceLines
(
    DocumentId UNIQUEIDENTIFIER NOT NULL,
    LineNumber INT NOT NULL,
    BillableServiceId UNIQUEIDENTIFIER NOT NULL,
    ServiceCode NVARCHAR(48) NOT NULL,
    Description NVARCHAR(500) NOT NULL,
    UnitCode NVARCHAR(8) NOT NULL,
    TaxCode NVARCHAR(16) NOT NULL,
    TaxName NVARCHAR(80) NOT NULL,
    TaxRate DECIMAL(9,6) NOT NULL,
    Quantity DECIMAL(19,6) NOT NULL,
    UnitPrice DECIMAL(19,4) NOT NULL,
    DiscountAmount DECIMAL(19,4) NOT NULL,
    UntaxedAmount DECIMAL(19,4) NOT NULL,
    TaxAmount DECIMAL(19,4) NOT NULL,
    LineTotal DECIMAL(19,4) NOT NULL,
    CONSTRAINT PK_SalesDocumentServiceLines PRIMARY KEY(DocumentId,LineNumber),
    CONSTRAINT FK_SalesDocumentServiceLines_Document FOREIGN KEY(DocumentId) REFERENCES dbo.SalesDocuments(DocumentId),
    CONSTRAINT FK_SalesDocumentServiceLines_Service FOREIGN KEY(BillableServiceId) REFERENCES billing.BillableServices(BillableServiceId),
    CONSTRAINT CK_SalesDocumentServiceLines_Values CHECK(LineNumber>0 AND Quantity>0 AND UnitPrice>=0 AND DiscountAmount>=0 AND UntaxedAmount>=0 AND TaxAmount>=0 AND LineTotal>0 AND TaxRate BETWEEN 0 AND 100)
);
GO

CREATE INDEX IX_SalesDocumentServiceLines_Service_Document
  ON sales.SalesDocumentServiceLines(BillableServiceId,DocumentId);
GO

CREATE TABLE sales.SalesDocumentServiceFiscalSnapshots
(
    DocumentId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_SalesDocumentServiceFiscalSnapshots PRIMARY KEY,
    SnapshotJson NVARCHAR(MAX) NOT NULL,
    PayloadHash BINARY(32) NOT NULL,
    Environment TINYINT NOT NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT FK_SalesDocumentServiceFiscalSnapshots_Document FOREIGN KEY(DocumentId) REFERENCES dbo.FiscalDocuments(DocumentId),
    CONSTRAINT CK_SalesDocumentServiceFiscalSnapshots_Environment CHECK(Environment IN(1,2))
);
GO
