using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Expenses;
using Auraly.Contracts.Expenses;
using Auraly.Domain.Expenses;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlExpenseStore
{
    public async Task<ExpenseAcceptance?> FindReplayAsync(ExpenseUserIdentity user, string idempotencyKey,
        ConfirmExpenseRequest request, ExpenseAmounts amounts, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        return await FindReplayAsync(connection, null, user, idempotencyKey, request, amounts, ct);
    }

    private static async Task<ExpenseAcceptance?> FindReplayAsync(SqlConnection connection, SqlTransaction? tx,
        ExpenseUserIdentity user, string idempotencyKey, ConfirmExpenseRequest request,
        ExpenseAmounts amounts, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT e.ExpenseId,e.DocumentNumber,e.Status,e.RequestHash,COALESCE(j.ProcessingSequence,0),
              j.JobId,a.AccountingPostingJobId,COALESCE(s.PayloadJson,p.PayloadJson),
              CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.FiscalDocuments f WHERE f.DocumentId=e.ExpenseId) THEN 1 ELSE 0 END)
            FROM dbo.Expenses e WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Businesses b ON b.BusinessId=e.BusinessId AND b.TenantId=@TenantId
            LEFT JOIN dbo.DocumentProcessingJobs j ON j.DocumentId=e.ExpenseId AND j.DocumentType=N'Expense'
            LEFT JOIN dbo.DocumentProcessingPayloads p ON p.DocumentId=e.ExpenseId AND p.DocumentType=N'Expense'
            LEFT JOIN dbo.AccountingPostingJobs a ON a.SourceDocumentId=e.ExpenseId AND a.SourceDocumentType=N'Expense'
            LEFT JOIN dbo.AccountingSourceDocuments s ON s.SourceDocumentId=e.ExpenseId AND s.SourceDocumentType=N'Expense'
            WHERE e.BusinessId=@BusinessId AND (e.ExpenseId=@Id OR e.IdempotencyKey=@Key);
            """, connection, tx);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@Id", request.ExpenseId);
        command.Parameters.AddWithValue("@Key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var storedHash = reader.GetFieldValue<byte[]>(3);
        if (!storedHash.AsSpan().SequenceEqual(HashRequest(request)))
        {
            // Legacy hashes included derived withholding. Compare against its immutable snapshot,
            // never against current supplier or tax configuration when replaying an accepted command.
            if (reader.IsDBNull(7)) throw new ExpenseConflictException("El gasto aceptado no tiene su snapshot histórico.");
            var withholding = ExpenseContractSerializer.Deserialize(reader.GetString(7)).Withholding;
            var legacyHash = SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { request, amounts, withholding })));
            if (!storedHash.AsSpan().SequenceEqual(legacyHash))
                throw new ExpenseConflictException("La clave de idempotencia se reutilizó con otros datos.");
        }
        var result = new ExpenseAcceptance(reader.GetGuid(0), reader.IsDBNull(5) ? Guid.Empty : reader.GetGuid(5),
            reader.GetString(1), reader.GetString(2), reader.GetInt64(4), true,
            reader.IsDBNull(6) ? null : reader.GetGuid(6), reader.GetBoolean(8));
        if (await reader.ReadAsync(ct))
            throw new ExpenseConflictException("El identificador y la clave pertenecen a gastos distintos.");
        return result;
    }

    private static byte[] HashRequest(ConfirmExpenseRequest request) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)));

    public async Task<ExpenseConceptView?> GetConceptAsync(ExpenseUserIdentity user, Guid conceptId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT c.ExpenseConceptId,c.BusinessId,c.Code,c.Name,c.ExpenseAccountId,a.Code,a.Name,
              c.DefaultCostCenterId,cc.Name,c.WithholdingConceptCode,c.IsActive
            FROM dbo.ExpenseConcepts c JOIN dbo.AccountingAccounts a ON a.AccountId=c.ExpenseAccountId
            JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId AND b.TenantId=@TenantId
            LEFT JOIN dbo.AccountingCostCenters cc ON cc.CostCenterId=c.DefaultCostCenterId
            WHERE c.BusinessId=@BusinessId AND c.ExpenseConceptId=@Id;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@Id", conceptId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadConcept(reader) : null;
    }
}
