using System.Data;
using System.Text.Json;
using Auraly.Contracts.Expenses;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlExpenseStore
{
    internal sealed record AcceptedExpense(ExpenseDocumentPayload Payload, string IdempotencyKey,
        byte[] RequestHash, Guid AccountingJobId);

    internal static async Task PersistAcceptedAsync(SqlConnection connection, SqlTransaction transaction,
        IReadOnlyList<AcceptedExpense> accepted, DateTimeOffset now, CancellationToken ct)
    {
        if (accepted.Count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(accepted));
        var expenses = accepted.Select(item => item.Payload).ToArray();
        if (expenses.Select(item => (item.TenantId, item.BusinessId)).Distinct().Count() != 1)
            throw new InvalidOperationException("Expenses must belong to one business.");
        await using var command = new SqlCommand("""
            INSERT dbo.Expenses(ExpenseId,BusinessId,SupplierId,ExpenseConceptId,CostCenterId,
              DocumentSeriesId,DocumentNumber,DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,
              SupplierDocumentNumber,SourceInvoiceId,PurchaseEvidenceType,IssuedAt,DueDate,CurrencyCode,Description,
              TaxExclusiveAmount,VatAmount,GrossAmount,WithholdingAmount,NetPayable,EvidenceUrl,
              Status,ConfirmedByUserId,IdempotencyKey,RequestHash,AcceptedAt)
            SELECT p.ExpenseId,p.BusinessId,p.SupplierId,p.ConceptId,p.CostCenterId,
              p.DocumentSeriesId,p.DocumentNumber,p.DocumentPrefix,p.DocumentSeriesCode,p.DocumentConsecutive,
              p.SupplierDocumentNumber,p.SourceInvoiceId,p.PurchaseEvidenceType,p.IssuedAt,p.DueDate,p.CurrencyCode,p.Description,
              p.TaxExclusiveAmount,p.VatAmount,p.GrossAmount,p.WithholdingTotal,p.NetAmount,p.EvidenceUrl,
              N'Accepted',p.ConfirmedByUserId,p.IdempotencyKey,CONVERT(binary(32),p.Hash,2),@Now
            FROM OPENJSON(@AcceptedJson) WITH(IdempotencyKey nvarchar(160),Hash varchar(64),ExpenseId uniqueidentifier,BusinessId uniqueidentifier,
              SupplierId uniqueidentifier,ConceptId uniqueidentifier,CostCenterId uniqueidentifier,
              DocumentSeriesId uniqueidentifier,DocumentNumber nvarchar(64),DocumentPrefix nvarchar(8),
              DocumentSeriesCode nvarchar(8),DocumentConsecutive bigint,SupplierDocumentNumber nvarchar(80),
              SourceInvoiceId uniqueidentifier,PurchaseEvidenceType nvarchar(40),IssuedAt datetimeoffset,DueDate datetimeoffset,
              CurrencyCode char(3),Description nvarchar(300),TaxExclusiveAmount decimal(19,4),
              VatAmount decimal(19,4),GrossAmount decimal(19,4),EvidenceUrl nvarchar(1000),ConfirmedByUserId uniqueidentifier,
              WithholdingTotal decimal(19,4),NetAmount decimal(19,4)) p;
            """, connection, transaction);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.Add("@AcceptedJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
            accepted.Select(item => new { item.Payload.ExpenseId, item.Payload.BusinessId, item.Payload.SupplierId,
                item.Payload.ConceptId, item.Payload.CostCenterId, item.Payload.DocumentSeriesId,
                item.Payload.DocumentNumber, item.Payload.DocumentPrefix, item.Payload.DocumentSeriesCode,
                item.Payload.DocumentConsecutive, item.Payload.SupplierDocumentNumber, item.Payload.SourceInvoiceId,
                item.Payload.PurchaseEvidenceType,
                item.Payload.IssuedAt, item.Payload.DueDate, item.Payload.CurrencyCode, item.Payload.Description,
                item.Payload.TaxExclusiveAmount, item.Payload.VatAmount, item.Payload.GrossAmount,
                item.Payload.EvidenceUrl, item.Payload.ConfirmedByUserId,
                item.Payload.Withholding.WithholdingTotal, item.Payload.Withholding.NetAmount,
                item.IdempotencyKey, Hash = Convert.ToHexString(item.RequestHash) }));
        if (await command.ExecuteNonQueryAsync(ct) != accepted.Count)
            throw new DBConcurrencyException("The complete expense batch was not accepted.");
        await PersistWithholdingAsync(connection, transaction, expenses, ct);
        await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction,
            accepted.Select(item => new SqlAccountingPostingJobWriter.Source(item.Payload.TenantId,
                item.Payload.BusinessId, item.Payload.ExpenseId, ExpenseDocumentTypes.Expense,
                ExpenseContractSerializer.Serialize(item.Payload), item.Payload.IssuedAt, item.AccountingJobId)).ToArray(),
            now, AccountingJobRequirement.PreserveCommercialEffects, ct);
    }
}
