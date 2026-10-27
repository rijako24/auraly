using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(AccountingSliceCollection.Name)]
public sealed class AccountingClosureClaimTests(ServerSliceFixture fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cash_sale_corrections_bind_each_new_balance_to_its_own_net_opening(bool accountingRequired)
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        var actor = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(actor);
        await service.ActivateAsync(actor, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var at = new DateTimeOffset(accountingRequired ? 2026 : 2025, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var partyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var saleIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var arrange = new SqlCommand("""
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES(@PartyId,@TenantId,N'Organization',N'Cliente de cierre',N'Incomplete',1,@UserId,@At);
            INSERT dbo.Customers(CustomerId,PartyId,TenantId,RequiresElectronicInvoice,IsActive,CreatedBy,CreatedAt)
            VALUES(@CustomerId,@PartyId,@TenantId,0,1,@UserId,@At);
            DECLARE @SiteId uniqueidentifier=NEWID();
            INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,
              CityId,AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
            SELECT TOP(1) @SiteId,@PartyId,N'PRINCIPAL',N'Principal',country.CountryId,
              division.AdministrativeDivisionId,city.CityId,N'Dirección de prueba',1,1,@UserId,@At
            FROM dbo.Cities city JOIN dbo.AdministrativeDivisions division
              ON division.AdministrativeDivisionId=city.AdministrativeDivisionId
            JOIN dbo.Countries country ON country.CountryId=division.CountryId
            WHERE city.IsActive=1 AND division.IsActive=1 AND country.IsActive=1
            ORDER BY city.CityId;
            INSERT dbo.SalesDocuments(DocumentId,BusinessId,WarehouseId,SourceMode,DocumentSeriesId,
              DocumentNumber,DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,DocumentType,
              IdempotencyKey,PayloadHash,IssuedAt,CustomerIdentification,CustomerId,CustomerPartySiteId,
              UntaxedAmount,TaxAmount,PayableAmount,CreditAmount,ProcessingStatus,ReceivedAt,SoldByUserId)
            SELECT input.Id,@BusinessId,@WarehouseId,N'Online',@SeriesId,
              CONVERT(nvarchar(36),input.Id),N'ECC',N'00',input.Ordinal,N'SalesReceipt',
              CONVERT(nvarchar(36),input.Id),CONVERT(binary(32),0),@At,N'900123456',@CustomerId,@SiteId,
              input.Amount,0,input.Amount,0,N'Processed',@At,@UserId
            FROM OPENJSON(@Sales) WITH(Id uniqueidentifier,Ordinal bigint,Amount decimal(19,4)) input;
            -- Original cash sales are already completed; this test processes only their later correction.
            INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,
              SourceDocumentId,SourceDocumentType,SourcePayloadHash,OccurredAt,Status,AttemptCount,CreatedAt)
            SELECT NEWID(),@TenantId,@BusinessId,Id,N'SalesReceipt',CONVERT(binary(32),0),@At,N'Posted',1,@At
            FROM OPENJSON(@Sales) WITH(Id uniqueidentifier);
            """, connection))
        {
            arrange.Parameters.AddWithValue("@PartyId", partyId);
            arrange.Parameters.AddWithValue("@CustomerId", customerId);
            arrange.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            arrange.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            arrange.Parameters.AddWithValue("@UserId", fixture.UserId);
            arrange.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            arrange.Parameters.AddWithValue("@SeriesId", fixture.OnlineSalesReceiptSeriesId);
            arrange.Parameters.AddWithValue("@At", at);
            arrange.Parameters.AddWithValue("@Sales", JsonSerializer.Serialize(saleIds.Select((id, index) =>
                new { Id = id, Ordinal = (long)BitConverter.ToUInt32(id.ToByteArray(), 0) + 1, Amount = (index + 1) * 10_000m })));
            await arrange.ExecuteNonQueryAsync();
        }
        var payload = new WorkSessionClosureReconciliationPayload(Guid.NewGuid(), Guid.NewGuid(), fixture.TenantId,
            fixture.BusinessId, fixture.UserId, at,
            [new("Cash", "Cash", 26_000m, 22_000m, 22_000m, -4_000m, "CORRECTION"),
             new("Transfer", "Bank", 4_000m, 5_000m, 5_000m, 1_000m, "CORRECTION")], [],
            PaymentCorrections:
            [new("sale-one-cash", "Sale", saleIds[0], 1, "Cash", 6_000m, "Cash", 4_000m,
                "CORRECTION", partyId, customerId, null, "AccountsReceivable"),
             new("sale-one-transfer", "Sale", saleIds[0], 1, "Transfer", 4_000m, "Transfer", 5_000m,
                "CORRECTION", partyId, customerId, null, "AccountsReceivable"),
             new("sale-two-cash", "Sale", saleIds[1], 2, "Cash", 20_000m, "Cash", 18_000m,
                "CORRECTION", partyId, customerId, null, "AccountsReceivable")]);
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction,
                [new(fixture.TenantId, fixture.BusinessId, payload.ReconciliationId,
                    WorkSessionAccountingDocumentTypes.ClosureReconciliation,
                    JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)), at, Guid.NewGuid())],
                at, AccountingJobRequirement.PreserveCommercialEffects, default);
            await transaction.CommitAsync();
        }
        await using var notices = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages
            WHERE BusinessId=@BusinessId AND EntityType=N'Customer' AND EntityId=@CustomerId AND Stream=N'Customers';
            """, connection);
        notices.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        notices.Parameters.AddWithValue("@CustomerId", customerId);
        var noticesBefore = (int)(await notices.ExecuteScalarAsync())!;
        if (accountingRequired)
        {
            using var fault = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                commandFilter: command => command.CommandText.Contains("DECLARE @OpeningLines TABLE", StringComparison.Ordinal),
                beforeExecute: command =>
                {
                    var lines = JsonNode.Parse((string)command.Parameters["@Lines"].Value)!.AsArray();
                    lines.First(line => line!["OpeningDocumentId"] is not null)!["OpeningDocumentId"] = Guid.NewGuid();
                    command.Parameters["@Lines"].Value = lines.ToJsonString();
                });
            var error = await Assert.ThrowsAsync<SqlException>(() => processor.ProcessAsync(payload.ReconciliationId,
                WorkSessionAccountingDocumentTypes.ClosureReconciliation, fixture.BusinessId, default));
            Assert.Contains(error.Errors.Cast<SqlError>(), item => item.Number == 51844);
            Assert.Equal(1, fault.Count);
            Assert.Equal(noticesBefore, (int)(await notices.ExecuteScalarAsync())!);
        }
        using (var notifications = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter: command => command.CommandText.Contains("INSERT dbo.PosSynchronizationOutboxMessages", StringComparison.Ordinal)))
        using (var openings = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter: command => command.CommandText.Contains("INSERT dbo.Receivables", StringComparison.Ordinal)))
        using (var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter: command => command.CommandText.Contains("DECLARE @JournalLines TABLE", StringComparison.Ordinal)))
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await processor.ProcessAsync(payload.ReconciliationId, WorkSessionAccountingDocumentTypes.ClosureReconciliation,
                fixture.BusinessId, default);
            watch.Stop();
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Two-sale closure processing took {watch.Elapsed}.");
            Assert.Equal(accountingRequired ? 1 : 0, counter.Count);
            Assert.Equal(1, notifications.Count);
            Assert.Equal(1, openings.Count);
        }
        Assert.Equal(noticesBefore + 1, (int)(await notices.ExecuteScalarAsync())!);
        using (var notifications = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter: command => command.CommandText.Contains("INSERT dbo.PosSynchronizationOutboxMessages", StringComparison.Ordinal)))
        using (var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter: command => command.CommandText.Contains("DECLARE @JournalLines TABLE", StringComparison.Ordinal)))
        {
            await processor.ProcessAsync(payload.ReconciliationId, WorkSessionAccountingDocumentTypes.ClosureReconciliation,
                fixture.BusinessId, default);
            Assert.Equal(0, counter.Count);
            Assert.Equal(0, notifications.Count);
        }
        Assert.Equal(noticesBefore + 1, (int)(await notices.ExecuteScalarAsync())!);
        var positions = await service.OpenPositionsAsync(actor, new("Receivable", 1, 25, PartyId: partyId));
        Assert.Equal(2, positions.Items.Count);
        Assert.Equal(3_000m, positions.Items.Sum(position => position.OutstandingAmount));
        if (accountingRequired)
            Assert.Equal(2, positions.Items.Select(position => position.OpeningEntryLineNumber).Distinct().Count());
        foreach (var position in positions.Items)
        {
            Assert.Equal(customerId, position.CounterpartyRoleId);
            Assert.Equal(partyId, position.PartyId);
            Assert.Equal(position.SourceDocumentId == saleIds[0] ? 1_000m : 2_000m, position.OutstandingAmount);
            if (accountingRequired)
            {
                Assert.NotNull(position.AccountId);
                Assert.Null(position.OpeningLinkIssue);
            }
            else
            {
                Assert.Null(position.AccountId);
                Assert.NotNull(position.OpeningLinkIssue);
            }
        }
        await using var verify = new SqlCommand("""
            SELECT balance.SourceDocumentId,balance.OriginalAmount,line.Debit,line.Credit,
              entry.SourceDocumentId,entry.SourceDocumentType,
              (SELECT COUNT(*) FROM dbo.ReceivableTransactions transactionLine
               WHERE transactionLine.ReceivableId=balance.ReceivableId) Transactions
            FROM dbo.Receivables balance
            LEFT JOIN dbo.AccountingEntryLines line ON line.EntryId=balance.OpeningEntryId
              AND line.LineNumber=balance.OpeningEntryLineNumber
            LEFT JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
            WHERE balance.BusinessId=@BusinessId AND balance.CustomerId=@CustomerId;
            """, connection);
        verify.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        verify.Parameters.AddWithValue("@CustomerId", customerId);
        await using var reader = await verify.ExecuteReaderAsync();
        var count = 0;
        while (await reader.ReadAsync())
        {
            Assert.Contains(reader.GetGuid(0), saleIds);
            Assert.Equal(1, reader.GetInt32(6));
            if (accountingRequired)
            {
                Assert.Equal(reader.GetDecimal(1), reader.GetDecimal(2));
                Assert.Equal(0m, reader.GetDecimal(3));
                Assert.Equal(payload.ReconciliationId, reader.GetGuid(4));
                Assert.Equal(WorkSessionAccountingDocumentTypes.ClosureReconciliation, reader.GetString(5));
            }
            else Assert.True(reader.IsDBNull(2));
            count++;
        }
        Assert.Equal(2, count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Employee_claim_uses_the_financial_processor_atomically_and_preserves_legacy_openings(
        bool accountingRequired, bool legacyOpening)
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        var actor = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(actor);
        await service.ActivateAsync(actor, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var mappedAccounts = (await service.ListMappingsAsync(actor)).Where(mapping => mapping.Category == "EmployeeShortagesReceivable")
            .Select(mapping => mapping.AccountId).ToHashSet();
        var activeAccounts = (await service.ListAccountsAsync(actor)).Where(account => mappedAccounts.Contains(account.AccountId) && account.IsActive)
            .Select(account => account.AccountId).ToArray();
        Assert.NotEmpty(activeAccounts);
        var at = new DateTimeOffset(accountingRequired ? 2026 : 2025, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var partyId = Guid.NewGuid();
        var claim = new WorkSessionCashShortageClaim(Guid.NewGuid(), partyId, 5_000m);
        var payload = new WorkSessionClosureReconciliationPayload(Guid.NewGuid(), Guid.NewGuid(), fixture.TenantId,
            fixture.BusinessId, fixture.UserId, at,
            [new("Cash", "Cash", 5_000m, 0m, 0m, -5_000m, "COUNT_DIFFERENCE")], [], [], claim);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var party = new SqlCommand("""
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES(@PartyId,@TenantId,N'NaturalPerson',N'Empleado del cierre',N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.Employees(EmployeeId,TenantId,PartyId,Name,IsActive)
            VALUES(NEWID(),@TenantId,@PartyId,N'Empleado del cierre',1);
            """, connection))
        {
            party.Parameters.AddWithValue("@PartyId", partyId);
            party.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            party.Parameters.AddWithValue("@UserId", fixture.UserId);
            await party.ExecuteNonQueryAsync();
        }
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction,
                [new(fixture.TenantId, fixture.BusinessId, payload.ReconciliationId,
                    WorkSessionAccountingDocumentTypes.ClosureReconciliation,
                    JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)), at, Guid.NewGuid())],
                at, AccountingJobRequirement.PreserveCommercialEffects, default);
            await transaction.CommitAsync();
        }
        await AssertStateAsync(0, 0, 0);
        if (legacyOpening)
        {
            await using var legacy = new SqlCommand("""
                INSERT dbo.Receivables(ReceivableId,BusinessId,PartyId,SourceDocumentId,SourceDocumentType,
                  DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
                VALUES(@ReceivableId,@BusinessId,@PartyId,@SourceId,N'WorkSessionClosureReconciliation',
                  @Number,N'COP',5000,3000,@At,N'PartiallyPaid',@At);
                INSERT dbo.ReceivableTransactions(ReceivableTransactionId,ReceivableId,TransactionType,Amount,
                  SourceDocumentId,OccurredAt,CreatedAt)
                VALUES(NEWID(),@ReceivableId,N'Opening',5000,@SourceId,@At,@At);
                INSERT dbo.ReceivableTransactions(ReceivableTransactionId,ReceivableId,TransactionType,Amount,
                  SourceDocumentId,OccurredAt,CreatedAt)
                VALUES(NEWID(),@ReceivableId,N'Payment',-2000,NEWID(),@At,@At);
                """, connection);
            legacy.Parameters.AddWithValue("@ReceivableId", claim.ReceivableId);
            legacy.Parameters.AddWithValue("@PartyId", partyId);
            legacy.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            legacy.Parameters.AddWithValue("@SourceId", payload.ReconciliationId);
            legacy.Parameters.AddWithValue("@Number", $"FALT-{payload.WorkSessionClosureId:N}");
            legacy.Parameters.AddWithValue("@At", at);
            await legacy.ExecuteNonQueryAsync();
        }
        if (accountingRequired && !legacyOpening)
        {
            await SetAccountActiveAsync(false);
            try
            {
                await ProcessAsync();
                // Configuration failure rolls back both the obligation and its opening.
                await AssertStateAsync(0, 0, 0);
            }
            finally { await SetAccountActiveAsync(true); }

            // Fail after the journal insert, at the FK-binding write, to prove
            // that the opening, entry and its lines share the processor transaction.
            Assert.StartsWith("AuralyServerSlice_", connection.Database);
            var triggerName = $"TestRejectOpeningBinding_{Guid.NewGuid():N}";
            await using (var trigger = new SqlCommand($"""
                CREATE TRIGGER dbo.[{triggerName}] ON dbo.Receivables AFTER UPDATE AS
                BEGIN
                  IF EXISTS(SELECT 1 FROM inserted WHERE ReceivableId='{claim.ReceivableId:D}'
                    AND OpeningEntryId IS NOT NULL)
                    THROW 51997,'Injected failure while binding the opening journal line.',1;
                END;
                """, connection))
                await trigger.ExecuteNonQueryAsync();
            try
            {
                var error = await Assert.ThrowsAsync<SqlException>(ProcessAsync);
                Assert.Contains(error.Errors.Cast<SqlError>(), item => item.Number == 51997);
                await AssertStateAsync(0, 0, 0);
            }
            finally
            {
                await using var drop = new SqlCommand($"DROP TRIGGER dbo.[{triggerName}];", connection);
                await drop.ExecuteNonQueryAsync();
            }
        }
        await ProcessAsync();
        await ProcessAsync();
        await AssertStateAsync(1, 1, accountingRequired ? 1 : 0);
        var positions = await service.OpenPositionsAsync(actor,
            new("Receivable", 1, 25, PartyId: partyId, SubledgerId: claim.ReceivableId));
        var position = Assert.Single(positions.Items);
        Assert.Null(position.CounterpartyRoleId);
        Assert.Equal(partyId, position.PartyId);
        Assert.Equal(legacyOpening ? 3000m : 5000m, position.OutstandingAmount);
        Assert.Equal(position.OutstandingAmount, position.AvailableAmount);
        if (accountingRequired && !legacyOpening)
        {
            Assert.Contains(position.AccountId!.Value, activeAccounts);
            Assert.Null(position.OpeningLinkIssue);
        }
        if (!accountingRequired)
        {
            Assert.Null(position.AccountId);
            Assert.NotNull(position.OpeningLinkIssue);
        }

        Task ProcessAsync() => processor.ProcessAsync(payload.ReconciliationId,
            WorkSessionAccountingDocumentTypes.ClosureReconciliation, fixture.BusinessId, default);

        async Task SetAccountActiveAsync(bool active)
        {
            await using var command = new SqlCommand("""
                UPDATE dbo.AccountingAccounts SET IsActive=@Active
                WHERE TenantId=@TenantId AND AccountId IN(SELECT value FROM OPENJSON(@Ids));
                """, connection);
            command.Parameters.AddWithValue("@Active", active);
            command.Parameters.AddWithValue("@Ids", JsonSerializer.Serialize(activeAccounts));
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            Assert.Equal(activeAccounts.Length, await command.ExecuteNonQueryAsync());
        }

        async Task AssertStateAsync(int obligations, int openings, int entries)
        {
            await using var command = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM dbo.Receivables WHERE ReceivableId=@ReceivableId),
                  (SELECT COUNT(*) FROM dbo.ReceivableTransactions WHERE ReceivableId=@ReceivableId AND TransactionType=N'Opening'),
                  (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@SourceId),
                  (SELECT AccountingEntryRequired FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@SourceId),
                  (SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@ReceivableId),
                  (SELECT line.AccountId FROM dbo.Receivables balance
                   JOIN dbo.AccountingEntryLines line ON line.EntryId=balance.OpeningEntryId
                     AND line.LineNumber=balance.OpeningEntryLineNumber
                   JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
                     AND entry.SourceDocumentId=balance.SourceDocumentId AND entry.SourceDocumentType=balance.SourceDocumentType
                     AND entry.BusinessId=balance.BusinessId
                   WHERE balance.ReceivableId=@ReceivableId AND line.PartyId=balance.PartyId
                     AND line.Debit=5000 AND entry.TenantId=@TenantId);
                """, connection);
            command.Parameters.AddWithValue("@ReceivableId", claim.ReceivableId);
            command.Parameters.AddWithValue("@SourceId", payload.ReconciliationId);
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(obligations, reader.GetInt32(0));
            Assert.Equal(openings, reader.GetInt32(1));
            Assert.Equal(entries, reader.GetInt32(2));
            Assert.Equal(accountingRequired, reader.GetBoolean(3));
            if (obligations > 0) Assert.Equal(legacyOpening ? 3_000m : 5_000m, reader.GetDecimal(4));
            if (entries > 0) Assert.Contains(reader.GetGuid(5), activeAccounts);
            else Assert.True(reader.IsDBNull(5));
        }
    }
}
