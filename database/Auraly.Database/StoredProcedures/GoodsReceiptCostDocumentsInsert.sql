CREATE PROCEDURE [purchasing].[GoodsReceiptCostDocumentsInsert]
    @GoodsReceiptId UNIQUEIDENTIFIER,
    @DocumentsJson NVARCHAR(MAX),
    @Now DATETIMEOFFSET(7),
    @ReferencedDocumentsJson NVARCHAR(MAX) = N'[]',
    @RevisionId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @RevisionId IS NOT NULL AND NOT EXISTS
    (
      SELECT 1 FROM purchasing.GoodsReceiptCostRevisions revision
      WHERE revision.RevisionId=@RevisionId AND revision.GoodsReceiptId=@GoodsReceiptId
    ) THROW 51741,'The cost revision does not belong to this receipt.',1;
    -- Acceptance owns the transaction and has already validated the scope and
    -- calculated the immutable snapshot. This participant only persists it.
    DECLARE @Documents TABLE
    (
        CostDocumentId UNIQUEIDENTIFIER PRIMARY KEY, SupplierId UNIQUEIDENTIFIER,
        PartySiteId UNIQUEIDENTIFIER, PurchaseEvidenceType NVARCHAR(MAX), DocumentNumber NVARCHAR(MAX),
        IssuedAt DATETIMEOFFSET(7), CreatesPayable BIT, DueDate DATETIMEOFFSET(7),
        CurrencyCode NVARCHAR(MAX), ExchangeRate DECIMAL(19,8), ExchangeRateDate DATE,
        ExchangeRateSource NVARCHAR(MAX), NetAmount DECIMAL(19,4), TaxAmount DECIMAL(19,4),
        GrandTotal DECIMAL(19,4), FunctionalNetAmount DECIMAL(19,4),
        FunctionalTaxAmount DECIMAL(19,4), FunctionalGrandTotal DECIMAL(19,4), LinesJson NVARCHAR(MAX)
    );
    INSERT @Documents
    SELECT CostDocumentId,SupplierId,PartySiteId,PurchaseEvidenceType,DocumentNumber,
        IssuedAt,CreatesPayable,DueDate,CurrencyCode,ExchangeRate,ExchangeRateDate,
        ExchangeRateSource,NetAmount,TaxAmount,GrandTotal,FunctionalNetAmount,
        FunctionalTaxAmount,FunctionalGrandTotal,LinesJson
    FROM OPENJSON(@DocumentsJson) WITH
    (
        CostDocumentId UNIQUEIDENTIFIER, SupplierId UNIQUEIDENTIFIER, PartySiteId UNIQUEIDENTIFIER,
        PurchaseEvidenceType NVARCHAR(MAX), DocumentNumber NVARCHAR(MAX), IssuedAt DATETIMEOFFSET(7),
        CreatesPayable BIT, DueDate DATETIMEOFFSET(7), CurrencyCode NVARCHAR(MAX), ExchangeRate DECIMAL(19,8),
        ExchangeRateDate DATE, ExchangeRateSource NVARCHAR(MAX), NetAmount DECIMAL(19,4), TaxAmount DECIMAL(19,4),
        GrandTotal DECIMAL(19,4), FunctionalNetAmount DECIMAL(19,4), FunctionalTaxAmount DECIMAL(19,4),
        FunctionalGrandTotal DECIMAL(19,4), LinesJson NVARCHAR(MAX) 'strict $.Lines' AS JSON
    );
    DECLARE @Lines TABLE
    (
        CostDocumentId UNIQUEIDENTIFIER, LineNumber INT, CostKind NVARCHAR(MAX), Description NVARCHAR(MAX),
        Amount DECIMAL(19,4), TaxableBaseAmount DECIMAL(19,4), TaxCode NVARCHAR(MAX), TaxRate DECIMAL(9,6),
        TaxAmount DECIMAL(19,4), TaxTreatment NVARCHAR(MAX), CostTreatment NVARCHAR(MAX), AllocationMethod NVARCHAR(MAX),
        FunctionalAmount DECIMAL(19,4), FunctionalTaxableBaseAmount DECIMAL(19,4),
        FunctionalTaxAmount DECIMAL(19,4), FunctionalDocumentAmount DECIMAL(19,4), AllocationsJson NVARCHAR(MAX),
        PRIMARY KEY(CostDocumentId,LineNumber)
    );
    INSERT @Lines
    SELECT document.CostDocumentId,line.LineNumber,line.CostKind,line.Description,
        line.Amount,line.TaxableBaseAmount,line.TaxCode,line.TaxRate,line.TaxAmount,
        line.TaxTreatment,line.CostTreatment,line.AllocationMethod,line.FunctionalAmount,
        line.FunctionalTaxableBaseAmount,line.FunctionalTaxAmount,line.FunctionalDocumentAmount,line.AllocationsJson
    FROM @Documents document CROSS APPLY OPENJSON(document.LinesJson) WITH
    (
        LineNumber INT, CostKind NVARCHAR(MAX), Description NVARCHAR(MAX), Amount DECIMAL(19,4),
        TaxableBaseAmount DECIMAL(19,4), TaxCode NVARCHAR(MAX), TaxRate DECIMAL(9,6), TaxAmount DECIMAL(19,4),
        TaxTreatment NVARCHAR(MAX), CostTreatment NVARCHAR(MAX), AllocationMethod NVARCHAR(MAX),
        FunctionalAmount DECIMAL(19,4), FunctionalTaxableBaseAmount DECIMAL(19,4),
        FunctionalTaxAmount DECIMAL(19,4), FunctionalDocumentAmount DECIMAL(19,4),
        AllocationsJson NVARCHAR(MAX) 'strict $.Allocations' AS JSON
    ) line;

    INSERT purchasing.GoodsReceiptCostDocuments
        (CostDocumentId,GoodsReceiptId,SupplierId,PartySiteId,PurchaseEvidenceType,DocumentNumber,
         IssuedAt,CreatesPayable,DueDate,CurrencyCode,ExchangeRate,ExchangeRateDate,
         ExchangeRateSource,NetAmount,TaxAmount,GrandTotal,FunctionalNetAmount,
         FunctionalTaxAmount,FunctionalGrandTotal,CreatedAt)
    SELECT CostDocumentId,@GoodsReceiptId,SupplierId,PartySiteId,PurchaseEvidenceType,DocumentNumber,
        IssuedAt,CreatesPayable,DueDate,CurrencyCode,ExchangeRate,ExchangeRateDate,
        ExchangeRateSource,NetAmount,TaxAmount,GrandTotal,FunctionalNetAmount,
        FunctionalTaxAmount,FunctionalGrandTotal,@Now FROM @Documents;
    IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @Documents)
        THROW 51736,'The complete receipt cost documents were not persisted.',1;

    INSERT purchasing.GoodsReceiptCostLines
        (CostDocumentId,LineNumber,CostKind,DescriptionSnapshot,Amount,TaxableBaseAmount,
         TaxCode,TaxRate,TaxAmount,TaxTreatment,CostTreatment,AllocationMethod,
         FunctionalAmount,FunctionalTaxableBaseAmount,FunctionalTaxAmount,FunctionalDocumentAmount)
    SELECT CostDocumentId,LineNumber,CostKind,Description,Amount,TaxableBaseAmount,
        TaxCode,TaxRate,TaxAmount,TaxTreatment,CostTreatment,AllocationMethod,
        FunctionalAmount,FunctionalTaxableBaseAmount,FunctionalTaxAmount,FunctionalDocumentAmount FROM @Lines;
    IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @Lines)
        THROW 51737,'The complete receipt cost lines were not persisted.',1;

    INSERT purchasing.GoodsReceiptCostAllocations
        (CostDocumentId,CostLineNumber,GoodsReceiptId,ReceiptLineNumber,
         AllocationMethod,AllocationFactor,FunctionalAmount)
    SELECT line.CostDocumentId,line.LineNumber,@GoodsReceiptId,allocation.ReceiptLineNumber,
        allocation.AllocationMethod,allocation.Factor,allocation.FunctionalAmount
    FROM @Lines line CROSS APPLY OPENJSON(line.AllocationsJson) WITH
    (
        ReceiptLineNumber INT, AllocationMethod NVARCHAR(MAX),
        Factor DECIMAL(19,12), FunctionalAmount DECIMAL(19,4)
    ) allocation;

    DECLARE @References TABLE(AssociationId uniqueidentifier PRIMARY KEY,
      EntryId uniqueidentifier,Mode nvarchar(16),LinesJson nvarchar(max));
    INSERT @References SELECT AssociationId,EntryId,Mode,LinesJson
    FROM OPENJSON(@ReferencedDocumentsJson) WITH(AssociationId uniqueidentifier,
      EntryId uniqueidentifier,Mode nvarchar(16),LinesJson nvarchar(max) '$.Lines' AS JSON);
    IF EXISTS(SELECT 1 FROM @References requested
      WHERE NOT EXISTS(SELECT 1 FROM dbo.AccountingEntries entry
        JOIN dbo.GoodsReceipts receipt ON receipt.GoodsReceiptId=@GoodsReceiptId AND receipt.BusinessId=entry.BusinessId
        JOIN dbo.Businesses business ON business.BusinessId=receipt.BusinessId AND business.TenantId=entry.TenantId
        WHERE entry.EntryId=requested.EntryId))
      THROW 51738,'A referenced cost voucher belongs to another scope.',1;
    INSERT purchasing.GoodsReceiptCostDocuments
      (CostDocumentId,GoodsReceiptId,SourceEntryId,AssociationMode,CostRevisionId,CreatedAt)
    SELECT AssociationId,@GoodsReceiptId,EntryId,Mode,@RevisionId,@Now FROM @References;
    DECLARE @ReferencedLines TABLE(AssociationId uniqueidentifier,EntryId uniqueidentifier,
      EntryLineNumber int,FunctionalAmount decimal(19,4),AllocationMethod nvarchar(16),AllocationsJson nvarchar(max),
      PRIMARY KEY(AssociationId,EntryLineNumber));
    INSERT @ReferencedLines
    SELECT document.AssociationId,document.EntryId,line.EntryLineNumber,line.FunctionalAmount,
      line.AllocationMethod,line.AllocationsJson
    FROM @References document CROSS APPLY OPENJSON(document.LinesJson) WITH(
      EntryLineNumber int,FunctionalAmount decimal(19,4),AllocationMethod nvarchar(16),
      AllocationsJson nvarchar(max) '$.Allocations' AS JSON) line;
    IF EXISTS(SELECT 1 FROM @References document WHERE
      (document.Mode=N'Associate' AND EXISTS(SELECT 1 FROM @ReferencedLines line WHERE line.AssociationId=document.AssociationId))
      OR (document.Mode=N'Distribute' AND NOT EXISTS(SELECT 1 FROM @ReferencedLines line WHERE line.AssociationId=document.AssociationId)))
      THROW 51739,'The cost association mode and its assignments differ.',1;
    INSERT purchasing.GoodsReceiptCostLines
      (CostDocumentId,LineNumber,SourceEntryId,SourceEntryLineNumber,FunctionalAmount,AllocationMethod)
    SELECT AssociationId,EntryLineNumber,EntryId,EntryLineNumber,FunctionalAmount,AllocationMethod FROM @ReferencedLines;
    INSERT purchasing.GoodsReceiptCostAllocations
      (CostDocumentId,CostLineNumber,GoodsReceiptId,ReceiptLineNumber,AllocationMethod,AllocationFactor,FunctionalAmount)
    SELECT line.AssociationId,line.EntryLineNumber,@GoodsReceiptId,allocation.ReceiptLineNumber,
      line.AllocationMethod,allocation.Factor,allocation.FunctionalAmount
    FROM @ReferencedLines line CROSS APPLY OPENJSON(line.AllocationsJson) WITH(
      ReceiptLineNumber int,Factor decimal(19,12),FunctionalAmount decimal(19,4)) allocation;
    IF EXISTS(SELECT 1 FROM @ReferencedLines line WHERE line.FunctionalAmount<>(
      SELECT COALESCE(SUM(allocation.FunctionalAmount),0) FROM purchasing.GoodsReceiptCostAllocations allocation
      WHERE allocation.CostDocumentId=line.AssociationId AND allocation.CostLineNumber=line.EntryLineNumber))
      THROW 51740,'The referenced cost assignments do not reconcile.',1;
END;
