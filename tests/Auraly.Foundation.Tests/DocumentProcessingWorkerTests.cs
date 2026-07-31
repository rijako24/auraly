using System.Collections.Concurrent;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;

namespace Auraly.Foundation.Tests;

public sealed class DocumentProcessingWorkerTests
{
    [Fact]
    public async Task Failure_in_one_business_does_not_stop_another_business_in_the_same_pass()
    {
        var first = CreateDocument();
        var second = CreateDocument();
        var handler = new SelectiveHandler(first.DocumentId);
        var engine = new DocumentProcessingEngine(new ReceiptStore(), [handler]);
        var worker = new DocumentProcessingWorker(
            new WorkSource([first, second]),
            engine);

        var attempted = await worker.RunOnceAsync();

        Assert.Equal(2, attempted);
        Assert.Equal([second.DocumentId], handler.Completed);
    }

    private static ConfirmedDocument CreateDocument() =>
        new(
            new TenantId(Guid.NewGuid()),
            new BusinessId(Guid.NewGuid()),
            new DocumentId(Guid.NewGuid()),
            SelectiveHandler.Type,
            "{}",
            DateTimeOffset.UtcNow);

    private sealed class WorkSource(IReadOnlyList<ConfirmedDocument> documents)
        : IDocumentProcessingWorkSource
    {
        public Task<IReadOnlyList<ConfirmedDocument>> LoadReadyAsync(
            int maximumCount,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ConfirmedDocument>>(
                documents.Take(maximumCount).ToArray());
    }

    private sealed class SelectiveHandler(DocumentId failingDocumentId)
        : IConfirmedDocumentHandler
    {
        public const string Type = "sales.invoice";
        public List<DocumentId> Completed { get; } = [];
        string IConfirmedDocumentHandler.DocumentType => Type;

        public Task HandleAsync(
            ConfirmedDocument document,
            CancellationToken cancellationToken)
        {
            if (document.DocumentId == failingDocumentId)
                throw new InvalidOperationException("Expected test failure.");

            Completed.Add(document.DocumentId);
            return Task.CompletedTask;
        }
    }

    private sealed class ReceiptStore : IDocumentProcessingReceiptStore
    {
        private readonly ConcurrentDictionary<DocumentId, string> _states = new();

        public Task<ProcessingLeaseResult> TryAcquireAsync(
            DocumentProcessingContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                _states.TryAdd(context.DocumentId, "Processing")
                    ? ProcessingLeaseResult.Acquired
                    : ProcessingLeaseResult.Busy);

        public Task MarkCompletedAsync(
            DocumentProcessingContext context,
            CancellationToken cancellationToken)
        {
            _states[context.DocumentId] = "Completed";
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            DocumentProcessingContext context,
            Exception error,
            CancellationToken cancellationToken)
        {
            _states[context.DocumentId] = "Failed";
            return Task.CompletedTask;
        }
    }
}
