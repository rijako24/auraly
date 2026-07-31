using Auraly.Application.DocumentProcessing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlDocumentProcessingSessionAccessor
{
    private Session? _current;

    internal Session Current =>
        _current ?? throw new InvalidOperationException("No SQL document-processing session is active.");

    internal void Set(
        SqlConnection connection,
        SqlTransaction transaction,
        DocumentProcessingContext context,
        Guid receiptId,
        Guid jobId,
        long processingSequence)
    {
        if (_current is not null)
        {
            throw new InvalidOperationException("A SQL document-processing session is already active.");
        }

        _current = new Session(
            connection,
            transaction,
            context,
            receiptId,
            jobId,
            processingSequence);
    }

    internal Session Take()
    {
        var current = Current;
        _current = null;
        return current;
    }

    internal sealed record Session(
        SqlConnection Connection,
        SqlTransaction Transaction,
        DocumentProcessingContext Context,
        Guid ReceiptId,
        Guid JobId,
        long ProcessingSequence);
}

