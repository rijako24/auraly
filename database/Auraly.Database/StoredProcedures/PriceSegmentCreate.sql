CREATE PROCEDURE [dbo].[PriceSegmentCreate] @Id UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER,@Code NVARCHAR(32),@Name NVARCHAR(120),@Strategy NVARCHAR(48),@Value DECIMAL(19,6)=NULL AS
BEGIN SET NOCOUNT ON;
 INSERT dbo.PriceChannels(PriceChannelId,BusinessId,Code,Name,Strategy,Value,IsActive,CreatedAt) VALUES(@Id,@BusinessId,@Code,@Name,@Strategy,@Value,1,SYSUTCDATETIME());
END
