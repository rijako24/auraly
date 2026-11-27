CREATE PROCEDURE [dbo].[PriceSegmentItemSave]
  @Id UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER,
  @ProductId UNIQUEIDENTIFIER=NULL,@MinimumQuantity DECIMAL(19,6)=NULL,@Amount DECIMAL(19,4)=NULL,
  @TargetMarginPercent DECIMAL(9,6)=NULL,@AutoUpdatePrice BIT=0,@NewItemsJson NVARCHAR(MAX)=NULL,
  @RetiredItemsJson NVARCHAR(MAX)=NULL AS
BEGIN
  SET NOCOUNT ON;
  DECLARE @TenantId UNIQUEIDENTIFIER=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId);
  IF NOT EXISTS(SELECT 1 FROM dbo.PriceChannels WHERE PriceChannelId=@Id AND TenantId=@TenantId)
    THROW 51004,'Segment not found',1;

  DECLARE @Items TABLE(ProductId UNIQUEIDENTIFIER,MinimumQuantity DECIMAL(19,6),Amount DECIMAL(19,4),
    TargetMarginPercent DECIMAL(9,6),AutoUpdatePrice BIT);
  IF @NewItemsJson IS NULL
    INSERT @Items VALUES(@ProductId,@MinimumQuantity,@Amount,@TargetMarginPercent,@AutoUpdatePrice);
  ELSE
    INSERT @Items SELECT ProductId,MinimumQuantity,Amount,TargetMarginPercent,AutoUpdatePrice FROM OPENJSON(@NewItemsJson)
      WITH(ProductId UNIQUEIDENTIFIER '$.ProductId',MinimumQuantity DECIMAL(19,6) '$.MinimumQuantity',
        Amount DECIMAL(19,4) '$.Amount',TargetMarginPercent DECIMAL(9,6) '$.TargetMarginPercent',
        AutoUpdatePrice BIT '$.AutoUpdatePrice');

  IF EXISTS(SELECT 1 FROM @Items item WHERE NOT EXISTS(SELECT 1 FROM dbo.Products product
      WHERE product.ProductId=item.ProductId AND product.TenantId=@TenantId AND product.IsActive=1))
    THROW 51004,'Product not found',1;
  DECLARE @Retired TABLE(ProductId UNIQUEIDENTIFIER, MinimumQuantity DECIMAL(19,6),
    PRIMARY KEY(ProductId,MinimumQuantity));
  IF @NewItemsJson IS NULL
    INSERT @Retired VALUES(@ProductId,@MinimumQuantity);
  ELSE IF @RetiredItemsJson IS NOT NULL
    INSERT @Retired SELECT ProductId,MinimumQuantity FROM OPENJSON(@RetiredItemsJson)
      WITH(ProductId UNIQUEIDENTIFIER '$.ProductId', MinimumQuantity DECIMAL(19,6) '$.MinimumQuantity');

  UPDATE active SET IsActive=0
  FROM dbo.PriceChannelItems active
  JOIN @Retired retired ON retired.ProductId=active.ProductId AND retired.MinimumQuantity=active.MinimumQuantity
  WHERE active.PriceChannelId=@Id AND active.IsActive=1;
  IF @RetiredItemsJson IS NOT NULL AND @@ROWCOUNT<>(SELECT COUNT(*) FROM @Retired)
    THROW 51007,N'Una escala ya cambió o fue retirada. Vuelve a abrir el canal antes de guardar.',1;

  IF @NewItemsJson IS NOT NULL AND EXISTS(SELECT 1 FROM dbo.PriceChannelItems active WITH(UPDLOCK,HOLDLOCK)
    JOIN @Items item ON item.ProductId=active.ProductId AND item.MinimumQuantity=active.MinimumQuantity
    WHERE active.PriceChannelId=@Id AND active.IsActive=1)
    THROW 51006,N'El canal ya tiene un precio para una de las cantidades seleccionadas. Revisa sus escalas antes de guardar.',1;

  DECLARE @Now DATETIMEOFFSET=SYSDATETIMEOFFSET();
  INSERT dbo.PriceChannelItems(PriceChannelItemId,PriceChannelId,ProductId,MinimumQuantity,Amount,
    TargetMarginPercent,AutoUpdatePrice,CurrencyCode,ValidFrom,IsActive,CreatedAt)
  SELECT NEWID(),@Id,ProductId,MinimumQuantity,Amount,TargetMarginPercent,AutoUpdatePrice,N'COP',@Now,1,@Now FROM @Items;
END
