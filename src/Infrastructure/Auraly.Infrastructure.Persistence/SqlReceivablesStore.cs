using Auraly.Commerce.Accounting.Infrastructure;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Receivables;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Domain.Payments;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Receivables;
using Auraly.Domain.Receivables;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlReceivablesStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IReceivablesStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<CustomerPortfolioPage> ListCustomersAsync(ReceivablesUserIdentity user,
        CustomerPortfolioQuery query, CancellationToken token)
    {
        var order = SqlPagedSort.Build(query.SortBy, query.SortDirection,
            new Dictionary<string, string>
            {
                ["name"] = "CustomerName", ["invoiceCount"] = "InvoiceCount",
                ["originalAmount"] = "OriginalAmount", ["paidAmount"] = "PaidAmount",
                ["outstandingAmount"] = "OutstandingAmount", ["overdueAmount"] = "OverdueAmount"
            }, "name", "asc", "PartySiteName", "PartySiteId", "CustomerId");
        await using var connection=connections.Create(); await connection.OpenAsync(token);
        await using var command=new SqlCommand($"""
            WITH Paid AS(
              SELECT application.PositionId ReceivableId,SUM(application.Amount) PaidAmount
              FROM accounting.PortfolioAppliedAmounts(@BusinessId,N'Receivable') application
              JOIN dbo.Receivables scoped ON scoped.ReceivableId=application.PositionId
              WHERE scoped.BusinessId=@BusinessId
              GROUP BY application.PositionId),
            Portfolio AS(
              SELECT r.CustomerId,r.PartySiteId,site.Name PartySiteName,
                COALESCE(p.DisplayName,p.LegalName,p.Identification) CustomerName,
                COALESCE(p.Identification,N'') Identification,COUNT(*) InvoiceCount,
                SUM(r.OriginalAmount) OriginalAmount,SUM(COALESCE(paid.PaidAmount,0)) PaidAmount,
                SUM(r.OutstandingAmount) OutstandingAmount,
                SUM(CASE WHEN r.OutstandingAmount>0 AND r.DueDate<@Now THEN r.OutstandingAmount ELSE 0 END) OverdueAmount
              FROM dbo.Receivables r
              JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId
              JOIN dbo.Customers c ON c.CustomerId=r.CustomerId
              JOIN dbo.Parties p ON p.PartyId=c.PartyId
              LEFT JOIN dbo.PartySites site ON site.PartySiteId=r.PartySiteId AND site.PartyId=p.PartyId
              LEFT JOIN Paid paid ON paid.ReceivableId=r.ReceivableId
              WHERE r.BusinessId=@BusinessId AND b.TenantId=@TenantId
                AND (@CustomerId IS NULL OR r.CustomerId=@CustomerId)
                AND (@PartySiteId IS NULL OR r.PartySiteId=@PartySiteId)
                AND (@Status IS NULL OR r.Status=@Status)
                AND (@From IS NULL OR r.CreatedAt>=@From)
                AND (@To IS NULL OR r.CreatedAt<@To)
                AND (@Overdue IS NULL OR (@Overdue=1 AND r.OutstandingAmount>0 AND r.DueDate<@Now)
                  OR (@Overdue=0 AND (r.OutstandingAmount=0 OR r.DueDate>=@Now)))
                AND (@Search IS NULL OR p.DisplayName LIKE N'%' + @Search + N'%'
                  OR p.LegalName LIKE N'%' + @Search + N'%' OR p.Identification LIKE N'%' + @Search + N'%'
                  OR r.DocumentNumber LIKE N'%' + @Search + N'%' OR site.Name LIKE N'%' + @Search + N'%')
              GROUP BY r.CustomerId,r.PartySiteId,site.Name,p.DisplayName,p.LegalName,p.Identification)
            SELECT * INTO #Portfolio FROM Portfolio
            WHERE @Overdue IS NULL OR (@Overdue=1 AND OverdueAmount>0)
              OR (@Overdue=0 AND OverdueAmount=0);
            SELECT COUNT(*),COALESCE(SUM(OutstandingAmount),0),COALESCE(SUM(OverdueAmount),0),COALESCE(SUM(InvoiceCount),0),
              COALESCE(SUM(OriginalAmount),0),COALESCE(SUM(PaidAmount),0)
            FROM #Portfolio;
            SELECT CustomerId,CustomerName,Identification,InvoiceCount,OriginalAmount,PaidAmount,
              OutstandingAmount,OverdueAmount,PartySiteId,PartySiteName FROM #Portfolio
            ORDER BY {order}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """,connection);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@TenantId",user.TenantId);
        command.Parameters.AddWithValue("@Search",(object?)query.Search??DBNull.Value);command.Parameters.AddWithValue("@Overdue",(object?)query.Overdue??DBNull.Value);
        command.Parameters.AddWithValue("@CustomerId",(object?)query.CustomerId??DBNull.Value);
        command.Parameters.AddWithValue("@PartySiteId",(object?)query.PartySiteId??DBNull.Value);
        command.Parameters.AddWithValue("@Status",(object?)query.Status??DBNull.Value);
        AddDateRange(command,query.From,query.To);
        command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());command.Parameters.AddWithValue("@Offset",(query.Page-1)*query.PageSize);command.Parameters.AddWithValue("@PageSize",query.PageSize);
        await using var reader=await command.ExecuteReaderAsync(token);await reader.ReadAsync(token);
        var count=reader.GetInt32(0);var outstanding=reader.GetDecimal(1);var overdue=reader.GetDecimal(2);var invoices=reader.GetInt32(3);
        var original=reader.GetDecimal(4);var paid=reader.GetDecimal(5);
        await reader.NextResultAsync(token);var items=new List<CustomerPortfolioItem>();
        while(await reader.ReadAsync(token))items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetInt32(3),reader.GetDecimal(4),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetDecimal(7),reader.IsDBNull(8)?null:reader.GetGuid(8),reader.IsDBNull(9)?null:reader.GetString(9)));
        return new(items,query.Page,query.PageSize,count,outstanding,overdue,invoices,original,paid);
    }

    public async Task<ReceivablePage> ListAsync(ReceivablesUserIdentity user, ReceivableQuery query, CancellationToken token)
    {
        var sortColumns = new Dictionary<string, string>
        {
            ["default"] = "CASE WHEN r.OutstandingAmount>0 AND r.DueDate<@Now THEN 0 ELSE 1 END,r.DueDate",
            ["documentNumber"] = "r.DocumentNumber", ["dueDate"] = "r.DueDate",
            ["originalAmount"] = "r.OriginalAmount", ["outstandingAmount"] = "r.OutstandingAmount",
            ["status"] = "CASE WHEN r.OutstandingAmount>0 AND r.DueDate<@Now THEN N'Vencida' WHEN r.Status=N'Open' THEN N'Pendiente' WHEN r.Status=N'PartiallyPaid' THEN N'Abono parcial' WHEN r.Status=N'Paid' THEN N'Pagada' ELSE N'Cancelada' END"
        };
        var order = SqlPagedSort.Build(query.SortBy, query.SortDirection, sortColumns,
            "default", "asc", "r.ReceivableId");
        var pageOrder = SqlPagedSort.Build(query.SortBy, query.SortDirection,
            sortColumns.ToDictionary(item => item.Key,
                item => item.Value.Replace("r.", "page.", StringComparison.Ordinal)),
            "default", "asc", "page.ReceivableId");
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        const string where = """
            r.BusinessId=@BusinessId AND b.TenantId=@TenantId
            AND (@CustomerId IS NULL OR r.CustomerId=@CustomerId)
            AND (@PartySiteId IS NULL OR r.PartySiteId=@PartySiteId)
            AND (@Status IS NULL OR r.Status=@Status)
            AND (@From IS NULL OR r.CreatedAt>=@From)
            AND (@To IS NULL OR r.CreatedAt<@To)
            AND (@OutstandingOnly=0 OR r.OutstandingAmount>0)
            AND (@Overdue IS NULL OR (@Overdue=1 AND r.OutstandingAmount>0 AND r.DueDate<@Now)
                 OR (@Overdue=0 AND (r.OutstandingAmount=0 OR r.DueDate>=@Now)))
            AND (@Search IS NULL OR r.DocumentNumber LIKE N'%' + @Search + N'%'
                 OR p.DisplayName LIKE N'%' + @Search + N'%'
                 OR p.Identification LIKE N'%' + @Search + N'%'
                 OR site.Name LIKE N'%' + @Search + N'%'
                 OR site.Code LIKE N'%' + @Search + N'%')
            """;
        int count; decimal outstanding; decimal overdue; decimal original; decimal paidTotal;
        await using (var command = new SqlCommand($"""
            WITH Filtered AS (
              SELECT r.ReceivableId,r.OutstandingAmount,r.DueDate,r.OriginalAmount
            FROM dbo.Receivables r INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId
            LEFT JOIN dbo.Customers c ON c.CustomerId=r.CustomerId
            INNER JOIN dbo.Parties p ON p.PartyId=COALESCE(c.PartyId,r.PartyId)
            LEFT JOIN dbo.PartySites site ON site.PartySiteId=r.PartySiteId
              WHERE {where}),
            Paid AS (SELECT application.PositionId ReceivableId,SUM(application.Amount) PaidAmount
              FROM accounting.PortfolioAppliedAmounts(@BusinessId,N'Receivable') application
              JOIN Filtered scoped ON scoped.ReceivableId=application.PositionId
              GROUP BY application.PositionId)
            SELECT COUNT(*),COALESCE(SUM(r.OutstandingAmount),0),
              COALESCE(SUM(CASE WHEN r.OutstandingAmount>0 AND r.DueDate<@Now
                THEN r.OutstandingAmount ELSE 0 END),0),
              COALESCE(SUM(r.OriginalAmount),0),COALESCE(SUM(COALESCE(paid.PaidAmount,0)),0)
            FROM Filtered r LEFT JOIN Paid paid ON paid.ReceivableId=r.ReceivableId;
            """, connection))
        {
            AddQuery(command, user, query, timeProvider.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(token);
            await reader.ReadAsync(token); count=reader.GetInt32(0); outstanding=reader.GetDecimal(1); overdue=reader.GetDecimal(2);
            original=reader.GetDecimal(3); paidTotal=reader.GetDecimal(4);
        }
        var items = new List<ReceivableListItem>();
        await using (var command = new SqlCommand($"""
            SELECT r.ReceivableId,r.CustomerId,COALESCE(p.DisplayName,p.LegalName,p.Identification) CustomerName,
                   r.DocumentNumber,r.CurrencyCode,r.OriginalAmount,r.OutstandingAmount,r.DueDate,
                   r.Status,r.CreatedAt,CAST(CASE WHEN r.OutstandingAmount>0 AND r.DueDate<@Now THEN 1 ELSE 0 END AS bit) IsOverdue,
                   r.PartySiteId,site.Name PartySiteName,r.PartyId
            INTO #Page
            FROM dbo.Receivables r INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId
            LEFT JOIN dbo.Customers c ON c.CustomerId=r.CustomerId
            INNER JOIN dbo.Parties p ON p.PartyId=COALESCE(c.PartyId,r.PartyId)
            LEFT JOIN dbo.PartySites site ON site.PartySiteId=r.PartySiteId WHERE {where}
            ORDER BY {order}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            SELECT page.ReceivableId,page.CustomerId,page.CustomerName,page.DocumentNumber,
              page.CurrencyCode,page.OriginalAmount,page.OutstandingAmount,page.DueDate,
              page.Status,page.CreatedAt,page.IsOverdue,page.PartySiteId,page.PartySiteName,
              COALESCE(paid.PaidAmount,0),page.PartyId
            FROM #Page page
            LEFT JOIN (SELECT application.PositionId ReceivableId,SUM(application.Amount) PaidAmount
              FROM accounting.PortfolioAppliedAmounts(@BusinessId,N'Receivable') application
              JOIN #Page selected ON selected.ReceivableId=application.PositionId
              GROUP BY application.PositionId) paid ON paid.ReceivableId=page.ReceivableId
            ORDER BY {pageOrder};
            """, connection))
        {
            AddQuery(command,user,query,timeProvider.GetUtcNow());
            command.Parameters.AddWithValue("@Offset",(query.Page-1)*query.PageSize);
            command.Parameters.AddWithValue("@PageSize",query.PageSize);
            await using var reader=await command.ExecuteReaderAsync(token);
            while(await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.IsDBNull(1)?null:reader.GetGuid(1),reader.GetString(2),
                reader.GetString(3),reader.GetString(4),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetDateTimeOffset(7),
                reader.GetString(8),reader.GetBoolean(10),reader.GetDateTimeOffset(9),
                reader.IsDBNull(11)?null:reader.GetGuid(11),reader.IsDBNull(12)?null:reader.GetString(12),reader.GetDecimal(13),
                reader.IsDBNull(14)?null:reader.GetGuid(14)));
        }
        return new(items,query.Page,query.PageSize,count,outstanding,overdue,original,paidTotal);
    }

    public Task<CustomerPaymentHistoryPage> PaymentHistoryAsync(ReceivablesUserIdentity user,
        Guid customerId, int page, int pageSize, CancellationToken token) =>
        ListPaymentsAsync(user,new(page,pageSize,null,customerId),token);

    public async Task<CustomerPaymentHistoryPage> ListPaymentsAsync(ReceivablesUserIdentity user,
        CustomerPaymentHistoryQuery query,CancellationToken token)
    {
        var paymentOrder = SqlPagedSort.Build(query.SortBy, query.SortDirection,
            new Dictionary<string, string>
            {
                ["paidAt"] = "payment.PaidAt",
                ["partyName"] = "COALESCE(party.DisplayName,party.LegalName,party.Identification)",
                ["documentNumber"] = "origin.DocumentNumber",
                ["totalAmount"] = "filtered.TotalAmount"
            }, "paidAt", "desc", "payment.PaymentId DESC");
        await using var connection=connections.Create(); await connection.OpenAsync(token);
        await using var command=new SqlCommand($"""
            -- Capture only filtered keys/amounts, then aggregate their applications.
            -- An advance has no invoice; its site comes from the accepted group.
            CREATE TABLE #FilteredPayments(PaymentId uniqueidentifier PRIMARY KEY,TotalAmount decimal(19,4));
            INSERT #FilteredPayments SELECT payment.PaymentId,payment.TotalAmount
            FROM dbo.CustomerPayments payment
            INNER JOIN accounting.PortfolioPaymentOrigins origin ON origin.PaymentId=payment.PaymentId
              AND origin.SubledgerKind=N'Receivable' AND origin.BusinessId=payment.BusinessId
            INNER JOIN dbo.Businesses business ON business.BusinessId=payment.BusinessId
            LEFT JOIN dbo.Customers customer ON customer.CustomerId=payment.CustomerId AND customer.TenantId=@TenantId
            INNER JOIN dbo.Parties party ON party.PartyId=COALESCE(payment.PartyId,customer.PartyId) AND party.TenantId=@TenantId
            WHERE payment.BusinessId=@BusinessId AND business.TenantId=@TenantId
              AND (@CustomerId IS NULL OR payment.CustomerId=@CustomerId)
              AND (@From IS NULL OR payment.PaidAt>=@From)
              AND (@To IS NULL OR payment.PaidAt<@To)
              AND (@Status IS NULL AND @Overdue IS NULL OR EXISTS(
                SELECT 1 FROM dbo.CustomerPaymentApplications application
                JOIN dbo.Receivables invoice ON invoice.ReceivableId=application.ReceivableId
                WHERE application.PaymentId=payment.PaymentId
                  AND (@PartySiteId IS NULL OR invoice.PartySiteId=@PartySiteId)
                  AND (@Status IS NULL OR invoice.Status=@Status)
                  AND (@Overdue IS NULL OR (@Overdue=1 AND invoice.OutstandingAmount>0 AND invoice.DueDate<@Now)
                    OR (@Overdue=0 AND (invoice.OutstandingAmount=0 OR invoice.DueDate>=@Now)))))
              AND (@Search IS NULL OR origin.DocumentNumber LIKE N'%' + @Search + N'%'
                OR party.DisplayName LIKE N'%' + @Search + N'%' OR party.LegalName LIKE N'%' + @Search + N'%'
                OR party.Identification LIKE N'%' + @Search + N'%'
                OR EXISTS(SELECT 1 FROM dbo.CustomerPaymentApplications application
                  JOIN dbo.Receivables invoice ON invoice.ReceivableId=application.ReceivableId
                  WHERE application.PaymentId=payment.PaymentId
                    AND (@PartySiteId IS NULL OR invoice.PartySiteId=@PartySiteId)
                    AND invoice.DocumentNumber LIKE N'%' + @Search + N'%'));
            IF @PartySiteId IS NOT NULL
            BEGIN
              SELECT filtered.PaymentId,
                COALESCE(SUM(CASE WHEN invoice.PartySiteId=@PartySiteId THEN application.Amount ELSE 0 END),0)
                  +CASE WHEN payment.PartySiteId=@PartySiteId THEN payment.TotalAmount-COALESCE(SUM(application.Amount),0)
                    ELSE 0 END AppliedAmount
              INTO #SiteApplied
              FROM #FilteredPayments filtered JOIN dbo.CustomerPayments payment ON payment.PaymentId=filtered.PaymentId
              LEFT JOIN dbo.CustomerPaymentApplications application ON application.PaymentId=filtered.PaymentId
              LEFT JOIN dbo.Receivables invoice ON invoice.ReceivableId=application.ReceivableId AND invoice.BusinessId=@BusinessId
              GROUP BY filtered.PaymentId,payment.PartySiteId,payment.TotalAmount
              HAVING payment.PartySiteId=@PartySiteId OR MAX(CASE WHEN invoice.PartySiteId=@PartySiteId THEN 1 ELSE 0 END)=1;
              DELETE filtered FROM #FilteredPayments filtered
                WHERE NOT EXISTS(SELECT 1 FROM #SiteApplied siteApplied WHERE siteApplied.PaymentId=filtered.PaymentId);
              UPDATE filtered SET TotalAmount=siteApplied.AppliedAmount
                FROM #FilteredPayments filtered JOIN #SiteApplied siteApplied ON siteApplied.PaymentId=filtered.PaymentId;
            END;
            SELECT COUNT(*),COALESCE(SUM(TotalAmount),0) FROM #FilteredPayments;
            DECLARE @Page TABLE(PaymentId uniqueidentifier PRIMARY KEY);
            INSERT @Page(PaymentId)
            SELECT payment.PaymentId FROM #FilteredPayments filtered
            JOIN dbo.CustomerPayments payment ON payment.PaymentId=filtered.PaymentId
            INNER JOIN accounting.PortfolioPaymentOrigins origin ON origin.PaymentId=payment.PaymentId
              AND origin.SubledgerKind=N'Receivable' AND origin.BusinessId=payment.BusinessId
            INNER JOIN dbo.Businesses business ON business.BusinessId=payment.BusinessId
            LEFT JOIN dbo.Customers customer ON customer.CustomerId=payment.CustomerId AND customer.TenantId=@TenantId
            INNER JOIN dbo.Parties party ON party.PartyId=COALESCE(payment.PartyId,customer.PartyId) AND party.TenantId=@TenantId
            ORDER BY {paymentOrder}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            SELECT payment.PaymentId,origin.DocumentNumber,payment.PaidAt,payment.CurrencyCode,
              filtered.TotalAmount,payment.Status,
              COUNT(application.ReceivableId) AppliedDocumentCount,payment.CustomerId,
              COALESCE(party.DisplayName,party.LegalName,party.Identification) CustomerName,
              origin.SourceDocumentId,origin.SourceDocumentType,origin.SourceGroupId
            FROM @Page page INNER JOIN dbo.CustomerPayments payment ON payment.PaymentId=page.PaymentId
            JOIN #FilteredPayments filtered ON filtered.PaymentId=page.PaymentId
            INNER JOIN accounting.PortfolioPaymentOrigins origin ON origin.PaymentId=payment.PaymentId
              AND origin.SubledgerKind=N'Receivable' AND origin.BusinessId=payment.BusinessId
            INNER JOIN dbo.Businesses business ON business.BusinessId=payment.BusinessId
            LEFT JOIN dbo.Customers customer ON customer.CustomerId=payment.CustomerId AND customer.TenantId=@TenantId
            INNER JOIN dbo.Parties party ON party.PartyId=COALESCE(payment.PartyId,customer.PartyId) AND party.TenantId=@TenantId
            LEFT JOIN dbo.CustomerPaymentApplications application ON application.PaymentId=payment.PaymentId
              AND (@PartySiteId IS NULL OR EXISTS(SELECT 1 FROM dbo.Receivables invoice
                WHERE invoice.ReceivableId=application.ReceivableId AND invoice.PartySiteId=@PartySiteId))
            WHERE payment.BusinessId=@BusinessId AND business.TenantId=@TenantId
            GROUP BY payment.PaymentId,origin.DocumentNumber,payment.PaidAt,payment.CurrencyCode,
              filtered.TotalAmount,payment.Status,payment.CustomerId,
              party.DisplayName,party.LegalName,party.Identification,
              origin.SourceDocumentId,origin.SourceDocumentType,origin.SourceGroupId
            ORDER BY {paymentOrder};
            SELECT tender.PaymentId,tender.LineNumber,tender.MethodCode,tender.Amount,tender.TenderedAmount,
              tender.BankAccountId,tender.Reference,tender.Notes,tender.CardFranchiseCode,tender.ApprovalNumber
            FROM dbo.CustomerPaymentTenders tender INNER JOIN @Page page ON page.PaymentId=tender.PaymentId
            ORDER BY tender.PaymentId,tender.LineNumber;
            SELECT application.PaymentId,application.ReceivableId,receivable.DocumentNumber,application.Amount
            FROM dbo.CustomerPaymentApplications application
            INNER JOIN @Page page ON page.PaymentId=application.PaymentId
            INNER JOIN dbo.Receivables receivable ON receivable.ReceivableId=application.ReceivableId
            WHERE @PartySiteId IS NULL OR receivable.PartySiteId=@PartySiteId
            ORDER BY application.PaymentId,application.LineNumber;
            SELECT applied.PaymentId,SUM(applied.Amount) NetAppliedAmount
            FROM accounting.PortfolioAppliedAmounts(@BusinessId,N'Receivable') applied
            JOIN @Page page ON page.PaymentId=applied.PaymentId
            JOIN dbo.Receivables position ON position.ReceivableId=applied.PositionId AND position.BusinessId=@BusinessId
              AND (@PartySiteId IS NULL OR position.PartySiteId=@PartySiteId)
            GROUP BY applied.PaymentId;
            """,connection);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        command.Parameters.AddWithValue("@TenantId",user.TenantId);
        command.Parameters.AddWithValue("@CustomerId",(object?)query.CustomerId??DBNull.Value);
        command.Parameters.AddWithValue("@PartySiteId",(object?)query.PartySiteId??DBNull.Value);
        command.Parameters.AddWithValue("@Search",(object?)query.Search??DBNull.Value);
        command.Parameters.AddWithValue("@Status",(object?)query.Status??DBNull.Value);
        command.Parameters.AddWithValue("@Overdue",(object?)query.Overdue??DBNull.Value);
        command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());
        AddDateRange(command,query.From,query.To);
        command.Parameters.AddWithValue("@Offset",(query.Page-1)*query.PageSize);
        command.Parameters.AddWithValue("@PageSize",query.PageSize);
        await using var reader=await command.ExecuteReaderAsync(token); await reader.ReadAsync(token);
        var total=reader.GetInt32(0);var totalAmount=reader.GetDecimal(1); await reader.NextResultAsync(token);
        var items=new List<CustomerPaymentHistoryItem>();
        while(await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.GetString(1),
            reader.GetDateTimeOffset(2),reader.GetString(3),reader.GetDecimal(4),reader.GetString(5),reader.GetInt32(6),
            [],[],reader.IsDBNull(7)?null:reader.GetGuid(7),reader.GetString(8),reader.IsDBNull(11)?null:reader.GetGuid(9),
            reader.IsDBNull(11)?null:reader.GetString(10),reader.IsDBNull(11)?null:reader.GetGuid(11)));
        var byPayment=items.ToDictionary(item=>item.PaymentId,_=>new List<CustomerPaymentTenderSnapshot>());
        var applications=items.ToDictionary(item=>item.PaymentId,_=>new List<CustomerPaymentHistoryApplication>());
        await reader.NextResultAsync(token);
        while(await reader.ReadAsync(token)) byPayment[reader.GetGuid(0)].Add(new(
            reader.GetInt32(1),reader.GetString(2),reader.GetDecimal(3),
            reader.IsDBNull(4)?null:reader.GetDecimal(4),reader.IsDBNull(5)?null:reader.GetGuid(5),
            reader.IsDBNull(6)?null:reader.GetString(6),reader.IsDBNull(7)?null:reader.GetString(7),
            reader.IsDBNull(8)?null:reader.GetString(8),reader.IsDBNull(9)?null:reader.GetString(9)));
        await reader.NextResultAsync(token);
        while(await reader.ReadAsync(token)) applications[reader.GetGuid(0)].Add(new(
            reader.GetGuid(1),reader.GetString(2),reader.GetDecimal(3)));
        var netApplied=new Dictionary<Guid,decimal>();
        await reader.NextResultAsync(token);
        while(await reader.ReadAsync(token)) netApplied.Add(reader.GetGuid(0),reader.GetDecimal(1));
        return new(items.Select(item=>item with { Payments=byPayment[item.PaymentId],Applications=applications[item.PaymentId],
                NetAppliedAmount=netApplied.GetValueOrDefault(item.PaymentId) }).ToArray(),
            query.Page,query.PageSize,total,totalAmount);
    }

    public async Task<ReceivableDetail?> GetAsync(ReceivablesUserIdentity user, Guid id, CancellationToken token)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(token);
        await using var header=new SqlCommand("""
            SELECT r.CustomerId,COALESCE(p.DisplayName,p.LegalName,p.Identification),COALESCE(p.Identification,N''),
                   r.SourceDocumentId,r.SourceDocumentType,r.DocumentNumber,r.CurrencyCode,r.OriginalAmount,
                   r.OutstandingAmount,r.DueDate,r.Status,r.PartySiteId,site.Name,r.PartyId,
                   reconciliation.Note
            FROM dbo.Receivables r INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId
            LEFT JOIN dbo.Customers c ON c.CustomerId=r.CustomerId INNER JOIN dbo.Parties p ON p.PartyId=COALESCE(c.PartyId,r.PartyId)
            LEFT JOIN dbo.PartySites site ON site.PartySiteId=r.PartySiteId
            LEFT JOIN dbo.WorkSessionClosureReconciliations reconciliation
              ON r.SourceDocumentType=N'WorkSessionClosureReconciliation'
                AND reconciliation.ReconciliationId=r.SourceDocumentId
            WHERE r.ReceivableId=@Id AND r.BusinessId=@BusinessId AND b.TenantId=@TenantId;
            """,connection);
        header.Parameters.AddWithValue("@Id",id); header.Parameters.AddWithValue("@BusinessId",user.BusinessId); header.Parameters.AddWithValue("@TenantId",user.TenantId);
        Guid? customerId, partyId, partySiteId; Guid sourceId; string name, identification, sourceType, number, currency, status; string? partySiteName, sourceNote; decimal original,balance; DateTimeOffset due;
        await using(var reader=await header.ExecuteReaderAsync(token))
        {
            if(!await reader.ReadAsync(token)) return null;
            customerId=reader.IsDBNull(0)?null:reader.GetGuid(0); name=reader.GetString(1); identification=reader.GetString(2); sourceId=reader.GetGuid(3);
            sourceType=reader.GetString(4); number=reader.GetString(5); currency=reader.GetString(6); original=reader.GetDecimal(7);
            balance=reader.GetDecimal(8); due=reader.GetDateTimeOffset(9); status=reader.GetString(10);
            partySiteId=reader.IsDBNull(11)?null:reader.GetGuid(11); partySiteName=reader.IsDBNull(12)?null:reader.GetString(12);
            partyId=reader.IsDBNull(13)?null:reader.GetGuid(13);
            sourceNote=reader.IsDBNull(14)?null:reader.GetString(14);
        }
        var movements=new List<ReceivableTransactionView>();
        await using var detail=new SqlCommand("""
            SELECT tx.ReceivableTransactionId,tx.TransactionType,tx.Amount,COALESCE(origin.SourceDocumentId,tx.SourceDocumentId),tx.OccurredAt,effect.BalanceChange
            FROM dbo.ReceivableTransactions tx
            CROSS APPLY accounting.PortfolioTransactionEffect(tx.TransactionType,tx.Amount) effect
            LEFT JOIN accounting.PortfolioPaymentOrigins origin ON tx.TransactionType=N'Payment'
              AND origin.PaymentId=tx.SourceDocumentId AND origin.SubledgerKind=N'Receivable'
              AND origin.BusinessId=@BusinessId AND origin.TenantId=@TenantId
            WHERE tx.ReceivableId=@Id ORDER BY tx.OccurredAt,tx.ReceivableTransactionId;
            """,connection);
        detail.Parameters.AddWithValue("@Id",id);
        detail.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        detail.Parameters.AddWithValue("@TenantId",user.TenantId);
        await using(var reader=await detail.ExecuteReaderAsync(token))
            while(await reader.ReadAsync(token)) movements.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetDecimal(2),reader.GetGuid(3),reader.GetDateTimeOffset(4),reader.GetDecimal(5)));
        return new(id,customerId,name,identification,sourceId,sourceType,number,currency,original,balance,due,status,movements,partySiteId,partySiteName,partyId,sourceNote);
    }

    public async Task<CustomerCreditProfile?> GetCreditProfileAsync(ReceivablesUserIdentity user, Guid customerId, CancellationToken token)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(token);
        await using var command=new SqlCommand("""
            SELECT cp.CreditLimit,cp.DefaultDueDays,cp.IsCreditEnabled,
                   COALESCE(SUM(CASE WHEN r.Status IN(N'Open',N'PartiallyPaid') THEN r.OutstandingAmount ELSE 0 END),0)
            FROM dbo.Customers c INNER JOIN dbo.Businesses b ON b.BusinessId=@BusinessId AND b.TenantId=c.TenantId
            LEFT JOIN dbo.CustomerCreditProfiles cp ON cp.CustomerId=c.CustomerId AND cp.BusinessId=@BusinessId
            LEFT JOIN dbo.Receivables r ON r.CustomerId=c.CustomerId AND r.BusinessId=@BusinessId
            WHERE c.CustomerId=@CustomerId AND c.TenantId=@TenantId
            GROUP BY cp.CreditLimit,cp.DefaultDueDays,cp.IsCreditEnabled;
            """,connection);
        command.Parameters.AddWithValue("@CustomerId",customerId); command.Parameters.AddWithValue("@BusinessId",user.BusinessId); command.Parameters.AddWithValue("@TenantId",user.TenantId);
        await using var reader=await command.ExecuteReaderAsync(token); if(!await reader.ReadAsync(token)) return null;
        var limit=reader.IsDBNull(0)?(decimal?)null:reader.GetDecimal(0); var due=reader.IsDBNull(1)?0:reader.GetInt32(1);
        var enabled=!reader.IsDBNull(2)&&reader.GetBoolean(2); var used=reader.GetDecimal(3);
        return new(customerId,limit,due,enabled,used,limit is null?null:decimal.Max(0,limit.Value-used));
    }

    public async Task<CustomerCreditProfile> UpdateCreditProfileAsync(ReceivablesUserIdentity user, Guid customerId, UpdateCustomerCreditProfileRequest request, CancellationToken token)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(token);
        await using var command=new SqlCommand("""
            SET XACT_ABORT ON;
            SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
            BEGIN TRANSACTION;
            BEGIN TRY
            IF NOT EXISTS(SELECT 1 FROM dbo.Customers c INNER JOIN dbo.Businesses b ON b.BusinessId=@BusinessId AND b.TenantId=c.TenantId
                          WHERE c.CustomerId=@CustomerId AND c.TenantId=@TenantId)
                THROW 51300,'The customer is outside the authenticated business.',1;
            IF EXISTS(SELECT 1 FROM dbo.CustomerCreditProfiles WITH(UPDLOCK,HOLDLOCK) WHERE CustomerId=@CustomerId AND BusinessId=@BusinessId)
                UPDATE dbo.CustomerCreditProfiles SET CreditLimit=@Limit,DefaultDueDays=@Days,IsCreditEnabled=@Enabled,
                    UpdatedByUserId=@UserId,UpdatedAt=@Now WHERE CustomerId=@CustomerId AND BusinessId=@BusinessId;
            ELSE
                INSERT dbo.CustomerCreditProfiles(CustomerId,BusinessId,CreditLimit,DefaultDueDays,IsCreditEnabled,UpdatedByUserId,UpdatedAt)
                VALUES(@CustomerId,@BusinessId,@Limit,@Days,@Enabled,@UserId,@Now);
            DECLARE @Cursor BIGINT;
            SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
            FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND Stream=N'Customers';
            INSERT dbo.PosSynchronizationOutboxMessages(
              NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt,
              EntityType,EntityId,ChangeKind)
            VALUES(@NotificationId,@BusinessId,N'Customers',@Cursor,@Now,
                   N'Customer',@CustomerId,N'Upsert');
            COMMIT TRANSACTION;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
            """,connection);
        command.Parameters.AddWithValue("@CustomerId",customerId); command.Parameters.AddWithValue("@BusinessId",user.BusinessId); command.Parameters.AddWithValue("@TenantId",user.TenantId);
        command.Parameters.AddWithValue("@Limit",(object?)request.CreditLimit??DBNull.Value); command.Parameters.AddWithValue("@Days",request.DefaultDueDays);
        command.Parameters.AddWithValue("@Enabled",request.IsCreditEnabled);
        command.Parameters.AddWithValue("@UserId",user.UserId); command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());
        command.Parameters.AddWithValue("@NotificationId",ids.NewId());
        try { await command.ExecuteNonQueryAsync(token); } catch(SqlException ex) when(ex.Number==51300) { throw new ReceivablesValidationException(ex.Message); }
        return (await GetCreditProfileAsync(user,customerId,token))!;
    }

    public async Task<CustomerPaymentAcceptance> AcceptPaymentAsync(ReceivablesUserIdentity user,string key,
        ConfirmCustomerPaymentRequest request,ReceivableSettlement settlement,
        PaymentTenderBreakdown tenders,CancellationToken token)
    {
        var hash=SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { request.PaymentId,request.BusinessId,
            request.CustomerId,request.WorkSessionId,request.PaidAt,request.CurrencyCode,request.Notes,
            settlement.Allocations,tenders.Tenders }));
        for(var attempt=1;;attempt++)
        {
            try
            {
                return await AcceptPaymentAttemptAsync(user,key,request,settlement,tenders,hash,token);
            }
            catch(SqlException exception) when(exception.Number==1205&&attempt<3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25*attempt),timeProvider,token);
            }
            catch(SqlException exception) when(exception.Number==1205)
            {
                throw new ReceivablesConflictException(
                    "El saldo cambió mientras se registraba el pago. Recarga la cartera e inténtalo de nuevo.");
            }
        }
    }

    public async Task<ImportPreexistingReceivablesAcceptance> ImportPreexistingAsync(
        ReceivablesUserIdentity user,ImportPreexistingReceivablesRequest request,CancellationToken token)
    {
        await using var connection=connections.Create();await connection.OpenAsync(token);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token);
        try
        {
            var resolvedCustomers=new Dictionary<Guid,(Guid CustomerId,Guid PartySiteId)>();
            await using(var validate=new SqlCommand("""
                DECLARE @Input TABLE(ReceivableId uniqueidentifier PRIMARY KEY,CustomerId uniqueidentifier NULL,
                  CustomerIdentification nvarchar(64) NULL,
                  PartySiteId uniqueidentifier NULL,DocumentNumber nvarchar(80),CounterpartAccountId uniqueidentifier);
                INSERT @Input SELECT ReceivableId,CustomerId,CustomerIdentification,PartySiteId,DocumentNumber,CounterpartAccountId
                FROM OPENJSON(@Items) WITH(ReceivableId uniqueidentifier,CustomerId uniqueidentifier,CustomerIdentification nvarchar(64),
                  PartySiteId uniqueidentifier,DocumentNumber nvarchar(80),CounterpartAccountId uniqueidentifier);
                IF (SELECT COUNT(*) FROM @Input)<>@Count THROW 51320,N'El lote contiene identificadores duplicados.',1;
                IF EXISTS(SELECT 1 FROM @Input GROUP BY DocumentNumber HAVING COUNT(*)>1)
                  THROW 51326,N'La plantilla contiene números de factura duplicados.',1;
                IF EXISTS(SELECT 1 FROM @Input i OUTER APPLY(SELECT TOP(1)c.CustomerId,c.PartyId FROM dbo.Customers c
                  JOIN dbo.Parties p ON p.PartyId=c.PartyId WHERE c.TenantId=@TenantId AND c.IsActive=1
                    AND ((i.CustomerId IS NOT NULL AND c.CustomerId=i.CustomerId) OR (i.CustomerId IS NULL
                      AND (p.Identification=i.CustomerIdentification OR p.NormalizedIdentification=i.CustomerIdentification)))) c
                  WHERE c.CustomerId IS NULL)
                  THROW 51321,N'Un cliente no pertenece al negocio o está inactivo.',1;
                IF EXISTS(SELECT 1 FROM @Input i CROSS APPLY(SELECT TOP(1)c.CustomerId,c.PartyId FROM dbo.Customers c
                  JOIN dbo.Parties p ON p.PartyId=c.PartyId WHERE c.TenantId=@TenantId AND c.IsActive=1
                    AND ((i.CustomerId IS NOT NULL AND c.CustomerId=i.CustomerId) OR (i.CustomerId IS NULL
                      AND (p.Identification=i.CustomerIdentification OR p.NormalizedIdentification=i.CustomerIdentification)))) c
                  LEFT JOIN dbo.PartySites site ON site.PartySiteId=i.PartySiteId AND site.PartyId=c.PartyId AND site.IsActive=1
                  WHERE i.PartySiteId IS NOT NULL AND site.PartySiteId IS NULL)
                  THROW 51322,N'Una sede no pertenece al cliente indicado.',1;
                IF EXISTS(SELECT 1 FROM @Input i CROSS APPLY(SELECT TOP(1)c.PartyId FROM dbo.Customers c
                  JOIN dbo.Parties p ON p.PartyId=c.PartyId WHERE c.TenantId=@TenantId AND c.IsActive=1
                    AND ((i.CustomerId IS NOT NULL AND c.CustomerId=i.CustomerId) OR (i.CustomerId IS NULL
                      AND (p.Identification=i.CustomerIdentification OR p.NormalizedIdentification=i.CustomerIdentification)))) c
                  WHERE i.PartySiteId IS NULL AND NOT EXISTS(SELECT 1 FROM dbo.PartySites site
                    WHERE site.PartyId=c.PartyId AND site.IsActive=1))
                  THROW 51325,N'El cliente no tiene una sede activa para importar cartera.',1;
                IF EXISTS(SELECT 1 FROM @Input i LEFT JOIN dbo.AccountingAccounts account
                  ON account.AccountId=i.CounterpartAccountId AND account.TenantId=@TenantId
                  AND account.IsActive=1 AND account.AllowsPosting=1 WHERE account.AccountId IS NULL)
                  THROW 51323,N'Una cuenta contrapartida no es imputable o no pertenece al tenant.',1;
                IF EXISTS(SELECT 1 FROM @Input i JOIN dbo.Receivables r ON r.BusinessId=@BusinessId
                  AND (r.ReceivableId=i.ReceivableId OR r.DocumentNumber=i.DocumentNumber)
                  WHERE r.ReceivableId<>i.ReceivableId OR r.SourceDocumentType<>N'PreexistingReceivable')
                  THROW 51324,N'La factura ya existe en cartera.',1;
                SELECT i.ReceivableId,c.CustomerId,site.PartySiteId FROM @Input i CROSS APPLY(SELECT TOP(1)c.CustomerId,c.PartyId FROM dbo.Customers c
                  JOIN dbo.Parties p ON p.PartyId=c.PartyId WHERE c.TenantId=@TenantId AND c.IsActive=1
                    AND ((i.CustomerId IS NOT NULL AND c.CustomerId=i.CustomerId) OR (i.CustomerId IS NULL
                      AND (p.Identification=i.CustomerIdentification OR p.NormalizedIdentification=i.CustomerIdentification)))) c
                  CROSS APPLY(SELECT TOP(1)site.PartySiteId FROM dbo.PartySites site
                    WHERE site.PartyId=c.PartyId AND site.IsActive=1
                      AND (i.PartySiteId IS NULL OR site.PartySiteId=i.PartySiteId)
                    ORDER BY site.IsPrimary DESC,site.PartySiteId) site;
                """,connection,transaction))
            {
                validate.Parameters.AddWithValue("@Items",JsonSerializer.Serialize(request.Items));validate.Parameters.AddWithValue("@Count",request.Items.Count);
                validate.Parameters.AddWithValue("@BusinessId",user.BusinessId);validate.Parameters.AddWithValue("@TenantId",user.TenantId);
                try{await using var reader=await validate.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))resolvedCustomers.Add(reader.GetGuid(0),(reader.GetGuid(1),reader.GetGuid(2)));}catch(SqlException error)when(error.Number is >=51320 and <=51326){throw new ReceivablesValidationException(error.Message);}
            }
            var payloads=request.Items.Select(item=>new PreexistingReceivablePayload(user.TenantId,user.BusinessId,
                item.ReceivableId,resolvedCustomers[item.ReceivableId].CustomerId,resolvedCustomers[item.ReceivableId].PartySiteId,user.UserId,item.DocumentNumber,item.IssuedAt,
                item.DueDate,item.Amount,item.CounterpartAccountId,item.Notes)).ToArray();
            var now=timeProvider.GetUtcNow();
            var sources=payloads.Select(payload=>new SqlAccountingPostingJobWriter.Source(user.TenantId,user.BusinessId,
                payload.ReceivableId,ReceivablesDocumentTypes.PreexistingReceivable,
                PreexistingReceivableContractSerializer.Serialize(payload),now,ids.NewId())).ToArray();
            await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection,transaction,sources,now,
                AccountingJobRequirement.PreserveCommercialEffects,token);
            await transaction.CommitAsync(token);
            return new(payloads.Length,payloads.Select(x=>x.ReceivableId).ToArray());
        }
        catch(SqlException error) when(error.Number==51732)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new ReceivablesConflictException("Una factura del lote ya fue aceptada con datos distintos.");
        }
        catch{await transaction.RollbackAsync(CancellationToken.None);throw;}
    }

    private async Task<CustomerPaymentAcceptance> AcceptPaymentAttemptAsync(ReceivablesUserIdentity user,string key,
        ConfirmCustomerPaymentRequest request,ReceivableSettlement settlement,
        PaymentTenderBreakdown tenders,byte[] hash,CancellationToken token)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(token);
        await using var tx=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token);
        try
        {
            var replay=await FindReplayAsync(connection,tx,user.TenantId,user.BusinessId,request.PaymentId,key,hash,settlement,token); if(replay is not null){await tx.CommitAsync(token);return replay;}
            var now=timeProvider.GetUtcNow();
            AuralyDocumentNumberAssignment number;
            try
            {
                number=await SqlOperationalDocumentAllocator.AllocateExistingNumberAsync(connection,tx,
                    user.TenantId,user.BusinessId,AuralyDocumentTypes.ReceivablePayment,now,token);
            }
            catch(SqlException error) when(error.Number is 51730 or 51731)
            {
                throw new ReceivablesValidationException(error.Message,error);
            }
            var movementId=ids.NewId();
            var paymentSnapshots=tenders.Tenders.Select((x,i)=>new CustomerPaymentTenderSnapshot(
                i+1,x.MethodCode,x.Amount,x.TenderedAmount,x.BankAccountId,x.Reference,x.Notes,
                x.CardFranchiseCode,x.ApprovalNumber)).ToArray();
            var payload=new CustomerPaymentDocumentPayload(user.TenantId,user.BusinessId,request.PaymentId,request.CustomerId,user.UserId,
                request.WorkSessionId,number.FullNumber,number.SeriesId,number.Prefix,number.SeriesCode,number.Consecutive,request.PaidAt,
                request.CurrencyCode,request.Notes,settlement.TotalAmount,
                settlement.Allocations.Select((x,i)=>new CustomerPaymentAllocationSnapshot(i+1,x.ReceivableId,x.Amount)).ToArray(),
                paymentSnapshots);
            var json=CustomerPaymentContractSerializer.Serialize(payload);
            await AcceptPaymentRecordAsync(connection,tx,user,json,key,hash,now,token);
            await SqlAccountingPostingJobWriter.InsertSourceAsync(connection,tx,user.TenantId,user.BusinessId,
                request.PaymentId,ReceivablesDocumentTypes.Payment,json,request.PaidAt,ids,timeProvider,token,
                AccountingJobRequirement.PreserveCommercialEffects,movementId);
            await tx.CommitAsync(token); return new(request.PaymentId,movementId,number.FullNumber,"Accepted",false);
        }
        catch { if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None); throw; }
    }

    private static async Task AcceptPaymentRecordAsync(
        SqlConnection connection, SqlTransaction transaction, ReceivablesUserIdentity user,
        string payloadJson, string idempotencyKey, byte[] requestHash,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("accounting.PortfolioPaymentAccept", connection, transaction)
        { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@SubledgerKind", "Receivable");
        command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = payloadJson;
        command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
        command.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = requestHash;
        command.Parameters.AddWithValue("@AcceptedAt", now);
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (SqlException error) when (error.Number is >= 51400 and <= 51402)
        {
            if (error.Number == 51402) throw new ReceivablesConflictException(error.Message, error);
            throw new ReceivablesValidationException(error.Message, error);
        }
    }

    private static async Task<CustomerPaymentAcceptance?> FindReplayAsync(SqlConnection c,SqlTransaction t,Guid tenantId,Guid businessId,Guid paymentId,string key,byte[] hash,ReceivableSettlement settlement,CancellationToken token)
    {
        await using var command=new SqlCommand("""
            -- Positions precede replay/payment/job ranges, as in the financial processor.
            DECLARE @Positions TABLE(ReceivableId uniqueidentifier PRIMARY KEY);
            INSERT @Positions SELECT ReceivableId FROM OPENJSON(@Allocations)
              WITH(ReceivableId uniqueidentifier);
            DECLARE @LockedPosition uniqueidentifier;
            SELECT @LockedPosition=r.ReceivableId FROM @Positions input
            JOIN dbo.Receivables r WITH(UPDLOCK,HOLDLOCK) ON r.ReceivableId=input.ReceivableId
              AND r.BusinessId=@BusinessId
            JOIN dbo.Businesses business ON business.BusinessId=r.BusinessId AND business.TenantId=@TenantId
            ORDER BY input.ReceivableId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
            SELECT p.PaymentId,origin.DocumentNumber,p.Status,j.AccountingPostingJobId,p.PayloadHash,s.SourceDocumentId,origin.SourceGroupId FROM dbo.CustomerPayments p
            INNER JOIN dbo.Businesses business ON business.BusinessId=p.BusinessId AND business.TenantId=@TenantId
            INNER JOIN accounting.PortfolioPaymentOrigins origin ON origin.PaymentId=p.PaymentId
              AND origin.SubledgerKind=N'Receivable' AND origin.BusinessId=p.BusinessId
            LEFT JOIN dbo.AccountingPostingJobs j ON j.SourceDocumentId=origin.SourceDocumentId AND j.SourceDocumentType=origin.SourceDocumentType
              AND j.TenantId=@TenantId AND j.BusinessId=@BusinessId
            LEFT JOIN dbo.AccountingSourceDocuments s ON s.SourceDocumentId=origin.SourceDocumentId AND s.SourceDocumentType=origin.SourceDocumentType
              AND s.TenantId=@TenantId AND s.BusinessId=@BusinessId
            WHERE p.BusinessId=@BusinessId AND (p.PaymentId=@PaymentId OR p.IdempotencyKey=@Key);
            """,c,t);command.Parameters.AddWithValue("@BusinessId",businessId);command.Parameters.AddWithValue("@PaymentId",paymentId);command.Parameters.AddWithValue("@Key",key);
        command.Parameters.Add("@Allocations",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(settlement.Allocations);
        command.Parameters.AddWithValue("@TenantId",tenantId);
        await using var reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))return null;
        if(!reader.IsDBNull(6))
            throw new ReceivablesConflictException("El recaudo pertenece a un comprobante. Consulta o reintenta ese comprobante; no se aceptó un recaudo independiente.");
        if(!reader.GetFieldValue<byte[]>(4).AsSpan().SequenceEqual(hash))throw new ReceivablesConflictException("The idempotency key or PaymentId was reused with another payload.");
        if(reader.IsDBNull(3)||reader.IsDBNull(5))
            throw new ReceivablesConflictException("El pago ya está registrado, pero su fuente o trabajo financiero requieren revisión. No se aceptó otro pago.");
        var acceptance=new CustomerPaymentAcceptance(reader.GetGuid(0),reader.GetGuid(3),reader.GetString(1),reader.GetString(2),true);
        if(await reader.ReadAsync(token))
            throw new ReceivablesConflictException("El identificador del pago y la clave de reintento pertenecen a pagos diferentes.");
        return acceptance;
    }

    private static void AddQuery(SqlCommand c,ReceivablesUserIdentity u,ReceivableQuery q,DateTimeOffset now){c.Parameters.AddWithValue("@BusinessId",u.BusinessId);c.Parameters.AddWithValue("@TenantId",u.TenantId);c.Parameters.AddWithValue("@CustomerId",(object?)q.CustomerId??DBNull.Value);c.Parameters.AddWithValue("@PartySiteId",(object?)q.PartySiteId??DBNull.Value);c.Parameters.AddWithValue("@Status",(object?)q.Status??DBNull.Value);c.Parameters.AddWithValue("@OutstandingOnly",q.OutstandingOnly);c.Parameters.AddWithValue("@Overdue",(object?)q.Overdue??DBNull.Value);c.Parameters.AddWithValue("@Search",(object?)q.Search??DBNull.Value);c.Parameters.AddWithValue("@Now",now);AddDateRange(c,q.From,q.To);}
    private static void AddDateRange(SqlCommand command,DateOnly? from,DateOnly? to)
    {
        command.Parameters.AddWithValue("@From",(object?)from?.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc)??DBNull.Value);
        command.Parameters.AddWithValue("@To",(object?)to?.AddDays(1).ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc)??DBNull.Value);
    }
}
