CREATE PROCEDURE [dbo].[SellerUserAccessCreate]
    @TenantId UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER,@PartyId UNIQUEIDENTIFIER,@ActorUserId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,@Username NVARCHAR(100),@NormalizedUsername NVARCHAR(100),@Email NVARCHAR(256),@NormalizedEmail NVARCHAR(256),
    @PasswordHash NVARCHAR(MAX),@OfflineSalt VARBINARY(MAX),@OfflineHash VARBINARY(MAX),@OfflineIterations INT,
    @OfflineChangedAt DATETIMEOFFSET(7),@FirstName NVARCHAR(100),@LastName NVARCHAR(100),@PhoneNumber NVARCHAR(20)=NULL,@Now DATETIMEOFFSET(7)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @RoleId uniqueidentifier;
    SELECT @RoleId=RoleId FROM dbo.AppRoles WITH(UPDLOCK,HOLDLOCK)
    WHERE TenantId=@TenantId AND NormalizedName=N'SELLER' AND IsActive=1;
    IF @RoleId IS NULL THROW 51913,'El rol Vendedor no está configurado para la empresa.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.Parties party WITH(UPDLOCK,HOLDLOCK)
      JOIN dbo.CommerceSellers seller ON seller.PartyId=party.PartyId AND seller.BusinessId=@BusinessId AND seller.IsActive=1
      WHERE party.TenantId=@TenantId AND party.PartyId=@PartyId AND party.IsActive=1)
      THROW 51910,'El tercero no es un vendedor activo de este negocio.',1;
    IF EXISTS(SELECT 1 FROM dbo.AppUsers WHERE PartyId=@PartyId)
      THROW 51911,'El vendedor ya tiene una cuenta de acceso.',1;
    IF EXISTS(SELECT 1 FROM dbo.AppUsers WHERE TenantId=@TenantId AND (NormalizedUsername=@NormalizedUsername OR NormalizedEmail=@NormalizedEmail))
      THROW 51912,'El usuario o el correo ya están registrados.',1;

    INSERT dbo.AppUsers(UserId,TenantId,PartyId,CreatedByUserId,Username,NormalizedUsername,Email,NormalizedEmail,
      PasswordHash,PosOfflinePasswordSalt,PosOfflinePasswordHash,PosOfflinePasswordIterations,PosOfflinePasswordChangedAt,
      FirstName,LastName,PhoneNumber,AccessFailedCount,EmailConfirmed,IsActive,CreatedAt)
    VALUES(@UserId,@TenantId,@PartyId,@ActorUserId,@Username,@NormalizedUsername,@Email,@NormalizedEmail,@PasswordHash,
      @OfflineSalt,@OfflineHash,@OfflineIterations,@OfflineChangedAt,@FirstName,@LastName,@PhoneNumber,0,0,1,@Now);
    INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt,AssignedByUserId)
    VALUES(NEWID(),@UserId,@RoleId,@BusinessId,@Now,@ActorUserId);
    SELECT app.UserId,app.PartyId,app.Username,app.Email,app.IsActive,role.Name,@BusinessId
    FROM dbo.AppUsers app JOIN dbo.AppRoles role ON role.RoleId=@RoleId WHERE app.UserId=@UserId;
END
