using System.Net.Http.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class RestaurantTipTests(ServerSliceFixture fixture)
{
    [Theory]
    [InlineData(PosSaleDocumentTypes.Receipt)]
    [InlineData(PosSaleDocumentTypes.Invoice)]
    public async Task Suggested_prebill_and_received_tip_leave_the_sales_document_at_consumption_value(string documentType)
    {
        var waiterId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var secondPartyId = Guid.NewGuid();
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
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,
                FirstName,LastName,CompletionStatus,IsActive,CreatedAt)
            VALUES(@SecondPartyId,@TenantId,N'NaturalPerson',N'Compañero propina',
                   N'Compañero',N'Propina',N'Incomplete',1,SYSDATETIMEOFFSET());
            INSERT parties.Waiters(WaiterId,TenantId,PartyId,Code,IsActive,CreatedAt)
            VALUES(NEWID(),@TenantId,@SecondPartyId,@SecondCode,1,SYSDATETIMEOFFSET());
            UPDATE dbo.AppUsers SET PartyId=@PartyId
            WHERE UserId=@WaiterId AND TenantId=@TenantId;
            DELETE FROM sales.RestaurantProductRoutes
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND ProductId=@ProductId;
            """, connection))
        {
            setup.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            setup.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            setup.Parameters.AddWithValue("@PartyId", partyId);
            setup.Parameters.AddWithValue("@SecondPartyId", secondPartyId);
            setup.Parameters.AddWithValue("@WaiterId", waiterId);
            setup.Parameters.AddWithValue("@Code", $"T-{partyId:N}"[..12]);
            setup.Parameters.AddWithValue("@SecondCode", $"T-{secondPartyId:N}"[..12]);
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
                    [new OnlineSalesPayment("Cash", half, null,
                        TenderedAmount: half + 5_000m)],
                    new OnlineSalesPayment("Cash", 1_000m, null,
                        TenderedAmount: 1_000m)));
                request.Headers.Add("Idempotency-Key", productCollectionId.ToString("N"));
                using var response = await cashier.SendAsync(request);
                response.EnsureSuccessStatusCode();
                partial = (await response.Content.ReadFromJsonAsync<RestaurantCollectionSummary>())!;
                Assert.Equal(half, partial.Remaining);
                Assert.Equal(half, partial.Collected);
                Assert.Equal(1_000m, partial.TipsCollected);
                Assert.Equal(half + 5_000m,
                    partial.Collections.Single(item =>
                        item.CollectionId == productCollectionId)
                        .Payments.Single().TenderedAmount);
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
                    [], [], new OnlineSalesPayment("Cash", tipAmount, null,
                        TenderedAmount: tipAmount + 5_000m)));
                request.Headers.Add("Idempotency-Key", collectionId.ToString("N"));
                using var response = await cashier.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var captured = (await response.Content.ReadFromJsonAsync<RestaurantCollectionSummary>())!;
                Assert.Equal(0, captured.Remaining);
                Assert.Equal(initial.Total, captured.Collected);
                Assert.Equal(3_000m + tipAmount, captured.TipsCollected);
                Assert.Equal(tipAmount + 5_000m,
                    captured.Collections.Single(item => item.CollectionId == collectionId)
                        .TipPayment?.TenderedAmount);
                using var complete = new HttpRequestMessage(HttpMethod.Post,
                    $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/complete")
                {
                    Content = JsonContent.Create(new CompleteRestaurantAccountRequest(
                        workSessionId, new CompleteOnlineSalesDraftRequest(
                            captured.AccountVersion,
                            [new OnlineSalesPayment("Cash", initial.Total, null,
                                TenderedAmount: initial.Total + 5_000m)],
                            DocumentType: documentType)))
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
            using var supervisor = fixture.CreateAdminClient("cash.close");
            using (var forbidden = await cashier.GetAsync(
                "/api/commerce/v1/restaurant/tips/pending"))
                Assert.Equal(System.Net.HttpStatusCode.Forbidden, forbidden.StatusCode);
            using (var pendingResponse = await supervisor.GetAsync(
                "/api/commerce/v1/restaurant/tips/pending"))
            {
                pendingResponse.EnsureSuccessStatusCode();
                var pending = (await pendingResponse.Content.ReadFromJsonAsync<RestaurantTipPendingPage>())!;
                Assert.Contains(pending.Items, item => item.CollectionId == collectionId);
            }
            var settlementId = Guid.NewGuid();
            var settlementRequest = new SettleRestaurantTipsRequest(settlementId,
                [productCollectionId, secondCollectionId],
                [new RestaurantTipRecipientRequest(partyId, 0),
                 new RestaurantTipRecipientRequest(secondPartyId, 0)],
                "EqualByLaw", null, "Cash", null, null);
            using (var settled = await supervisor.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/tips/settlements", settlementRequest))
            {
                settled.EnsureSuccessStatusCode();
                var result = (await settled.Content.ReadFromJsonAsync<RestaurantTipSettlement>())!;
                Assert.Equal(3_000m, result.TotalAmount);
                Assert.Equal(2, result.Recipients.Count);
                Assert.All(result.Recipients, recipient => Assert.Equal(1_500m, recipient.Amount));
            }
            using (var replay = await supervisor.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/tips/settlements", settlementRequest))
                replay.EnsureSuccessStatusCode();
            using (var changedReplay = await supervisor.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/tips/settlements",
                settlementRequest with { PaymentReference = "otro" }))
                Assert.Equal(System.Net.HttpStatusCode.Conflict, changedReplay.StatusCode);
            var agreedSettlementId = Guid.NewGuid();
            using (var agreed = await supervisor.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/tips/settlements",
                new SettleRestaurantTipsRequest(agreedSettlementId,
                    [collectionId], [new RestaurantTipRecipientRequest(partyId, tipAmount)],
                    "WorkerAgreement", "Acta de trabajadores 2026-10-14",
                    "Cash", null, null)))
            {
                agreed.EnsureSuccessStatusCode();
                var result = (await agreed.Content.ReadFromJsonAsync<RestaurantTipSettlement>())!;
                Assert.Equal(tipAmount, result.TotalAmount);
                Assert.Equal("WorkerAgreement", result.DistributionBasis);
            }
            using (var duplicate = await supervisor.PostAsJsonAsync(
                "/api/commerce/v1/restaurant/tips/settlements",
                new SettleRestaurantTipsRequest(Guid.NewGuid(), [collectionId],
                    [new RestaurantTipRecipientRequest(partyId, 0)],
                    "EqualByLaw", null, "Cash", null, null)))
                Assert.Equal(System.Net.HttpStatusCode.Conflict, duplicate.StatusCode);
            using (var historyResponse = await supervisor.GetAsync(
                "/api/commerce/v1/restaurant/tips/settlements"))
            {
                historyResponse.EnsureSuccessStatusCode();
                var history = (await historyResponse.Content.ReadFromJsonAsync<RestaurantTipSettlement[]>())!;
                Assert.Contains(history, item => item.SettlementId == settlementId);
            }
            using (var afterResponse = await supervisor.GetAsync(
                "/api/commerce/v1/restaurant/tips/pending"))
            {
                afterResponse.EnsureSuccessStatusCode();
                var after = (await afterResponse.Content.ReadFromJsonAsync<RestaurantTipPendingPage>())!;
                Assert.DoesNotContain(after.Items, item =>
                    item.CollectionId == productCollectionId ||
                    item.CollectionId == secondCollectionId ||
                    item.CollectionId == collectionId);
            }
            await using (var verifySettlement = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM sales.RestaurantTipSettlementCollections
                        WHERE SettlementId=@SettlementId),
                       (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments
                        WHERE SourceDocumentId=@SettlementId
                          AND SourceDocumentType=N'RestaurantTipSettlement');
                """, connection))
            {
                verifySettlement.Parameters.AddWithValue("@SettlementId", settlementId);
                await using var reader = await verifySettlement.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(2, reader.GetInt32(0));
                Assert.Equal(1, reader.GetInt32(1));
            }
            decimal? posted = null;
            for (var attempt = 0; attempt < 100 && posted is null; attempt++)
            {
                await using var entry = new SqlCommand("""
                    SELECT DebitTotal FROM dbo.AccountingEntries
                    WHERE SourceDocumentId=@SettlementId
                      AND SourceDocumentType=N'RestaurantTipSettlement';
                    """, connection);
                entry.Parameters.AddWithValue("@SettlementId", settlementId);
                posted = await entry.ExecuteScalarAsync() is decimal amount ? amount : null;
                if (posted is null) await Task.Delay(100);
            }
            Assert.Equal(3_000m, posted);
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
