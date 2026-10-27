using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(AccountingSliceCollection.Name)]
public sealed class PortfolioManualCardTests(ServerSliceFixture fixture)
{
    [Theory]
    [InlineData(true, "DebitCard")]
    [InlineData(true, "CreditCard")]
    [InlineData(false, "DebitCard")]
    [InlineData(false, "CreditCard")]
    public async Task Card_payment_uses_the_chosen_auxiliary_without_turning_its_bank_dimension_into_a_transfer(bool supplier, string method)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend, AccountingPermissionCodes.Retry,
            PayablesPermissionCodes.RegisterPayment, ReceivablesPermissionCodes.RegisterPayment);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activate = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) activate.EnsureSuccessStatusCode();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Guid bankType; Guid methodId;
        await using (var options = new SqlCommand("""
            SELECT OptionId FROM reference.Options WHERE CatalogCode=N'bank-account-type' AND Code=N'Checking';
            SELECT OptionId FROM reference.Options WHERE CatalogCode=N'payment-method' AND Code=@Method;
            """, connection))
        {
            options.Parameters.AddWithValue("@Method", method);
            await using var reader = await options.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync()); bankType = reader.GetGuid(0);
            await reader.NextResultAsync(); Assert.True(await reader.ReadAsync()); methodId = reader.GetGuid(0);
        }
        var bankId = Guid.NewGuid();
        using (var bank = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bankId}",
            new SaveBankAccountRequest(bankId, null, bankType, "Banco de la cuenta de tarjeta", bankId.ToString("N"),
                "Dimensión bancaria de tarjeta", false, true, null))) bank.EnsureSuccessStatusCode();
        var function = !supplier ? "CardClearing" : method == "CreditCard" ? "CreditCardLiability" : "Bank";
        var liability = function == "CreditCardLiability";
        var card = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            liability ? "2394" : "1394", liability ? "Liability" : "Asset", function,
            liability ? "Credit" : "Debit", bankId, methodId, sharedAcrossBusinesses: true);
        var control = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            supplier ? "2294" : "1393", supplier ? "Liability" : "Asset", supplier ? "Payable" : "Receivable", supplier ? "Credit" : "Debit");
        var counterpart = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            supplier ? "5194" : "4194", supplier ? "Expense" : "Revenue", "General", supplier ? "Debit" : "Credit");
        var at = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var partyId = fixture.SupplierPartyId; // Also exercises collections for a party without a Customer role.
        var opened = await SaveAndSendAsync(new(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher, at,
            "MANUAL_VOUCHER", "Obligación para la tarjeta", "OBL-CARD",
            [new(control.AccountId, partyId, null, "Obligación", supplier ? 0 : 1_000m, supplier ? 1_000m : 0,
                "OBL-CARD", Guid.NewGuid(), DueDate: at.AddDays(30)),
             new(counterpart.AccountId, partyId, null, "Contrapartida", supplier ? 1_000m : 0, supplier ? 0 : 1_000m, LineId: Guid.NewGuid())],
            null, null, MainPartyId: partyId, PayloadVersion: 4, VoucherTypeCode: "Accounting"));
        var positionId = Assert.Single(opened.FinancialEffects!.Positions).PositionId;
        var request = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher, at,
            "MANUAL_VOUCHER", "Tarjeta desde el auxiliar", "CARD-PAY",
            [new(control.AccountId, partyId, null, "Abono", supplier ? 200m : 0, supplier ? 0 : 200m,
                LineId: Guid.NewGuid(), ExistingPosition: new(supplier ? "Payable" : "Receivable", positionId)),
             new(card.AccountId, partyId, null, "Cuenta de tarjeta", supplier ? 0 : 200m, supplier ? 200m : 0,
                "CARD-REF", Guid.NewGuid(), PaymentEvidence: new("Visa", "CARD-APPROVAL"))],
            null, null, MainPartyId: partyId, PayloadVersion: 4, VoucherTypeCode: supplier ? "Disbursement" : "Accounting",
            WorkSessionId: await fixture.OpenWebWorkSessionAsync());
        var posted = await SaveAndSendAsync(request);
        var payment = Assert.Single(posted.FinancialEffects!.Payments);
        var tender = Assert.Single(payment.Tenders);
        Assert.Equal(method, tender.MethodCode); Assert.Equal(card.AccountId, tender.AccountId);
        Assert.Null(tender.BankAccountId); // Bank identity remains on the immutable PUC auxiliary.
        var table = supplier ? "SupplierPaymentTenders" : "CustomerPaymentTenders";
        var positions = supplier ? "Payables" : "Receivables";
        var key = supplier ? "PayableId" : "ReceivableId";
        await using var verify = new SqlCommand($"""
            SELECT tender.AccountId,tender.BankAccountId,account.BankAccountId,line.Debit,line.Credit
            FROM dbo.{table} tender
            JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=@DocumentId AND entry.SourceDocumentType=N'ManualAccountingVoucher'
              AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId
            JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId AND line.AccountId=tender.AccountId
            JOIN dbo.AccountingAccounts account ON account.AccountId=tender.AccountId AND account.TenantId=@TenantId
            WHERE tender.PaymentId=@PaymentId;
            SELECT OutstandingAmount FROM dbo.{positions} WHERE {key}=@PositionId AND BusinessId=@BusinessId;
            """, connection);
        verify.Parameters.AddWithValue("@TenantId", fixture.TenantId); verify.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        verify.Parameters.AddWithValue("@DocumentId", request.DocumentId); verify.Parameters.AddWithValue("@PaymentId", payment.PaymentId);
        verify.Parameters.AddWithValue("@PositionId", positionId);
        await using var result = await verify.ExecuteReaderAsync();
        Assert.True(await result.ReadAsync()); Assert.Equal(card.AccountId, result.GetGuid(0)); Assert.True(result.IsDBNull(1));
        Assert.Equal(bankId, result.GetGuid(2)); Assert.Equal(supplier ? 0 : 200m, result.GetDecimal(3));
        Assert.Equal(supplier ? 200m : 0, result.GetDecimal(4)); Assert.False(await result.ReadAsync());
        await result.NextResultAsync(); Assert.True(await result.ReadAsync()); Assert.Equal(800m, result.GetDecimal(0));
        await result.DisposeAsync();
        if (supplier)
        {
            await using (var series = new SqlCommand("dbo.BusinessDocumentSeriesDefaultsProvision", connection)
                { CommandType = System.Data.CommandType.StoredProcedure })
            {
                series.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                await series.ExecuteNonQueryAsync();
            }
            // Paying with a corporate card/bank withdrawal cannot use the
            // acquiring-operator asset configured for customer collections.
            var category = method == "DebitCard" ? "SupplierDebitCard" : "SupplierCreditCard";
            var nativeId = Guid.NewGuid();
            var native = new ConfirmSupplierPaymentRequest(nativeId, fixture.BusinessId, fixture.SupplierId, at,
                "COP", "Misma cuenta desde cuentas por pagar", [new(positionId, 200m)],
                [new(method, 200m, Reference: "CARD-REF", CardFranchiseCode: "Visa", ApprovalNumber: "CARD-APPROVAL")], request.WorkSessionId);
            await SendNativeAsync();
            await AssertNotPaidAsync(); // No fabricated default bank/card auxiliary.
            await SetMappingAsync(null, card.AccountId);
            var wrong = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
                "1492", "Asset", "General", "Debit");
            using (var invalidMapping = await client.PutAsJsonAsync("/api/commerce/v1/accounting/account-mappings",
                new SetAccountMappingRequest(fixture.TenantId, fixture.BusinessId, category, wrong.AccountId, new(2026, 1, 1), null)))
                Assert.Equal(HttpStatusCode.Conflict, invalidMapping.StatusCode);
            // New configuration rejects this shape. Inject it only in the
            // disposable fixture to exercise an invalid legacy override at retry.
            await using (var legacyMapping = new SqlCommand("""
                UPDATE dbo.AccountingAccountMappings SET AccountId=@AccountId
                WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND Category=@Category AND EffectiveFrom=@From;
                IF @@ROWCOUNT=0
                  INSERT dbo.AccountingAccountMappings(MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,CreatedAt)
                  VALUES(NEWID(),@TenantId,@BusinessId,@Category,@AccountId,@From,SYSDATETIMEOFFSET());
                """, connection))
            {
                legacyMapping.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                legacyMapping.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                legacyMapping.Parameters.AddWithValue("@Category", category);
                legacyMapping.Parameters.AddWithValue("@AccountId", wrong.AccountId);
                legacyMapping.Parameters.AddWithValue("@From", new DateTime(2026, 1, 1));
                await legacyMapping.ExecuteNonQueryAsync();
            }
            using (var retryWrong = await client.PostAsync($"/api/commerce/v1/accounting/postings/{nativeId}/retry", null))
                retryWrong.EnsureSuccessStatusCode();
            await AssertNotPaidAsync(); // Invalid override cannot choose the valid shared mapping.
            await SetMappingAsync(fixture.BusinessId, card.AccountId);
            using (var applications = PortfolioOriginalAccountScenario.CountApplicationWrites(fixture))
            {
                using (var retry = await client.PostAsync($"/api/commerce/v1/accounting/postings/{nativeId}/retry", null))
                    retry.EnsureSuccessStatusCode();
                for (var attempt = 0; attempt < 2; attempt++)
                    await SendNativeAsync();
                Assert.Equal(1, applications.Count);
            }
            await using var sameAccount = new SqlCommand("""
                SELECT line.AccountId,line.Credit FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line
                  ON line.EntryId=entry.EntryId
                WHERE entry.SourceDocumentId=@NativeId AND entry.SourceDocumentType=N'PayablePayment'
                  AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId AND line.Credit>0;
                SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@PositionId AND BusinessId=@BusinessId;
                """, connection);
            sameAccount.Parameters.AddWithValue("@NativeId", nativeId); sameAccount.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            sameAccount.Parameters.AddWithValue("@BusinessId", fixture.BusinessId); sameAccount.Parameters.AddWithValue("@PositionId", positionId);
            await using var nativeResult = await sameAccount.ExecuteReaderAsync();
            Assert.True(await nativeResult.ReadAsync()); Assert.Equal(card.AccountId, nativeResult.GetGuid(0));
            Assert.Equal(200m, nativeResult.GetDecimal(1)); Assert.False(await nativeResult.ReadAsync());
            await nativeResult.NextResultAsync(); Assert.True(await nativeResult.ReadAsync()); Assert.Equal(600m, nativeResult.GetDecimal(0));

            async Task SendNativeAsync()
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/payable-payments/confirm")
                    { Content = JsonContent.Create(native) };
                message.Headers.Add("Idempotency-Key", nativeId.ToString("N"));
                using var response = await client.SendAsync(message);
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            }
            async Task SetMappingAsync(Guid? businessId, Guid accountId)
            {
                using var mapping = await client.PutAsJsonAsync("/api/commerce/v1/accounting/account-mappings",
                    new SetAccountMappingRequest(fixture.TenantId, businessId, category, accountId, new(2026, 1, 1), null));
                Assert.True(mapping.IsSuccessStatusCode, await mapping.Content.ReadAsStringAsync());
            }
            async Task AssertNotPaidAsync()
            {
                await using var state = new SqlCommand("""
                    SELECT balance.OutstandingAmount,job.LastErrorCode,job.LastErrorMessage,
                      (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@NativeId),
                      (SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@NativeId)
                    FROM dbo.Payables balance JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=@NativeId
                      AND job.SourceDocumentType=N'PayablePayment' AND job.BusinessId=balance.BusinessId
                    WHERE balance.PayableId=@PositionId AND balance.BusinessId=@BusinessId;
                    """, connection);
                state.Parameters.AddWithValue("@NativeId", nativeId); state.Parameters.AddWithValue("@PositionId", positionId);
                state.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                await using var stateRow = await state.ExecuteReaderAsync();
                Assert.True(await stateRow.ReadAsync()); Assert.Equal(800m, stateRow.GetDecimal(0));
                Assert.Equal("AccountMappingMissing", stateRow.GetString(1)); Assert.Contains("El pago no se realizó", stateRow.GetString(2));
                Assert.Equal(0, stateRow.GetInt32(3)); Assert.Equal(0, stateRow.GetInt32(4));
            }
        }

        async Task<VoucherDraftView> SaveAndSendAsync(SaveVoucherDraftRequest capture)
        {
            var path = $"/api/commerce/v1/accounting/manual/drafts/{capture.DocumentId}";
            using var save = await client.PutAsJsonAsync(path, capture);
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
            var saved = (await save.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            using var send = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion));
            Assert.True(send.IsSuccessStatusCode, await send.Content.ReadAsStringAsync());
            var sent = (await send.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            Assert.Equal("Posted", sent.Status); return sent;
        }
    }
}
