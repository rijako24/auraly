CREATE PROCEDURE dbo.PriceChannelSettingsUpdate
    @BusinessId UNIQUEIDENTIFIER,
    @Id UNIQUEIDENTIFIER,
    @Name NVARCHAR(120),
    @Strategy NVARCHAR(48),
    @Value DECIMAL(19, 6) = NULL,
    @AutoUpdatePrice BIT = NULL,
    @ItemSyncJson NVARCHAR(MAX) = N'[]',
    @NewItemsJson NVARCHAR(MAX) = NULL,
    @RetiredItemsJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @TenantId UNIQUEIDENTIFIER=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId);
    IF NOT EXISTS(SELECT 1 FROM dbo.PriceChannels WHERE PriceChannelId=@Id AND TenantId=@TenantId)
        THROW 51004, 'Segment not found', 1;

    DECLARE @Overrides TABLE(ProductId UNIQUEIDENTIFIER,MinimumQuantity DECIMAL(19,6),AutoUpdatePrice BIT);
    INSERT @Overrides SELECT ProductId,MinimumQuantity,AutoUpdatePrice
    FROM OPENJSON(@ItemSyncJson) WITH(ProductId UNIQUEIDENTIFIER '$.ProductId',
        MinimumQuantity DECIMAL(19,6) '$.MinimumQuantity',AutoUpdatePrice BIT '$.AutoUpdatePrice');
    IF (SELECT COUNT(*) FROM @Overrides)>500 OR EXISTS(
        SELECT ProductId,MinimumQuantity FROM @Overrides GROUP BY ProductId,MinimumQuantity HAVING COUNT(*)>1)
        OR EXISTS(SELECT 1 FROM @Overrides WHERE ProductId IS NULL OR MinimumQuantity IS NULL
            OR MinimumQuantity<=0 OR AutoUpdatePrice IS NULL)
        THROW 51005, N'La selección de sincronización no es válida; guarda como máximo 500 excepciones por vez.', 1;
    IF (@AutoUpdatePrice IS NOT NULL OR EXISTS(SELECT 1 FROM @Overrides)) AND @Strategy<>N'TieredProductPrice'
        THROW 51005, N'Sincronizar precio solo aplica al modo por producto y cantidad.', 1;

    -- This command changes only the selection. The existing publication engine
    -- retains ownership of price calculation and versioning.
    DECLARE @Changes TABLE(PriceChannelItemId UNIQUEIDENTIFIER,ProductId UNIQUEIDENTIFIER,MinimumQuantity DECIMAL(19,6),AutoUpdatePrice BIT,
        PRIMARY KEY(ProductId,MinimumQuantity));
    INSERT @Changes
    SELECT item.PriceChannelItemId,item.ProductId,item.MinimumQuantity,COALESCE(selection.AutoUpdatePrice,@AutoUpdatePrice)
    FROM dbo.PriceChannelItems item WITH(UPDLOCK,HOLDLOCK)
    LEFT JOIN @Overrides selection ON selection.ProductId=item.ProductId AND selection.MinimumQuantity=item.MinimumQuantity
    WHERE item.PriceChannelId=@Id AND item.IsActive=1
        AND (@AutoUpdatePrice IS NOT NULL OR selection.ProductId IS NOT NULL);
    IF EXISTS(SELECT 1 FROM @Overrides selection WHERE NOT EXISTS(
        SELECT 1 FROM @Changes changeValue WHERE changeValue.ProductId=selection.ProductId
            AND changeValue.MinimumQuantity=selection.MinimumQuantity))
        THROW 51005, N'Una condición de precio cambió o no pertenece al canal. Vuelve a abrirlo antes de guardar.', 1;
    UPDATE dbo.PriceChannels SET Name=@Name,Strategy=@Strategy,Value=@Value
    WHERE PriceChannelId=@Id AND TenantId=@TenantId;
    IF @@ROWCOUNT=0 THROW 51004, 'Segment not found', 1;

    IF @NewItemsJson IS NOT NULL
    BEGIN
        IF @Strategy<>N'TieredProductPrice'
            THROW 51005, N'Las escalas requieren el modo por producto y cantidad.', 1;
        EXEC dbo.PriceSegmentItemSave @Id=@Id, @BusinessId=@BusinessId,
            @NewItemsJson=@NewItemsJson, @RetiredItemsJson=@RetiredItemsJson;
    END

    IF EXISTS(SELECT 1 FROM @Changes changeValue
        JOIN dbo.PriceChannelItems item ON item.PriceChannelItemId=changeValue.PriceChannelItemId AND item.IsActive=1
        WHERE changeValue.AutoUpdatePrice=1 AND item.TargetMarginPercent IS NULL)
        THROW 51005, N'Configura un margen válido en todos los productos seleccionados antes de sincronizar sus precios.', 1;
    UPDATE item SET AutoUpdatePrice=changeValue.AutoUpdatePrice
    FROM dbo.PriceChannelItems item
    JOIN @Changes changeValue ON changeValue.PriceChannelItemId=item.PriceChannelItemId
    WHERE item.PriceChannelId=@Id AND item.IsActive=1 AND item.AutoUpdatePrice<>changeValue.AutoUpdatePrice;

    IF @NewItemsJson IS NOT NULL
        SELECT COUNT(*) FROM dbo.PriceChannelItems WHERE PriceChannelId=@Id AND IsActive=1;
END
