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

public sealed class SqlReceivablesStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IReceivablesStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<CustomerPortfolioPage> ListCustomersAsync(ReceivablesUserIdentity user,
        CustomerPortfolioQuery query, CancellationToken token)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(token);
        await using var command=new SqlCommand("""
            WITH Paid AS(
              SELECT application.ReceivableId,SUM(application.Amount) PaidAmount
              FROM dbo.CustomerPaymentApplications application
              JOIN dbo.Receivables scoped ON scoped.ReceivableId=application.ReceivableId
              WHERE scoped.BusinessId=@BusinessId AND application.AppliedAt IS NOT NULL
              GROUP BY application.ReceivableId),
            Portfolio AS(
              SELECT r.CustomerId,COALESCE(p.DisplayName,p.LegalName,p.Identification) CustomerName,
                COALESCE(p.Identification,N'') Identification,COUNT(*) InvoiceCount,
                SUM(r.OriginalAmount) OriginalAmount,SUM(COALESCE(paid.PaidAmount,0)) PaidAmount,
                SUM(r.OutstandingAmount) OutstandingAmount,
                SUM(CASE WHEN r.OutstandingAmount>0 AND r.DueDate<@Now THEN r.OutstandingAmount ELSE 0 END) OverdueAmount
              FROM dbo.Receivables r
              JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId
              JOIN dbo.Customers c ON c.CustomerId=r.CustomerId
              JOIN dbo.Parties p ON p.PartyId=c.PartyId
              LEFT JOIN Paid paid ON paid.ReceivableId=r.ReceivableId
              WHERE r.BusinessId=@BusinessId AND b.TenantId=@TenantId
                AND (@CustomerId IS NULL OR r.CustomerId=@CustomerId)
                AND (@Status IS NULL OR r.Status=@Status)
                AND (@From IS NULL OR r.CreatedAt>=@From)
                AND (@To IS NULL OR r.CreatedAt<@To)
                AND (@Overdue IS NULL OR (@Overdue=1 AND r.OutstandingAmount>0 AND r.DueDate<@Now)
                  OR (@Overdue=0 AND (r.OutstandingAmount=0 OR r.DueDate>=@Now)))
                AND (@Search IS NULL OR p.DisplayName LIKE N'%' + @Search + N'%'
                  OR p.LegalName LIKE N'%' + @Search + N'%' OR p.Identification LIKE N'%' + @Search + N'%'
                  OR r.DocumentNumber LIKE N'%' + @Search + N'%')
              GROUP BY r.CustomerId,p.DisplayName,p.LegalName,p.Identification)
            SELECT COUNT(*),COALESCE(SUM(OutstandingAmount),0),COALESCE(SUM(OverdueAmount),0),COALESCE(SUM(InvoiceCount),0)
            FROM Portfolio WHERE @Overdue IS NULL OR (@Overdue=1 AND OverdueAmount>0)
              OR (@Overdue=0 AND OverdueAmount=0);
            WITH Paid AS(
              SELECT application.ReceivableId,SUM(application.Amount) PaidAmount
              FROM dbo.CustomerPaymentApplications application
              JOIN dbo.Receivables scoped ON scoped.ReceivableId=application.ReceivableId
              WHERE scoped.BusinessId=@BusinessId AND application.AppliedAt IS NOT NULL
              GROUP BY application.ReceivableId),
            Portfolio AS(
              SELECT r.CustomerId,COALESCE(p.DisplayName,p.LegalName,p.Identification) CustomerName,
                COALESCE(p.Identification,N'') Identification,COUNT(*) InvoiceCount,
                SUM(r.OriginalAmount) OriginalAmount,SUM(COALESCE(paid.PaidAmount,0)) PaidAmount,
                SUM(r.OutstandingAmount) OutstandingAmount,
                SUM(CASE WHEN r.OutstandingAmount>0 AND r.DueDate<@Now THEN r.OutstandingAmount ELSE 0 END) OverdueAmount
              FROM dbo.Receivables r
              JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId
              JOIN dbo.Customers c ON c.CustomerId=r.CustomerId
              JOIN dbo.Parties p ON p.PartyId=c.PartyId
              LEFT JOIN Paid paid ON paid.ReceivableId=r.ReceivableId
              WHERE r.BusinessId=@BusinessId AND b.TenantId=@TenantId
                AND (@CustomerId IS NULL OR r.CustomerId=@CustomerId)
                AND (@Status IS NULL OR r.Status=@Status)
                AND (@From IS NULL OR r.CreatedAt>=@From)
                AND (@To IS NULL OR r.CreatedAt<@To)
                AND (@Overdue IS NULL OR (@Overdue=1 AND r.OutstandingAmount>0 AND r.DueDate<@Now)
                  OR (@Overdue=0 AND (r.OutstandingAmount=0 OR r.DueDate>=@Now)))
                AND (@Search IS NULL OR p.DisplayName LIKE N'%' + @Search + N'%'
                  OR p.LegalName LIKE N'%' + @Search + N'%' OR p.Identification LIKE N'%' + @Search + N'%'
                  OR r.DocumentNumber LIKE N'%' + @Search + N'%')
              GROUP BY r.CustomerId,p.DisplayName,p.LegalName,p.Identification)
            SELECT CustomerId,CustomerName,Identification,InvoiceCount,OriginalAmount,PaidAmount,
              OutstandingAmount,OverdueAmount FROM Portfolio
            WHERE @Overdue IS NULL OR (@Overdue=1 AND OverdueAmount>0) OR (@Overdue=0 AND OverdueAmount=0)
            ORDER BY CASE WHEN OverdueAmount>0 THEN 0 ELSE 1 END,OutstandingAmount DESC,CustomerName
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """,connection);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@TenantId",user.TenantId);
        command.Parameters.AddWithValue("@Search",(object?)query.Search??DBNull.Value);command.Parameters.AddWithValue("@Overdue",(object?)query.Overdue??DBNull.Value);
        command.Parameters.AddWithValue("@CustomerId",(object?)query.CustomerId??DBNull.Value);
        command.Parameters.AddWithValue("@Status",(object?)query.Status??DBNull.Value);
        AddDateRange(command,query.From,query.To);
        command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());command.Parameters.AddWithValue("@Offset",(query.Page-1)*query.PageSize);command.Parameters.AddWithValue("@PageSize",query.PageSize);
        await using var reader=await command.ExecuteReaderAsync(token);await reader.ReadAsync(token);
        var count=reader.GetInt32(0);var outstanding=reader.GetDecimal(1);var overdue=reader.GetDecimal(2);var invoices=reader.GetInt32(3);
        await reader.NextResultAsync(token);var items=new List<CustomerPortfolioItem>();
        while(await reader.ReadAsync(token))items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetInt32(3),reader.GetDecimal(4),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetDecimal(7)));
        return new(items,query.Page,query.PageSize,count,outstanding,overdue,invoices);
    }

    public async Task<ReceivablePage> ListAsync(ReceivablesUserIdentity user, ReceivableQuery query, CancellationToken token)
    {
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
        int count; decimal outstanding; decimal overdue;
        await using (var command = new SqlCommand($"""
            SELECT COUNT(*),COALESCE(SUM(r.OutstandingAmount),0),
                   COALESCE(SUM(CASE WHEN r.OutstandingAmount>0 AND r.DueDate<@Now THEN r.OutstandingAmount ELSE 0 END),0)
            FROM dbo.Receivables r INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId
            INNER JOIN dbo.Customers c ON c.CustomerId=r.CustomerId
            INNER JOIN dbo.Parties p ON p.PartyId=c.PartyId
            LEFT JOIN dbo.PartySites site ON site.PartySiteId=r.PartySiteId WHERE {where};
            """, connection))
        {
            AddQuery(command, user, query, timeProvider.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(token);
            await reader.ReadAsync(token); count=reader.GetInt32(0); outstanding=reader.GetDecimal(1); overdue=reader.GetDecimal(2);
        }
        var items = new List<ReceivableListItem>();
        await using (var command = new SqlCommand($"""
            SELECT r.ReceivableId,r.CustomerId,COALESCE(p.DisplayName,p.LegalName,p.Identification),
                   r.DocumentNumber,r.CurrencyCode,r.OriginalAmount,r.OutstandingAmount,r.DueDate,
                   r.Status,r.CreatedAt,CAST(CASE WHEN r.OutstandingAmount>0 AND r.DueDate<@Now THEN 1 ELSE 0 END AS bit),
                   r.PartySiteId,site.Name
            FROM dbo.Receivables r INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId
            INNER JOIN dbo.Customers c ON c.CustomerId=r.CustomerId
            INNER JOIN dbo.Parties p ON p.PartyId=c.PartyId
            LEFT JOIN dbo.PartySites site ON site.PartySiteId=r.PartySiteId WHERE {where}
            ORDER BY CASE WHEN r.OutstandingAmount>0 AND r.DueDate<@Now THEN 0 ELSE 1 END,r.DueDate,r.ReceivableId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, connection))
        {
            AddQuery(command,user,query,timeProvider.GetUtcNow());
            command.Parameters.AddWithValue("@Offset",(query.Page-1)*query.PageSize);
            command.Parameters.AddWithValue("@PageSize",query.PageSize);
            await using var reader=await command.ExecuteReaderAsync(token);
            while(await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),
                reader.GetString(3),reader.GetString(4),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetDateTimeOffset(7),
                reader.GetString(8),reader.GetBoolean(10),reader.GetDateTimeOffset(9),
                reader.IsDBNull(11)?null:reader.GetGuid(11),reader.IsDBNull(12)?null:reader.GetString(12)));
        }
        return new(items,query.Page,query.PageSize,count,outstanding,overdue);
    }

    public Task<CustomerPaymentHistoryPage> PaymentHistoryAsync(ReceivablesUserIdentity user,
        Guid customerId, int page, int pageSize, CancellationToken token) =>
        ListPaymentsAsync(user,new(page,pageSize,null,customerId),token);

    public async Task<CustomerPaymentHistoryPage> ListPaymentsAsync(ReceivablesUserIdentity user,
        CustomerPaymentHistoryQuery query,CancellationToken token)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(token);
        await using var command=new SqlCommand("""
            SELECT COUNT(*) FROM dbo.CustomerPayments payment
            INNER JOIN dbo.Businesses business ON business.BusinessId=payment.BusinessId
            INNER JOIN dbo.Customers customer ON customer.CustomerId=payment.CustomerId
            INNER JOIN dbo.Parties party ON party.PartyId=customer.PartyId
            WHERE payment.BusinessId=@BusinessId AND business.TenantId=@TenantId
              AND (@CustomerId IS NULL OR payment.CustomerId=@CustomerId)
              AND (@From IS NULL OR payment.PaidAt>=@From)
              AND (@To IS NULL OR payment.PaidAt<@To)
              AND (@Status IS NULL AND @Overdue IS NULL OR EXISTS(
                SELECT 1 FROM dbo.CustomerPaymentApplications application
                JOIN dbo.Receivables invoice ON invoice.ReceivableId=application.ReceivableId
                WHERE application.PaymentId=payment.PaymentId
                  AND (@Status IS NULL OR invoice.Status=@Status)
                  AND (@Overdue IS NULL OR (@Overdue=1 AND invoice.OutstandingAmount>0 AND invoice.DueDate<@Now)
                    OR (@Overdue=0 AND (invoice.OutstandingAmount=0 OR invoice.DueDate>=@Now)))))
              AND (@Search IS NULL OR payment.DocumentNumber LIKE N'%' + @Search + N'%'
                OR party.DisplayName LIKE N'%' + @Search + N'%' OR party.LegalName LIKE N'%' + @Search + N'%'
                OR party.Identification LIKE N'%' + @Search + N'%'
                OR EXISTS(SELECT 1 FROM dbo.CustomerPaymentApplications application
                  JOIN dbo.Receivables invoice ON invoice.ReceivableId=application.ReceivableId
                  WHERE application.PaymentId=payment.PaymentId
                    AND invoice.DocumentNumber LIKE N'%' + @Search + N'%'));
            DECLARE @Page TABLE(PaymentId uniqueidentifier PRIMARY KEY);
            INSERT @Page(PaymentId)
            SELECT payment.PaymentId FROM dbo.CustomerPayments payment
            INNER JOIN dbo.Businesses business ON business.BusinessId=payment.BusinessId
            INNER JOIN dbo.Customers customer ON customer.CustomerId=payment.CustomerId
            INNER JOIN dbo.Parties party ON party.PartyId=customer.PartyId
            WHERE payment.BusinessId=@BusinessId AND business.TenantId=@TenantId
              AND (@CustomerId IS NULL OR payment.CustomerId=@CustomerId)
              AND (@From IS NULL OR payment.PaidAt>=@From)
              AND (@To IS NULL OR payment.PaidAt<@To)
              AND (@Status IS NULL AND @Overdue IS NULL OR EXISTS(
                SELECT 1 FROM dbo.CustomerPaymentApplications application
                JOIN dbo.Receivables invoice ON invoice.ReceivableId=application.ReceivableId
                WHERE application.PaymentId=payment.PaymentId
                  AND (@Status IS NULL OR invoice.Status=@Status)
                  AND (@Overdue IS NULL OR (@Overdue=1 AND invoice.OutstandingAmount>0 AND invoice.DueDate<@Now)
                    OR (@Overdue=0 AND (invoice.OutstandingAmount=0 OR invoice.DueDate>=@Now)))))
              AND (@Search IS NULL OR payment.DocumentNumber LIKE N'%' + @Search + N'%'
                OR party.DisplayName LIKE N'%' + @Search + N'%' OR party.LegalName LIKE N'%' + @Search + N'%'
                OR party.Identification LIKE N'%' + @Search + N'%'
                OR EXISTS(SELECT 1 FROM dbo.CustomerPaymentApplications application
                  JOIN dbo.Receivables invoice ON invoice.ReceivableId=application.ReceivableId
                  WHERE application.PaymentId=payment.PaymentId
                    AND invoice.DocumentNumber LIKE N'%' + @Search + N'%'))
            ORDER BY payment.PaidAt DESC,payment.PaymentId DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            SELECT payment.PaymentId,payment.DocumentNumber,payment.PaidAt,payment.CurrencyCode,
              payment.TotalAmount,payment.Status,
              COUNT(application.ReceivableId) AppliedDocumentCount,payment.CustomerId,
              COALESCE(party.DisplayName,party.LegalName,party.Identification) CustomerName
            FROM @Page page INNER JOIN dbo.CustomerPayments payment ON payment.PaymentId=page.PaymentId
            INNER JOIN dbo.Businesses business ON business.BusinessId=payment.BusinessId
            INNER JOIN dbo.Customers customer ON customer.CustomerId=payment.CustomerId
            INNER JOIN dbo.Parties party ON party.PartyId=customer.PartyId
            LEFT JOIN dbo.CustomerPaymentApplications application ON application.PaymentId=payment.PaymentId
            WHERE payment.BusinessId=@BusinessId AND business.TenantId=@TenantId
            GROUP BY payment.PaymentId,payment.DocumentNumber,payment.PaidAt,payment.CurrencyCode,
              payment.TotalAmount,payment.Status,payment.CustomerId,
              party.DisplayName,party.LegalName,party.Identification
            ORDER BY payment.PaidAt DESC,payment.PaymentId DESC;
            SELECT tender.PaymentId,tender.LineNumber,tender.MethodCode,tender.Amount,tender.TenderedAmount,
              tender.BankAccountId,tender.Reference,tender.Notes,tender.CardFranchiseCode,tender.ApprovalNumber
            FROM dbo.CustomerPaymentTenders tender INNER JOIN @Page page ON page.PaymentId=tender.PaymentId
            ORDER BY tender.PaymentId,tender.LineNumber;
            SELECT application.PaymentId,application.ReceivableId,receivable.DocumentNumber,application.Amount
            FROM dbo.CustomerPaymentApplications application
            INNER JOIN @Page page ON page.PaymentId=application.PaymentId
            INNER JOIN dbo.Receivables receivable ON receivable.ReceivableId=application.ReceivableId
            ORDER BY application.PaymentId,application.LineNumber;
            """,connection);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        command.Parameters.AddWithValue("@TenantId",user.TenantId);
        command.Parameters.AddWithValue("@CustomerId",(object?)query.CustomerId??DBNull.Value);
        command.Parameters.AddWithValue("@Search",(object?)query.Search??DBNull.Value);
        command.Parameters.AddWithValue("@Status",(object?)query.Status??DBNull.Value);
        command.Parameters.AddWithValue("@Overdue",(object?)query.Overdue??DBNull.Value);
        command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());
        AddDateRange(command,query.From,query.To);
        command.Parameters.AddWithValue("@Offset",(query.Page-1)*query.PageSize);
        command.Parameters.AddWithValue("@PageSize",query.PageSize);
        await using var reader=await command.ExecuteReaderAsync(token); await reader.ReadAsync(token);
        var total=reader.GetInt32(0); await reader.NextResultAsync(token);
        var items=new List<CustomerPaymentHistoryItem>();
        while(await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.GetString(1),
            reader.GetDateTimeOffset(2),reader.GetString(3),reader.GetDecimal(4),reader.GetString(5),reader.GetInt32(6),
            [],[],reader.GetGuid(7),reader.GetString(8)));
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
        return new(items.Select(item=>item with { Payments=byPayment[item.PaymentId],Applications=applications[item.PaymentId] }).ToArray(),
            query.Page,query.PageSize,total);
    }

    public async Task<ReceivableDetail?> GetAsync(ReceivablesUserIdentity user, Guid id, CancellationToken token)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(token);
        await using var header=new SqlCommand("""
            SELECT r.CustomerId,COALESCE(p.DisplayName,p.LegalName,p.Identification),COALESCE(p.Identification,N''),
                   r.SourceDocumentId,r.SourceDocumentType,r.DocumentNumber,r.CurrencyCode,r.OriginalAmount,
                   r.OutstandingAmount,r.DueDate,r.Status,r.PartySiteId,site.Name
            FROM dbo.Receivables r INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId
            INNER JOIN dbo.Customers c ON c.CustomerId=r.CustomerId INNER JOIN dbo.Parties p ON p.PartyId=c.PartyId
            LEFT JOIN dbo.PartySites site ON site.PartySiteId=r.PartySiteId
            WHERE r.ReceivableId=@Id AND r.BusinessId=@BusinessId AND b.TenantId=@TenantId;
            """,connection);
        header.Parameters.AddWithValue("@Id",id); header.Parameters.AddWithValue("@BusinessId",user.BusinessId); header.Parameters.AddWithValue("@TenantId",user.TenantId);
        Guid customerId, sourceId; Guid? partySiteId; string name, identification, sourceType, number, currency, status; string? partySiteName; decimal original,balance; DateTimeOffset due;
        await using(var reader=await header.ExecuteReaderAsync(token))
        {
            if(!await reader.ReadAsync(token)) return null;
            customerId=reader.GetGuid(0); name=reader.GetString(1); identification=reader.GetString(2); sourceId=reader.GetGuid(3);
            sourceType=reader.GetString(4); number=reader.GetString(5); currency=reader.GetString(6); original=reader.GetDecimal(7);
            balance=reader.GetDecimal(8); due=reader.GetDateTimeOffset(9); status=reader.GetString(10);
            partySiteId=reader.IsDBNull(11)?null:reader.GetGuid(11); partySiteName=reader.IsDBNull(12)?null:reader.GetString(12);
        }
        var movements=new List<ReceivableTransactionView>();
        await using var detail=new SqlCommand("SELECT ReceivableTransactionId,TransactionType,Amount,SourceDocumentId,OccurredAt FROM dbo.ReceivableTransactions WHERE ReceivableId=@Id ORDER BY OccurredAt,ReceivableTransactionId",connection);
        detail.Parameters.AddWithValue("@Id",id); await using(var reader=await detail.ExecuteReaderAsync(token))
            while(await reader.ReadAsync(token)) movements.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetDecimal(2),reader.GetGuid(3),reader.GetDateTimeOffset(4)));
        return new(id,customerId,name,identification,sourceId,sourceType,number,currency,original,balance,due,status,movements,partySiteId,partySiteName);
    }

    public async Task<CustomerCreditProfile?> GetCreditProfileAsync(ReceivablesUserIdentity user, Guid customerId, CancellationToken token)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(token);
        await using var command=new SqlCommand("""
            SELECT cp.CreditLimit,cp.DefaultDueDays,cp.IsCreditEnabled,
                   COALESCE(SUM(CASE WHEN r.Status IN(N'Open',N'PartiallyPaid') THEN r.OutstandingAmount ELSE 0 END),0)
            FROM dbo.Customers c INNER JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId
            LEFT JOIN dbo.CustomerCreditProfiles cp ON cp.CustomerId=c.CustomerId
            LEFT JOIN dbo.Receivables r ON r.CustomerId=c.CustomerId AND r.BusinessId=c.BusinessId
            WHERE c.CustomerId=@CustomerId AND c.BusinessId=@BusinessId AND b.TenantId=@TenantId
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
            IF NOT EXISTS(SELECT 1 FROM dbo.Customers c INNER JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId
                          WHERE c.CustomerId=@CustomerId AND c.BusinessId=@BusinessId AND b.TenantId=@TenantId)
                THROW 51300,'The customer is outside the authenticated business.',1;
            IF EXISTS(SELECT 1 FROM dbo.CustomerCreditProfiles WITH(UPDLOCK,HOLDLOCK) WHERE CustomerId=@CustomerId)
                UPDATE dbo.CustomerCreditProfiles SET CreditLimit=@Limit,DefaultDueDays=@Days,IsCreditEnabled=@Enabled,
                    UpdatedByUserId=@UserId,UpdatedAt=@Now WHERE CustomerId=@CustomerId;
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
                  JOIN dbo.Parties p ON p.PartyId=c.PartyId WHERE c.BusinessId=@BusinessId AND c.IsActive=1
                    AND ((i.CustomerId IS NOT NULL AND c.CustomerId=i.CustomerId) OR (i.CustomerId IS NULL
                      AND (p.Identification=i.CustomerIdentification OR p.NormalizedIdentification=i.CustomerIdentification)))) c
                  WHERE c.CustomerId IS NULL)
                  THROW 51321,N'Un cliente no pertenece al negocio o está inactivo.',1;
                IF EXISTS(SELECT 1 FROM @Input i CROSS APPLY(SELECT TOP(1)c.CustomerId,c.PartyId FROM dbo.Customers c
                  JOIN dbo.Parties p ON p.PartyId=c.PartyId WHERE c.BusinessId=@BusinessId AND c.IsActive=1
                    AND ((i.CustomerId IS NOT NULL AND c.CustomerId=i.CustomerId) OR (i.CustomerId IS NULL
                      AND (p.Identification=i.CustomerIdentification OR p.NormalizedIdentification=i.CustomerIdentification)))) c
                  LEFT JOIN dbo.PartySites site ON site.PartySiteId=i.PartySiteId AND site.PartyId=c.PartyId AND site.IsActive=1
                  WHERE i.PartySiteId IS NOT NULL AND site.PartySiteId IS NULL)
                  THROW 51322,N'Una sede no pertenece al cliente indicado.',1;
                IF EXISTS(SELECT 1 FROM @Input i CROSS APPLY(SELECT TOP(1)c.PartyId FROM dbo.Customers c
                  JOIN dbo.Parties p ON p.PartyId=c.PartyId WHERE c.BusinessId=@BusinessId AND c.IsActive=1
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
                  JOIN dbo.Parties p ON p.PartyId=c.PartyId WHERE c.BusinessId=@BusinessId AND c.IsActive=1
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
            var replay=await FindReplayAsync(connection,tx,user.BusinessId,request.PaymentId,key,hash,token); if(replay is not null){await tx.CommitAsync(token);return replay;}
            await ValidateAllocationsAsync(connection,tx,user,request,settlement,tenders,token);
            var number=await AllocateNumberAsync(connection,tx,user.BusinessId,token); var now=timeProvider.GetUtcNow();
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
            await InsertAcceptedAsync(connection,tx,user,key,request,settlement,number,hash,
                JsonSerializer.Serialize(paymentSnapshots),now,token);
            await SqlAccountingPostingJobWriter.InsertSourceAsync(connection,tx,user.TenantId,user.BusinessId,
                request.PaymentId,ReceivablesDocumentTypes.Payment,json,request.PaidAt,ids,timeProvider,token,
                AccountingJobRequirement.PreserveCommercialEffects,movementId);
            await tx.CommitAsync(token); return new(request.PaymentId,movementId,number.FullNumber,"Accepted",false);
        }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
    }

    private static async Task ValidateAllocationsAsync(SqlConnection c,SqlTransaction t,ReceivablesUserIdentity user,
        ConfirmCustomerPaymentRequest request,ReceivableSettlement settlement,
        PaymentTenderBreakdown tenders,CancellationToken token)
    {
        await using var command=new SqlCommand("""
            DECLARE @Input TABLE(ReceivableId uniqueidentifier PRIMARY KEY,Amount decimal(19,4));
            INSERT @Input SELECT ReceivableId,Amount FROM OPENJSON(@Allocations)
              WITH(ReceivableId uniqueidentifier,Amount decimal(19,4));
            IF (SELECT COUNT(*) FROM @Input)<>@Count THROW 51310,'The allocation batch is invalid.',1;
            IF EXISTS(
              SELECT 1 FROM @Input input
              LEFT JOIN dbo.Receivables r WITH(UPDLOCK,HOLDLOCK) ON r.ReceivableId=input.ReceivableId
                AND r.BusinessId=@BusinessId
              LEFT JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId AND b.TenantId=@TenantId
              OUTER APPLY(SELECT COALESCE(SUM(a.Amount),0) Reserved
                FROM dbo.CustomerPaymentApplications a WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.CustomerPayments p WITH(UPDLOCK,HOLDLOCK) ON p.PaymentId=a.PaymentId
                WHERE a.ReceivableId=input.ReceivableId AND a.AppliedAt IS NULL AND p.Status=N'Accepted') pending
              WHERE r.ReceivableId IS NULL OR b.BusinessId IS NULL OR r.CustomerId<>@CustomerId
                OR r.CurrencyCode<>@Currency OR r.Status IN(N'Paid',N'Cancelled')
                OR input.Amount>r.OutstandingAmount-pending.Reserved)
              THROW 51311,'An allocation is unavailable or outside the selected customer.',1;
            IF @SessionId IS NOT NULL AND NOT EXISTS(
              SELECT 1 FROM dbo.WorkSessions WITH(UPDLOCK,HOLDLOCK)
              WHERE WorkSessionId=@SessionId AND TenantId=@TenantId AND BusinessId=@BusinessId
                AND UserId=@UserId AND Status=N'Open')
              THROW 51312,'The work session is not open for this user.',1;
            DECLARE @AccountingReady bit=CONVERT(bit,CASE WHEN EXISTS(
              SELECT 1 FROM dbo.AccountingTenantSettings WHERE TenantId=@TenantId AND Status=N'Ready'
                AND EffectiveFrom<=CONVERT(date,@PaidAt)) THEN 1 ELSE 0 END);
            IF EXISTS(SELECT 1 FROM OPENJSON(@Payments) WITH(
                MethodCode nvarchar(32),BankAccountId uniqueidentifier)
              WHERE MethodCode=N'BankTransfer' AND @AccountingReady=1 AND BankAccountId IS NULL)
              THROW 51313,'Select a bank account for every transfer.',1;
            IF EXISTS(SELECT 1 FROM OPENJSON(@Payments) WITH(
                MethodCode nvarchar(32),BankAccountId uniqueidentifier) input
              WHERE input.BankAccountId IS NOT NULL AND NOT EXISTS(
                SELECT 1 FROM accounting.BankAccounts bank
                WHERE bank.BankAccountId=input.BankAccountId AND bank.TenantId=@TenantId AND bank.IsActive=1))
              THROW 51314,'A selected bank account is not active for this tenant.',1;
            """,c,t);
        command.Parameters.AddWithValue("@Allocations",JsonSerializer.Serialize(settlement.Allocations));
        command.Parameters.AddWithValue("@Payments",JsonSerializer.Serialize(tenders.Tenders));
        command.Parameters.AddWithValue("@Count",settlement.Allocations.Count);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@TenantId",user.TenantId);
        command.Parameters.AddWithValue("@CustomerId",request.CustomerId);command.Parameters.AddWithValue("@Currency",request.CurrencyCode);
        command.Parameters.AddWithValue("@SessionId",(object?)request.WorkSessionId??DBNull.Value);
        command.Parameters.AddWithValue("@UserId",user.UserId);command.Parameters.AddWithValue("@PaidAt",request.PaidAt);
        try { await command.ExecuteNonQueryAsync(token); }
        catch(SqlException ex) when(ex.Number is >=51310 and <=51314)
        { if(ex.Number==51311) throw new ReceivablesConflictException(ex.Message); throw new ReceivablesValidationException(ex.Message); }
    }

    private static async Task InsertAcceptedAsync(SqlConnection c,SqlTransaction t,ReceivablesUserIdentity user,
        string key,ConfirmCustomerPaymentRequest request,ReceivableSettlement settlement,
        AuralyDocumentNumberAssignment number,byte[] hash,string tendersJson,DateTimeOffset now,CancellationToken token)
    {
        await using(var command=new SqlCommand("""
            INSERT dbo.CustomerPayments(PaymentId,BusinessId,CustomerId,WorkSessionId,DocumentSeriesId,DocumentNumber,DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,IdempotencyKey,PayloadHash,PaidAt,CurrencyCode,Notes,TotalAmount,Status,ConfirmedByUserId,AcceptedAt)
            VALUES(@Id,@BusinessId,@CustomerId,@SessionId,@SeriesId,@Number,@Prefix,@SeriesCode,@Consecutive,@Key,@Hash,@PaidAt,@Currency,@Notes,@Total,N'Accepted',@UserId,@Now);
            """,c,t))
        {
            command.Parameters.AddWithValue("@Id",request.PaymentId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@CustomerId",request.CustomerId);command.Parameters.AddWithValue("@SessionId",(object?)request.WorkSessionId??DBNull.Value);
            command.Parameters.AddWithValue("@SeriesId",number.SeriesId);command.Parameters.AddWithValue("@Number",number.FullNumber);command.Parameters.AddWithValue("@Prefix",number.Prefix);command.Parameters.AddWithValue("@SeriesCode",number.SeriesCode);command.Parameters.AddWithValue("@Consecutive",number.Consecutive);
            command.Parameters.AddWithValue("@Key",key);command.Parameters.Add("@Hash",SqlDbType.Binary,32).Value=hash;command.Parameters.AddWithValue("@PaidAt",request.PaidAt);command.Parameters.AddWithValue("@Currency",request.CurrencyCode);
            command.Parameters.AddWithValue("@Notes",(object?)request.Notes??DBNull.Value);Money(command,"@Total",settlement.TotalAmount);command.Parameters.AddWithValue("@UserId",user.UserId);command.Parameters.AddWithValue("@Now",now);await command.ExecuteNonQueryAsync(token);
        }
        await using var tenders=new SqlCommand("""
            INSERT dbo.CustomerPaymentTenders(PaymentId,LineNumber,MethodCode,Amount,TenderedAmount,
              BankAccountId,Reference,Notes,CardFranchiseCode,ApprovalNumber)
            SELECT @PaymentId,LineNumber,MethodCode,Amount,TenderedAmount,BankAccountId,Reference,Notes,
              CardFranchiseCode,ApprovalNumber
            FROM OPENJSON(@Tenders) WITH(LineNumber int,MethodCode nvarchar(32),Amount decimal(19,4),
              TenderedAmount decimal(19,4),BankAccountId uniqueidentifier,Reference nvarchar(160),
              Notes nvarchar(500),CardFranchiseCode nvarchar(64),ApprovalNumber nvarchar(100));
            IF @@ROWCOUNT<>@Count THROW 51316,'The payment methods were not inserted atomically.',1;
            """,c,t);
        tenders.Parameters.AddWithValue("@PaymentId",request.PaymentId);
        tenders.Parameters.AddWithValue("@Tenders",tendersJson);
        tenders.Parameters.AddWithValue("@Count",request.Payments.Count);
        await tenders.ExecuteNonQueryAsync(token);
        await using var applications=new SqlCommand("""
            INSERT dbo.CustomerPaymentApplications(PaymentId,LineNumber,ReceivableId,Amount)
            SELECT @PaymentId,input.[key]+1,value.ReceivableId,value.Amount
            FROM OPENJSON(@Allocations) input CROSS APPLY OPENJSON(input.value)
              WITH(ReceivableId uniqueidentifier,Amount decimal(19,4)) value;
            IF @@ROWCOUNT<>@Count THROW 51315,'The applications were not inserted atomically.',1;
            """,c,t);
        applications.Parameters.AddWithValue("@PaymentId",request.PaymentId);
        applications.Parameters.AddWithValue("@Allocations",JsonSerializer.Serialize(settlement.Allocations));
        applications.Parameters.AddWithValue("@Count",settlement.Allocations.Count);
        await applications.ExecuteNonQueryAsync(token);
    }

    private static async Task<CustomerPaymentAcceptance?> FindReplayAsync(SqlConnection c,SqlTransaction t,Guid businessId,Guid paymentId,string key,byte[] hash,CancellationToken token)
    {
        await using var command=new SqlCommand("""
            SELECT p.PaymentId,p.DocumentNumber,p.Status,j.AccountingPostingJobId,p.PayloadHash FROM dbo.CustomerPayments p
            INNER JOIN dbo.AccountingPostingJobs j ON j.SourceDocumentId=p.PaymentId AND j.SourceDocumentType=N'ReceivablePayment'
            WHERE p.BusinessId=@BusinessId AND (p.PaymentId=@PaymentId OR p.IdempotencyKey=@Key);
            """,c,t);command.Parameters.AddWithValue("@BusinessId",businessId);command.Parameters.AddWithValue("@PaymentId",paymentId);command.Parameters.AddWithValue("@Key",key);
        await using var reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))return null;
        if(!reader.GetFieldValue<byte[]>(4).AsSpan().SequenceEqual(hash))throw new ReceivablesConflictException("The idempotency key or PaymentId was reused with another payload.");
        return new(reader.GetGuid(0),reader.GetGuid(3),reader.GetString(1),reader.GetString(2),true);
    }

    private static async Task<AuralyDocumentNumberAssignment> AllocateNumberAsync(SqlConnection c,SqlTransaction t,Guid businessId,CancellationToken token)
    {
        await using var select=new SqlCommand("""
            SELECT TOP(1) ds.DocumentSeriesId,ds.Prefix,ds.SeriesCode,ds.Padding,ds.RangeEnd,COALESCE(x.NextConsecutive,ds.RangeStart)
            FROM dbo.DocumentSeries ds WITH(UPDLOCK,HOLDLOCK) LEFT JOIN dbo.DocumentSeriesCursors x WITH(UPDLOCK,HOLDLOCK) ON x.DocumentSeriesId=ds.DocumentSeriesId
            WHERE ds.BusinessId=@BusinessId AND ds.DocumentType=N'ReceivablePayment' AND ds.DeviceId IS NULL AND ds.IsActive=1 ORDER BY ds.DocumentSeriesId;
            """,c,t);select.Parameters.AddWithValue("@BusinessId",businessId);
        Guid id;string prefix,code;byte padding;long end,next;await using(var reader=await select.ExecuteReaderAsync(token)){if(!await reader.ReadAsync(token))throw new ReceivablesValidationException("No active ReceivablePayment series is configured.");id=reader.GetGuid(0);prefix=reader.GetString(1);code=reader.GetString(2);padding=reader.GetByte(3);end=reader.GetInt64(4);next=reader.GetInt64(5);}if(next>end)throw new ReceivablesValidationException("The ReceivablePayment series is exhausted.");
        await using var update=new SqlCommand("IF EXISTS(SELECT 1 FROM dbo.DocumentSeriesCursors WHERE DocumentSeriesId=@Id) UPDATE dbo.DocumentSeriesCursors SET NextConsecutive=@Next,UpdatedAt=SYSDATETIMEOFFSET() WHERE DocumentSeriesId=@Id ELSE INSERT dbo.DocumentSeriesCursors(DocumentSeriesId,NextConsecutive,UpdatedAt) VALUES(@Id,@Next,SYSDATETIMEOFFSET())",c,t);update.Parameters.AddWithValue("@Id",id);update.Parameters.AddWithValue("@Next",next+1);await update.ExecuteNonQueryAsync(token);
        return AuralyDocumentNumberAssignment.Create(id,AuralyDocumentTypes.ReceivablePayment,prefix,code,next,padding);
    }
    private static void AddQuery(SqlCommand c,ReceivablesUserIdentity u,ReceivableQuery q,DateTimeOffset now){c.Parameters.AddWithValue("@BusinessId",u.BusinessId);c.Parameters.AddWithValue("@TenantId",u.TenantId);c.Parameters.AddWithValue("@CustomerId",(object?)q.CustomerId??DBNull.Value);c.Parameters.AddWithValue("@PartySiteId",(object?)q.PartySiteId??DBNull.Value);c.Parameters.AddWithValue("@Status",(object?)q.Status??DBNull.Value);c.Parameters.AddWithValue("@OutstandingOnly",q.OutstandingOnly);c.Parameters.AddWithValue("@Overdue",(object?)q.Overdue??DBNull.Value);c.Parameters.AddWithValue("@Search",(object?)q.Search??DBNull.Value);c.Parameters.AddWithValue("@Now",now);AddDateRange(c,q.From,q.To);}
    private static void AddDateRange(SqlCommand command,DateOnly? from,DateOnly? to)
    {
        command.Parameters.AddWithValue("@From",(object?)from?.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc)??DBNull.Value);
        command.Parameters.AddWithValue("@To",(object?)to?.AddDays(1).ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc)??DBNull.Value);
    }
    private static void Money(SqlCommand c,string name,decimal value){var p=c.Parameters.Add(name,SqlDbType.Decimal);p.Precision=19;p.Scale=4;p.Value=value;}
}
