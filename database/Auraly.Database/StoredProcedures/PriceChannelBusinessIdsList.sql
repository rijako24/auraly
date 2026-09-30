CREATE PROCEDURE dbo.PriceChannelBusinessIdsList
    @Id UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT target.BusinessId
    FROM dbo.PriceChannels channelValue
    JOIN dbo.Businesses currentBusiness ON currentBusiness.TenantId=channelValue.TenantId
    JOIN dbo.Businesses target ON target.TenantId=channelValue.TenantId AND target.IsActive=1
    WHERE channelValue.PriceChannelId=@Id AND currentBusiness.BusinessId=@BusinessId
      AND currentBusiness.IsActive=1;
END;
