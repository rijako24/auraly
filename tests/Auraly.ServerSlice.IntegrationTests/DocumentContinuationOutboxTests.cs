using System.Net;
using Auraly.Application.DocumentProcessing;
using Auraly.Application.Fiscal;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Commerce.Accounting.Application;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "EndToEnd")]
public sealed class DocumentContinuationOutboxTests(ServerSliceFixture fixture)
{
    private const int AccountingDestinations = 11;

    [Fact]
    public async Task Completion_without_publication_is_recoverable_and_acknowledged_only_by_the_active_lease()
    {
        const string flag = "Auraly:DocumentProcessing:DurableContinuation:Enabled";
        var request = fixture.CreateValidRequest(8_808);
        using var scope = fixture.CreateScope();
        using var client = fixture.CreateClient();
        var services = scope.ServiceProvider;
        var configuration = services.GetRequiredService<IConfiguration>();
        var before = configuration[flag];
        configuration[flag] = "true";
        try
        {
            fixture.PauseDocumentProcessing();
            try
            {
                using var response = await client.SendAsync(fixture.CreateUploadMessage(request));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            finally
            {
                fixture.ResumeDocumentProcessing();
            }

            var signal = Assert.Single(fixture.DrainDocumentSignals(), candidate => candidate.DocumentId == request.DocumentId);
            var jobSource = services.GetRequiredService<IDocumentProcessingWorkSource>();
            var work = await jobSource.LoadAsync(signal, default);
            Assert.Equal(DocumentProcessingWorkState.Ready, work.State);
            var engine = services.GetRequiredService<DocumentProcessingEngine>();
            await engine.ProcessAsync(Assert.IsType<Auraly.Contracts.DocumentProcessing.ConfirmedDocument>(work.Document));
            Assert.Equal(DocumentProcessingWorkState.Completed, (await jobSource.LoadAsync(signal, default)).State);

            var recovery = new SqlDocumentContinuationSignalPublicationStore(
                services.GetRequiredService<SqlServerConnectionFactory>());
            Assert.Empty((await recovery.ClaimAsync(default)).Activations);

            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var expire = new SqlCommand("""
                UPDATE dbo.ServerOutboxMessages SET RecoveryAvailableAt=DATEADD(second,-1,SYSUTCDATETIME())
                WHERE DocumentId=@DocumentId AND RecoveryAvailableAt IS NOT NULL AND ProcessedAt IS NULL;
                """, connection);
            expire.Parameters.AddWithValue("@DocumentId", request.DocumentId);
            Assert.Equal(1, await expire.ExecuteNonQueryAsync());
            var first = await recovery.ClaimAsync(default);
            var delivery = Assert.Single(first.Activations);
            Assert.Equal(signal, delivery.Signal);
            Assert.Empty((await recovery.ClaimAsync(default)).Activations);

            Assert.Equal(1, await expire.ExecuteNonQueryAsync());
            var next = await recovery.ClaimAsync(default);
            Assert.NotEqual(first.LeaseId, next.LeaseId);
            Assert.Equal(delivery, Assert.Single(next.Activations));
            await recovery.MarkPublishedAsync(first.LeaseId, [delivery.OutboxMessageId], default);
            await using var readCount = new SqlCommand("""
                SELECT RecoveryPublicationCount FROM dbo.ServerOutboxMessages WHERE MessageId=@MessageId;
                """, connection);
            readCount.Parameters.AddWithValue("@MessageId", delivery.OutboxMessageId);
            Assert.Equal(0, Convert.ToInt32(await readCount.ExecuteScalarAsync()));
            await recovery.MarkPublishedAsync(next.LeaseId, [delivery.OutboxMessageId], default);
            Assert.Empty((await recovery.ClaimAsync(default)).Activations);

            Assert.Equal(1, await expire.ExecuteNonQueryAsync());
            var lostInBroker = Assert.Single((await recovery.ClaimAsync(default)).Activations);
            Assert.Equal(1, lostInBroker.PublicationCount);
            Assert.Equal(delivery.Signal, lostInBroker.Signal);
            await fixture.DocumentSignals.PublishAsync(lostInBroker.Signal);
            Assert.Equal(DocumentProcessingWorkState.CompletedAndPublished, (await jobSource.LoadAsync(signal, default)).State);
            Assert.Equal(1, await fixture.CountAsync("SalesDocuments", request.DocumentId));
            Assert.Equal(1, await fixture.CountAsync("SalesPayments", request.DocumentId));
            Assert.Equal(0, await expire.ExecuteNonQueryAsync());
        }
        finally
        {
            configuration[flag] = before;
        }
    }

    [Fact]
    public async Task Redelivery_preserves_destination_identity_and_sends_only_unacknowledged_work()
    {
        var request = fixture.CreateValidRequest(8_807);
        using var client = fixture.CreateClient();
        using var response = await client.SendAsync(fixture.CreateUploadMessage(request));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.CreateScope();
        var services = scope.ServiceProvider;
        var source = services.GetRequiredService<IDocumentProcessingWorkSource>();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.CommandText = """
            UPDATE dbo.ServerOutboxMessages SET ProcessedAt=NULL
            WHERE DocumentId=@DocumentId AND Type=N'sales.document.processed';
            """;
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        var signal = Assert.Single(fixture.DrainDocumentSignals().Distinct(),
            candidate => candidate.DocumentId == request.DocumentId);

        command.CommandText = """
            INSERT dbo.ServerOutboxMessages(MessageId,DocumentId,DocumentType,Type,Payload,OccurredAt)
            SELECT NEWID(),DocumentId,DocumentType,N'FiscalDocument.DianAccepted',N'{}',SYSUTCDATETIME()
            FROM dbo.DocumentProcessingJobs WHERE DocumentId=@DocumentId
              AND NOT EXISTS (SELECT 1 FROM dbo.ServerOutboxMessages
                  WHERE DocumentId=@DocumentId AND Type=N'FiscalDocument.DianAccepted');
            """;
        await command.ExecuteNonQueryAsync();

        var transport = new InterruptedTransport();
        var ids = services.GetRequiredService<IAuralyIdGenerator>();
        var gate = new CountingGate();
        var accounting = new AccountingProcessingCoordinator(transport, ids, gate);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auraly:DocumentProcessing:DurableContinuation:Enabled"] = "true"
        }).Build();
        SqlDocumentProcessingCompletionObserver Observer() => new(
            services.GetRequiredService<SqlServerConnectionFactory>(), transport, accounting, transport, transport,
            services.GetRequiredService<SqlDocumentContinuationOutbox>(), configuration, ids,
            NullLogger<SqlDocumentProcessingCompletionObserver>.Instance, TimeProvider.System);

