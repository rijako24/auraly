IF OBJECT_ID(N'dbo.PriceChannels', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.PriceChannels', N'Strategy') IS NOT NULL
   AND COL_LENGTH(N'dbo.PriceChannels', N'Value') IS NOT NULL
BEGIN
    -- Los checks anteriores podían permanecer no confiables después de despliegues
    -- históricos. Normalizamos únicamente valores fuera del dominio de cada
    -- estrategia antes de que el DACPAC vuelva a validar la restricción.
    EXEC sys.sp_executesql N'
        UPDATE dbo.PriceChannels
        SET Value = NULL
        WHERE Strategy IN (N''TieredProductPrice'', N''SellAtAverageCost'')
          AND Value IS NOT NULL;

        UPDATE dbo.PriceChannels
        SET Value = CASE
            WHEN Value IS NULL THEN 0
            WHEN Value < -100 THEN -100
            WHEN Value > 1000 THEN 1000
            ELSE Value
        END
        WHERE Strategy = N''PercentageOverBasePrice''
          AND (Value IS NULL OR Value < -100 OR Value > 1000);

        UPDATE dbo.PriceChannels
        SET Value = CASE
            WHEN Value IS NULL OR Value < 0 THEN 0
            WHEN Value > 1000 THEN 1000
            ELSE Value
        END
        WHERE Strategy = N''PercentageOverAverageCost''
          AND (Value IS NULL OR Value < 0 OR Value > 1000);

        UPDATE dbo.PriceChannels
        SET Value = CASE
            WHEN Value IS NULL OR Value < 0 THEN 0
            WHEN Value >= 100 THEN CONVERT(DECIMAL(19,6), 99.999999)
            ELSE Value
        END
        WHERE Strategy = N''FixedMarginOverAverageCost''
          AND (Value IS NULL OR Value < 0 OR Value >= 100);

        UPDATE dbo.PriceChannels
        SET Value = CASE
            WHEN Value IS NULL THEN 0
            WHEN Value <= -100 THEN CONVERT(DECIMAL(19,6), -99.999999)
            WHEN Value >= 100 THEN CONVERT(DECIMAL(19,6), 99.999999)
            ELSE Value
        END
        WHERE Strategy = N''ProductMarginAdjustment''
          AND (Value IS NULL OR Value <= -100 OR Value >= 100);';

    PRINT 'NormalizePriceChannelValues: valores históricos ajustados al dominio seguro de cada estrategia.';
END;
