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

public sealed class SqlPayablesStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IPayablesStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<SupplierPortfolioPage> ListSuppliersAsync(PayablesUserIdentity user,
        SupplierPortfolioQuery query,CancellationToken token)
    {
        await using var connection=connections.Create();await connection.OpenAsync(token);
        await using var command=new SqlCommand("""
            WITH Paid AS(
              SELECT application.PayableId,SUM(application.Amount) PaidAmount
              FROM dbo.SupplierPaymentApplications application
              JOIN dbo.Payables scoped ON scoped.PayableId=application.PayableId
              WHERE scoped.BusinessId=@BusinessId AND application.AppliedAt IS NOT NULL
              GROUP BY application.PayableId),
            Portfolio AS(
              SELECT p.SupplierId,s.Name SupplierName,COALESCE(s.Identification,N'') Identification,
                COUNT(*) InvoiceCount,SUM(p.OriginalAmount) OriginalAmount,
                SUM(COALESCE(paid.PaidAmount,0)) PaidAmount,SUM(p.OutstandingAmount) OutstandingAmount,
                SUM(CASE WHEN p.OutstandingAmount>0 AND p.DueDate<@Now THEN p.OutstandingAmount ELSE 0 END) OverdueAmount
              FROM dbo.Payables p JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
              JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId
              LEFT JOIN Paid paid ON paid.PayableId=p.PayableId
              WHERE p.BusinessId=@BusinessId AND b.TenantId=@TenantId
                AND (@SupplierId IS NULL OR p.SupplierId=@SupplierId)
                AND (@Status IS NULL OR p.Status=@Status)
                AND (@From IS NULL OR p.CreatedAt>=@From)
                AND (@To IS NULL OR p.CreatedAt<@To)
                AND (@Overdue IS NULL OR (@Overdue=1 AND p.OutstandingAmount>0 AND p.DueDate<@Now)
                  OR (@Overdue=0 AND (p.OutstandingAmount=0 OR p.DueDate>=@Now)))
                AND (@Search IS NULL OR s.Name LIKE N'%' + @Search + N'%' OR s.Identification LIKE N'%' + @Search + N'%'
                  OR p.DocumentNumber LIKE N'%' + @Search + N'%')
              GROUP BY p.SupplierId,s.Name,s.Identification)
            SELECT COUNT(*),COALESCE(SUM(OutstandingAmount),0),COALESCE(SUM(OverdueAmount),0),COALESCE(SUM(InvoiceCount),0)
            FROM Portfolio WHERE @Overdue IS NULL OR (@Overdue=1 AND OverdueAmount>0) OR (@Overdue=0 AND OverdueAmount=0);
            WITH Paid AS(
              SELECT application.PayableId,SUM(application.Amount) PaidAmount
              FROM dbo.SupplierPaymentApplications application
              JOIN dbo.Payables scoped ON scoped.PayableId=application.PayableId
              WHERE scoped.BusinessId=@BusinessId AND application.AppliedAt IS NOT NULL
              GROUP BY application.PayableId),
            Portfolio AS(
              SELECT p.SupplierId,s.Name SupplierName,COALESCE(s.Identification,N'') Identification,
                COUNT(*) InvoiceCount,SUM(p.OriginalAmount) OriginalAmount,
                SUM(COALESCE(paid.PaidAmount,0)) PaidAmount,SUM(p.OutstandingAmount) OutstandingAmount,
                SUM(CASE WHEN p.OutstandingAmount>0 AND p.DueDate<@Now THEN p.OutstandingAmount ELSE 0 END) OverdueAmount
              FROM dbo.Payables p JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
              JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId
              LEFT JOIN Paid paid ON paid.PayableId=p.PayableId
              WHERE p.BusinessId=@BusinessId AND b.TenantId=@TenantId
                AND (@SupplierId IS NULL OR p.SupplierId=@SupplierId)
                AND (@Status IS NULL OR p.Status=@Status)
                AND (@From IS NULL OR p.CreatedAt>=@From)
                AND (@To IS NULL OR p.CreatedAt<@To)
                AND (@Overdue IS NULL OR (@Overdue=1 AND p.OutstandingAmount>0 AND p.DueDate<@Now)
                  OR (@Overdue=0 AND (p.OutstandingAmount=0 OR p.DueDate>=@Now)))
                AND (@Search IS NULL OR s.Name LIKE N'%' + @Search + N'%' OR s.Identification LIKE N'%' + @Search + N'%'
                  OR p.DocumentNumber LIKE N'%' + @Search + N'%')
              GROUP BY p.SupplierId,s.Name,s.Identification)
            SELECT SupplierId,SupplierName,Identification,InvoiceCount,OriginalAmount,PaidAmount,OutstandingAmount,OverdueAmount
            FROM Portfolio WHERE @Overdue IS NULL OR (@Overdue=1 AND OverdueAmount>0) OR (@Overdue=0 AND OverdueAmount=0)
            ORDER BY CASE WHEN OverdueAmount>0 THEN 0 ELSE 1 END,OutstandingAmount DESC,SupplierName
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """,connection);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@TenantId",user.TenantId);command.Parameters.AddWithValue("@Search",(object?)query.Search??DBNull.Value);command.Parameters.AddWithValue("@Overdue",(object?)query.Overdue??DBNull.Value);command.Parameters.AddWithValue("@SupplierId",(object?)query.SupplierId??DBNull.Value);command.Parameters.AddWithValue("@Status",(object?)query.Status??DBNull.Value);AddDateRange(command,query.From,query.To);command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());command.Parameters.AddWithValue("@Offset",(query.Page-1)*query.PageSize);command.Parameters.AddWithValue("@PageSize",query.PageSize);
        await using var reader=await command.ExecuteReaderAsync(token);await reader.ReadAsync(token);var count=reader.GetInt32(0);var outstanding=reader.GetDecimal(1);var overdue=reader.GetDecimal(2);var invoices=reader.GetInt32(3);await reader.NextResultAsync(token);
        var items=new List<SupplierPortfolioItem>();while(await reader.ReadAsync(token))items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetInt32(3),reader.GetDecimal(4),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetDecimal(7)));
        return new(items,query.Page,query.PageSize,count,outstanding,overdue,invoices);
    }

    public async Task<PayablePage> ListAsync(
        PayablesUserIdentity user,
        PayableQuery query,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string filters = """
            p.BusinessId=@BusinessId
            AND b.TenantId=@TenantId
            AND (@SupplierId IS NULL OR p.SupplierId=@SupplierId)
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
            SELECT COUNT(*),COALESCE(SUM(p.OutstandingAmount),0),
                   COALESCE(SUM(CASE WHEN p.OutstandingAmount>0 AND p.DueDate<@Now
                                     THEN p.OutstandingAmount ELSE 0 END),0)
            FROM dbo.Payables p
            INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
            INNER JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId
            WHERE {filters};
            """;
        int totalCount;
        decimal totalOutstanding;
        decimal totalOverdue;
        await using (var command = new SqlCommand(countSql, connection))
        {
            AddQueryParameters(command, user, query, timeProvider.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            totalCount = reader.GetInt32(0);
            totalOutstanding = reader.GetDecimal(1);
            totalOverdue = reader.GetDecimal(2);
        }

        var dataSql = $"""
            SELECT p.PayableId,p.SupplierId,s.Name,p.DocumentNumber,p.CurrencyCode,
                   p.OriginalAmount,p.OutstandingAmount,p.DueDate,p.Status,p.CreatedAt,
                   CASE WHEN p.OutstandingAmount>0 AND p.DueDate<@Now THEN CAST(1 AS BIT)
                        ELSE CAST(0 AS BIT) END
            FROM dbo.Payables p
            INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
            INNER JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId
            WHERE {filters}
            ORDER BY CASE WHEN p.OutstandingAmount>0 AND p.DueDate<@Now THEN 0 ELSE 1 END,
                     p.DueDate,p.PayableId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        var items = new List<PayableListItem>();
        await using (var command = new SqlCommand(dataSql, connection))
        {
            AddQueryParameters(command, user, query, timeProvider.GetUtcNow());
            command.Parameters.AddWithValue("@Offset", (query.Page - 1) * query.PageSize);
            command.Parameters.AddWithValue("@PageSize", query.PageSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new PayableListItem(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetDecimal(5),
                    reader.GetDecimal(6), reader.GetDateTimeOffset(7), reader.GetString(8),
                    reader.GetBoolean(10), reader.GetDateTimeOffset(9)));
        }
        return new PayablePage(
            items, query.Page, query.PageSize, totalCount, totalOutstanding, totalOverdue);
    }

    public Task<SupplierPaymentHistoryPage> PaymentHistoryAsync(PayablesUserIdentity user,
        Guid supplierId, int page, int pageSize, CancellationToken cancellationToken) =>
        ListPaymentsAsync(user,new(page,pageSize,null,supplierId),cancellationToken);

    public async Task<SupplierPaymentHistoryPage> ListPaymentsAsync(PayablesUserIdentity user,
        SupplierPaymentHistoryQuery query,CancellationToken cancellationToken)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(cancellationToken);
        await using var command=new SqlCommand("""
            SELECT COUNT(*) FROM dbo.SupplierPayments payment
            INNER JOIN dbo.Businesses business ON business.BusinessId=payment.BusinessId
            INNER JOIN dbo.Suppliers supplier ON supplier.SupplierId=payment.SupplierId
            WHERE payment.BusinessId=@BusinessId AND business.TenantId=@TenantId
              AND (@SupplierId IS NULL OR payment.SupplierId=@SupplierId)
              AND (@From IS NULL OR payment.PaidAt>=@From)
              AND (@To IS NULL OR payment.PaidAt<@To)
              AND (@Status IS NULL AND @Overdue IS NULL OR EXISTS(
                SELECT 1 FROM dbo.SupplierPaymentApplications application
                JOIN dbo.Payables invoice ON invoice.PayableId=application.PayableId
                WHERE application.PaymentId=payment.PaymentId
                  AND (@Status IS NULL OR invoice.Status=@Status)
                  AND (@Overdue IS NULL OR (@Overdue=1 AND invoice.OutstandingAmount>0 AND invoice.DueDate<@Now)
                    OR (@Overdue=0 AND (invoice.OutstandingAmount=0 OR invoice.DueDate>=@Now)))))
              AND (@Search IS NULL OR payment.DocumentNumber LIKE N'%' + @Search + N'%'
                OR supplier.Name LIKE N'%' + @Search + N'%' OR supplier.Identification LIKE N'%' + @Search + N'%'
                OR EXISTS(SELECT 1 FROM dbo.SupplierPaymentApplications application
                  JOIN dbo.Payables invoice ON invoice.PayableId=application.PayableId
                  WHERE application.PaymentId=payment.PaymentId
                    AND invoice.DocumentNumber LIKE N'%' + @Search + N'%'));
            DECLARE @Page TABLE(PaymentId uniqueidentifier PRIMARY KEY);
            INSERT @Page(PaymentId)
            SELECT payment.PaymentId FROM dbo.SupplierPayments payment
            INNER JOIN dbo.Businesses business ON business.BusinessId=payment.BusinessId
            INNER JOIN dbo.Suppliers supplier ON supplier.SupplierId=payment.SupplierId
            WHERE payment.BusinessId=@BusinessId AND business.TenantId=@TenantId
              AND (@SupplierId IS NULL OR payment.SupplierId=@SupplierId)
              AND (@From IS NULL OR payment.PaidAt>=@From)
              AND (@To IS NULL OR payment.PaidAt<@To)
              AND (@Status IS NULL AND @Overdue IS NULL OR EXISTS(
                SELECT 1 FROM dbo.SupplierPaymentApplications application
                JOIN dbo.Payables invoice ON invoice.PayableId=application.PayableId
                WHERE application.PaymentId=payment.PaymentId
                  AND (@Status IS NULL OR invoice.Status=@Status)
                  AND (@Overdue IS NULL OR (@Overdue=1 AND invoice.OutstandingAmount>0 AND invoice.DueDate<@Now)
                    OR (@Overdue=0 AND (invoice.OutstandingAmount=0 OR invoice.DueDate>=@Now)))))
              AND (@Search IS NULL OR payment.DocumentNumber LIKE N'%' + @Search + N'%'
                OR supplier.Name LIKE N'%' + @Search + N'%' OR supplier.Identification LIKE N'%' + @Search + N'%'
                OR EXISTS(SELECT 1 FROM dbo.SupplierPaymentApplications application
                  JOIN dbo.Payables invoice ON invoice.PayableId=application.PayableId
                  WHERE application.PaymentId=payment.PaymentId
                    AND invoice.DocumentNumber LIKE N'%' + @Search + N'%'))
            ORDER BY payment.PaidAt DESC,payment.PaymentId DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            SELECT payment.PaymentId,payment.DocumentNumber,payment.PaidAt,payment.CurrencyCode,
              payment.TotalAmount,payment.Status,
              COUNT(application.PayableId) AppliedDocumentCount,payment.SupplierId,supplier.Name
            FROM @Page page INNER JOIN dbo.SupplierPayments payment ON payment.PaymentId=page.PaymentId
            INNER JOIN dbo.Businesses business ON business.BusinessId=payment.BusinessId
            INNER JOIN dbo.Suppliers supplier ON supplier.SupplierId=payment.SupplierId
            LEFT JOIN dbo.SupplierPaymentApplications application ON application.PaymentId=payment.PaymentId
            WHERE payment.BusinessId=@BusinessId AND business.TenantId=@TenantId
            GROUP BY payment.PaymentId,payment.DocumentNumber,payment.PaidAt,payment.CurrencyCode,
              payment.TotalAmount,payment.Status,payment.SupplierId,supplier.Name
            ORDER BY payment.PaidAt DESC,payment.PaymentId DESC;
            SELECT tender.PaymentId,tender.LineNumber,tender.MethodCode,tender.Amount,tender.TenderedAmount,
              tender.BankAccountId,tender.Reference,tender.Notes,tender.CardFranchiseCode,tender.ApprovalNumber
            FROM dbo.SupplierPaymentTenders tender INNER JOIN @Page page ON page.PaymentId=tender.PaymentId
            ORDER BY tender.PaymentId,tender.LineNumber;
            SELECT application.PaymentId,application.PayableId,payable.DocumentNumber,application.Amount
            FROM dbo.SupplierPaymentApplications application
            INNER JOIN @Page page ON page.PaymentId=application.PaymentId
            INNER JOIN dbo.Payables payable ON payable.PayableId=application.PayableId
            ORDER BY application.PaymentId,application.LineNumber;
            """,connection);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        command.Parameters.AddWithValue("@TenantId",user.TenantId);
        command.Parameters.AddWithValue("@SupplierId",(object?)query.SupplierId??DBNull.Value);
        command.Parameters.AddWithValue("@Search",(object?)query.Search??DBNull.Value);
        command.Parameters.AddWithValue("@Status",(object?)query.Status??DBNull.Value);
        command.Parameters.AddWithValue("@Overdue",(object?)query.Overdue??DBNull.Value);
        command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());
        AddDateRange(command,query.From,query.To);
        command.Parameters.AddWithValue("@Offset",(query.Page-1)*query.PageSize);
        command.Parameters.AddWithValue("@PageSize",query.PageSize);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken); await reader.ReadAsync(cancellationToken);
        var total=reader.GetInt32(0); await reader.NextResultAsync(cancellationToken);
        var items=new List<SupplierPaymentHistoryItem>();
        while(await reader.ReadAsync(cancellationToken)) items.Add(new(reader.GetGuid(0),reader.GetString(1),
            reader.GetDateTimeOffset(2),reader.GetString(3),reader.GetDecimal(4),reader.GetString(5),reader.GetInt32(6),
            [],[],reader.GetGuid(7),reader.GetString(8)));
        var byPayment=items.ToDictionary(item=>item.PaymentId,_=>new List<SupplierPaymentTenderSnapshot>());
        var applications=items.ToDictionary(item=>item.PaymentId,_=>new List<SupplierPaymentHistoryApplication>());
        await reader.NextResultAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken)) byPayment[reader.GetGuid(0)].Add(new(
            reader.GetInt32(1),reader.GetString(2),reader.GetDecimal(3),
            reader.IsDBNull(4)?null:reader.GetDecimal(4),reader.IsDBNull(5)?null:reader.GetGuid(5),
            reader.IsDBNull(6)?null:reader.GetString(6),reader.IsDBNull(7)?null:reader.GetString(7)));
        await reader.NextResultAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken)) applications[reader.GetGuid(0)].Add(new(
            reader.GetGuid(1),reader.GetString(2),reader.GetDecimal(3)));
        return new(items.Select(item=>item with { Payments=byPayment[item.PaymentId],Applications=applications[item.PaymentId] }).ToArray(),
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
                   p.OutstandingAmount,p.DueDate,p.Status
            FROM dbo.Payables p
            INNER JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId
            INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
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
        }
        var transactions = new List<PayableTransactionView>();
        await using (var command = new SqlCommand("""
            SELECT PayableTransactionId,TransactionType,Amount,SourceDocumentId,OccurredAt
            FROM dbo.PayableTransactions
            WHERE PayableId=@PayableId ORDER BY OccurredAt,PayableTransactionId;
            """, connection))
        {
            command.Parameters.AddWithValue("@PayableId", payableId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                transactions.Add(new PayableTransactionView(
                    reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2),
                    reader.GetGuid(3), reader.GetDateTimeOffset(4)));
        }
        return new PayableDetail(
            payableId, supplierId, supplierName, supplierIdentification,
            sourceDocumentId, sourceDocumentType, documentNumber, currency,
            original, outstanding, dueDate, status, transactions);
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
                connection, transaction, user.BusinessId, request.PaymentId,
                idempotencyKey, requestHash, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }
            await ValidateScopeAndAvailabilityAsync(
                connection, transaction, user, request, settlement, tenders, cancellationToken);
            var number = await AllocateNumberAsync(
                connection, transaction, user.BusinessId, cancellationToken);
            var now = timeProvider.GetUtcNow();
            var movementId = ids.NewId();
            var paymentSnapshots=tenders.Tenders.Select((item,index)=>new SupplierPaymentTenderSnapshot(
                index+1,item.MethodCode,item.Amount,item.TenderedAmount,item.BankAccountId,
                item.Reference,item.Notes)).ToArray();
            var payload = new SupplierPaymentDocumentPayload(
                user.TenantId, user.BusinessId, request.PaymentId, request.SupplierId,
                user.UserId, number.FullNumber, number.SeriesId, number.Prefix,
                number.SeriesCode, number.Consecutive, request.PaidAt, request.CurrencyCode, request.Notes,
                settlement.TotalAmount,
                settlement.Allocations.Select((item, index) =>
                    new SupplierPaymentAllocationSnapshot(index + 1, item.PayableId, item.Amount))
                    .ToArray(), paymentSnapshots, request.WorkSessionId);
            var payloadJson = SupplierPaymentContractSerializer.Serialize(payload);
            await InsertPaymentAsync(
                connection, transaction, user, request, settlement, number,
                idempotencyKey, requestHash, JsonSerializer.Serialize(paymentSnapshots), now, cancellationToken);
            await InsertApplicationsAsync(
                connection, transaction, request.PaymentId, settlement, cancellationToken);
            await SqlAccountingPostingJobWriter.InsertSourceAsync(connection,transaction,user.TenantId,
                user.BusinessId,request.PaymentId,PayablesDocumentTypes.Payment,payloadJson,request.PaidAt,
                ids,timeProvider,cancellationToken,AccountingJobRequirement.PreserveCommercialEffects,movementId);
            await transaction.CommitAsync(cancellationToken);
            return new SupplierPaymentAcceptance(
                request.PaymentId, movementId, number.FullNumber, "Accepted", false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void AddQueryParameters(
        SqlCommand command, PayablesUserIdentity user, PayableQuery query, DateTimeOffset now)
    {
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@SupplierId", (object?)query.SupplierId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Status", (object?)query.Status ?? DBNull.Value);
        command.Parameters.AddWithValue("@OutstandingOnly", query.OutstandingOnly);
        command.Parameters.AddWithValue("@Overdue", (object?)query.Overdue ?? DBNull.Value);
        command.Parameters.AddWithValue("@Search", (object?)query.Search ?? DBNull.Value);
        command.Parameters.AddWithValue("@Now", now);
        AddDateRange(command,query.From,query.To);
    }

    private static void AddDateRange(SqlCommand command,DateOnly? from,DateOnly? to)
    {
        command.Parameters.AddWithValue("@From",(object?)from?.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc)??DBNull.Value);
        command.Parameters.AddWithValue("@To",(object?)to?.AddDays(1).ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc)??DBNull.Value);
    }

    private static async Task<SupplierPaymentAcceptance?> FindReplayAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        Guid paymentId, string idempotencyKey, byte[] requestHash,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT p.PaymentId,p.DocumentNumber,p.Status,j.AccountingPostingJobId,p.PayloadHash
            FROM dbo.SupplierPayments p
            INNER JOIN dbo.AccountingPostingJobs j
              ON j.SourceDocumentId=p.PaymentId AND j.SourceDocumentType=N'PayablePayment'
            WHERE p.BusinessId=@BusinessId
              AND (p.PaymentId=@PaymentId OR p.IdempotencyKey=@IdempotencyKey);
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@PaymentId", paymentId);
        command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (!reader.GetFieldValue<byte[]>(4).AsSpan().SequenceEqual(requestHash))
            throw new PayablesConflictException(
                "The idempotency key or PaymentId was reused with another payload.");
        return new SupplierPaymentAcceptance(
            reader.GetGuid(0), reader.GetGuid(3), reader.GetString(1), reader.GetString(2), true);
    }

    private static async Task ValidateScopeAndAvailabilityAsync(
        SqlConnection connection, SqlTransaction transaction, PayablesUserIdentity user,
        ConfirmSupplierPaymentRequest request, PayableSettlement settlement,
        PaymentTenderBreakdown tenders,
        CancellationToken cancellationToken)
    {
        await using (var scope = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
              THROW 51200,'The business is outside the authenticated tenant.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.Suppliers WHERE SupplierId=@SupplierId AND BusinessId=@BusinessId AND IsActive=1)
              THROW 51201,'The supplier is outside the authenticated business.',1;
            IF @WorkSessionId IS NOT NULL AND NOT EXISTS(
              SELECT 1 FROM dbo.WorkSessions
              WHERE WorkSessionId=@WorkSessionId AND BusinessId=@BusinessId
                AND TenantId=@TenantId AND UserId=@UserId AND Status=N'Open')
              THROW 51202,'The work session is not open for the authenticated user.',1;
            """, connection, transaction))
        {
            scope.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            scope.Parameters.AddWithValue("@TenantId", user.TenantId);
            scope.Parameters.AddWithValue("@SupplierId", request.SupplierId);
            scope.Parameters.AddWithValue("@UserId", user.UserId);
            scope.Parameters.AddWithValue("@WorkSessionId", (object?)request.WorkSessionId ?? DBNull.Value);
            try { await scope.ExecuteNonQueryAsync(cancellationToken); }
            catch (SqlException exception) when (exception.Number is 51200 or 51201 or 51202)
            { throw new PayablesValidationException(exception.Message); }
        }
        await using var batch = new SqlCommand("""
            DECLARE @Input TABLE(PayableId uniqueidentifier PRIMARY KEY,Amount decimal(19,4));
            INSERT @Input SELECT PayableId,Amount FROM OPENJSON(@Allocations)
              WITH(PayableId uniqueidentifier,Amount decimal(19,4));
            IF (SELECT COUNT(*) FROM @Input)<>@Count THROW 51210,'The allocation batch is invalid.',1;
            IF EXISTS(
              SELECT 1 FROM @Input input
              LEFT JOIN dbo.Payables p WITH(UPDLOCK,HOLDLOCK) ON p.PayableId=input.PayableId
                AND p.BusinessId=@BusinessId
              OUTER APPLY(SELECT COALESCE(SUM(a.Amount),0) Reserved
                FROM dbo.SupplierPaymentApplications a WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.SupplierPayments payment WITH(UPDLOCK,HOLDLOCK) ON payment.PaymentId=a.PaymentId
                WHERE a.PayableId=input.PayableId AND a.AppliedAt IS NULL AND payment.Status=N'Accepted') pending
              WHERE p.PayableId IS NULL OR p.SupplierId<>@SupplierId OR p.CurrencyCode<>@Currency
                OR p.Status IN(N'Paid',N'Cancelled') OR input.Amount>p.OutstandingAmount-pending.Reserved)
              THROW 51211,'An allocation is unavailable or outside the selected supplier.',1;
            DECLARE @AccountingReady bit=CONVERT(bit,CASE WHEN EXISTS(
              SELECT 1 FROM dbo.AccountingTenantSettings WHERE TenantId=@TenantId AND Status=N'Ready'
                AND EffectiveFrom<=CONVERT(date,@PaidAt)) THEN 1 ELSE 0 END);
            IF EXISTS(SELECT 1 FROM OPENJSON(@Payments) WITH(
                MethodCode nvarchar(32),BankAccountId uniqueidentifier)
              WHERE MethodCode=N'BankTransfer' AND @AccountingReady=1 AND BankAccountId IS NULL)
              THROW 51212,'Select a bank account for every transfer.',1;
            IF EXISTS(SELECT 1 FROM OPENJSON(@Payments) WITH(BankAccountId uniqueidentifier) input
              WHERE input.BankAccountId IS NOT NULL AND NOT EXISTS(
                SELECT 1 FROM accounting.BankAccounts bank
                WHERE bank.BankAccountId=input.BankAccountId AND bank.TenantId=@TenantId AND bank.IsActive=1))
              THROW 51213,'A selected bank account is not active for this tenant.',1;
            """, connection, transaction);
        batch.Parameters.AddWithValue("@Allocations",JsonSerializer.Serialize(settlement.Allocations));
        batch.Parameters.AddWithValue("@Payments",JsonSerializer.Serialize(tenders.Tenders));
        batch.Parameters.AddWithValue("@Count",settlement.Allocations.Count);
        batch.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        batch.Parameters.AddWithValue("@TenantId",user.TenantId);
        batch.Parameters.AddWithValue("@SupplierId",request.SupplierId);
        batch.Parameters.AddWithValue("@Currency",request.CurrencyCode);
        batch.Parameters.AddWithValue("@PaidAt",request.PaidAt);
        try { await batch.ExecuteNonQueryAsync(cancellationToken); }
        catch(SqlException ex) when(ex.Number is >=51210 and <=51213)
        { if(ex.Number==51211) throw new PayablesConflictException(ex.Message); throw new PayablesValidationException(ex.Message); }
    }

    private static async Task<AuralyDocumentNumberAssignment> AllocateNumberAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        CancellationToken cancellationToken)
    {
        await using var select = new SqlCommand("""
            SELECT TOP(1) ds.DocumentSeriesId,ds.Prefix,ds.SeriesCode,ds.Padding,
                   ds.RangeEnd,COALESCE(c.NextConsecutive,ds.RangeStart)
            FROM dbo.DocumentSeries ds WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.DocumentSeriesCursors c WITH(UPDLOCK,HOLDLOCK)
              ON c.DocumentSeriesId=ds.DocumentSeriesId
            WHERE ds.BusinessId=@BusinessId AND ds.DocumentType=N'PayablePayment'
              AND ds.DeviceId IS NULL AND ds.IsActive=1
            ORDER BY ds.DocumentSeriesId;
            """, connection, transaction);
        select.Parameters.AddWithValue("@BusinessId", businessId);
        Guid seriesId; string prefix; string seriesCode; byte padding; long rangeEnd; long consecutive;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                throw new PayablesValidationException(
                    "No active PayablePayment document series is configured for the business.");
            seriesId = reader.GetGuid(0); prefix = reader.GetString(1);
            seriesCode = reader.GetString(2); padding = reader.GetByte(3);
            rangeEnd = reader.GetInt64(4); consecutive = reader.GetInt64(5);
        }
        if (consecutive > rangeEnd)
            throw new PayablesValidationException("The PayablePayment document series is exhausted.");
        await using var update = new SqlCommand("""
            IF EXISTS(SELECT 1 FROM dbo.DocumentSeriesCursors WHERE DocumentSeriesId=@SeriesId)
              UPDATE dbo.DocumentSeriesCursors SET NextConsecutive=@Next,UpdatedAt=@Now WHERE DocumentSeriesId=@SeriesId;
            ELSE INSERT dbo.DocumentSeriesCursors(DocumentSeriesId,NextConsecutive,UpdatedAt)
              VALUES(@SeriesId,@Next,@Now);
            """, connection, transaction);
        update.Parameters.AddWithValue("@SeriesId", seriesId);
        update.Parameters.AddWithValue("@Next", consecutive + 1);
        update.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        await update.ExecuteNonQueryAsync(cancellationToken);
        return AuralyDocumentNumberAssignment.Create(
            seriesId, AuralyDocumentTypes.PayablePayment, prefix, seriesCode, consecutive, padding);
    }

    private static async Task InsertPaymentAsync(
        SqlConnection connection, SqlTransaction transaction, PayablesUserIdentity user,
        ConfirmSupplierPaymentRequest request, PayableSettlement settlement,
        AuralyDocumentNumberAssignment number, string idempotencyKey, byte[] requestHash,
        string tendersJson, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.SupplierPayments
              (PaymentId,BusinessId,SupplierId,WorkSessionId,DocumentSeriesId,DocumentNumber,
               DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,IdempotencyKey,
               PayloadHash,PaidAt,CurrencyCode,Notes,TotalAmount,
               Status,ConfirmedByUserId,AcceptedAt)
            VALUES(@Id,@BusinessId,@SupplierId,@WorkSessionId,@SeriesId,@Number,@Prefix,@SeriesCode,
               @Consecutive,@Key,@Hash,@PaidAt,@Currency,@Notes,@Total,
               N'Accepted',@UserId,@Now);
            """, connection, transaction);
        command.Parameters.AddWithValue("@Id", request.PaymentId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@SupplierId", request.SupplierId);
        command.Parameters.AddWithValue("@SeriesId", number.SeriesId);
        command.Parameters.AddWithValue("@WorkSessionId", (object?)request.WorkSessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Number", number.FullNumber);
        command.Parameters.AddWithValue("@Prefix", number.Prefix);
        command.Parameters.AddWithValue("@SeriesCode", number.SeriesCode);
        command.Parameters.AddWithValue("@Consecutive", number.Consecutive);
        command.Parameters.AddWithValue("@Key", idempotencyKey);
        command.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = requestHash;
        command.Parameters.AddWithValue("@PaidAt", request.PaidAt);
        command.Parameters.AddWithValue("@Currency", request.CurrencyCode);
        command.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);
        AddMoney(command, "@Total", settlement.TotalAmount);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await using var tenders=new SqlCommand("""
            INSERT dbo.SupplierPaymentTenders(PaymentId,LineNumber,MethodCode,Amount,TenderedAmount,
              BankAccountId,Reference,Notes,CardFranchiseCode,ApprovalNumber)
            SELECT @PaymentId,LineNumber,MethodCode,Amount,TenderedAmount,BankAccountId,Reference,Notes,
              CardFranchiseCode,ApprovalNumber
            FROM OPENJSON(@Tenders) WITH(LineNumber int,MethodCode nvarchar(32),Amount decimal(19,4),
              TenderedAmount decimal(19,4),BankAccountId uniqueidentifier,Reference nvarchar(160),
              Notes nvarchar(500),CardFranchiseCode nvarchar(64),ApprovalNumber nvarchar(100));
            IF @@ROWCOUNT<>@Count THROW 51215,'The payment methods were not inserted atomically.',1;
            """,connection,transaction);
        tenders.Parameters.AddWithValue("@PaymentId",request.PaymentId);
        tenders.Parameters.AddWithValue("@Tenders",tendersJson);
        tenders.Parameters.AddWithValue("@Count",request.Payments.Count);
        await tenders.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertApplicationsAsync(
        SqlConnection connection, SqlTransaction transaction, Guid paymentId,
        PayableSettlement settlement, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.SupplierPaymentApplications(PaymentId,LineNumber,PayableId,Amount)
            SELECT @PaymentId,input.[key]+1,value.PayableId,value.Amount
            FROM OPENJSON(@Allocations) input CROSS APPLY OPENJSON(input.value)
              WITH(PayableId uniqueidentifier,Amount decimal(19,4)) value;
            IF @@ROWCOUNT<>@Count THROW 51214,'The applications were not inserted atomically.',1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@PaymentId", paymentId);
        command.Parameters.AddWithValue("@Allocations", JsonSerializer.Serialize(settlement.Allocations));
        command.Parameters.AddWithValue("@Count", settlement.Allocations.Count);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static byte[] HashRequest(
        ConfirmSupplierPaymentRequest request, PayableSettlement settlement,
        PaymentTenderBreakdown tenders) =>
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            request.PaymentId, request.BusinessId, request.SupplierId, request.PaidAt,
            Currency = request.CurrencyCode, request.Notes, request.WorkSessionId,
            settlement.TotalAmount, settlement.Allocations, tenders.Tenders
        }));

    private static void AddMoney(SqlCommand command, string name, decimal value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = 19; parameter.Scale = 4; parameter.Value = value;
    }
}
