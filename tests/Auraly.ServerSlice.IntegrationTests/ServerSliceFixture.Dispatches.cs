using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class ServerSliceFixture
{
    public async Task<Guid> SeedDispatchCarrierAsync(Guid? assignedUser = null)
    {
        var carrier=Guid.NewGuid(); var party=Guid.NewGuid(); var user=assignedUser??Guid.NewGuid(); var role=Guid.NewGuid();
        await using var connection=new SqlConnection(ConnectionString); await connection.OpenAsync();
        await using var command=new SqlCommand("""
          SELECT @Party=PartyId FROM dbo.AppUsers WHERE UserId=@User AND PartyId IS NOT NULL;
          IF NOT EXISTS(SELECT 1 FROM dbo.Parties WHERE PartyId=@Party)
          INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedAt)
            VALUES(@Party,@Tenant,N'NaturalPerson',N'Transportador asignado',N'Incomplete',1,SYSUTCDATETIME());
          SELECT @Carrier=CarrierId FROM dbo.Carriers WHERE TenantId=@Tenant AND PartyId=@Party;
          IF NOT EXISTS(SELECT 1 FROM dbo.Carriers WHERE CarrierId=@Carrier)
          INSERT dbo.Carriers(CarrierId,TenantId,PartyId,Code,TransportationMode,IsActive,CreatedAt)
            VALUES(@Carrier,@Tenant,@Party,LEFT(CONVERT(nvarchar(36),@Carrier),32),N'Road',1,SYSUTCDATETIME());
          IF NOT EXISTS(SELECT 1 FROM dbo.AppUsers WHERE UserId=@User)
          INSERT dbo.AppUsers(UserId,TenantId,PartyId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,IsActive)
            VALUES(@User,@Tenant,@Party,CONVERT(nvarchar(36),@User),CONVERT(nvarchar(36),@User),
              CONCAT(@User,N'@test.invalid'),CONCAT(@User,N'@test.invalid'),N'Transportador',N'Asignado',1);
          UPDATE dbo.AppUsers SET PartyId=@Party WHERE UserId=@User AND PartyId IS NULL;
          INSERT dbo.AppRoles(RoleId,TenantId,Name,NormalizedName) VALUES(@Role,@Tenant,CONVERT(nvarchar(36),@Role),CONVERT(nvarchar(36),@Role));
          INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId)
            SELECT NEWID(),@Role,PermissionId FROM dbo.Permissions WHERE Resource=N'dispatches.delivery.execute';
          INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId) VALUES(NEWID(),@User,@Role,@Business);
          SELECT @Carrier;
          """,connection);
        command.Parameters.AddWithValue("@Carrier",carrier); command.Parameters.AddWithValue("@Party",party);
        command.Parameters.AddWithValue("@User",user); command.Parameters.AddWithValue("@Role",role);
        command.Parameters.AddWithValue("@Tenant",TenantId); command.Parameters.AddWithValue("@Business",BusinessId);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

}
