using Auraly.BuildingBlocks.Domain.Time;
using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Auraly.Commerce.Accounting.Infrastructure;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Payables;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Domain.Payments;
using Auraly.Contracts.Payables;
using Auraly.Domain.Payables;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPayablesStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IPayablesStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<PayableExpenseConceptPage> ListExpenseConceptsAsync(
        PayablesUserIdentity user, string? search, int page, int pageSize, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT COUNT(*)
            FROM dbo.ExpenseConcepts concept
            JOIN dbo.Businesses business ON business.BusinessId=concept.BusinessId
            WHERE concept.BusinessId=@BusinessId AND business.TenantId=@TenantId
              AND (@Search IS NULL OR concept.Name LIKE N'%' + @Search + N'%');
            SELECT concept.ExpenseConceptId,concept.Name
            FROM dbo.ExpenseConcepts concept
            JOIN dbo.Businesses business ON business.BusinessId=concept.BusinessId
            WHERE concept.BusinessId=@BusinessId AND business.TenantId=@TenantId
              AND (@Search IS NULL OR concept.Name LIKE N'%' + @Search + N'%')
            ORDER BY concept.Name,concept.ExpenseConceptId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@PageSize", pageSize);
        var items = new List<PayableExpenseConceptOption>();
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        var totalCount = reader.GetInt32(0);
        await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0), reader.GetString(1)));
        return new(items, page, pageSize, totalCount);
    }
    public async Task<SupplierPortfolioPage> ListSuppliersAsync(PayablesUserIdentity user,
        SupplierPortfolioQuery query,CancellationToken token)
    {
        var order = SqlPagedSort.Build(query.SortBy, query.SortDirection,
            new Dictionary<string, string>
            {
                ["name"] = "SupplierName", ["invoiceCount"] = "InvoiceCount",
                ["originalAmount"] = "OriginalAmount", ["paidAmount"] = "PaidAmount",
                ["outstandingAmount"] = "OutstandingAmount", ["overdueAmount"] = "OverdueAmount",
                ["supplierCreditAmount"] = "SupplierCreditAmount"
            }, "name", "asc", "PartySiteName", "PartySiteId", "SupplierId", "CurrencyCode");
        await using var connection=connections.Create();await connection.OpenAsync(token);
        var zone=await SqlBusinessLocalDates.ReadTimeZoneAsync(connection,user.TenantId,user.BusinessId,token)
            ?? throw new PayablesForbiddenException("La sede no está disponible para consultar cartera.");
        var referenceDate=BusinessCalendar.DateAt(timeProvider.GetUtcNow(),zone);
        var referenceStart=SqlBusinessLocalDates.StartOfDay(referenceDate,zone);
        await using var command=new SqlCommand($"""
            WITH Paid AS(
              SELECT application.PositionId PayableId,SUM(application.Amount) PaidAmount
              FROM accounting.PortfolioAppliedAmounts(@BusinessId,N'Payable') application
              JOIN dbo.Payables scoped ON scoped.PayableId=application.PositionId
              WHERE scoped.BusinessId=@BusinessId
              GROUP BY application.PositionId),
            Credits AS(
              SELECT SupplierId,PartySiteId,SUM(AvailableAmount) CreditAmount
              FROM dbo.SupplierCredits WHERE BusinessId=@BusinessId AND AvailableAmount>0
              GROUP BY SupplierId,PartySiteId),
            PayableGrouped AS(
              SELECT p.SupplierId,p.PartySiteId,site.Name PartySiteName,
                s.Name SupplierName,COALESCE(s.Identification,N'') Identification,
                p.CurrencyCode,
                SUM(CASE WHEN p.OutstandingAmount>0 THEN 1 ELSE 0 END) InvoiceCount,
                SUM(p.OriginalAmount) OriginalAmount,
                SUM(COALESCE(paid.PaidAmount,0)) PaidAmount,SUM(p.OutstandingAmount) OutstandingAmount,
                SUM(CASE WHEN p.OutstandingAmount>0 AND p.DueDate<@Now THEN p.OutstandingAmount ELSE 0 END) OverdueAmount
              FROM dbo.Payables p JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
              JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId
              LEFT JOIN dbo.PartySites site ON site.PartySiteId=p.PartySiteId AND site.PartyId=s.PartyId
              LEFT JOIN Paid paid ON paid.PayableId=p.PayableId
              WHERE p.BusinessId=@BusinessId AND b.TenantId=@TenantId
                AND (@SupplierId IS NULL OR p.SupplierId=@SupplierId)
                AND (@PartySiteId IS NULL OR p.PartySiteId=@PartySiteId)
                AND (@Status IS NULL OR p.Status=@Status)
                AND (@From IS NULL OR p.CreatedAt>=@From)
                AND (@To IS NULL OR p.CreatedAt<@To)
                AND (@Overdue IS NULL OR (@Overdue=1 AND p.OutstandingAmount>0 AND p.DueDate<@Now)
                  OR (@Overdue=0 AND (p.OutstandingAmount=0 OR p.DueDate>=@Now)))
                AND (@Search IS NULL OR s.Name LIKE N'%' + @Search + N'%' OR s.Identification LIKE N'%' + @Search + N'%'
                  OR p.DocumentNumber LIKE N'%' + @Search + N'%')
              GROUP BY p.SupplierId,p.PartySiteId,site.Name,s.Name,s.Identification,p.CurrencyCode),
            Grouped AS(
              SELECT * FROM PayableGrouped
              UNION ALL
              SELECT credit.SupplierId,credit.PartySiteId,site.Name,supplier.Name,
                COALESCE(supplier.Identification,N''),N'COP',0,0,0,0,0
              FROM Credits credit
              JOIN dbo.Suppliers supplier ON supplier.SupplierId=credit.SupplierId
              JOIN dbo.Businesses business ON business.BusinessId=@BusinessId AND business.TenantId=@TenantId
              LEFT JOIN dbo.PartySites site ON site.PartySiteId=credit.PartySiteId AND site.PartyId=supplier.PartyId
              WHERE NOT EXISTS (SELECT 1 FROM PayableGrouped existing
                WHERE existing.SupplierId=credit.SupplierId AND existing.PartySiteId=credit.PartySiteId)
                AND (@SupplierId IS NULL OR credit.SupplierId=@SupplierId)
                AND (@PartySiteId IS NULL OR credit.PartySiteId=@PartySiteId)
                AND @Status IS NULL AND @From IS NULL AND @To IS NULL
                AND (@Search IS NULL OR supplier.Name LIKE N'%' + @Search + N'%'
                  OR supplier.Identification LIKE N'%' + @Search + N'%'
                  OR site.Name LIKE N'%' + @Search + N'%')),
            Portfolio AS(
              SELECT grouped.*,
                CASE WHEN grouped.CurrencyCode=COALESCE(
                  MAX(CASE WHEN grouped.CurrencyCode=N'COP' THEN N'COP' END)
                    OVER(PARTITION BY grouped.SupplierId,grouped.PartySiteId),
                  MIN(grouped.CurrencyCode) OVER(PARTITION BY grouped.SupplierId,grouped.PartySiteId))
                THEN COALESCE(credits.CreditAmount,0) ELSE 0 END SupplierCreditAmount
              FROM Grouped grouped
              LEFT JOIN Credits credits ON credits.SupplierId=grouped.SupplierId
                AND credits.PartySiteId=grouped.PartySiteId)
            SELECT * INTO #Portfolio FROM Portfolio
            WHERE @Overdue IS NULL OR (@Overdue=1 AND OverdueAmount>0) OR (@Overdue=0 AND OverdueAmount=0);
            SELECT CASE WHEN GROUPING(CurrencyCode)=1 THEN NULL ELSE CurrencyCode END,
              COUNT(*),COALESCE(SUM(InvoiceCount),0),COALESCE(SUM(SupplierCreditAmount),0),
              COALESCE(SUM(OutstandingAmount),0),COALESCE(SUM(OverdueAmount),0)
            FROM #Portfolio GROUP BY GROUPING SETS ((CurrencyCode),());
            SELECT SupplierId,SupplierName,Identification,InvoiceCount,OriginalAmount,PaidAmount,OutstandingAmount,OverdueAmount,SupplierCreditAmount,CurrencyCode,PartySiteId,PartySiteName
            FROM #Portfolio
            ORDER BY {order}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """,connection);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@TenantId",user.TenantId);command.Parameters.AddWithValue("@Search",(object?)query.Search??DBNull.Value);command.Parameters.AddWithValue("@Overdue",(object?)query.Overdue??DBNull.Value);command.Parameters.AddWithValue("@SupplierId",(object?)query.SupplierId??DBNull.Value);command.Parameters.AddWithValue("@PartySiteId",(object?)query.PartySiteId??DBNull.Value);command.Parameters.AddWithValue("@Status",(object?)query.Status??DBNull.Value);SqlBusinessLocalDates.BindDateRange(command,query.From,query.To,zone,query.DateRangeVersion);command.Parameters.AddWithValue("@Now",referenceStart);command.Parameters.AddWithValue("@Offset",(query.Page-1)*query.PageSize);command.Parameters.AddWithValue("@PageSize",query.PageSize);
        await using var reader=await command.ExecuteReaderAsync(token);
        var count=0;var invoices=0;var supplierCredit=0m;
        var currencyTotals=new List<PayableCurrencyTotal>();
        while(await reader.ReadAsync(token))
        {
            if(reader.IsDBNull(0))
            {
                count=reader.GetInt32(1);invoices=reader.GetInt32(2);supplierCredit=reader.GetDecimal(3);
            }
            else currencyTotals.Add(new(reader.GetString(0),reader.GetDecimal(4),reader.GetDecimal(5)));
        }
        await reader.NextResultAsync(token);
        var items=new List<SupplierPortfolioItem>();while(await reader.ReadAsync(token))items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetInt32(3),reader.GetDecimal(4),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetDecimal(7),reader.GetDecimal(8),reader.GetString(9),reader.IsDBNull(10)?null:reader.GetGuid(10),reader.IsDBNull(11)?null:reader.GetString(11)));
        var cop=currencyTotals.FirstOrDefault(item=>item.CurrencyCode=="COP");
        return new SupplierPortfolioPage(items,query.Page,query.PageSize,count,
            cop?.OutstandingAmount??0,cop?.OverdueAmount??0,invoices,supplierCredit)
            {CurrencyTotals=currencyTotals};
    }

    public async Task<PayablePage> ListAsync(
        PayablesUserIdentity user,
        PayableQuery query,
        CancellationToken cancellationToken)
    {
        var sortColumns = new Dictionary<string, string>
        {
            ["default"] = "CASE WHEN p.OutstandingAmount>0 AND p.DueDate<@Now THEN 0 ELSE 1 END,p.DueDate",
            ["documentNumber"] = "p.DocumentNumber", ["dueDate"] = "p.DueDate",
            ["originalAmount"] = "p.OriginalAmount", ["outstandingAmount"] = "p.OutstandingAmount",
            ["status"] = "CASE WHEN p.OutstandingAmount>0 AND p.DueDate<@Now THEN N'Vencida' WHEN p.Status=N'Open' THEN N'Pendiente' WHEN p.Status=N'PartiallyPaid' THEN N'Pago parcial' WHEN p.Status=N'Paid' THEN N'Pagada' ELSE N'Cancelada' END"
        };
        var order = SqlPagedSort.Build(query.SortBy, query.SortDirection, sortColumns,
            "default", "asc", "p.PayableId");
        var pageOrder = SqlPagedSort.Build(query.SortBy, query.SortDirection,
            sortColumns.ToDictionary(item => item.Key,
                item => item.Value.Replace("p.", "page.", StringComparison.Ordinal)),
            "default", "asc", "page.PayableId");
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var zone=await SqlBusinessLocalDates.ReadTimeZoneAsync(connection,user.TenantId,user.BusinessId,cancellationToken)
            ?? throw new PayablesForbiddenException("La sede no está disponible para consultar cartera.");
        var referenceDate=BusinessCalendar.DateAt(timeProvider.GetUtcNow(),zone);
        var referenceStart=SqlBusinessLocalDates.StartOfDay(referenceDate,zone);
        const string filters = """
            p.BusinessId=@BusinessId
            AND b.TenantId=@TenantId
            AND (@PayableId IS NULL OR p.PayableId=@PayableId)
            AND (@SupplierId IS NULL OR p.SupplierId=@SupplierId)
            AND (@PartySiteId IS NULL OR p.PartySiteId=@PartySiteId)
            AND (@ConceptId IS NULL OR EXISTS (
                SELECT 1 FROM dbo.Expenses expense
                WHERE p.SourceDocumentType=N'Expense' AND expense.ExpenseId=p.SourceDocumentId
                  AND expense.BusinessId=p.BusinessId AND (expense.ExpenseConceptId=@ConceptId OR EXISTS(
                    SELECT 1 FROM dbo.AccountingSourceDocuments source
                    CROSS APPLY OPENJSON(source.PayloadJson,'$.lines') WITH(ConceptId uniqueidentifier '$.conceptId') line
                    WHERE source.SourceDocumentId=expense.ExpenseId AND source.SourceDocumentType=N'Expense'
                      AND source.BusinessId=expense.BusinessId AND line.ConceptId=@ConceptId))))
            AND (@Status IS NULL OR p.Status=@Status)
            AND (@From IS NULL OR p.CreatedAt>=@From)
            AND (@To IS NULL OR p.CreatedAt<@To)
            AND (@OutstandingOnly=0 OR p.OutstandingAmount>0)
            AND (@Overdue IS NULL OR
                 (@Overdue=1 AND p.OutstandingAmount>0 AND p.DueDate<@Now) OR
                 (@Overdue=0 AND (p.OutstandingAmount=0 OR p.DueDate>=@Now)))
            AND (@Search IS NULL OR p.DocumentNumber LIKE N'%' + @Search + N'%'
                 OR s.Name LIKE N'%' + @Search + N'%'
                 OR s.Identification LIKE N'%' + @Search + N'%')
            """;
        var countSql = $"""
            SELECT p.PayableId,p.CurrencyCode,p.OriginalAmount,p.OutstandingAmount,p.DueDate
            INTO #FilteredPayables FROM dbo.Payables p
            INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
            INNER JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId WHERE {filters};
            CREATE UNIQUE CLUSTERED INDEX IX_FilteredPayables ON #FilteredPayables(PayableId);
            WITH Paid AS(SELECT application.PositionId,SUM(application.Amount) Amount
              FROM accounting.PortfolioAppliedAmounts(@BusinessId,N'Payable') application
              JOIN #FilteredPayables scoped ON scoped.PayableId=application.PositionId GROUP BY application.PositionId)
            SELECT CASE WHEN GROUPING(p.CurrencyCode)=1 THEN NULL ELSE p.CurrencyCode END,
                   COUNT(*),COALESCE(SUM(p.OutstandingAmount),0),
                   COALESCE(SUM(CASE WHEN p.OutstandingAmount>0 AND p.DueDate<@Now
                                     THEN p.OutstandingAmount ELSE 0 END),0),
                   COALESCE(SUM(p.OriginalAmount),0),COALESCE(SUM(paid.Amount),0)
            FROM #FilteredPayables p LEFT JOIN Paid paid ON paid.PositionId=p.PayableId
            GROUP BY GROUPING SETS ((p.CurrencyCode),());
            """;
        var totalCount = 0;
        var currencyTotals = new List<PayableCurrencyTotal>();
        await using (var command = new SqlCommand(countSql, connection))
        {
            AddQueryParameters(command, user, query, referenceStart, zone);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0)) totalCount = reader.GetInt32(1);
                else currencyTotals.Add(new(reader.GetString(0),reader.GetDecimal(2),reader.GetDecimal(3),reader.GetDecimal(4),reader.GetDecimal(5)));
            }
        }

        var dataSql = $"""
            SELECT p.PayableId,p.SupplierId,s.Name SupplierName,p.DocumentNumber,p.CurrencyCode,
                   p.OriginalAmount,p.OutstandingAmount,p.DueDate,p.Status,p.CreatedAt,
                   CASE WHEN p.OutstandingAmount>0 AND p.DueDate<@Now THEN CAST(1 AS BIT)
                        ELSE CAST(0 AS BIT) END IsOverdue,
                   p.PartySiteId,s.PartyId SupplierPartyId,p.SourceDocumentId,p.SourceDocumentType,p.InvoiceIssuedAt
            INTO #Page
            FROM dbo.Payables p
            INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
            INNER JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId
            WHERE {filters}
            ORDER BY {order}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            -- Materialize each page enrichment independently. Expanding the
            -- applied-money union and receipt-discount JSON/returns do not
            -- need to expand together in the final detail join plan.
            SELECT application.PositionId PayableId,SUM(application.Amount) PaidAmount
            INTO #PagePaid
            FROM accounting.PortfolioAppliedAmounts(@BusinessId,N'Payable') application
            JOIN #Page selected ON selected.PayableId=application.PositionId
            GROUP BY application.PositionId;
            SELECT DISTINCT SourceDocumentId INTO #PageReceiptIds FROM #Page
            WHERE SourceDocumentType=N'GoodsReceipt';
            SELECT receipt.SourceDocumentId,discount.EligibleAmount,discount.Anticipated
            INTO #PageDiscounts FROM #PageReceiptIds receipt
            CROSS APPLY accounting.GoodsReceiptFinancialDiscount(@BusinessId,receipt.SourceDocumentId) discount;
            SELECT page.PayableId,page.SupplierId,page.SupplierName,page.DocumentNumber,
              page.CurrencyCode,page.OriginalAmount,page.OutstandingAmount,page.DueDate,
              page.Status,page.CreatedAt,page.IsOverdue,concept.Name ExpenseConceptName,
              COALESCE(paid.PaidAmount,0),page.PartySiteId,site.Name PartySiteName,
              CASE WHEN page.SourceDocumentType=N'GoodsReceipt' AND discount.EligibleAmount>0
                AND decided.PayableId IS NULL THEN discount.EligibleAmount END FinancialDiscountEligibleAmount,
              CASE WHEN page.SourceDocumentType=N'GoodsReceipt' AND discount.EligibleAmount>0
                AND decided.PayableId IS NULL THEN discount.Anticipated END FinancialDiscountAnticipated,
              CASE WHEN pending.PurchaseReturnId IS NULL THEN CONVERT(bit,0) ELSE CONVERT(bit,1) END FinancialDiscountPendingReturn,
              COALESCE(page.InvoiceIssuedAt,issue.IssuedAt),page.SourceDocumentType,
              COALESCE(fiscal.AuralyDocumentNumber,expense.DocumentNumber,receipt.DocumentNumber),fiscal.FiscalNumber
            FROM #Page page
            LEFT JOIN dbo.FiscalDocuments fiscal ON fiscal.DocumentId=page.SourceDocumentId AND fiscal.BusinessId=@BusinessId
            LEFT JOIN dbo.GoodsReceipts receipt ON page.SourceDocumentType=N'GoodsReceipt'
              AND receipt.GoodsReceiptId=page.SourceDocumentId AND receipt.BusinessId=@BusinessId
            LEFT JOIN dbo.PartySites site ON site.PartySiteId=page.PartySiteId AND site.PartyId=page.SupplierPartyId
            LEFT JOIN dbo.Expenses expense ON page.SourceDocumentType=N'Expense'
              AND expense.ExpenseId=page.SourceDocumentId AND expense.BusinessId=@BusinessId
            LEFT JOIN dbo.ExpenseConcepts concept ON concept.ExpenseConceptId=expense.ExpenseConceptId
              AND concept.BusinessId=@BusinessId
            LEFT JOIN dbo.AccountingSourceDocuments financialSource ON financialSource.SourceDocumentId=page.SourceDocumentId
              AND financialSource.SourceDocumentType=page.SourceDocumentType AND financialSource.BusinessId=@BusinessId
              AND financialSource.TenantId=@TenantId
            CROSS APPLY accounting.PortfolioDocumentIssueDate(financialSource.PayloadJson,financialSource.OccurredAt) issue
            LEFT JOIN #PagePaid paid ON paid.PayableId=page.PayableId
            LEFT JOIN #PageDiscounts discount ON page.SourceDocumentType=N'GoodsReceipt'
              AND discount.SourceDocumentId=page.SourceDocumentId
            OUTER APPLY(SELECT TOP(1) application.PayableId FROM dbo.SupplierPaymentApplications application
              WHERE application.PayableId=page.PayableId AND application.FinancialDiscountEarned IS NOT NULL) decided
            OUTER APPLY(SELECT TOP(1) purchaseReturn.PurchaseReturnId FROM dbo.PurchaseReturns purchaseReturn
              WHERE purchaseReturn.BusinessId=@BusinessId AND purchaseReturn.OriginalGoodsReceiptId=page.SourceDocumentId
                AND purchaseReturn.Status=N'Accepted') pending
            ORDER BY {pageOrder};
            """;
        var items = new List<PayableListItem>();
        await using (var command = new SqlCommand(dataSql, connection))
        {
            AddQueryParameters(command, user, query, referenceStart, zone);
            command.Parameters.AddWithValue("@Offset", (query.Page - 1) * query.PageSize);
            command.Parameters.AddWithValue("@PageSize", query.PageSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var item=new PayableListItem(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetDecimal(5),
                    reader.GetDecimal(6), reader.GetDateTimeOffset(7), reader.GetString(8),
                    reader.GetBoolean(10), reader.GetDateTimeOffset(9),
                    reader.IsDBNull(11) ? null : reader.GetString(11),reader.GetDecimal(12),
                    reader.IsDBNull(13)?null:reader.GetGuid(13),reader.IsDBNull(14)?null:reader.GetString(14),
                    reader.IsDBNull(15)?null:reader.GetDecimal(15),reader.IsDBNull(16)?null:reader.GetBoolean(16),
                    reader.GetBoolean(17));
                var age=PortfolioAging.Calculate(item.OutstandingAmount,item.DueDate,referenceDate,zone);
                DateTimeOffset? issuedAt=reader.IsDBNull(18)?null:reader.GetDateTimeOffset(18);
                items.Add(item with {OverdueDays=age.OverdueDays,AgingBandCode=age.BandCode,
                    IssuedAt=issuedAt,SourceDocumentType=reader.GetString(19),
                    InternalDocumentNumber=reader.IsDBNull(20)?null:reader.GetString(20),
                    FiscalNumber=reader.IsDBNull(21)?null:reader.GetString(21),DueOn=age.DueOn,
                    IssuedOn=issuedAt is {} issued ? BusinessCalendar.DateAt(issued,zone) : null});
            }
        }
        var cop = currencyTotals.FirstOrDefault(item => item.CurrencyCode == "COP");
        return new PayablePage(items, query.Page, query.PageSize, totalCount,
            cop?.OutstandingAmount ?? 0, cop?.OverdueAmount ?? 0)
            { CurrencyTotals = currencyTotals,ReferenceDate=referenceDate };
    }

    public Task<SupplierPaymentHistoryPage> PaymentHistoryAsync(PayablesUserIdentity user,
        Guid supplierId, int page, int pageSize, CancellationToken cancellationToken) =>
        ListPaymentsAsync(user,new(page,pageSize,null,supplierId),cancellationToken);

    public async Task<SupplierPaymentHistoryPage> ListPaymentsAsync(PayablesUserIdentity user,
        SupplierPaymentHistoryQuery query,CancellationToken cancellationToken)
    {
        var paymentOrder = SqlPagedSort.Build(query.SortBy, query.SortDirection,
            new Dictionary<string, string>
            {
                ["paidAt"] = "payment.PaidAt", ["partyName"] = "supplier.Name",
                ["documentNumber"] = "origin.DocumentNumber", ["totalAmount"] = "payment.TotalAmount"
            }, "paidAt", "desc", "payment.PaymentId DESC");
        var pageOrder = paymentOrder.Replace("supplier.Name", "filtered.PartyName", StringComparison.Ordinal)
            .Replace("payment.", "filtered.", StringComparison.Ordinal)
            .Replace("origin.", "filtered.", StringComparison.Ordinal);
        await using var connection=connections.Create(); await connection.OpenAsync(cancellationToken);
        var referenceNow=timeProvider.GetUtcNow();
        var zone=TimeZoneInfo.Utc;
        if(query.Overdue.HasValue || query.DateRangeVersion == 2 && (query.From.HasValue || query.To.HasValue))
        {
            zone=await SqlBusinessLocalDates.ReadTimeZoneAsync(connection,user.TenantId,user.BusinessId,cancellationToken)
                ?? throw new PayablesForbiddenException("La sede no está disponible para consultar cartera.");
            if(query.Overdue.HasValue)
                referenceNow=SqlBusinessLocalDates.StartOfDay(BusinessCalendar.DateAt(referenceNow,zone),zone);
        }
        await using var command=new SqlCommand($"""
            CREATE TABLE #FilteredPayments(PaymentId uniqueidentifier PRIMARY KEY,PaidAt datetimeoffset(7),
              TotalAmount decimal(19,4),PartyName nvarchar(300) COLLATE DATABASE_DEFAULT,DocumentNumber nvarchar(100) COLLATE DATABASE_DEFAULT);
            INSERT #FilteredPayments SELECT payment.PaymentId,payment.PaidAt,payment.TotalAmount,supplier.Name,origin.DocumentNumber
            FROM dbo.SupplierPayments payment
            INNER JOIN accounting.PortfolioPaymentOrigins origin ON origin.PaymentId=payment.PaymentId
              AND origin.SubledgerKind=N'Payable' AND origin.BusinessId=payment.BusinessId
            INNER JOIN dbo.Businesses business ON business.BusinessId=payment.BusinessId
            INNER JOIN dbo.Suppliers supplier ON supplier.SupplierId=payment.SupplierId
            WHERE payment.BusinessId=@BusinessId AND business.TenantId=@TenantId
              AND (@SupplierId IS NULL OR payment.SupplierId=@SupplierId)
              AND (@PartySiteId IS NULL OR payment.PartySiteId=@PartySiteId OR EXISTS(SELECT 1 FROM dbo.SupplierPaymentApplications siteApplication
                JOIN dbo.Payables sitePayable ON sitePayable.PayableId=siteApplication.PayableId
                WHERE siteApplication.PaymentId=payment.PaymentId AND sitePayable.PartySiteId=@PartySiteId))
              AND (@From IS NULL OR payment.PaidAt>=@From)
              AND (@To IS NULL OR payment.PaidAt<@To)
              AND (@Status IS NULL AND @Overdue IS NULL OR EXISTS(
                SELECT 1 FROM dbo.SupplierPaymentApplications application
                JOIN dbo.Payables invoice ON invoice.PayableId=application.PayableId
                WHERE application.PaymentId=payment.PaymentId
                  AND (@Status IS NULL OR invoice.Status=@Status)
                  AND (@Overdue IS NULL OR (@Overdue=1 AND invoice.OutstandingAmount>0 AND invoice.DueDate<@Now)
                    OR (@Overdue=0 AND (invoice.OutstandingAmount=0 OR invoice.DueDate>=@Now)))))
              AND (@Search IS NULL OR origin.DocumentNumber LIKE N'%' + @Search + N'%'
                OR supplier.Name LIKE N'%' + @Search + N'%' OR supplier.Identification LIKE N'%' + @Search + N'%'
                OR EXISTS(SELECT 1 FROM dbo.SupplierPaymentApplications application
                  JOIN dbo.Payables invoice ON invoice.PayableId=application.PayableId
                  WHERE application.PaymentId=payment.PaymentId
                    AND invoice.DocumentNumber LIKE N'%' + @Search + N'%'));
            SELECT COUNT(*) FROM #FilteredPayments;
            DECLARE @Page TABLE(PaymentId uniqueidentifier PRIMARY KEY);
            INSERT @Page(PaymentId)
            -- Reuse the authorized filter/sort facts. Rejoining every source
            -- and party here duplicated the work before selecting 25 headers.
            SELECT filtered.PaymentId FROM #FilteredPayments filtered
            ORDER BY {pageOrder}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            SELECT payment.PaymentId,origin.DocumentNumber,payment.PaidAt,payment.CurrencyCode,
              payment.TotalAmount,payment.Status,
              COUNT(application.PayableId) AppliedDocumentCount,payment.SupplierId,supplier.Name,
              origin.SourceDocumentId,origin.SourceDocumentType,origin.SourceGroupId
            FROM @Page page INNER JOIN dbo.SupplierPayments payment ON payment.PaymentId=page.PaymentId
            INNER JOIN accounting.PortfolioPaymentOrigins origin ON origin.PaymentId=payment.PaymentId
              AND origin.SubledgerKind=N'Payable' AND origin.BusinessId=payment.BusinessId
            INNER JOIN dbo.Businesses business ON business.BusinessId=payment.BusinessId
            INNER JOIN dbo.Suppliers supplier ON supplier.SupplierId=payment.SupplierId
            LEFT JOIN dbo.SupplierPaymentApplications application ON application.PaymentId=payment.PaymentId
              AND (@PartySiteId IS NULL OR EXISTS(SELECT 1 FROM dbo.Payables sitePayable
                WHERE sitePayable.PayableId=application.PayableId AND sitePayable.PartySiteId=@PartySiteId))
            WHERE payment.BusinessId=@BusinessId AND business.TenantId=@TenantId
            GROUP BY payment.PaymentId,origin.DocumentNumber,payment.PaidAt,payment.CurrencyCode,
              payment.TotalAmount,payment.Status,payment.SupplierId,supplier.Name,
              origin.SourceDocumentId,origin.SourceDocumentType,origin.SourceGroupId
            ORDER BY {paymentOrder};
            SELECT tender.PaymentId,tender.LineNumber,tender.MethodCode,tender.Amount,tender.TenderedAmount,
              tender.BankAccountId,tender.Reference,tender.Notes,tender.CardFranchiseCode,tender.ApprovalNumber,
              tender.PaymentMethodId,tender.PaymentMethodVersion,tender.PaymentMethodCode,tender.PaymentMethodName,tender.AccountId
            FROM dbo.SupplierPaymentTenders tender INNER JOIN @Page page ON page.PaymentId=tender.PaymentId
            ORDER BY tender.PaymentId,tender.LineNumber;
            SELECT application.PaymentId,application.PayableId,payable.DocumentNumber,application.Amount,
              COALESCE(fiscal.AuralyDocumentNumber,expense.DocumentNumber,receipt.DocumentNumber),fiscal.FiscalNumber
            FROM dbo.SupplierPaymentApplications application
            INNER JOIN @Page page ON page.PaymentId=application.PaymentId
            INNER JOIN dbo.Payables payable ON payable.PayableId=application.PayableId AND payable.BusinessId=@BusinessId
            LEFT JOIN dbo.FiscalDocuments fiscal ON fiscal.DocumentId=payable.SourceDocumentId AND fiscal.BusinessId=@BusinessId
            LEFT JOIN dbo.GoodsReceipts receipt ON payable.SourceDocumentType=N'GoodsReceipt' AND receipt.GoodsReceiptId=payable.SourceDocumentId AND receipt.BusinessId=@BusinessId
            LEFT JOIN dbo.Expenses expense ON payable.SourceDocumentType=N'Expense' AND expense.ExpenseId=payable.SourceDocumentId AND expense.BusinessId=@BusinessId
            WHERE @PartySiteId IS NULL OR payable.PartySiteId=@PartySiteId
            ORDER BY application.PaymentId,application.LineNumber;
            SELECT applied.PaymentId,SUM(applied.Amount) NetAppliedAmount
            FROM accounting.PortfolioAppliedAmounts(@BusinessId,N'Payable') applied
            JOIN @Page page ON page.PaymentId=applied.PaymentId
            JOIN dbo.Payables position ON position.PayableId=applied.PositionId AND position.BusinessId=@BusinessId
              AND (@PartySiteId IS NULL OR position.PartySiteId=@PartySiteId)
            GROUP BY applied.PaymentId;
            """,connection);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        command.Parameters.AddWithValue("@TenantId",user.TenantId);
        command.Parameters.AddWithValue("@SupplierId",(object?)query.SupplierId??DBNull.Value);
        command.Parameters.AddWithValue("@PartySiteId",(object?)query.PartySiteId??DBNull.Value);
        command.Parameters.AddWithValue("@Search",(object?)query.Search??DBNull.Value);
        command.Parameters.AddWithValue("@Status",(object?)query.Status??DBNull.Value);
        command.Parameters.AddWithValue("@Overdue",(object?)query.Overdue??DBNull.Value);
        command.Parameters.AddWithValue("@Now",referenceNow);
        SqlBusinessLocalDates.BindDateRange(command,query.From,query.To,zone,query.DateRangeVersion);
        command.Parameters.AddWithValue("@Offset",(query.Page-1)*query.PageSize);
        command.Parameters.AddWithValue("@PageSize",query.PageSize);
        command.CommandText=SqlQueryShape.FoldOptionalPredicates(command);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken); await reader.ReadAsync(cancellationToken);
        var total=reader.GetInt32(0); await reader.NextResultAsync(cancellationToken);
        var items=new List<SupplierPaymentHistoryItem>();
        while(await reader.ReadAsync(cancellationToken)) items.Add(new(reader.GetGuid(0),reader.GetString(1),
            reader.GetDateTimeOffset(2),reader.GetString(3),reader.GetDecimal(4),reader.GetString(5),reader.GetInt32(6),
            [],[],reader.GetGuid(7),reader.GetString(8),reader.IsDBNull(11)?null:reader.GetGuid(9),
            reader.IsDBNull(11)?null:reader.GetString(10),reader.IsDBNull(11)?null:reader.GetGuid(11)));
        var byPayment=items.ToDictionary(item=>item.PaymentId,_=>new List<SupplierPaymentTenderSnapshot>());
        var applications=items.ToDictionary(item=>item.PaymentId,_=>new List<SupplierPaymentHistoryApplication>());
        await reader.NextResultAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken)) byPayment[reader.GetGuid(0)].Add(new(
            reader.GetInt32(1),reader.GetString(2),reader.GetDecimal(3),
            reader.IsDBNull(4)?null:reader.GetDecimal(4),reader.IsDBNull(5)?null:reader.GetGuid(5),
            reader.IsDBNull(6)?null:reader.GetString(6),reader.IsDBNull(7)?null:reader.GetString(7),
            reader.IsDBNull(8)?null:reader.GetString(8),reader.IsDBNull(9)?null:reader.GetString(9),
            reader.IsDBNull(10)?null:reader.GetGuid(10),reader.IsDBNull(11)?null:reader.GetInt32(11),
            reader.IsDBNull(12)?null:reader.GetString(12),reader.IsDBNull(13)?null:reader.GetString(13),
            reader.IsDBNull(14)?null:reader.GetGuid(14)));
        await reader.NextResultAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken)) applications[reader.GetGuid(0)].Add(new(
            reader.GetGuid(1),reader.GetString(2),reader.GetDecimal(3)) {
                InternalDocumentNumber=reader.IsDBNull(4)?null:reader.GetString(4),
                FiscalNumber=reader.IsDBNull(5)?null:reader.GetString(5) });
        var netApplied=new Dictionary<Guid,decimal>();
        await reader.NextResultAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken)) netApplied.Add(reader.GetGuid(0),reader.GetDecimal(1));
        return new(items.Select(item=>item with { Payments=byPayment[item.PaymentId],Applications=applications[item.PaymentId],
                NetAppliedAmount=netApplied.GetValueOrDefault(item.PaymentId) }).ToArray(),
            query.Page,query.PageSize,total);
    }

    public async Task<PayableDetail?> GetAsync(
        PayablesUserIdentity user,
        Guid payableId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string headerSql = """
            SELECT p.PayableId,p.SupplierId,s.Name,s.Identification,p.SourceDocumentId,
                   p.SourceDocumentType,p.DocumentNumber,p.CurrencyCode,p.OriginalAmount,
                   p.OutstandingAmount,p.DueDate,p.Status,concept.Name,expense.Description,
                   invoice.DocumentNumber,
                   CASE WHEN p.SourceDocumentType=N'GoodsReceipt' THEN p.SourceDocumentId
                        ELSE p.ParentGoodsReceiptId END,p.PartySiteId,site.Name,
                   COALESCE(fiscal.AuralyDocumentNumber,expense.DocumentNumber,receipt.DocumentNumber),fiscal.FiscalNumber,invoice.FiscalNumber
            FROM dbo.Payables p
            INNER JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId
            LEFT JOIN dbo.PartySites site ON site.PartySiteId=p.PartySiteId AND site.PartyId=s.PartyId
            INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
            LEFT JOIN dbo.FiscalDocuments fiscal ON fiscal.DocumentId=p.SourceDocumentId AND fiscal.BusinessId=p.BusinessId
            LEFT JOIN dbo.GoodsReceipts receipt ON p.SourceDocumentType=N'GoodsReceipt'
              AND receipt.GoodsReceiptId=p.SourceDocumentId AND receipt.BusinessId=p.BusinessId
            LEFT JOIN dbo.Expenses expense ON p.SourceDocumentType=N'Expense'
              AND expense.ExpenseId=p.SourceDocumentId AND expense.BusinessId=p.BusinessId
            LEFT JOIN dbo.ExpenseConcepts concept ON concept.ExpenseConceptId=expense.ExpenseConceptId
              AND concept.BusinessId=p.BusinessId
            LEFT JOIN dbo.SalesDocuments invoice ON invoice.DocumentId=expense.SourceInvoiceId
              AND invoice.BusinessId=p.BusinessId
            WHERE p.PayableId=@PayableId AND p.BusinessId=@BusinessId AND b.TenantId=@TenantId;
            """;
        Guid supplierId;
        string supplierName;
        string supplierIdentification;
        Guid sourceDocumentId;
        string sourceDocumentType;
        string documentNumber;
        string currency;
        decimal original;
        decimal outstanding;
        DateTimeOffset dueDate;
        string status;
        string? conceptName;
        string? description;
        string? sourceInvoiceNumber;
        Guid? goodsReceiptId;
        Guid? partySiteId;
        string? partySiteName;
        string? internalDocumentNumber,fiscalNumber,sourceInvoiceFiscalNumber;
        await using (var command = new SqlCommand(headerSql, connection))
        {
            command.Parameters.AddWithValue("@PayableId", payableId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            supplierId = reader.GetGuid(1);
            supplierName = reader.GetString(2);
            supplierIdentification = reader.GetString(3);
            sourceDocumentId = reader.GetGuid(4);
            sourceDocumentType = reader.GetString(5);
            documentNumber = reader.GetString(6);
            currency = reader.GetString(7);
            original = reader.GetDecimal(8);
            outstanding = reader.GetDecimal(9);
            dueDate = reader.GetDateTimeOffset(10);
            status = reader.GetString(11);
            conceptName = reader.IsDBNull(12) ? null : reader.GetString(12);
            description = reader.IsDBNull(13) ? null : reader.GetString(13);
            sourceInvoiceNumber = reader.IsDBNull(14) ? null : reader.GetString(14);
            goodsReceiptId = reader.IsDBNull(15) ? null : reader.GetGuid(15);
            partySiteId = reader.IsDBNull(16) ? null : reader.GetGuid(16);
            partySiteName = reader.IsDBNull(17) ? null : reader.GetString(17);
            internalDocumentNumber=reader.IsDBNull(18)?null:reader.GetString(18);
            fiscalNumber=reader.IsDBNull(19)?null:reader.GetString(19);
            sourceInvoiceFiscalNumber=reader.IsDBNull(20)?null:reader.GetString(20);
        }
        var transactions = new List<PayableTransactionView>();
        await using (var command = new SqlCommand("""
            SELECT tx.PayableTransactionId,tx.TransactionType,tx.Amount,COALESCE(origin.SourceDocumentId,tx.SourceDocumentId),tx.OccurredAt,effect.BalanceChange
            FROM dbo.PayableTransactions tx
            CROSS APPLY accounting.PortfolioTransactionEffect(tx.TransactionType,tx.Amount) effect
            LEFT JOIN accounting.PortfolioPaymentOrigins origin ON tx.TransactionType=N'Payment'
              AND origin.PaymentId=tx.SourceDocumentId AND origin.SubledgerKind=N'Payable'
              AND origin.BusinessId=@BusinessId AND origin.TenantId=@TenantId
            WHERE tx.PayableId=@PayableId ORDER BY tx.OccurredAt,tx.PayableTransactionId;
            """, connection))
        {
            command.Parameters.AddWithValue("@PayableId", payableId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                transactions.Add(new PayableTransactionView(
                    reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2),
                    reader.GetGuid(3), reader.GetDateTimeOffset(4), reader.GetDecimal(5)));
        }
        return new PayableDetail(
            payableId, supplierId, supplierName, supplierIdentification,
            sourceDocumentId, sourceDocumentType, documentNumber, currency,
            original, outstanding, dueDate, status, transactions,
            conceptName, description, sourceInvoiceNumber, goodsReceiptId,
            partySiteId, partySiteName){InternalDocumentNumber=internalDocumentNumber,FiscalNumber=fiscalNumber,SourceInvoiceFiscalNumber=sourceInvoiceFiscalNumber};
    }

    public async Task<SupplierPaymentAcceptance> AcceptPaymentAsync(
        PayablesUserIdentity user,
        string idempotencyKey,
        ConfirmSupplierPaymentRequest request,
        PayableSettlement settlement,
        PaymentTenderBreakdown tenders,
        CancellationToken cancellationToken)
    {
        var requestHash = HashRequest(request, settlement, tenders);
        if(request.PaidOn is {} selectedDay)
            requestHash=SHA256.HashData(requestHash.Concat(Encoding.UTF8.GetBytes(selectedDay.ToString("O",System.Globalization.CultureInfo.InvariantCulture))).ToArray());
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await AcceptPaymentAttemptAsync(
                    user, idempotencyKey, request, settlement, tenders, requestHash,
                    cancellationToken);
            }
            catch (SqlException exception) when (exception.Number == 1205 && attempt < 3)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(25 * attempt), timeProvider, cancellationToken);
            }
        }
    }

    private async Task<SupplierPaymentAcceptance> AcceptPaymentAttemptAsync(
        PayablesUserIdentity user,
        string idempotencyKey,
        ConfirmSupplierPaymentRequest request,
        PayableSettlement settlement,
        PaymentTenderBreakdown tenders,
        byte[] requestHash,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var replay = await FindReplayAsync(
                connection, transaction, user.TenantId, user.BusinessId, request.PaymentId,
                idempotencyKey, requestHash, settlement, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }
            if(request.PaidOn is {} paidOn)
            {
                try { request=request with {PaidAt=await SqlBusinessLocalDates.EffectivePaymentDateAsync(connection,transaction,user.TenantId,user.BusinessId,paidOn,cancellationToken)}; }
                catch(ArgumentException error) when(error.ParamName=="day")
                { throw new PayablesValidationException(error.Message); }
            }
            var now = timeProvider.GetUtcNow();
            AuralyDocumentNumberAssignment number;
            try
            {
                number = await SqlOperationalDocumentAllocator.AllocateExistingNumberAsync(
                    connection, transaction, user.TenantId, user.BusinessId,
                    AuralyDocumentTypes.PayablePayment, now, cancellationToken);
            }
            catch (SqlException error) when (error.Number is 51730 or 51731)
            {
                throw new PayablesValidationException(error.Message, error);
            }
            var movementId = ids.NewId();
            IReadOnlyList<PaymentTender> resolvedTenders;
            try { resolvedTenders=await SqlPaymentMethodResolver.ResolveAsync(connection,transaction,user.TenantId,user.BusinessId,"SupplierPayments",tenders.Tenders,cancellationToken); }
            catch(ArgumentException error) { throw new PayablesValidationException(error.Message); }
            var paymentSnapshots=resolvedTenders.Select((item,index)=>new SupplierPaymentTenderSnapshot(
                index+1,item.MethodCode,item.Amount,item.TenderedAmount,item.BankAccountId,
                item.Reference,item.Notes,item.CardFranchiseCode,item.ApprovalNumber,item.PaymentMethodId,item.PaymentMethodVersion,
                item.PaymentMethodCode,item.PaymentMethodName,item.AccountId)).ToArray();
            var discountDecisions = request.Allocations.ToDictionary(item => item.PayableId,
                item => item.FinancialDiscountEarned);
            var payload = new SupplierPaymentDocumentPayload(
                user.TenantId, user.BusinessId, request.PaymentId, request.SupplierId,
                user.UserId, number.FullNumber, number.SeriesId, number.Prefix,
                number.SeriesCode, number.Consecutive, request.PaidAt, request.CurrencyCode, request.Notes,
                settlement.TotalAmount,
                settlement.Allocations.Select((item, index) =>
                    new SupplierPaymentAllocationSnapshot(index + 1, item.PayableId, item.Amount,
                        discountDecisions[item.PayableId]))
                    .ToArray(), paymentSnapshots, request.WorkSessionId);
            var payloadJson = SupplierPaymentContractSerializer.Serialize(payload);
            await AcceptPaymentRecordAsync(connection, transaction, user, payloadJson,
                idempotencyKey, requestHash, now, cancellationToken);
            await SqlAccountingPostingJobWriter.InsertSourceAsync(connection,transaction,user.TenantId,
                user.BusinessId,request.PaymentId,PayablesDocumentTypes.Payment,payloadJson,request.PaidAt,
                ids,timeProvider,cancellationToken,AccountingJobRequirement.PreserveCommercialEffects,movementId);
            await transaction.CommitAsync(cancellationToken);
            return new SupplierPaymentAcceptance(
                request.PaymentId, movementId, number.FullNumber, "Accepted", false,request.PaidAt.ToUniversalTime());
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void AddQueryParameters(
        SqlCommand command, PayablesUserIdentity user, PayableQuery query, DateTimeOffset now, TimeZoneInfo zone)
    {
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@SupplierId", (object?)query.SupplierId ?? DBNull.Value);
        command.Parameters.Add("@PayableId",SqlDbType.UniqueIdentifier).Value=(object?)query.PayableId??DBNull.Value;
        command.Parameters.AddWithValue("@PartySiteId", (object?)query.PartySiteId ?? DBNull.Value);
        command.Parameters.AddWithValue("@ConceptId", (object?)query.ConceptId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Status", (object?)query.Status ?? DBNull.Value);
        command.Parameters.AddWithValue("@OutstandingOnly", query.OutstandingOnly);
        command.Parameters.AddWithValue("@Overdue", (object?)query.Overdue ?? DBNull.Value);
        command.Parameters.AddWithValue("@Search", (object?)query.Search ?? DBNull.Value);
        command.Parameters.AddWithValue("@Now", now);
        SqlBusinessLocalDates.BindDateRange(command,query.From,query.To,zone,query.DateRangeVersion);
    }



    private static async Task<SupplierPaymentAcceptance?> FindReplayAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, Guid businessId,
        Guid paymentId, string idempotencyKey, byte[] requestHash,
        PayableSettlement settlement,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            -- Lock the bounded allocation set before replay can hold payment/job key ranges.
            -- Availability is evaluated later, with these position locks already held.
            DECLARE @Positions TABLE(PayableId uniqueidentifier PRIMARY KEY);
            INSERT @Positions SELECT PayableId FROM OPENJSON(@Allocations)
              WITH(PayableId uniqueidentifier);
            DECLARE @LockedPosition uniqueidentifier;
            SELECT @LockedPosition=p.PayableId FROM @Positions input
            JOIN dbo.Payables p WITH(UPDLOCK,HOLDLOCK) ON p.PayableId=input.PayableId
              AND p.BusinessId=@BusinessId
            JOIN dbo.Businesses business ON business.BusinessId=p.BusinessId AND business.TenantId=@TenantId
            ORDER BY input.PayableId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
            SELECT p.PaymentId,origin.DocumentNumber,p.Status,j.AccountingPostingJobId,p.PayloadHash,s.SourceDocumentId,origin.SourceGroupId,p.PaidAt
            FROM dbo.SupplierPayments p
            INNER JOIN dbo.Businesses business ON business.BusinessId=p.BusinessId AND business.TenantId=@TenantId
            INNER JOIN accounting.PortfolioPaymentOrigins origin ON origin.PaymentId=p.PaymentId
              AND origin.SubledgerKind=N'Payable' AND origin.BusinessId=p.BusinessId
            LEFT JOIN dbo.AccountingPostingJobs j
              ON j.SourceDocumentId=origin.SourceDocumentId AND j.SourceDocumentType=origin.SourceDocumentType
                AND j.TenantId=@TenantId AND j.BusinessId=@BusinessId
            LEFT JOIN dbo.AccountingSourceDocuments s
              ON s.SourceDocumentId=origin.SourceDocumentId AND s.SourceDocumentType=origin.SourceDocumentType
                AND s.TenantId=@TenantId AND s.BusinessId=@BusinessId
            WHERE p.BusinessId=@BusinessId
              AND (p.PaymentId=@PaymentId OR p.IdempotencyKey=@IdempotencyKey);
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@PaymentId", paymentId);
        command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
        command.Parameters.Add("@Allocations",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(settlement.Allocations);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (!reader.IsDBNull(6))
            throw new PayablesConflictException("El pago pertenece a un comprobante. Consulta o reintenta ese comprobante; no se aceptó un pago independiente.");
        if (!reader.GetFieldValue<byte[]>(4).AsSpan().SequenceEqual(requestHash))
            throw new PayablesConflictException(
                "The idempotency key or PaymentId was reused with another payload.");
        if (reader.IsDBNull(3) || reader.IsDBNull(5))
            throw new PayablesConflictException("El pago ya está registrado, pero su fuente o trabajo financiero requieren revisión. No se aceptó otro pago.");
        var acceptance = new SupplierPaymentAcceptance(
            reader.GetGuid(0), reader.GetGuid(3), reader.GetString(1), reader.GetString(2), true,reader.GetDateTimeOffset(7));
        if (await reader.ReadAsync(cancellationToken))
            throw new PayablesConflictException("El identificador del pago y la clave de reintento pertenecen a pagos diferentes.");
        return acceptance;
    }

    private static byte[] HashRequest(
        ConfirmSupplierPaymentRequest request, PayableSettlement settlement,
        PaymentTenderBreakdown tenders)
    {
        if (request.Allocations.All(item => item.FinancialDiscountEarned is null))
            return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                request.PaymentId, request.BusinessId, request.SupplierId, request.PaidAt,
                Currency = request.CurrencyCode, request.Notes, request.WorkSessionId,
                settlement.TotalAmount, settlement.Allocations, tenders.Tenders
            }));
        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            request.PaymentId, request.BusinessId, request.SupplierId, request.PaidAt,
            Currency = request.CurrencyCode, request.Notes, request.WorkSessionId,
            settlement.TotalAmount, settlement.Allocations,
            DiscountDecisions = request.Allocations.Select(item => new { item.PayableId, item.FinancialDiscountEarned }),
            tenders.Tenders
        }));
    }

    private static async Task AcceptPaymentRecordAsync(
        SqlConnection connection, SqlTransaction transaction, PayablesUserIdentity user,
        string payloadJson, string idempotencyKey, byte[] requestHash,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("accounting.PortfolioPaymentAccept", connection, transaction)
        { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@SubledgerKind", "Payable");
        command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = payloadJson;
        command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
        command.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = requestHash;
        command.Parameters.AddWithValue("@AcceptedAt", now);
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (SqlException error) when (error.Number is >= 51400 and <= 51402)
        {
            if (error.Number == 51402) throw new PayablesConflictException(error.Message, error);
            throw new PayablesValidationException(error.Message, error);
        }
    }

}
