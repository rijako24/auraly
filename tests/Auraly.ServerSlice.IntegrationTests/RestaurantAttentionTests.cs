using System.Net;
using System.Net.Http.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class RestaurantAttentionTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Shared_cashier_attention_attributes_waiter_and_cannot_cross_tables_or_outlive_exit()
    {
        var waiterId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var otherWaiterId = Guid.NewGuid();
        var otherPartyId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var otherTableId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        using var waiter = fixture.CreateUserClient(waiterId, "restaurant.attend");
        using var otherWaiter = fixture.CreateUserClient(otherWaiterId, "restaurant.attend");
        using var cashier = fixture.CreateAdminClient("sales.create",
            "restaurant.attend", "pos.approvals.manage_credential");
        await PrepareAsync(waiterId, partyId, tableId, otherTableId, grantId);
        await RegisterAdditionalWaiterAsync(otherWaiterId, otherPartyId);
        try
        {
            var page = await cashier.GetFromJsonAsync<RestaurantWaiterPage>(
                "/api/commerce/v1/restaurant/waiters");
            Assert.True(page!.RequireCredential);
            Assert.Contains(page.Items, option => option.UserId == waiterId);

            using (var created = await cashier.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/waiters/{waiterId}/secondary-credential",
                new { secret = "1234" }))
                Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);
            using (var incorrect = await cashier.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/attention",
                new { tableId, waiterUserId = waiterId, secret = "9999" }))
                Assert.Equal(HttpStatusCode.Forbidden, incorrect.StatusCode);
            RestaurantAttentionStarted grant;
            using (var accepted = await cashier.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/attention",
                new { tableId, waiterUserId = waiterId, secret = "1234" }))
            {
                accepted.EnsureSuccessStatusCode();
                grant = (await accepted.Content.ReadFromJsonAsync<RestaurantAttentionStarted>())!;
            }

            var accountId = Guid.NewGuid();
            using (var crossed = await SendWithAttentionAsync(cashier,
                HttpMethod.Post, "/api/commerce/v1/restaurant/accounts",
                grant.Token, new OpenRestaurantAccountRequest(Guid.NewGuid(),
                    otherTableId, fixture.WarehouseId)))
                Assert.Equal(HttpStatusCode.Forbidden, crossed.StatusCode);
            using (var opened = await SendWithAttentionAsync(cashier,
                HttpMethod.Post, "/api/commerce/v1/restaurant/accounts",
                grant.Token, new OpenRestaurantAccountRequest(accountId,
                    tableId, fixture.WarehouseId)))
            {
                Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
                var account = await opened.Content.ReadFromJsonAsync<RestaurantAccount>();
                Assert.Equal(waiterId, account!.ResponsibleUserId);
            }
            var additionId = Guid.NewGuid();
            using (var added = await SendWithAttentionAsync(cashier,
                HttpMethod.Post,
                $"/api/commerce/v1/restaurant/accounts/{accountId}/additions",
                grant.Token, new AddRestaurantAccountItemRequest(additionId,
                    fixture.ProductId, 1, 1)))
            {
                added.EnsureSuccessStatusCode();
                var draft = await added.Content.ReadFromJsonAsync<RestaurantAccountDraft>();
                Assert.Equal(waiterId, Assert.Single(draft!.Additions).AddedByUserId);
            }
            using (var ended = await SendWithAttentionAsync(cashier,
                HttpMethod.Delete, "/api/commerce/v1/restaurant/attention",
                grant.Token))
                Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
            using (var reused = await SendWithAttentionAsync(cashier,
                HttpMethod.Get,
                $"/api/commerce/v1/restaurant/accounts/{accountId}/draft",
                grant.Token))
                Assert.Equal(HttpStatusCode.Forbidden, reused.StatusCode);

            await SetAttentionPolicyAsync(false, false);
            RestaurantAttentionStarted openSelection;
            using (var selected = await cashier.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/attention",
                new { tableId, waiterUserId = waiterId }))
            {
                selected.EnsureSuccessStatusCode();
                openSelection = (await selected.Content.ReadFromJsonAsync<RestaurantAttentionStarted>())!;
            }
            using (var readable = await SendWithAttentionAsync(cashier,
                HttpMethod.Get,
                $"/api/commerce/v1/restaurant/accounts/{accountId}/draft",
                openSelection.Token))
                Assert.Equal(HttpStatusCode.OK, readable.StatusCode);
            await SetAttentionPolicyAsync(true, true);
            using (var invalidated = await SendWithAttentionAsync(cashier,
                HttpMethod.Get,
                $"/api/commerce/v1/restaurant/accounts/{accountId}/draft",
                openSelection.Token))
                Assert.Equal(HttpStatusCode.Forbidden, invalidated.StatusCode);
            using (var blockedOtherWaiter = await cashier.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/attention",
                new { tableId, waiterUserId = otherWaiterId, secret = "1234" }))
                Assert.Equal(HttpStatusCode.Forbidden, blockedOtherWaiter.StatusCode);
            using (var blockedTablet = await otherWaiter.GetAsync(
                $"/api/commerce/v1/restaurant/accounts/{accountId}/draft"))
                Assert.Equal(HttpStatusCode.NotFound, blockedTablet.StatusCode);
            using (var blockedAddition = await otherWaiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{accountId}/additions",
                new AddRestaurantAccountItemRequest(Guid.NewGuid(),
                    fixture.ProductId, 1, 2)))
                Assert.Equal(HttpStatusCode.Conflict, blockedAddition.StatusCode);

            using var directCashier = fixture.CreateUserClient(Guid.NewGuid(),
                "sales.create", "restaurant.tables.open_without_waiter",
                "pos.approvals.manage_credential");
            using (var credential = await directCashier.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/waiters/{otherWaiterId}/secondary-credential",
                new { secret = "5678" }))
                Assert.Equal(HttpStatusCode.NoContent, credential.StatusCode);
            RestaurantAttentionStarted transfer;
            using (var selected = await directCashier.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/attention",
                new { tableId, waiterUserId = otherWaiterId, secret = "5678" }))
            {
                selected.EnsureSuccessStatusCode();
                transfer = (await selected.Content
                    .ReadFromJsonAsync<RestaurantAttentionStarted>())!;
            }
            var transferred = await directCashier.GetFromJsonAsync<RestaurantAccountDraft>(
                $"/api/commerce/v1/restaurant/accounts/{accountId}/draft");
            Assert.Equal(otherWaiterId, transferred!.Account.ResponsibleUserId);
            Assert.Equal(3, transferred.Account.Version);
            using (var addedAfterTransfer = await SendWithAttentionAsync(
                directCashier, HttpMethod.Post,
                $"/api/commerce/v1/restaurant/accounts/{accountId}/additions",
                transfer.Token, new AddRestaurantAccountItemRequest(Guid.NewGuid(),
                    fixture.ProductId, 1, transferred.Account.Version)))
            {
                addedAfterTransfer.EnsureSuccessStatusCode();
                var draft = await addedAfterTransfer.Content
                    .ReadFromJsonAsync<RestaurantAccountDraft>();
                Assert.Contains(draft!.Additions, item =>
                    item.AddedByUserId == otherWaiterId);
            }
        }
        finally
        {
            await CleanupAsync(grantId);
        }
    }

    private static async Task<HttpResponseMessage> SendWithAttentionAsync(
        HttpClient client, HttpMethod method, string uri, string token,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("X-Restaurant-Attention", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private async Task PrepareAsync(Guid waiterId, Guid partyId,
        Guid tableId, Guid otherTableId, Guid grantId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.Businesses
            SET PointOfSaleMode=N'RestaurantBar',
                RestaurantRequireWaiterCredential=1,
                RestaurantRestrictTableToWaiter=0
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,
                FirstName,LastName,CompletionStatus,IsActive,CreatedAt)
            VALUES(@PartyId,@TenantId,N'NaturalPerson',N'Mesero prueba',
                N'Mesero',N'Prueba',N'Incomplete',1,SYSDATETIMEOFFSET());
            INSERT parties.Waiters(WaiterId,TenantId,PartyId,Code,IsActive,CreatedAt)
            VALUES(NEWID(),@TenantId,@PartyId,@WaiterCode,1,SYSDATETIMEOFFSET());
            UPDATE dbo.AppUsers SET PartyId=@PartyId
            WHERE UserId=@WaiterId AND TenantId=@TenantId;
            INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
            SELECT @GrantId,@RoleId,PermissionId,SYSDATETIMEOFFSET()
            FROM dbo.Permissions WHERE Resource=N'restaurant.attend'
              AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions
                WHERE RoleId=@RoleId AND PermissionId=dbo.Permissions.PermissionId);
            INSERT sales.RestaurantTables(TableId,TenantId,BusinessId,Code,
                IsActive,CreatedAt,UpdatedAt)
            VALUES(@TableId,@TenantId,@BusinessId,@TableCode,1,
                   SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET()),
                  (@OtherTableId,@TenantId,@BusinessId,@OtherTableCode,1,
                   SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET());
            """, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WaiterId", waiterId);
        command.Parameters.AddWithValue("@PartyId", partyId);
        command.Parameters.AddWithValue("@WaiterCode", $"W-{partyId:N}"[..12]);
        command.Parameters.AddWithValue("@RoleId", fixture.RoleId);
        command.Parameters.AddWithValue("@GrantId", grantId);
        command.Parameters.AddWithValue("@TableId", tableId);
        command.Parameters.AddWithValue("@OtherTableId", otherTableId);
        command.Parameters.AddWithValue("@TableCode", $"T-{tableId:N}"[..12]);
        command.Parameters.AddWithValue("@OtherTableCode", $"T-{otherTableId:N}"[..12]);
        await command.ExecuteNonQueryAsync();
    }

    private async Task CleanupAsync(Guid grantId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.Businesses SET PointOfSaleMode=N'DirectSale',
                RestaurantRequireWaiterCredential=1,
                RestaurantRestrictTableToWaiter=0
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
            DELETE dbo.RolePermissions WHERE RolePermissionId=@GrantId;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@GrantId", grantId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task RegisterAdditionalWaiterAsync(Guid userId, Guid partyId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,
                FirstName,LastName,CompletionStatus,IsActive,CreatedAt)
            VALUES(@PartyId,@TenantId,N'NaturalPerson',N'Otro mesero',
                N'Otro',N'Mesero',N'Incomplete',1,SYSDATETIMEOFFSET());
            INSERT parties.Waiters(WaiterId,TenantId,PartyId,Code,IsActive,CreatedAt)
            VALUES(NEWID(),@TenantId,@PartyId,@Code,1,SYSDATETIMEOFFSET());
            UPDATE dbo.AppUsers SET PartyId=@PartyId
            WHERE UserId=@UserId AND TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@PartyId", partyId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Code", $"W-{partyId:N}"[..12]);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SetAttentionPolicyAsync(bool requireCredential,
        bool restrictTableToWaiter)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.Businesses
            SET RestaurantRequireWaiterCredential=@Require,
                RestaurantRestrictTableToWaiter=@Restrict
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@Require", requireCredential);
        command.Parameters.AddWithValue("@Restrict", restrictTableToWaiter);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        await command.ExecuteNonQueryAsync();
    }
}
