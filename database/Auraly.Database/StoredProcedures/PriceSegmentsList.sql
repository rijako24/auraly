CREATE PROCEDURE [dbo].[PriceSegmentsList] @BusinessId UNIQUEIDENTIFIER AS
BEGIN SET NOCOUNT ON;
 SELECT c.PriceChannelId,c.Code,c.Name,c.IsActive,c.CreatedAt,
   (SELECT COUNT(*) FROM dbo.PriceChannelItems i WHERE i.PriceChannelId=c.PriceChannelId AND i.IsActive=1),
   (SELECT COUNT(*) FROM dbo.CustomerPricingSettings s JOIN dbo.Customers customer ON customer.CustomerId=s.CustomerId
    WHERE customer.BusinessId=c.BusinessId AND customer.IsActive=1 AND s.PriceChannelId=c.PriceChannelId
      AND s.ValidFrom<=SYSDATETIMEOFFSET() AND(s.ValidUntil IS NULL OR s.ValidUntil>SYSDATETIMEOFFSET())),
   c.Strategy,c.Value
 FROM dbo.PriceChannels c WHERE c.BusinessId=@BusinessId ORDER BY c.Name;
END
