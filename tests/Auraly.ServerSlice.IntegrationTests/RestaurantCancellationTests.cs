using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class RestaurantCancellationTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Sent_cancellation_requires_station_and_supervisor_and_never_restocks_prepared_loss()
    {
        var waiterId = Guid.NewGuid();
        var waiterPartyId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        using var waiter = fixture.CreateUserClient(waiterId, "restaurant.attend");
        using var admin = fixture.CreateAdminClient("businesses.update",
            "sales.create", "restaurant.cancellations.approve");
        using var kitchen = fixture.CreateAdminClient("restaurant.commands.read",
            "restaurant.commands.manage");
        await PrepareAsync(waiterId, waiterPartyId);
        try
        {
            using (var created = await admin.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/tables",
                new CreateRestaurantTableRequest(tableId, fixture.BusinessId,
                    $"C-{tableId:N}"[..12])))
                created.EnsureSuccessStatusCode();
            using (var station = await admin.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/stations",
                new CreateRestaurantStationRequest(stationId, fixture.BusinessId,
                    $"C-{stationId:N}"[..12], "Cocina", 0,
                    "Preparation", 15)))
                station.EnsureSuccessStatusCode();
            using (var route = await admin.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/product-routes/{fixture.ProductId}",
                new SetRestaurantProductRouteRequest(fixture.BusinessId,
                    stationId, "Plato", null)))
                route.EnsureSuccessStatusCode();
            var configuration = await admin.GetFromJsonAsync<RestaurantCommandConfiguration>(
                $"/api/commerce/v1/restaurant/stations/{stationId}/command-configuration");
            using (var printer = await admin.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/stations/{stationId}/command-configuration",
                new UpdateRestaurantCommandConfigurationRequest("Cocina", "Both",
                    fixture.DeviceId, "COCINA", configuration!.RowVersion)))
                printer.EnsureSuccessStatusCode();
            RestaurantAccount account;
            using (var opened = await waiter.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/accounts",
                new OpenRestaurantAccountRequest(Guid.NewGuid(), tableId,
                    fixture.WarehouseId)))
            {
                opened.EnsureSuccessStatusCode();
                account = (await opened.Content.ReadFromJsonAsync<RestaurantAccount>())!;
            }
            var additionId = Guid.NewGuid();
            using (var added = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                new AddRestaurantAccountItemRequest(additionId,
                    fixture.ProductId, 2, 1)))
                added.EnsureSuccessStatusCode();
            SendRestaurantPendingResult sent;
            using (var response = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/commands",
                new SendRestaurantPendingRequest(Guid.NewGuid(), 2)))
            {
                response.EnsureSuccessStatusCode();
                sent = (await response.Content.ReadFromJsonAsync<SendRestaurantPendingResult>())!;
            }
            var cardId = Assert.Single(sent.Cards).CardId;
            var cancellationId = Guid.NewGuid();
            var request = new RequestRestaurantCancellation(cancellationId, 1,
                "CUSTOMER_REQUEST", "El cliente retiró una unidad", 3);
            RestaurantCancellation cancellation;
            using (var response = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions/{additionId}/cancellations",
                request))
            {
                response.EnsureSuccessStatusCode();
                cancellation = (await response.Content.ReadFromJsonAsync<RestaurantCancellation>())!;
            }
            Assert.Equal("Requested", cancellation.Status);
            using (var replay = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions/{additionId}/cancellations",
                request))
            {
                replay.EnsureSuccessStatusCode();
                Assert.Equal(cancellation.CancellationId,
                    (await replay.Content.ReadFromJsonAsync<RestaurantCancellation>())!.CancellationId);
            }
            using (var denied = await waiter.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/cancellations/{cancellationId}/resolution",
                new ResolveRestaurantCancellation(true, cancellation.RowVersion)))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var premature = await admin.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/cancellations/{cancellationId}/resolution",
                new ResolveRestaurantCancellation(true, cancellation.RowVersion)))
                Assert.Equal(HttpStatusCode.Conflict, premature.StatusCode);
            var board = await kitchen.GetFromJsonAsync<RestaurantCommandBoardPage>(
                "/api/commerce/v1/restaurant/command-cards");
            var card = board!.Items.Single(item => item.CardId == cardId);
            Assert.Equal(cancellationId, Assert.Single(card.Lines).CancellationId);
            Assert.Contains("Pending", board.StatesWithPendingCorrections!);
            var otherState = await kitchen.GetFromJsonAsync<RestaurantCommandBoardPage>(
                "/api/commerce/v1/restaurant/command-cards?state=Preparing");
            Assert.Contains("Pending", otherState!.StatesWithPendingCorrections!);
            using (var blocked = await kitchen.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/command-cards/{cardId}/state",
                new ChangeRestaurantCommandStateRequest("Preparing", card.RowVersion)))
                Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            using (var confirmed = await kitchen.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/cancellations/{cancellationId}/confirmation",
                new ConfirmRestaurantCancellation("NotPrepared", cancellation.RowVersion)))
            {
                confirmed.EnsureSuccessStatusCode();
                cancellation = (await confirmed.Content.ReadFromJsonAsync<RestaurantCancellation>())!;
            }
            var confirmedBoard = await kitchen.GetFromJsonAsync<RestaurantCommandBoardPage>(
                "/api/commerce/v1/restaurant/command-cards");
            Assert.DoesNotContain("Pending", confirmedBoard!.StatesWithPendingCorrections!);
            using (var approved = await admin.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/cancellations/{cancellationId}/resolution",
                new ResolveRestaurantCancellation(true, cancellation.RowVersion)))
            {
                approved.EnsureSuccessStatusCode();
                cancellation = (await approved.Content.ReadFromJsonAsync<RestaurantCancellation>())!;
            }
            Assert.Equal("Approved", cancellation.Status);
            Assert.Null(await DamageIdAsync(cancellationId));
            var draft = await waiter.GetFromJsonAsync<RestaurantAccountDraft>(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/draft");
            Assert.Equal(1, Assert.Single(draft!.Additions).Quantity);
            var afterPartial = await kitchen.GetFromJsonAsync<RestaurantCommandBoardPage>(
                "/api/commerce/v1/restaurant/command-cards");
            Assert.Equal(1, Assert.Single(afterPartial!.Items.Single(
                item => item.CardId == cardId).Lines).Quantity);
            using (var device = fixture.CreateClient())
            using (var claim = new HttpRequestMessage(HttpMethod.Post,
                "/api/pos/v1/restaurant/print-jobs/claim"))
            {
                claim.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
                claim.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
                using var response = await device.SendAsync(claim);
                response.EnsureSuccessStatusCode();
                var claims = (await response.Content.ReadFromJsonAsync<List<CorrectionPrintClaim>>())!;
                var correction = Assert.Single(claims.Where(item => item.CardId == cardId));
                Assert.Equal("Correction", correction.PrintKind);
                Assert.Equal(1, correction.RemovedQuantity);
                Assert.Equal(1, Assert.Single(correction.Card.Lines).Quantity);
            }

            var nextAdditionId = Guid.NewGuid();
            using (var added = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                new AddRestaurantAccountItemRequest(nextAdditionId,
                    fixture.ProductId, 1, draft.Account.Version)))
                added.EnsureSuccessStatusCode();
            SendRestaurantPendingResult secondRound;
            using (var response = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/commands",
                new SendRestaurantPendingRequest(Guid.NewGuid(), draft.Account.Version + 1)))
            {
                response.EnsureSuccessStatusCode();
                secondRound = (await response.Content.ReadFromJsonAsync<SendRestaurantPendingResult>())!;
            }
            var secondCardId = Assert.Single(secondRound.Cards).CardId;
            var updated = await waiter.GetFromJsonAsync<RestaurantAccountDraft>(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/draft");
            var preparedId = Guid.NewGuid();
            using (var response = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions/{nextAdditionId}/cancellations",
                new RequestRestaurantCancellation(preparedId, 1,
                    "QUALITY_ISSUE", "Plato descartado", updated!.Account.Version)))
            {
                response.EnsureSuccessStatusCode();
                cancellation = (await response.Content.ReadFromJsonAsync<RestaurantCancellation>())!;
            }
            using (var understated = await kitchen.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/cancellations/{preparedId}/confirmation",
                new ConfirmRestaurantCancellation("Prepared", cancellation.RowVersion,
                    [new(fixture.ProductId, 0.1m)])))
                Assert.Equal(HttpStatusCode.BadRequest, understated.StatusCode);
            await SetProductStockModeAsync(false);
            using (var changedMode = await kitchen.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/cancellations/{preparedId}/confirmation",
                new ConfirmRestaurantCancellation("Prepared", cancellation.RowVersion)))
                Assert.Equal(HttpStatusCode.Conflict, changedMode.StatusCode);
            await SetProductStockModeAsync(true);
            using (var confirmed = await kitchen.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/cancellations/{preparedId}/confirmation",
                new ConfirmRestaurantCancellation("Prepared", cancellation.RowVersion)))
            {
                confirmed.EnsureSuccessStatusCode();
                cancellation = (await confirmed.Content.ReadFromJsonAsync<RestaurantCancellation>())!;
            }
            Assert.Contains(cancellation.ConsumedComponents!, component =>
                component.ProductId == fixture.ProductId && component.Quantity == 1);
            await SetProductStockModeAsync(false);
            using (var changedAfterConfirmation = await admin.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/cancellations/{preparedId}/resolution",
                new ResolveRestaurantCancellation(true, cancellation.RowVersion)))
                Assert.Equal(HttpStatusCode.Conflict, changedAfterConfirmation.StatusCode);
            await SetProductStockModeAsync(true);
            using (var approved = await admin.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/cancellations/{preparedId}/resolution",
                new ResolveRestaurantCancellation(true, cancellation.RowVersion)))
            {
                approved.EnsureSuccessStatusCode();
                cancellation = (await approved.Content.ReadFromJsonAsync<RestaurantCancellation>())!;
            }
            Assert.Equal("Approved", cancellation.Status);
            Assert.NotNull(await DamageIdAsync(preparedId));
            using (var replay = await admin.PutAsJsonAsync(
                $"/api/commerce/v1/restaurant/cancellations/{preparedId}/resolution",
                new ResolveRestaurantCancellation(true, cancellation.RowVersion)))
                replay.EnsureSuccessStatusCode();
            Assert.Equal(1, await CountDamageMovementsAsync(preparedId));
            Assert.Equal(0, await CountAveEntriesAsync(preparedId));
            var afterFull = await kitchen.GetFromJsonAsync<RestaurantCommandBoardPage>(
                "/api/commerce/v1/restaurant/command-cards?state=Cancelled");
            Assert.Equal("Cancelled", afterFull!.Items.Single(
                item => item.CardId == secondCardId).State);
            var activeBoard = await kitchen.GetFromJsonAsync<RestaurantCommandBoardPage>(
                "/api/commerce/v1/restaurant/command-cards");
            Assert.DoesNotContain(activeBoard!.Items,
                item => item.CardId == secondCardId);
            var beforeRework = await waiter.GetFromJsonAsync<RestaurantAccountDraft>(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/draft");
            var replacementId = Guid.NewGuid();
            var replacement = new AddRestaurantAccountItemRequest(replacementId,
                fixture.ProductId, cancellation.Quantity,
                beforeRework!.Account.Version, preparedId);
            using (var added = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                replacement))
            {
                added.EnsureSuccessStatusCode();
                draft = (await added.Content.ReadFromJsonAsync<RestaurantAccountDraft>())!;
            }
            var replacementLine = Assert.Single(draft.Additions.Where(item =>
                item.AdditionId == replacementId));
            Assert.Equal(preparedId, replacementLine.ReplacementOfCancellationId);
            Assert.Equal(beforeRework.Additions.Single(item =>
                item.AdditionId == nextAdditionId).PublicUnitPrice,
                replacementLine.PublicUnitPrice);
            using (var replayAddition = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                replacement))
                replayAddition.EnsureSuccessStatusCode();
            using (var duplicateReplacement = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                replacement with { AdditionId = Guid.NewGuid(),
                    ExpectedVersion = draft.Account.Version }))
                Assert.Equal(HttpStatusCode.Conflict, duplicateReplacement.StatusCode);
            using (var wrongProduct = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                replacement with { AdditionId = Guid.NewGuid(),
                    ProductId = Guid.NewGuid(), ExpectedVersion = draft.Account.Version }))
                Assert.Equal(HttpStatusCode.BadRequest, wrongProduct.StatusCode);
            using (var sentReplacement = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/commands",
                new SendRestaurantPendingRequest(Guid.NewGuid(),
                    draft.Account.Version)))
                sentReplacement.EnsureSuccessStatusCode();
            using (var device = fixture.CreateClient())
            using (var claim = new HttpRequestMessage(HttpMethod.Post,
                "/api/pos/v1/restaurant/print-jobs/claim"))
            {
                claim.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
                claim.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
                using var response = await device.SendAsync(claim);
                response.EnsureSuccessStatusCode();
                var claims = (await response.Content.ReadFromJsonAsync<List<CorrectionPrintClaim>>())!;
                var correction = Assert.Single(claims.Where(item => item.CardId == secondCardId));
                Assert.Equal("Correction", correction.PrintKind);
                Assert.Equal("Cancelled", correction.Card.State);
            }
        }
        finally
        {
            await SetProductStockModeAsync(true);
            await RestoreModeAsync();
        }
    }

    private sealed record CorrectionPrintClaim(Guid CardId, string PrintKind,
        decimal? RemovedQuantity, RestaurantCommandCardView Card);

    private async Task SetProductStockModeAsync(bool manageStock)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.Products SET ManageStock=@ManageStock
            WHERE TenantId=@TenantId AND ProductId=@ProductId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        command.Parameters.AddWithValue("@ManageStock", manageStock);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task PrepareAsync(Guid waiterId, Guid partyId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.Businesses SET PointOfSaleMode=N'RestaurantBar'
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
            EXEC dbo.AccountingDefaultsProvision @TenantId=@TenantId,
                @BusinessId=@BusinessId,@Now=@Now;
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,
                FirstName,LastName,CompletionStatus,IsActive,CreatedAt)
            VALUES(@PartyId,@TenantId,N'NaturalPerson',N'Mesero prueba',
                N'Mesero',N'Prueba',N'Incomplete',1,SYSDATETIMEOFFSET());
            INSERT parties.Waiters(WaiterId,TenantId,PartyId,Code,IsActive,CreatedAt)
            VALUES(NEWID(),@TenantId,@PartyId,@Code,1,SYSDATETIMEOFFSET());
            UPDATE dbo.AppUsers SET PartyId=@PartyId
            WHERE UserId=@WaiterId AND TenantId=@TenantId;
            DELETE sales.RestaurantProductRoutes WHERE BusinessId=@BusinessId
              AND TenantId=@TenantId AND ProductId=@ProductId;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@WaiterId", waiterId);
        command.Parameters.AddWithValue("@PartyId", partyId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        command.Parameters.AddWithValue("@Code", $"W-{partyId:N}"[..12]);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
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

    private async Task<Guid?> DamageIdAsync(Guid cancellationId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT InventoryDocumentId
            FROM sales.RestaurantCancellationRequests
            WHERE CancellationId=@CancellationId;
            """, connection);
        command.Parameters.AddWithValue("@CancellationId", cancellationId);
        return await command.ExecuteScalarAsync() is Guid value ? value : null;
    }

    private async Task<int> CountDamageMovementsAsync(Guid cancellationId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.InventoryMovements movement
            JOIN sales.RestaurantCancellationRequests cancellation
              ON cancellation.InventoryDocumentId=movement.DocumentId
            WHERE cancellation.CancellationId=@CancellationId
              AND movement.MovementType=N'InventoryDamage';
            """, connection);
        command.Parameters.AddWithValue("@CancellationId", cancellationId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> CountAveEntriesAsync(Guid cancellationId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.InventoryMovements movement
            JOIN sales.RestaurantCancellationRequests cancellation
              ON cancellation.InventoryDocumentId=movement.DocumentId
            JOIN dbo.Warehouses warehouse ON warehouse.WarehouseId=movement.WarehouseId
            WHERE cancellation.CancellationId=@CancellationId
              AND warehouse.Code=N'AVE';
            """, connection);
        command.Parameters.AddWithValue("@CancellationId", cancellationId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
