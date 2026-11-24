using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Parties;
using Auraly.Contracts.Authentication;
using Auraly.Contracts.Authorization;
using Auraly.Platform.Application.Identity.DTOs;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PartyUserAccountVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Renaming_the_linked_username_preserves_identity_credentials_roles_and_offline_sync()
    {
        var id = Guid.NewGuid(); var party = Guid.NewGuid(); var roleId = Guid.NewGuid();
        var oldName = $"rename-{id:N}"; var newName = $"renamed-{id:N}";
        const string password = "Auraly-Rename-Test-2026!";
        var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 4);
        var verifier = PosOfflinePasswordHasher.Hash(password, DateTimeOffset.UtcNow);
        await ExecuteAsync("""
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedAt)
              VALUES(@Party,@Tenant,N'NaturalPerson',N'Usuario de prueba',N'Incomplete',1,SYSUTCDATETIME());
            INSERT dbo.AppUsers(UserId,TenantId,PartyId,Username,NormalizedUsername,Email,NormalizedEmail,
                PasswordHash,PosOfflinePasswordSalt,PosOfflinePasswordHash,PosOfflinePasswordIterations,
                PosOfflinePasswordChangedAt,FirstName,LastName,IsActive,CreatedAt)
              VALUES(@Id,@Tenant,@Party,@Username,UPPER(@Username),@Email,UPPER(@Email),@Hash,@Salt,@OfflineHash,
                @Iterations,@ChangedAt,N'Usuario',N'Prueba',1,SYSUTCDATETIME());
            INSERT dbo.AppRoles(RoleId,TenantId,Name,NormalizedName,IsActive,CreatedAt)
              VALUES(@Role,@Tenant,N'Usuario de prueba',@RoleName,1,SYSUTCDATETIME());
            INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
              SELECT NEWID(),@Role,PermissionId,SYSUTCDATETIME() FROM dbo.Permissions WHERE Resource=N'sales.create';
            INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
              VALUES(NEWID(),@Id,@Role,@Business,SYSUTCDATETIME());
            """, new("@Id", id), new("@Party", party), new("@Tenant", fixture.TenantId),
            new("@Username", oldName), new("@Email", $"{oldName}@auraly.test"), new("@Hash", hash),
            new("@Salt", verifier.Salt), new("@OfflineHash", verifier.Hash), new("@Iterations", verifier.Iterations),
            new("@ChangedAt", verifier.ChangedAt), new("@Role", roleId), new("@RoleName", $"RENAME-{id:N}"), new("@Business", fixture.BusinessId));
        var request = new UpdateUserRequest(null, null, null, null, $"  {newName}  ");
        using var denied = fixture.CreateAdminClient(PartyWorkspacePermissionCodes.Read);
        using var denial = await denied.PutAsJsonAsync($"/api/v1/users/{id}", request);
        Assert.Equal(HttpStatusCode.Forbidden, denial.StatusCode);
        using var admin = fixture.CreateAdminClient("users.update", PartyWorkspacePermissionCodes.Read);
        foreach (var invalid in new[] { " ", new string('u', 101) })
        {
            using var invalidResponse = await admin.PutAsJsonAsync($"/api/v1/users/{id}", request with { Username = invalid });
            Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        }
        var occupied = await ScalarAsync<string>("SELECT Username FROM dbo.AppUsers WHERE UserId=@Id", new SqlParameter("@Id", fixture.UserId));
        using var duplicate = await admin.PutAsJsonAsync($"/api/v1/users/{id}", request with { Username = occupied!.ToLowerInvariant() });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var renamed = await admin.PutAsJsonAsync($"/api/v1/users/{id}", request);
        renamed.EnsureSuccessStatusCode();
        Assert.Equal(newName, (await renamed.Content.ReadFromJsonAsync<UserDto>())!.Username);
        using var replay = await admin.PutAsJsonAsync($"/api/v1/users/{id}", request);
        replay.EnsureSuccessStatusCode();
        var detail = await admin.GetFromJsonAsync<PartyWorkspaceDetail>($"/api/commerce/v1/parties/{party}");
        Assert.Equal(newName, detail!.User!.Username);
        Assert.Equal(id, detail.User.UserId);
        Assert.Single(detail.User.Roles, role => role.RoleId == roleId);
        Assert.Equal(hash, await ScalarAsync<string>("SELECT PasswordHash FROM dbo.AppUsers WHERE UserId=@Id", new SqlParameter("@Id", id)));
        using var client = fixture.CreateClient();
        using var snapshotRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/pos/v1/identity/snapshot?businessId={fixture.BusinessId}");
        snapshotRequest.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString());
        snapshotRequest.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        using var snapshotResponse = await client.SendAsync(snapshotRequest);
        snapshotResponse.EnsureSuccessStatusCode();
        var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<PosOfflineIdentitySnapshot>();
        var offline = Assert.Single(snapshot!.Users, item => item.UserId == id);
        Assert.Equal(newName, offline.Username);
        Assert.True(PosOfflinePasswordHasher.Verify(password, offline.PasswordVerifier));
        foreach (var name in new[] { oldName, newName.ToUpperInvariant() })
        {
            using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            { Content = JsonContent.Create(new AuthenticationLoginRequest(name, "@auraly-e2e", password)) };
            login.Headers.Add(AuthenticationDefaults.ClientIdHeader, Guid.NewGuid().ToString());
            using var response = await client.SendAsync(login);
            Assert.Equal(name == oldName ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, response.StatusCode);
        }
        // The selected tenant cannot rename an account belonging to another tenant.
        var otherTenant = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await ExecuteAsync("""
            INSERT dbo.Tenants(TenantId,Name,Email,IsActive,CreatedAt) VALUES(@Tenant,N'Otro tenant',@Email,1,SYSUTCDATETIME());
            INSERT dbo.AppUsers(UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,IsActive,CreatedAt)
              VALUES(@Id,@Tenant,@Username,UPPER(@Username),@Email,UPPER(@Email),N'Otro',N'Usuario',1,SYSUTCDATETIME());
            """, new("@Tenant", otherTenant), new("@Id", otherId), new("@Username", newName), new("@Email", $"{otherTenant:N}@auraly.test"));
        using var crossTenant = await admin.PutAsJsonAsync($"/api/v1/users/{otherId}", request with { Username = oldName });
        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);
        var rivalId = Guid.NewGuid();
        await ExecuteAsync("""
            INSERT dbo.AppUsers(UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,IsActive,CreatedAt)
              VALUES(@Id,@Tenant,@Username,UPPER(@Username),@Email,UPPER(@Email),N'Otro',N'Usuario',1,SYSUTCDATETIME());
            """, new("@Tenant", fixture.TenantId), new("@Id", rivalId), new("@Username", $"rival-{rivalId:N}"), new("@Email", $"{rivalId:N}@auraly.test"));
        var racingName = $"concurrent-{id:N}";
        var responses = await Task.WhenAll(new[] { id, rivalId }.Select(target =>
            admin.PutAsJsonAsync($"/api/v1/users/{target}", request with { Username = racingName })));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Contains("nombre de usuario", await conflict.Content.ReadAsStringAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AppUsers WHERE TenantId=@Tenant AND NormalizedUsername=UPPER(@Name)",
            new("@Tenant", fixture.TenantId), new("@Name", racingName)));
    }

    [Fact]
    public async Task Party_and_user_account_are_linked_without_moving_security_roles_to_party()
    {
        var countryId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var secondPartyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var otherTenantUserId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N");

        await ExecuteAsync(
            """
            INSERT dbo.Countries(CountryId,Code,Name,IsActive,CreatedAt)
              VALUES(@CountryId,N'ZZ',N'Integration country',1,SYSDATETIMEOFFSET());
            INSERT dbo.Parties
              (PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,
               Identification,NormalizedIdentification,DisplayName,CompletionStatus,
               IsActive,CreatedBy,CreatedAt)
              VALUES
              (@PartyId,@TenantId,N'NaturalPerson',@CountryId,N'CC',@Identification,
               @Identification,N'Ada Operator',N'Complete',1,@ActorId,SYSDATETIMEOFFSET()),
              (@SecondPartyId,@TenantId,N'NaturalPerson',@CountryId,N'CC',@SecondIdentification,
               @SecondIdentification,N'Grace Operator',N'Complete',1,@ActorId,SYSDATETIMEOFFSET());
            INSERT dbo.AppUsers
              (UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
               FirstName,LastName,AccessFailedCount,EmailConfirmed,IsActive,CreatedAt)
              VALUES
              (@UserId,@TenantId,@Username,@NormalizedUsername,@Email,@NormalizedEmail,
               N'Login',N'Projection',0,1,1,SYSUTCDATETIME()),
              (@SecondUserId,@TenantId,@SecondUsername,@SecondNormalizedUsername,
               @SecondEmail,@SecondNormalizedEmail,N'Second',N'Projection',0,1,1,SYSUTCDATETIME());
            INSERT dbo.Tenants(TenantId,Name,Email,IsActive,CreatedAt)
              VALUES(@OtherTenantId,N'Other tenant',@OtherTenantEmail,1,SYSUTCDATETIME());
            INSERT dbo.AppUsers
              (UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
               FirstName,LastName,AccessFailedCount,EmailConfirmed,IsActive,CreatedAt)
              VALUES
              (@OtherTenantUserId,@OtherTenantId,@OtherUsername,@OtherNormalizedUsername,
               @OtherEmail,@OtherNormalizedEmail,N'Other',N'User',0,1,1,SYSUTCDATETIME());
            """,
            new SqlParameter("@CountryId", countryId),
            new SqlParameter("@PartyId", partyId),
            new SqlParameter("@SecondPartyId", secondPartyId),
            new SqlParameter("@TenantId", fixture.TenantId),
            new SqlParameter("@ActorId", fixture.UserId),
            new SqlParameter("@Identification", $"10{suffix[..14]}"),
            new SqlParameter("@SecondIdentification", $"20{suffix[..14]}"),
            new SqlParameter("@UserId", userId),
            new SqlParameter("@SecondUserId", secondUserId),
            new SqlParameter("@Username", $"party-user-{suffix}"),
            new SqlParameter("@NormalizedUsername", $"PARTY-USER-{suffix}"),
            new SqlParameter("@Email", $"party-user-{suffix}@auraly.test"),
            new SqlParameter("@NormalizedEmail", $"PARTY-USER-{suffix}@AURALY.TEST"),
            new SqlParameter("@SecondUsername", $"party-user-2-{suffix}"),
            new SqlParameter("@SecondNormalizedUsername", $"PARTY-USER-2-{suffix}"),
            new SqlParameter("@SecondEmail", $"party-user-2-{suffix}@auraly.test"),
            new SqlParameter("@SecondNormalizedEmail", $"PARTY-USER-2-{suffix}@AURALY.TEST"),
            new SqlParameter("@OtherTenantId", otherTenantId),
            new SqlParameter("@OtherTenantUserId", otherTenantUserId),
            new SqlParameter("@OtherTenantEmail", $"tenant-{suffix}@auraly.test"),
            new SqlParameter("@OtherUsername", $"other-user-{suffix}"),
            new SqlParameter("@OtherNormalizedUsername", $"OTHER-USER-{suffix}"),
            new SqlParameter("@OtherEmail", $"other-user-{suffix}@auraly.test"),
            new SqlParameter("@OtherNormalizedEmail", $"OTHER-USER-{suffix}@AURALY.TEST"));

        using var denied = fixture.CreateAdminClient(PartyPermissionCodes.Read);
        using var deniedResponse = await denied.PutAsJsonAsync(
            $"/api/commerce/v1/parties/{partyId:D}/user-account",
            new LinkPartyUserAccountRequest(userId));
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var admin = fixture.CreateAdminClient(PartyPermissionCodes.ManageUserAccounts);
        using var linkedResponse = await admin.PutAsJsonAsync(
            $"/api/commerce/v1/parties/{partyId:D}/user-account",
            new LinkPartyUserAccountRequest(userId));
        Assert.Equal(HttpStatusCode.OK, linkedResponse.StatusCode);
        var linked = await linkedResponse.Content.ReadFromJsonAsync<PartyUserAccountLink>();
        Assert.NotNull(linked);
        Assert.Equal(partyId, linked.PartyId);
        Assert.Equal(userId, linked.UserId);
        Assert.Equal(0, await ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM dbo.UserRoles assignment
            JOIN dbo.AppRoles role ON role.RoleId=assignment.RoleId AND role.NormalizedName=N'SELLER'
            WHERE assignment.UserId=@UserId;
            """, new SqlParameter("@UserId", userId)));

        var queried = await admin.GetFromJsonAsync<PartyUserAccountLink>(
            $"/api/commerce/v1/parties/{partyId:D}/user-account");
        Assert.NotNull(queried);
        Assert.Equal(userId, queried.UserId);

        using var idempotent = await admin.PutAsJsonAsync(
            $"/api/commerce/v1/parties/{partyId:D}/user-account",
            new LinkPartyUserAccountRequest(userId));
        Assert.Equal(HttpStatusCode.OK, idempotent.StatusCode);

        using var partyAlreadyLinked = await admin.PutAsJsonAsync(
            $"/api/commerce/v1/parties/{partyId:D}/user-account",
            new LinkPartyUserAccountRequest(secondUserId));
        Assert.Equal(HttpStatusCode.Conflict, partyAlreadyLinked.StatusCode);

        using var userAlreadyLinked = await admin.PutAsJsonAsync(
            $"/api/commerce/v1/parties/{secondPartyId:D}/user-account",
            new LinkPartyUserAccountRequest(userId));
        Assert.Equal(HttpStatusCode.Conflict, userAlreadyLinked.StatusCode);

        using var crossTenant = await admin.PutAsJsonAsync(
            $"/api/commerce/v1/parties/{secondPartyId:D}/user-account",
            new LinkPartyUserAccountRequest(otherTenantUserId));
        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);

        using var unlinked = await admin.DeleteAsync(
            $"/api/commerce/v1/parties/{partyId:D}/user-account");
        Assert.Equal(HttpStatusCode.NoContent, unlinked.StatusCode);
        Assert.Null(await ScalarAsync<Guid?>(
            "SELECT PartyId FROM dbo.AppUsers WHERE UserId=@UserId;",
            new SqlParameter("@UserId", userId)));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.AppUsers WHERE UserId=@UserId;",
            new SqlParameter("@UserId", userId)));
    }

    [Fact]
    public async Task Creating_access_for_an_existing_seller_links_the_party_and_assigns_the_seller_role_atomically()
    {
        var partyId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N");
        var username = $"seller-{suffix}";
        var email = $"seller-{suffix}@auraly.test";
        var otherTenantId = Guid.NewGuid();
        var otherTenantUserId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,LegalName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES(@PartyId,@TenantId,N'Organization',N'Vendedor acceso',N'Vendedor acceso',N'Complete',1,@ActorId,SYSDATETIMEOFFSET());
            INSERT dbo.CommerceSellers(SellerId,TenantId,PartyId,Code,CommissionBasis,CommissionTrigger,IsActive,CreatedAt)
            VALUES(@SellerId,@TenantId,@PartyId,@SellerCode,N'SaleAfterTax',N'Sale',1,SYSDATETIMEOFFSET());
            IF NOT EXISTS(SELECT 1 FROM dbo.AppRoles WHERE TenantId=@TenantId AND NormalizedName=N'SELLER')
              INSERT dbo.AppRoles(RoleId,TenantId,Name,NormalizedName,Description,IsActive,IsSystemRole,CreatedAt)
              VALUES(@RoleId,@TenantId,N'Vendedor',N'SELLER',N'Integration seller role',1,0,SYSDATETIMEOFFSET());
            INSERT dbo.Tenants(TenantId,Name,Email,IsActive,CreatedAt)
              VALUES(@OtherTenantId,N'Tenant with reusable identity',@OtherTenantEmail,1,SYSUTCDATETIME());
            INSERT dbo.AppUsers
              (UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
               FirstName,LastName,AccessFailedCount,EmailConfirmed,IsActive,CreatedAt)
              VALUES(@OtherTenantUserId,@OtherTenantId,@Username,UPPER(@Username),@Email,UPPER(@Email),
               N'Another',N'Tenant',0,1,1,SYSUTCDATETIME());
            """,
            new SqlParameter("@PartyId", partyId), new SqlParameter("@SellerId", sellerId),
            new SqlParameter("@RoleId", roleId), new SqlParameter("@TenantId", fixture.TenantId),
            new SqlParameter("@BusinessId", fixture.BusinessId), new SqlParameter("@ActorId", fixture.UserId),
            new SqlParameter("@SellerCode", $"SA-{suffix[..10]}"),
            new SqlParameter("@OtherTenantId", otherTenantId),
            new SqlParameter("@OtherTenantUserId", otherTenantUserId),
            new SqlParameter("@OtherTenantEmail", $"tenant-{suffix}@auraly.test"),
            new SqlParameter("@Username", username), new SqlParameter("@Email", email));

        using var denied = fixture.CreateAdminClient(PartyPermissionCodes.ManageUserAccounts);
        using var deniedResponse = await denied.PostAsJsonAsync(
            $"/api/commerce/v1/parties/{partyId:D}/seller-access",
            new { username, email, password="Seller.2026!", firstName="Sara", lastName="Ventas", phoneNumber="3001234567" });
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var admin = fixture.CreateAdminClient(
            "users.create", "users.assign_role", PartyPermissionCodes.ManageUserAccounts);
        using var response = await admin.PostAsJsonAsync(
            $"/api/commerce/v1/parties/{partyId:D}/seller-access",
            new { username, email, password="Seller.2026!", firstName="Sara", lastName="Ventas", phoneNumber="3001234567" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM dbo.AppUsers app
            JOIN dbo.UserRoles assignment ON assignment.UserId=app.UserId AND assignment.BusinessId=@BusinessId
            JOIN dbo.AppRoles role ON role.RoleId=assignment.RoleId AND role.NormalizedName=N'SELLER'
            WHERE app.PartyId=@PartyId AND app.PosOfflinePasswordHash IS NOT NULL AND app.PosOfflinePasswordSalt IS NOT NULL;
            """, new SqlParameter("@PartyId",partyId),new SqlParameter("@BusinessId",fixture.BusinessId)));

        using var duplicate = await admin.PostAsJsonAsync(
            $"/api/commerce/v1/parties/{partyId:D}/seller-access",
            new { username=$"seller-duplicate-{suffix}", email=$"seller-duplicate-{suffix}@auraly.test", password="Seller.2026!", firstName="Sara", lastName="Ventas", phoneNumber="3001234567" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AppUsers WHERE PartyId=@PartyId;",new SqlParameter("@PartyId",partyId)));
    }

    [Fact]
    public async Task Adding_the_seller_commercial_role_to_an_already_linked_user_assigns_access_automatically()
    {
        var countryId = await ScalarAsync<Guid>("SELECT TOP(1) CountryId FROM dbo.Countries WHERE IsActive=1 ORDER BY Code;");
        var divisionId = await ScalarAsync<Guid>("SELECT TOP(1) AdministrativeDivisionId FROM dbo.AdministrativeDivisions WHERE CountryId=@CountryId AND IsActive=1 ORDER BY Code;",new SqlParameter("@CountryId",countryId));
        var cityId = await ScalarAsync<Guid>("SELECT TOP(1) CityId FROM dbo.Cities WHERE AdministrativeDivisionId=@DivisionId AND IsActive=1 ORDER BY Code;",new SqlParameter("@DivisionId",divisionId));
        var partyId=Guid.NewGuid();var userId=Guid.NewGuid();var roleId=Guid.NewGuid();var suffix=Guid.NewGuid().ToString("N");
        await ExecuteAsync(
            """
            INSERT dbo.Parties(PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,Identification,NormalizedIdentification,DisplayName,FirstName,LastName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES(@PartyId,@TenantId,N'NaturalPerson',@CountryId,N'CC',@Identification,@Identification,N'Vendedor existente',N'Vendedor',N'Existente',N'Complete',1,@ActorId,SYSDATETIMEOFFSET());
            INSERT dbo.AppUsers(UserId,TenantId,PartyId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,AccessFailedCount,EmailConfirmed,IsActive,CreatedAt)
            VALUES(@UserId,@TenantId,@PartyId,@Username,@NormalizedUsername,@Email,@NormalizedEmail,N'Vendedor',N'Existente',0,1,1,SYSUTCDATETIME());
            IF NOT EXISTS(SELECT 1 FROM dbo.AppRoles WHERE TenantId=@TenantId AND NormalizedName=N'SELLER')
              INSERT dbo.AppRoles(RoleId,TenantId,Name,NormalizedName,Description,IsActive,IsSystemRole,CreatedAt)
              VALUES(@RoleId,@TenantId,N'Vendedor',N'SELLER',N'Integration seller role',1,0,SYSDATETIMEOFFSET());
            """,
            new SqlParameter("@PartyId",partyId),new SqlParameter("@UserId",userId),new SqlParameter("@RoleId",roleId),
            new SqlParameter("@TenantId",fixture.TenantId),new SqlParameter("@ActorId",fixture.UserId),new SqlParameter("@CountryId",countryId),
            new SqlParameter("@Identification",$"7{suffix[..14]}"),new SqlParameter("@Username",$"existing-{suffix}"),new SqlParameter("@NormalizedUsername",$"EXISTING-{suffix}"),
            new SqlParameter("@Email",$"existing-{suffix}@auraly.test"),new SqlParameter("@NormalizedEmail",$"EXISTING-{suffix}@AURALY.TEST"));
        var request=new CreateSellerRequest(Guid.NewGuid(),fixture.BusinessId,
            new PartyInput(PartyTypes.NaturalPerson,countryId,"CC",$"7{suffix[..14]}",null,"Vendedor existente",null,"Vendedor","Existente",$"existing-{suffix}@auraly.test","3001234567"),
            new PartySiteInput("PRINCIPAL","Principal",countryId,divisionId,cityId,"Calle 1",null,null,null,"3001234567",true),
            $"UE-{suffix[..10]}",null,"SaleAfterTax","Sale");
        using var admin=fixture.CreateAdminClient(
            PartyWorkspacePermissionCodes.SellerCreate,
            PartyWorkspacePermissionCodes.Read);
        using var response=await admin.PostAsJsonAsync("/api/commerce/v1/sellers",request);
        Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        Assert.Equal(1,await ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM dbo.UserRoles assignment
            JOIN dbo.AppRoles role ON role.RoleId=assignment.RoleId AND role.NormalizedName=N'SELLER'
            WHERE assignment.UserId=@UserId AND assignment.BusinessId=@BusinessId;
            """,new SqlParameter("@UserId",userId),new SqlParameter("@BusinessId",fixture.BusinessId)));
        var detail = await admin.GetFromJsonAsync<PartyWorkspaceDetail>(
            $"/api/commerce/v1/parties/{partyId:D}");
        Assert.NotNull(detail?.User);
        Assert.Contains(detail.User.Roles, assignment =>
            assignment.BusinessId == fixture.BusinessId &&
            assignment.RoleName == "Vendedor");
    }

    [Fact]
    public async Task Creating_a_seller_reuses_and_completes_the_incomplete_party_linked_to_the_same_email()
    {
        var countryId=await ScalarAsync<Guid>("SELECT TOP(1) CountryId FROM dbo.Countries WHERE IsActive=1 ORDER BY Code;");
        var divisionId=await ScalarAsync<Guid>("SELECT TOP(1) AdministrativeDivisionId FROM dbo.AdministrativeDivisions WHERE CountryId=@CountryId AND IsActive=1 ORDER BY Code;",new SqlParameter("@CountryId",countryId));
        var cityId=await ScalarAsync<Guid>("SELECT TOP(1) CityId FROM dbo.Cities WHERE AdministrativeDivisionId=@DivisionId AND IsActive=1 ORDER BY Code;",new SqlParameter("@DivisionId",divisionId));
        var partyId=Guid.NewGuid();var userId=Guid.NewGuid();var suffix=Guid.NewGuid().ToString("N");
        var email=$"incomplete-{suffix}@auraly.test";var identification=$"8{suffix[..14]}";
        await ExecuteAsync(
            """
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,FirstName,LastName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES(@PartyId,@TenantId,N'NaturalPerson',N'Administrador incompleto',N'Administrador',N'Incompleto',N'Incomplete',1,@ActorId,SYSDATETIMEOFFSET());
            INSERT dbo.AppUsers(UserId,TenantId,PartyId,Username,NormalizedUsername,Email,NormalizedEmail,FirstName,LastName,AccessFailedCount,EmailConfirmed,IsActive,CreatedAt)
            VALUES(@UserId,@TenantId,@PartyId,@Username,UPPER(@Username),@Email,UPPER(@Email),N'Administrador',N'Incompleto',0,1,1,SYSUTCDATETIME());
            IF NOT EXISTS(SELECT 1 FROM dbo.AppRoles WHERE TenantId=@TenantId AND NormalizedName=N'SELLER')
              INSERT dbo.AppRoles(RoleId,TenantId,Name,NormalizedName,Description,IsActive,IsSystemRole,CreatedAt)
              VALUES(NEWID(),@TenantId,N'Vendedor',N'SELLER',N'Integration seller role',1,0,SYSDATETIMEOFFSET());
            """,new SqlParameter("@PartyId",partyId),new SqlParameter("@UserId",userId),
            new SqlParameter("@TenantId",fixture.TenantId),new SqlParameter("@ActorId",fixture.UserId),
            new SqlParameter("@Username",$"incomplete-{suffix}"),new SqlParameter("@Email",email));
        var request=new CreateSellerRequest(Guid.NewGuid(),fixture.BusinessId,
            new PartyInput(PartyTypes.NaturalPerson,countryId,"CC",identification,null,"Administrador completo",null,"Administrador","Completo",email,"3001234567"),
            new PartySiteInput("PRINCIPAL","Principal",countryId,divisionId,cityId,"Calle 1",null,null,email,"3001234567",true),
            $"UI-{suffix[..10]}",null,"SaleAfterTax","Sale");
        using var admin=fixture.CreateAdminClient(PartyWorkspacePermissionCodes.SellerCreate,PartyWorkspacePermissionCodes.Read);
        using var response=await admin.PostAsJsonAsync("/api/commerce/v1/sellers",request);
        Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        Assert.Equal(1,await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.CommerceSellers WHERE TenantId=@TenantId AND PartyId=@PartyId;",
            new SqlParameter("@TenantId",fixture.TenantId),new SqlParameter("@PartyId",partyId)));
        Assert.Equal(1,await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Parties WHERE TenantId=@TenantId AND NormalizedIdentification=@Identification;",
            new SqlParameter("@TenantId",fixture.TenantId),new SqlParameter("@Identification",identification)));
        Assert.Equal(partyId,await ScalarAsync<Guid>("SELECT PartyId FROM dbo.AppUsers WHERE UserId=@UserId;",new SqlParameter("@UserId",userId)));
        Assert.Equal(1,await ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM dbo.UserRoles assignment
            JOIN dbo.AppRoles role ON role.RoleId=assignment.RoleId AND role.NormalizedName=N'SELLER'
            WHERE assignment.UserId=@UserId AND assignment.BusinessId=@BusinessId;
            """,new SqlParameter("@UserId",userId),new SqlParameter("@BusinessId",fixture.BusinessId)));
    }

    private async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T?> ScalarAsync<T>(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync();
        if (value is null or DBNull) return default;
        return value is T typed ? typed : (T)Convert.ChangeType(value, typeof(T));
    }
}
