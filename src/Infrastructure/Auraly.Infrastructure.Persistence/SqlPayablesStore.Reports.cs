using System.Data;
using Auraly.Application.Payables;
using Auraly.Contracts.Payables;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPayablesStore
{
    public Task<PayablesReportPage> ReportAsync(PayablesUserIdentity user,
        PayablesReportQuery query, CancellationToken token) =>
        ReportCoreAsync(user, query, query.PageSize, token);

    public Task<PayablesReportPage> PrintReportAsync(PayablesUserIdentity user,
        PayablesReportQuery query, CancellationToken token) =>
        ReportCoreAsync(user, query with { Page = 1 }, 5_001, token);

    private async Task<PayablesReportPage> ReportCoreAsync(PayablesUserIdentity user,
        PayablesReportQuery query, int take, CancellationToken token)
    {
        var selectedOrder = SqlPagedSort.Build(query.SortBy, query.SortDirection,
            new Dictionary<string, string>
            {
                ["name"] = "SupplierName", ["issuedAt"] = "IssuedAt",
                ["dueDate"] = "DueDate", ["documentNumber"] = "DocumentNumber",
                ["originalAmount"] = "OriginalAmount", ["paidAmount"] = "PaidAmount",
                ["outstandingAmount"] = "OutstandingAmount",
                ["overdueAmount"] = "OverdueAmount", ["invoiceCount"] = "InvoiceCount",
                ["currency"] = "CurrencyCode"
            }, query.Consolidated ? "name" : "issuedAt", "asc",
            "SupplierId", "PartySiteId", "CurrencyCode", "PayableId");
        var order = query.Consolidated ? selectedOrder : SqlPagedSort.PrependDistinct(selectedOrder,
            query.SortBy == "name" && query.SortDirection == "desc" ? "SupplierName DESC" : "SupplierName ASC",
            "PartySiteId ASC");
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        var zone = await SqlBusinessLocalDates.ReadTimeZoneAsync(connection, user.TenantId,
            user.BusinessId, token) ?? throw new PayablesForbiddenException(
                "La sede no pertenece a la empresa autenticada.");
        await using var command = new SqlCommand($"""
            SET NOCOUNT ON;
            WITH Scoped AS (
              SELECT p.PayableId,p.SupplierId,p.PartySiteId,p.DocumentNumber,
                p.CurrencyCode,p.OriginalAmount,p.DueDate,p.Status,
                source.OccurredAt IssuedAt,s.Name SupplierName,
                COALESCE(s.Identification,N'') Identification,site.Name PartySiteName
              FROM dbo.Payables p
              JOIN dbo.Businesses business ON business.BusinessId=p.BusinessId
              JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId
              LEFT JOIN dbo.PartySites site ON site.PartySiteId=p.PartySiteId AND site.PartyId=s.PartyId
              JOIN dbo.AccountingSourceDocuments source
                ON source.SourceDocumentId=p.SourceDocumentId
                AND source.SourceDocumentType=p.SourceDocumentType
              WHERE p.BusinessId=@BusinessId AND business.TenantId=@TenantId
                AND p.CreatedAt<@Cutoff AND source.OccurredAt<@Cutoff
                AND (@SupplierId IS NULL OR p.SupplierId=@SupplierId)
                AND (@PartySiteId IS NULL OR p.PartySiteId=@PartySiteId)
                AND (@ConceptId IS NULL OR EXISTS (
                  SELECT 1 FROM dbo.Expenses expense
                  WHERE p.SourceDocumentType=N'Expense' AND expense.ExpenseId=p.SourceDocumentId
                    AND expense.BusinessId=p.BusinessId AND (expense.ExpenseConceptId=@ConceptId OR EXISTS (
                      SELECT 1 FROM dbo.AccountingSourceDocuments conceptSource
                      CROSS APPLY OPENJSON(conceptSource.PayloadJson,'$.lines')
                        WITH(ConceptId uniqueidentifier '$.conceptId') line
                      WHERE conceptSource.SourceDocumentId=expense.ExpenseId
                        AND conceptSource.SourceDocumentType=N'Expense'
                        AND conceptSource.BusinessId=expense.BusinessId
                        AND line.ConceptId=@ConceptId))))
                AND (@Search IS NULL OR p.DocumentNumber LIKE N'%' + @Search + N'%'
                  OR s.Name LIKE N'%' + @Search + N'%'
                  OR s.Identification LIKE N'%' + @Search + N'%')
                AND (@From IS NULL OR p.CreatedAt>=@From)
                AND (@To IS NULL OR p.CreatedAt<@To)
            ), Movement AS (
              SELECT tx.PayableId,
                SUM(CASE WHEN tx.TransactionType IN(N'Opening',N'Adjustment') THEN tx.Amount
                  ELSE -tx.Amount END) Balance
              FROM dbo.PayableTransactions tx
              JOIN Scoped scoped ON scoped.PayableId=tx.PayableId
              WHERE tx.CreatedAt<@Cutoff AND tx.OccurredAt<@Cutoff
              GROUP BY tx.PayableId
            ), Paid AS (
              SELECT application.PayableId,SUM(application.Amount) PaidAmount
              FROM dbo.SupplierPaymentApplications application
              JOIN Scoped scoped ON scoped.PayableId=application.PayableId
              JOIN dbo.SupplierPayments payment ON payment.PaymentId=application.PaymentId
                AND payment.BusinessId=@BusinessId
              WHERE application.AppliedAt<@Cutoff AND payment.PaidAt<@Cutoff
              GROUP BY application.PayableId
            )
            SELECT scoped.*,COALESCE(movement.Balance,0) OutstandingAmount,
              COALESCE(paid.PaidAmount,0) PaidAmount,
              CASE WHEN movement.Balance<=0 THEN
                CASE WHEN scoped.Status=N'Cancelled' THEN N'Cancelled' ELSE N'Paid' END
                WHEN movement.Balance>=scoped.OriginalAmount THEN N'Open'
                ELSE N'PartiallyPaid' END HistoricalStatus
            INTO #Ledger
            FROM Scoped scoped
            JOIN Movement movement ON movement.PayableId=scoped.PayableId
            LEFT JOIN Paid paid ON paid.PayableId=scoped.PayableId;

            SELECT *,CASE WHEN OutstandingAmount>0 AND DueDate<@Cutoff
              THEN OutstandingAmount ELSE CONVERT(decimal(19,4),0) END OverdueAmount
            INTO #Filtered FROM #Ledger
            WHERE (@Status IS NULL OR HistoricalStatus=@Status)
              AND (@OutstandingOnly=0 OR OutstandingAmount>0)
              AND (@OverdueOnly=0 OR (OutstandingAmount>0 AND DueDate<@Cutoff));

            SELECT SupplierId,SupplierName,Identification,PartySiteId,PartySiteName,
              CurrencyCode,COUNT(*) InvoiceCount,SUM(OriginalAmount) OriginalAmount,
              SUM(PaidAmount) PaidAmount,SUM(OutstandingAmount) OutstandingAmount,
              SUM(OverdueAmount) OverdueAmount,
              CAST(NULL AS uniqueidentifier) PayableId,CAST(NULL AS nvarchar(80)) DocumentNumber,
              CAST(NULL AS datetimeoffset(7)) IssuedAt,CAST(NULL AS datetimeoffset(7)) DueDate
            INTO #Report FROM #Filtered WHERE @Consolidated=1
            GROUP BY SupplierId,SupplierName,Identification,PartySiteId,PartySiteName,CurrencyCode
            UNION ALL
            SELECT SupplierId,SupplierName,Identification,PartySiteId,PartySiteName,
              CurrencyCode,1,OriginalAmount,PaidAmount,OutstandingAmount,OverdueAmount,
              PayableId,DocumentNumber,IssuedAt,DueDate
            FROM #Filtered WHERE @Consolidated=0;

            SELECT CurrencyCode,COUNT(*) Rows,COALESCE(SUM(InvoiceCount),0),
              COALESCE(SUM(OriginalAmount),0),COALESCE(SUM(PaidAmount),0),
              COALESCE(SUM(OutstandingAmount),0),COALESCE(SUM(OverdueAmount),0)
            FROM #Report GROUP BY CurrencyCode;
            SELECT COUNT(*) FROM #Report;
            SELECT * INTO #Page FROM #Report
            ORDER BY {order} OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            SELECT SupplierId,SupplierName,Identification,PartySiteId,PartySiteName,
              CurrencyCode,InvoiceCount,OriginalAmount,PaidAmount,OutstandingAmount,
              OverdueAmount,PayableId,DocumentNumber,IssuedAt,DueDate
            FROM #Page ORDER BY {order};
            SELECT application.PayableId,payment.DocumentNumber,application.AppliedAt,
              application.Amount
            FROM dbo.SupplierPaymentApplications application
            JOIN #Page page ON page.PayableId=application.PayableId
            JOIN dbo.SupplierPayments payment ON payment.PaymentId=application.PaymentId
              AND payment.BusinessId=@BusinessId
            WHERE application.AppliedAt<@Cutoff AND payment.PaidAt<@Cutoff
            ORDER BY application.PayableId,application.AppliedAt,application.LineNumber;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@SupplierId", (object?)query.SupplierId ?? DBNull.Value);
        command.Parameters.AddWithValue("@PartySiteId", (object?)query.PartySiteId ?? DBNull.Value);
        command.Parameters.AddWithValue("@ConceptId", (object?)query.ConceptId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Search", (object?)query.Search?.Trim() is string { Length: > 0 } search ? search : DBNull.Value);
        command.Parameters.AddWithValue("@Consolidated", query.Consolidated);
        command.Parameters.AddWithValue("@OutstandingOnly", query.OutstandingOnly);
        command.Parameters.AddWithValue("@OverdueOnly", query.OverdueOnly);
        command.Parameters.AddWithValue("@Status", (object?)query.Status ?? DBNull.Value);
        command.Parameters.Add("@From", SqlDbType.DateTimeOffset).Value = query.From is DateOnly from
            ? new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)) : DBNull.Value;
        command.Parameters.Add("@To", SqlDbType.DateTimeOffset).Value = query.To is DateOnly to
            ? new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)) : DBNull.Value;
        var cutoff = SqlBusinessLocalDates.StartOfDay(query.Cutoff.AddDays(1), zone);
        if (take > 100)
        {
            var observed = timeProvider.GetUtcNow();
            if (observed < cutoff) cutoff = observed;
        }
        command.Parameters.AddWithValue("@Cutoff", cutoff);
        command.Parameters.AddWithValue("@Skip", (query.Page - 1) * query.PageSize);
        command.Parameters.AddWithValue("@Take", take);
        await using var reader = await command.ExecuteReaderAsync(token);
        var currencies = new List<PayablesReportCurrencyTotal>();
        while (await reader.ReadAsync(token))
            currencies.Add(new(reader.GetString(0), reader.GetInt32(2), reader.GetDecimal(3),
                reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6)));
        await reader.NextResultAsync(token);
        await reader.ReadAsync(token);
        var count = reader.GetInt32(0);
        await reader.NextResultAsync(token);
        var rows = new List<PayablesReportItem>();
        while (await reader.ReadAsync(token))
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5),
                reader.GetInt32(6), reader.GetDecimal(7), reader.GetDecimal(8),
                reader.GetDecimal(9), reader.GetDecimal(10),
                reader.IsDBNull(11) ? null : reader.GetGuid(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetDateTimeOffset(13),
                reader.IsDBNull(14) ? null : reader.GetDateTimeOffset(14)));
        await reader.NextResultAsync(token);
        var applications = new Dictionary<Guid, List<PayablesReportApplication>>();
        while (await reader.ReadAsync(token))
        {
            var id = reader.GetGuid(0);
            if (!applications.TryGetValue(id, out var lines))
                applications.Add(id, lines = []);
            lines.Add(new(reader.GetString(1), reader.GetDateTimeOffset(2), reader.GetDecimal(3)));
        }
        var items = rows.Select(row => row.PayableId is Guid id
            ? row with { Applications = applications.GetValueOrDefault(id) ?? [] }
            : row).ToArray();
        return new(items, currencies, query.Page, take, count);
    }
}
