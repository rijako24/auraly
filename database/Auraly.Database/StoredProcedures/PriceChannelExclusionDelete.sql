CREATE PROCEDURE [dbo].[PriceChannelExclusionDelete]
    @ExclusionId UNIQUEIDENTIFIER,
    @Id UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    DELETE exclusion
    FROM dbo.PriceChannelExclusions exclusion
    JOIN dbo.PriceChannels channelValue
      ON channelValue.PriceChannelId = exclusion.PriceChannelId
    WHERE exclusion.PriceChannelExclusionId = @ExclusionId
      AND exclusion.PriceChannelId = @Id
      AND channelValue.BusinessId = @BusinessId;
END
