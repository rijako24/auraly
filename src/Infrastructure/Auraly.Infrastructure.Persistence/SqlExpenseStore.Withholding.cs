using Auraly.Contracts.Expenses;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlExpenseStore
{
    internal static async Task PersistWithholdingAsync(SqlConnection connection, SqlTransaction transaction,
        IReadOnlyList<ExpenseDocumentPayload> expenses, CancellationToken ct)
    {
        if (expenses.Count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(expenses));
        foreach (var expense in expenses)
            if (expense.Withholding.GrossAmount != expense.GrossAmount ||
                expense.Withholding.WithholdingTotal != expense.Withholding.Lines.Sum(line => line.Amount) ||
                expense.Withholding.NetAmount + expense.Withholding.WithholdingTotal != expense.GrossAmount)
                throw new InvalidOperationException("The immutable expense withholding snapshot does not reconcile.");
        if (expenses.Any(expense => expense.BusinessId != expenses[0].BusinessId))
            throw new InvalidOperationException("The expense tax snapshot batch must belong to one business.");
        await SqlWithholdingRuleStore.InsertDocumentSnapshotsAsync(connection, transaction, expenses[0].BusinessId,
            expenses.Select(expense => new SqlWithholdingRuleStore.DocumentSnapshotSource(expense.ExpenseId,
                ExpenseDocumentTypes.Expense, expense.IssuedAt, expense.Withholding)).ToArray(), ct);
    }
}
