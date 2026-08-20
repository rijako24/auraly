using System.Data;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Expenses;
using Auraly.Domain.Payables;
using Microsoft.Data.SqlClient;

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
        if (expense.Withholding.GrossAmount != expense.GrossAmount || expense.Withholding.WithholdingTotal != expense.Withholding.Lines.Sum(x => x.Amount) || expense.Withholding.NetAmount + expense.Withholding.WithholdingTotal != expense.GrossAmount)
            throw new InvalidOperationException("The immutable expense withholding snapshot does not reconcile.");
        var session = sessions.Current;
        await PersistWithholdingAsync(session, expense, ct);
        if (expense.Withholding.NetAmount > 0) await OpenPayableAsync(session, expense, ct);
        await SqlAccountingPostingJobWriter.InsertAsync(session, document, expense.IssuedAt, ids, timeProvider, ct);
        await using var command = new SqlCommand("""
            UPDATE dbo.Expenses SET Status=N'Processed',ProcessedAt=@Now
              WHERE ExpenseId=@Id AND BusinessId=@BusinessId AND Status=N'Accepted';
            INSERT dbo.ServerOutboxMessages(MessageId,DocumentId,DocumentType,Type,Payload,OccurredAt)
              VALUES(@MessageId,@Id,N'Expense',N'expenses.expense.processed',@Payload,@Now);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@Id", expense.ExpenseId); command.Parameters.AddWithValue("@BusinessId", expense.BusinessId);
        command.Parameters.AddWithValue("@MessageId", ids.NewId()); command.Parameters.AddWithValue("@Payload", document.Payload);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        if (await command.ExecuteNonQueryAsync(ct) != 2) throw new DBConcurrencyException("The expense could not be completed.");
    }

    private static async Task PersistWithholdingAsync(SqlDocumentProcessingSessionAccessor.Session s, ExpenseDocumentPayload e, CancellationToken ct)
    {
        await using (var command = new SqlCommand("""
            INSERT dbo.DocumentWithholdingSnapshots(DocumentId,DocumentType,BusinessId,GrossAmount,WithholdingTotal,NetAmount,RecognizedAt)
              VALUES(@Id,N'Expense',@BusinessId,@Gross,@Held,@Net,@At);
            """, s.Connection, s.Transaction))
        { command.Parameters.AddWithValue("@Id", e.ExpenseId); command.Parameters.AddWithValue("@BusinessId", e.BusinessId); Money(command, "@Gross", e.Withholding.GrossAmount); Money(command, "@Held", e.Withholding.WithholdingTotal); Money(command, "@Net", e.Withholding.NetAmount); command.Parameters.AddWithValue("@At", e.IssuedAt); await command.ExecuteNonQueryAsync(ct); }
        for (var i = 0; i < e.Withholding.Lines.Count; i++)
        { var line = e.Withholding.Lines[i]; await using var command = new SqlCommand("""
            INSERT dbo.DocumentWithholdingLines(DocumentId,DocumentType,LineNumber,RuleId,RuleVersion,RuleCode,Name,Kind,BaseKind,TaxableBase,Rate,Amount,JurisdictionCode)
              VALUES(@Id,N'Expense',@Line,@RuleId,@Version,@Code,@Name,@Kind,@BaseKind,@Base,@Rate,@Amount,@Jurisdiction);
            """, s.Connection, s.Transaction); command.Parameters.AddWithValue("@Id", e.ExpenseId); command.Parameters.AddWithValue("@Line", i + 1); command.Parameters.AddWithValue("@RuleId", line.RuleId); command.Parameters.AddWithValue("@Version", line.RuleVersion); command.Parameters.AddWithValue("@Code", line.RuleCode); command.Parameters.AddWithValue("@Name", line.Name); command.Parameters.AddWithValue("@Kind", line.Kind); command.Parameters.AddWithValue("@BaseKind", line.BaseKind); Money(command, "@Base", line.TaxableBase); var rate = command.Parameters.Add("@Rate", SqlDbType.Decimal); rate.Precision = 9; rate.Scale = 6; rate.Value = line.Rate; Money(command, "@Amount", line.Amount); command.Parameters.AddWithValue("@Jurisdiction", (object?)line.JurisdictionCode ?? DBNull.Value); await command.ExecuteNonQueryAsync(ct); }
    }

    private async Task OpenPayableAsync(SqlDocumentProcessingSessionAccessor.Session s, ExpenseDocumentPayload e, CancellationToken ct)
    {
        var payable = PayableOpening.Create(e.Withholding.NetAmount, e.IssuedAt, e.DueDate); var now = timeProvider.GetUtcNow();
        await using var command = new SqlCommand("""
            INSERT dbo.Payables(PayableId,BusinessId,SupplierId,SourceDocumentId,SourceDocumentType,DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
              VALUES(@PayableId,@BusinessId,@SupplierId,@Id,N'Expense',@Number,@Currency,@Original,@Outstanding,@Due,N'Open',@Now);
            INSERT dbo.PayableTransactions(PayableTransactionId,PayableId,TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
              VALUES(@TransactionId,@PayableId,N'Opening',@Original,@Id,@OccurredAt,@Now);
            """, s.Connection, s.Transaction);
        command.Parameters.AddWithValue("@PayableId", ids.NewId()); command.Parameters.AddWithValue("@TransactionId", ids.NewId()); command.Parameters.AddWithValue("@BusinessId", e.BusinessId); command.Parameters.AddWithValue("@SupplierId", e.SupplierId); command.Parameters.AddWithValue("@Id", e.ExpenseId); command.Parameters.AddWithValue("@Number", e.DocumentNumber); command.Parameters.AddWithValue("@Currency", e.CurrencyCode); Money(command, "@Original", payable.OriginalAmount); Money(command, "@Outstanding", payable.OutstandingAmount); command.Parameters.AddWithValue("@Due", payable.DueDate); command.Parameters.AddWithValue("@OccurredAt", e.IssuedAt); command.Parameters.AddWithValue("@Now", now); await command.ExecuteNonQueryAsync(ct);
    }
    private static void Money(SqlCommand c, string name, decimal value) { var p = c.Parameters.Add(name, SqlDbType.Decimal); p.Precision = 19; p.Scale = 4; p.Value = value; }
}
