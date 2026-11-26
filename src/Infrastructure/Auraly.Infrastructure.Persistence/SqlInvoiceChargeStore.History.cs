using System.Data;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlInvoiceChargeStore
{
    public async Task<InvoiceChargeHistoryPage> HistoryAsync(InvoiceChargeActor actor, DateOnly from, DateOnly to,
        int page, int pageSize, string? search, CancellationToken ct,
        Guid? createdByUserId = null, Guid? customerId = null, bool forExport = false)
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
            LEFT JOIN dbo.Expenses filterExpense ON filterExpense.ExpenseId=c.AppliedChargeId
              AND filterExpense.BusinessId=@BusinessId AND filterExpense.SourceInvoiceId=d.DocumentId
            LEFT JOIN dbo.FiscalDocuments filterExpenseFiscal ON filterExpenseFiscal.DocumentId=filterExpense.ExpenseId
              AND filterExpenseFiscal.BusinessId=@BusinessId
            WHERE d.BusinessId=@BusinessId AND d.IssuedAt>=@From AND d.IssuedAt<@Until
              AND (@CreatedByUserId IS NULL OR filterExpense.ConfirmedByUserId=@CreatedByUserId)
              AND (@CustomerId IS NULL OR d.CustomerId=@CustomerId)
              AND (@Search IS NULL OR c.Name LIKE N'%'+@Search+N'%' OR c.Code LIKE N'%'+@Search+N'%'
                OR c.SupplierName LIKE N'%'+@Search+N'%' OR d.DocumentNumber LIKE N'%'+@Search+N'%'
                OR d.FiscalNumber LIKE N'%'+@Search+N'%' OR filterExpense.DocumentNumber LIKE N'%'+@Search+N'%'
                OR filterExpenseFiscal.FiscalNumber LIKE N'%'+@Search+N'%')
            """;
        await using var command = new SqlCommand($"""
            -- Keep only filter/aggregate keys for the scoped period. Select invoice identities
            -- before loading their charge snapshots, expenses and balances. The temporary
            -- result also avoids OPENJSON's fixed row estimates on the paginated sort.
            SELECT d.DocumentId,d.DocumentType,d.IssuedAt,
              c.AppliedChargeId,c.InvoicedAmount,c.ExpenseAmount,filterExpense.GrossAmount GeneratedExpenseAmount
            INTO #InvoiceChargeHistory {filter};
            DECLARE @FilteredChargeCount int=(SELECT COUNT(*) FROM #InvoiceChargeHistory);
            SELECT @FilteredChargeCount,COALESCE(SUM(InvoicedAmount),0),COALESCE(SUM(ExpenseAmount),0),COUNT(DISTINCT DocumentId),
              COALESCE(SUM(GeneratedExpenseAmount),0)
            FROM #InvoiceChargeHistory;
            WITH Page AS (
              SELECT DocumentId,DocumentType,MAX(IssuedAt) IssuedAt FROM #InvoiceChargeHistory
              WHERE @ForExport=0 OR @FilteredChargeCount<=@ExportLimit
              GROUP BY DocumentId,DocumentType
              ORDER BY MAX(IssuedAt) DESC,DocumentId
              OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY)
            SELECT h.DocumentId,d.DocumentNumber,h.IssuedAt,
              COALESCE(NULLIF(party.DisplayName,N''),NULLIF(party.LegalName,N''),
                NULLIF(d.CustomerIdentification,N''),N'Consumidor final') CustomerName,
              d.WorkSessionId,j.value ChargeJson,
              expense.DocumentNumber,expense.Status,payable.OutstandingAmount,
              d.FiscalNumber,expense.ExpenseId,expense.AcceptedAt,expense.IssuedAt,expenseFiscal.FiscalNumber,expense.GrossAmount,
              expense.WithholdingAmount,expense.NetPayable,expense.DueDate,d.PayableAmount,
              COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(expenseCreator.FirstName,N' ',expenseCreator.LastName))),N''),expenseCreator.Username)
            FROM Page page
            JOIN dbo.SalesDocuments d ON d.DocumentId=page.DocumentId AND d.BusinessId=@BusinessId
            JOIN dbo.DocumentProcessingPayloads payload ON payload.DocumentId=page.DocumentId AND payload.DocumentType=page.DocumentType
            CROSS APPLY OPENJSON(payload.PayloadJson,'$.charges') j
            CROSS APPLY OPENJSON(j.value) WITH(AppliedChargeId uniqueidentifier '$.appliedChargeId') matched
            JOIN #InvoiceChargeHistory h ON h.DocumentId=page.DocumentId AND h.AppliedChargeId=matched.AppliedChargeId
            LEFT JOIN dbo.Customers customer ON customer.CustomerId=d.CustomerId
            LEFT JOIN dbo.Parties party ON party.PartyId=customer.PartyId
            LEFT JOIN dbo.Expenses expense ON expense.ExpenseId=h.AppliedChargeId AND expense.BusinessId=@BusinessId AND expense.SourceInvoiceId=d.DocumentId
            LEFT JOIN dbo.AppUsers expenseCreator ON expenseCreator.UserId=expense.ConfirmedByUserId AND expenseCreator.TenantId=@TenantId
            LEFT JOIN dbo.FiscalDocuments expenseFiscal ON expenseFiscal.DocumentId=expense.ExpenseId AND expenseFiscal.BusinessId=@BusinessId
            LEFT JOIN dbo.Payables payable ON payable.SourceDocumentId=expense.ExpenseId AND payable.SourceDocumentType=N'Expense' AND payable.BusinessId=@BusinessId
            ORDER BY h.IssuedAt DESC,h.DocumentId,h.AppliedChargeId;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
        command.Parameters.AddWithValue("@TenantId", actor.TenantId);
        command.Parameters.AddWithValue("@From", start);
        command.Parameters.AddWithValue("@Until", end);
        command.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("@Offset", forExport ? 0 : (page - 1) * pageSize);
        command.Parameters.AddWithValue("@Size", forExport ? InvoiceChargeService.MaximumReportRows : pageSize);
        command.Parameters.AddWithValue("@ForExport", forExport);
        command.Parameters.AddWithValue("@ExportLimit", InvoiceChargeService.MaximumReportRows);
        command.Parameters.Add("@CreatedByUserId", SqlDbType.UniqueIdentifier).Value = (object?)createdByUserId ?? DBNull.Value;
        command.Parameters.Add("@CustomerId", SqlDbType.UniqueIdentifier).Value = (object?)customerId ?? DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var count = reader.GetInt32(0); var invoiced = reader.GetDecimal(1); var expenseTotal = reader.GetDecimal(2); var documentCount = reader.GetInt32(3); var generatedTotal = reader.GetDecimal(4);
        await reader.NextResultAsync(ct);
        var items = new List<InvoiceChargeHistoryItem>();
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetDateTimeOffset(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                JsonSerializer.Deserialize<AppliedInvoiceCharge>(reader.GetString(5), jsonOptions)
                    ?? throw new InvalidDataException("El cargo histórico no contiene su snapshot."),
                reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDecimal(8))
            {
                FiscalNumber=reader.IsDBNull(9)?null:reader.GetString(9),
                ExpenseId=reader.IsDBNull(10)?null:reader.GetGuid(10),
                ExpenseAcceptedAt=reader.IsDBNull(11)?null:reader.GetDateTimeOffset(11),
                ExpenseIssuedAt=reader.IsDBNull(12)?null:reader.GetDateTimeOffset(12),
                ExpenseFiscalNumber=reader.IsDBNull(13)?null:reader.GetString(13),
                ExpenseGrossAmount=reader.IsDBNull(14)?null:reader.GetDecimal(14),
                ExpenseWithholdingAmount=reader.IsDBNull(15)?null:reader.GetDecimal(15),
                ExpenseNetPayable=reader.IsDBNull(16)?null:reader.GetDecimal(16),
                ExpenseDueDate=reader.IsDBNull(17)?null:reader.GetDateTimeOffset(17),
                InvoiceTotal=reader.GetDecimal(18),
                ExpenseCreatedByName=reader.IsDBNull(19)?null:reader.GetString(19)
            });
        return new(items, page, forExport ? InvoiceChargeService.MaximumReportRows : pageSize, count, invoiced, expenseTotal) { TotalDocuments = documentCount, GeneratedExpenseTotal = generatedTotal };
    }
}
