CREATE PROCEDURE [purchasing].[PurchaseOrderDraftSave]
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @PurchaseOrderId UNIQUEIDENTIFIER,
    @WarehouseId UNIQUEIDENTIFIER = NULL,
    @SupplierId UNIQUEIDENTIFIER = NULL,
    @OrderedAt DATETIMEOFFSET(7),
    @ExpectedAt DATETIMEOFFSET(7) = NULL,
    @CurrencyCode CHAR(3),
    @Notes NVARCHAR(1000) = NULL,
    @NetAmount DECIMAL(19,4),
    @TaxAmount DECIMAL(19,4),
    @GrandTotal DECIMAL(19,4),
    @ExpectedRowVersion VARBINARY(8) = NULL,
    @LinesJson NVARCHAR(MAX),
    @Now DATETIMEOFFSET(7)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
        THROW 51200,'Business is outside the tenant.',1;
    IF @WarehouseId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Warehouses WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId AND IsActive=1 AND IsSystem=0 AND UseForGoodsReceipts=1)
        THROW 51201,'Warehouse is invalid.',1;
    IF @SupplierId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Suppliers WHERE SupplierId=@SupplierId AND BusinessId=@BusinessId AND IsActive=1)
        THROW 51202,'Supplier is invalid.',1;
    IF EXISTS
    (
        SELECT 1 FROM OPENJSON(@LinesJson) WITH(ProductId uniqueidentifier '$.ProductId') input
        WHERE NOT EXISTS(SELECT 1 FROM dbo.Products p WHERE p.ProductId=input.ProductId AND p.IsActive=1 AND (p.TenantId=@TenantId OR p.BusinessId=@BusinessId))
    ) THROW 51202,'A product is invalid.',1;
    IF EXISTS(SELECT 1 FROM purchasing.PurchaseOrders WHERE PurchaseOrderId=@PurchaseOrderId AND BusinessId=@BusinessId)
        THROW 51203,'A confirmed purchase order is immutable.',1;

    IF EXISTS(SELECT 1 FROM purchasing.PurchaseOrderDrafts WITH(UPDLOCK,HOLDLOCK) WHERE PurchaseOrderId=@PurchaseOrderId AND BusinessId=@BusinessId)
    BEGIN
        IF @ExpectedRowVersion IS NULL OR NOT EXISTS(SELECT 1 FROM purchasing.PurchaseOrderDrafts WHERE PurchaseOrderId=@PurchaseOrderId AND RowVersion=@ExpectedRowVersion)
            THROW 51204,'The purchase-order draft changed in another session.',1;
        UPDATE purchasing.PurchaseOrderDrafts
        SET WarehouseId=@WarehouseId,SupplierId=@SupplierId,OrderedAt=@OrderedAt,ExpectedAt=@ExpectedAt,
            CurrencyCode=@CurrencyCode,Notes=@Notes,NetAmount=@NetAmount,TaxAmount=@TaxAmount,
            GrandTotal=@GrandTotal,UpdatedByUserId=@UserId,UpdatedAt=@Now
        WHERE PurchaseOrderId=@PurchaseOrderId;
    END
    ELSE
        INSERT purchasing.PurchaseOrderDrafts
            (PurchaseOrderId,BusinessId,WarehouseId,SupplierId,OrderedAt,ExpectedAt,CurrencyCode,Notes,
             NetAmount,TaxAmount,GrandTotal,CreatedByUserId,UpdatedByUserId,CreatedAt,UpdatedAt)
        VALUES(@PurchaseOrderId,@BusinessId,@WarehouseId,@SupplierId,@OrderedAt,@ExpectedAt,@CurrencyCode,@Notes,
            @NetAmount,@TaxAmount,@GrandTotal,@UserId,@UserId,@Now,@Now);

    DELETE purchasing.PurchaseOrderDraftLines WHERE PurchaseOrderId=@PurchaseOrderId;
    INSERT purchasing.PurchaseOrderDraftLines
        (PurchaseOrderId,LineId,LineNumber,ProductId,DescriptionSnapshot,OrderedQuantity,
         PresentationNameSnapshot,PresentationQuantity,UnitsPerPresentation,UnitCost,DiscountAmount,
         TaxCode,TaxRate,TaxTreatment,NetAmount,TaxAmount,LineTotal)
    SELECT @PurchaseOrderId,LineId,LineNumber,ProductId,Description,OrderedQuantity,PresentationName,
        PresentationQuantity,UnitsPerPresentation,UnitCost,DiscountAmount,TaxCode,TaxRate,
        TaxTreatment,NetAmount,TaxAmount,LineTotal
    FROM OPENJSON(@LinesJson) WITH
    (
        LineId uniqueidentifier '$.LineId',LineNumber int '$.LineNumber',ProductId uniqueidentifier '$.ProductId',
        Description nvarchar(250) '$.Description',OrderedQuantity decimal(19,6) '$.OrderedQuantity',
        PresentationName nvarchar(80) '$.PresentationName',PresentationQuantity decimal(19,6) '$.PresentationQuantity',
        UnitsPerPresentation decimal(19,6) '$.UnitsPerPresentation',UnitCost decimal(19,6) '$.UnitCost',
        DiscountAmount decimal(19,4) '$.DiscountAmount',TaxCode nvarchar(32) '$.TaxCode',TaxRate decimal(9,6) '$.TaxRate',
        TaxTreatment nvarchar(32) '$.TaxTreatment',NetAmount decimal(19,4) '$.NetAmount',
        TaxAmount decimal(19,4) '$.TaxAmount',LineTotal decimal(19,4) '$.LineTotal'
    );
END;
