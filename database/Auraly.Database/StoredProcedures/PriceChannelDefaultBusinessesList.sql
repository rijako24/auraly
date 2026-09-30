CREATE PROCEDURE dbo.PriceChannelDefaultBusinessesList
    @BusinessId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT businessValue.BusinessId
    FROM dbo.Businesses currentBusiness
    JOIN dbo.Businesses businessValue ON businessValue.TenantId=currentBusiness.TenantId
      AND businessValue.IsActive=1
    WHERE currentBusiness.BusinessId=@BusinessId AND currentBusiness.IsActive=1;
END;
