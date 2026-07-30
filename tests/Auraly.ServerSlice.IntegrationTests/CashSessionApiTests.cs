using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Cash;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class CashSessionApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Two_online_cashiers_share_the_register_without_replacing_each_other()
    {
        var registerId = Guid.NewGuid();
        var firstCashierId = Guid.NewGuid();
        var secondCashierId = Guid.NewGuid();
        var supervisorId = Guid.NewGuid();
        const string supervisorPassword = "Supervisor-Only-2026!";
        await SeedCashScenarioAsync(
            registerId,
            firstCashierId,
            secondCashierId,
            supervisorId,
            BCrypt.Net.BCrypt.HashPassword(supervisorPassword, workFactor: 10));

        string printableCredential;
        using (var manager = fixture.CreateUserClient(
                   firstCashierId,
                   CommercePermissionCodes.SupervisorCredentialsManage))
        {
            using var provision = await manager.PostAsJsonAsync(
                "/api/commerce/v1/security/supervisor-credentials",
                new ProvisionSupervisorCredentialRequest(supervisorId));
            provision.EnsureSuccessStatusCode();
            var result =
                await provision.Content.ReadFromJsonAsync<ProvisionSupervisorCredentialResult>();
            printableCredential = Assert.IsType<string>(result!.PrintableCredential);
            Assert.StartsWith("AUR-SUP-", printableCredential, StringComparison.Ordinal);
        }

        using var firstClient = fixture.CreateUserClient(
            firstCashierId, CommercePermissionCodes.SalesCreate);
        using var secondClient = fixture.CreateUserClient(
            secondCashierId, CommercePermissionCodes.SalesCreate);
        var entries = await Task.WhenAll(
            firstClient.PostAsJsonAsync(
                $"/api/commerce/v1/cash/registers/{registerId:D}/session",
                new OpenCashSessionRequest(
                    fixture.BusinessId,
                    50_000m,
                    $"open-{Guid.NewGuid():N}")),
            secondClient.PostAsJsonAsync(
                $"/api/commerce/v1/cash/registers/{registerId:D}/session",
                new OpenCashSessionRequest(
                    fixture.BusinessId,
                    50_000m,
                    $"open-{Guid.NewGuid():N}")));
        using var firstEntry = entries[0];
        using var secondEntry = entries[1];
        firstEntry.EnsureSuccessStatusCode();
        secondEntry.EnsureSuccessStatusCode();
        var firstSession =
            (await firstEntry.Content.ReadFromJsonAsync<CashSessionView>())!;
        var secondSession =
            (await secondEntry.Content.ReadFromJsonAsync<CashSessionView>())!;
        Assert.Equal(firstCashierId, firstSession.ResponsibleUserId);
        Assert.Equal(secondCashierId, secondSession.ResponsibleUserId);
        Assert.Equal(firstSession.CashSessionId, secondSession.CashSessionId);
        Assert.NotEqual(firstSession.CashierShiftId, secondSession.CashierShiftId);

        using (var firstStillActive = fixture.CreateUserClient(
                   firstCashierId,
                   CommercePermissionCodes.CashRead))
        using (var current = await firstStillActive.GetAsync(
                   $"/api/commerce/v1/cash/registers/{registerId:D}/session"))
        {
            current.EnsureSuccessStatusCode();
            var firstCurrent =
                await current.Content.ReadFromJsonAsync<CashSessionView>();
            Assert.Equal(firstSession.CashierShiftId, firstCurrent!.CashierShiftId);
            Assert.Equal(firstCashierId, firstCurrent.ResponsibleUserId);
        }

        SupervisorAuthorizationGrant scannedGrant;
        using (var second = fixture.CreateUserClient(
                   secondCashierId,
                   CommercePermissionCodes.SalesCreate))
        {
            using var authorized = await second.PostAsJsonAsync(
                $"/api/commerce/v1/cash/registers/{registerId:D}/handoff-authorizations",
                new SupervisorAuthorizationRequest(null, printableCredential));
            authorized.EnsureSuccessStatusCode();
            scannedGrant =
                (await authorized.Content.ReadFromJsonAsync<SupervisorAuthorizationGrant>())!;
            Assert.Equal(supervisorId, scannedGrant.AuthorizedByUserId);
            Assert.Equal(CommercePermissionCodes.CashHandoffApprove, scannedGrant.PermissionCode);

            var handoff = new HandoffCashRequest(
                firstCashierId,
                [new CashCountLineInput("Cash", 50_000m)],
                "Entrega opcional al cajero anterior",
                null,
                scannedGrant.Token,
                $"handoff-{Guid.NewGuid():N}");
            using var delivered = await second.PostAsJsonAsync(
                $"/api/commerce/v1/cash/registers/{registerId:D}/handoff",
                handoff);
            delivered.EnsureSuccessStatusCode();
            var result = (await delivered.Content.ReadFromJsonAsync<CashHandoffResult>())!;
            Assert.Equal(firstCashierId, result.Session.ResponsibleUserId);

            using var duplicate = await second.PostAsJsonAsync(
                $"/api/commerce/v1/cash/registers/{registerId:D}/handoff",
                handoff);
            duplicate.EnsureSuccessStatusCode();
            var duplicateResult =
                (await duplicate.Content.ReadFromJsonAsync<CashHandoffResult>())!;
            Assert.Equal(result.CashCountId, duplicateResult.CashCountId);
        }

        using (var first = fixture.CreateUserClient(
                   firstCashierId,
                   CommercePermissionCodes.SalesCreate))
        {
            using var reused = await first.PostAsJsonAsync(
                $"/api/commerce/v1/cash/registers/{registerId:D}/handoff",
                new HandoffCashRequest(
                    secondCashierId,
                    [new CashCountLineInput("Cash", 50_000m)],
                    null,
                    null,
                    scannedGrant.Token,
                    $"reuse-{Guid.NewGuid():N}"));
            Assert.Equal(HttpStatusCode.Forbidden, reused.StatusCode);

            using var passwordAuthorization = await first.PostAsJsonAsync(
                $"/api/commerce/v1/cash/registers/{registerId:D}/handoff-authorizations",
                new SupervisorAuthorizationRequest("cash-supervisor", supervisorPassword));
            passwordAuthorization.EnsureSuccessStatusCode();
            var passwordGrant =
                await passwordAuthorization.Content
                    .ReadFromJsonAsync<SupervisorAuthorizationGrant>();
            Assert.Equal(supervisorId, passwordGrant!.AuthorizedByUserId);
        }

        await AssertDatabaseStateAsync(
            registerId,
            firstSession.CashSessionId,
            firstCashierId,
            secondCashierId,
            supervisorId);
    }

    private async Task SeedCashScenarioAsync(
        Guid registerId,
        Guid firstCashierId,
        Guid secondCashierId,
        Guid supervisorId,
        string supervisorPasswordHash)
    {
        const string sql = """
            INSERT dbo.CashRegisters
              (RegisterId,BusinessId,WarehouseId,Code,Name,IsActive,CreatedAt)
            VALUES
              (@RegisterId,@BusinessId,@WarehouseId,N'01',N'Caja relevo',1,SYSDATETIMEOFFSET());

            INSERT dbo.AppUsers
              (UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
               PasswordHash,FirstName,LastName,IsActive,CreatedAt)
            VALUES
              (@FirstCashierId,@TenantId,N'cash-one',N'CASH-ONE',@FirstEmail,@FirstEmail,
               NULL,N'Caja',N'Uno',1,SYSUTCDATETIME()),
              (@SecondCashierId,@TenantId,N'cash-two',N'CASH-TWO',@SecondEmail,@SecondEmail,
               NULL,N'Caja',N'Dos',1,SYSUTCDATETIME()),
              (@SupervisorId,@TenantId,N'cash-supervisor',N'CASH-SUPERVISOR',
               @SupervisorEmail,@SupervisorEmail,@SupervisorPasswordHash,
               N'Supervisor',N'Caja',1,SYSUTCDATETIME());

            DECLARE @RoleId UNIQUEIDENTIFIER=NEWID();
            INSERT dbo.AppRoles
              (RoleId,TenantId,Name,NormalizedName,Description,IsActive,IsSystemRole,CreatedAt)
            VALUES
              (@RoleId,@TenantId,N'Supervisor de caja',N'CASH-SUPERVISOR-TEST',
               N'Autoriza entregas de caja',1,0,SYSUTCDATETIME());
            INSERT dbo.UserRoles
              (UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
            VALUES
              (NEWID(),@SupervisorId,@RoleId,@BusinessId,SYSUTCDATETIME());
            INSERT dbo.RolePermissions
              (RolePermissionId,RoleId,PermissionId,AssignedAt)
            SELECT NEWID(),@RoleId,p.PermissionId,SYSUTCDATETIME()
            FROM dbo.Permissions p
            WHERE p.Resource=@HandoffPermission;
            """;
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@RegisterId", registerId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@FirstCashierId", firstCashierId);
        command.Parameters.AddWithValue("@SecondCashierId", secondCashierId);
        command.Parameters.AddWithValue("@SupervisorId", supervisorId);
        command.Parameters.AddWithValue("@FirstEmail", $"{firstCashierId:N}@auraly.test");
        command.Parameters.AddWithValue("@SecondEmail", $"{secondCashierId:N}@auraly.test");
        command.Parameters.AddWithValue("@SupervisorEmail", $"{supervisorId:N}@auraly.test");
        command.Parameters.AddWithValue("@SupervisorPasswordHash", supervisorPasswordHash);
        command.Parameters.AddWithValue(
            "@HandoffPermission",
            CommercePermissionCodes.CashHandoffApprove);
        await command.ExecuteNonQueryAsync();
    }

    private async Task AssertDatabaseStateAsync(
        Guid registerId,
        Guid cashSessionId,
        Guid firstCashierId,
        Guid secondCashierId,
        Guid supervisorId)
    {
        const string sql = """
            SELECT Status FROM dbo.CashSessions
            WHERE CashSessionId=@CashSessionId AND RegisterId=@RegisterId;

            SELECT UserId,EndReason
            FROM dbo.CashierShifts
            WHERE CashSessionId=@CashSessionId
            ORDER BY CASE WHEN UserId=@FirstCashierId THEN 0 ELSE 1 END;

            SELECT COUNT_BIG(1),MIN(AuthorizedByUserId)
            FROM dbo.CashCounts
            WHERE CashSessionId=@CashSessionId AND CountType=N'Handoff';

            SELECT COUNT_BIG(1)
            FROM dbo.SupervisorAuthorizationGrants
            WHERE RegisterId=@RegisterId AND ConsumedAt IS NOT NULL;
            """;
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@RegisterId", registerId);
        command.Parameters.AddWithValue("@CashSessionId", cashSessionId);
        command.Parameters.AddWithValue("@FirstCashierId", firstCashierId);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("Open", reader.GetString(0));

        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(firstCashierId, reader.GetGuid(0));
        Assert.True(reader.IsDBNull(1));
        Assert.True(await reader.ReadAsync());
        Assert.Equal(secondCashierId, reader.GetGuid(0));
        Assert.Equal("Handoff", reader.GetString(1));
        Assert.False(await reader.ReadAsync());

        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(supervisorId, reader.GetGuid(1));

        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
    }
}
