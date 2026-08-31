CREATE PROCEDURE [purchasing].[PurchaseOrderConfirm]
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @PurchaseOrderId UNIQUEIDENTIFIER,
    @WarehouseId UNIQUEIDENTIFIER,
    @SupplierId UNIQUEIDENTIFIER,
    @OrderedAt DATETIMEOFFSET(7),
    @ExpectedAt DATETIMEOFFSET(7) = NULL,
    @CurrencyCode CHAR(3),
    @Notes NVARCHAR(1000) = NULL,
    @NetAmount DECIMAL(19,4),
    @TaxAmount DECIMAL(19,4),
    @GrandTotal DECIMAL(19,4),
    @IdempotencyKey NVARCHAR(160),
    @PayloadHash BINARY(32),
    @DraftRowVersion VARBINARY(8) = NULL,
    @LinesJson NVARCHAR(MAX),
    @Now DATETIMEOFFSET(7)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ExistingId UNIQUEIDENTIFIER,@ExistingNumber NVARCHAR(40),@ExistingStatus NVARCHAR(24),@ExistingHash BINARY(32);
    SELECT @ExistingId=PurchaseOrderId,@ExistingNumber=DocumentNumber,@ExistingStatus=Status,@ExistingHash=PayloadHash
    FROM purchasing.PurchaseOrders WITH(UPDLOCK,HOLDLOCK)
    WHERE BusinessId=@BusinessId AND (PurchaseOrderId=@PurchaseOrderId OR IdempotencyKey=@IdempotencyKey);
    IF @ExistingId IS NOT NULL
    BEGIN
        IF @ExistingHash<>@PayloadHash THROW 51207,'The purchase-order idempotency key was reused with another payload.',1;
        SELECT @ExistingId PurchaseOrderId,@ExistingNumber DocumentNumber,@ExistingStatus Status,CONVERT(bit,1) Replayed;
        RETURN;
    END;

    IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
        THROW 51200,'Business is outside the tenant.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.Warehouses WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId AND IsActive=1 AND IsSystem=0 AND UseForGoodsReceipts=1)
        THROW 51201,'Warehouse is invalid.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.Suppliers WHERE SupplierId=@SupplierId AND BusinessId=@BusinessId AND IsActive=1)
        THROW 51202,'Supplier is invalid.',1;
    IF EXISTS
    (
        SELECT 1 FROM OPENJSON(@LinesJson) WITH(ProductId uniqueidentifier '$.ProductId') input
        WHERE NOT EXISTS(SELECT 1 FROM dbo.Products p WHERE p.ProductId=input.ProductId AND p.IsActive=1 AND (p.TenantId=@TenantId OR p.BusinessId=@BusinessId))
    ) THROW 51202,'A product is invalid.',1;

    DECLARE @StoredDraftVersion VARBINARY(8);
    SELECT @StoredDraftVersion=RowVersion FROM purchasing.PurchaseOrderDrafts WITH(UPDLOCK,HOLDLOCK)
    WHERE PurchaseOrderId=@PurchaseOrderId AND BusinessId=@BusinessId;
    IF @StoredDraftVersion IS NULL AND @DraftRowVersion IS NOT NULL THROW 51204,'The draft no longer exists.',1;
    IF @StoredDraftVersion IS NOT NULL AND (@DraftRowVersion IS NULL OR @StoredDraftVersion<>@DraftRowVersion)
        THROW 51204,'The draft changed in another session.',1;

    DECLARE @SeriesId UNIQUEIDENTIFIER,@Prefix NVARCHAR(8),@SeriesCode NVARCHAR(16),@Padding TINYINT,@RangeEnd BIGINT,@Consecutive BIGINT;
    SELECT TOP(1) @SeriesId=ds.DocumentSeriesId,@Prefix=ds.Prefix,@SeriesCode=ds.SeriesCode,@Padding=ds.Padding,
        @RangeEnd=ds.RangeEnd,@Consecutive=COALESCE(seriesCursor.NextConsecutive,ds.RangeStart)
    FROM dbo.DocumentSeries ds WITH(UPDLOCK,HOLDLOCK)
    LEFT JOIN dbo.DocumentSeriesCursors seriesCursor WITH(UPDLOCK,HOLDLOCK) ON seriesCursor.DocumentSeriesId=ds.DocumentSeriesId
    WHERE ds.BusinessId=@BusinessId AND ds.DocumentType=N'PurchaseOrder' AND ds.DeviceId IS NULL AND ds.IsActive=1
    ORDER BY ds.DocumentSeriesId;
    IF @SeriesId IS NULL THROW 51208,'La serie de órdenes de compra no está activa.',1;
    IF @Consecutive>@RangeEnd THROW 51208,'La numeración de órdenes de compra se agotó.',1;
    SET @Prefix=UPPER(LTRIM(RTRIM(@Prefix)));
    SET @SeriesCode=UPPER(LTRIM(RTRIM(@SeriesCode)));
    IF @Prefix<>N'OCP' OR @Padding<>8 OR NULLIF(@SeriesCode,N'') IS NULL
        THROW 51208,'La serie de órdenes de compra no cumple la numeración canónica.',1;
    DECLARE @DocumentNumber NVARCHAR(40)=CONCAT(@Prefix,@SeriesCode,N'-',RIGHT(REPLICATE('0',@Padding)+CONVERT(varchar(20),@Consecutive),@Padding));

    MERGE dbo.DocumentSeriesCursors WITH(HOLDLOCK) AS target
    USING(SELECT @SeriesId DocumentSeriesId) source ON target.DocumentSeriesId=source.DocumentSeriesId
    WHEN MATCHED THEN UPDATE SET NextConsecutive=@Consecutive+1,UpdatedAt=@Now
    WHEN NOT MATCHED THEN INSERT(DocumentSeriesId,NextConsecutive,UpdatedAt) VALUES(@SeriesId,@Consecutive+1,@Now);

    INSERT purchasing.PurchaseOrders
        (PurchaseOrderId,BusinessId,WarehouseId,SupplierId,DocumentSeriesId,DocumentNumber,DocumentPrefix,
         DocumentSeriesCode,DocumentConsecutive,IdempotencyKey,PayloadHash,OrderedAt,ExpectedAt,CurrencyCode,
         Notes,NetAmount,TaxAmount,GrandTotal,Status,ConfirmedByUserId,ConfirmedAt,UpdatedAt)
    VALUES(@PurchaseOrderId,@BusinessId,@WarehouseId,@SupplierId,@SeriesId,@DocumentNumber,@Prefix,@SeriesCode,
        @Consecutive,@IdempotencyKey,@PayloadHash,@OrderedAt,@ExpectedAt,@CurrencyCode,@Notes,@NetAmount,@TaxAmount,
        @GrandTotal,N'Open',@UserId,@Now,@Now);

    INSERT purchasing.PurchaseOrderLines
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

    DELETE purchasing.PurchaseOrderDrafts WHERE PurchaseOrderId=@PurchaseOrderId AND BusinessId=@BusinessId;
    SELECT @PurchaseOrderId PurchaseOrderId,@DocumentNumber DocumentNumber,N'Open' Status,CONVERT(bit,0) Replayed;
END;
