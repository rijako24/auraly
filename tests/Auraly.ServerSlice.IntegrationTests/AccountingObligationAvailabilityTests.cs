using System.Data;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Application.Payables;
using Auraly.Application.Receivables;
using Auraly.BuildingBlocks.Domain.Payments;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;
using Auraly.Domain.Payables;
using Auraly.Domain.Receivables;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AccountingObligationAvailabilityCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Auraly accounting obligation availability performance";
}

[Collection(AccountingObligationAvailabilityCollection.Name)]
[Trait("Performance", "AccountingObligationAvailability")]
public sealed class AccountingObligationAvailabilityTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Availability_and_pages_are_scoped_and_bounded_with_twenty_one_thousand_sources()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        var now = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        Guid accountId;
        await using (var account = new SqlCommand("SELECT AccountId FROM dbo.AccountingAccounts WHERE TenantId=@TenantId AND Code=N'519595'", connection, transaction))
        {
            account.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            accountId = (Guid)(await account.ExecuteScalarAsync())!;
        }
        var selected = Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToArray();
        // Unrelated history must not increase the number of payloads read by a
        // position lookup. This collection owns a disposable database, including
        // its synthetic positions and sources, which are never processed.
        var positions = selected.Concat(Enumerable.Range(0, 10_000).Select(_ => Guid.NewGuid())).ToArray();
        var sources = positions.SelectMany(id => new[] { "Payable", "Receivable" }.Select(kind =>
        {
            var documentId = Guid.NewGuid();
            var request = new ConfirmAccountAdjustmentRequest(documentId, fixture.BusinessId, kind, id,
                "Decrease", 2m, accountId, null, now, "ACCOUNT_ADJUSTMENT", "Disponibilidad de prueba");
            return new SqlAccountingPostingJobWriter.Source(fixture.TenantId, fixture.BusinessId, documentId,
                "AccountAdjustment", JsonSerializer.Serialize(request), now, Guid.NewGuid());
        })).ToArray();
        foreach (var batch in sources.Chunk(100))
            await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction, batch, now,
                AccountingJobRequirement.ManualBookEntry, CancellationToken.None);

        const string sql = """
            SELECT input.Id,reserved.PendingPaymentAmount,reserved.PendingAdjustmentAmount,reserved.AvailableAmount
            FROM OPENJSON(@Positions) WITH(Id uniqueidentifier '$') input
            CROSS APPLY accounting.ObligationAvailability(@BusinessId,@Kind,input.Id,5000000,@ExcludedId) reserved;
            """;
        connection.StatisticsEnabled = true;
        foreach (var kind in new[] { "Payable", "Receivable" })
        {
            // Warm up outside timing. The remaining samples run without builds,
            // frontend or diagnostic queries competing with the measured path.
            await ReadAsync(selected.Take(1), kind, fixture.BusinessId);
            foreach (var count in new[] { 1, 50, 500 })
            {
                connection.ResetStatistics();
                var watch = Stopwatch.StartNew();
                Assert.Equal(count * 2m, await ReadAsync(selected.Take(count), kind, fixture.BusinessId));
                watch.Stop();
                var trips = (long)connection.RetrieveStatistics()["ServerRoundtrips"]!;
                output.WriteLine($"{kind}: {count} posiciones, {trips} SQL, {watch.Elapsed.TotalMilliseconds:F1} ms");
                Assert.Equal(1, trips);
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"{count} posiciones: {watch.Elapsed.TotalMilliseconds:F1} ms");
            }
        }
        Assert.Equal(0m, await ReadAsync(selected, "Payable", Guid.NewGuid()));
        Assert.Equal(0m, await ReadAsync(selected.Take(1), "Payable", fixture.BusinessId, sources[0].DocumentId));

        await using (var plan = Command("SET STATISTICS XML ON;\n" + sql + "\nSET STATISTICS XML OFF;", selected, "Payable", fixture.BusinessId))
        {
            await using var reader = await plan.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { }
            Assert.True(await reader.NextResultAsync());
            Assert.True(await reader.ReadAsync());
            var xml = XDocument.Parse(reader.GetString(0));
            XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
            Assert.Contains(xml.Descendants(ns + "RelOp"), operation =>
                (string?)operation.Attribute("PhysicalOp") == "Index Seek" &&
                operation.Descendants(ns + "Object").Any(value =>
                    (string?)value.Attribute("Index") == "[IX_AccountingSourceDocuments_AdjustmentObligation]"));
        }
        // Keep the same representative history for the real paged reader. These
        // synthetic obligation headers only exist in this collection's database.
        await using (var headers = new SqlCommand("""
            DECLARE @Positions TABLE(Id uniqueidentifier,SourceId uniqueidentifier,Kind nvarchar(16),Number int);
            INSERT @Positions SELECT Id,SourceId,Kind,Number FROM OPENJSON(@HeadersJson)
                WITH(Id uniqueidentifier,SourceId uniqueidentifier,Kind nvarchar(16),Number int);
            INSERT dbo.Payables(PayableId,BusinessId,SupplierId,SourceDocumentId,SourceDocumentType,
                DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
            SELECT Id,@BusinessId,@SupplierId,SourceId,N'AccountAdjustment',CONCAT(N'OPEN-PERF-',Number),
                N'COP',5,5,DATEADD(minute,Number,@At),N'Open',@At FROM @Positions WHERE Kind=N'Payable';
            INSERT dbo.Receivables(ReceivableId,BusinessId,PartyId,SourceDocumentId,SourceDocumentType,
                DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
            SELECT Id,@BusinessId,@PartyId,SourceId,N'AccountAdjustment',CONCAT(N'OPEN-PERF-',Number),
                N'COP',5,5,DATEADD(minute,Number,@At),N'Open',@At FROM @Positions WHERE Kind=N'Receivable';
            """, connection, transaction))
        {
            headers.Parameters.Add("@HeadersJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
                sources.Select((source, index) => new { Id = positions[index / 2], SourceId = source.DocumentId,
                    Kind = index % 2 == 0 ? "Payable" : "Receivable", Number = index / 2 }));
            headers.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            headers.Parameters.AddWithValue("@SupplierId", fixture.SupplierId);
            headers.Parameters.AddWithValue("@PartyId", fixture.SupplierPartyId);
            headers.Parameters.AddWithValue("@At", now);
            await headers.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read });
        foreach (var kind in new[] { "Payable", "Receivable" })
        {
            var first = new AccountingOpenPositionQuery(kind, 1, 50, Search: "OPEN-PERF-");
            await service.OpenPositionsAsync(user, first);
            var scenarios = new[] { ("primera", first, 50), ("siguiente", first with { Page = 2 }, 50),
                ("tercero", first with { PartyId = fixture.SupplierPartyId }, 50),
                ("sin tercero", first with { PartyId = Guid.NewGuid() }, 0),
                ("página vacía", first with { Page = 500 }, 0) };
            foreach (var (name, query, expectedItems) in scenarios)
            {
                var samples = new double[20];
                for (var attempt = 0; attempt < samples.Length; attempt++)
                {
                    using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
                    var watch = Stopwatch.StartNew();
                    var page = await service.OpenPositionsAsync(user, query);
                    watch.Stop();
                    samples[attempt] = watch.Elapsed.TotalMilliseconds;
                    Assert.Equal(1, counter.Count);
                    Assert.Equal(expectedItems, page.Items.Count);
                    var expectedCount = name == "sin tercero" ? 0 : positions.Length;
                    Assert.Equal(expectedCount, page.TotalCount);
                    Assert.Equal(expectedCount * 5m, page.TotalOutstandingAmount);
                    Assert.All(page.Items, item =>
                    {
                        Assert.Equal(fixture.SupplierPartyId, item.PartyId);
                        Assert.Equal(5m, item.OutstandingAmount);
                        Assert.Equal(2m, item.PendingAdjustmentAmount);
                        Assert.Equal(3m, item.AvailableAmount);
                        Assert.Null(item.AccountId);
                        Assert.NotNull(item.OpeningLinkIssue);
                    });
                }
                var p95 = samples.Order().ElementAt(18);
                output.WriteLine($"{kind} {name}: {expectedItems} filas, 1 SQL, primera {samples[0]:F1} ms, p95 {p95:F1} ms");
                Assert.True(p95 < 750, $"{kind} {name}: p95 {p95:F1} ms supera 750 ms");
            }
            Assert.Empty((await service.OpenPositionsAsync(user with { TenantId = Guid.NewGuid() }, first)).Items);
            Assert.Empty((await service.OpenPositionsAsync(user with { BusinessId = Guid.NewGuid() }, first)).Items);
        }

        var mixed = new AccountingOpenPositionQuery("All", 1, 50, PartyId: fixture.SupplierPartyId, Search: "OPEN-PERF-");
        await service.OpenPositionsAsync(user, mixed);
        foreach (var pageNumber in new[] { 1, 2 })
        {
            var samples = new double[20];
            for (var attempt = 0; attempt < samples.Length; attempt++)
            {
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
                var watch = Stopwatch.StartNew();
                var page = await service.OpenPositionsAsync(user, mixed with { Page = pageNumber });
                watch.Stop();
                samples[attempt] = watch.Elapsed.TotalMilliseconds;
                Assert.Equal(1, counter.Count);
                Assert.Equal(50, page.Items.Count);
                Assert.Equal(positions.Length * 2, page.TotalCount);
                Assert.Equal(positions.Length * 10m, page.TotalOutstandingAmount);
                Assert.All(page.Items, item => Assert.Equal(fixture.SupplierPartyId, item.PartyId));
            }
            var p95 = samples.Order().ElementAt(18);
            output.WriteLine($"All página {pageNumber}: 50 filas, 1 SQL, primera {samples[0]:F1} ms, p95 {p95:F1} ms");
            Assert.True(p95 < 2_000, $"All página {pageNumber}: p95 {p95:F1} ms supera 2 segundos");
        }

        await VerifyNativePaymentAcceptanceAsync(connection, selected.Take(100).ToArray(), now);

        SqlCommand Command(string text, IEnumerable<Guid> positions, string kind, Guid businessId, Guid? excludedId = null)
        {
            var command = new SqlCommand(text, connection, transaction);
            command.Parameters.Add("@Positions", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(positions);
            command.Parameters.AddWithValue("@BusinessId", businessId);
            command.Parameters.AddWithValue("@Kind", kind);
            command.Parameters.Add("@ExcludedId", SqlDbType.UniqueIdentifier).Value = (object?)excludedId ?? DBNull.Value;
            return command;
        }

        async Task<decimal> ReadAsync(IEnumerable<Guid> positions, string kind, Guid businessId, Guid? excludedId = null)
        {
            await using var command = Command(sql, positions, kind, businessId, excludedId);
            await using var reader = await command.ExecuteReaderAsync();
            decimal total = 0;
            while (await reader.ReadAsync())
            {
                Assert.Equal(0m, reader.GetDecimal(1));
                Assert.Equal(5000000m-reader.GetDecimal(2), reader.GetDecimal(3));
                total += reader.GetDecimal(2);
            }
            return total;
        }
    }

    private async Task VerifyNativePaymentAcceptanceAsync(SqlConnection connection, Guid[] positions, DateTimeOffset now)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        Guid customerId;
        await using (var setup = new SqlCommand("""
            DECLARE @CustomerId uniqueidentifier=(SELECT CustomerId FROM dbo.Customers
              WHERE TenantId=@TenantId AND PartyId=@PartyId);
            IF @CustomerId IS NULL
            BEGIN
              SET @CustomerId=NEWID();
              INSERT dbo.Customers(CustomerId,PartyId,TenantId,IsActive,CreatedBy,CreatedAt)
              VALUES(@CustomerId,@PartyId,@TenantId,1,@UserId,@At);
            END;
            DECLARE @SiteId uniqueidentifier=(SELECT PartySiteId FROM dbo.PartySites
              WHERE PartyId=@PartyId AND IsPrimary=1 AND IsActive=1);
            IF @SiteId IS NULL THROW 51999,'The payment benchmark requires the seeded primary site.',1;
            DECLARE @Selected TABLE(Id uniqueidentifier PRIMARY KEY);
            INSERT @Selected SELECT Id FROM OPENJSON(@Positions) WITH(Id uniqueidentifier '$');
            UPDATE p SET PartySiteId=@SiteId FROM dbo.Payables p JOIN @Selected s ON s.Id=p.PayableId;
            UPDATE r SET PartyId=NULL,CustomerId=@CustomerId,PartySiteId=@SiteId
              FROM dbo.Receivables r JOIN @Selected s ON s.Id=r.ReceivableId;
            SELECT @CustomerId;
            """, connection, transaction))
        {
            setup.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            setup.Parameters.AddWithValue("@PartyId", fixture.SupplierPartyId);
            setup.Parameters.AddWithValue("@UserId", fixture.UserId);
            setup.Parameters.AddWithValue("@At", now);
            setup.Parameters.Add("@Positions", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(positions);
            customerId = (Guid)(await setup.ExecuteScalarAsync())!;
        }
        // Existing 21,000 sources remain in this database. Only the selected
        // 1/100 positions participate; each has a pending adjustment of two.
        foreach (var kind in new[] { "Payable", "Receivable" })
        foreach (var count in new[] { 1, 100 })
        {
            var number = await SqlOperationalDocumentAllocator.AllocateNumberAsync(connection, transaction,
                fixture.BusinessId, kind == "Payable" ? PayablesDocumentTypes.Payment : ReceivablesDocumentTypes.Payment, now, default);
            var id = Guid.NewGuid();
            var selected = positions.Take(count).ToArray();
            var payload = kind == "Payable"
                ? SupplierPaymentContractSerializer.Serialize(new(fixture.TenantId, fixture.BusinessId, id, fixture.SupplierId,
                    fixture.UserId, number.FullNumber, number.SeriesId, number.Prefix, number.SeriesCode, number.Consecutive,
                    now, "COP", null, count, selected.Select((position, index) =>
                        new SupplierPaymentAllocationSnapshot(index + 1, position, 1m)).ToArray(), [new(1, "Cash", count, count)]))
                : CustomerPaymentContractSerializer.Serialize(new(fixture.TenantId, fixture.BusinessId, id, customerId,
                    fixture.UserId, null, number.FullNumber, number.SeriesId, number.Prefix, number.SeriesCode, number.Consecutive,
                    now, "COP", null, count, selected.Select((position, index) =>
                        new CustomerPaymentAllocationSnapshot(index + 1, position, 1m)).ToArray(), [new(1, "Cash", count, count)]));
            transaction.Save("BeforePayment");
            await AcceptAsync();
            transaction.Rollback("BeforePayment");
            transaction.Save("BeforePayment");
            connection.ResetStatistics();
            var watch = Stopwatch.StartNew();
            await AcceptAsync();
            watch.Stop();
            var trips = (long)connection.RetrieveStatistics()["ServerRoundtrips"]!;
            output.WriteLine($"{kind} aceptación {count}: {trips} SQL, {watch.Elapsed.TotalMilliseconds:F1} ms");
            Assert.Equal(1, trips);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"{kind} {count}: {watch.Elapsed.TotalMilliseconds:F1} ms");
            await using (var state = new SqlCommand("""
                SELECT COUNT(*),SUM(pending.AvailableAmount)
                FROM OPENJSON(@Positions) WITH(Id uniqueidentifier '$') input
                CROSS APPLY accounting.ObligationAvailability(@BusinessId,@Kind,input.Id,5,NULL) pending;
                """, connection, transaction))
            {
                state.Parameters.Add("@Positions", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(selected);
                state.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                state.Parameters.AddWithValue("@Kind", kind);
                await using var reader = await state.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(count, reader.GetInt32(0));
                Assert.Equal(count * 2m, reader.GetDecimal(1));
            }
            transaction.Rollback("BeforePayment");

            async Task AcceptAsync()
            {
                await using var command = new SqlCommand("accounting.PortfolioPaymentAccept", connection, transaction)
                { CommandType = CommandType.StoredProcedure };
                command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                command.Parameters.AddWithValue("@UserId", fixture.UserId);
                command.Parameters.AddWithValue("@SubledgerKind", kind);
                command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = payload;
                command.Parameters.AddWithValue("@IdempotencyKey", id.ToString("N"));
                command.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload));
                command.Parameters.AddWithValue("@AcceptedAt", now);
                await command.ExecuteNonQueryAsync();
            }
        }
        // Only the role/site preconditions and numbering remain: each measured
        // participant payment was rolled back to its savepoint above.
        await transaction.CommitAsync();
        await VerifyNativeStoreAcceptanceAsync(connection.Database, positions, customerId, now);
    }

    private async Task VerifyNativeStoreAcceptanceAsync(string database, Guid[] positions,
        Guid customerId, DateTimeOffset now)
    {
        using var scope = fixture.CreateScope();
        var payables = scope.ServiceProvider.GetRequiredService<IPayablesStore>();
        var receivables = scope.ServiceProvider.GetRequiredService<IReceivablesStore>();
        var payableUser = new PayablesUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string>());
        var receivableUser = new ReceivablesUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string>());
        foreach (var kind in new[] { "Payable", "Receivable" })
        foreach (var count in new[] { 1, 100 })
        {
            var total = count * 0.25m;
            var selected = positions.Take(count).ToArray();
            var tenders = PaymentTenderBreakdown.Create([new("Cash", total, total)], total, count,
                new HashSet<string> { "Cash" });
            // Real acceptance stays pending in this disposable fixture. A quarter
            // per application leaves enough of the original available three for
            // both warm-up and measured commands, without altering the gate data.
            await AcceptAsync(Guid.NewGuid(), false);
            var id = Guid.NewGuid();
            using (var counter = new InventoryBalanceProcessingTests.CommandCounter(database))
            {
                var watch = Stopwatch.StartNew();
                await AcceptAsync(id, false);
                watch.Stop();
                output.WriteLine($"{kind} store {count}: {counter.Count} SQL, {watch.Elapsed.TotalMilliseconds:F1} ms");
                Assert.Equal(4, counter.Count);
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"{kind} store {count}: {watch.Elapsed.TotalMilliseconds:F1} ms");
            }
            using (var counter = new InventoryBalanceProcessingTests.CommandCounter(database))
            {
                await AcceptAsync(id, true);
                Assert.Equal(1, counter.Count);
            }

            async Task AcceptAsync(Guid paymentId, bool replay)
            {
                if (kind == "Payable")
                {
                    var request = new ConfirmSupplierPaymentRequest(paymentId, fixture.BusinessId, fixture.SupplierId,
                        now, "COP", null, selected.Select(position => new SupplierPaymentAllocationRequest(position, 0.25m)).ToArray(),
                        [new("Cash", total, total)]);
                    var settlement = PayableSettlement.Create(selected.Select(position => new PayableAllocation(position, 0.25m)));
                    var result = await payables.AcceptPaymentAsync(payableUser, paymentId.ToString("N"), request,
                        settlement, tenders, default);
                    Assert.Equal(replay, result.IdempotentReplay);
                    Assert.Equal("Accepted", result.Status);
                }
                else
                {
                    var request = new ConfirmCustomerPaymentRequest(paymentId, fixture.BusinessId, customerId,
                        null, now, "COP", null, selected.Select(position => new CustomerPaymentAllocationRequest(position, 0.25m)).ToArray(),
                        [new("Cash", total, total)]);
                    var settlement = ReceivableSettlement.Create(selected.Select(position => new ReceivableAllocation(position, 0.25m)));
                    var result = await receivables.AcceptPaymentAsync(receivableUser, paymentId.ToString("N"), request,
                        settlement, tenders, default);
                    Assert.Equal(replay, result.IdempotentReplay);
                    Assert.Equal("Accepted", result.Status);
                }
            }
        }
    }
}
