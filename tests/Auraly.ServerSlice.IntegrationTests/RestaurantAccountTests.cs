using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class RestaurantAccountTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Opening_requires_restaurant_mode_active_waiter_and_a_free_table()
    {
        var waiterId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        using var waiter = fixture.CreateUserClient(waiterId, "restaurant.attend");
        using var administrator = fixture.CreateAdminClient(
            "businesses.update", "sales.create", "restaurant.attend");
        var tableId = Guid.NewGuid();
        RestaurantTable table;
        using (var create = await administrator.PostAsJsonAsync(
            "/api/commerce/v1/restaurant/tables",
            new CreateRestaurantTableRequest(tableId, fixture.BusinessId,
                $"A-{tableId:N}"[..10])))
        {
            create.EnsureSuccessStatusCode();
            table = (await create.Content.ReadFromJsonAsync<RestaurantTable>())!;
        }
        var request = new OpenRestaurantAccountRequest(Guid.NewGuid(),
            tableId, fixture.WarehouseId);
        try
        {
            using (var direct = await waiter.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/accounts", request))
                Assert.Equal(HttpStatusCode.Conflict, direct.StatusCode);
            await SetRestaurantModeAsync(true);
            using (var unlinked = await waiter.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/accounts", request))
                Assert.Equal(HttpStatusCode.Conflict, unlinked.StatusCode);
            await RegisterWaiterAsync(waiterId, partyId);
            using (var cashierCannotOpen = await administrator.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/accounts", request))
                Assert.Equal(HttpStatusCode.Conflict, cashierCannotOpen.StatusCode);

            RestaurantAccount account;
            using (var opened = await waiter.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/accounts", request))
            {
                Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
                account = (await opened.Content.ReadFromJsonAsync<RestaurantAccount>())!;
            }
            Assert.Equal("Open", account.Status);
            Assert.Equal(waiterId, account.ResponsibleUserId);
            Assert.Equal(1, account.Version);
            using (var replay = await waiter.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/accounts", request))
            {
                Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
                var replayAccount = await replay.Content.ReadFromJsonAsync<RestaurantAccount>();
                Assert.Equal(account.OpenedAt, replayAccount!.OpenedAt);
            }
            using (var occupied = await waiter.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/accounts",
                request with { AccountId = Guid.NewGuid() }))
                Assert.Equal(HttpStatusCode.Conflict, occupied.StatusCode);
            using (var renumber = await administrator.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/tables/{table.TableId}",
                new UpdateRestaurantTableRequest("MOVIDA", table.Name,
                    table.Capacity, table.RoomId, true, table.RowVersion)))
                Assert.Equal(HttpStatusCode.Conflict, renumber.StatusCode);
            using (var read = await administrator.GetAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}"))
                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var states = await administrator.GetFromJsonAsync<
                IReadOnlyList<RestaurantTableOccupancy>>(
                "/api/commerce/v1/restaurant/occupancy");
            Assert.Contains(states!, state => state.TableId == tableId &&
                state.AccountId == account.AccountId && state.State == "Free");
            var products = await waiter.GetFromJsonAsync<OnlineSalesProductPage>(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/products?search=P-E2E&take=50");
            Assert.Contains(products!.Items,
                product => product.ProductId == fixture.ProductId);
            var categories = await waiter.GetFromJsonAsync<
                IReadOnlyList<RestaurantMenuCategory>>(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/categories");
            Assert.NotNull(categories);
            var seededProduct = products.Items.Single(
                product => product.ProductId == fixture.ProductId);
            if (seededProduct.ProductCategoryId is { } categoryId)
            {
                Assert.Contains(categories, category =>
                    category.CategoryId == categoryId);
                var filtered = await waiter.GetFromJsonAsync<
                    OnlineSalesProductPage>(
                    $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/products?search=P-E2E&categoryId={categoryId:D}&take=50");
                Assert.Contains(filtered!.Items,
                    product => product.ProductId == fixture.ProductId);
            }
            var first = new AddRestaurantAccountItemRequest(Guid.NewGuid(),
                fixture.ProductId, 2, account.Version);
            RestaurantAccountDraft draft;
            var addStarted = Stopwatch.GetTimestamp();
            using (var added = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                first))
            {
                Assert.Equal(HttpStatusCode.OK, added.StatusCode);
                draft = (await added.Content.ReadFromJsonAsync<RestaurantAccountDraft>())!;
            }
            var addElapsed = Stopwatch.GetElapsedTime(addStarted);
            Assert.True(addElapsed < TimeSpan.FromSeconds(1),
                $"Agregar el primer producto tardó {addElapsed.TotalMilliseconds:F0} ms.");
            Assert.Equal(2, draft.Account.Version);
            Assert.Contains(draft.Additions, item => item.AdditionId == first.AdditionId &&
                item.AddedByUserId == waiterId && item.Status == "Direct");
            states = await administrator.GetFromJsonAsync<
                IReadOnlyList<RestaurantTableOccupancy>>(
                "/api/commerce/v1/restaurant/occupancy");
            Assert.Contains(states!, state => state.TableId == tableId &&
                state.AccountId == account.AccountId && state.State == "Open");
            using (var noCommand = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/commands",
                new SendRestaurantPendingRequest(Guid.NewGuid(), 2)))
                Assert.Equal(HttpStatusCode.Conflict, noCommand.StatusCode);
            using (var prebill = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/prebill",
                new PrintRestaurantPrebillRequest(2)))
            {
                prebill.EnsureSuccessStatusCode();
                var printed = (await prebill.Content.ReadFromJsonAsync<RestaurantPrebill>())!;
                Assert.Equal(2, Assert.Single(printed.Lines).Quantity);
            }
            using (var replay = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                first))
            {
                replay.EnsureSuccessStatusCode();
                var unchanged = await replay.Content.ReadFromJsonAsync<RestaurantAccountDraft>();
                Assert.Equal(2, unchanged!.Account.Version);
                Assert.Single(unchanged.Additions);
            }
            using (var stale = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                first with { AdditionId = Guid.NewGuid() }))
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var nextWaiterId = Guid.NewGuid();
            var nextPartyId = Guid.NewGuid();
            using var nextWaiter = fixture.CreateUserClient(nextWaiterId,
                "restaurant.attend");
            await RegisterWaiterAsync(nextWaiterId, nextPartyId);
            var secondAdditionId = Guid.NewGuid();
            using (var second = await nextWaiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                new AddRestaurantAccountItemRequest(secondAdditionId,
                    fixture.ProductId, 1, 2)))
            {
                second.EnsureSuccessStatusCode();
                var twoAuthors = (await second.Content.ReadFromJsonAsync<RestaurantAccountDraft>())!;
                Assert.Equal(3, twoAuthors.Account.Version);
                Assert.Equal(2, twoAuthors.Additions.Count);
                Assert.Contains(twoAuthors.Additions,
                    item => item.AddedByUserId == nextWaiterId);
            }
            var reduction = new ChangeRestaurantAccountQuantityRequest(
                Guid.NewGuid(), 1, 3);
            var removeStarted = Stopwatch.GetTimestamp();
            using (var changed = await nextWaiter.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions/{first.AdditionId}/quantity",
                reduction))
            {
                changed.EnsureSuccessStatusCode();
                var adjusted = (await changed.Content.ReadFromJsonAsync<RestaurantAccountDraft>())!;
                Assert.Equal(4, adjusted.Account.Version);
                Assert.Equal(1, adjusted.Additions.Single(item =>
                    item.AdditionId == first.AdditionId).Quantity);
            }
            var removeElapsed = Stopwatch.GetElapsedTime(removeStarted);
            Assert.True(removeElapsed < TimeSpan.FromSeconds(1),
                $"Reducir el producto tardó {removeElapsed.TotalMilliseconds:F0} ms.");
            using (var replayChange = await nextWaiter.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions/{first.AdditionId}/quantity",
                reduction))
            {
                replayChange.EnsureSuccessStatusCode();
                var unchanged = await replayChange.Content.ReadFromJsonAsync<RestaurantAccountDraft>();
                Assert.Equal(4, unchanged!.Account.Version);
            }
            using (var replayOriginalAdd = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                first))
            {
                replayOriginalAdd.EnsureSuccessStatusCode();
                var unchanged = await replayOriginalAdd.Content.ReadFromJsonAsync<RestaurantAccountDraft>();
                Assert.Equal(4, unchanged!.Account.Version);
                Assert.Equal(1, unchanged.Additions.Single(item =>
                    item.AdditionId == first.AdditionId).Quantity);
            }
            using (var staleChange = await waiter.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions/{first.AdditionId}/quantity",
                new ChangeRestaurantAccountQuantityRequest(Guid.NewGuid(), 1, 3)))
                Assert.Equal(HttpStatusCode.Conflict, staleChange.StatusCode);
            using (var cancelPending = await waiter.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions/{first.AdditionId}/quantity",
                new ChangeRestaurantAccountQuantityRequest(Guid.NewGuid(), 0, 4)))
            {
                cancelPending.EnsureSuccessStatusCode();
                var corrected = (await cancelPending.Content.ReadFromJsonAsync<RestaurantAccountDraft>())!;
                Assert.Equal(5, corrected.Account.Version);
                Assert.Equal("Cancelled", corrected.Additions.Single(item =>
                    item.AdditionId == first.AdditionId).Status);
                Assert.Single(corrected.Additions,
                    item => item.Status == "Direct");
            }
            using (var cashierAdd = await administrator.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                new AddRestaurantAccountItemRequest(Guid.NewGuid(),
                    fixture.ProductId, 1, 5)))
                Assert.Equal(HttpStatusCode.Forbidden, cashierAdd.StatusCode);
            using (var cashierChange = await administrator.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions/{secondAdditionId}/quantity",
                new ChangeRestaurantAccountQuantityRequest(Guid.NewGuid(), 0, 5)))
                Assert.Equal(HttpStatusCode.Forbidden, cashierChange.StatusCode);
            Assert.Equal(1, await CountOpeningAuditsAsync(account.AccountId));
            Assert.Equal(2, await CountAdditionAuditsAsync(account.AccountId));
            Assert.Equal(2, await CountQuantityAuditsAsync(account.AccountId));

            var directCashierId = Guid.NewGuid();
            using var directCashier = fixture.CreateUserClient(directCashierId,
                "sales.create", "restaurant.tables.open_without_waiter");
            var cashierTableId = Guid.NewGuid();
            using (var tableCreated = await administrator.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/tables",
                new CreateRestaurantTableRequest(cashierTableId,
                    fixture.BusinessId, $"C-{cashierTableId:N}"[..10])))
                tableCreated.EnsureSuccessStatusCode();
            RestaurantAccount cashierAccount;
            using (var opened = await directCashier.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/accounts",
                new OpenRestaurantAccountRequest(Guid.NewGuid(),
                    cashierTableId, fixture.WarehouseId)))
            {
                opened.EnsureSuccessStatusCode();
                cashierAccount = (await opened.Content
                    .ReadFromJsonAsync<RestaurantAccount>())!;
            }
            Assert.Equal(directCashierId, cashierAccount.ResponsibleUserId);
            states = await administrator.GetFromJsonAsync<
                IReadOnlyList<RestaurantTableOccupancy>>(
                "/api/commerce/v1/restaurant/occupancy");
            Assert.Contains(states!, state => state.TableId == cashierTableId &&
                state.AccountId == cashierAccount.AccountId && state.State == "Free");
            var cashierProducts = await directCashier.GetFromJsonAsync<OnlineSalesProductPage>(
                $"/api/commerce/v1/restaurant/accounts/{cashierAccount.AccountId}/products?search=P-E2E&take=50");
            Assert.Contains(cashierProducts!.Items,
                product => product.ProductId == fixture.ProductId);
            var cashierCategories = await directCashier.GetFromJsonAsync<
                IReadOnlyList<RestaurantMenuCategory>>(
                $"/api/commerce/v1/restaurant/accounts/{cashierAccount.AccountId}/categories");
            Assert.NotNull(cashierCategories);
            var cashierAddition = new AddRestaurantAccountItemRequest(Guid.NewGuid(),
                fixture.ProductId, 1, cashierAccount.Version);
            using (var added = await directCashier.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{cashierAccount.AccountId}/additions",
                cashierAddition))
            {
                added.EnsureSuccessStatusCode();
                var cashierDraft = (await added.Content
                    .ReadFromJsonAsync<RestaurantAccountDraft>())!;
                Assert.Contains(cashierDraft.Additions, item =>
                    item.AdditionId == cashierAddition.AdditionId &&
                    item.AddedByUserId == directCashierId);
            }
            using (var removed = await directCashier.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{cashierAccount.AccountId}/additions/{cashierAddition.AdditionId}/quantity",
                new ChangeRestaurantAccountQuantityRequest(Guid.NewGuid(), 0, 2)))
                removed.EnsureSuccessStatusCode();
            states = await administrator.GetFromJsonAsync<
                IReadOnlyList<RestaurantTableOccupancy>>(
                "/api/commerce/v1/restaurant/occupancy");
            Assert.Contains(states!, state => state.TableId == cashierTableId &&
                state.AccountId == cashierAccount.AccountId && state.State == "Free");

            var parallelTableId = Guid.NewGuid();
            using (var tableCreated = await administrator.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/tables",
                new CreateRestaurantTableRequest(parallelTableId,
                    fixture.BusinessId, $"P-{parallelTableId:N}"[..10])))
                tableCreated.EnsureSuccessStatusCode();
            var races = await Task.WhenAll(
                waiter.PostAsJsonAsync("/api/commerce/v1/restaurant/accounts",
                    new OpenRestaurantAccountRequest(Guid.NewGuid(),
                        parallelTableId, fixture.WarehouseId)),
                waiter.PostAsJsonAsync("/api/commerce/v1/restaurant/accounts",
                    new OpenRestaurantAccountRequest(Guid.NewGuid(),
                        parallelTableId, fixture.WarehouseId)));
            try
            {
                Assert.Single(races,
                    response => response.StatusCode == HttpStatusCode.Created);
                Assert.Single(races,
                    response => response.StatusCode == HttpStatusCode.Conflict);
            }
            finally
            {
                foreach (var response in races) response.Dispose();
            }
            var additions = await Task.WhenAll(
                waiter.PostAsJsonAsync(
                    $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                    new AddRestaurantAccountItemRequest(Guid.NewGuid(),
                        fixture.ProductId, 1, 5)),
                nextWaiter.PostAsJsonAsync(
                    $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                    new AddRestaurantAccountItemRequest(Guid.NewGuid(),
                        fixture.ProductId, 1, 5)));
            try
            {
                Assert.Single(additions,
                    response => response.StatusCode == HttpStatusCode.OK);
                Assert.Single(additions,
                    response => response.StatusCode == HttpStatusCode.Conflict);
            }
            finally
            {
                foreach (var response in additions) response.Dispose();
            }
        }
        finally
        {
            await SetRestaurantModeAsync(false);
        }
    }

    private async Task RegisterWaiterAsync(Guid userId, Guid partyId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.Parties
                (PartyId,TenantId,PartyType,DisplayName,FirstName,LastName,
                 CompletionStatus,IsActive,CreatedAt)
            VALUES(@PartyId,@TenantId,N'NaturalPerson',N'Mesero prueba',
                   N'Mesero',N'Prueba',N'Incomplete',1,SYSDATETIMEOFFSET());
            INSERT parties.Waiters
                (WaiterId,TenantId,PartyId,Code,IsActive,CreatedAt)
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

    private async Task SetRestaurantModeAsync(bool enabled)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.Businesses SET PointOfSaleMode=@Mode
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@Mode", enabled ? "RestaurantBar" : "DirectSale");
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountOpeningAuditsAsync(Guid accountId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.AuditLogs
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId
              AND EntityId=@AccountId AND Action=N'RestaurantAccountOpened';
            """, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@AccountId", accountId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> CountAdditionAuditsAsync(Guid accountId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.AuditLogs
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId
              AND EntityId=@AccountId
              AND Action=N'RestaurantAccountAdditionCreated';
            """, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@AccountId", accountId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> CountQuantityAuditsAsync(Guid accountId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.AuditLogs
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId
              AND EntityId=@AccountId
              AND Action=N'RestaurantAccountQuantityChanged';
            """, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@AccountId", accountId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
