IF OBJECT_ID(N'dbo.PriceChannels', N'U') IS NOT NULL
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.PriceChannels')
          AND name = N'CK_PriceChannels_Strategy'
    )
        ALTER TABLE dbo.PriceChannels DROP CONSTRAINT CK_PriceChannels_Strategy;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.PriceChannels')
          AND name = N'CK_PriceChannels_Value'
    )
        ALTER TABLE dbo.PriceChannels DROP CONSTRAINT CK_PriceChannels_Value;

    -- La estrategia anterior expresaba markup sobre costo promedio. Conservamos
    -- el precio matemático equivalente al convertir ese markup a margen y dejamos
    -- que el resolutor use, desde este despliegue, el último costo observado.
    UPDATE dbo.PriceChannels
    SET Strategy = N'MarginOverLatestCost',
        Value = CASE
            WHEN Value IS NULL OR Value <= 0 THEN 0
            ELSE CONVERT(DECIMAL(19,6), 100 * Value / (100 + Value))
        END
    WHERE Strategy = N'PercentageOverAverageCost';

    PRINT 'ReplaceAverageCostMarkupWithLatestCostMargin: canales convertidos sin cambiar el precio matemático de referencia.';
END;
