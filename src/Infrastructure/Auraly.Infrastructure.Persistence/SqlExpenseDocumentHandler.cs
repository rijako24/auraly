using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Expenses;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlExpenseDocumentHandler(SqlDocumentProcessingSessionAccessor sessions,
    IAuralyIdGenerator ids, TimeProvider timeProvider) : IConfirmedDocumentHandler
{
    public string DocumentType => ExpenseDocumentTypes.Expense;

    public async Task HandleAsync(ConfirmedDocument document, CancellationToken ct)
    {
        var expense = ExpenseContractSerializer.Deserialize(document.Payload);
        if (expense.ExpenseId != document.DocumentId.Value || expense.BusinessId != document.BusinessId.Value || expense.TenantId != document.TenantId.Value)
            throw new InvalidOperationException("The expense envelope does not match its payload.");
        // Compatibility bridge for operational expense jobs accepted before the
        // direct financial cutover. New expenses never enter this handler.
        var session = sessions.Current;
        await SqlExpenseStore.PersistWithholdingAsync(session.Connection, session.Transaction, [expense], ct);
        await SqlAccountingPostingJobWriter.InsertAsync(session, document, expense.IssuedAt,
            ids, timeProvider, ct, AccountingJobRequirement.PreserveCommercialEffects);
    }
}
