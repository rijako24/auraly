using System.Net.Http.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class RestaurantTipTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Suggested_prebill_and_received_tip_leave_the_sales_document_at_consumption_value()
    {
        var waiterId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        using var waiter = fixture.CreateUserClient(waiterId, "restaurant.attend");
        using var cashier = fixture.CreateAdminClient("sales.create", "businesses.update");
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var setup = new SqlCommand("""
            UPDATE dbo.Businesses
            SET PointOfSaleMode=N'RestaurantBar',RestaurantSuggestedTipPercent=10
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
            EXEC dbo.AccountingDefaultsProvision @TenantId=@TenantId,
                @BusinessId=@BusinessId,@Now=@Now;
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,
                FirstName,LastName,CompletionStatus,IsActive,CreatedAt)
            VALUES(@PartyId,@TenantId,N'NaturalPerson',N'Mesero propina',
                   N'Mesero',N'Propina',N'Incomplete',1,SYSDATETIMEOFFSET());
            INSERT parties.Waiters(WaiterId,TenantId,PartyId,Code,IsActive,CreatedAt)
            VALUES(NEWID(),@TenantId,@PartyId,@Code,1,SYSDATETIMEOFFSET());
            UPDATE dbo.AppUsers SET PartyId=@PartyId
            WHERE UserId=@WaiterId AND TenantId=@TenantId;
            DELETE FROM sales.RestaurantProductRoutes
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND ProductId=@ProductId;
            """, connection))
        {
            setup.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            setup.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            setup.Parameters.AddWithValue("@PartyId", partyId);
            setup.Parameters.AddWithValue("@WaiterId", waiterId);
            setup.Parameters.AddWithValue("@Code", $"T-{partyId:N}"[..12]);
            setup.Parameters.AddWithValue("@ProductId", fixture.ProductId);
            setup.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
            await setup.ExecuteNonQueryAsync();
        }
        try
        {
            using (var response = await cashier.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/tables",
                new CreateRestaurantTableRequest(tableId, fixture.BusinessId,
                    $"P-{tableId:N}"[..12])))
                response.EnsureSuccessStatusCode();
            RestaurantAccount account;
            using (var response = await waiter.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/accounts",
                new OpenRestaurantAccountRequest(Guid.NewGuid(), tableId,
                    fixture.WarehouseId)))
            {
                response.EnsureSuccessStatusCode();
                account = (await response.Content.ReadFromJsonAsync<RestaurantAccount>())!;
            }
            RestaurantAccountDraft draft;
            using (var response = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/additions",
                new AddRestaurantAccountItemRequest(Guid.NewGuid(), fixture.ProductId,
                    2, account.Version)))
            {
                response.EnsureSuccessStatusCode();
                draft = (await response.Content.ReadFromJsonAsync<RestaurantAccountDraft>())!;
            }
            var addition = Assert.Single(draft.Additions);
            Assert.Equal("Direct", addition.Status);
            using (var response = await waiter.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/prebill",
                new PrintRestaurantPrebillRequest(draft.Account.Version)))
            {
                response.EnsureSuccessStatusCode();
                var prebill = (await response.Content.ReadFromJsonAsync<RestaurantPrebill>())!;
                Assert.Equal(10m, prebill.SuggestedTipPercent);
                Assert.Equal(decimal.Round(prebill.Total / 10, 0,
                    MidpointRounding.AwayFromZero),
                    prebill.SuggestedTipAmount);
            }
            await using (var disableSuggestion = new SqlCommand("""
                UPDATE dbo.Businesses SET RestaurantSuggestedTipPercent=0
                WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
                """, connection))
            {
                disableSuggestion.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                disableSuggestion.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                await disableSuggestion.ExecuteNonQueryAsync();
            }
            var workSessionId = await fixture.OpenWebWorkSessionAsync();
            RestaurantCollectionSummary initial;
            using (var response = await cashier.GetAsync(
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/collections"))
            {
                response.EnsureSuccessStatusCode();
                initial = (await response.Content.ReadFromJsonAsync<RestaurantCollectionSummary>())!;
                Assert.Equal(0, initial.SuggestedTipPercent);
            }
            var tipAmount = decimal.Round(initial.Total / 10, 0,
                MidpointRounding.AwayFromZero);
            var half = initial.Total / 2;
            var productCollectionId = Guid.NewGuid();
            RestaurantCollectionSummary partial;
            using (var request = new HttpRequestMessage(HttpMethod.Post,
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/collections"))
            {
                request.Content = JsonContent.Create(new CaptureRestaurantCollectionRequest(
                    productCollectionId, workSessionId, initial.AccountVersion,
                    [new RestaurantCollectionAllocationRequest(addition.AdditionId, 1)],
                    [new OnlineSalesPayment("Cash", half, null)],
                    new OnlineSalesPayment("Cash", 1_000m, null)));
                request.Headers.Add("Idempotency-Key", productCollectionId.ToString("N"));
                using var response = await cashier.SendAsync(request);
                response.EnsureSuccessStatusCode();
                partial = (await response.Content.ReadFromJsonAsync<RestaurantCollectionSummary>())!;
                Assert.Equal(half, partial.Remaining);
                Assert.Equal(half, partial.Collected);
                Assert.Equal(1_000m, partial.TipsCollected);
            }
            var secondCollectionId = Guid.NewGuid();
            RestaurantCollectionSummary paid;
            using (var request = new HttpRequestMessage(HttpMethod.Post,
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/collections"))
            {
                request.Content = JsonContent.Create(new CaptureRestaurantCollectionRequest(
                    secondCollectionId, workSessionId, partial.AccountVersion,
                    [new RestaurantCollectionAllocationRequest(addition.AdditionId, 1)],
                    [new OnlineSalesPayment("Cash", half, null)],
                    new OnlineSalesPayment("Cash", 2_000m, null)));
                request.Headers.Add("Idempotency-Key", secondCollectionId.ToString("N"));
                using var response = await cashier.SendAsync(request);
                response.EnsureSuccessStatusCode();
                paid = (await response.Content.ReadFromJsonAsync<RestaurantCollectionSummary>())!;
                Assert.Equal(0, paid.Remaining);
                Assert.Equal(initial.Total, paid.Collected);
                Assert.Equal(3_000m, paid.TipsCollected);
            }
            var collectionId = Guid.NewGuid();
            using (var request = new HttpRequestMessage(HttpMethod.Post,
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/collections"))
            {
                request.Content = JsonContent.Create(new CaptureRestaurantCollectionRequest(
                    collectionId, workSessionId, paid.AccountVersion,
                    [], [], new OnlineSalesPayment("Cash", tipAmount, null)));
                request.Headers.Add("Idempotency-Key", collectionId.ToString("N"));
                using var response = await cashier.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var captured = (await response.Content.ReadFromJsonAsync<RestaurantCollectionSummary>())!;
                Assert.Equal(0, captured.Remaining);
                Assert.Equal(initial.Total, captured.Collected);
                Assert.Equal(3_000m + tipAmount, captured.TipsCollected);
                using var complete = new HttpRequestMessage(HttpMethod.Post,
                    $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/complete")
                {
                    Content = JsonContent.Create(new CompleteRestaurantAccountRequest(
                        workSessionId, new CompleteOnlineSalesDraftRequest(
                            captured.AccountVersion,
                            [new OnlineSalesPayment("Cash", initial.Total, null,
                                TenderedAmount: initial.Total)],
                            DocumentType: PosSaleDocumentTypes.Receipt)))
                };
                complete.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
                using var issued = await cashier.SendAsync(complete);
                issued.EnsureSuccessStatusCode();
                var result = (await issued.Content.ReadFromJsonAsync<CompleteOnlineOrderSaleResponse>())!;
                Assert.Equal(initial.Total, result.Receipt.PayableAmount);
                Assert.True(result.Receipt.IsRestaurantSale);
                Assert.Equal(3_000m + tipAmount, result.Receipt.RestaurantTipAmount);
                using var reprintUser = fixture.CreateAdminClient("sales.reprint");
                using var reprint = await reprintUser.PostAsJsonAsync(
                    $"/api/commerce/v1/pos/drafts/sales/{result.Receipt.DocumentId}/receipt",
                    new OnlineSalesHistoryContext(fixture.BusinessId));
                reprint.EnsureSuccessStatusCode();
                var copy = (await reprint.Content.ReadFromJsonAsync<OnlineSalesReceipt>())!;
                Assert.Equal(initial.Total, copy.PayableAmount);
                Assert.Equal(3_000m + tipAmount, copy.RestaurantTipAmount);
                await using var projected = new SqlCommand("""
                    SELECT TotalAmount,RestaurantTipAmount
                    FROM reporting.SalesReportDocuments
                    WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId;
                    """, connection);
                projected.Parameters.AddWithValue("@DocumentId", result.Receipt.DocumentId);
                projected.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                await using var projectionReader = await projected.ExecuteReaderAsync();
                Assert.True(await projectionReader.ReadAsync());
                Assert.Equal(initial.Total, projectionReader.GetDecimal(0));
                Assert.Equal(3_000m + tipAmount, projectionReader.GetDecimal(1));
            }
            await using (var verify = new SqlCommand("""
                SELECT collection.TipAmount,
                  (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments source
                   WHERE source.SourceDocumentId=collection.CollectionId
                     AND source.SourceDocumentType=N'RestaurantTip')
                FROM sales.RestaurantAccountCollections collection
                WHERE collection.CollectionId=@CollectionId;
                """, connection))
            {
                verify.Parameters.AddWithValue("@CollectionId", collectionId);
                await using var reader = await verify.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(tipAmount, reader.GetDecimal(0));
                Assert.Equal(1, reader.GetInt32(1));
            }
        }
        finally
        {
            await using var restore = new SqlCommand("""
                UPDATE dbo.Businesses
                SET PointOfSaleMode=N'DirectSale',RestaurantSuggestedTipPercent=0
                WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
                """, connection);
            restore.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            restore.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            await restore.ExecuteNonQueryAsync();
        }
    }
}
