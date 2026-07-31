using Auraly.Contracts.DocumentProcessing;

namespace Auraly.Application.DocumentProcessing;

public interface IDocumentProcessingWorkSource
{
    Task<IReadOnlyList<ConfirmedDocument>> LoadReadyAsync(
        int maximumCount,
        CancellationToken cancellationToken);
}

public sealed class DocumentProcessingWorker(
    IDocumentProcessingWorkSource workSource,
    DocumentProcessingEngine engine)
{
    public async Task<int> RunOnceAsync(
        int maximumCount = 20,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));

        var documents = await workSource.LoadReadyAsync(maximumCount, cancellationToken);
        var attempted = 0;
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await engine.ProcessAsync(document, cancellationToken);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                // The durable job contains the classified failure and retry schedule.
                // Another business must still be allowed to advance in this worker pass.
            }

            attempted++;
        }

        return attempted;
    }
}

