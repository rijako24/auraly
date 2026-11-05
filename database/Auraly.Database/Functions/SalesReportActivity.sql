CREATE FUNCTION reporting.SalesReportActivity(
    @TenantId UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER,@From DATE,@To DATE,
    @SourceDocumentId UNIQUEIDENTIFIER,@OriginalSaleDocumentId UNIQUEIDENTIFIER,@WholeDocuments BIT)
RETURNS TABLE
AS RETURN
(
    WITH ProductLines AS
    (
      SELECT * FROM reporting.SalesReportLineFacts
      WHERE @WholeDocuments=0 AND TenantId=@TenantId AND BusinessId=@BusinessId AND BusinessLocalDate BETWEEN @From AND @To
        AND (@SourceDocumentId IS NULL OR SourceDocumentId=@SourceDocumentId)
        AND (@OriginalSaleDocumentId IS NULL OR OriginalSaleDocumentId=@OriginalSaleDocumentId)
    ), ServiceLines AS
    (
      SELECT * FROM reporting.ServiceSalesReportLineFacts
      WHERE @WholeDocuments=0 AND TenantId=@TenantId AND BusinessId=@BusinessId AND BusinessLocalDate BETWEEN @From AND @To
        AND (@SourceDocumentId IS NULL OR SourceDocumentId=@SourceDocumentId)
        AND (@OriginalSaleDocumentId IS NULL OR OriginalSaleDocumentId=@OriginalSaleDocumentId)
    ), LineTaxes AS
    (
      SELECT SourceDocumentId,SourceDocumentType,TaxCode,TaxRate,CONVERT(decimal(19,4),SUM(UntaxedAmount)) Base,
        CONVERT(decimal(19,4),SUM(TaxAmount)) Tax,CONVERT(decimal(19,4),SUM(TotalAmount)) Total
      FROM (SELECT SourceDocumentId,SourceDocumentType,TaxCode,TaxRate,UntaxedAmount,TaxAmount,TotalAmount FROM ProductLines
        UNION ALL SELECT SourceDocumentId,SourceDocumentType,TaxCode,TaxRate,UntaxedAmount,TaxAmount,TotalAmount FROM ServiceLines) l
      GROUP BY SourceDocumentId,SourceDocumentType,TaxCode,TaxRate
    ), ReturnCosts AS
    (
      SELECT SourceDocumentId,CONVERT(decimal(19,6),SUM(Quantity)) Quantity,CONVERT(decimal(19,4),SUM(Cost)) Cost,SUM(Missing) Missing
      FROM(
        SELECT SourceDocumentId,Quantity,RecognizedCostAmount Cost,CONVERT(bigint,1-CONVERT(int,CostAvailable)) Missing
        FROM reporting.SalesReportLineFacts
        WHERE @WholeDocuments=1 AND TenantId=@TenantId AND BusinessId=@BusinessId AND BusinessLocalDate BETWEEN @From AND @To
          AND SourceDocumentType=N'SalesReturn' AND (@SourceDocumentId IS NULL OR SourceDocumentId=@SourceDocumentId)
          AND (@OriginalSaleDocumentId IS NULL OR OriginalSaleDocumentId=@OriginalSaleDocumentId)
        UNION ALL
        SELECT SourceDocumentId,CONVERT(decimal(19,6),0),CONVERT(decimal(19,4),0),CONVERT(bigint,1)
        FROM reporting.ServiceSalesReportLineFacts
        WHERE @WholeDocuments=1 AND TenantId=@TenantId AND BusinessId=@BusinessId AND BusinessLocalDate BETWEEN @From AND @To
          AND SourceDocumentType=N'SalesReturn' AND (@SourceDocumentId IS NULL OR SourceDocumentId=@SourceDocumentId)
          AND (@OriginalSaleDocumentId IS NULL OR OriginalSaleDocumentId=@OriginalSaleDocumentId)
      ) costs GROUP BY SourceDocumentId
    ), WholeReturns AS
    (
      SELECT SourceDocumentId,OriginalSaleDocumentId,BusinessLocalDate,MAX(BusinessLocalHour) BusinessLocalHour,
        MAX(OccurredAt) OccurredAt,MAX(ProjectedAt) ProjectedAt,CONVERT(decimal(19,4),SUM(TaxableAmount)) Base,
        CONVERT(decimal(19,4),SUM(TaxAmount)) Tax,CONVERT(decimal(19,4),SUM(TotalAmount)) Total
      FROM reporting.SalesReportTaxFacts
      WHERE @WholeDocuments=1 AND TenantId=@TenantId AND BusinessId=@BusinessId AND BusinessLocalDate BETWEEN @From AND @To
        AND SourceDocumentType=N'SalesReturn' AND (@SourceDocumentId IS NULL OR SourceDocumentId=@SourceDocumentId)
        AND (@OriginalSaleDocumentId IS NULL OR OriginalSaleDocumentId=@OriginalSaleDocumentId)
      GROUP BY SourceDocumentId,OriginalSaleDocumentId,BusinessLocalDate
    )
    SELECT p.FactId,p.SourceDocumentId,p.SourceDocumentType,p.SourceLineNumber,p.OriginalSaleDocumentId,
      p.MovementType,p.OccurredAt,p.BusinessLocalDate,p.WarehouseId,p.SellerId,p.CustomerId,
      p.ProductId,p.ProductCode ItemCode,p.ProductName ItemName,p.CategoryId,p.CategoryName,p.SupplierId,p.SupplierName,
      p.WaiterPartyId,p.WaiterName,p.UnitCode,CONVERT(NVARCHAR(16),N'Product') ItemKind,
      p.Quantity,p.GrossAmount,p.DiscountAmount,p.UntaxedAmount,p.TaxCode,p.TaxRate,p.TaxAmount,p.TotalAmount,
      CASE WHEN p.CostAvailable=1 THEN p.RecognizedCostAmount END RecognizedCostAmount,
      p.CompositionKind,p.CompositionDetailJson,p.ProjectedAt,p.BusinessLocalHour,@TenantId TenantId,@BusinessId BusinessId,
      CONVERT(bigint,1-CONVERT(int,p.CostAvailable)) MissingCostLines,p.CategoryHierarchyJson
    FROM ProductLines p
    UNION ALL
    SELECT s.FactId,s.SourceDocumentId,s.SourceDocumentType,s.SourceLineNumber,s.OriginalSaleDocumentId,
      s.MovementType,s.OccurredAt,s.BusinessLocalDate,NULL,s.SellerId,s.CustomerId,
      NULL,s.ServiceCode,s.ServiceName,NULL,NULL,NULL,NULL,NULL,NULL,s.UnitCode,N'Service',
      s.Quantity,s.GrossAmount,s.DiscountAmount,s.UntaxedAmount,s.TaxCode,s.TaxRate,s.TaxAmount,s.TotalAmount,
      NULL,NULL,NULL,s.ProjectedAt,s.BusinessLocalHour,@TenantId,@BusinessId,CONVERT(bigint,1),NULL FROM ServiceLines s
    UNION ALL
    SELECT t.SourceDocumentId,t.SourceDocumentId,t.SourceDocumentType,0,COALESCE(t.OriginalSaleDocumentId,t.SourceDocumentId),
      CASE WHEN t.SourceDocumentType=N'SalesReturn' THEN N'Return' ELSE N'Sale' END,
      t.OccurredAt,t.BusinessLocalDate,d.WarehouseId,d.SellerId,d.CustomerId,
      NULL,N'',N'Cargos facturados sin atribución a producto',NULL,NULL,NULL,NULL,NULL,NULL,NULL,N'Charge',
      0,t.TaxableAmount-COALESCE(l.Base,0),0,t.TaxableAmount-COALESCE(l.Base,0),t.TaxCode,t.TaxRate,
      t.TaxAmount-COALESCE(l.Tax,0),t.TotalAmount-COALESCE(l.Total,0),0,NULL,NULL,t.ProjectedAt,t.BusinessLocalHour,@TenantId,@BusinessId,CONVERT(bigint,0),NULL
    FROM reporting.SalesReportTaxFacts t
    JOIN reporting.SalesReportDocuments d ON d.DocumentId=COALESCE(t.OriginalSaleDocumentId,t.SourceDocumentId)
      AND d.TenantId=@TenantId AND d.BusinessId=@BusinessId
    LEFT JOIN LineTaxes l ON l.SourceDocumentId=t.SourceDocumentId AND l.SourceDocumentType=t.SourceDocumentType
      AND l.TaxCode=t.TaxCode AND l.TaxRate=t.TaxRate
    WHERE @WholeDocuments=0 AND t.TenantId=@TenantId AND t.BusinessId=@BusinessId AND t.BusinessLocalDate BETWEEN @From AND @To
      AND (@SourceDocumentId IS NULL OR t.SourceDocumentId=@SourceDocumentId)
      AND (@OriginalSaleDocumentId IS NULL OR COALESCE(t.OriginalSaleDocumentId,t.SourceDocumentId)=@OriginalSaleDocumentId)
      AND (t.TaxableAmount<>COALESCE(l.Base,0) OR t.TaxAmount<>COALESCE(l.Tax,0) OR t.TotalAmount<>COALESCE(l.Total,0))
    UNION ALL
    -- At whole-document grain the canonical header already owns its totals.
    -- Product/party subsets and line detail always use the fact branches above.
    SELECT d.DocumentId,d.DocumentId,d.DocumentType,0,d.DocumentId,N'Sale',d.IssuedAt,d.BusinessLocalDate,
      d.WarehouseId,d.SellerId,d.CustomerId,NULL,N'',d.DocumentNumber,NULL,NULL,NULL,NULL,NULL,NULL,NULL,
      CASE WHEN d.DocumentType=N'ServiceInvoice' THEN N'Service' ELSE N'Product' END,
      d.ProductQuantity,d.GrossAmount,d.DiscountAmount,d.UntaxedAmount,NULL,NULL,d.TaxAmount,
      CONVERT(decimal(19,4),d.TotalAmount-d.RoundingAdjustmentAmount),d.RecognizedCostAmount,NULL,NULL,d.ProjectedAt,d.BusinessLocalHour,
      @TenantId,@BusinessId,d.MissingCostLineCount,NULL
    FROM reporting.SalesReportDocuments d
    WHERE @WholeDocuments=1 AND d.TenantId=@TenantId AND d.BusinessId=@BusinessId AND d.BusinessLocalDate BETWEEN @From AND @To
      AND (@SourceDocumentId IS NULL OR d.DocumentId=@SourceDocumentId)
      AND (@OriginalSaleDocumentId IS NULL OR d.DocumentId=@OriginalSaleDocumentId)
    UNION ALL
    SELECT t.SourceDocumentId,t.SourceDocumentId,N'SalesReturn',0,t.OriginalSaleDocumentId,N'Return',t.OccurredAt,t.BusinessLocalDate,
      d.WarehouseId,d.SellerId,d.CustomerId,NULL,N'',d.DocumentNumber,NULL,NULL,NULL,NULL,NULL,NULL,NULL,
      CASE WHEN d.DocumentType=N'ServiceInvoice' THEN N'Service' ELSE N'Product' END,
      COALESCE(c.Quantity,0),t.Base,0,t.Base,NULL,NULL,t.Tax,t.Total,
      CASE WHEN COALESCE(c.Missing,0)=0 THEN COALESCE(c.Cost,0) END,NULL,NULL,t.ProjectedAt,t.BusinessLocalHour,
      @TenantId,@BusinessId,COALESCE(c.Missing,0),NULL
    FROM WholeReturns t JOIN reporting.SalesReportDocuments d ON d.DocumentId=t.OriginalSaleDocumentId
      AND d.TenantId=@TenantId AND d.BusinessId=@BusinessId
    LEFT JOIN ReturnCosts c ON c.SourceDocumentId=t.SourceDocumentId
);
