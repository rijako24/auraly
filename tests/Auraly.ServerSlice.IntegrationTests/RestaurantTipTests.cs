using System.Net.Http.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

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
        var restaurantProductId = Guid.NewGuid();
        var taxProfileId = Guid.NewGuid();
        using var waiter = fixture.CreateUserClient(waiterId, "restaurant.attend");
        using var cashier = fixture.CreateAdminClient("sales.create", "businesses.update");
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        using var accountingScope = fixture.CreateScope();
        var accounting = accountingScope.ServiceProvider.GetRequiredService<AccountingService>();
        var accountingUser = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate });
        await accounting.EnsureDefaultsAsync(accountingUser);
        await accounting.ActivateAsync(accountingUser, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
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
            INSERT dbo.TaxProfiles(TaxProfileId,TenantId,Code,Name,Rate,
                IsActive,CreatedAt)
            VALUES(@TaxProfileId,@TenantId,@TaxCode,N'IVA plato de prueba',19,
                1,SYSUTCDATETIME());
            INSERT dbo.Products(ProductId,TenantId,ProductCode,BaseUnitCode,
                TaxProfileId,Name,Currency,ManageStock,IsIngredient,IsActive,CreatedAt)
            SELECT @RestaurantProductId,TenantId,@RestaurantProductCode,
                   N'EA',@TaxProfileId,N'Plato con ajuste al peso',Currency,
                   0,0,1,SYSUTCDATETIME()
            FROM dbo.Products WHERE ProductId=@ProductId AND TenantId=@TenantId;
            INSERT dbo.ProductPrices(ProductPriceId,BusinessId,ProductId,Amount,
                PreparedAmount,CurrencyCode,ValidFrom,IsActive,CreatedAt)
            VALUES(NEWID(),@BusinessId,@RestaurantProductId,373284,373284,
                N'COP',DATEADD(day,-1,SYSDATETIMEOFFSET()),1,SYSDATETIMEOFFSET());
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
            setup.Parameters.AddWithValue("@RestaurantProductId", restaurantProductId);
            setup.Parameters.AddWithValue("@TaxProfileId", taxProfileId);
            setup.Parameters.AddWithValue("@TaxCode", $"RND-{taxProfileId:N}"[..32]);
            setup.Parameters.AddWithValue("@RestaurantProductCode",
                $"RND-{restaurantProductId:N}"[..32]);
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
                new AddRestaurantAccountItemRequest(Guid.NewGuid(), restaurantProductId,
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
            Assert.Equal(746568m, initial.Total);
            var adjustment = PosPaymentRoundingPolicy.Adjustment(initial.Total);
            Assert.Equal(32m, adjustment);
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
            using (var missingAdjustment = new HttpRequestMessage(HttpMethod.Post,
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/collections"))
            {
                missingAdjustment.Content = JsonContent.Create(
                    new CaptureRestaurantCollectionRequest(Guid.NewGuid(),
                        workSessionId, partial.AccountVersion,
                        [new RestaurantCollectionAllocationRequest(addition.AdditionId, 1)],
                        [new OnlineSalesPayment("Cash", half, null)]));
                missingAdjustment.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
                using var rejected = await cashier.SendAsync(missingAdjustment);
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, rejected.StatusCode);
            }
            RestaurantCollectionSummary paid;
            using (var request = new HttpRequestMessage(HttpMethod.Post,
                $"/api/commerce/v1/restaurant/accounts/{account.AccountId}/collections"))
            {
                request.Content = JsonContent.Create(new CaptureRestaurantCollectionRequest(
                    secondCollectionId, workSessionId, partial.AccountVersion,
                    [new RestaurantCollectionAllocationRequest(addition.AdditionId, 1)],
                    [new OnlineSalesPayment("Cash", half, null,
                        TenderedAmount: half + adjustment,
                        RoundingAdjustment: adjustment)],
                    new OnlineSalesPayment("Cash", 2_000m, null)));
                request.Headers.Add("Idempotency-Key", secondCollectionId.ToString("N"));
                using var response = await cashier.SendAsync(request);
                response.EnsureSuccessStatusCode();
                paid = (await response.Content.ReadFromJsonAsync<RestaurantCollectionSummary>())!;
                Assert.Equal(0, paid.Remaining);
                Assert.Equal(initial.Total, paid.Collected);
                Assert.Equal(3_000m, paid.TipsCollected);
            }
            paid = await AssertTipReversalUsesOriginalJournalAsync(cashier, connection, account.AccountId, workSessionId, paid,
                documentType == PosSaleDocumentTypes.Invoice);
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
                                TenderedAmount: initial.Total + 5_000m + adjustment,
                                RoundingAdjustment: adjustment)],
                            DocumentType: documentType)))
                };
                complete.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
                using var issued = await cashier.SendAsync(complete);
                issued.EnsureSuccessStatusCode();
                var result = (await issued.Content.ReadFromJsonAsync<CompleteOnlineOrderSaleResponse>())!;
                Assert.Equal(initial.Total + adjustment, result.Receipt.PayableAmount);
                Assert.Equal(adjustment, result.Receipt.PayableRoundingAmount);
                Assert.True(result.Receipt.IsRestaurantSale);
                Assert.Equal(3_000m + tipAmount, result.Receipt.RestaurantTipAmount);
                using var reprintUser = fixture.CreateAdminClient("sales.reprint");
                using var reprint = await reprintUser.PostAsJsonAsync(
                    $"/api/commerce/v1/pos/drafts/sales/{result.Receipt.DocumentId}/receipt",
                    new OnlineSalesHistoryContext(fixture.BusinessId));
                reprint.EnsureSuccessStatusCode();
                var copy = (await reprint.Content.ReadFromJsonAsync<OnlineSalesReceipt>())!;
                Assert.Equal(initial.Total + adjustment, copy.PayableAmount);
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
                Assert.Equal(initial.Total + adjustment, projectionReader.GetDecimal(0));
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

    private async Task<RestaurantCollectionSummary> AssertTipReversalUsesOriginalJournalAsync(
        HttpClient cashier, SqlConnection connection, Guid accountId, Guid workSessionId, RestaurantCollectionSummary before, bool bankPayment)
    {
        var id = Guid.NewGuid();
        using var admin = fixture.CreateAdminClient(AccountingPermissionCodes.Configure);
        Guid? bankId = null;
        if (bankPayment)
        {
            bankId = Guid.NewGuid();
            await using var type = new SqlCommand("SELECT OptionId FROM reference.Options WHERE CatalogCode=N'bank-account-type' AND Code=N'Checking'", connection);
            var typeId = (Guid)(await type.ExecuteScalarAsync())!;
            using var bank = await admin.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bankId}",
                new SaveBankAccountRequest(bankId.Value, null, typeId, "Banco para reversión", bankId.Value.ToString("N"), "Propina bancaria", false, true, null));
            Assert.True(bank.IsSuccessStatusCode, await bank.Content.ReadAsStringAsync());
            await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, admin, "1119", "Asset", "Bank", "Debit", bankId);
        }
        var payment = bankPayment ? new OnlineSalesPayment("Transfer", 123m, "PROPINA", BankAccountId: bankId)
            : new OnlineSalesPayment("Cash", 123m, null, TenderedAmount: 123m);
        using var capture = new HttpRequestMessage(HttpMethod.Post, $"/api/commerce/v1/restaurant/accounts/{accountId}/collections")
        {
            Content = JsonContent.Create(new CaptureRestaurantCollectionRequest(id, workSessionId, before.AccountVersion,
                [], [], payment))
        };
        capture.Headers.Add("Idempotency-Key", id.ToString("N"));
        using var accepted = await cashier.SendAsync(capture);
        Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());
        var captured = (await accepted.Content.ReadFromJsonAsync<RestaurantCollectionSummary>())!;
        using var scope = fixture.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        await processor.ProcessAsync(id, "RestaurantTip", fixture.BusinessId, default);
        if (bankPayment)
        {
            // Model an already posted pre-cutover source. Ambiguous new bank
            // configuration must not prevent reversal of its actual journal.
            await using var legacy = new SqlCommand("UPDATE dbo.AccountingPostingJobs SET ResolvedBankAccountsJson=NULL,ResolvedAccountMappingsJson=NULL WHERE SourceDocumentId=@Id AND SourceDocumentType=N'RestaurantTip' AND BusinessId=@Business", connection);
            legacy.Parameters.AddWithValue("@Id", id);
            legacy.Parameters.AddWithValue("@Business", fixture.BusinessId);
            Assert.Equal(1, await legacy.ExecuteNonQueryAsync());
            await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, admin, "1118", "Asset", "Bank", "Debit", bankId);
        }
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, admin);
        var liability = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, admin,
            "2389", "Liability", "General", "Credit");
        await using var mapping = new SqlCommand("""
            DECLARE @Previous nvarchar(max)=(SELECT MappingId,AccountId FROM dbo.AccountingAccountMappings
              WHERE TenantId=@Tenant AND Category IN(N'Cash',N'RestaurantTipsPayable') FOR JSON PATH);
            UPDATE dbo.AccountingAccountMappings SET AccountId=CASE Category WHEN N'Cash' THEN @Cash ELSE @Liability END
              WHERE TenantId=@Tenant AND Category IN(N'Cash',N'RestaurantTipsPayable');
            SELECT @Previous;
            """, connection);
        mapping.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        mapping.Parameters.AddWithValue("@Cash", cash.AccountId);
        mapping.Parameters.AddWithValue("@Liability", liability.AccountId);
        var previous = (string)(await mapping.ExecuteScalarAsync())!;
        RestaurantCollectionSummary result;
        try
        {
            using var reverse = await cashier.PostAsJsonAsync(
                $"/api/commerce/v1/restaurant/accounts/{accountId}/collections/{id}/reverse",
                new ReverseRestaurantCollectionRequest(captured.AccountVersion));
            Assert.True(reverse.IsSuccessStatusCode, await reverse.Content.ReadAsStringAsync());
            result = (await reverse.Content.ReadFromJsonAsync<RestaurantCollectionSummary>())!;
            await processor.ProcessAsync(id, "RestaurantTipReversal", fixture.BusinessId, default);
            await processor.ProcessAsync(id, "RestaurantTipReversal", fixture.BusinessId, default);
            await using var verify = new SqlCommand("""
                SELECT entry.SourceDocumentType,line.AccountId,line.Debit,line.Credit,line.CostCenterId
                FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
                WHERE entry.SourceDocumentId=@Id AND entry.BusinessId=@Business AND entry.TenantId=@Tenant
                  AND entry.SourceDocumentType IN(N'RestaurantTip',N'RestaurantTipReversal')
                ORDER BY entry.SourceDocumentType,line.LineNumber;
                """, connection);
            verify.Parameters.AddWithValue("@Id", id);
            verify.Parameters.AddWithValue("@Business", fixture.BusinessId);
            verify.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            var original = new List<(Guid Account, decimal Debit, decimal Credit, Guid Center)>();
            var reversed = new List<(Guid Account, decimal Debit, decimal Credit, Guid Center)>();
            await using var rows = await verify.ExecuteReaderAsync();
            while (await rows.ReadAsync())
            {
                var line = (rows.GetGuid(1), rows.GetDecimal(2), rows.GetDecimal(3), rows.GetGuid(4));
                if (rows.GetString(0) == "RestaurantTip") original.Add(line);
                else reversed.Add(line);
            }
            Assert.Equal(2, original.Count);
            Assert.Equal(original.Select(line => (line.Account, line.Credit, line.Debit, line.Center)), reversed);
            Assert.DoesNotContain(reversed, line => line.Account == cash.AccountId || line.Account == liability.AccountId);
            Assert.Equal(before.TipsCollected, result.TipsCollected);
        }
        finally
        {
            await using var restore = new SqlCommand("""
                UPDATE mapping SET AccountId=original.AccountId FROM dbo.AccountingAccountMappings mapping
                JOIN OPENJSON(@Previous) WITH(MappingId uniqueidentifier,AccountId uniqueidentifier) original ON original.MappingId=mapping.MappingId
                WHERE mapping.TenantId=@Tenant;
                """, connection);
            restore.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            restore.Parameters.AddWithValue("@Previous", previous);
            await restore.ExecuteNonQueryAsync();
        }
        return result;
    }
}