        await Observer().ObserveAsync(signal with { BusinessId = Guid.NewGuid() }, default);
        Assert.Empty(transport.Fiscal);
        Assert.Equal(0, gate.Calls);

        using var sqlCalls = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter: sql => sql.CommandText.Contains("doc.send.", StringComparison.Ordinal));
        using var stopped = new CancellationTokenSource();
        transport.StopAfterFiscal = stopped.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Observer().ObserveAsync(signal, stopped.Token));
        Assert.Equal(2, sqlCalls.Count);
        var fiscalBefore = Assert.Single(transport.Fiscal);
        Assert.Empty(transport.Accounting);

        transport.StopAfterFiscal = null;
        await Assert.ThrowsAsync<IOException>(() => Observer().ObserveAsync(signal, default));
        Assert.Equal(5, sqlCalls.Count);
        var accountingBefore = Assert.Single(transport.Accounting);
        Assert.Equal(new[] { fiscalBefore, fiscalBefore }, transport.Fiscal);
        Assert.Empty(transport.Reporting);
        command.CommandText = """
            SELECT COUNT(*) FROM dbo.ServerOutboxMessages
            WHERE DocumentId=@DocumentId AND Type LIKE 'doc.send.%'
              AND ProcessedAt IS NULL;
            """;
        Assert.Equal(AccountingDestinations + 1, Convert.ToInt32(await command.ExecuteScalarAsync()));

        var callsBeforeReplay = sqlCalls.Count;
        await Observer().ObserveAsync(signal, default);
        Assert.Equal(2, sqlCalls.Count - callsBeforeReplay);
        Assert.Equal(2, transport.Fiscal.Count);
        Assert.Equal(accountingBefore, transport.Accounting[1]);
        Assert.Single(transport.Reporting);
        Assert.Equal(1, gate.Calls);
        Assert.Equal(DocumentProcessingWorkState.CompletedAndPublished, (await source.LoadAsync(signal, default)).State);

        // Even a direct replay after the job has drained preserves the original plan.
        await Observer().ObserveAsync(signal, default);
        Assert.Equal(AccountingDestinations + 1, transport.Accounting.Count);
        Assert.Single(transport.Reporting);
        command.CommandText = """
            SELECT COUNT(*) FROM dbo.ServerOutboxMessages
            WHERE DocumentId=@DocumentId AND Type=N'FiscalDocument.DianAccepted' AND ProcessedAt IS NULL;
            """;
        Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    private sealed class CountingGate : IAccountingProcessingSignalGate
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<AccountingPendingWork>> ListPendingWorkAsync(
            Guid businessId, Guid documentId, string documentType, CancellationToken cancellationToken = default,
            bool includeDependencies = true)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<AccountingPendingWork>>(
                [new(documentId, documentType),
                    .. Enumerable.Range(1, AccountingDestinations - 1)
                        .Select(_ => new AccountingPendingWork(Guid.NewGuid(), "Expense"))]);
        }
    }

    private sealed class InterruptedTransport :
        IFiscalProcessingSignalPublisher, IAccountingProcessingSignalPublisher, ISalesReportingProcessingSignalPublisher
    {
        public List<FiscalProcessingSignal> Fiscal { get; } = [];
        public List<AccountingProcessingSignal> Accounting { get; } = [];
        public List<SalesReportingProcessingSignal> Reporting { get; } = [];
        public Action? StopAfterFiscal { get; set; }

        public Task PublishAsync(FiscalProcessingSignal signal, DateTimeOffset? scheduledEnqueueTime,
            CancellationToken cancellationToken)
        {
            Fiscal.Add(signal);
            StopAfterFiscal?.Invoke();
            return Task.CompletedTask;
        }

        public Task PublishAsync(AccountingProcessingSignal signal, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Accounting.Add(signal);
            return Accounting.Count == 1
                ? Task.FromException(new IOException("Se perdió la respuesta del primer envío contable."))
                : Task.CompletedTask;
        }

        public Task PublishAsync(SalesReportingProcessingSignal signal, CancellationToken cancellationToken)
        {
            Reporting.Add(signal);
            return Task.CompletedTask;
        }
    }
}
