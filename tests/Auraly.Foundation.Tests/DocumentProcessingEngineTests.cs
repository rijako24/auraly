using System.Collections.Concurrent;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;

namespace Auraly.Foundation.Tests;

public sealed class DocumentProcessingEngineTests
{
    [Fact]
    public async Task Duplicate_document_is_handled_once()
    {
        var store = new InMemoryReceiptStore();
        var handler = new CountingHandler();
        var engine = new DocumentProcessingEngine(store, [handler]);
        var document = new ConfirmedDocument(
            new TenantId(Guid.NewGuid()),
            new BusinessId(Guid.NewGuid()),
            new DocumentId(Guid.NewGuid()),
            CountingHandler.Type,
            "{}",
            DateTimeOffset.UtcNow);

        var first = await engine.ProcessAsync(document);
        var duplicate = await engine.ProcessAsync(document);

        Assert.Equal(DocumentProcessingResult.Processed, first);
        Assert.Equal(DocumentProcessingResult.AlreadyProcessed, duplicate);
        Assert.Equal(1, handler.Count);
    }

    private sealed class CountingHandler : IConfirmedDocumentHandler
    {
        public const string Type = "sales.invoice";
        public int Count { get; private set; }
        string IConfirmedDocumentHandler.DocumentType => Type;

        public Task HandleAsync(ConfirmedDocument document, CancellationToken cancellationToken)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryReceiptStore : IDocumentProcessingReceiptStore
    {
        private readonly ConcurrentDictionary<DocumentId, string> _states = new();

        public Task<ProcessingLeaseResult> TryAcquireAsync(
            DocumentProcessingContext context,
            CancellationToken cancellationToken)
        {
            if (_states.TryAdd(context.DocumentId, "processing"))
            {
                return Task.FromResult(ProcessingLeaseResult.Acquired);
            }

            return Task.FromResult(
                _states[context.DocumentId] == "completed"
                    ? ProcessingLeaseResult.AlreadyCompleted
                    : ProcessingLeaseResult.Busy);
        }

        public Task MarkCompletedAsync(
            DocumentProcessingContext context,
            CancellationToken cancellationToken)
        {
            _states[context.DocumentId] = "completed";
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            DocumentProcessingContext context,
            Exception error,
            CancellationToken cancellationToken)
        {
            _states.TryRemove(context.DocumentId, out _);
            return Task.CompletedTask;
        }
    }
}
