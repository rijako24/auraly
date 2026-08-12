using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Receivables;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Receivables;
using Auraly.Domain.Receivables;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlReceivablesStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IReceivablesStore
{
    public async Task<ReceivablePage> ListAsync(ReceivablesUserIdentity user, ReceivableQuery query, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        const string where = """
            r.BusinessId=@BusinessId AND b.TenantId=@TenantId
            AND (@CustomerId IS NULL OR r.CustomerId=@CustomerId)
            AND (@Status IS NULL OR r.Status=@Status)
            AND (@Overdue IS NULL OR (@Overdue=1 AND r.OutstandingAmount>0 AND r.DueDate<@Now)
                 OR (@Overdue=0 AND (r.OutstandingAmount=0 OR r.DueDate>=@Now)))
            AND (@Search IS NULL OR r.DocumentNumber LIKE N'%' + @Search + N'%'
                 OR p.DisplayName LIKE N'%' + @Search + N'%'
                 OR p.Identification LIKE N'%' + @Search + N'%')
            """;
        int count; decimal outstanding; decimal overdue;
        await using (var command = new SqlCommand($"""
            SELECT COUNT(*),COALESCE(SUM(r.OutstandingAmount),0),
                   COALESCE(SUM(CASE WHEN r.OutstandingAmount>0 AND r.DueDate<@Now THEN r.OutstandingAmount ELSE 0 END),0)
            FROM dbo.Receivables r INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId
            INNER JOIN dbo.Customers c ON c.CustomerId=r.CustomerId
            INNER JOIN dbo.Parties p ON p.PartyId=c.PartyId WHERE {where};
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
                   r.Status,r.CreatedAt,CAST(CASE WHEN r.OutstandingAmount>0 AND r.DueDate<@Now THEN 1 ELSE 0 END AS bit)
            FROM dbo.Receivables r INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId
            INNER JOIN dbo.Customers c ON c.CustomerId=r.CustomerId
            INNER JOIN dbo.Parties p ON p.PartyId=c.PartyId WHERE {where}
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
                reader.GetString(8),reader.GetBoolean(10),reader.GetDateTimeOffset(9)));
        }
        return new(items,query.Page,query.PageSize,count,outstanding,overdue);
    }

    public async Task<ReceivableDetail?> GetAsync(ReceivablesUserIdentity user, Guid id, CancellationToken token)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(token);
        await using var header=new SqlCommand("""
            SELECT r.CustomerId,COALESCE(p.DisplayName,p.LegalName,p.Identification),COALESCE(p.Identification,N''),
                   r.SourceDocumentId,r.SourceDocumentType,r.DocumentNumber,r.CurrencyCode,r.OriginalAmount,
                   r.OutstandingAmount,r.DueDate,r.Status
            FROM dbo.Receivables r INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId
            INNER JOIN dbo.Customers c ON c.CustomerId=r.CustomerId INNER JOIN dbo.Parties p ON p.PartyId=c.PartyId
            WHERE r.ReceivableId=@Id AND r.BusinessId=@BusinessId AND b.TenantId=@TenantId;
            """,connection);
        header.Parameters.AddWithValue("@Id",id); header.Parameters.AddWithValue("@BusinessId",user.BusinessId); header.Parameters.AddWithValue("@TenantId",user.TenantId);
        Guid customerId, sourceId; string name, identification, sourceType, number, currency, status; decimal original,balance; DateTimeOffset due;
        await using(var reader=await header.ExecuteReaderAsync(token))
        {
            if(!await reader.ReadAsync(token)) return null;
            customerId=reader.GetGuid(0); name=reader.GetString(1); identification=reader.GetString(2); sourceId=reader.GetGuid(3);
            sourceType=reader.GetString(4); number=reader.GetString(5); currency=reader.GetString(6); original=reader.GetDecimal(7);
            balance=reader.GetDecimal(8); due=reader.GetDateTimeOffset(9); status=reader.GetString(10);
        }
        var movements=new List<ReceivableTransactionView>();
        await using var detail=new SqlCommand("SELECT ReceivableTransactionId,TransactionType,Amount,SourceDocumentId,OccurredAt FROM dbo.ReceivableTransactions WHERE ReceivableId=@Id ORDER BY OccurredAt,ReceivableTransactionId",connection);
        detail.Parameters.AddWithValue("@Id",id); await using(var reader=await detail.ExecuteReaderAsync(token))
            while(await reader.ReadAsync(token)) movements.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetDecimal(2),reader.GetGuid(3),reader.GetDateTimeOffset(4)));
        return new(id,customerId,name,identification,sourceId,sourceType,number,currency,original,balance,due,status,movements);
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
            COMMIT TRANSACTION;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
            """,connection);
        command.Parameters.AddWithValue("@CustomerId",customerId); command.Parameters.AddWithValue("@BusinessId",user.BusinessId); command.Parameters.AddWithValue("@TenantId",user.TenantId);
        command.Parameters.AddWithValue("@Limit",(object?)request.CreditLimit??DBNull.Value); command.Parameters.AddWithValue("@Days",request.DefaultDueDays);
        command.Parameters.AddWithValue("@Enabled",request.IsCreditEnabled); command.Parameters.AddWithValue("@UserId",user.UserId); command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());
        try { await command.ExecuteNonQueryAsync(token); } catch(SqlException ex) when(ex.Number==51300) { throw new ReceivablesValidationException(ex.Message); }
        return (await GetCreditProfileAsync(user,customerId,token))!;
    }

    public async Task<CustomerPaymentAcceptance> AcceptPaymentAsync(ReceivablesUserIdentity user,string key,ConfirmCustomerPaymentRequest request,ReceivableSettlement settlement,CancellationToken token)
    {
        var hash=SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { request.PaymentId,request.BusinessId,request.CustomerId,request.WorkSessionId,request.PaidAt,request.CurrencyCode,request.PaymentMethod,request.Reference,request.Notes,settlement.Allocations }));
        for(var attempt=1;;attempt++)
        {
            try
            {
                return await AcceptPaymentAttemptAsync(user,key,request,settlement,hash,token);
            }
            catch(SqlException exception) when(exception.Number==1205&&attempt<3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25*attempt),timeProvider,token);
            }
        }
    }

    private async Task<CustomerPaymentAcceptance> AcceptPaymentAttemptAsync(ReceivablesUserIdentity user,string key,ConfirmCustomerPaymentRequest request,ReceivableSettlement settlement,byte[] hash,CancellationToken token)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(token);
        await using var tx=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token);
        try
        {
            var replay=await FindReplayAsync(connection,tx,user.BusinessId,request.PaymentId,key,hash,token); if(replay is not null){await tx.CommitAsync(token);return replay;}
            await ValidateAllocationsAsync(connection,tx,user,request,settlement,token);
            var number=await AllocateNumberAsync(connection,tx,user.BusinessId,token); var now=timeProvider.GetUtcNow();
            var sequence=await AllocateSequenceAsync(connection,tx,user.BusinessId,now,token); var movementId=ids.NewId();
            var payload=new CustomerPaymentDocumentPayload(user.TenantId,user.BusinessId,request.PaymentId,request.CustomerId,user.UserId,
                request.WorkSessionId,number.FullNumber,number.SeriesId,number.Prefix,number.SeriesCode,number.Consecutive,request.PaidAt,
                request.CurrencyCode,request.PaymentMethod,request.Reference,request.Notes,settlement.TotalAmount,
                settlement.Allocations.Select((x,i)=>new CustomerPaymentAllocationSnapshot(i+1,x.ReceivableId,x.Amount)).ToArray());
            var json=CustomerPaymentContractSerializer.Serialize(payload); var payloadHash=SHA256.HashData(Encoding.UTF8.GetBytes(json));
            await InsertAcceptedAsync(connection,tx,user,key,request,settlement,number,hash,movementId,sequence,json,payloadHash,now,token);
            await tx.CommitAsync(token); return new(request.PaymentId,movementId,number.FullNumber,"Accepted",sequence,false);
        }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
    }

    private static async Task ValidateAllocationsAsync(SqlConnection c,SqlTransaction t,ReceivablesUserIdentity user,ConfirmCustomerPaymentRequest request,ReceivableSettlement settlement,CancellationToken token)
    {
        foreach(var allocation in settlement.Allocations.OrderBy(x=>x.ReceivableId))
        {
            await using var command=new SqlCommand("""
                SELECT r.CustomerId,r.CurrencyCode,r.OutstandingAmount,r.Status,
                  COALESCE(SUM(CASE WHEN p.Status=N'Accepted' THEN a.Amount ELSE 0 END),0)
                FROM dbo.Receivables r WITH(UPDLOCK,HOLDLOCK)
                INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId
                LEFT JOIN dbo.CustomerPaymentApplications a WITH(UPDLOCK,HOLDLOCK) ON a.ReceivableId=r.ReceivableId AND a.AppliedAt IS NULL
                LEFT JOIN dbo.CustomerPayments p WITH(UPDLOCK,HOLDLOCK) ON p.PaymentId=a.PaymentId
                WHERE r.ReceivableId=@Id AND r.BusinessId=@BusinessId AND b.TenantId=@TenantId
                GROUP BY r.CustomerId,r.CurrencyCode,r.OutstandingAmount,r.Status;
                """,c,t);
            command.Parameters.AddWithValue("@Id",allocation.ReceivableId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@TenantId",user.TenantId);
            await using var reader=await command.ExecuteReaderAsync(token);
            if(!await reader.ReadAsync(token)) throw new ReceivablesValidationException("An allocation references an unknown receivable.");
            if(reader.GetGuid(0)!=request.CustomerId||reader.GetString(1)!=request.CurrencyCode) throw new ReceivablesValidationException("All receivables must belong to the selected customer and currency.");
            if(reader.GetString(3) is "Paid" or "Cancelled") throw new ReceivablesValidationException("A settled receivable cannot receive a payment.");
            if(allocation.Amount>reader.GetDecimal(2)-reader.GetDecimal(4)) throw new ReceivablesConflictException("The allocation exceeds the unreserved balance.");
        }
        if(request.WorkSessionId is Guid sessionId)
        {
            await using var session=new SqlCommand("SELECT COUNT_BIG(1) FROM dbo.WorkSessions WITH(UPDLOCK,HOLDLOCK) WHERE WorkSessionId=@Id AND BusinessId=@BusinessId AND UserId=@UserId AND Status=N'Open'",c,t);
            session.Parameters.AddWithValue("@Id",sessionId);session.Parameters.AddWithValue("@BusinessId",user.BusinessId);session.Parameters.AddWithValue("@UserId",user.UserId);
            if(Convert.ToInt64(await session.ExecuteScalarAsync(token))!=1) throw new ReceivablesValidationException("The work session is not open for this user.");
        }
    }

    private static async Task InsertAcceptedAsync(SqlConnection c,SqlTransaction t,ReceivablesUserIdentity user,string key,ConfirmCustomerPaymentRequest request,ReceivableSettlement settlement,AuralyDocumentNumberAssignment number,byte[] hash,Guid movementId,long sequence,string json,byte[] payloadHash,DateTimeOffset now,CancellationToken token)
    {
        await using(var command=new SqlCommand("""
            INSERT dbo.CustomerPayments(PaymentId,BusinessId,CustomerId,WorkSessionId,DocumentSeriesId,DocumentNumber,DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,IdempotencyKey,PayloadHash,PaidAt,CurrencyCode,PaymentMethod,Reference,Notes,TotalAmount,Status,ConfirmedByUserId,AcceptedAt)
            VALUES(@Id,@BusinessId,@CustomerId,@SessionId,@SeriesId,@Number,@Prefix,@SeriesCode,@Consecutive,@Key,@Hash,@PaidAt,@Currency,@Method,@Reference,@Notes,@Total,N'Accepted',@UserId,@Now);
            """,c,t))
        {
            command.Parameters.AddWithValue("@Id",request.PaymentId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@CustomerId",request.CustomerId);command.Parameters.AddWithValue("@SessionId",(object?)request.WorkSessionId??DBNull.Value);
            command.Parameters.AddWithValue("@SeriesId",number.SeriesId);command.Parameters.AddWithValue("@Number",number.FullNumber);command.Parameters.AddWithValue("@Prefix",number.Prefix);command.Parameters.AddWithValue("@SeriesCode",number.SeriesCode);command.Parameters.AddWithValue("@Consecutive",number.Consecutive);
            command.Parameters.AddWithValue("@Key",key);command.Parameters.Add("@Hash",SqlDbType.Binary,32).Value=hash;command.Parameters.AddWithValue("@PaidAt",request.PaidAt);command.Parameters.AddWithValue("@Currency",request.CurrencyCode);command.Parameters.AddWithValue("@Method",request.PaymentMethod);
            command.Parameters.AddWithValue("@Reference",(object?)request.Reference??DBNull.Value);command.Parameters.AddWithValue("@Notes",(object?)request.Notes??DBNull.Value);Money(command,"@Total",settlement.TotalAmount);command.Parameters.AddWithValue("@UserId",user.UserId);command.Parameters.AddWithValue("@Now",now);await command.ExecuteNonQueryAsync(token);
        }
        for(var i=0;i<settlement.Allocations.Count;i++)
        {
            await using var command=new SqlCommand("INSERT dbo.CustomerPaymentApplications(PaymentId,LineNumber,ReceivableId,Amount) VALUES(@PaymentId,@Line,@ReceivableId,@Amount)",c,t);
            command.Parameters.AddWithValue("@PaymentId",request.PaymentId);command.Parameters.AddWithValue("@Line",i+1);command.Parameters.AddWithValue("@ReceivableId",settlement.Allocations[i].ReceivableId);Money(command,"@Amount",settlement.Allocations[i].Amount);await command.ExecuteNonQueryAsync(token);
        }
        await using var job=new SqlCommand("""
            INSERT dbo.DocumentProcessingJobs(JobId,BusinessId,ProcessingSequence,DocumentId,DocumentType,Status,AvailableAt,CreatedAt)
            VALUES(@JobId,@BusinessId,@Sequence,@DocumentId,N'ReceivablePayment',N'Pending',@Now,@Now);
            INSERT dbo.DocumentProcessingPayloads(DocumentId,DocumentType,BusinessId,ContractVersion,PayloadJson,PayloadHash,AcceptedAt)
            VALUES(@DocumentId,N'ReceivablePayment',@BusinessId,1,@Payload,@PayloadHash,@Now);
            """,c,t);
        job.Parameters.AddWithValue("@JobId",movementId);job.Parameters.AddWithValue("@BusinessId",user.BusinessId);job.Parameters.AddWithValue("@Sequence",sequence);job.Parameters.AddWithValue("@DocumentId",request.PaymentId);job.Parameters.AddWithValue("@Now",now);job.Parameters.AddWithValue("@Payload",json);job.Parameters.Add("@PayloadHash",SqlDbType.Binary,32).Value=payloadHash;await job.ExecuteNonQueryAsync(token);
    }

    private static async Task<CustomerPaymentAcceptance?> FindReplayAsync(SqlConnection c,SqlTransaction t,Guid businessId,Guid paymentId,string key,byte[] hash,CancellationToken token)
    {
        await using var command=new SqlCommand("""
            SELECT p.PaymentId,p.DocumentNumber,p.Status,j.ProcessingSequence,j.JobId,p.PayloadHash FROM dbo.CustomerPayments p
            INNER JOIN dbo.DocumentProcessingJobs j ON j.DocumentId=p.PaymentId AND j.DocumentType=N'ReceivablePayment'
            WHERE p.BusinessId=@BusinessId AND (p.PaymentId=@PaymentId OR p.IdempotencyKey=@Key);
            """,c,t);command.Parameters.AddWithValue("@BusinessId",businessId);command.Parameters.AddWithValue("@PaymentId",paymentId);command.Parameters.AddWithValue("@Key",key);
        await using var reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))return null;
        if(!reader.GetFieldValue<byte[]>(5).AsSpan().SequenceEqual(hash))throw new ReceivablesConflictException("The idempotency key or PaymentId was reused with another payload.");
        return new(reader.GetGuid(0),reader.GetGuid(4),reader.GetString(1),reader.GetString(2),reader.GetInt64(3),true);
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
    private static async Task<long> AllocateSequenceAsync(SqlConnection c,SqlTransaction t,Guid businessId,DateTimeOffset now,CancellationToken token)
    {
        await using var command=new SqlCommand("IF NOT EXISTS(SELECT 1 FROM dbo.BusinessProcessingCursors WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@Id) INSERT dbo.BusinessProcessingCursors(BusinessId,LastAssignedSequence,LastCompletedSequence,UpdatedAt) VALUES(@Id,0,0,@Now); UPDATE dbo.BusinessProcessingCursors WITH(UPDLOCK,HOLDLOCK) SET LastAssignedSequence=LastAssignedSequence+1,UpdatedAt=@Now OUTPUT inserted.LastAssignedSequence WHERE BusinessId=@Id;",c,t);command.Parameters.AddWithValue("@Id",businessId);command.Parameters.AddWithValue("@Now",now);return Convert.ToInt64(await command.ExecuteScalarAsync(token));
    }
    private static void AddQuery(SqlCommand c,ReceivablesUserIdentity u,ReceivableQuery q,DateTimeOffset now){c.Parameters.AddWithValue("@BusinessId",u.BusinessId);c.Parameters.AddWithValue("@TenantId",u.TenantId);c.Parameters.AddWithValue("@CustomerId",(object?)q.CustomerId??DBNull.Value);c.Parameters.AddWithValue("@Status",(object?)q.Status??DBNull.Value);c.Parameters.AddWithValue("@Overdue",(object?)q.Overdue??DBNull.Value);c.Parameters.AddWithValue("@Search",(object?)q.Search??DBNull.Value);c.Parameters.AddWithValue("@Now",now);}
    private static void Money(SqlCommand c,string name,decimal value){var p=c.Parameters.Add(name,SqlDbType.Decimal);p.Precision=19;p.Scale=4;p.Value=value;}
}
