SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Run before DACPAC planning. Historical evidence is copied once into the
-- invoice; Reporting never performs this valuation or reads the ledger.
IF OBJECT_ID(N'dbo.SalesDocumentLines',N'U') IS NULL RETURN;

BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.SalesDocumentLines',N'RecognizedCostAmount') IS NULL
    EXEC(N'ALTER TABLE dbo.SalesDocumentLines ADD RecognizedCostAmount decimal(19,4) NULL;');

EXEC sys.sp_executesql N'
    -- The retired habilitation route is fiscal-only, as enforced by the sale
    -- handler. Older Reporting backfills incorrectly queued these roots.
    SELECT payload.DocumentId,payload.DocumentType,payload.BusinessId
    INTO #FiscalOnlySources
    FROM dbo.DocumentProcessingPayloads payload
    JOIN dbo.SalesDocuments sale ON sale.DocumentId=payload.DocumentId
      AND sale.DocumentType=payload.DocumentType AND sale.BusinessId=payload.BusinessId
      AND sale.PayloadHash=payload.PayloadHash
    JOIN dbo.Businesses business ON business.BusinessId=sale.BusinessId
    WHERE payload.DocumentType IN(N''SalesReceipt'',N''SalesInvoice'')
      AND payload.PayloadHash=HASHBYTES(''SHA2_256'',CONVERT(varchar(max),payload.PayloadJson COLLATE Latin1_General_100_BIN2_UTF8))
      AND TRY_CONVERT(uniqueidentifier,COALESCE(JSON_VALUE(payload.PayloadJson,''$.documentId''),JSON_VALUE(payload.PayloadJson,''$.DocumentId'')))=payload.DocumentId
      AND TRY_CONVERT(uniqueidentifier,COALESCE(JSON_VALUE(payload.PayloadJson,''$.businessId''),JSON_VALUE(payload.PayloadJson,''$.BusinessId'')))=payload.BusinessId
      AND TRY_CONVERT(uniqueidentifier,COALESCE(JSON_VALUE(payload.PayloadJson,''$.tenantId''),JSON_VALUE(payload.PayloadJson,''$.TenantId'')))=business.TenantId
      AND EXISTS(SELECT 1 FROM OPENJSON(payload.PayloadJson) property
        WHERE property.[key] COLLATE Latin1_General_100_BIN2 IN(N''fiscalHabilitationOnly'',N''FiscalHabilitationOnly'')
          AND property.[type]=3 AND property.value=N''true'');

    IF EXISTS(SELECT 1 FROM #FiscalOnlySources source
      WHERE EXISTS(SELECT 1 FROM dbo.SalesDocumentLines line WHERE line.DocumentId=source.DocumentId)
        OR EXISTS(SELECT 1 FROM dbo.InventoryMovements movement WHERE movement.DocumentId=source.DocumentId AND movement.BusinessId=source.BusinessId)
        OR EXISTS(SELECT 1 FROM dbo.AccountingEntries entry WHERE entry.SourceDocumentId=source.DocumentId AND entry.BusinessId=source.BusinessId)
        OR EXISTS(SELECT 1 FROM dbo.Receivables debt WHERE debt.SourceDocumentId=source.DocumentId AND debt.BusinessId=source.BusinessId))
      THROW 51624,N''Una habilitación fiscal conserva efectos comerciales. Revisa su evidencia antes de migrar.'',1;

    DELETE report FROM reporting.SalesReportingJobs report JOIN #FiscalOnlySources source
      ON source.DocumentId=report.SourceDocumentId AND source.DocumentType=report.SourceDocumentType
        AND source.BusinessId=report.BusinessId;

    IF OBJECT_ID(N''reporting.SalesReportLineFacts'',N''U'') IS NOT NULL
      AND EXISTS(SELECT 1 FROM reporting.SalesReportLineFacts fact
        JOIN reporting.SalesReportDocuments document ON document.DocumentId=fact.SourceDocumentId
          AND document.DocumentType=fact.SourceDocumentType AND document.BusinessId=fact.BusinessId
        LEFT JOIN dbo.SalesDocumentLines line ON line.DocumentId=fact.SourceDocumentId
          AND line.LineNumber=fact.SourceLineNumber AND line.ProductId=fact.ProductId
        WHERE document.SourceDocumentAvailable=1 AND fact.SourceDocumentType IN(N''SalesReceipt'',N''SalesInvoice'')
          AND NOT EXISTS(SELECT 1 FROM #FiscalOnlySources source WHERE source.DocumentId=fact.SourceDocumentId
            AND source.DocumentType=fact.SourceDocumentType AND source.BusinessId=fact.BusinessId)
          AND line.DocumentId IS NULL)
      THROW 51624,N''Hay facturas proyectadas sin sus líneas operativas. Revisa el origen antes de migrar costos.'',1;

    SELECT line.DocumentId,line.LineNumber,line.ProductId,sale.BusinessId,
      sale.DocumentType,line.Quantity,line.UnitCostSnapshot,
      CONVERT(bit,CASE WHEN line.ProductRecipeVersionId IS NOT NULL
        OR line.CompositionKindSnapshot IN(N''Ingredients'',N''Combo'') THEN 1 ELSE 0 END) IsComposed,
      CONVERT(decimal(19,4),NULL) Cost
    INTO #Costs
    FROM dbo.SalesDocumentLines line WITH(UPDLOCK,HOLDLOCK)
    JOIN dbo.SalesDocuments sale ON sale.DocumentId=line.DocumentId
    JOIN dbo.Businesses business ON business.BusinessId=sale.BusinessId
    JOIN dbo.Products product ON product.ProductId=line.ProductId AND product.TenantId=business.TenantId
    WHERE line.RecognizedCostAmount IS NULL;
    CREATE UNIQUE CLUSTERED INDEX IX_Costs ON #Costs(DocumentId,LineNumber);

    IF OBJECT_ID(N''sales.RestaurantCheckoutLines'',N''U'') IS NOT NULL
      UPDATE cost SET Cost=mapping.RecognizedCostAmount
      FROM #Costs cost JOIN sales.RestaurantCheckoutLines mapping
        ON mapping.DocumentId=cost.DocumentId AND mapping.LineNumber=cost.LineNumber
        AND mapping.BusinessId=cost.BusinessId
      WHERE mapping.RecognizedCostAmount IS NOT NULL;

    IF OBJECT_ID(N''sales.RestaurantRecipeConsumptionLines'',N''U'') IS NOT NULL
      UPDATE cost SET Cost=evidence.Amount
      FROM #Costs cost
      JOIN (SELECT component.DocumentId,component.SaleLineNumber,
          SUM(component.RecognizedCostAmount) Amount
        FROM sales.RestaurantRecipeConsumptionLines component
        JOIN #Costs candidate ON candidate.DocumentId=component.DocumentId
          AND candidate.LineNumber=component.SaleLineNumber AND candidate.Cost IS NULL
        GROUP BY component.DocumentId,component.SaleLineNumber
        HAVING COUNT(*)=COUNT(component.RecognizedCostAmount)) evidence
        ON evidence.DocumentId=cost.DocumentId AND evidence.SaleLineNumber=cost.LineNumber
      WHERE cost.Cost IS NULL;

    UPDATE cost SET Cost=evidence.Amount
    FROM #Costs cost
    JOIN (SELECT candidate.DocumentId,candidate.LineNumber,SUM(-movement.ValueChange) Amount
      FROM #Costs candidate
      JOIN dbo.InventoryMovements movement
        ON movement.DocumentId=candidate.DocumentId AND movement.BusinessId=candidate.BusinessId
        AND movement.DocumentType=candidate.DocumentType AND movement.LineNumber=candidate.LineNumber
        AND movement.MovementType=N''Sale''
      WHERE candidate.Cost IS NULL AND candidate.IsComposed=0
      GROUP BY candidate.DocumentId,candidate.LineNumber
      HAVING COUNT(*)=COUNT(movement.ValueChange)) evidence
      ON evidence.DocumentId=cost.DocumentId AND evidence.LineNumber=cost.LineNumber;

    -- Where no valuation was recorded, retain the unit cost already frozen on
    -- the original simple invoice. Current inventory mode cannot prove its past.
    UPDATE #Costs SET Cost=TRY_CONVERT(decimal(19,4),ROUND(UnitCostSnapshot*Quantity,4))
      WHERE Cost IS NULL AND IsComposed=0 AND UnitCostSnapshot IS NOT NULL;

    IF EXISTS(SELECT 1 FROM #Costs WHERE Cost IS NULL OR Cost<0)
      THROW 51624,N''Hay líneas de factura sin evidencia de costo histórico. La migración no inventará costos.'',1;

    UPDATE line SET RecognizedCostAmount=cost.Cost
    FROM dbo.SalesDocumentLines line JOIN #Costs cost
      ON cost.DocumentId=line.DocumentId AND cost.LineNumber=line.LineNumber
    WHERE line.RecognizedCostAmount IS NULL;

    IF EXISTS(SELECT 1 FROM dbo.SalesDocumentLines WHERE RecognizedCostAmount IS NULL)
      THROW 51624,N''Hay líneas de factura sin un propietario o costo histórico verificable.'',1;

    -- Complete the reviewed nullability cutover before DacFx builds its plan.
    -- Its generic data-loss guard rejects any populated nullable-to-required
    -- column, even after every historical value has been recovered.
    IF COLUMNPROPERTY(OBJECT_ID(N''dbo.SalesDocumentLines''),N''RecognizedCostAmount'',''AllowsNull'')=1
      ALTER TABLE dbo.SalesDocumentLines ALTER COLUMN RecognizedCostAmount decimal(19,4) NOT NULL;
';
COMMIT;
