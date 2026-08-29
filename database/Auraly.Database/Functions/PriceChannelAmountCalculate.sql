CREATE FUNCTION [dbo].[PriceChannelAmountCalculate]
(
    @Strategy NVARCHAR(48),
    @Value DECIMAL(19,6),
    @BaseAmount DECIMAL(19,4),
    @AverageCost DECIMAL(19,6),
    @LatestCost DECIMAL(19,6),
    @ProductTargetMarginPercent DECIMAL(9,6),
    @SpecialAmount DECIMAL(19,4)
)
RETURNS DECIMAL(19,4)
AS
BEGIN
    DECLARE @calculated DECIMAL(38,10);
    DECLARE @targetMargin DECIMAL(19,6);

    IF @Strategy = N'TieredProductPrice'
        SET @calculated = @SpecialAmount;
    ELSE IF @Strategy = N'PercentageOverBasePrice'
        SET @calculated = @BaseAmount * (1 + COALESCE(@Value, 0) / 100);
    ELSE IF @Strategy = N'MarginOverLatestCost' AND @LatestCost > 0
        SET @calculated = @LatestCost / (1 - COALESCE(@Value, 0) / 100);
    ELSE IF @Strategy = N'FixedMarginOverAverageCost' AND @AverageCost > 0
        SET @calculated = @AverageCost / (1 - COALESCE(@Value, 0) / 100);
    ELSE IF @Strategy = N'SellAtAverageCost' AND @AverageCost > 0
        SET @calculated = @AverageCost;
    ELSE IF @Strategy = N'ProductMarginAdjustment'
    BEGIN
        SET @targetMargin = COALESCE(
            @ProductTargetMarginPercent,
            CASE WHEN @BaseAmount > 0
                 THEN 100 - (100 * @AverageCost / @BaseAmount)
            END);
        IF @targetMargin IS NOT NULL
        BEGIN
            DECLARE @adjustedMargin DECIMAL(19,6) = @targetMargin + COALESCE(@Value, 0);
            IF @adjustedMargin < 0 SET @adjustedMargin = 0;
            IF @adjustedMargin >= 100 SET @adjustedMargin = 99.999999;

            -- Escalar el precio público por la relación entre márgenes conserva
            -- el tratamiento tributario ya incluido en el precio del producto.
            -- 20% -> 30% equivale a precio * (1-.20)/(1-.30).
            IF @BaseAmount > 0 AND @targetMargin >= 0 AND @targetMargin < 100
                SET @calculated = @BaseAmount
                    * (1 - @targetMargin / 100)
                    / (1 - @adjustedMargin / 100);
            ELSE
                SET @calculated = @AverageCost / (1 - @adjustedMargin / 100);
        END;
    END;

    IF @calculated IS NULL OR @calculated < 0 RETURN NULL;

    -- El motor de Pricing nunca materializa un canal por debajo del costo promedio.
    -- También protege precios escalonados y descuentos si el costo cambia después.
    IF @AverageCost > 0 AND @calculated < @AverageCost
        SET @calculated = @AverageCost;

    RETURN CONVERT(DECIMAL(19,4), ROUND(@calculated, 4));
END;
