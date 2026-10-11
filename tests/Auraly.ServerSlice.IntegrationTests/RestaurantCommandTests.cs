using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class RestaurantCommandTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Sending_pending_is_atomic_routed_and_idempotent()
    {
        var waiterId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var tableCode = $"T-{tableId:N}"[..12];
        var stationId = Guid.NewGuid();
        using var waiter = fixture.CreateUserClient(waiterId, "restaurant.attend");
        using var admin = fixture.CreateAdminClient("businesses.update", "sales.create");
        using var kitchen = fixture.CreateAdminClient(
            "restaurant.commands.read", "restaurant.commands.manage");
        await PrepareWaiterAsync(waiterId, partyId);
        try
        {
            using (var created = await admin.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/tables",
                new CreateRestaurantTableRequest(tableId, fixture.BusinessId,
                    tableCode)))
                created.EnsureSuccessStatusCode();
            RestaurantAccount account;
            using (var opened = await waiter.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/accounts",
                new OpenRestaurantAccountRequest(Guid.NewGuid(), tableId,
                    fixture.WarehouseId)))
            {
                opened.EnsureSuccessStatusCode();
                account = (await opened.Content.ReadFromJsonAsync<RestaurantAccount>())!;
            }
            using (var station = await admin.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/stations",
                new CreateRestaurantStationRequest(stationId, fixture.BusinessId,
                    $"K-{stationId:N}"[..12], "Cocina", 0,
                    "Preparation", 15)))
                station.EnsureSuccessStatusCode();
            using (var route = await admin.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/product-routes/{fixture.ProductId}",
                new SetRestaurantProductRouteRequest(fixture.BusinessId,
                    stationId, "Hamburguesa", null)))
                route.EnsureSuccessStatusCode();
            var additionId = Guid.NewGuid();
            using (var added = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                new AddRestaurantAccountItemRequest(additionId,
                    fixture.ProductId, 2, 1)))
                added.EnsureSuccessStatusCode();
            var request = new SendRestaurantPendingRequest(Guid.NewGuid(), 2);

            RestaurantCommandConfiguration configuration;
            using (var read = await admin.GetAsync(
                $"/api/commerce/v1/restaurant/stations/{stationId}/command-configuration"))
            {
                read.EnsureSuccessStatusCode();
                configuration = (await read.Content.ReadFromJsonAsync<RestaurantCommandConfiguration>())!;
            }
            using (var updated = await admin.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/stations/{stationId}/command-configuration",
                new UpdateRestaurantCommandConfigurationRequest("Cocina", "Both",
                    fixture.DeviceId, "COCINA", configuration.RowVersion)))
                updated.EnsureSuccessStatusCode();


            fixture.DrainSynchronizationMessages();
            SendRestaurantPendingResult first;
            using (var sent = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/commands",
                request))
            {
                sent.EnsureSuccessStatusCode();
                first = (await sent.Content.ReadFromJsonAsync<SendRestaurantPendingResult>())!;
            }
            Assert.Equal(3, first.Draft.Account.Version);
            var firstAddition = Assert.Single(first.Draft.Additions);
            Assert.Equal("Sent", firstAddition.Status);
            Assert.Equal(stationId, firstAddition.StationId);
            var card = Assert.Single(first.Cards);
            Assert.Equal(stationId, card.StationId);
            Assert.Equal("Pending", card.State);
            Assert.Equal(1, card.LineCount);
            var commandNotice = await fixture.ReadSynchronizationMessageAsync();
            for (var seen = 0; seen < 5 && commandNotice.Stream != "RestaurantCommands";
                 seen++)
                commandNotice = await fixture.ReadSynchronizationMessageAsync();
            Assert.Equal("RestaurantCommands", commandNotice.Stream);
            Assert.Equal(fixture.TenantId, commandNotice.TenantId);
            Assert.Equal(fixture.BusinessId, commandNotice.BusinessId);
            Assert.Equal(1, await PrintNotificationsAsync(card.CardId));
            using (var device = fixture.CreateClient())
            {
                using var claimRequest = new HttpRequestMessage(HttpMethod.Post,
                    "/api/pos/v1/restaurant/print-jobs/claim");
                claimRequest.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
                claimRequest.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
                using var claimed = await device.SendAsync(claimRequest);
                claimed.EnsureSuccessStatusCode();
                var jobs = (await claimed.Content.ReadFromJsonAsync<List<ClaimedPrintJob>>())!;
                var job = Assert.Single(jobs.Where(item => item.Card.CardId == card.CardId));
                Assert.Equal("COCINA", job.ProfileKey);
                Assert.Equal("Hamburguesa", Assert.Single(job.Card.Lines).ProductName);
                using var acknowledgement = new HttpRequestMessage(HttpMethod.Post,
                    $"/api/pos/v1/restaurant/print-jobs/{job.CardId}/ack")
                {
                    Content = JsonContent.Create(new { job.LeaseToken, State = "Submitted", Error = (string?)null })
                };
                acknowledgement.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
                acknowledgement.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
                using var acknowledged = await device.SendAsync(acknowledgement);
                Assert.Equal(HttpStatusCode.NoContent, acknowledged.StatusCode);
                using (var forbiddenCopy = await admin.PostAsync(
                    $"/api/commerce/v1/restaurant/command-cards/{job.CardId}/reprint",
                    null))
                    Assert.Equal(HttpStatusCode.Forbidden, forbiddenCopy.StatusCode);
                using (var copyRequest = await kitchen.PostAsync(
                    $"/api/commerce/v1/restaurant/command-cards/{job.CardId}/reprint",
                    null))
                    Assert.Equal(HttpStatusCode.NoContent, copyRequest.StatusCode);
                using var copyClaimRequest = new HttpRequestMessage(HttpMethod.Post,
                    "/api/pos/v1/restaurant/print-jobs/claim");
                copyClaimRequest.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
                copyClaimRequest.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
                using var copyClaimed = await device.SendAsync(copyClaimRequest);
                copyClaimed.EnsureSuccessStatusCode();
                var copyJobs = (await copyClaimed.Content
                    .ReadFromJsonAsync<List<ClaimedPrintJob>>())!;
                Assert.True(Assert.Single(copyJobs.Where(item =>
                    item.CardId == job.CardId)).IsCopy);
                Assert.Equal(2, await PrintNotificationsAsync(card.CardId));
            }
            using (var replay = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/commands",
                request))
            {
                replay.EnsureSuccessStatusCode();
                var original = await replay.Content.ReadFromJsonAsync<SendRestaurantPendingResult>();
                Assert.Equal(first.RoundId, original!.RoundId);
                Assert.Equal(first.Draft.Account.Version, original.Draft.Account.Version);
            }
            using (var nothingLeft = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/commands",
                new SendRestaurantPendingRequest(Guid.NewGuid(), 3)))
                Assert.Equal(HttpStatusCode.Conflict, nothingLeft.StatusCode);
            using (var immutable = await waiter.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions/{additionId}/quantity",
                new ChangeRestaurantAccountQuantityRequest(Guid.NewGuid(), 1, 3)))
                Assert.Equal(HttpStatusCode.Conflict, immutable.StatusCode);
            Assert.Equal((1, 1, 1), await CountsAsync(account.AccountId));

            var firstPendingId = Guid.NewGuid();
            using (var added = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                new AddRestaurantAccountItemRequest(firstPendingId,
                    fixture.ProductId, 1, 3)))
                added.EnsureSuccessStatusCode();
            var secondPendingId = Guid.NewGuid();
            using (var added = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                new AddRestaurantAccountItemRequest(secondPendingId,
                    fixture.ProductId, 1, 4)))
                added.EnsureSuccessStatusCode();
            using (var partial = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/commands",
                new SendRestaurantPendingRequest(Guid.NewGuid(), 5, firstPendingId)))
            {
                partial.EnsureSuccessStatusCode();
                var sentOne = (await partial.Content
                    .ReadFromJsonAsync<SendRestaurantPendingResult>())!;
                Assert.Equal(6, sentOne.Draft.Account.Version);
                Assert.Equal("Sent", sentOne.Draft.Additions.Single(
                    item => item.AdditionId == firstPendingId).Status);
                Assert.Equal("Pending", sentOne.Draft.Additions.Single(
                    item => item.AdditionId == secondPendingId).Status);
                Assert.Equal(1, Assert.Single(sentOne.Cards).LineCount);
            }
            Assert.Equal((2, 2, 2), await CountsAsync(account.AccountId));
            using (var remainder = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/commands",
                new SendRestaurantPendingRequest(Guid.NewGuid(), 6)))
            {
                remainder.EnsureSuccessStatusCode();
                var sentRest = (await remainder.Content
                    .ReadFromJsonAsync<SendRestaurantPendingResult>())!;
                Assert.All(sentRest.Draft.Additions,
                    item => Assert.Equal("Sent", item.Status));
            }
            Assert.Equal((3, 3, 3), await CountsAsync(account.AccountId));
            using (var forbidden = await waiter.GetAsync(
                $"/api/commerce/v1/restaurant/command-cards?stationId={stationId}"))
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            RestaurantCommandCardView current;
            using (var board = await kitchen.GetAsync(
                $"/api/commerce/v1/restaurant/command-cards?stationId={stationId}"))
            {
                board.EnsureSuccessStatusCode();
                var cards = (await board.Content.ReadFromJsonAsync<RestaurantCommandBoardPage>())!.Items;
                Assert.Equal(3, cards.Count);
                current = cards.Single(value => value.CardId == card.CardId);
                Assert.Equal("Pending", current.State);
                Assert.Equal("Hamburguesa", Assert.Single(current.Lines).ProductName);
            }
            using (var filtered = await kitchen.GetAsync(
                $"/api/commerce/v1/restaurant/command-cards?stationId={stationId}&state=Pending&tableCode={tableCode}"))
            {
                filtered.EnsureSuccessStatusCode();
                Assert.Equal(3, (await filtered.Content.ReadFromJsonAsync<RestaurantCommandBoardPage>())!.Items.Count);
            }
            using (var empty = await kitchen.GetAsync(
                $"/api/commerce/v1/restaurant/command-cards?state=Preparing&tableCode={tableCode}"))
            {
                empty.EnsureSuccessStatusCode();
                Assert.Empty((await empty.Content.ReadFromJsonAsync<RestaurantCommandBoardPage>())!.Items);
            }
            using (var nextPage = await kitchen.GetAsync(
                $"/api/commerce/v1/restaurant/command-cards?stationId={stationId}&page=2"))
            {
                nextPage.EnsureSuccessStatusCode();
                var page = (await nextPage.Content.ReadFromJsonAsync<RestaurantCommandBoardPage>())!;
                Assert.Equal(2, page.Page);
                Assert.Empty(page.Items);
                Assert.False(page.HasMore);
            }
            foreach (var nextState in new[] { "Preparing", "Ready", "ToDeliver", "Delivered" })
            {
                using var changed = await kitchen.PutAsJsonAsync(
                    $"/api/commerce/v1/restaurant/command-cards/{card.CardId}/state",
                    new ChangeRestaurantCommandStateRequest(nextState, current.RowVersion));
                changed.EnsureSuccessStatusCode();
                using var stale = await kitchen.PutAsJsonAsync(
                    $"/api/commerce/v1/restaurant/command-cards/{card.CardId}/state",
                    new ChangeRestaurantCommandStateRequest(nextState, current.RowVersion));
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
                using var board = await kitchen.GetAsync(
                    $"/api/commerce/v1/restaurant/command-cards?stationId={stationId}");
                board.EnsureSuccessStatusCode();
                var cards = (await board.Content.ReadFromJsonAsync<RestaurantCommandBoardPage>())!.Items;
                if (nextState == "Delivered")
                    Assert.DoesNotContain(cards, value => value.CardId == card.CardId);
                else
                    current = cards.Single(value => value.CardId == card.CardId);
                if (nextState == "Preparing")
                {
                    using var preparing = await kitchen.GetAsync(
                        $"/api/commerce/v1/restaurant/command-cards?stationId={stationId}&state=Preparing&tableCode={tableCode}");
                    preparing.EnsureSuccessStatusCode();
                    Assert.Contains((await preparing.Content.ReadFromJsonAsync<RestaurantCommandBoardPage>())!.Items,
                        value => value.CardId == card.CardId);
                }
            }

            var withoutOnionId = Guid.NewGuid();
            var withoutLettuceId = Guid.NewGuid();
            using (var added = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                new AddRestaurantAccountItemRequest(withoutOnionId,
                    fixture.ProductId, 1, 7)))
                added.EnsureSuccessStatusCode();
            using (var added = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                new AddRestaurantAccountItemRequest(withoutLettuceId,
                    fixture.ProductId, 1, 8)))
                added.EnsureSuccessStatusCode();
            var firstNote = new ChangeRestaurantAccountNoteRequest("Sin cebolla", 9);
            using (var noted = await waiter.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions/{withoutOnionId}/note",
                firstNote))
            {
                noted.EnsureSuccessStatusCode();
                var draft = (await noted.Content.ReadFromJsonAsync<RestaurantAccountDraft>())!;
                Assert.Equal(10, draft.Account.Version);
                Assert.Equal("Sin cebolla", draft.Additions.Single(item =>
                    item.AdditionId == withoutOnionId).PreparationNote);
            }
            using (var replay = await waiter.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions/{withoutOnionId}/note",
                firstNote))
            {
                replay.EnsureSuccessStatusCode();
                Assert.Equal(10, (await replay.Content.ReadFromJsonAsync<RestaurantAccountDraft>())!.Account.Version);
            }
            using (var noted = await waiter.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions/{withoutLettuceId}/note",
                new ChangeRestaurantAccountNoteRequest("Sin lechuga", 10)))
                noted.EnsureSuccessStatusCode();
            using (var sent = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/commands",
                new SendRestaurantPendingRequest(Guid.NewGuid(), 11)))
                sent.EnsureSuccessStatusCode();
            using (var board = await kitchen.GetAsync(
                $"/api/commerce/v1/restaurant/command-cards?stationId={stationId}"))
            {
                board.EnsureSuccessStatusCode();
                var lines = (await board.Content.ReadFromJsonAsync<RestaurantCommandBoardPage>())!
                    .Items.SelectMany(value => value.Lines).ToArray();
                Assert.Contains(lines, line => line.AdditionId == withoutOnionId &&
                    line.PreparationNote == "Sin cebolla");
                Assert.Contains(lines, line => line.AdditionId == withoutLettuceId &&
                    line.PreparationNote == "Sin lechuga");
            }
            using (var sentNote = await waiter.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions/{withoutOnionId}/note",
                new ChangeRestaurantAccountNoteRequest("Otra nota", 12)))
                Assert.Equal(HttpStatusCode.Conflict, sentNote.StatusCode);
        }
        finally
        {
            await RestoreModeAsync();
        }
    }

    private sealed record ClaimedPrintJob(Guid CardId, Guid LeaseToken,
        string ProfileKey, bool IsCopy, RestaurantCommandCardView Card);

    private async Task<int> PrintNotificationsAsync(Guid cardId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages
            WHERE BusinessId=@BusinessId AND Stream=N'RestaurantCommandPrint'
              AND EntityId=@CardId AND TargetDeviceId=@DeviceId;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@CardId", cardId);
        command.Parameters.AddWithValue("@DeviceId", fixture.DeviceId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task PrepareWaiterAsync(Guid waiterId, Guid partyId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.Businesses SET PointOfSaleMode=N'RestaurantBar'
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,
                FirstName,LastName,CompletionStatus,IsActive,CreatedAt)
            VALUES(@PartyId,@TenantId,N'NaturalPerson',N'Mesero comanda',
                   N'Mesero',N'Comanda',N'Incomplete',1,SYSDATETIMEOFFSET());
            INSERT parties.Waiters(WaiterId,TenantId,PartyId,Code,IsActive,CreatedAt)
            VALUES(NEWID(),@TenantId,@PartyId,@Code,1,SYSDATETIMEOFFSET());
            UPDATE dbo.AppUsers SET PartyId=@PartyId
            WHERE UserId=@WaiterId AND TenantId=@TenantId;
            DELETE FROM sales.RestaurantProductRoutes
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId
              AND ProductId=@ProductId;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@WaiterId", waiterId);
        command.Parameters.AddWithValue("@PartyId", partyId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        command.Parameters.AddWithValue("@Code", $"W-{partyId:N}"[..12]);
        await command.ExecuteNonQueryAsync();
    }

    private async Task RestoreModeAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.Businesses SET PointOfSaleMode=N'DirectSale'
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<(int Cards, int Lines, int Notifications)> CountsAsync(
        Guid accountId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT (SELECT COUNT(*) FROM sales.RestaurantCommandCards card
                    JOIN sales.RestaurantCommandRounds round ON round.RoundId=card.RoundId
                    WHERE round.AccountId=@AccountId),
                   (SELECT COUNT(*) FROM sales.RestaurantCommandLines line
                    JOIN sales.RestaurantCommandCards card ON card.CardId=line.CardId
                    JOIN sales.RestaurantCommandRounds round ON round.RoundId=card.RoundId
                    WHERE round.AccountId=@AccountId),
                   (SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages notice
                    JOIN sales.RestaurantCommandRounds round
                      ON round.RoundId=notice.EntityId
                    WHERE round.AccountId=@AccountId
                      AND notice.BusinessId=@BusinessId
                      AND notice.Stream=N'RestaurantCommands');
            """, connection);
        command.Parameters.AddWithValue("@AccountId", accountId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }
}
