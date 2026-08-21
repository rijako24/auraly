CREATE PROCEDURE [dbo].[SellerUserAccessGet]
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @PartyId UNIQUEIDENTIFIER,
    @ActorUserId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT app.UserId,app.PartyId,app.Username,app.Email,app.IsActive,role.Name,@BusinessId
    FROM dbo.AppUsers app
    JOIN dbo.UserRoles assignment ON assignment.UserId=app.UserId AND assignment.BusinessId=@BusinessId
    JOIN dbo.AppRoles role ON role.RoleId=assignment.RoleId AND role.NormalizedName=N'SELLER'
    WHERE app.TenantId=@TenantId AND app.PartyId=@PartyId;
END
