using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PosPortfolioDeviceApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Portfolio_party_picker_uses_portfolio_permission_without_workspace_permission()
    {
        using var client=fixture.CreateAdminClient(PayablesPermissionCodes.Read);
        using var allowed=await client.GetAsync(
            "/api/commerce/v1/portfolio/parties/role-options?role=Supplier&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);
        using var denied=await client.GetAsync(
            "/api/commerce/v1/portfolio/parties/role-options?role=Customer&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }

    [Fact]
    public async Task Pos_payment_permissions_do_not_open_administrative_portfolios()
    {
        var customerId=Guid.NewGuid();
        using var client=fixture.CreateAdminClient(
            ReceivablesPermissionCodes.RegisterPosPayment,
            PayablesPermissionCodes.RegisterPosPayment);
        foreach(var path in new[]
        {
            "/api/commerce/v1/pos/portfolio/parties/role-options?role=Customer&page=1&pageSize=10",
            "/api/commerce/v1/pos/portfolio/parties/role-options?role=Supplier&page=1&pageSize=10",
            $"/api/commerce/v1/pos/receivables?page=1&pageSize=10&customerId={customerId:D}",
            $"/api/commerce/v1/pos/payables?page=1&pageSize=10&supplierId={fixture.SupplierId:D}"
        })
        {
            using var response=await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        }
        using(var response=await client.GetAsync(
            "/api/commerce/v1/pos/payables?page=1&pageSize=10"))
            Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        foreach(var path in new[]
        {
            "/api/commerce/v1/portfolio/parties/role-options?role=Customer&page=1&pageSize=10",
            "/api/commerce/v1/portfolio/parties/role-options?role=Supplier&page=1&pageSize=10",
            "/api/commerce/v1/receivables?page=1&pageSize=10",
            "/api/commerce/v1/payables?page=1&pageSize=10"
        })
        {
            using var response=await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        }
        var receivable=new ConfirmCustomerPaymentRequest(Guid.NewGuid(),fixture.BusinessId,Guid.NewGuid(),
            null,DateTimeOffset.UtcNow,"COP",null,[],[]);
        using(var response=await client.PostAsJsonAsync(
            "/api/commerce/v1/receivable-payments/confirm",receivable))
            Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        using(var response=await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/receivable-payments/confirm",receivable))
            Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        var payable=new ConfirmSupplierPaymentRequest(Guid.NewGuid(),fixture.BusinessId,Guid.NewGuid(),
            DateTimeOffset.UtcNow,"COP",null,[],[],null);
        using(var response=await client.PostAsJsonAsync(
            "/api/commerce/v1/payable-payments/confirm",payable))
            Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        using(var response=await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/payable-payments/confirm",payable))
            Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [Fact]
    public async Task Enrolled_portfolio_uses_the_registered_user_session_and_canonical_services()
    {
        var customerId=Guid.NewGuid();
        var userId=Guid.NewGuid();
        var workSessionId=Guid.NewGuid();
        var roleId=Guid.NewGuid();
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command=connection.CreateCommand();
            command.CommandText="""
                INSERT dbo.AppRoles(RoleId,TenantId,Name,NormalizedName,Description,IsActive,IsSystemRole,CreatedAt)
                VALUES(@RoleId,@TenantId,@RoleName,@RoleName,N'POS cartera',1,0,SYSUTCDATETIME());
                INSERT dbo.AppUsers(UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
                    FirstName,LastName,IsActive,CreatedAt)
                VALUES(@UserId,@TenantId,@Username,UPPER(@Username),@Email,UPPER(@Email),
                    N'Operador',N'Cartera',1,SYSUTCDATETIME());
                INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
                VALUES(NEWID(),@UserId,@RoleId,@BusinessId,SYSUTCDATETIME());
                INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
                SELECT NEWID(),@RoleId,PermissionId,SYSUTCDATETIME()
                FROM dbo.Permissions
                WHERE Resource IN(N'pos.receivables.payments.create',
                    N'pos.payables.payments.create')
                  AND NOT EXISTS(
                    SELECT 1 FROM dbo.RolePermissions existing
                    WHERE existing.RoleId=@RoleId
                      AND existing.PermissionId=dbo.Permissions.PermissionId);
                """;
            command.Parameters.AddWithValue("@UserId",userId);
            command.Parameters.AddWithValue("@TenantId",fixture.TenantId);
            command.Parameters.AddWithValue("@BusinessId",fixture.BusinessId);
            command.Parameters.AddWithValue("@RoleId",roleId);
            command.Parameters.AddWithValue("@RoleName",$"POS-PORTFOLIO-{roleId:N}");
            command.Parameters.AddWithValue("@Username",$"portfolio-{userId:N}");
            command.Parameters.AddWithValue("@Email",$"portfolio-{userId:N}@test.local");
            await command.ExecuteNonQueryAsync();
        }
        using var client=fixture.CreateClient();
        using(var opened=DeviceRequest(HttpMethod.Post,"/api/pos/v1/work-sessions/opened",userId,workSessionId))
        {
            opened.Content=JsonContent.Create(new RegisterDeviceWorkSessionRequest(
                userId,workSessionId,fixture.BusinessId,DateTimeOffset.UtcNow));
            using var response=await client.SendAsync(opened);
            response.EnsureSuccessStatusCode();
        }

        foreach(var path in new[]
        {
            "/api/pos/v1/portfolio/parties/role-options?role=Customer&page=1&pageSize=20",
            "/api/pos/v1/portfolio/parties/role-options?role=Supplier&page=1&pageSize=20",
            $"/api/pos/v1/receivables?page=1&pageSize=20&customerId={customerId:D}",
            $"/api/pos/v1/payables?page=1&pageSize=20&supplierId={fixture.SupplierId:D}"
        })
        {
            using var request=DeviceRequest(HttpMethod.Get,path,userId,workSessionId);
            using var response=await client.SendAsync(request);
            Assert.True(response.IsSuccessStatusCode,$"{path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        using(var request=DeviceRequest(HttpMethod.Get,
            $"/api/pos/v1/receivables?page=1&pageSize=20&customerId={customerId:D}",userId,Guid.NewGuid()))
        using(var response=await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);

        var receivable=new ConfirmCustomerPaymentRequest(Guid.NewGuid(),fixture.BusinessId,Guid.NewGuid(),
            workSessionId,DateTimeOffset.UtcNow,"COP",null,[],[]);
        using(var request=DeviceRequest(HttpMethod.Post,"/api/pos/v1/receivable-payments/confirm",userId,workSessionId))
        {
            request.Content=JsonContent.Create(receivable);
            request.Headers.Add("Idempotency-Key",$"test-{Guid.NewGuid():N}");
            using var response=await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        }
        var payable=new ConfirmSupplierPaymentRequest(Guid.NewGuid(),fixture.BusinessId,Guid.NewGuid(),
            DateTimeOffset.UtcNow,"COP",null,[],[],workSessionId);
        using(var request=DeviceRequest(HttpMethod.Post,"/api/pos/v1/payable-payments/confirm",userId,workSessionId))
        {
            request.Content=JsonContent.Create(payable);
            request.Headers.Add("Idempotency-Key",$"test-{Guid.NewGuid():N}");
            using var response=await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        }
    }

    private HttpRequestMessage DeviceRequest(HttpMethod method,string path,Guid userId,Guid sessionId)
    {
        var request=new HttpRequestMessage(method,path);
        request.Headers.Add("X-Auraly-Device-Id",fixture.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret",ServerSliceFixture.DeviceSecret);
        request.Headers.Add("X-Auraly-User-Id",userId.ToString("D"));
        request.Headers.Add("X-Auraly-Business-Id",fixture.BusinessId.ToString("D"));
        request.Headers.Add("X-Auraly-Work-Session-Id",sessionId.ToString("D"));
        return request;
    }
}
