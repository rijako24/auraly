using System.Data;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Commerce.Payroll.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class AccountingSourceAcceptanceTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Acceptance_does_not_request_an_update_lock_on_a_neighboring_sources_job()
    {
        var prefix = Guid.NewGuid().ToString("N")[..20];
        var at = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var original = new SqlAccountingPostingJobWriter.Source(fixture.TenantId, fixture.BusinessId,
            Guid.ParseExact(prefix + "000000000002", "N"), "AcceptanceContractTest", "{}", at, Guid.NewGuid());
        var next = original with { DocumentId = Guid.ParseExact(prefix + "000000000001", "N"), JobId = Guid.NewGuid() };
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var setup = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            Assert.Equal(1, await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, setup, [original], at,
                AccountingJobRequirement.PreserveCommercialEffects, default));
            await setup.CommitAsync();
        }
        try
        {
            await using var worker = new SqlConnection(fixture.ConnectionString);
            await worker.OpenAsync();
            await using var held = (SqlTransaction)await worker.BeginTransactionAsync(IsolationLevel.Serializable);
            // SQL Guid ordering compares the final six bytes first. The next
            // absent key precedes this existing job in the source index, where
            // an update-range read used to conflict with the processor's lock.
            await using (var job = new SqlCommand("""
                SELECT AccountingPostingJobId FROM dbo.AccountingPostingJobs WITH(UPDLOCK,HOLDLOCK,INDEX(UQ_AccountingPostingJobs_Source))
                WHERE SourceDocumentId=@Id AND SourceDocumentType=@Type;
                """, worker, held))
            {
                job.Parameters.AddWithValue("@Id", original.DocumentId);
                job.Parameters.AddWithValue("@Type", original.DocumentType);
                Assert.Equal(original.JobId, await job.ExecuteScalarAsync());
            }
            await using (var deadline = new SqlCommand("SET LOCK_TIMEOUT 1000;", connection))
                await deadline.ExecuteNonQueryAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            using (var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
            {
                Assert.Equal(1, await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction, [next], at,
                    AccountingJobRequirement.PreserveCommercialEffects, default));
                Assert.Equal(1, counter.Count);
            }
            await transaction.RollbackAsync();
            await held.RollbackAsync();
        }
        finally
        {
            await using var cleanup = new SqlCommand("""
                SET LOCK_TIMEOUT -1;
                UPDATE dbo.AccountingSourceDocuments SET AccountingPostingJobId=NULL
                  WHERE SourceDocumentId=@Id AND SourceDocumentType=@Type;
                DELETE dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id AND SourceDocumentType=@Type;
                DELETE dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=@Type;
                """, connection);
            cleanup.Parameters.AddWithValue("@Id", original.DocumentId);
            cleanup.Parameters.AddWithValue("@Type", original.DocumentType);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Activation_replay_returns_the_stored_result_without_rechecking_the_whole_configuration()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        DateOnly effectiveFrom;
        string currency, openingMode;
        DateTimeOffset activatedAt, updatedAt;
        await using (var read = new SqlCommand("""
            SELECT EffectiveFrom,FunctionalCurrencyCode,OpeningBalanceMode,ActivatedAt,UpdatedAt
            FROM dbo.AccountingTenantSettings WHERE TenantId=@TenantId AND Status=N'Ready';
            """, connection))
        {
            read.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            await using var reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            effectiveFrom = DateOnly.FromDateTime(reader.GetDateTime(0));
            currency = reader.GetString(1);
            openingMode = reader.GetString(2);
            activatedAt = reader.GetDateTimeOffset(3);
            updatedAt = reader.GetDateTimeOffset(4);
        }
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Activate });
        var request = new ActivateAccountingRequest(effectiveFrom, currency, openingMode);
        using (var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
        {
            var replay = await service.ActivateAsync(user, request);
            Assert.Equal(activatedAt, replay.ActivatedAt);
            Assert.Equal(effectiveFrom, replay.EffectiveFrom);
            Assert.Equal(currency, replay.FunctionalCurrencyCode);
            Assert.Equal(openingMode, replay.OpeningBalanceMode);
            Assert.Equal(2, counter.Count);
        }
        // A ready activation is not an edit endpoint: an otherwise valid new
        // date must never be reported as saved when the settings did not change.
        var repeated = await service.ActivateAsync(user, request with
        {
            EffectiveFrom = effectiveFrom.AddDays(1), OpeningBalanceMode = "ZeroDeclared"
        });
        Assert.Equal(effectiveFrom, repeated.EffectiveFrom);
        Assert.Equal(activatedAt, repeated.ActivatedAt);
        await using var verify = new SqlCommand("""
            SELECT EffectiveFrom,ActivatedAt,UpdatedAt FROM dbo.AccountingTenantSettings WHERE TenantId=@TenantId;
            """, connection);
        verify.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        await using var saved = await verify.ExecuteReaderAsync();
        Assert.True(await saved.ReadAsync());
        Assert.Equal(effectiveFrom, DateOnly.FromDateTime(saved.GetDateTime(0)));
        Assert.Equal(activatedAt, saved.GetDateTimeOffset(1));
        Assert.Equal(updatedAt, saved.GetDateTimeOffset(2));
    }

    [Theory]
    [InlineData("PayablePayment", false)]
    [InlineData("PayablePayment", true)]
    [InlineData("ReceivablePayment", false)]
    [InlineData("ReceivablePayment", true)]
    public async Task Native_payment_numbering_preserves_missing_and_exhausted_series_rejections(
        string documentType, bool exhausted)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        await using (var provision = new SqlCommand("dbo.BusinessDocumentSeriesDefaultsProvision", connection, transaction)
            { CommandType = CommandType.StoredProcedure })
        {
            provision.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            provision.Parameters.AddWithValue("@Now", now);
            await provision.ExecuteNonQueryAsync();
        }
        int seriesCount;
        long? next;
        Guid seriesId;
        await using (var setup = new SqlCommand("""
            DECLARE @SeriesId uniqueidentifier,@End bigint;
            SELECT TOP(1) @SeriesId=DocumentSeriesId,@End=RangeEnd FROM dbo.DocumentSeries
            WHERE BusinessId=@BusinessId AND DocumentType=@Type AND DeviceId IS NULL AND IsActive=1
            ORDER BY DocumentSeriesId;
            IF @Exhausted=1
            BEGIN
              UPDATE dbo.DocumentSeriesCursors SET NextConsecutive=@End+1 WHERE DocumentSeriesId=@SeriesId;
              IF @@ROWCOUNT=0 INSERT dbo.DocumentSeriesCursors(DocumentSeriesId,NextConsecutive,UpdatedAt)
                VALUES(@SeriesId,@End+1,@Now);
            END
            ELSE UPDATE dbo.DocumentSeries SET IsActive=0
              WHERE BusinessId=@BusinessId AND DocumentType=@Type AND DeviceId IS NULL;
            SELECT (SELECT COUNT(*) FROM dbo.DocumentSeries WHERE BusinessId=@BusinessId AND DocumentType=@Type),
              @SeriesId,(SELECT NextConsecutive FROM dbo.DocumentSeriesCursors WHERE DocumentSeriesId=@SeriesId);
            """, connection, transaction))
        {
            setup.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            setup.Parameters.AddWithValue("@Type", documentType);
            setup.Parameters.AddWithValue("@Exhausted", exhausted);
            setup.Parameters.AddWithValue("@Now", now);
            await using var reader = await setup.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            seriesCount = reader.GetInt32(0);
            seriesId = reader.GetGuid(1);
            next = reader.IsDBNull(2) ? null : reader.GetInt64(2);
        }
        var error = await Assert.ThrowsAsync<SqlException>(() =>
            Auraly.Infrastructure.Persistence.SqlOperationalDocumentAllocator.AllocateExistingNumberAsync(
                connection, transaction, fixture.TenantId, fixture.BusinessId, documentType, now, default));
        Assert.Equal(exhausted ? 51731 : 51730, error.Number);
        await using (var state = new SqlCommand("""
            SELECT (SELECT COUNT(*) FROM dbo.DocumentSeries WHERE BusinessId=@BusinessId AND DocumentType=@Type),
              (SELECT NextConsecutive FROM dbo.DocumentSeriesCursors WHERE DocumentSeriesId=@SeriesId);
            """, connection, transaction))
        {
            state.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            state.Parameters.AddWithValue("@Type", documentType);
            state.Parameters.AddWithValue("@SeriesId", seriesId);
            await using var reader = await state.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(seriesCount, reader.GetInt32(0));
            Assert.Equal(next, reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1));
        }
        await transaction.RollbackAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Account_references_cover_manual_and_native_snapshot_names_without_including_party_or_bank_ids(bool webSnapshot)
    {
        var counterpart = Guid.NewGuid();
        var expense = Guid.NewGuid();
        var manualLine = Guid.NewGuid();
        var accountingLine = Guid.NewGuid();
        var expenseLine = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            CounterpartAccountId = counterpart,
            ExpenseAccountId = expense,
            PartyId = Guid.NewGuid(),
            BankAccountId = Guid.NewGuid(),
            Lines = new[] { new { AccountId = manualLine, AccountingAccountId = accountingLine,
                ExpenseAccountId = expenseLine, PartyId = Guid.NewGuid(), BankAccountId = Guid.NewGuid() },
                new { AccountId = manualLine, AccountingAccountId = accountingLine,
                    ExpenseAccountId = expenseLine, PartyId = Guid.NewGuid(), BankAccountId = Guid.NewGuid() } }
        }, webSnapshot ? new JsonSerializerOptions(JsonSerializerDefaults.Web) : new JsonSerializerOptions());
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT AccountId FROM accounting.AccountingAccountReferences(@Payload)", connection);
        command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = payload;
        var actual = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) actual.Add(reader.GetGuid(0));
        Assert.Equal(new[] { counterpart, expense, manualLine, accountingLine, expenseLine }.Order(), actual.Order());
    }

    [Theory]
    [InlineData("HeaderParty")]
    [InlineData("LineParty")]
    [InlineData("Account")]
    public async Task Rejected_manual_acceptance_rolls_back_source_job_and_party_index(string invalidDimension)
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var accounts = await service.ListAccountsAsync(user);
        var id = Guid.NewGuid();
        var request = new ConfirmManualAccountingVoucherRequest(id, fixture.BusinessId,
            new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Validación atómica", [
                new(invalidDimension == "Account" ? Guid.NewGuid() : accounts.First(account => account.Code == "519595").AccountId,
                    invalidDimension == "LineParty" ? Guid.NewGuid() : fixture.SupplierPartyId, null, "Gasto", 100, 0),
                new(accounts.First(account => account.Code == "111005").AccountId, null, null, "Banco", 0, 100)],
            invalidDimension == "HeaderParty" ? Guid.NewGuid() : fixture.SupplierPartyId);
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        using var response = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var verify = new SqlCommand("""
            SELECT (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id),
              (SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id),
              (SELECT COUNT(*) FROM accounting.SourcePartyIndex WHERE SourceDocumentId=@Id),
              (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id);
            """, connection);
        verify.Parameters.AddWithValue("@Id", id);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        for (var column = 0; column < 4; column++) Assert.Equal(0, reader.GetInt32(column));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_replay_preserves_legacy_payloads_and_uses_canonical_job_integrity(bool corruptJob)
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend });
        var accounts = await service.ListAccountsAsync(user);
        var at = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var id = Guid.NewGuid();
        var request = new ConfirmManualAccountingVoucherRequest(id, fixture.BusinessId, at,
            "MANUAL_VOUCHER", "Comprobante anterior al tercero principal", [
                new(accounts.First(account => account.Code == "519595").AccountId, fixture.SupplierPartyId, null, "Gasto", 100, 0),
                new(accounts.First(account => account.Code == "111005").AccountId, null, null, "Banco", 0, 100)]);
        var legacyJson = System.Text.Json.JsonSerializer.Serialize(new {
            request.VoucherId, request.BusinessId, request.OccurredAt, request.ConceptCode, request.Description, request.Lines });
        Assert.DoesNotContain("MainPartyId", legacyJson, StringComparison.Ordinal);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        try
        {
            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction,
                    [new(fixture.TenantId, fixture.BusinessId, id, AccountingManualDocumentTypes.ManualVoucher,
                        legacyJson, at, Guid.NewGuid())], at, AccountingJobRequirement.AccountingEntryOnly, default);
                if (corruptJob)
                {
                    await using var corrupt = new SqlCommand("UPDATE dbo.AccountingPostingJobs SET SourcePayloadHash=HASHBYTES('SHA2_256',N'altered') WHERE SourceDocumentId=@Id", connection, transaction);
                    corrupt.Parameters.AddWithValue("@Id", id);
                    await corrupt.ExecuteNonQueryAsync();
                }
                await transaction.CommitAsync();
            }
            if (corruptJob)
                await Assert.ThrowsAsync<AccountingConflictException>(() => service.ConfirmManualVoucherAsync(user, request));
            else
            {
                var replay = await service.ConfirmManualVoucherAsync(user, request);
                Assert.True(replay.IsDuplicate);
                Assert.Equal("Pending", replay.Status);
                await Assert.ThrowsAsync<AccountingConflictException>(() => service.ConfirmManualVoucherAsync(user,
                    request with { Description = "Contenido cambiado" }));
            }
            await using var verify = new SqlCommand("""
                SELECT source.PayloadJson,(SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id),
                  CONVERT(bit,CASE WHEN job.SourcePayloadHash=source.PayloadHash THEN 1 ELSE 0 END)
                FROM dbo.AccountingSourceDocuments source JOIN dbo.AccountingPostingJobs job
                  ON job.SourceDocumentId=source.SourceDocumentId AND job.SourceDocumentType=source.SourceDocumentType
                WHERE source.SourceDocumentId=@Id;
                """, connection);
            verify.Parameters.AddWithValue("@Id", id);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(legacyJson, reader.GetString(0));
            Assert.Equal(1, reader.GetInt32(1));
            Assert.Equal(!corruptJob, reader.GetBoolean(2));
        }
        finally
        {
            await using var cleanup = new SqlCommand("""
                DELETE accounting.SourcePartyIndex WHERE SourceDocumentId=@Id;
                DELETE dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId;
                DELETE dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId;
                """, connection);
            cleanup.Parameters.AddWithValue("@Id", id);
            cleanup.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            cleanup.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData("AccountingOpeningBalance")]
    [InlineData("PayrollAccrual")]
    public async Task Unposted_line_parties_follow_their_source_contract(string documentType)
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read });
        var accounts = await service.ListAccountsAsync(user);
        var at = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var id = Guid.NewGuid();
        var request = new ConfirmManualAccountingVoucherRequest(id, fixture.BusinessId, at,
            "OPENING_BALANCE", "Apertura pendiente", [
                new(accounts.First(account => account.Code == "519595").AccountId, fixture.SupplierPartyId, null, "Apertura", 100, 0),
                new(accounts.First(account => account.Code == "111005").AccountId, null, null, "Contrapartida", 0, 100)]);
        var manual = request with { VoucherId = Guid.NewGuid(), ConceptCode = "MANUAL_VOUCHER", MainPartyId = fixture.SupplierPartyId };
        var date = DateOnly.FromDateTime(at.UtcDateTime);
        var payload = documentType == "AccountingOpeningBalance" ? System.Text.Json.JsonSerializer.Serialize(request)
            : PayrollContractSerializer.Serialize(new PayrollAccountingPayload(fixture.TenantId, fixture.BusinessId, id,
                "Regular", date, date, date, "Nómina pendiente", [
                    new("AdministrativePayrollExpense", 100, 0, fixture.SupplierPartyId, "Devengado"),
                    new("NetPayrollPayable", 0, 100, null, "Contrapartida")]));
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        try
        {
            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction,
                    [new(fixture.TenantId, fixture.BusinessId, id, documentType, payload, at, Guid.NewGuid()),
                     new(fixture.TenantId, fixture.BusinessId, manual.VoucherId, AccountingManualDocumentTypes.ManualVoucher,
                         JsonSerializer.Serialize(manual), at, Guid.NewGuid())], at,
                    AccountingJobRequirement.AccountingEntryOnly, default);
                await transaction.CommitAsync();
            }
            using (var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
            {
                var result = await service.ListDocumentsAsync(user, new(2026, 10, 1), new(2026, 10, 31),
                    documentType, "Pending", null, 1, 50, partyId: fixture.SupplierPartyId);
                Assert.Equal(1, counter.Count);
                Assert.Contains(result.Items, item => item.SourceDocumentId == id);
            }
            using (var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
            {
                var mixed = await service.ListDocumentsAsync(user, new(2026, 10, 1), new(2026, 10, 31),
                    null, "Pending", null, 1, 50, partyId: fixture.SupplierPartyId);
                Assert.Equal(1, counter.Count);
                Assert.Contains(mixed.Items, item => item.SourceDocumentId == id && item.SourceDocumentType == documentType);
                Assert.Contains(mixed.Items, item => item.SourceDocumentId == manual.VoucherId
                    && item.SourceDocumentType == AccountingManualDocumentTypes.ManualVoucher);
            }
            var unrelated = await service.ListDocumentsAsync(user, new(2026, 10, 1), new(2026, 10, 31),
                documentType, "Pending", null, 1, 50, partyId: Guid.NewGuid());
            Assert.DoesNotContain(unrelated.Items, item => item.SourceDocumentId == id);
            var longerType = await service.ListDocumentsAsync(user, new(2026, 10, 1), new(2026, 10, 31),
                documentType.PadRight(64) + "X", "Pending", null, 1, 50, partyId: fixture.SupplierPartyId);
            Assert.Empty(longerType.Items);
            Assert.Equal(0, longerType.TotalCount);
            await using (var outsideRange = new SqlCommand("UPDATE dbo.AccountingPostingJobs SET OccurredAt=@At WHERE SourceDocumentId=@Id;", connection))
            {
                outsideRange.Parameters.AddWithValue("@At", at.AddDays(1));
                outsideRange.Parameters.AddWithValue("@Id", id);
                Assert.Equal(1, await outsideRange.ExecuteNonQueryAsync());
            }
            var jobOutsideRange = await service.ListDocumentsAsync(user, new(2026, 10, 1), new(2026, 10, 1),
                documentType, null, null, 1, 50, partyId: fixture.SupplierPartyId);
            Assert.DoesNotContain(jobOutsideRange.Items, item => item.SourceDocumentId == id);
            await using (var missingJob = new SqlCommand("""
                UPDATE dbo.AccountingSourceDocuments SET AccountingPostingJobId=NULL WHERE SourceDocumentId=@Id;
                DELETE dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id;
                """, connection))
            {
                missingJob.Parameters.AddWithValue("@Id", id);
                Assert.Equal(2, await missingJob.ExecuteNonQueryAsync());
            }
            foreach (var lookup in new (Guid? PartyId, string? Search)[]
                { (fixture.SupplierPartyId, id.ToString()), (null, id.ToString()), (null, null) })
            {
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
                var missing = await service.ListDocumentsAsync(user, new(2026, 10, 1), new(2026, 10, 1),
                    documentType, "MissingAccountingJob", lookup.Search, 1, 50, partyId: lookup.PartyId);
                Assert.Equal(1, counter.Count);
                Assert.Equal("MissingAccountingJob", Assert.Single(missing.Items).Status);
                Assert.Equal(id, missing.Items[0].SourceDocumentId);
                Assert.Equal(1, missing.TotalCount);
            }
        }
        finally
        {
            await using var cleanup = new SqlCommand("""
                DELETE accounting.SourcePartyIndex WHERE SourceDocumentId IN(@Id,@ManualId);
                DELETE dbo.AccountingSourceDocuments WHERE SourceDocumentId IN(@Id,@ManualId) AND TenantId=@TenantId AND BusinessId=@BusinessId;
                DELETE dbo.AccountingPostingJobs WHERE SourceDocumentId IN(@Id,@ManualId) AND TenantId=@TenantId AND BusinessId=@BusinessId;
                """, connection);
            cleanup.Parameters.AddWithValue("@Id", id);
            cleanup.Parameters.AddWithValue("@ManualId", manual.VoucherId);
            cleanup.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            cleanup.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Document_search_preserves_identifiers_patterns_collation_and_non_identifier_references()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var actor = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate,
                AccountingPermissionCodes.ManualSend });
        var accounts = await service.ListAccountsAsync(actor);
        var id = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var at = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        await service.ConfirmManualVoucherAsync(actor, new(id, fixture.BusinessId, at,
            "MANUAL_VOUCHER", "Búsqueda por identificador",
            [new(accounts.First(account => account.Code == "519595").AccountId, null, null, "Gasto", 1, 0),
             new(accounts.First(account => account.Code == "111005").AccountId, null, null, "Banco", 0, 1)]));
        var draft = await service.SaveVoucherDraftAsync(actor, new(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
            at, "MANUAL_VOUCHER", "Búsqueda por referencia", "BENCH-NO-MATCH", [], null, null));
        string[] searches = [id.ToString(), "01234567", "0123_567-89ab-cdef", "[0-9]1234567-89ab-cdef",
            "01234567%89ab-cdef", "01234567-89AB-CDEF", "01234567-89áb-cdef", "０１２３４５６７", "BENCH-NO-MATCH"];
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        // The existing SQL LIKE contract is the oracle, including the database's
        // collation. Resolve all expectations together, outside the reader probe.
        await using var expected = new SqlCommand("""
            SELECT CONVERT(int,input.[key]),CONVERT(bit,CASE WHEN CONVERT(nvarchar(36),@Id)
              LIKE N'%'+input.value+N'%' THEN 1 ELSE 0 END)
            FROM OPENJSON(@Searches) input ORDER BY CONVERT(int,input.[key]);
            """, connection);
        expected.Parameters.AddWithValue("@Id", id);
        expected.Parameters.Add("@Searches", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(searches);
        var matches = new List<bool>();
        await using (var reader = await expected.ExecuteReaderAsync())
            while (await reader.ReadAsync()) matches.Add(reader.GetBoolean(1));
        Assert.Equal(searches.Length, matches.Count);
        for (var index = 0; index < searches.Length; index++)
        {
            using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
            var page = await service.ListDocumentsAsync(actor, new(2026, 10, 22), new(2026, 10, 22),
                AccountingManualDocumentTypes.ManualVoucher, null, searches[index], 1, 50);
            Assert.Equal(1, counter.Count);
            Assert.Equal(matches[index], page.Items.Any(item => item.SourceDocumentId == id));
            if (searches[index] == "BENCH-NO-MATCH") Assert.Contains(page.Items, item => item.SourceDocumentId == draft.DocumentId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Document_page_and_count_share_the_same_keys_during_concurrent_acceptance(bool filterParty)
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var actor = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
                AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(actor);
        await service.ActivateAsync(actor, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var accounts = await service.ListAccountsAsync(actor);
        var partyId = Guid.NewGuid();
        await using var gate = new SqlConnection(fixture.ConnectionString);
        await gate.OpenAsync();
        await using (var party = new SqlCommand("""
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES(@PartyId,@TenantId,N'NaturalPerson',N'Lectura de bandeja',N'Incomplete',1,@UserId,@At);
            """, gate))
        {
            party.Parameters.AddWithValue("@PartyId", partyId);
            party.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            party.Parameters.AddWithValue("@UserId", fixture.UserId);
            party.Parameters.AddWithValue("@At", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
            Assert.Equal(1, await party.ExecuteNonQueryAsync());
        }
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read,
            AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        var firstId = await PostAsync();
        Guid? selectedParty = filterParty ? partyId : null;
        var baseline = await service.ListDocumentsAsync(actor, new(2026, 10, 18), new(2026, 10, 18),
            null, null, null, 1, 50, partyId: selectedParty);
        Assert.Contains(baseline.Items, item => item.SourceDocumentId == firstId);
        var resource = Guid.NewGuid().ToString("N");
        await using (var acquire = new SqlCommand("""
            DECLARE @Result int;
            EXEC @Result=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
            SELECT @Result;
            """, gate))
        {
            acquire.Parameters.AddWithValue("@Resource", resource);
            Assert.True((int)(await acquire.ExecuteScalarAsync())! >= 0);
        }
        const string boundary = "SELECT * FROM #Documents ORDER BY OccurredAt DESC,SourceDocumentId,SourceDocumentType;";
        var session = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var probe = new InventoryBalanceProcessingTests.CommandCounter(gate.Database,
            commandFilter: command => !session.Task.IsCompleted && command.CommandText.Contains(boundary, StringComparison.Ordinal)
                && Equals(command.Parameters["@PartyId"].Value, (object?)selectedParty ?? DBNull.Value),
            beforeExecute: command =>
            {
                command.CommandText = command.CommandText.Replace(boundary, """
                    EXEC sys.sp_getapplock @Resource=@TraceabilityReadGate,@LockMode=N'Shared',@LockOwner=N'Session';
                    EXEC sys.sp_releaseapplock @Resource=@TraceabilityReadGate,@LockOwner=N'Session';
                    """ + "\n" + boundary, StringComparison.Ordinal);
                command.Parameters.AddWithValue("@TraceabilityReadGate", resource);
                session.TrySetResult(command.Connection!.ServerProcessId);
            });
        var reading = service.ListDocumentsAsync(actor, new(2026, 10, 18), new(2026, 10, 18),
            null, null, null, 1, 50, partyId: selectedParty);
        Guid concurrentId;
        try
        {
            var sessionId = await session.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var waiting = new SqlCommand("""
                SELECT COUNT(*) FROM sys.dm_tran_locks
                WHERE request_session_id=@SessionId AND resource_type=N'APPLICATION' AND request_status=N'WAIT';
                """, gate);
            waiting.Parameters.AddWithValue("@SessionId", sessionId);
            while ((int)(await waiting.ExecuteScalarAsync(deadline.Token))! == 0)
                await Task.Delay(25, deadline.Token);
            concurrentId = await PostAsync();
        }
        finally
        {
            await using var release = new SqlCommand("EXEC sys.sp_releaseapplock @Resource=@Resource,@LockOwner=N'Session';", gate);
            release.Parameters.AddWithValue("@Resource", resource);
            await release.ExecuteNonQueryAsync();
        }
        var page = await reading;
        Assert.Equal(1, probe.Count);
        Assert.Equal(baseline.TotalCount, page.TotalCount);
        Assert.Equal(baseline.Items.Select(item => item.SourceDocumentId), page.Items.Select(item => item.SourceDocumentId));
        Assert.DoesNotContain(page.Items, item => item.SourceDocumentId == concurrentId);
        var refreshed = await service.ListDocumentsAsync(actor, new(2026, 10, 18), new(2026, 10, 18),
            null, null, null, 1, 50, partyId: selectedParty);
        Assert.Equal(baseline.TotalCount + 1, refreshed.TotalCount);
        Assert.Contains(refreshed.Items, item => item.SourceDocumentId == concurrentId);

        async Task<Guid> PostAsync()
        {
            var id = Guid.NewGuid();
            using var response = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers",
                new ConfirmManualAccountingVoucherRequest(id, fixture.BusinessId,
                    new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Lectura concurrente de comprobantes",
                    [new(accounts.First(account => account.Code == "519595").AccountId, partyId, null, "Gasto", 7, 0),
                     new(accounts.First(account => account.Code == "111005").AccountId, null, null, "Contrapartida", 0, 7)], partyId));
            Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
            return id;
        }
    }

    [Fact]
    public async Task Manual_party_index_is_derived_scoped_replay_safe_and_atomic()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var actor = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(actor);
        await service.ActivateAsync(actor, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var accounts = await service.ListAccountsAsync(actor);
        var at = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var linePartyId = Guid.NewGuid();
        var request = new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), fixture.BusinessId, at,
            "MANUAL_VOUCHER", "Índice de terceros", [
                new(accounts.First(account => account.Code == "519595").AccountId, fixture.SupplierPartyId, null, "Gasto", 100, 0),
                new(accounts.First(account => account.Code == "111005").AccountId, linePartyId, null, "Banco", 0, 100)], fixture.SupplierPartyId);
        var source = Source(request);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await using (var party = new SqlCommand("""
                INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
                SELECT @Id,TenantId,PartyType,N'Tercero de partida',CompletionStatus,IsActive,CreatedBy,CreatedAt
                FROM dbo.Parties WHERE PartyId=@SupplierPartyId;
                """, connection, transaction))
            {
                party.Parameters.AddWithValue("@Id", linePartyId);
                party.Parameters.AddWithValue("@SupplierPartyId", fixture.SupplierPartyId);
                Assert.Equal(1, await party.ExecuteNonQueryAsync());
            }
            await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction, [source], at,
                AccountingJobRequirement.AccountingEntryOnly, default);
            await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction,
                [source with { JobId = Guid.NewGuid(), OccurredAt = at.AddDays(1) }], at.AddDays(1),
                AccountingJobRequirement.AccountingEntryOnly, default);
            await AssertCountsAsync(source.DocumentId, 1, transaction);
            await using (var removeIndex = new SqlCommand("DELETE accounting.SourcePartyIndex WHERE SourceDocumentId=@Id", connection, transaction))
            {
                removeIndex.Parameters.AddWithValue("@Id", source.DocumentId);
                await removeIndex.ExecuteNonQueryAsync();
            }
            await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction, [source], at,
                AccountingJobRequirement.AccountingEntryOnly, default);
            await AssertCountsAsync(source.DocumentId, 1, transaction, expectedMembers: 0);
            await fixture.ApplyMigrationAsync("20261021_IndexManualVoucherParties.sql", connection, transaction);
            await fixture.ApplyMigrationAsync("20261021_IndexManualVoucherParties.sql", connection, transaction);
            await AssertCountsAsync(source.DocumentId, 1, transaction);
            // Existing derived rows cannot replace validation of the legacy
            // source's tenant references during an otherwise unchanged replay.
            await using (var corruptLegacyParty = new SqlCommand("""
                UPDATE dbo.AccountingSourceDocuments
                SET PayloadJson=JSON_MODIFY(PayloadJson,'$.MainPartyId',@InvalidPartyId)
                WHERE SourceDocumentId=@Id AND SourceDocumentType=N'ManualAccountingVoucher';
                """, connection, transaction))
            {
                corruptLegacyParty.Parameters.AddWithValue("@Id", source.DocumentId);
                corruptLegacyParty.Parameters.AddWithValue("@InvalidPartyId", Guid.NewGuid().ToString());
                Assert.Equal(1, await corruptLegacyParty.ExecuteNonQueryAsync());
            }
            var migrationError = await Assert.ThrowsAsync<SqlException>(() => fixture.ApplyMigrationAsync(
                "20261021_IndexManualVoucherParties.sql", connection, transaction));
            Assert.Equal(51734, migrationError.Number);
            await AssertCountsAsync(source.DocumentId, 1, transaction);
            await transaction.RollbackAsync();
        }
        await AssertCountsAsync(source.DocumentId, 0, null);
        var validLines = request.Lines.Select(line => line with { PartyId = fixture.SupplierPartyId }).ToArray();
        foreach (var invalid in new[] {
            Source(request with { VoucherId = Guid.NewGuid(), MainPartyId = Guid.NewGuid(), Lines = validLines }),
            Source(request with { VoucherId = Guid.NewGuid(), Lines = [validLines[0], validLines[1] with { PartyId = Guid.NewGuid() }] }) })
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            var error = await Assert.ThrowsAsync<SqlException>(() => SqlAccountingPostingJobWriter.InsertSourcesAsync(
                connection, transaction, [invalid], at, AccountingJobRequirement.AccountingEntryOnly, default));
            Assert.Equal(51731, error.Number);
            if (transaction.Connection is not null) await transaction.RollbackAsync();
            await AssertCountsAsync(invalid.DocumentId, 0, null);
        }

        SqlAccountingPostingJobWriter.Source Source(ConfirmManualAccountingVoucherRequest value) => new(
            fixture.TenantId, fixture.BusinessId, value.VoucherId, AccountingManualDocumentTypes.ManualVoucher,
            System.Text.Json.JsonSerializer.Serialize(value), at, Guid.NewGuid());
        async Task AssertCountsAsync(Guid id, int expected, SqlTransaction? transaction, int? expectedMembers = null)
        {
            await using var command = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id),
                  (SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id),
                  (SELECT COUNT(*) FROM accounting.SourcePartyIndex WHERE SourceDocumentId=@Id);
                """, connection, transaction);
            command.Parameters.AddWithValue("@Id", id);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            for (var field = 0; field < 2; field++) Assert.Equal(expected, reader.GetInt32(field));
            Assert.Equal(expectedMembers ?? expected * 2, reader.GetInt32(2));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Explicit_legacy_recovery_links_one_job_preserves_the_frozen_mode_and_never_repeats_an_entry(bool? frozenMode)
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Retry });
        var accounts = await service.ListAccountsAsync(user);
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        var request = new ConfirmManualAccountingVoucherRequest(id, fixture.BusinessId, at,
            "MANUAL_VOUCHER", "Recuperación histórica explícita", [
                new(accounts.First(account => account.Code == "519595").AccountId, fixture.SupplierPartyId, null, "Gasto", 100, 0),
                new(accounts.First(account => account.Code == "111005").AccountId, null, null, "Banco", 0, 100)]);
        var payload = JsonSerializer.Serialize(request);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction,
                [new(fixture.TenantId, fixture.BusinessId, id, AccountingManualDocumentTypes.ManualVoucher,
                    payload, at, Guid.NewGuid())], at, AccountingJobRequirement.PreserveCommercialEffects, default);
            // Reproduce a legacy source without work and with its original mode,
            // including the absence of evidence. Never mutate a real database.
            await using var legacy = new SqlCommand("""
                UPDATE dbo.AccountingSourceDocuments SET AccountingPostingJobId=NULL,
                  AccountingEntryRequired=@Mode WHERE SourceDocumentId=@Id;
                DELETE dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id;
                """, connection, transaction);
            legacy.Parameters.AddWithValue("@Id", id);
            legacy.Parameters.Add("@Mode", SqlDbType.Bit).Value = (object?)frozenMode ?? DBNull.Value;
            Assert.Equal(2, await legacy.ExecuteNonQueryAsync());
            await transaction.CommitAsync();
        }
        if (frozenMode is null)
        {
            var error = await Assert.ThrowsAsync<AccountingConflictException>(() => service.RetryPostingAsync(user, id));
            Assert.Contains("frozen mode", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            var expected = frozenMode.Value ? "Posted" : "CommercialEffectsApplied";
            Assert.Equal(expected, (await service.RetryPostingAsync(user, id))!.Status);
            Assert.Equal(expected, (await service.RetryPostingAsync(user, id))!.Status);
        }
        await using var verify = new SqlCommand("""
            SELECT source.PayloadJson,source.AccountingEntryRequired,
              CONVERT(bit,CASE WHEN source.AccountingPostingJobId=job.AccountingPostingJobId
                AND source.TenantId=job.TenantId AND source.BusinessId=job.BusinessId
                AND source.PayloadHash=job.SourcePayloadHash AND source.OccurredAt=job.OccurredAt
                AND source.AccountingEntryRequired=job.AccountingEntryRequired THEN 1 ELSE 0 END),
              (SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id),
              (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id),
              source.AccountingPostingJobId
            FROM dbo.AccountingSourceDocuments source
            LEFT JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=source.SourceDocumentId
              AND job.SourceDocumentType=source.SourceDocumentType
            WHERE source.SourceDocumentId=@Id;
            """, connection);
        verify.Parameters.AddWithValue("@Id", id);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(payload, reader.GetString(0));
        Assert.Equal(frozenMode, reader.IsDBNull(1) ? (bool?)null : reader.GetBoolean(1));
        Assert.Equal(frozenMode.HasValue, reader.GetBoolean(2));
        Assert.Equal(frozenMode.HasValue ? 1 : 0, reader.GetInt32(3));
        Assert.Equal(frozenMode == true ? 1 : 0, reader.GetInt32(4));
        Assert.Equal(!frozenMode.HasValue, reader.IsDBNull(5));
    }

    [Fact]
    public async Task Source_job_links_are_scoped_and_legacy_backfill_never_creates_work_or_repairs_a_replay()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        var at = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var sources = Enumerable.Range(0, 2).Select(_ => new SqlAccountingPostingJobWriter.Source(
            fixture.TenantId, fixture.BusinessId, Guid.NewGuid(), "SourceJobLinkTest", "{}", at, Guid.NewGuid())).ToArray();
        var legacyId = Guid.NewGuid();
        try
        {
            await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction, sources, at,
                AccountingJobRequirement.PreserveCommercialEffects, default);
            await using (var legacy = new SqlCommand("""
                UPDATE dbo.AccountingSourceDocuments SET AccountingPostingJobId=NULL WHERE SourceDocumentId=@LinkedId;
                INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,
                  PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
                VALUES(@LegacyId,N'SourceJobLinkTest',@TenantId,@BusinessId,N'{}',HASHBYTES('SHA2_256','{}'),@At,@At,1);
                """, connection, transaction))
            {
                legacy.Parameters.AddWithValue("@LinkedId", sources[0].DocumentId);
                legacy.Parameters.AddWithValue("@LegacyId", legacyId);
                legacy.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                legacy.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                legacy.Parameters.AddWithValue("@At", at);
                await legacy.ExecuteNonQueryAsync();
            }
            await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction,
                [sources[0] with { JobId = Guid.NewGuid() }], at, AccountingJobRequirement.PreserveCommercialEffects, default);
            await using (var replay = new SqlCommand("SELECT AccountingPostingJobId FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", connection, transaction))
            {
                replay.Parameters.AddWithValue("@Id", sources[0].DocumentId);
                Assert.Equal(DBNull.Value, await replay.ExecuteScalarAsync());
            }
            await fixture.ApplyMigrationAsync("20261022_LinkAccountingSourcesToJobs.sql", connection, transaction);
            await fixture.ApplyMigrationAsync("20261022_LinkAccountingSourcesToJobs.sql", connection, transaction);
            await using (var verify = new SqlCommand("""
                SELECT (SELECT AccountingPostingJobId FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@LinkedId),
                  (SELECT AccountingPostingJobId FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@LegacyId),
                  (SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId IN(@LinkedId,@OtherId,@LegacyId)),
                  (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId IN(@LinkedId,@OtherId,@LegacyId));
                """, connection, transaction))
            {
                verify.Parameters.AddWithValue("@LinkedId", sources[0].DocumentId);
                verify.Parameters.AddWithValue("@OtherId", sources[1].DocumentId);
                verify.Parameters.AddWithValue("@LegacyId", legacyId);
                await using var reader = await verify.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(sources[0].JobId, reader.GetGuid(0));
                Assert.True(reader.IsDBNull(1));
                Assert.Equal(2, reader.GetInt32(2));
                Assert.Equal(0, reader.GetInt32(3));
            }
            await using var wrongJob = new SqlCommand("UPDATE dbo.AccountingSourceDocuments SET AccountingPostingJobId=@WrongJob WHERE SourceDocumentId=@Id", connection, transaction);
            wrongJob.Parameters.AddWithValue("@WrongJob", sources[1].JobId);
            wrongJob.Parameters.AddWithValue("@Id", sources[0].DocumentId);
            var error = await Assert.ThrowsAsync<SqlException>(() => wrongJob.ExecuteNonQueryAsync());
            Assert.Equal(547, error.Number);
        }
        finally
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Canonical_acceptance_is_batched_atomic_scoped_and_replay_preserves_the_frozen_mode()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var protectedAccount = await service.CreateAccountAsync(user,
            new(Guid.NewGuid(), fixture.TenantId, "51959701", "Cuenta de aceptación", "Expense", true, false));
        var commercialAccount = await service.CreateAccountAsync(user,
            new(Guid.NewGuid(), fixture.TenantId, "51959702", "Cuenta comercial configurable", "Expense", true, false));
        var occurredAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var sources = Enumerable.Range(0, 100).Select(index => new SqlAccountingPostingJobWriter.Source(
            fixture.TenantId, fixture.BusinessId, Guid.NewGuid(), "AcceptanceContractTest",
            System.Text.Json.JsonSerializer.Serialize(new { Line = index, CounterpartAccountId = protectedAccount.AccountId }), occurredAt, Guid.NewGuid())).ToArray();
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            using (var counter = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog))
            {
                var timer = Stopwatch.StartNew();
                Assert.Equal(100, await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction, sources, occurredAt,
                    AccountingJobRequirement.AccountingEntryOnly, default));
                timer.Stop();
                Assert.Equal(1, counter.Count);
                Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
                output.WriteLine($"Aceptar 100 fuentes: {counter.Count} SQL, {timer.Elapsed.TotalMilliseconds:F1} ms.");
            }
            await using (var disable = new SqlCommand("""
                UPDATE dbo.AccountingTenantSettings SET Status=N'Disabled',EffectiveFrom=NULL,
                  ActivatedAt=NULL,ActivatedByUserId=NULL WHERE TenantId=@TenantId;
                """, connection, transaction))
            {
                disable.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                await disable.ExecuteNonQueryAsync();
            }
            Assert.Equal(100, await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction,
                sources.Select(source => source with { JobId = Guid.NewGuid(), OccurredAt = occurredAt.AddDays(1) }).ToArray(), occurredAt.AddDays(1),
                AccountingJobRequirement.PreserveCommercialEffects, default));
            await using var verify = new SqlCommand("""
                SELECT COUNT(*),SUM(CONVERT(int,s.AccountingEntryRequired)),
                  SUM(CASE WHEN j.AccountingPostingJobId=input.JobId AND j.AccountingEntryRequired=1
                    AND s.OccurredAt=@OriginalOccurredAt AND j.OccurredAt=@OriginalOccurredAt THEN 1 ELSE 0 END),
                  SUM(CASE WHEN s.AccountingPostingJobId=j.AccountingPostingJobId THEN 1 ELSE 0 END)
                FROM OPENJSON(@Sources) WITH(DocumentId uniqueidentifier,JobId uniqueidentifier) input
                JOIN dbo.AccountingSourceDocuments s ON s.SourceDocumentId=input.DocumentId
                JOIN dbo.AccountingPostingJobs j ON j.SourceDocumentId=s.SourceDocumentId
                  AND j.SourceDocumentType=s.SourceDocumentType;
                """, connection, transaction);
            verify.Parameters.AddWithValue("@Sources", System.Text.Json.JsonSerializer.Serialize(sources));
            verify.Parameters.AddWithValue("@OriginalOccurredAt", occurredAt);
            await using (var reader = await verify.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(100, reader.GetInt32(0));
                Assert.Equal(100, reader.GetInt32(1));
                Assert.Equal(100, reader.GetInt32(2));
                Assert.Equal(100, reader.GetInt32(3));
            }
            var newSource = sources[0] with { DocumentId = Guid.NewGuid(), JobId = Guid.NewGuid(),
                Payload = System.Text.Json.JsonSerializer.Serialize(new { CounterpartAccountId = commercialAccount.AccountId }) };
            Assert.Equal(0, await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction, [newSource], occurredAt,
                AccountingJobRequirement.AccountingEntryOnly, default));
            await using (var skipped = new SqlCommand("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", connection, transaction))
            {
                skipped.Parameters.AddWithValue("@Id", newSource.DocumentId);
                Assert.Equal(0, Convert.ToInt32(await skipped.ExecuteScalarAsync()));
            }
            Assert.Equal(1, await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction, [newSource], occurredAt,
                AccountingJobRequirement.PreserveCommercialEffects, default));
            await using (var commercial = new SqlCommand("""
                SELECT CONVERT(int,s.AccountingEntryRequired)+CONVERT(int,j.AccountingEntryRequired)
                FROM dbo.AccountingSourceDocuments s JOIN dbo.AccountingPostingJobs j
                  ON j.SourceDocumentId=s.SourceDocumentId AND j.SourceDocumentType=s.SourceDocumentType
                WHERE s.SourceDocumentId=@Id;
                """, connection, transaction))
            {
                commercial.Parameters.AddWithValue("@Id", newSource.DocumentId);
                Assert.Equal(0, Convert.ToInt32(await commercial.ExecuteScalarAsync()));
            }
            await using (var legacyProtection = new SqlCommand("UPDATE dbo.AccountingAccounts SET ConfigurationLockedAt=NULL WHERE AccountId=@Id", connection, transaction))
            {
                legacyProtection.Parameters.AddWithValue("@Id", protectedAccount.AccountId);
                await legacyProtection.ExecuteNonQueryAsync();
            }
            await fixture.ApplyMigrationAsync("20261021_ProtectUsedAccountConfiguration.sql", connection, transaction);
            await fixture.ApplyMigrationAsync("20261021_ProtectUsedAccountConfiguration.sql", connection, transaction);
            await using (var protection = new SqlCommand("""
                SELECT COUNT(*) FROM dbo.AccountingAccounts
                WHERE (AccountId=@Protected AND ConfigurationLockedAt IS NOT NULL)
                   OR (AccountId=@Commercial AND ConfigurationLockedAt IS NULL);
                """, connection, transaction))
            {
                protection.Parameters.AddWithValue("@Protected", protectedAccount.AccountId);
                protection.Parameters.AddWithValue("@Commercial", commercialAccount.AccountId);
                Assert.Equal(2, Convert.ToInt32(await protection.ExecuteScalarAsync()));
            }
            await transaction.RollbackAsync();
        }
        // Rolling back acceptance rolls back both the immutable input and its work.
        await using (var absent = new SqlCommand("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", connection))
        {
            absent.Parameters.AddWithValue("@Id", sources[0].DocumentId);
            Assert.Equal(0, Convert.ToInt32(await absent.ExecuteScalarAsync()));
        }

        foreach (var invalid in new[] { sources[0] with { BusinessId = Guid.NewGuid() },
            sources[0] with { TenantId = Guid.NewGuid() }, sources[0] with { Payload = null } })
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            var error = await Assert.ThrowsAsync<SqlException>(() => SqlAccountingPostingJobWriter.InsertSourcesAsync(
                connection, transaction, [invalid], occurredAt, AccountingJobRequirement.PreserveCommercialEffects, default));
            Assert.Equal(51731, error.Number);
            // XACT_ABORT may have already rolled back this deliberately invalid command.
            if (transaction.Connection is not null) await transaction.RollbackAsync();
        }
        foreach (var inconsistency in new[] { "ChangedSource", "MissingJob", "ChangedJobMode", "ChangedJobHash", "MissingSource", "MissingSourceInactive" })
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction, [sources[0]], occurredAt,
                AccountingJobRequirement.PreserveCommercialEffects, default);
            if (inconsistency == "MissingJob")
            {
                // Simulate a legacy orphan inside this rollback-only fixture transaction.
                await using var orphan = new SqlCommand("""
                    UPDATE dbo.AccountingSourceDocuments SET AccountingPostingJobId=NULL WHERE SourceDocumentId=@Id;
                    DELETE dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id;
                    """, connection, transaction);
                orphan.Parameters.AddWithValue("@Id", sources[0].DocumentId);
                await orphan.ExecuteNonQueryAsync();
            }
            if (inconsistency is "ChangedJobMode" or "ChangedJobHash")
            {
                var mutation = inconsistency == "ChangedJobMode"
                    ? "UPDATE dbo.AccountingPostingJobs SET AccountingEntryRequired=1-AccountingEntryRequired WHERE SourceDocumentId=@Id"
                    : "UPDATE dbo.AccountingPostingJobs SET SourcePayloadHash=HASHBYTES('SHA2_256',N'altered') WHERE SourceDocumentId=@Id";
                await using var inconsistent = new SqlCommand(mutation, connection, transaction);
                inconsistent.Parameters.AddWithValue("@Id", sources[0].DocumentId);
                Assert.Equal(1, await inconsistent.ExecuteNonQueryAsync());
            }
            if (inconsistency is "MissingSource" or "MissingSourceInactive")
            {
                await using var orphan = new SqlCommand("""
                    DELETE dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id;
                    IF @Disable=1
                      UPDATE dbo.AccountingTenantSettings SET Status=N'Disabled',EffectiveFrom=NULL,
                        ActivatedAt=NULL,ActivatedByUserId=NULL WHERE TenantId=@TenantId;
                    """, connection, transaction);
                orphan.Parameters.AddWithValue("@Id", sources[0].DocumentId);
                orphan.Parameters.AddWithValue("@Disable", inconsistency == "MissingSourceInactive");
                orphan.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                await orphan.ExecuteNonQueryAsync();
            }
            var repeated = inconsistency == "ChangedSource" ? sources[0] with { Payload = "{\"Line\":999}" } : sources[0];
            var error = await Assert.ThrowsAsync<SqlException>(() => SqlAccountingPostingJobWriter.InsertSourcesAsync(
                connection, transaction, [repeated], occurredAt,
                inconsistency == "MissingSourceInactive" ? AccountingJobRequirement.AccountingEntryOnly : AccountingJobRequirement.PreserveCommercialEffects, default));
            Assert.Equal(inconsistency == "ChangedSource" ? 51732 : 51733, error.Number);
            if (transaction.Connection is not null) await transaction.RollbackAsync();
        }
    }
}
