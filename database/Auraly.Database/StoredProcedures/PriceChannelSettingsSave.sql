CREATE PROCEDURE [dbo].[PriceChannelSettingsSave]
    @BusinessId UNIQUEIDENTIFIER,
    @PriceChannelId UNIQUEIDENTIFIER,
    @RuleKind NVARCHAR(40),
    @NumericValue DECIMAL(19,6),
    @ValidFrom DATETIMEOFFSET(7)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @RuleKind<>N'PercentageVariation'
        THROW 51007,'Unsupported channel pricing rule.',1;
    IF @NumericValue < -100 OR @NumericValue > 1000
        THROW 51006,'Price variation percent is outside the allowed range.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.PriceChannels WHERE PriceChannelId=@PriceChannelId AND BusinessId=@BusinessId)
        THROW 51004,'Segment not found',1;
    UPDATE dbo.PriceChannelRules SET IsActive=0
    WHERE PriceChannelId=@PriceChannelId AND RuleKind=@RuleKind AND AppliesTo=N'AllProducts' AND IsActive=1;
    INSERT dbo.PriceChannelRules(PriceChannelRuleId,PriceChannelId,RuleKind,AppliesTo,NumericValue,ValidFrom,ValidUntil,IsActive,CreatedAt)
    VALUES(NEWID(),@PriceChannelId,@RuleKind,N'AllProducts',@NumericValue,@ValidFrom,NULL,1,SYSUTCDATETIME());
END
