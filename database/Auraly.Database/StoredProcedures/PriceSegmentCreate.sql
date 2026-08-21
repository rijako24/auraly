CREATE PROCEDURE [dbo].[PriceSegmentCreate] @Kind NVARCHAR(20),@Id UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER,@Code NVARCHAR(32),@Name NVARCHAR(120) AS
BEGIN SET NOCOUNT ON;
 IF @Kind=N'PriceList' INSERT dbo.PriceLists(PriceListId,BusinessId,Code,Name,IsActive,CreatedAt) VALUES(@Id,@BusinessId,@Code,@Name,1,SYSUTCDATETIME());
 ELSE IF @Kind=N'PriceChannel' INSERT dbo.PriceChannels(PriceChannelId,BusinessId,Code,Name,IsActive,CreatedAt) VALUES(@Id,@BusinessId,@Code,@Name,1,SYSUTCDATETIME());
 ELSE THROW 51005,'Invalid price segment kind.',1;
END
