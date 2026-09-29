CREATE PROCEDURE [dbo].[PriceChannelExclusionDelete]
    @ExclusionId UNIQUEIDENTIFIER,
    @Id UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS(SELECT 1 FROM dbo.PriceChannels
                  WHERE PriceChannelId=@Id AND TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId))
        THROW 51004, 'Price channel not found', 1;

    DELETE exclusion
    FROM dbo.PriceChannelExclusions exclusion
    JOIN dbo.PriceChannels channelValue
      ON channelValue.PriceChannelId = exclusion.PriceChannelId
    WHERE exclusion.PriceChannelExclusionId = @ExclusionId
      AND exclusion.PriceChannelId = @Id
      AND channelValue.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId);
    SELECT @@ROWCOUNT;
END
