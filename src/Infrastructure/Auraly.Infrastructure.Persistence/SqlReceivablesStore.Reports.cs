using System.Data;
using Auraly.Application.Receivables;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlReceivablesStore
{
    public Task<ReceivablesReportPage> ReportAsync(ReceivablesUserIdentity user,
        ReceivablesReportQuery query, CancellationToken token) =>
        ReportCoreAsync(user, query, query.PageSize, token);

    public Task<ReceivablesReportPage> PrintReportAsync(ReceivablesUserIdentity user,
        ReceivablesReportQuery query, CancellationToken token) =>
        ReportCoreAsync(user, query with { Page = 1 }, 5_001, token);

    private async Task<ReceivablesReportPage> ReportCoreAsync(ReceivablesUserIdentity user,
        ReceivablesReportQuery query, int take, CancellationToken token)
    {
        var selectedOrder = SqlPagedSort.Build(query.SortBy, query.SortDirection,
            new Dictionary<string, string>
            {
                ["name"] = "CustomerName", ["issuedAt"] = "IssuedAt",
                ["dueDate"] = "DueDate", ["documentNumber"] = "DocumentNumber",
                ["originalAmount"] = "OriginalAmount", ["paidAmount"] = "PaidAmount",
                ["outstandingAmount"] = "OutstandingAmount",
                ["overdueAmount"] = "OverdueAmount", ["invoiceCount"] = "InvoiceCount"
            }, query.Consolidated ? "name" : "issuedAt", "asc",
            "CustomerId", "PartySiteId", "CurrencyCode", "ReceivableId");
        var order = query.Consolidated ? selectedOrder : SqlPagedSort.PrependDistinct(selectedOrder,
            query.SortBy == "name" && query.SortDirection == "desc" ? "CustomerName DESC" : "CustomerName ASC",
            "PartySiteId ASC");
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        var zone = await SqlBusinessLocalDates.ReadTimeZoneAsync(connection, user.TenantId,
            user.BusinessId, token) ?? throw new ReceivablesForbiddenException(
                "La sede no pertenece a la empresa autenticada.");
        await using var command = new SqlCommand($"""
            SET NOCOUNT ON;
            WITH Scoped AS (
              SELECT r.ReceivableId,r.CustomerId,r.PartySiteId,r.DocumentNumber,
                r.CurrencyCode,r.OriginalAmount,r.DueDate,r.Status,source.OccurredAt IssuedAt,
                COALESCE(p.DisplayName,p.LegalName,p.Identification) CustomerName,
                COALESCE(p.Identification,N'') Identification,site.Name PartySiteName
              FROM dbo.Receivables r
              JOIN dbo.Businesses business ON business.BusinessId=r.BusinessId
              JOIN dbo.Customers customer ON customer.CustomerId=r.CustomerId
              JOIN dbo.Parties p ON p.PartyId=customer.PartyId
              JOIN dbo.PartySites site ON site.PartySiteId=r.PartySiteId AND site.PartyId=p.PartyId
              JOIN dbo.AccountingSourceDocuments source
                ON source.SourceDocumentId=r.SourceDocumentId
                AND source.SourceDocumentType=r.SourceDocumentType
              WHERE r.BusinessId=@BusinessId AND business.TenantId=@TenantId
                AND r.CreatedAt<@Cutoff AND source.OccurredAt<@Cutoff
                AND (@CustomerId IS NULL OR r.CustomerId=@CustomerId)
                AND (@PartySiteId IS NULL OR r.PartySiteId=@PartySiteId)
                AND (@Search IS NULL OR r.DocumentNumber LIKE N'%' + @Search + N'%'
                  OR p.DisplayName LIKE N'%' + @Search + N'%'
                  OR p.Identification LIKE N'%' + @Search + N'%'
                  OR site.Name LIKE N'%' + @Search + N'%'
                  OR site.Code LIKE N'%' + @Search + N'%')
                AND (@From IS NULL OR r.CreatedAt>=@From)
                AND (@To IS NULL OR r.CreatedAt<@To)
            ), Movement AS (
              SELECT tx.ReceivableId,
                SUM(CASE WHEN tx.TransactionType IN(N'Opening',N'Adjustment') THEN tx.Amount
                  ELSE -tx.Amount END) Balance
              FROM dbo.ReceivableTransactions tx
              JOIN Scoped scoped ON scoped.ReceivableId=tx.ReceivableId
              WHERE tx.CreatedAt<@Cutoff AND tx.OccurredAt<@Cutoff
              GROUP BY tx.ReceivableId
            ), Paid AS (
              SELECT application.ReceivableId,SUM(application.Amount) PaidAmount
              FROM dbo.CustomerPaymentApplications application
              JOIN Scoped scoped ON scoped.ReceivableId=application.ReceivableId
              JOIN dbo.CustomerPayments payment ON payment.PaymentId=application.PaymentId
                AND payment.BusinessId=@BusinessId
              WHERE application.AppliedAt<@Cutoff AND payment.PaidAt<@Cutoff
              GROUP BY application.ReceivableId
            )
            SELECT scoped.*,COALESCE(movement.Balance,0) OutstandingAmount,
              COALESCE(paid.PaidAmount,0) PaidAmount,
              CASE WHEN movement.Balance<=0 THEN
                CASE WHEN scoped.Status=N'Cancelled' THEN N'Cancelled' ELSE N'Paid' END
                WHEN movement.Balance>=scoped.OriginalAmount THEN N'Open'
                ELSE N'PartiallyPaid' END HistoricalStatus
            INTO #Ledger
            FROM Scoped scoped
            JOIN Movement movement ON movement.ReceivableId=scoped.ReceivableId
            LEFT JOIN Paid paid ON paid.ReceivableId=scoped.ReceivableId;

            SELECT *,CASE WHEN OutstandingAmount>0 AND DueDate<@AgeDay
              THEN OutstandingAmount ELSE CONVERT(decimal(19,4),0) END OverdueAmount,
              CASE WHEN OutstandingAmount>0 AND DueDate>=@AgeDay THEN OutstandingAmount ELSE CONVERT(decimal(19,4),0) END NotDueAmount,
              CASE WHEN OutstandingAmount>0 AND DueDate<@AgeDay AND DueDate>=@Age30 THEN OutstandingAmount ELSE CONVERT(decimal(19,4),0) END Overdue1To30Amount,
              CASE WHEN OutstandingAmount>0 AND DueDate<@Age30 AND DueDate>=@Age60 THEN OutstandingAmount ELSE CONVERT(decimal(19,4),0) END Overdue31To60Amount,
              CASE WHEN OutstandingAmount>0 AND DueDate<@Age60 AND DueDate>=@Age90 THEN OutstandingAmount ELSE CONVERT(decimal(19,4),0) END Overdue61To90Amount,
              CASE WHEN OutstandingAmount>0 AND DueDate<@Age90 THEN OutstandingAmount ELSE CONVERT(decimal(19,4),0) END OverdueOver90Amount
            INTO #Filtered FROM #Ledger
            WHERE (@Status IS NULL OR HistoricalStatus=@Status)
              AND (@OutstandingOnly=0 OR OutstandingAmount>0)
              AND (@OverdueOnly=0 OR (OutstandingAmount>0 AND DueDate<@AgeDay));

            SELECT CustomerId,CustomerName,Identification,PartySiteId,PartySiteName,
              CurrencyCode,COUNT(*) InvoiceCount,SUM(OriginalAmount) OriginalAmount,
              SUM(PaidAmount) PaidAmount,SUM(OutstandingAmount) OutstandingAmount,
              SUM(OverdueAmount) OverdueAmount,
              SUM(NotDueAmount) NotDueAmount,SUM(Overdue1To30Amount) Overdue1To30Amount,
              SUM(Overdue31To60Amount) Overdue31To60Amount,SUM(Overdue61To90Amount) Overdue61To90Amount,
              SUM(OverdueOver90Amount) OverdueOver90Amount,
              CAST(NULL AS uniqueidentifier) ReceivableId,CAST(NULL AS nvarchar(64)) DocumentNumber,
              CAST(NULL AS datetimeoffset(7)) IssuedAt,CAST(NULL AS datetimeoffset(7)) DueDate
            INTO #Report FROM #Filtered WHERE @Consolidated=1
            GROUP BY CustomerId,CustomerName,Identification,PartySiteId,PartySiteName,CurrencyCode
            UNION ALL
            SELECT CustomerId,CustomerName,Identification,PartySiteId,PartySiteName,
              CurrencyCode,1,OriginalAmount,PaidAmount,OutstandingAmount,OverdueAmount,
              NotDueAmount,Overdue1To30Amount,Overdue31To60Amount,Overdue61To90Amount,OverdueOver90Amount,
              ReceivableId,DocumentNumber,IssuedAt,DueDate
            FROM #Filtered WHERE @Consolidated=0;

            SELECT COUNT(*),COALESCE(SUM(InvoiceCount),0),COALESCE(SUM(OriginalAmount),0),
              COALESCE(SUM(PaidAmount),0),COALESCE(SUM(OutstandingAmount),0),
              COALESCE(SUM(OverdueAmount),0),COALESCE(SUM(NotDueAmount),0),
              COALESCE(SUM(Overdue1To30Amount),0),COALESCE(SUM(Overdue31To60Amount),0),
              COALESCE(SUM(Overdue61To90Amount),0),COALESCE(SUM(OverdueOver90Amount),0) FROM #Report;
            SELECT * INTO #Page FROM #Report
            ORDER BY {order} OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            SELECT CustomerId,CustomerName,Identification,PartySiteId,PartySiteName,
              CurrencyCode,InvoiceCount,OriginalAmount,PaidAmount,OutstandingAmount,
              OverdueAmount,ReceivableId,DocumentNumber,IssuedAt,DueDate,
              NotDueAmount,Overdue1To30Amount,Overdue31To60Amount,Overdue61To90Amount,OverdueOver90Amount
            FROM #Page ORDER BY {order};
            SELECT application.ReceivableId,payment.DocumentNumber,application.AppliedAt,
              application.Amount
            FROM dbo.CustomerPaymentApplications application
            JOIN #Page page ON page.ReceivableId=application.ReceivableId
            JOIN dbo.CustomerPayments payment ON payment.PaymentId=application.PaymentId
              AND payment.BusinessId=@BusinessId
            WHERE application.AppliedAt<@Cutoff AND payment.PaidAt<@Cutoff
            ORDER BY application.ReceivableId,application.AppliedAt,application.LineNumber;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@CustomerId", (object?)query.CustomerId ?? DBNull.Value);
        command.Parameters.AddWithValue("@PartySiteId", (object?)query.PartySiteId ?? DBNull.Value);
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
        command.Parameters.AddWithValue("@AgeDay", SqlBusinessLocalDates.StartOfDay(query.Cutoff, zone));
        command.Parameters.AddWithValue("@Age30", SqlBusinessLocalDates.StartOfDay(query.Cutoff.AddDays(-30), zone));
        command.Parameters.AddWithValue("@Age60", SqlBusinessLocalDates.StartOfDay(query.Cutoff.AddDays(-60), zone));
        command.Parameters.AddWithValue("@Age90", SqlBusinessLocalDates.StartOfDay(query.Cutoff.AddDays(-90), zone));
        command.Parameters.AddWithValue("@Skip", (query.Page - 1) * query.PageSize);
        command.Parameters.AddWithValue("@Take", take);
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        var count = reader.GetInt32(0);
        var invoiceCount = reader.GetInt32(1);
        var original = reader.GetDecimal(2);
        var paid = reader.GetDecimal(3);
        var outstanding = reader.GetDecimal(4);
        var overdue = reader.GetDecimal(5);
        var notDue = reader.GetDecimal(6);
        var overdue1To30 = reader.GetDecimal(7);
        var overdue31To60 = reader.GetDecimal(8);
        var overdue61To90 = reader.GetDecimal(9);
        var overdueOver90 = reader.GetDecimal(10);
        await reader.NextResultAsync(token);
        var rows = new List<ReceivablesReportItem>();
        while (await reader.ReadAsync(token))
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetGuid(3), reader.GetString(4), reader.GetString(5),
                reader.GetInt32(6), reader.GetDecimal(7), reader.GetDecimal(8),
                reader.GetDecimal(9), reader.GetDecimal(10),
                reader.IsDBNull(11) ? null : reader.GetGuid(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetDateTimeOffset(13),
                reader.IsDBNull(14) ? null : reader.GetDateTimeOffset(14))
            {
                NotDueAmount = reader.GetDecimal(15), Overdue1To30Amount = reader.GetDecimal(16),
                Overdue31To60Amount = reader.GetDecimal(17), Overdue61To90Amount = reader.GetDecimal(18),
                OverdueOver90Amount = reader.GetDecimal(19)
            });
        await reader.NextResultAsync(token);
        var applications = new Dictionary<Guid, List<ReceivablesReportApplication>>();
        while (await reader.ReadAsync(token))
        {
            var id = reader.GetGuid(0);
            if (!applications.TryGetValue(id, out var lines))
                applications.Add(id, lines = []);
            lines.Add(new(reader.GetString(1), reader.GetDateTimeOffset(2), reader.GetDecimal(3)));
        }
        var items = rows.Select(row => row.ReceivableId is Guid id
            ? row with { Applications = applications.GetValueOrDefault(id) ?? [] }
            : row).ToArray();
        return new ReceivablesReportPage(items, query.Page, take, count, invoiceCount,
            original, paid, outstanding, overdue)
        {
            TotalNotDue = notDue, TotalOverdue1To30 = overdue1To30,
            TotalOverdue31To60 = overdue31To60, TotalOverdue61To90 = overdue61To90,
            TotalOverdueOver90 = overdueOver90
        };
    }
}
