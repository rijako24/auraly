using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlInvoiceChargeStore
{
    public async Task<InvoiceChargeHistoryPage> HistoryAsync(InvoiceChargeActor actor, DateOnly from, DateOnly to,
        int page, int pageSize, string? search, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var scope = new SqlCommand("SELECT TimeZone FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId;", connection);
        AddScope(scope, actor);
        var zoneId = await scope.ExecuteScalarAsync(ct) as string
            ?? throw new InvoiceChargeForbiddenException("La sede no pertenece a la empresa activa.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), zone));
        var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue), zone));
        const string filter = """
            FROM dbo.SalesDocuments d
            JOIN dbo.DocumentProcessingPayloads p ON p.DocumentId=d.DocumentId AND p.DocumentType=d.DocumentType
            CROSS APPLY OPENJSON(p.PayloadJson,'$.charges') j
            CROSS APPLY OPENJSON(j.value) WITH(AppliedChargeId uniqueidentifier '$.appliedChargeId',
              Name nvarchar(120) '$.name',Code nvarchar(32) '$.code',SupplierName nvarchar(300) '$.supplier.name',
              InvoicedAmount decimal(19,2) '$.invoicedAmount',ExpenseAmount decimal(19,2) '$.expenseAmount') c
            WHERE d.BusinessId=@BusinessId AND d.IssuedAt>=@From AND d.IssuedAt<@Until
              AND (@Search IS NULL OR c.Name LIKE N'%'+@Search+N'%' OR c.Code LIKE N'%'+@Search+N'%'
                OR c.SupplierName LIKE N'%'+@Search+N'%' OR d.DocumentNumber LIKE N'%'+@Search+N'%')
            """;
        await using var command = new SqlCommand($"""
            -- Materialize the scoped period once. OPENJSON has fixed cardinality estimates;
            -- sorting its nvarchar(max) projection directly can request excessive memory
            -- even for a small period. The temporary result supplies actual row statistics.
            SELECT d.DocumentId,d.DocumentNumber,d.IssuedAt,d.WorkSessionId,j.value ChargeJson,
              c.AppliedChargeId,c.InvoicedAmount,c.ExpenseAmount
            INTO #InvoiceChargeHistory {filter};
            SELECT COUNT(*),COALESCE(SUM(InvoicedAmount),0),COALESCE(SUM(ExpenseAmount),0)
            FROM #InvoiceChargeHistory;
            WITH Page AS (
              SELECT DocumentId,AppliedChargeId FROM #InvoiceChargeHistory
              ORDER BY IssuedAt DESC,DocumentId,AppliedChargeId
              OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY)
            SELECT h.DocumentId,h.DocumentNumber,h.IssuedAt,h.WorkSessionId,h.ChargeJson,
              expense.DocumentNumber,expense.Status,payable.OutstandingAmount
            FROM Page page JOIN #InvoiceChargeHistory h
              ON h.DocumentId=page.DocumentId AND h.AppliedChargeId=page.AppliedChargeId
            LEFT JOIN dbo.Expenses expense ON expense.ExpenseId=page.AppliedChargeId AND expense.BusinessId=@BusinessId
            LEFT JOIN dbo.Payables payable ON payable.SourceDocumentId=expense.ExpenseId AND payable.SourceDocumentType=N'Expense' AND payable.BusinessId=@BusinessId
            ORDER BY h.IssuedAt DESC,h.DocumentId,h.AppliedChargeId;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
        command.Parameters.AddWithValue("@From", start);
        command.Parameters.AddWithValue("@Until", end);
        command.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@Size", pageSize);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var count = reader.GetInt32(0); var invoiced = reader.GetDecimal(1); var expenseTotal = reader.GetDecimal(2);
        await reader.NextResultAsync(ct);
        var items = new List<InvoiceChargeHistoryItem>();
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetDateTimeOffset(2), reader.GetGuid(3),
                JsonSerializer.Deserialize<AppliedInvoiceCharge>(reader.GetString(4), jsonOptions)
                    ?? throw new InvalidDataException("El cargo histórico no contiene su snapshot."),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7)));
        return new(items, page, pageSize, count, invoiced, expenseTotal);
    }
}
