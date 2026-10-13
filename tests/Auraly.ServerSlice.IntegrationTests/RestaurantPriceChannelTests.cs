using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Parties;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class RestaurantPriceChannelTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Selected_channel_reprices_existing_items_and_customer_channel_takes_priority()
    {
        var channelId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var partySiteId = Guid.NewGuid();
        decimal channelAmount;
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var setup = new SqlCommand("""
                UPDATE dbo.Businesses SET PointOfSaleMode=N'RestaurantBar'
                WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
                DECLARE @Base DECIMAL(19,4)=(SELECT TOP(1) Amount
                    FROM dbo.ProductPrices WHERE BusinessId=@BusinessId
                      AND ProductId=@ProductId AND IsActive=1);
                INSERT dbo.PriceChannels(PriceChannelId,TenantId,Code,Name,
                    Strategy,IsActive,CreatedAt)
                VALUES(@ChannelId,@TenantId,@Code,N'Para llevar',
                    N'TieredProductPrice',1,SYSDATETIMEOFFSET());
                INSERT dbo.PriceChannelItems(PriceChannelItemId,PriceChannelId,
                    ProductId,MinimumQuantity,Amount,CurrencyCode,ValidFrom,
                    IsActive,CreatedAt)
                VALUES(NEWID(),@ChannelId,@ProductId,1,@Base*2,N'COP',
                    SYSDATETIMEOFFSET(),1,SYSDATETIMEOFFSET());
                INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,
                    CompletionStatus,IsActive,CreatedBy,CreatedAt)
                VALUES(@PartyId,@TenantId,N'NaturalPerson',N'Cliente restaurante',
                    N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());
                INSERT dbo.Customers(CustomerId,PartyId,TenantId,
                    RequiresElectronicInvoice,IsActive,CreatedBy,CreatedAt)
                VALUES(@CustomerId,@PartyId,@TenantId,0,1,@UserId,SYSDATETIMEOFFSET());
                INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,
                    AdministrativeDivisionId,CityId,AddressLine,IsPrimary,IsActive,
                    CreatedBy,CreatedAt)
                SELECT TOP(1) @PartySiteId,@PartyId,N'PRINCIPAL',N'Principal',
                    country.CountryId,division.AdministrativeDivisionId,city.CityId,
                    N'CL 1 # 2-3',1,1,@UserId,SYSDATETIMEOFFSET()
                FROM dbo.Countries country
                JOIN dbo.AdministrativeDivisions division ON division.CountryId=country.CountryId
                JOIN dbo.Cities city ON city.AdministrativeDivisionId=division.AdministrativeDivisionId
                WHERE country.Code=N'CO'
                ORDER BY division.AdministrativeDivisionId,city.CityId;
                INSERT dbo.CustomerPricingSettings(CustomerId,PriceChannelId,
                    UpdatedBy,UpdatedAt)
                VALUES(@CustomerId,@ChannelId,@UserId,SYSDATETIMEOFFSET());
                SELECT @Base*2;
                """, connection);
            setup.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            setup.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            setup.Parameters.AddWithValue("@ProductId", fixture.ProductId);
            setup.Parameters.AddWithValue("@ChannelId", channelId);
            setup.Parameters.AddWithValue("@Code", $"TAKE-{channelId:N}"[..20]);
            setup.Parameters.AddWithValue("@CustomerId", customerId);
            setup.Parameters.AddWithValue("@PartyId", partyId);
            setup.Parameters.AddWithValue("@PartySiteId", partySiteId);
            setup.Parameters.AddWithValue("@UserId", fixture.UserId);
            channelAmount = (decimal)(await setup.ExecuteScalarAsync())!;
        }
        try
        {
            using var client = fixture.CreateAdminClient("businesses.update",
                "restaurant.tables.open_without_waiter",
                "restaurant.price_channels.select");
            using (var created = await client.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/tables",
                new CreateRestaurantTableRequest(tableId, fixture.BusinessId,
                    $"T-{tableId:N}"[..12])))
                created.EnsureSuccessStatusCode();
            using (var opened = await client.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/accounts",
                new OpenRestaurantAccountRequest(accountId, tableId,
                    fixture.WarehouseId)))
                opened.EnsureSuccessStatusCode();
            var customers = await client.GetFromJsonAsync<PartyRoleOptionPage>(
                "/api/commerce/v1/parties/role-options?role=Customer&search=Cliente&page=1&pageSize=20");
            Assert.Contains(customers!.Items, customer => customer.RoleId == customerId);
            using (var waiterOptions = fixture.CreateAdminClient("restaurant.attend"))
            {
                var waiterCustomers = await waiterOptions.GetFromJsonAsync<PartyRoleOptionPage>(
                    "/api/commerce/v1/parties/role-options?role=Customer&search=Cliente&page=1&pageSize=20");
                Assert.Contains(waiterCustomers!.Items, customer => customer.RoleId == customerId);
            }

            using (var waiter = fixture.CreateAdminClient(
                "restaurant.tables.open_without_waiter"))
            using (var forbidden = await waiter.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{accountId}/price-channel",
                new ChangeRestaurantPriceChannelRequest(channelId, 1)))
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            using var changed = await client.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{accountId}/price-channel",
                new ChangeRestaurantPriceChannelRequest(channelId, 1));
            changed.EnsureSuccessStatusCode();
            var selected = (await changed.Content.ReadFromJsonAsync<
                RestaurantAccountDraft>())!;
            Assert.Equal(channelId, selected.Account.PriceChannelId);
            Assert.Equal(2, selected.Account.Version);

            var menu = await client.GetFromJsonAsync<OnlineSalesProductPage>(
                $"/api/commerce/v1/restaurant/accounts/{accountId}/products?take=40");
            Assert.NotNull(menu);
            var product = Assert.Single(menu.Items, item =>
                item.ProductId == fixture.ProductId);
            Assert.Equal(channelAmount, product.UnitPrice);
            Assert.Equal("PriceChannel", product.PriceSource);

            using var added = await client.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{accountId}/additions",
                new AddRestaurantAccountItemRequest(Guid.NewGuid(),
                    fixture.ProductId, 1, selected.Account.Version));
            added.EnsureSuccessStatusCode();
            var draft = (await added.Content.ReadFromJsonAsync<
                RestaurantAccountDraft>())!;
            Assert.Equal(channelAmount, Assert.Single(draft.Additions).PublicUnitPrice);
            using var cleared = await client.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{accountId}/price-channel",
                new ChangeRestaurantPriceChannelRequest(null,
                    draft.Account.Version));
            cleared.EnsureSuccessStatusCode();
            var withoutChannel = (await cleared.Content.ReadFromJsonAsync<RestaurantAccountDraft>())!;
            Assert.Null(withoutChannel.Account.PriceChannelId);
            Assert.Equal(channelAmount / 2m, Assert.Single(withoutChannel.Additions).PublicUnitPrice);

            using var selectedCustomer = await client.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{accountId}/customer",
                new ChangeRestaurantCustomerRequest(customerId, null, withoutChannel.Account.Version));
            selectedCustomer.EnsureSuccessStatusCode();
            var customerDraft = (await selectedCustomer.Content.ReadFromJsonAsync<RestaurantAccountDraft>())!;
            Assert.Equal(customerId, customerDraft.Account.CustomerId);
            Assert.Equal(partySiteId, customerDraft.Account.CustomerPartySiteId);
            Assert.Equal(channelId, customerDraft.Account.PriceChannelId);
            Assert.Equal("Para llevar", customerDraft.Account.PriceChannelName);
            Assert.True(customerDraft.Account.CustomerPriceChannelLocked);
            Assert.Equal(channelAmount, Assert.Single(customerDraft.Additions).PublicUnitPrice);
            var workSessionId = await fixture.OpenWebWorkSessionAsync();
            using (var scope = fixture.CreateScope())
            {
                var checkoutStore = scope.ServiceProvider.GetRequiredService<IOnlineSalesCheckoutStore>();
                var source = await checkoutStore.ReadRestaurantSourceAsync(
                    new OnlineSalesUserIdentity(fixture.UserId, fixture.TenantId,
                        new HashSet<string>()), accountId, workSessionId,
                    CancellationToken.None);
                Assert.Equal(customerId, source.CustomerId);
                Assert.Equal(partySiteId, source.CustomerPartySiteId);
            }

            using var blocked = await client.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{accountId}/price-channel",
                new ChangeRestaurantPriceChannelRequest(null, customerDraft.Account.Version));
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

            using var removedCustomer = await client.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{accountId}/customer",
                new ChangeRestaurantCustomerRequest(null, null, customerDraft.Account.Version));
            removedCustomer.EnsureSuccessStatusCode();
            var restored = (await removedCustomer.Content.ReadFromJsonAsync<RestaurantAccountDraft>())!;
            Assert.Null(restored.Account.CustomerId);
            Assert.Null(restored.Account.PriceChannelId);
            Assert.Equal(channelAmount / 2m, Assert.Single(restored.Additions).PublicUnitPrice);
        }
        finally
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var cleanup = new SqlCommand("""
                DELETE sales.RestaurantAccountAdditions WHERE AccountId=@AccountId;
                DELETE sales.RestaurantAccounts WHERE AccountId=@AccountId;
                DELETE sales.RestaurantTables WHERE TableId=@TableId;
                DELETE dbo.CustomerPricingSettings WHERE CustomerId=@CustomerId;
                DELETE dbo.PartySites WHERE PartySiteId=@PartySiteId;
                DELETE dbo.Customers WHERE CustomerId=@CustomerId;
                DELETE dbo.Parties WHERE PartyId=@PartyId;
                DELETE dbo.PriceChannelItems WHERE PriceChannelId=@ChannelId;
                DELETE dbo.PriceChannels WHERE PriceChannelId=@ChannelId;
                UPDATE dbo.Businesses SET PointOfSaleMode=N'DirectSale'
                WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
                """, connection);
            cleanup.Parameters.AddWithValue("@AccountId", accountId);
            cleanup.Parameters.AddWithValue("@TableId", tableId);
            cleanup.Parameters.AddWithValue("@ChannelId", channelId);
            cleanup.Parameters.AddWithValue("@CustomerId", customerId);
            cleanup.Parameters.AddWithValue("@PartyId", partyId);
            cleanup.Parameters.AddWithValue("@PartySiteId", partySiteId);
            cleanup.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            cleanup.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
