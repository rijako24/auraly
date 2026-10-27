using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(AccountingSliceCollection.Name)]
public sealed class AccountingDirectTreasuryTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Pure_ledger_entry_cannot_be_accepted_as_a_disbursement_without_treasury()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activation = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) activation.EnsureSuccessStatusCode();
        var expense = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "5194", "Expense", "General", "Debit");
        var counterpart = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "3194", "Equity", "General", "Credit");
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        foreach (var type in new[] { "Disbursement", "Accounting" })
        {
            var document = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
                new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Asiento puro sin tesorería", null,
                [new(expense.AccountId, null, null, "Gasto", 100m, 0, LineId: Guid.NewGuid()),
                 new(counterpart.AccountId, null, null, "Contrapartida general", 0, 100m, LineId: Guid.NewGuid())],
                null, null, PayloadVersion: 4, VoucherTypeCode: type);
            var path = $"/api/commerce/v1/accounting/manual/drafts/{document.DocumentId}";
            using var save = await client.PutAsJsonAsync(path, document);
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
            var saved = (await save.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            using var send = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion));
            if (type == "Disbursement")
            {
                Assert.Equal(HttpStatusCode.BadRequest, send.StatusCode);
                Assert.Contains("No se registró el egreso", await send.Content.ReadAsStringAsync());
            }
            else
            {
                Assert.True(send.IsSuccessStatusCode, await send.Content.ReadAsStringAsync());
                var posted = (await send.Content.ReadFromJsonAsync<VoucherDraftView>())!;
                Assert.Equal("Posted", posted.Status);
                Assert.Empty(posted.FinancialEffects!.Payments);
                Assert.Empty(posted.FinancialEffects.Positions);
                Assert.Empty(posted.FinancialEffects.DirectMovements);
            }
            await using var verify = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id),
                  (SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id),
                  (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id),
                  (SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id);
                """, connection);
            verify.Parameters.AddWithValue("@Id", document.DocumentId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var expected = type == "Accounting" ? 1 : 0;
            Assert.Equal(expected, reader.GetInt32(0));
            Assert.Equal(expected, reader.GetInt32(1));
            Assert.Equal(expected, reader.GetInt32(2));
            Assert.Equal(0, reader.GetInt32(3));
        }
    }

    [Theory]
    [InlineData(true, "Cash")]
    [InlineData(false, "Cash")]
    [InlineData(true, "Transfer")]
    [InlineData(false, "Transfer")]
    [InlineData(false, "DebitCard")]
    [InlineData(true, "CreditCard")]
    public async Task Treasury_counterpart_without_an_invoice_uses_one_source_and_one_session_movement(bool outgoing, string method)
    {
        var userId = await fixture.CreateWorkSessionUserAsync("direct-treasury");
        using var client = fixture.CreateUserClient(userId, AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
            AccountingPermissionCodes.Retry,
            WorkSessionPermissionCodes.Read, WorkSessionPermissionCodes.Close, WorkSessionPermissionCodes.ManageCash,
            WorkSessionPermissionCodes.ReconcileClosures, WorkSessionPermissionCodes.ReadCashDifferences);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activate = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) activate.EnsureSuccessStatusCode();
        var cash = await CreateTreasuryAuxiliaryAsync(client, method, outgoing);
        var card = method is "DebitCard" or "CreditCard";
        var reference = method == "Cash" ? null : "TESORERIA-REF";
        var counterpart = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            outgoing ? "5194" : "4194", outgoing ? "Expense" : "Revenue", "General", outgoing ? "Debit" : "Credit");
        using var opened = await client.PostAsJsonAsync("/api/commerce/v1/work-sessions/current",
            new OpenWorkSessionRequest(fixture.BusinessId, fixture.WarehouseId, OpeningCash: 1_000m));
        Assert.True(opened.IsSuccessStatusCode, await opened.Content.ReadAsStringAsync());
        var session = (await opened.Content.ReadFromJsonAsync<WorkSessionView>())!.WorkSessionId;
        var before = (await client.GetFromJsonAsync<WorkSessionClosurePreviewView>(
            $"/api/commerce/v1/work-sessions/{session}/closure-preview"))!;
        var at = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var document = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
            at, "MANUAL_VOUCHER", "Movimiento directo de tesorería", "TESORERIA-DIRECTA",
            [new(cash.AccountId, fixture.SupplierPartyId, null, "Efectivo", outgoing ? 0 : 500m,
                outgoing ? 500m : 0, Reference: reference, LineId: Guid.NewGuid(),
                PaymentEvidence: card ? new("Visa", "APROBACION-DIRECTA") : null),
             new(counterpart.AccountId, fixture.SupplierPartyId, null, "Contrapartida identificada",
                outgoing ? 500m : 0, outgoing ? 0 : 500m, LineId: Guid.NewGuid())],
            null, null, MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4,
            VoucherTypeCode: outgoing ? "Disbursement" : "Accounting", WorkSessionId: session);
        var path = $"/api/commerce/v1/accounting/manual/drafts/{document.DocumentId}";
        using var savedResponse = await client.PutAsJsonAsync(path, document);
        Assert.True(savedResponse.IsSuccessStatusCode, await savedResponse.Content.ReadAsStringAsync());
        var saved = (await savedResponse.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        using (var limited = fixture.CreateUserClient(userId, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend))
        using (var forbidden = await limited.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var preparedSession = Guid.NewGuid();
        using (var preparedMessage = new HttpRequestMessage(HttpMethod.Post, "/api/pos/v1/work-sessions/opened")
               { Content = JsonContent.Create(new RegisterDeviceWorkSessionRequest(userId, preparedSession, fixture.BusinessId, DateTimeOffset.UtcNow)) })
        {
            preparedMessage.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
            preparedMessage.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
            using var prepared = await client.SendAsync(preparedMessage);
            Assert.True(prepared.IsSuccessStatusCode, await prepared.Content.ReadAsStringAsync());
        }
        foreach (var invalidSession in new[] { Guid.NewGuid(), fixture.WorkSessionId, preparedSession })
        {
            // A prepared-device session needs its local capture/projection. The
            // manual web editor must not inject a server-only movement into it.
            using var invalidSave = await client.PutAsJsonAsync(path,
                document with { WorkSessionId = invalidSession, RowVersion = saved.RowVersion });
            if (invalidSession != preparedSession)
            {
                Assert.Equal(HttpStatusCode.BadRequest, invalidSave.StatusCode);
                continue;
            }
            Assert.True(invalidSave.IsSuccessStatusCode, await invalidSave.Content.ReadAsStringAsync());
            var invalidDraft = (await invalidSave.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            using var invalidSend = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(invalidDraft.RowVersion));
            Assert.Equal(HttpStatusCode.BadRequest, invalidSend.StatusCode);
            using var restored = await client.PutAsJsonAsync(path, document with { RowVersion = invalidDraft.RowVersion });
            Assert.True(restored.IsSuccessStatusCode, await restored.Content.ReadAsStringAsync());
            saved = (await restored.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
        await using (var validationConnection = new SqlConnection(fixture.ConnectionString))
        {
            await validationConnection.OpenAsync();
            await using var rejected = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id)
                  +(SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id)
                  +(SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id);
                """, validationConnection);
            rejected.Parameters.AddWithValue("@Id", document.DocumentId);
            Assert.Equal(0, (int)(await rejected.ExecuteScalarAsync())!);
        }
        using (var fault = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains("DECLARE @JournalLines TABLE", StringComparison.Ordinal),
            beforeExecute: command =>
            {
                var journal = JsonNode.Parse((string)command.Parameters["@Lines"].Value)!.AsArray();
                journal[0]!["AccountId"] = Guid.Empty;
                command.Parameters["@Lines"].Value = journal.ToJsonString();
            }))
        using (var sent = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, sent.StatusCode);
            Assert.Equal(1, fault.Count);
        }
        var accepted = (await client.GetFromJsonAsync<VoucherDraftView>(path))!;
        Assert.Empty(accepted.FinancialEffects!.Payments);
        Assert.Empty(accepted.FinancialEffects.Positions);
        var movement = Assert.Single(accepted.FinancialEffects.DirectMovements);
        Assert.Equal(cash.AccountId, movement.AccountId);
        Assert.Equal(document.Lines[0].LineId, movement.SourceLineId);
        Assert.Equal(outgoing ? -500m : 500m, movement.Amount);
        await using (var failedConnection = new SqlConnection(fixture.ConnectionString))
        {
            await failedConnection.OpenAsync();
            await using var rolledBack = new SqlCommand(
                "SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id", failedConnection);
            rolledBack.Parameters.AddWithValue("@Id", document.DocumentId);
            Assert.Equal(0, (int)(await rolledBack.ExecuteScalarAsync())!);
        }
        var pending = (await client.GetFromJsonAsync<WorkSessionClosurePreviewView>(
            $"/api/commerce/v1/work-sessions/{session}/closure-preview"))!;
        Assert.Equal(before.ExpectedCash + (method == "Cash" ? outgoing ? -500m : 500m : 0m), pending.ExpectedCash);
        var closureMethod = card ? "Card" : method;
        Assert.Equal(before.PaymentTotals.Single(item => item.PaymentMethodCode == closureMethod).NetAmount + movement.Amount,
            pending.PaymentTotals.Single(item => item.PaymentMethodCode == closureMethod).NetAmount);
        WorkSessionClosureView? pendingClosure = null;
        if (method == "Transfer" && outgoing)
        {
            pendingClosure = await CloseSessionAsync(pending);
            var pendingPage = (await client.GetFromJsonAsync<WorkSessionPaymentVerificationPage>(
                $"/api/commerce/v1/work-sessions/closures/{pendingClosure.WorkSessionClosureId}/payment-verifications/page?page=1&pageSize=100"))!;
            Assert.Equal(movement.Amount, Assert.Single(pendingPage.Items, item => item.SourceId == movement.MovementId).Amount);
        }
        using (var retry = await client.PostAsync($"/api/commerce/v1/accounting/postings/{document.DocumentId}/retry", null))
            Assert.True(retry.IsSuccessStatusCode, await retry.Content.ReadAsStringAsync());
        using (var replay = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
            Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var verify = new SqlCommand("""
            SELECT
              (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id),
              (SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id),
              (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id),
              (SELECT COUNT(*) FROM dbo.SupplierPayments WHERE ParentSourceDocumentId=@Id),
              (SELECT COUNT(*) FROM dbo.CustomerPayments WHERE ParentSourceDocumentId=@Id),
              (SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id),
              (SELECT COUNT(*) FROM dbo.Receivables WHERE SourceDocumentId=@Id),
              (SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id AND WorkSessionId=@Session),
              (SELECT SUM(Amount) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id AND WorkSessionId=@Session);
            """, connection);
        verify.Parameters.AddWithValue("@Id", document.DocumentId);
        verify.Parameters.AddWithValue("@Session", session);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(new[] { 1, 1, 1, 0, 0, 0, 0, 1 }, Enumerable.Range(0, 8).Select(reader.GetInt32));
        Assert.Equal(outgoing ? -500m : 500m, reader.GetDecimal(8));
        await reader.DisposeAsync();
        // A closed session has its immutable closure instead of an open preview.
        // Its verification page below must still have exactly one movement after retry.
        var after = pendingClosure is null ? (await client.GetFromJsonAsync<WorkSessionClosurePreviewView>(
            $"/api/commerce/v1/work-sessions/{session}/closure-preview"))! : pending;
        Assert.Equal(pending.ExpectedCash, after.ExpectedCash);
        Assert.Equal(pending.PaymentTotals.Single(item => item.PaymentMethodCode == closureMethod).NetAmount,
            after.PaymentTotals.Single(item => item.PaymentMethodCode == closureMethod).NetAmount);
        if (method == "Cash")
            Assert.Contains(after.CashMovements!, item => item.DocumentId == document.DocumentId && item.Amount == 500m);
        else Assert.DoesNotContain(after.CashMovements!, item => item.DocumentId == document.DocumentId);

        var closure = pendingClosure ?? await CloseSessionAsync(after);
        var closurePath = $"/api/commerce/v1/work-sessions/closures/{closure.WorkSessionClosureId}";
        var page = (await client.GetFromJsonAsync<WorkSessionPaymentVerificationPage>(closurePath + "/payment-verifications/page?page=1&pageSize=100"))!;
        var verification = Assert.Single(page.Items, item => item.SourceId == movement.MovementId);
        Assert.Equal("ManualAccountingVoucher", verification.SourceDocumentType);
        Assert.Equal(closureMethod, verification.PaymentMethodCode);
        Assert.Equal(method, verification.TenderMethodCode);
        Assert.Equal(movement.Amount, verification.Amount);
        Assert.Equal(reference, verification.Reference);
        Assert.Equal(card ? "Visa" : null, verification.CardFranchiseCode);
        Assert.Equal(card ? "APROBACION-DIRECTA" : null, verification.ApprovalNumber);
        Assert.False(string.IsNullOrWhiteSpace(verification.DocumentNumber));
        Assert.Equal(page.TotalItems, page.Items.Count);
        WorkSessionPaymentCorrection[] corrections = [];
        AccountingAccountView? correctedCardAccount = null;
        var restoreMapping = false;
        var originalMappings = new List<Dictionary<string, object?>>();
        try
        {
            if (method != "Cash")
            {
                var correctedMethod = method;
                if (method == "Transfer" && outgoing)
                {
                    correctedMethod = "CreditCard";
                    correctedCardAccount = await CreateTreasuryAuxiliaryAsync(client, correctedMethod, outgoing: true);
                    Assert.StartsWith("AuralyServerSlice_", connection.Database);
                    await using (var capture = new SqlCommand("""
                        SELECT MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo,CreatedAt
                        FROM dbo.AccountingAccountMappings
                        WHERE TenantId=@Tenant AND BusinessId=@Business AND Category=N'SupplierCreditCard';
                        """, connection))
                    {
                        capture.Parameters.AddWithValue("@Tenant", fixture.TenantId);
                        capture.Parameters.AddWithValue("@Business", fixture.BusinessId);
                        await using var captured = await capture.ExecuteReaderAsync();
                        while (await captured.ReadAsync())
                        {
                            var values = new Dictionary<string, object?>();
                            for (var column = 0; column < captured.FieldCount; column++)
                                values[captured.GetName(column)] = captured.IsDBNull(column) ? null : captured.GetValue(column);
                            originalMappings.Add(values);
                        }
                        restoreMapping = true;
                    }
                    using var mapping = await client.PutAsJsonAsync("/api/commerce/v1/accounting/account-mappings",
                        new SetAccountMappingRequest(fixture.TenantId, fixture.BusinessId, "SupplierCreditCard",
                            correctedCardAccount.AccountId, new(2026, 1, 1), null));
                    Assert.True(mapping.IsSuccessStatusCode, await mapping.Content.ReadAsStringAsync());
                }
                var correctedCard = correctedMethod is "DebitCard" or "CreditCard";
                var correction = new WorkSessionPaymentCorrection(verification.VerificationKey, correctedCard ? "Card" : correctedMethod, movement.Amount,
                    "Evidencia verificada en el extracto", correctedMethod, correctedCard ? "Visa" : null,
                    correctedCard ? "APROBACION-CORREGIDA" : null, correctedMethod == "Transfer" ? "REFERENCIA-CORREGIDA" : null);
                using (var wrongAmount = await client.PostAsJsonAsync(closurePath + "/payment-corrections/validate",
                           new[] { correction with { Amount = movement.Amount * 2m } }))
                {
                    Assert.Equal(HttpStatusCode.BadRequest, wrongAmount.StatusCode);
                    Assert.Contains("desde contabilidad", await wrongAmount.Content.ReadAsStringAsync());
                }
                using var evidence = await client.PostAsJsonAsync(closurePath + "/payment-corrections/validate", new[] { correction });
                Assert.True(evidence.IsSuccessStatusCode, await evidence.Content.ReadAsStringAsync());
                corrections = [correction];
            }
            var reconcile = new ReconcileWorkSessionClosureRequest(
                closure.PaymentTotals.Where(item => item.RequiresCount).Select(item =>
                    new ReconcileWorkSessionClosureLine(item.PaymentMethodCode,
                        item.PaymentMethodCode == "Cash" ? closure.ExpectedCash : item.NetAmount +
                            corrections.Where(change => change.PaymentMethodCode == item.PaymentMethodCode).Sum(change => change.Amount) -
                            (item.PaymentMethodCode == closureMethod ? corrections.Sum(change => change.Amount) : 0m), true, null)).ToArray(),
                [], null, page.Items.Where(item => item.PaymentMethodCode != "Cash" ||
                    item.MovementType is "CashIn" or "CashOut" or "ReceivablePayment" or "PayablePayment" or "CreditSale")
                    .Select(item => new WorkSessionPaymentVerificationDecision(item.VerificationKey, "Verified")).ToArray(),
                PaymentCorrections: corrections);
            using (var omittedMessage = new HttpRequestMessage(HttpMethod.Post, closurePath + "/reconcile")
                   { Content = JsonContent.Create(reconcile with { PaymentVerifications = [] }) })
            {
                omittedMessage.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
                using var omitted = await client.SendAsync(omittedMessage);
                Assert.Equal(HttpStatusCode.BadRequest, omitted.StatusCode);
            }
            using var reconcileMessage = new HttpRequestMessage(HttpMethod.Post, closurePath + "/reconcile")
                { Content = JsonContent.Create(reconcile) };
            var reconciliationKey = Guid.NewGuid().ToString("N");
            reconcileMessage.Headers.Add("Idempotency-Key", reconciliationKey);
            using var reconciled = await client.SendAsync(reconcileMessage);
            Assert.True(reconciled.IsSuccessStatusCode, await reconciled.Content.ReadAsStringAsync());
            var completed = (await reconciled.Content.ReadFromJsonAsync<WorkSessionClosureReconciliationView>())!;
            Assert.Equal("Reconciled", completed.Status);
            using var duplicateMessage = new HttpRequestMessage(HttpMethod.Post, closurePath + "/reconcile")
                { Content = JsonContent.Create(reconcile) };
            duplicateMessage.Headers.Add("Idempotency-Key", reconciliationKey);
            using var duplicate = await client.SendAsync(duplicateMessage);
            Assert.True(duplicate.IsSuccessStatusCode, await duplicate.Content.ReadAsStringAsync());
            Assert.True((await duplicate.Content.ReadFromJsonAsync<WorkSessionClosureReconciliationView>())!.IdempotentReplay);
            var persisted = (await client.GetFromJsonAsync<WorkSessionPaymentVerificationPage>(closurePath + "/payment-verifications/page?page=1&pageSize=100"))!;
            var persistedMovement = Assert.Single(persisted.Items, item => item.SourceId == movement.MovementId);
            Assert.Equal("Verified", persistedMovement.Status);
            if (method != "Cash")
            {
                Assert.Equal(movement.Amount, persistedMovement.CorrectedAmount);
                Assert.Equal(corrections[0].Reference, persistedMovement.CorrectedReference);
                Assert.Equal(corrections[0].ApprovalNumber, persistedMovement.CorrectedApprovalNumber);
            }
            if (correctedCardAccount is not null)
            {
                await using var posted = new SqlCommand("""
                    SELECT SUM(CASE WHEN line.AccountId=@Original THEN line.Debit ELSE 0 END),
                      SUM(CASE WHEN line.AccountId=@Card THEN line.Credit ELSE 0 END)
                    FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
                    WHERE entry.SourceDocumentId=@Reconciliation AND entry.SourceDocumentType=N'WorkSessionClosureReconciliation'
                      AND entry.BusinessId=@Business AND entry.TenantId=@Tenant;
                    """, connection);
                posted.Parameters.AddWithValue("@Original", cash.AccountId);
                posted.Parameters.AddWithValue("@Card", correctedCardAccount.AccountId);
                posted.Parameters.AddWithValue("@Reconciliation", completed.ReconciliationId);
                posted.Parameters.AddWithValue("@Business", fixture.BusinessId);
                posted.Parameters.AddWithValue("@Tenant", fixture.TenantId);
                await using var posting = await posted.ExecuteReaderAsync();
                Assert.True(await posting.ReadAsync());
                Assert.Equal(500m, posting.GetDecimal(0));
                Assert.Equal(500m, posting.GetDecimal(1));
            }
        }
        finally
        {
            if (restoreMapping)
            {
                await using var restore = new SqlCommand("""
                    SET XACT_ABORT ON;
                    BEGIN TRANSACTION;
                    DELETE dbo.AccountingAccountMappings
                    WHERE TenantId=@Tenant AND BusinessId=@Business AND Category=N'SupplierCreditCard';
                    INSERT dbo.AccountingAccountMappings(MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo,CreatedAt)
                    SELECT MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo,CreatedAt
                    FROM OPENJSON(@Original) WITH(MappingId uniqueidentifier,TenantId uniqueidentifier,BusinessId uniqueidentifier,
                      Category nvarchar(64),AccountId uniqueidentifier,EffectiveFrom date,EffectiveTo date,CreatedAt datetimeoffset(7));
                    COMMIT TRANSACTION;
                    """, connection);
                restore.Parameters.AddWithValue("@Tenant", fixture.TenantId);
                restore.Parameters.AddWithValue("@Business", fixture.BusinessId);
                restore.Parameters.Add("@Original", System.Data.SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(originalMappings);
                await restore.ExecuteNonQueryAsync();
            }
        }

        async Task<WorkSessionClosureView> CloseSessionAsync(WorkSessionClosurePreviewView preview)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/commerce/v1/work-sessions/{session}/close")
            {
                Content = JsonContent.Create(new CloseWorkSessionRequest(preview.ExpectedCash, null,
                    PaymentCounts: preview.PaymentTotals.Where(item => item.RequiresCount)
                        .Select(item => new WorkSessionPaymentCount(item.PaymentMethodCode,
                            item.PaymentMethodCode == "Cash" ? preview.ExpectedCash : Math.Max(0m, item.NetAmount))).ToArray()))
            };
            message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var response = await client.SendAsync(message);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<WorkSessionClosureView>())!;
        }
    }

    private async Task<AccountingAccountView> CreateTreasuryAuxiliaryAsync(HttpClient client, string method, bool outgoing)
    {
        if (method == "Cash") return await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var options = new SqlCommand("""
            SELECT OptionId FROM reference.Options WHERE CatalogCode=N'bank-account-type' AND Code=N'Checking';
            SELECT OptionId FROM reference.Options WHERE CatalogCode=N'payment-method' AND Code=@Method;
            """, connection);
        options.Parameters.AddWithValue("@Method", method);
        await using var reader = await options.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync()); var bankType = reader.GetGuid(0);
        await reader.NextResultAsync(); Assert.True(await reader.ReadAsync()); var methodId = reader.GetGuid(0);
        await reader.DisposeAsync();
        var bankId = Guid.NewGuid();
        using var bank = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bankId}",
            new SaveBankAccountRequest(bankId, null, bankType, "Banco tesorería directa", bankId.ToString("N"),
                "Banco para comprobar cierre", false, true, null));
        Assert.True(bank.IsSuccessStatusCode, await bank.Content.ReadAsStringAsync());
        var function = method == "Transfer" ? "Bank" : outgoing ? "CreditCardLiability" : "CardClearing";
        var liability = function == "CreditCardLiability";
        return await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            liability ? "2394" : "1394", liability ? "Liability" : "Asset", function,
            liability ? "Credit" : "Debit", bankId, methodId);
    }
}
