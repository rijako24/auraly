using System.Data;
using System.Text.Json;
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
        await using var command = new SqlCommand("""
            DECLARE @Expenses TABLE(DocumentId uniqueidentifier PRIMARY KEY,BusinessId uniqueidentifier,
              IssuedAt datetimeoffset,Withholding nvarchar(max));
            INSERT @Expenses SELECT ExpenseId,BusinessId,IssuedAt,Withholding FROM OPENJSON(@Payloads) WITH(
              ExpenseId uniqueidentifier,BusinessId uniqueidentifier,IssuedAt datetimeoffset,Withholding nvarchar(max) AS JSON);
            INSERT dbo.DocumentWithholdingSnapshots
              (DocumentId,DocumentType,BusinessId,GrossAmount,WithholdingTotal,NetAmount,RecognizedAt)
            SELECT e.DocumentId,N'Expense',e.BusinessId,w.GrossAmount,w.WithholdingTotal,w.NetAmount,e.IssuedAt
            FROM @Expenses e CROSS APPLY OPENJSON(e.Withholding) WITH
              (GrossAmount decimal(19,4),WithholdingTotal decimal(19,4),NetAmount decimal(19,4)) w;
            INSERT dbo.DocumentWithholdingLines
              (DocumentId,DocumentType,LineNumber,RuleId,RuleVersion,RuleCode,Name,Kind,
               BaseKind,TaxableBase,Rate,Amount,JurisdictionCode)
            SELECT e.DocumentId,N'Expense',CONVERT(int,j.[key])+1,l.RuleId,l.RuleVersion,l.RuleCode,
              l.Name,l.Kind,l.BaseKind,l.TaxableBase,l.Rate,l.Amount,l.JurisdictionCode
            FROM @Expenses e CROSS APPLY OPENJSON(e.Withholding,'$.Lines') j
            CROSS APPLY OPENJSON(j.value) WITH(
              RuleId uniqueidentifier,RuleVersion int,RuleCode nvarchar(64),Name nvarchar(200),
              Kind nvarchar(32),BaseKind nvarchar(32),TaxableBase decimal(19,4),Rate decimal(9,6),
              Amount decimal(19,4),JurisdictionCode nvarchar(16)) l;
            """, connection, transaction);
        command.Parameters.Add("@Payloads", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(expenses);
        var count = await command.ExecuteNonQueryAsync(ct);
        // The table-variable insert also contributes to ExecuteNonQuery's row count.
        if (count != 2 * expenses.Count + expenses.Sum(expense => expense.Withholding.Lines.Count))
            throw new DBConcurrencyException("The expense withholdings were not captured atomically.");
    }
}
