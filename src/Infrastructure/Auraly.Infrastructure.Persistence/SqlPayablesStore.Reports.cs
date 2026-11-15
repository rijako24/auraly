using Auraly.BuildingBlocks.Domain.Time;
using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Auraly.BuildingBlocks.Domain.Payments;
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
        ReportCoreAsync(user, query with { Page = 1 }, PortfolioReportLimits.MaximumExportRows + 1, token);

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
              SELECT p.PayableId,p.SupplierId,p.PartySiteId,p.DocumentNumber,
                p.CurrencyCode,p.OriginalAmount,p.DueDate,p.Status,
                s.Name SupplierName,COALESCE(s.Identification,N'') Identification,site.Name PartySiteName,
                p.SourceDocumentId,p.SourceDocumentType
              INTO #Candidates
              FROM dbo.Payables p
              JOIN dbo.Businesses business ON business.BusinessId=p.BusinessId
              JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId
              LEFT JOIN dbo.PartySites site ON site.PartySiteId=p.PartySiteId AND site.PartyId=s.PartyId
              WHERE p.BusinessId=@BusinessId AND business.TenantId=@TenantId
                AND p.CreatedAt<@Cutoff
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
                AND (@To IS NULL OR p.CreatedAt<@To);
            -- Filter obligations before reading immutable payloads. The former
            -- join could scan unrelated wide sources before applying the filters.
            -- A source can open several obligations; decode its issue date once.
            SELECT DISTINCT SourceDocumentId,SourceDocumentType INTO #CandidateSources FROM #Candidates;
            SELECT selected.SourceDocumentId,selected.SourceDocumentType,issue.IssuedAt
            INTO #DocumentSources FROM #CandidateSources selected
            JOIN dbo.AccountingSourceDocuments source
              ON source.SourceDocumentId=selected.SourceDocumentId AND source.SourceDocumentType=selected.SourceDocumentType
                AND source.TenantId=@TenantId AND source.BusinessId=@BusinessId
            CROSS APPLY accounting.PortfolioDocumentIssueDate(source.PayloadJson,source.OccurredAt) issue
            WHERE source.OccurredAt<@Cutoff
            OPTION (FORCE ORDER, LOOP JOIN);
            SELECT candidate.PayableId,candidate.SupplierId,candidate.PartySiteId,candidate.DocumentNumber,
              candidate.CurrencyCode,candidate.OriginalAmount,candidate.DueDate,candidate.Status,
              document.IssuedAt,candidate.SupplierName,candidate.Identification,candidate.PartySiteName
            INTO #Scoped FROM #Candidates candidate
            JOIN #DocumentSources document ON document.SourceDocumentId=candidate.SourceDocumentId
              AND document.SourceDocumentType=candidate.SourceDocumentType;
            -- Materialize the authorized filter once for both aggregates, keeping
            -- their plans independent of estimates from repeated search joins.
            CREATE UNIQUE CLUSTERED INDEX IX_Scoped ON #Scoped(PayableId);
            CREATE TABLE #CancelledAtCutoff(PayableId uniqueidentifier PRIMARY KEY);
            -- Resolve the rare cancellation evidence separately; do not join
            -- wide source payloads into every payment/adjustment aggregate.
            IF EXISTS(SELECT 1 FROM #Scoped WHERE Status=N'Cancelled')
              INSERT #CancelledAtCutoff
              SELECT DISTINCT tx.PayableId
              FROM #Scoped scoped
              JOIN dbo.PayableTransactions tx ON tx.PayableId=scoped.PayableId AND tx.TransactionType=N'Credit'
              JOIN dbo.AccountingSourceDocuments cancellation
                ON cancellation.SourceDocumentId=tx.SourceDocumentId
                AND cancellation.SourceDocumentType=COALESCE(tx.SourceDocumentType,N'ExpenseCancellation')
                AND cancellation.TenantId=@TenantId AND cancellation.BusinessId=@BusinessId
              WHERE scoped.Status=N'Cancelled' AND tx.CreatedAt<@Cutoff AND tx.OccurredAt<@Cutoff
                AND (cancellation.SourceDocumentType=N'ExpenseCancellation'
                  OR cancellation.SourceDocumentType=N'ManualAccountingVoucher'
                    AND JSON_VALUE(cancellation.PayloadJson,'$.VoucherTypeCode')=N'SupportDocumentCancellation');
            -- Financial aggregates need only the selected identities. Joining
            -- the wide document rows here carried names and references into a
            -- 20,000-row transaction sort (613 bytes per row in the CI plan).
            CREATE TABLE #ScopedKeys(PayableId uniqueidentifier PRIMARY KEY);
            INSERT #ScopedKeys SELECT PayableId FROM #Scoped;
            WITH Movement AS (
              SELECT tx.PayableId,
                SUM(effect.BalanceChange) Balance
              FROM dbo.PayableTransactions tx
              CROSS APPLY accounting.PortfolioTransactionEffect(tx.TransactionType,tx.Amount) effect
              JOIN #ScopedKeys scoped ON scoped.PayableId=tx.PayableId
              WHERE tx.CreatedAt<@Cutoff AND tx.OccurredAt<@Cutoff
              GROUP BY tx.PayableId
            ), Paid AS (
              SELECT application.PositionId PayableId,SUM(application.Amount) PaidAmount
              FROM accounting.PortfolioAppliedAmounts(@BusinessId,N'Payable') application
              JOIN #ScopedKeys scoped ON scoped.PayableId=application.PositionId
              WHERE application.AppliedAt<@Cutoff AND application.OccurredAt<@Cutoff
              GROUP BY application.PositionId
            )
            -- A CTE is inlined: the optimizer can otherwise pull the wide
            -- document join below the transaction aggregate. Finish the narrow
            -- financial projection before joining descriptive fields.
            SELECT movement.PayableId,movement.Balance,COALESCE(paid.PaidAmount,0) PaidAmount
            INTO #FinancialAmounts
            FROM Movement movement LEFT JOIN Paid paid ON paid.PayableId=movement.PayableId;
            SELECT scoped.*,COALESCE(financial.Balance,0) OutstandingAmount,
              financial.PaidAmount,
              CASE WHEN financial.Balance<=0 THEN
                CASE WHEN cancellation.PayableId IS NOT NULL THEN N'Cancelled' ELSE N'Paid' END
                WHEN financial.Balance>=scoped.OriginalAmount THEN N'Open'
                ELSE N'PartiallyPaid' END HistoricalStatus
            INTO #Ledger
            FROM #Scoped scoped
            JOIN #FinancialAmounts financial ON financial.PayableId=scoped.PayableId
            LEFT JOIN #CancelledAtCutoff cancellation ON cancellation.PayableId=scoped.PayableId;

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

            SELECT SupplierId,SupplierName,Identification,PartySiteId,PartySiteName,
              CurrencyCode,COUNT(*) InvoiceCount,SUM(OriginalAmount) OriginalAmount,
              SUM(PaidAmount) PaidAmount,SUM(OutstandingAmount) OutstandingAmount,
              SUM(OverdueAmount) OverdueAmount,
              SUM(NotDueAmount) NotDueAmount,SUM(Overdue1To30Amount) Overdue1To30Amount,
              SUM(Overdue31To60Amount) Overdue31To60Amount,SUM(Overdue61To90Amount) Overdue61To90Amount,
              SUM(OverdueOver90Amount) OverdueOver90Amount,
              CAST(NULL AS uniqueidentifier) PayableId,CAST(NULL AS nvarchar(80)) DocumentNumber,
              CAST(NULL AS datetimeoffset(7)) IssuedAt,CAST(NULL AS datetimeoffset(7)) DueDate
            INTO #Report FROM #Filtered WHERE @Consolidated=1
            GROUP BY SupplierId,SupplierName,Identification,PartySiteId,PartySiteName,CurrencyCode
            UNION ALL
            SELECT SupplierId,SupplierName,Identification,PartySiteId,PartySiteName,
              CurrencyCode,1,OriginalAmount,PaidAmount,OutstandingAmount,OverdueAmount,
              NotDueAmount,Overdue1To30Amount,Overdue31To60Amount,Overdue61To90Amount,OverdueOver90Amount,
              PayableId,DocumentNumber,IssuedAt,DueDate
            FROM #Filtered WHERE @Consolidated=0;

            SELECT CurrencyCode,COUNT(*) Rows,COALESCE(SUM(InvoiceCount),0),
              COALESCE(SUM(OriginalAmount),0),COALESCE(SUM(PaidAmount),0),
              COALESCE(SUM(OutstandingAmount),0),COALESCE(SUM(OverdueAmount),0),
              COALESCE(SUM(NotDueAmount),0),COALESCE(SUM(Overdue1To30Amount),0),
              COALESCE(SUM(Overdue31To60Amount),0),COALESCE(SUM(Overdue61To90Amount),0),
              COALESCE(SUM(OverdueOver90Amount),0)
            FROM #Report GROUP BY CurrencyCode;
            SELECT COUNT(*) FROM #Report;
            SELECT * INTO #Page FROM #Report
            ORDER BY {order} OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            SELECT SupplierId,SupplierName,Identification,PartySiteId,PartySiteName,
              CurrencyCode,InvoiceCount,OriginalAmount,PaidAmount,OutstandingAmount,
              OverdueAmount,PayableId,DocumentNumber,IssuedAt,DueDate,
              NotDueAmount,Overdue1To30Amount,Overdue31To60Amount,Overdue61To90Amount,OverdueOver90Amount
            FROM #Page ORDER BY {order};
            IF @IncludeApplications=1
            BEGIN
            -- Enrich receipt identities only after selecting this page's facts;
            -- keep the origins view out of the full-history application join.
            SELECT TOP(@ApplicationTake) application.* INTO #PageApplications
            FROM accounting.PortfolioAppliedAmounts(@BusinessId,N'Payable') application
            JOIN #Page page ON page.PayableId=application.PositionId
            WHERE application.AppliedAt<@Cutoff AND application.OccurredAt<@Cutoff
            ORDER BY application.PositionId,application.AppliedAt,application.PaymentId,application.UnapplicationTransactionId;
            CREATE CLUSTERED INDEX IX_PageApplications ON #PageApplications(PaymentId);
            SELECT TOP(@ApplicationTake) application.PositionId,
              CASE WHEN application.Amount<0 THEN CONCAT(N'Desaplicación de ',origin.DocumentNumber) ELSE origin.DocumentNumber END,application.AppliedAt,
              application.Amount
            FROM #PageApplications application
            -- PaymentId and kind identify one origin: payment/draft primary
            -- keys and UQ_AccountingEntries_Source enforce that cardinality.
            CROSS APPLY (SELECT TOP(1) receipt.DocumentNumber
              FROM accounting.PortfolioPaymentOrigins receipt
              WHERE receipt.PaymentId=application.PaymentId
                AND receipt.SubledgerKind=N'Payable' AND receipt.BusinessId=@BusinessId) origin
            ORDER BY application.PositionId,application.AppliedAt,application.PaymentId,application.UnapplicationTransactionId;
            END
            ELSE
              SELECT PayableId PositionId,CAST(NULL AS nvarchar(80)) DocumentNumber,
                CAST(NULL AS datetimeoffset) AppliedAt,CAST(NULL AS decimal(19,4)) Amount
              FROM #Page WHERE 1=0;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@SupplierId", (object?)query.SupplierId ?? DBNull.Value);
        command.Parameters.AddWithValue("@PartySiteId", (object?)query.PartySiteId ?? DBNull.Value);
        command.Parameters.AddWithValue("@ConceptId", (object?)query.ConceptId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Search", (object?)query.Search?.Trim() is string { Length: > 0 } search ? search : DBNull.Value);
        command.Parameters.AddWithValue("@Consolidated", query.Consolidated);
        command.Parameters.AddWithValue("@IncludeApplications", query.IncludeApplications && !query.Consolidated);
        command.Parameters.AddWithValue("@ApplicationTake", take > 100 ? PortfolioReportLimits.MaximumExportRows + 1 : int.MaxValue);
        command.Parameters.AddWithValue("@OutstandingOnly", query.OutstandingOnly);
        command.Parameters.AddWithValue("@OverdueOnly", query.OverdueOnly);
        command.Parameters.AddWithValue("@Status", (object?)query.Status ?? DBNull.Value);
        SqlBusinessLocalDates.BindDateRange(command,query.From,query.To,zone,query.DateRangeVersion);
        command.Parameters.AddWithValue("@Cutoff", SqlBusinessLocalDates.StartOfDay(query.Cutoff.AddDays(1), zone));
        command.Parameters.AddWithValue("@AgeDay", SqlBusinessLocalDates.StartOfDay(query.Cutoff, zone));
        command.Parameters.AddWithValue("@Age30", SqlBusinessLocalDates.StartOfDay(query.Cutoff.AddDays(-PortfolioAging.FirstBandDays), zone));
        command.Parameters.AddWithValue("@Age60", SqlBusinessLocalDates.StartOfDay(query.Cutoff.AddDays(-PortfolioAging.SecondBandDays), zone));
        command.Parameters.AddWithValue("@Age90", SqlBusinessLocalDates.StartOfDay(query.Cutoff.AddDays(-PortfolioAging.ThirdBandDays), zone));
        command.Parameters.AddWithValue("@Skip", (query.Page - 1) * query.PageSize);
        command.Parameters.AddWithValue("@Take", take);
        await using var reader = await command.ExecuteReaderAsync(token);
        var currencies = new List<PayablesReportCurrencyTotal>();
        while (await reader.ReadAsync(token))
            currencies.Add(new PayablesReportCurrencyTotal(reader.GetString(0), reader.GetInt32(2), reader.GetDecimal(3),
                reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6))
            {
                NotDueAmount = reader.GetDecimal(7), Overdue1To30Amount = reader.GetDecimal(8),
                Overdue31To60Amount = reader.GetDecimal(9), Overdue61To90Amount = reader.GetDecimal(10),
                OverdueOver90Amount = reader.GetDecimal(11)
            });
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
                reader.IsDBNull(13) ? null : BusinessCalendar.InZone(reader.GetDateTimeOffset(13), zone),
                reader.IsDBNull(14) ? null : BusinessCalendar.InZone(reader.GetDateTimeOffset(14), zone))
            {
                NotDueAmount = reader.GetDecimal(15), Overdue1To30Amount = reader.GetDecimal(16),
                Overdue31To60Amount = reader.GetDecimal(17), Overdue61To90Amount = reader.GetDecimal(18),
                OverdueOver90Amount = reader.GetDecimal(19)
            });
        await reader.NextResultAsync(token);
        var applications = new Dictionary<Guid, List<PayablesReportApplication>>();
        while (await reader.ReadAsync(token))
        {
            var id = reader.GetGuid(0);
            if (!applications.TryGetValue(id, out var lines))
                applications.Add(id, lines = []);
            lines.Add(new(reader.GetString(1), BusinessCalendar.InZone(reader.GetDateTimeOffset(2), zone), reader.GetDecimal(3)));
        }
        var items = rows.Select(row => row.PayableId is Guid id
            ? row with { Applications = applications.GetValueOrDefault(id) ?? [] }
            : row).ToArray();
        return new(items, currencies, query.Page, take, count);
    }
}
