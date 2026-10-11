using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class RestaurantWaiterTests(ServerSliceFixture fixture, ITestOutputHelper output) : IAsyncLifetime
{
    private static readonly string[] Permissions = ["parties.read", "parties.update",
        "parties.waiters.create", "parties.waiters.configure"];
    private string? previousPointOfSaleMode;

    public async Task InitializeAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT PointOfSaleMode FROM dbo.Businesses
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        previousPointOfSaleMode = (string?)await command.ExecuteScalarAsync();
        if (previousPointOfSaleMode is null)
            throw new InvalidOperationException("The test business is missing.");
        await SetPointOfSaleModeAsync("RestaurantBar");
    }

    public Task DisposeAsync() => SetPointOfSaleModeAsync(
        previousPointOfSaleMode ?? "DirectSale");

    private async Task SetPointOfSaleModeAsync(string mode)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.Businesses SET PointOfSaleMode=@Mode
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@Mode", mode);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Create_replay_preserves_one_identity_role_and_audit_without_user_or_access_assignment()
    {
        using var client = fixture.CreateAdminClient(Permissions);
        var request = await RequestAsync();
        var accepted = await CreateAsync(client, request);
        var replay = await CreateAsync(client, request);
        Assert.Equal(accepted.RoleId, replay.RoleId);
        Assert.Equal(accepted.PartyId, replay.PartyId);
        Assert.True(replay.IdempotentReplay);
        using var changed = await client.PostAsJsonAsync("/api/commerce/v1/waiters",
            request with { ShortName = "Otro nombre" });
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        var detail = await DetailAsync(client, accepted.PartyId);
        Assert.Contains("Waiter", detail.Roles);
        Assert.NotNull(detail.Waiter);
        Assert.Null(detail.User);
        Assert.Null(detail.Employee);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM parties.Waiters WHERE PartyId=@Id;", accepted.PartyId));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.AppUsers WHERE PartyId=@Id;", accepted.PartyId));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.AuditLogs WHERE Action=N'RestaurantWaiterCreated' AND EntityId=CONVERT(nvarchar(100),@Id);", accepted.RoleId));
        using var duplicate = await client.PostAsJsonAsync("/api/commerce/v1/waiters",
            request with { OperationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Waiter_reuses_customer_identity_without_creating_or_assigning_a_user()
    {
        using var client = fixture.CreateAdminClient([..Permissions, "customers.create"]);
        var request = await RequestAsync();
        using var customerResponse = await client.PostAsJsonAsync("/api/commerce/v1/customers",
            new CreateCustomerRequest(Guid.NewGuid(), fixture.BusinessId, request.Party, request.PrimarySite, null));
        customerResponse.EnsureSuccessStatusCode();
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDetail>())!;
        var waiter = await CreateAsync(client, request);
        Assert.Equal(customer.PartyId, waiter.PartyId);
        var detail = await DetailAsync(client, waiter.PartyId);
        Assert.Contains("Customer", detail.Roles);
        Assert.Contains("Waiter", detail.Roles);
        Assert.Null(detail.User);
    }

    [Fact]
    public async Task Matching_an_existing_login_email_does_not_link_the_waiter_to_that_user()
    {
        var existingPartyId=Guid.NewGuid();var existingUserId=Guid.NewGuid();
        var email=$"existing-{existingUserId:N}@auraly.test";
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var seed=new SqlCommand("""
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedAt)
            VALUES(@PartyId,@TenantId,N'NaturalPerson',N'Usuario previo',N'Incomplete',1,SYSDATETIMEOFFSET());
            INSERT dbo.AppUsers(UserId,TenantId,PartyId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,IsActive,CreatedAt)
            VALUES(@UserId,@TenantId,@PartyId,@Username,UPPER(@Username),@Email,UPPER(@Email),N'Usuario',N'Previo',1,SYSUTCDATETIME());
            """,connection);
        seed.Parameters.AddWithValue("@PartyId",existingPartyId);seed.Parameters.AddWithValue("@UserId",existingUserId);
        seed.Parameters.AddWithValue("@TenantId",fixture.TenantId);seed.Parameters.AddWithValue("@Email",email);
        seed.Parameters.AddWithValue("@Username",$"existing-{existingUserId:N}");
        await seed.ExecuteNonQueryAsync();
        using var admin=fixture.CreateAdminClient(Permissions);
        var request=await RequestAsync();
        var created=await CreateAsync(admin,request with {Party=request.Party with {Email=email}});
        Assert.NotEqual(existingPartyId,created.PartyId);
        Assert.Null((await DetailAsync(admin,created.PartyId)).User);
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.AppUsers WHERE PartyId=@Id;",created.PartyId));
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.Parties WHERE PartyId=@Id AND NormalizedIdentification IS NULL;",existingPartyId));
    }

    [Fact]
    public async Task Concurrent_replay_creates_exactly_one_waiter_and_audit()
    {
        using var first = fixture.CreateAdminClient(Permissions);
        using var second = fixture.CreateAdminClient(Permissions);
        var request = await RequestAsync();
        var results = await Task.WhenAll(CreateAsync(first, request), CreateAsync(second, request));
        Assert.Equal(results[0].RoleId, results[1].RoleId);
        Assert.Single(results.Where(result => !result.IdempotentReplay));
        Assert.Single(results.Where(result => result.IdempotentReplay));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM parties.Waiters WHERE PartyId=@Id;", results[0].PartyId));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.AuditLogs WHERE Action=N'RestaurantWaiterCreated' AND EntityId=CONVERT(nvarchar(100),@Id);", results[0].RoleId));
    }

    [Fact]
    public async Task Waiter_configuration_is_shared_by_tenant_and_stale_updates_roll_back_without_a_second_audit()
    {
        using var first = fixture.CreateAdminClient(Permissions);
        var created = await CreateAsync(first, await RequestAsync());
        var original = await DetailAsync(first, created.PartyId);
        var update = Edit(original, "Nombre de equipo");
        using var saved = await first.PutAsJsonAsync($"/api/commerce/v1/parties/{created.PartyId}", update);
        saved.EnsureSuccessStatusCode();
        var item = (await saved.Content.ReadFromJsonAsync<PartyWorkspaceItem>())!;
        Assert.Equal("Nombre de equipo", item.Waiter!.ShortName);
        using var stale = await first.PutAsJsonAsync($"/api/commerce/v1/parties/{created.PartyId}",
            update with { Waiter = new("No guardar") });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var current = await DetailAsync(first, created.PartyId);
        Assert.Equal("Nombre de equipo", current.Waiter!.ShortName);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.AuditLogs WHERE Action=N'RestaurantWaiterConfigured' AND EntityId=CONVERT(nvarchar(100),@Id);", created.RoleId));

        var businessId = await OtherBusinessAsync();
        using var second = fixture.CreateAdminClientWithBusinessHeader(businessId, Permissions);
        var shared = await DetailAsync(second, created.PartyId);
        Assert.Equal(created.RoleId, shared.Waiter!.WaiterId);
        Assert.Equal("Nombre de equipo", shared.Waiter.ShortName);
        using var changed = await second.PutAsJsonAsync($"/api/commerce/v1/parties/{created.PartyId}", Edit(shared, "Nombre compartido"));
        changed.EnsureSuccessStatusCode();
        Assert.Equal("Nombre compartido", (await DetailAsync(first, created.PartyId)).Waiter!.ShortName);
        Assert.Equal(created.RoleId, (await DetailAsync(second, created.PartyId)).Waiter!.WaiterId);
        Assert.Null((await DetailAsync(first, created.PartyId)).User);
    }

    [Fact]
    public async Task Waiter_attendance_permission_does_not_grant_master_creation_or_configuration()
    {
        using var waiter = fixture.CreateAdminClient("restaurant.attend");
        using var forbiddenCreate = await waiter.PostAsJsonAsync("/api/commerce/v1/waiters", await RequestAsync());
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenCreate.StatusCode);
        using var admin = fixture.CreateAdminClient(Permissions);
        var created = await CreateAsync(admin, await RequestAsync());
        using var editor = fixture.CreateAdminClient("parties.read", "parties.update");
        using var forbiddenConfig = await editor.PutAsJsonAsync($"/api/commerce/v1/parties/{created.PartyId}",
            Edit(await DetailAsync(admin, created.PartyId), "No guardar"));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenConfig.StatusCode);
        Assert.Equal("Equipo", (await DetailAsync(admin, created.PartyId)).Waiter!.ShortName);
        using var crossed = await admin.PostAsJsonAsync("/api/commerce/v1/waiters",
            (await RequestAsync()) with { BusinessId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden, crossed.StatusCode);
    }

    [Fact]
    public async Task Manual_user_link_and_role_assignment_use_existing_apis_and_are_scoped_to_the_business()
    {
        using var admin = fixture.CreateAdminClient([..Permissions,"security.users.link-party","users.assign_role"]);
        var created = await CreateAsync(admin, await RequestAsync());
        var userId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var seed = new SqlCommand("""
            INSERT dbo.AppUsers(UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
              FirstName,LastName,IsActive,CreatedAt)
            VALUES(@UserId,@TenantId,@Username,UPPER(@Username),@Email,UPPER(@Email),N'Mesero',N'Manual',1,SYSUTCDATETIME());
            BEGIN TRANSACTION;
            EXEC dbo.RestaurantWaiterRoleEnsure @TenantId=@TenantId;
            INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
            SELECT NEWID(),@OperatorRoleId,PermissionId,SYSUTCDATETIME()
            FROM dbo.Permissions p WHERE p.Resource=N'restaurant.attend'
              AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp
                WHERE rp.RoleId=@OperatorRoleId AND rp.PermissionId=p.PermissionId);
            COMMIT TRANSACTION;
            SELECT RoleId FROM dbo.AppRoles WHERE TenantId=@TenantId AND NormalizedName=N'WAITER';
            """,connection);
        seed.Parameters.AddWithValue("@UserId",userId);seed.Parameters.AddWithValue("@TenantId",fixture.TenantId);
        seed.Parameters.AddWithValue("@OperatorRoleId",fixture.RoleId);
        seed.Parameters.AddWithValue("@Username",$"waiter-{userId:N}");seed.Parameters.AddWithValue("@Email",$"waiter-{userId:N}@auraly.test");
        var roleId=Assert.IsType<Guid>(await seed.ExecuteScalarAsync());
        using var linked=await admin.PutAsJsonAsync($"/api/commerce/v1/parties/{created.PartyId}/user-account",new LinkPartyUserAccountRequest(userId));
        linked.EnsureSuccessStatusCode();
        Assert.False((await DetailAsync(admin,created.PartyId)).User!.RestaurantAttentionAllowed);
        using var assigned=await admin.PostAsJsonAsync($"/api/v1/users/{userId}/roles",new {roleId,businessId=fixture.BusinessId});
        assigned.EnsureSuccessStatusCode();
        var detail=await DetailAsync(admin,created.PartyId);
        Assert.Equal(userId,detail.User!.UserId);
        Assert.True(detail.User.RestaurantAttentionAllowed);
        using var second=fixture.CreateAdminClientWithBusinessHeader(await OtherBusinessAsync(),Permissions);
        var other=await DetailAsync(second,created.PartyId);
        Assert.Equal(created.RoleId, other.Waiter!.WaiterId);
        Assert.False(other.User!.RestaurantAttentionAllowed);
        Assert.Equal(0,await CountAsync("""
            SELECT COUNT(*) FROM dbo.RolePermissions granted
            JOIN dbo.Permissions permission ON permission.PermissionId=granted.PermissionId
            WHERE granted.RoleId=@Id AND (permission.Resource=N'sales.create'
              OR permission.Resource=N'orders.invoice' OR permission.Resource LIKE N'work-sessions.%');
            """,roleId));
    }

    [Fact]
    public async Task Invalid_waiter_fields_are_rejected_before_creating_a_partial_identity()
    {
        using var client=fixture.CreateAdminClient(Permissions);
        var request=await RequestAsync();
        foreach(var invalid in new[] {
            request with {ShortName=new string('x',81)},
            request with {Code="invalid code"},
            request with {OperationId=Guid.Empty},
            request with {PrimarySite=request.PrimarySite with {Phone=new string('9',33)}},
        })
        {
            using var response=await client.PostAsJsonAsync("/api/commerce/v1/waiters",invalid);
            Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        }
        using var identity=await client.GetAsync($"/api/commerce/v1/parties/identity?countryId={request.Party.IdentificationCountryId}&identificationTypeCode=CC&identification={request.Party.Identification}&requestedRole=Waiter");
        identity.EnsureSuccessStatusCode();
        Assert.False((await identity.Content.ReadFromJsonAsync<PartyIdentityLookupResult>())!.Exists);
    }

    [Fact]
    public async Task Foreign_context_cannot_read_or_configure_waiter_and_role_fk_rejects_cross_tenant()
    {
        using var admin=fixture.CreateAdminClient(Permissions);
        var created=await CreateAsync(admin,await RequestAsync());
        var detail=await DetailAsync(admin,created.PartyId);
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var query=new SqlCommand("SELECT TOP(1) BusinessId FROM dbo.Businesses WHERE TenantId<>@TenantId ORDER BY BusinessId;",connection);
        query.Parameters.AddWithValue("@TenantId",fixture.TenantId);
        var otherBusinessId=Assert.IsType<Guid>(await query.ExecuteScalarAsync());
        await using var invalid=new SqlCommand("""
            INSERT parties.Waiters(WaiterId,TenantId,PartyId,Code,IsActive,CreatedAt)
            SELECT NEWID(),business.TenantId,@PartyId,N'INVALID-CROSS-TENANT',1,SYSDATETIMEOFFSET()
            FROM dbo.Businesses business WHERE business.BusinessId=@BusinessId;
            """,connection);
        invalid.Parameters.AddWithValue("@BusinessId",otherBusinessId);
        invalid.Parameters.AddWithValue("@PartyId",created.PartyId);
        var error=await Assert.ThrowsAsync<SqlException>(()=>invalid.ExecuteNonQueryAsync());
        Assert.Equal(547,error.Number);
        using var other=fixture.CreateAdminClientWithBusinessHeader(otherBusinessId,Permissions);
        using var response=await other.PutAsJsonAsync($"/api/commerce/v1/parties/{created.PartyId}",Edit(detail,"No guardar"));
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized);
        Assert.Equal("Equipo", (await DetailAsync(admin,created.PartyId)).Waiter!.ShortName);
    }

    [Fact]
    public async Task Paginated_waiters_and_role_picker_filter_before_enrichment_and_preserve_page_totals()
    {
        var marker = $"Mesero-{Guid.NewGuid():N}";
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var seed = new SqlCommand("""
            DECLARE @Rows TABLE(PartyId uniqueidentifier,Code nvarchar(32),Name nvarchar(200));
            WITH numbers AS (SELECT 1 Number UNION ALL SELECT Number+1 FROM numbers WHERE Number<80)
            INSERT @Rows SELECT NEWID(),REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),CONCAT(@Marker,N'-',RIGHT(CONCAT(N'000',Number),3)) FROM numbers;
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedAt)
            SELECT PartyId,@TenantId,N'NaturalPerson',Name,N'Incomplete',1,SYSDATETIMEOFFSET() FROM @Rows;
            INSERT parties.Waiters(WaiterId,TenantId,PartyId,Code,IsActive,CreatedAt)
            SELECT NEWID(),@TenantId,PartyId,Code,1,SYSDATETIMEOFFSET() FROM @Rows;
            """, connection);
        seed.Parameters.AddWithValue("@Marker", marker);
        seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        await seed.ExecuteNonQueryAsync();
        using var client = fixture.CreateAdminClient(Permissions);
        var timing = Stopwatch.StartNew();
        var first = (await client.GetFromJsonAsync<PartyWorkspacePage>($"/api/commerce/v1/parties?role=Waiter&pageSize=20&page=1&search={marker}"))!;
        var firstMs = timing.Elapsed.TotalMilliseconds;
        timing.Restart();
        var next = (await client.GetFromJsonAsync<PartyWorkspacePage>($"/api/commerce/v1/parties?role=Waiter&pageSize=20&page=2&search={marker}"))!;
        output.WriteLine($"Meseros, 80 fichas: primera página {firstMs:N0} ms; segunda {timing.Elapsed.TotalMilliseconds:N0} ms.");
        Assert.Equal(80, first.TotalCount);
        Assert.Equal(4, first.TotalPages);
        Assert.Equal(20, first.Items.Count);
        Assert.Equal(20, next.Items.Count);
        Assert.Empty(first.Items.Select(item => item.PartyId).Intersect(next.Items.Select(item => item.PartyId)));
        Assert.All(first.Items, item => Assert.NotNull(item.Waiter));
        var options = (await client.GetFromJsonAsync<PartyRoleOptionPage>($"/api/commerce/v1/parties/role-options?role=Waiter&pageSize=20&page=2&search={marker}"))!;
        Assert.Equal(80, options.TotalCount);
        Assert.Equal(20, options.Items.Count);
        Assert.All(options.Items, item => Assert.Equal("Waiter", item.Role));
    }

    private async Task<CreateWaiterRequest> RequestAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var query = new SqlCommand("""
            SELECT TOP(1) country.CountryId,division.AdministrativeDivisionId,city.CityId
            FROM dbo.Cities city JOIN dbo.AdministrativeDivisions division ON division.AdministrativeDivisionId=city.AdministrativeDivisionId
            JOIN dbo.Countries country ON country.CountryId=division.CountryId
            WHERE city.IsActive=1 AND division.IsActive=1 AND country.IsActive=1 ORDER BY country.Code,city.Code;
            """, connection);
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var suffix = Guid.NewGuid().ToString("N")[..12];
        return new(Guid.NewGuid(), fixture.BusinessId,
            new("NaturalPerson",reader.GetGuid(0),"CC",suffix,null,$"Mesero {suffix}",null,"Mesero",suffix,null,null),
            new("PRINCIPAL","Principal",reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),"Calle de prueba",null,null,null,null),
            ShortName:"Equipo");
    }

    private static UpdatePartyRequest Edit(PartyWorkspaceDetail detail, string shortName) =>
        new(detail.PartyType,detail.DisplayName,detail.LegalName,detail.FirstName,detail.LastName,
            detail.VerificationDigit,detail.Email,detail.Phone,detail.RowVersion,Waiter:new(shortName));

    private static async Task<CommercialRoleAcceptance> CreateAsync(HttpClient client, CreateWaiterRequest request)
    {
        using var response = await client.PostAsJsonAsync("/api/commerce/v1/waiters",request);
        Assert.True(response.StatusCode==HttpStatusCode.Created,await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CommercialRoleAcceptance>())!;
    }

    private static async Task<PartyWorkspaceDetail> DetailAsync(HttpClient client, Guid partyId) =>
        (await client.GetFromJsonAsync<PartyWorkspaceDetail>($"/api/commerce/v1/parties/{partyId}"))!;

    private async Task<int> CountAsync(string sql, Guid id)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql,connection);
        command.Parameters.AddWithValue("@Id",id);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<Guid> OtherBusinessAsync()
    {
        var id=Guid.NewGuid();
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command=new SqlCommand("""
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website)
            VALUES(@Id,@TenantId,N'Otra sede meseros',N'',N'',N'',N'',N'');
            INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
            VALUES(NEWID(),@OperatorId,@OperatorRoleId,@Id,SYSUTCDATETIME());
            """,connection);
        command.Parameters.AddWithValue("@Id",id);command.Parameters.AddWithValue("@TenantId",fixture.TenantId);
        command.Parameters.AddWithValue("@OperatorId",fixture.UserId);command.Parameters.AddWithValue("@OperatorRoleId",fixture.RoleId);
        await command.ExecuteNonQueryAsync();return id;
    }
}
