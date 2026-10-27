using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Opening_reconciliation_preserves_the_existing_position_and_its_payments(bool receivable)
    {
        var originalSettings = await CaptureOpeningTestSettingsAsync();
        try
        {
            await DisableAccountingAsync();
            var kind = receivable ? "Receivable" : "Payable";
            var effectiveOn = new DateOnly(2026, 11, receivable ? 2 : 1);
            using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
                AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
            var accounts = await client.GetFromJsonAsync<AccountingAccountView[]>("/api/commerce/v1/accounting/accounts")
                ?? throw new InvalidOperationException("The fixture requires its accounting catalog.");
            var accountId = await CreateHistoricalAccountAsync(client, receivable);
            var equity = accounts!.First(account => account.AccountType == "Equity" && account.AllowsPosting && account.IsActive);
            var payableId = Guid.NewGuid();
            var sourceId = Guid.NewGuid();
            var batchId = Guid.NewGuid();
            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var arrange = new SqlCommand(HistoricalPositionSql("""
                    INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,
                      SourceDocumentId,SourceDocumentType,SourcePayloadHash,OccurredAt,AccountingEntryRequired,
                      Status,AttemptCount,CreatedAt)
                    VALUES(NEWID(),@TenantId,@BusinessId,@SourceId,N'Expense',CONVERT(binary(32),0),
                      '2025-10-01',0,N'CommercialEffectsApplied',1,'2025-10-01');
                    INSERT dbo.Payables(PayableId,BusinessId,SupplierId,SourceDocumentId,SourceDocumentType,
                      DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
                    VALUES(@PayableId,@BusinessId,@SupplierId,@SourceId,N'Expense',N'APERTURA-HISTORICA',
                      N'COP',2000,1500,'2025-10-31',N'PartiallyPaid','2025-10-01');
                    INSERT dbo.PayableTransactions(PayableTransactionId,PayableId,TransactionType,Amount,
                      SourceDocumentId,OccurredAt,CreatedAt)
                    VALUES(NEWID(),@PayableId,N'Opening',2000,@SourceId,'2025-10-01','2025-10-01'),
                      (NEWID(),@PayableId,N'Payment',-500,NEWID(),'2025-10-02','2025-10-02');
                    """, receivable), connection);
                arrange.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                arrange.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                arrange.Parameters.AddWithValue("@SupplierId", receivable ? fixture.SupplierPartyId : fixture.SupplierId);
                arrange.Parameters.AddWithValue("@SourceId", sourceId);
                arrange.Parameters.AddWithValue("@PayableId", payableId);
                await arrange.ExecuteNonQueryAsync();
            }
            using var response = await client.PutAsJsonAsync("/api/commerce/v1/accounting/opening-balances", new
            {
                batchId, businessId = fixture.BusinessId, effectiveOn = effectiveOn.ToString("yyyy-MM-dd"), currencyCode = "COP",
                description = "Conciliación de deuda antigua", rowVersion = (string?)null,
                lines = new object[]
                {
                    new { accountId, partyId = fixture.SupplierPartyId,
                        description = "Deuda después del abono histórico", debit = receivable ? 1500m : 0m, credit = receivable ? 0m : 1500m,
                        existingPosition = new { subledgerKind = kind, subledgerId = payableId } },
                    new { accountId = equity.AccountId, description = "Contrapartida de apertura", debit = receivable ? 0m : 1500m, credit = receivable ? 1500m : 0m }
                }
            });
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var firstLine = document.RootElement.GetProperty("lines")[0];
            Assert.True(firstLine.TryGetProperty("existingPosition", out var position),
                "The selected historical obligation must not be silently discarded.");
            Assert.Equal(payableId, position.GetProperty("subledgerId").GetGuid());
            Assert.Equal(kind, position.GetProperty("subledgerKind").GetString());
            var sourceLineId = firstLine.GetProperty("lineId").GetGuid();
            Assert.NotEqual(Guid.Empty, sourceLineId);
            var saved = (await response.Content.ReadFromJsonAsync<AccountingOpeningBalanceView>())!;
            var captured = new SaveAccountingOpeningBalanceRequest(batchId, fixture.BusinessId,
                effectiveOn, "COP", saved.Description, saved.RowVersion,
                saved.Lines.Select(line => new AccountingOpeningBalanceLineRequest(line.AccountId, line.PartyId,
                    line.CostCenterId, line.Description, line.Debit, line.Credit, line.LineId, line.ExistingPosition)).ToArray());
            using (var scope = fixture.CreateScope())
            using (var counter = new InventoryBalanceProcessingTests.CommandCounter(
                new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog))
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var persisted = await scope.ServiceProvider.GetRequiredService<AccountingService>().SaveOpeningBalanceAsync(
                    new(fixture.UserId, fixture.TenantId, fixture.BusinessId,
                        new HashSet<string> { AccountingPermissionCodes.Configure }), captured);
                watch.Stop();
                output.WriteLine("Reconciled {0} opening: {1} commands, {2:F1} ms", kind, counter.Count, watch.Elapsed.TotalMilliseconds);
                Assert.Equal(3, counter.Count);
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Reconciled opening took {watch.Elapsed}.");
                captured = captured with { RowVersion = persisted.RowVersion };
            }
            var invalidCaptures = new[]
            {
                captured with { Lines = [captured.Lines[0] with { AccountId = accounts.First(account =>
                    account.AccountType == (receivable ? "Asset" : "Liability") && account.AllowsPosting &&
                    account.RequiresParty && account.Semantics is null).AccountId }, captured.Lines[1]] },
                captured with { Lines = [captured.Lines[0] with { Debit = receivable ? 1501m : 0m, Credit = receivable ? 0m : 1501m }, captured.Lines[1] with { Debit = receivable ? 0m : 1501m, Credit = receivable ? 1501m : 0m }] },
                captured with { Lines = [captured.Lines[0] with { AccountId = equity.AccountId }, captured.Lines[1]] },
                captured with { Lines = [captured.Lines[0] with { ExistingPosition = new(receivable ? "Payable" : "Receivable", payableId) }, captured.Lines[1]] },
                captured with { Lines = [captured.Lines[0], captured.Lines[0] with { LineId = Guid.NewGuid() }, captured.Lines[1]] }
            };
            foreach (var invalid in invalidCaptures)
            {
                using var rejected = await client.PutAsJsonAsync("/api/commerce/v1/accounting/opening-balances", invalid);
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                Assert.Contains("detail", await rejected.Content.ReadAsStringAsync());
            }
            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                foreach (var (makeInvalid, restore) in new[]
                {
                    ("UPDATE dbo.Payables SET OriginalAmount=2100 WHERE PayableId=@Id",
                     "UPDATE dbo.Payables SET OriginalAmount=2000 WHERE PayableId=@Id"),
                    ("UPDATE dbo.PayableTransactions SET Amount=-400 WHERE PayableId=@Id AND TransactionType=N'Payment'",
                     "UPDATE dbo.PayableTransactions SET Amount=-500 WHERE PayableId=@Id AND TransactionType=N'Payment'"),
                    ("UPDATE dbo.PayableTransactions SET OccurredAt='2026-11-03' WHERE PayableId=@Id AND TransactionType=N'Payment'",
                     "UPDATE dbo.PayableTransactions SET OccurredAt='2025-10-02' WHERE PayableId=@Id AND TransactionType=N'Payment'")
                })
                {
                    await using var change = new SqlCommand(HistoricalPositionSql(makeInvalid, receivable), connection);
                    change.Parameters.AddWithValue("@Id", payableId);
                    Assert.Equal(1, await change.ExecuteNonQueryAsync());
                    try
                    {
                        using var rejected = await client.PutAsJsonAsync("/api/commerce/v1/accounting/opening-balances", captured);
                        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                    }
                    finally
                    {
                        change.CommandText = HistoricalPositionSql(restore, receivable);
                        Assert.Equal(1, await change.ExecuteNonQueryAsync());
                    }
                }
                var pendingId = Guid.NewGuid();
                await using var pending = new SqlCommand("""
                    INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,
                      PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
                    VALUES(@PendingId,N'AccountAdjustment',@TenantId,@BusinessId,@Payload,CONVERT(binary(32),0),
                      '2025-10-03','2025-10-03',1);
                    """, connection);
                pending.Parameters.AddWithValue("@PendingId", pendingId);
                pending.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                pending.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                pending.Parameters.AddWithValue("@Payload", JsonSerializer.Serialize(new
                    { SubledgerId = payableId, SubledgerKind = kind, Direction = "Increase", Amount = 100m }));
                Assert.Equal(1, await pending.ExecuteNonQueryAsync());
                try
                {
                    using var rejected = await client.PutAsJsonAsync("/api/commerce/v1/accounting/opening-balances", captured);
                    Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                }
                finally
                {
                    pending.CommandText = "DELETE dbo.AccountingSourceDocuments WHERE SourceDocumentId=@PendingId";
                    Assert.Equal(1, await pending.ExecuteNonQueryAsync());
                }
            }
            // Approval must revalidate the live position, not trust the earlier draft.
            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var change = new SqlCommand(HistoricalPositionSql("UPDATE dbo.Payables SET OutstandingAmount=1400 WHERE PayableId=@Id", receivable), connection);
                change.Parameters.AddWithValue("@Id", payableId);
                Assert.Equal(1, await change.ExecuteNonQueryAsync());
                try
                {
                    using var staleApproval = await client.PostAsJsonAsync(
                        $"/api/commerce/v1/accounting/opening-balances/{batchId:D}/approve", new { });
                    Assert.Equal(HttpStatusCode.BadRequest, staleApproval.StatusCode);
                }
                finally
                {
                    change.CommandText = HistoricalPositionSql("UPDATE dbo.Payables SET OutstandingAmount=1500 WHERE PayableId=@Id", receivable);
                    Assert.Equal(1, await change.ExecuteNonQueryAsync());
                }
            }
            using (var approve = await client.PostAsJsonAsync(
                $"/api/commerce/v1/accounting/opening-balances/{batchId:D}/approve", new { }))
                Assert.True(approve.IsSuccessStatusCode, await approve.Content.ReadAsStringAsync());
            var otherBusinesses = await DisableOtherTestBusinessesAsync();
            try
            {
                using var scope = fixture.CreateScope();
                var accepted = await scope.ServiceProvider.GetRequiredService<IAccountingStore>().ActivateAsync(
                    new(fixture.UserId, fixture.TenantId, fixture.BusinessId,
                        new HashSet<string> { AccountingPermissionCodes.Activate }),
                    new(effectiveOn, "COP", "ImportedAndApproved"), default);
                Assert.Equal(AccountingActivationStatuses.Configuring, accepted.Status);
                await using (var connection = new SqlConnection(fixture.ConnectionString))
                {
                    await connection.OpenAsync();
                    await using var change = new SqlCommand("""
                        UPDATE dbo.AccountingPostingJobs SET AccountingEntryRequired=1 WHERE SourceDocumentId=@Id;
                        """, connection);
                    change.Parameters.AddWithValue("@Id", sourceId);
                    Assert.Equal(1, await change.ExecuteNonQueryAsync());
                    try
                    {
                        await scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>().ProcessAsync(
                            batchId, AccountingManualDocumentTypes.OpeningBalance, fixture.BusinessId, default);
                        Assert.Equal(0, await CountAsync("AccountingEntries", "SourceDocumentId", batchId));
                        Assert.Equal("OpeningReconciliationRequired", await ScalarAsync<string>(
                            "SELECT LastErrorCode FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", batchId));
                        Assert.Equal("AccountingPendingConfiguration", await ScalarAsync<string>(
                            "SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", batchId));
                        Assert.Equal(0, await ScalarAsync<int>(HistoricalPositionSql(
                            "SELECT COUNT(*) FROM dbo.Payables WHERE PayableId=@Id AND OpeningEntryId IS NOT NULL", receivable), payableId));
                    }
                    finally
                    {
                        change.CommandText = "UPDATE dbo.AccountingPostingJobs SET AccountingEntryRequired=0 WHERE SourceDocumentId=@Id";
                        Assert.Equal(1, await change.ExecuteNonQueryAsync());
                    }
                }
                using var activate = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
                    new ActivateAccountingRequest(effectiveOn, "COP", "ImportedAndApproved"));
                Assert.True(activate.IsSuccessStatusCode, await activate.Content.ReadAsStringAsync());
                Assert.Equal(AccountingActivationStatuses.Ready,
                    (await activate.Content.ReadFromJsonAsync<AccountingReadinessView>())!.Status);
                using var replay = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
                    new ActivateAccountingRequest(effectiveOn, "COP", "ImportedAndApproved"));
                Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
            }
            finally { await RestoreTestBusinessesAsync(otherBusinesses); }

            await using var verification = new SqlConnection(fixture.ConnectionString);
            await verification.OpenAsync();
            await using var verify = new SqlCommand(HistoricalPositionSql("""
                SELECT balance.OriginalAmount,balance.OutstandingAmount,balance.Status,
                  entry.SourceDocumentId,line.AccountId,line.PartyId,line.Debit,line.Credit,line.SourceLineId,
                  (SELECT COUNT(*) FROM dbo.PayableTransactions WHERE PayableId=@PayableId),
                  (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@BatchId),
                  (SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@SourceId)
                FROM dbo.Payables balance JOIN dbo.AccountingEntries entry ON entry.EntryId=balance.OpeningEntryId
                JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId AND line.LineNumber=balance.OpeningEntryLineNumber
                WHERE balance.PayableId=@PayableId;
                """, receivable), verification);
            verify.Parameters.AddWithValue("@PayableId", payableId);
            verify.Parameters.AddWithValue("@BatchId", batchId);
            verify.Parameters.AddWithValue("@SourceId", sourceId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(2000m, reader.GetDecimal(0));
            Assert.Equal(1500m, reader.GetDecimal(1));
            Assert.Equal("PartiallyPaid", reader.GetString(2));
            Assert.Equal(batchId, reader.GetGuid(3));
            Assert.Equal(accountId, reader.GetGuid(4));
            Assert.Equal(fixture.SupplierPartyId, reader.GetGuid(5));
            Assert.Equal(receivable ? 1500m : 0m, reader.GetDecimal(6));
            Assert.Equal(receivable ? 0m : 1500m, reader.GetDecimal(7));
            Assert.Equal(sourceLineId, reader.GetGuid(8));
            Assert.Equal(2, reader.GetInt32(9));
            Assert.Equal(1, reader.GetInt32(10));
            Assert.Equal(1, reader.GetInt32(11));
            Assert.False(await reader.ReadAsync());
            await PortfolioOriginalAccountScenario.AssertAdjustmentsAsync(fixture, client, receivable, payableId,
                new DateTimeOffset(effectiveOn.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(-5)));
            Assert.Equal(1500m, await ScalarAsync<decimal>(HistoricalPositionSql(
                "SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", receivable), payableId));
        }
        finally { await RestoreOpeningTestSettingsAsync(originalSettings); }
    }
    [Theory]
    [InlineData(2, 1)]
    [InlineData(50, 2)]
    [InlineData(500, 3)]
    public async Task Reconciled_opening_capture_keeps_three_commands_for_the_whole_batch(int count, int day)
    {
        var originalSettings = await CaptureOpeningTestSettingsAsync();
        try
        {
            await DisableAccountingAsync();
            using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure);
            var accountId = await CreateHistoricalAccountAsync(client, false);
            var accounts = await client.GetFromJsonAsync<AccountingAccountView[]>("/api/commerce/v1/accounting/accounts");
            var equity = accounts!.First(account => account.AccountType == "Equity" && account.AllowsPosting && account.IsActive);
            var positions = Enumerable.Range(1, count - 1).Select(_ => new
                { Id = Guid.NewGuid(), SourceId = Guid.NewGuid() }).ToArray();
            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var arrange = new SqlCommand("""
                    DECLARE @Positions TABLE(Id uniqueidentifier,SourceId uniqueidentifier);
                    INSERT @Positions SELECT Id,SourceId FROM OPENJSON(@Json) WITH(Id uniqueidentifier,SourceId uniqueidentifier);
                    INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
                      SourceDocumentType,SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt)
                    SELECT NEWID(),@TenantId,@BusinessId,SourceId,N'Expense',CONVERT(binary(32),0),
                      '2025-10-01',0,N'CommercialEffectsApplied',1,'2025-10-01' FROM @Positions;
                    INSERT dbo.Payables(PayableId,BusinessId,SupplierId,SourceDocumentId,SourceDocumentType,
                      DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
                    SELECT Id,@BusinessId,@SupplierId,SourceId,N'Expense',CONVERT(nvarchar(36),Id),
                      N'COP',1,1,'2025-10-31',N'Open','2025-10-01' FROM @Positions;
                    INSERT dbo.PayableTransactions(PayableTransactionId,PayableId,TransactionType,Amount,
                      SourceDocumentId,OccurredAt,CreatedAt)
                    SELECT NEWID(),Id,N'Opening',1,SourceId,'2025-10-01','2025-10-01' FROM @Positions;
                    """, connection);
                arrange.Parameters.AddWithValue("@Json", JsonSerializer.Serialize(positions));
                arrange.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                arrange.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                arrange.Parameters.AddWithValue("@SupplierId", fixture.SupplierId);
                await arrange.ExecuteNonQueryAsync();
            }
            var lines = positions.Select(position => new AccountingOpeningBalanceLineRequest(accountId,
                fixture.SupplierPartyId, null, "Obligación anterior", 0, 1,
                ExistingPosition: new("Payable", position.Id))).ToList();
            lines.Add(new(equity.AccountId, null, null, "Contrapartida", positions.Length, 0));
            using var scope = fixture.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
            AccountingOpeningBalanceView saved;
            using (var counter = new InventoryBalanceProcessingTests.CommandCounter(
                new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog))
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                saved = await service.SaveOpeningBalanceAsync(new(fixture.UserId, fixture.TenantId, fixture.BusinessId,
                        new HashSet<string> { AccountingPermissionCodes.Configure }),
                    new(Guid.NewGuid(), fixture.BusinessId, new(2026, 12, day), "COP", "Apertura conciliada por conjunto", null, lines));
                watch.Stop();
                output.WriteLine("Reconciled opening {0} lines: {1} commands, {2:F1} ms", count, counter.Count, watch.Elapsed.TotalMilliseconds);
                Assert.Equal(3, counter.Count);
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Reconciled {count}-line opening took {watch.Elapsed}.");
            }
            Assert.Equal(count, saved.Lines.Count);
            Assert.Equal(positions.Select(position => position.Id), saved.Lines.Take(count - 1).Select(line => line.ExistingPosition!.SubledgerId));
            Assert.All(saved.Lines.Take(count - 1), line => Assert.False(string.IsNullOrWhiteSpace(line.ExistingPositionDocumentNumber)));
            Assert.Equal(0, await CountAsync("AccountingEntries", "SourceDocumentId", saved.BatchId));
        }
        finally { await RestoreOpeningTestSettingsAsync(originalSettings); }
    }

    [Theory]
    [InlineData("Payable")]
    [InlineData("Receivable")]
    public async Task Obligation_availability_preserves_all_four_decimal_places(string kind)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT PendingPaymentAmount,PendingAdjustmentAmount,AvailableAmount
            FROM accounting.ObligationAvailability(@BusinessId,@Kind,@Id,@Amount,NULL);
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@Kind", kind);
        command.Parameters.AddWithValue("@Id", Guid.NewGuid());
        var amount = command.Parameters.Add("@Amount", System.Data.SqlDbType.Decimal);
        amount.Precision = 19;
        amount.Scale = 4;
        foreach (var outstanding in new[] { 0.0001m, 1000.5001m })
        {
            amount.Value = outstanding;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(0m, reader.GetDecimal(0));
            Assert.Equal(0m, reader.GetDecimal(1));
            Assert.Equal(outstanding, reader.GetDecimal(2));
        }
    }

    private Task<string> CaptureOpeningTestSettingsAsync() => ScalarAsync<string>("""
        SELECT Status,FunctionalCurrencyCode,EffectiveFrom,OpeningBalanceMode,ActivationRequestedAt,
          ActivationRequestedByUserId,ActivatedAt,ActivatedByUserId,UpdatedAt
        FROM dbo.AccountingTenantSettings WHERE TenantId=@Id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
        """, fixture.TenantId);

    private async Task RestoreOpeningTestSettingsAsync(string snapshot)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var restore = new SqlCommand("""
            UPDATE settings SET Status=original.Status,FunctionalCurrencyCode=original.FunctionalCurrencyCode,
              EffectiveFrom=original.EffectiveFrom,OpeningBalanceMode=original.OpeningBalanceMode,
              ActivationRequestedAt=original.ActivationRequestedAt,ActivationRequestedByUserId=original.ActivationRequestedByUserId,
              ActivatedAt=original.ActivatedAt,ActivatedByUserId=original.ActivatedByUserId,UpdatedAt=original.UpdatedAt
            FROM dbo.AccountingTenantSettings settings CROSS APPLY OPENJSON(@Snapshot) WITH(
              Status nvarchar(16),FunctionalCurrencyCode char(3),EffectiveFrom date,OpeningBalanceMode nvarchar(24),
              ActivationRequestedAt datetimeoffset(7),ActivationRequestedByUserId uniqueidentifier,
              ActivatedAt datetimeoffset(7),ActivatedByUserId uniqueidentifier,UpdatedAt datetimeoffset(7)) original
            WHERE settings.TenantId=@TenantId;
            """, connection);
        restore.Parameters.AddWithValue("@Snapshot", snapshot);
        restore.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        Assert.Equal(1, await restore.ExecuteNonQueryAsync());
    }

    private async Task<Guid> CreateHistoricalAccountAsync(HttpClient client, bool receivable)
    {
        var kind = receivable ? "Receivable" : "Payable";
        var accountType = receivable ? "Asset" : "Liability";
        var side = receivable ? "Debit" : "Credit";
        var accountId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var groupCode = (receivable ? "1" : "2") + (BitConverter.ToUInt32(groupId.ToByteArray()) % 100000).ToString("D5",
            System.Globalization.CultureInfo.InvariantCulture);
        using var createGroup = await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
            new CreateAccountingAccountRequest(groupId, fixture.TenantId, groupCode,
                "Cartera histórica", accountType, false, false));
        Assert.True(createGroup.StatusCode == HttpStatusCode.Created, await createGroup.Content.ReadAsStringAsync());
        using var createAccount = await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
            new CreateAccountingAccountRequest(accountId, fixture.TenantId,
                groupCode + "01",
                "Obligación conciliada", accountType, true, true,
                new(groupId, kind, side, fixture.BusinessId, null, null)));
        Assert.True(createAccount.StatusCode == HttpStatusCode.Created, await createAccount.Content.ReadAsStringAsync());
        return accountId;
    }

    // The fixture exercises the same portfolio contract for supplier CxP and
    // generic-party CxC; these are test-owned identifiers, never request input.
    private static string HistoricalPositionSql(string sql, bool receivable) => receivable
        ? sql.Replace("Payable", "Receivable", StringComparison.Ordinal)
            .Replace("@ReceivableId", "@PayableId", StringComparison.Ordinal)
            .Replace("SupplierId,SourceDocumentId", "PartyId,SourceDocumentId", StringComparison.Ordinal)
        : sql;
}
