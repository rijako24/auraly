using System.Data;
using Auraly.Application.Dispatching;
using Auraly.Contracts.Dispatching;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Dispatching;

public sealed class SqlDispatchDeliveryStore(DispatchingSqlConnectionFactory connections) : IDispatchDeliveryStore
{
    public async Task<IReadOnlyList<DispatchReasonOption>> ReasonsAsync(DispatchActorIdentity actor, string reasonType, CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);
        await using var command=new SqlCommand("SELECT DispatchReasonId,ReasonType,Code,Name FROM dispatch.DispatchReasons WHERE BusinessId=@BusinessId AND ReasonType=@Type AND IsActive=1 ORDER BY DisplayOrder,Name",connection);
        command.Parameters.AddWithValue("@BusinessId",actor.BusinessId);command.Parameters.AddWithValue("@Type",reasonType);
        var result=new List<DispatchReasonOption>();await using var reader=await command.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))result.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3)));
        return result;
    }

    public async Task<DispatchExecutionDetail?> GetAsync(DispatchActorIdentity actor, Guid dispatchId, CancellationToken ct)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(ct);
        await using var header=new SqlCommand("""
          SELECT DispatchId,DispatchNumber,ScheduledDate,DriverName,VehiclePlate,Status
          FROM dbo.Dispatches WHERE DispatchId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId
            AND (@ReadAll=1 OR @Settle=1 OR DriverUserId=@UserId);
        """,connection); Scope(header,actor,dispatchId);
        Guid id;string number,driver,status;DateOnly date;string? plate;
        await using(var reader=await header.ExecuteReaderAsync(ct))
        { if(!await reader.ReadAsync(ct))return null;id=reader.GetGuid(0);number=reader.GetString(1);date=DateOnly.FromDateTime(reader.GetDateTime(2));driver=reader.GetString(3);plate=NullableString(reader,4);status=reader.GetString(5); }

        var payments=new Dictionary<Guid,List<DispatchDeliveryPaymentDetail>>();
        await using(var command=new SqlCommand("""
          SELECT DispatchSourceDocumentId,DispatchDeliveryPaymentId,ApplicationType,PaymentMethod,Amount,Reference,EvidenceUrl
          FROM dbo.DispatchDeliveryPayments WHERE DispatchId=@Id ORDER BY CreatedAt,DispatchDeliveryPaymentId;
        """,connection))
        { command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct)){var documentId=reader.GetGuid(0);if(!payments.TryGetValue(documentId,out var values))payments[documentId]=values=[];values.Add(new(reader.GetGuid(1),reader.GetString(2),NullableString(reader,3),reader.GetDecimal(4),NullableString(reader,5),NullableString(reader,6)));} }

        var returns=new Dictionary<Guid,List<DispatchDeliveryReturnDetail>>();
        await using(var command=new SqlCommand("""
          SELECT r.DispatchSourceDocumentId,r.DispatchDeliveryReturnId,r.OriginalLineNumber,r.ProductId,
            COALESCE(p.ProductCode,p.Sku,CONVERT(nvarchar(36),p.ProductId)),l.Description,r.Quantity,r.InventoryDisposition,r.ReasonCode,r.ReasonDescription
          FROM dbo.DispatchDeliveryReturns r INNER JOIN dbo.Products p ON p.ProductId=r.ProductId
          INNER JOIN dbo.DispatchSourceDocuments source ON source.DispatchSourceDocumentId=r.DispatchSourceDocumentId
          INNER JOIN dbo.SalesDocumentLines l ON l.DocumentId=source.SourceDocumentId AND l.LineNumber=r.OriginalLineNumber
          WHERE r.DispatchId=@Id ORDER BY r.OriginalLineNumber;
        """,connection))
        { command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct)){var documentId=reader.GetGuid(0);if(!returns.TryGetValue(documentId,out var values))returns[documentId]=values=[];values.Add(new(reader.GetGuid(1),reader.GetInt32(2),reader.GetGuid(3),reader.GetString(4),reader.GetString(5),reader.GetDecimal(6),reader.GetString(7),reader.GetString(8),reader.GetString(9)));} }

        var lines=new Dictionary<Guid,List<DispatchDeliveryProductLine>>();
        await using(var command=new SqlCommand("SELECT DispatchSourceDocumentId,SourceLineNumber,ProductId,ProductCodeSnapshot,DescriptionSnapshot,AssignedQuantity,UnitPriceSnapshot,LineTotalSnapshot FROM dbo.DispatchLines WHERE DispatchId=@Id ORDER BY SourceLineNumber",connection))
        { command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct)){var documentId=reader.GetGuid(0);if(!lines.TryGetValue(documentId,out var values))lines[documentId]=values=[];values.Add(new(reader.GetInt32(1),reader.GetGuid(2),reader.GetString(3),reader.GetString(4),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetDecimal(7)));} }

        var documents=new List<DispatchDeliveryDocument>();
        await using(var command=new SqlCommand("""
          SELECT source.DispatchSourceDocumentId,source.SourceDocumentId,source.SourceDocumentType,source.DocumentNumberSnapshot,
            COALESCE(custom.Sequence,routeStop.Sequence,ROW_NUMBER() OVER(ORDER BY source.CustomerNameSnapshot,source.DocumentNumberSnapshot)),
            source.CustomerId,source.CustomerNameSnapshot,source.DeliveryAddressSnapshot,source.DocumentTotalSnapshot,sale.CreditAmount,
            destination.Latitude,destination.Longitude,COALESCE(delivery.DeliveryStatus,N'Pending'),delivery.Reason,delivery.Notes,delivery.Latitude,delivery.Longitude,delivery.OccurredAt
          FROM dbo.DispatchSourceDocuments source INNER JOIN dbo.Dispatches dispatch ON dispatch.DispatchId=source.DispatchId
          INNER JOIN dbo.SalesDocuments sale ON sale.DocumentId=source.SourceDocumentId
          LEFT JOIN dbo.DispatchDocumentSequences custom ON custom.DispatchId=source.DispatchId AND custom.DispatchSourceDocumentId=source.DispatchSourceDocumentId
          OUTER APPLY(SELECT TOP(1) stop.Sequence,stop.PartySiteId FROM dbo.SalesRouteStops stop LEFT JOIN dbo.PartySites stopSite ON stopSite.PartySiteId=stop.PartySiteId WHERE stop.RouteId=dispatch.RouteId AND stop.CustomerId=source.CustomerId AND stop.IsActive=1 ORDER BY CASE WHEN stopSite.AddressLine=source.DeliveryAddressSnapshot THEN 0 ELSE 1 END,stop.Sequence,stop.RouteStopId) routeStop
          OUTER APPLY(SELECT TOP(1) site.Latitude,site.Longitude FROM dbo.Customers customer INNER JOIN dbo.PartySites site ON site.PartyId=customer.PartyId AND site.IsActive=1 WHERE customer.CustomerId=source.CustomerId ORDER BY CASE WHEN site.PartySiteId=routeStop.PartySiteId THEN 0 WHEN site.IsPrimary=1 THEN 1 ELSE 2 END,site.CreatedAt) destination
          LEFT JOIN dbo.DispatchDeliveryEvents delivery ON delivery.DispatchSourceDocumentId=source.DispatchSourceDocumentId
          WHERE source.DispatchId=@Id ORDER BY COALESCE(custom.Sequence,routeStop.Sequence,2147483647),source.CustomerNameSnapshot,source.DocumentNumberSnapshot;
        """,connection))
        { command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct)){var documentId=reader.GetGuid(0);documents.Add(new(documentId,reader.GetGuid(1),reader.GetString(2),reader.GetString(3),Convert.ToInt32(reader.GetValue(4)),NullableGuid(reader,5),reader.GetString(6),NullableString(reader,7),reader.GetDecimal(8),reader.GetDecimal(9),NullableDecimal(reader,10),NullableDecimal(reader,11),reader.GetString(12),NullableString(reader,13),NullableString(reader,14),NullableDecimal(reader,15),NullableDecimal(reader,16),reader.IsDBNull(17)?null:reader.GetDateTimeOffset(17),lines.GetValueOrDefault(documentId)??[],payments.GetValueOrDefault(documentId)??[],returns.GetValueOrDefault(documentId)??[]));} }

        var expenses=new List<DispatchExpenseDetail>();
        await using(var command=new SqlCommand("SELECT DispatchExpenseId,Category,Amount,Description,EvidenceUrl,ApprovalStatus,ApprovedAmount FROM dbo.DispatchExpenses WHERE DispatchId=@Id ORDER BY OccurredAt,DispatchExpenseId",connection))
        { command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))expenses.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetDecimal(2),NullableString(reader,3),NullableString(reader,4),reader.GetString(5),NullableDecimal(reader,6))); }
        var dispatchTotal=documents.Sum(document=>document.DocumentTotal);
        var grossCash=payments.Values.SelectMany(value=>value).Where(value=>value.PaymentMethod=="Cash").Sum(value=>value.Amount);
        var approvedExpenses=expenses.Where(value=>value.ApprovalStatus=="Approved").Sum(value=>value.ApprovedAmount??0);

        DispatchSettlementSummary? settlement=null;
        await using(var command=new SqlCommand("""
          SELECT ExpectedCash,DeclaredCash,CashDifference,DepositTotal,CreditDocumentTotal,CreditAdvanceTotal,ReturnTotal,Status,ReceivedBy,ReceivedAt
          FROM dbo.DispatchSettlements WHERE DispatchId=@Id;
        """,connection))
        { command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);if(await reader.ReadAsync(ct)){var expected=reader.GetDecimal(0);var deposits=reader.GetDecimal(3);var remainingCredit=reader.GetDecimal(4);var advances=reader.GetDecimal(5);var returnTotal=reader.GetDecimal(6);var undelivered=documents.Where(value=>value.DeliveryStatus==DeliveryStatuses.NotDelivered).Sum(value=>value.DocumentTotal);var balance=dispatchTotal-(grossCash+deposits+remainingCredit+returnTotal+undelivered);settlement=new(grossCash,approvedExpenses,expected,reader.GetDecimal(1),reader.GetDecimal(2),deposits,remainingCredit,advances,returnTotal,undelivered,dispatchTotal,balance,reader.GetString(7),NullableGuid(reader,8),reader.IsDBNull(9)?null:reader.GetDateTimeOffset(9));} }
        return new(id,number,date,driver,plate,status,documents,expenses,settlement);
    }

    public async Task<DispatchExecutionDetail> RecordAsync(DispatchActorIdentity actor, Guid dispatchId, RecordDispatchDeliveryRequest request, CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);await using var tx=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            await using(var replay=new SqlCommand("SELECT DispatchId FROM dbo.DispatchDeliveryEvents WHERE BusinessId=@BusinessId AND IdempotencyKey=@Key",connection,tx))
            { replay.Parameters.AddWithValue("@BusinessId",actor.BusinessId);replay.Parameters.AddWithValue("@Key",request.IdempotencyKey);if(await replay.ExecuteScalarAsync(ct) is not null){await tx.CommitAsync(ct);return (await GetAsync(actor,dispatchId,ct))!;} }
            Guid sourceDocumentId;decimal total,credit;
            await using(var document=new SqlCommand("""
              SELECT source.SourceDocumentId,source.DocumentTotalSnapshot,sale.CreditAmount
              FROM dbo.DispatchSourceDocuments source INNER JOIN dbo.Dispatches dispatch WITH(UPDLOCK,HOLDLOCK) ON dispatch.DispatchId=source.DispatchId
              INNER JOIN dbo.SalesDocuments sale ON sale.DocumentId=source.SourceDocumentId
              WHERE source.DispatchSourceDocumentId=@DocumentId AND source.DispatchId=@Id AND dispatch.TenantId=@TenantId AND dispatch.BusinessId=@BusinessId
                AND dispatch.DriverUserId=@UserId AND dispatch.Status IN(N'Released',N'InDelivery');
            """,connection,tx))
            { Scope(document,actor,dispatchId);document.Parameters.AddWithValue("@DocumentId",request.DispatchSourceDocumentId);await using var reader=await document.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))throw new DispatchConflictException("The document is not available for this transporter.");sourceDocumentId=reader.GetGuid(0);total=reader.GetDecimal(1);credit=reader.GetDecimal(2); }

            var lineData=new Dictionary<int,(Guid ProductId,decimal Quantity,decimal Total)>();
            await using(var lines=new SqlCommand("SELECT LineNumber,ProductId,Quantity,LineTotal FROM dbo.SalesDocumentLines WHERE DocumentId=@DocumentId",connection,tx))
            { lines.Parameters.AddWithValue("@DocumentId",sourceDocumentId);await using var reader=await lines.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))lineData[reader.GetInt32(0)]=(reader.GetGuid(1),reader.GetDecimal(2),reader.GetDecimal(3)); }
            decimal returnTotal=0;
            foreach(var value in request.Returns)
            { if(!lineData.TryGetValue(value.OriginalLineNumber,out var line)||value.Quantity>line.Quantity)throw new DispatchValidationException("A returned quantity exceeds the original invoice line.");returnTotal+=decimal.Round(line.Total/line.Quantity*value.Quantity,2,MidpointRounding.AwayFromZero); }
            var due=Math.Max(0,total-returnTotal);var money=request.Payments.Where(x=>x.ApplicationType!=DeliveryPaymentApplications.CreditDocument).Sum(x=>x.Amount);
            var signed=request.Payments.Count(x=>x.ApplicationType==DeliveryPaymentApplications.CreditDocument);
            if(credit>0)
            { if(signed!=1||request.Payments.Any(x=>x.ApplicationType==DeliveryPaymentApplications.InvoicePayment)||money>due)throw new DispatchValidationException("A credit invoice requires its signed document and only permits an optional advance."); }
            else
            { if(signed>0||request.Payments.Any(x=>x.ApplicationType==DeliveryPaymentApplications.CreditAdvance))throw new DispatchValidationException("A cash invoice cannot be recorded as credit.");if(request.DeliveryStatus!=DeliveryStatuses.NotDelivered&&money!=due)throw new DispatchValidationException("Payments plus returned merchandise must balance the delivered invoice exactly.");if(money>due)throw new DispatchValidationException("Payments exceed the net delivered invoice."); }

            await using(var clear=new SqlCommand("DELETE dbo.DispatchDeliveryPayments WHERE DispatchSourceDocumentId=@DocumentId;DELETE dbo.DispatchDeliveryReturns WHERE DispatchSourceDocumentId=@DocumentId;DELETE dbo.DispatchDeliveryEvents WHERE DispatchSourceDocumentId=@DocumentId;",connection,tx))
            {clear.Parameters.AddWithValue("@DocumentId",request.DispatchSourceDocumentId);await clear.ExecuteNonQueryAsync(ct);}
            foreach(var payment in request.Payments)
            { await using var insert=new SqlCommand("INSERT dbo.DispatchDeliveryPayments(DispatchDeliveryPaymentId,BusinessId,DispatchId,DispatchSourceDocumentId,ApplicationType,PaymentMethod,Amount,Reference,EvidenceUrl,RecordedBy,OccurredAt,CreatedAt) VALUES(NEWID(),@BusinessId,@Id,@DocumentId,@Application,@Method,@Amount,@Reference,@Evidence,@UserId,@Occurred,SYSUTCDATETIME());",connection,tx);Scope(insert,actor,dispatchId);insert.Parameters.AddWithValue("@DocumentId",request.DispatchSourceDocumentId);insert.Parameters.AddWithValue("@Application",payment.ApplicationType);insert.Parameters.AddWithValue("@Method",(object?)payment.PaymentMethod??DBNull.Value);Money(insert,"@Amount",payment.Amount);insert.Parameters.AddWithValue("@Reference",(object?)payment.Reference?.Trim()??DBNull.Value);insert.Parameters.AddWithValue("@Evidence",(object?)payment.EvidenceUrl?.Trim()??DBNull.Value);insert.Parameters.AddWithValue("@Occurred",request.OccurredAt);await insert.ExecuteNonQueryAsync(ct); }
            foreach(var value in request.Returns)
            { var line=lineData[value.OriginalLineNumber];await using var insert=new SqlCommand("INSERT dbo.DispatchDeliveryReturns(DispatchDeliveryReturnId,BusinessId,DispatchId,DispatchSourceDocumentId,OriginalLineNumber,ProductId,Quantity,InventoryDisposition,ReasonCode,ReasonDescription,CreatedBy,CreatedAt) VALUES(NEWID(),@BusinessId,@Id,@DocumentId,@Line,@ProductId,@Quantity,@Disposition,@ReasonCode,@Reason,@UserId,SYSUTCDATETIME());",connection,tx);Scope(insert,actor,dispatchId);insert.Parameters.AddWithValue("@DocumentId",request.DispatchSourceDocumentId);insert.Parameters.AddWithValue("@Line",value.OriginalLineNumber);insert.Parameters.AddWithValue("@ProductId",line.ProductId);Quantity(insert,"@Quantity",value.Quantity);insert.Parameters.AddWithValue("@Disposition",value.InventoryDisposition);insert.Parameters.AddWithValue("@ReasonCode",value.ReasonCode.Trim());insert.Parameters.AddWithValue("@Reason",value.ReasonDescription.Trim());await insert.ExecuteNonQueryAsync(ct); }
            await using(var insert=new SqlCommand("""
              INSERT dbo.DispatchDeliveryEvents(DispatchDeliveryEventId,BusinessId,DispatchId,DispatchSourceDocumentId,DeliveryStatus,Reason,Notes,Latitude,Longitude,OccurredAt,RecordedBy,ReceivedAt,IdempotencyKey)
              VALUES(NEWID(),@BusinessId,@Id,@DocumentId,@Status,@Reason,@Notes,@Latitude,@Longitude,@Occurred,@UserId,SYSUTCDATETIME(),@Key);
              UPDATE dbo.DispatchSourceDocuments SET Status=@Status WHERE DispatchSourceDocumentId=@DocumentId;
              UPDATE dbo.Dispatches SET Status=N'InDelivery',UpdatedBy=@UserId,UpdatedAt=SYSUTCDATETIME() WHERE DispatchId=@Id;
            """,connection,tx))
            {Scope(insert,actor,dispatchId);insert.Parameters.AddWithValue("@DocumentId",request.DispatchSourceDocumentId);insert.Parameters.AddWithValue("@Status",request.DeliveryStatus);insert.Parameters.AddWithValue("@Reason",(object?)request.Reason??DBNull.Value);insert.Parameters.AddWithValue("@Notes",(object?)request.Notes??DBNull.Value);Coordinate(insert,"@Latitude",request.Latitude);Coordinate(insert,"@Longitude",request.Longitude);insert.Parameters.AddWithValue("@Occurred",request.OccurredAt);insert.Parameters.AddWithValue("@Key",request.IdempotencyKey);await insert.ExecuteNonQueryAsync(ct);}
            await tx.CommitAsync(ct);return (await GetAsync(actor,dispatchId,ct))!;
        }
        catch{await SafeRollbackAsync(tx,ct);throw;}
    }

    public async Task<DispatchExecutionDetail> ReorderAsync(DispatchActorIdentity actor, Guid dispatchId, ReorderDispatchDocumentsRequest request, byte[] rowVersion, CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);await using var tx=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try{await using(var validate=new SqlCommand("SELECT COUNT(*) FROM dbo.DispatchSourceDocuments source INNER JOIN dbo.Dispatches dispatch WITH(UPDLOCK,HOLDLOCK) ON dispatch.DispatchId=source.DispatchId WHERE source.DispatchId=@Id AND dispatch.DriverUserId=@UserId AND dispatch.BusinessId=@BusinessId AND dispatch.RowVersion=@Version AND dispatch.Status IN(N'Released',N'InDelivery')",connection,tx)){Scope(validate,actor,dispatchId);validate.Parameters.Add("@Version",SqlDbType.Timestamp).Value=rowVersion;if(Convert.ToInt32(await validate.ExecuteScalarAsync(ct))!=request.OrderedDocumentIds.Count)throw new DispatchConflictException("The dispatch changed or the order is incomplete.");}await using(var clear=new SqlCommand("DELETE dbo.DispatchDocumentSequences WHERE DispatchId=@Id",connection,tx)){clear.Parameters.AddWithValue("@Id",dispatchId);await clear.ExecuteNonQueryAsync(ct);}var sequence=0;foreach(var documentId in request.OrderedDocumentIds){await using var insert=new SqlCommand("IF NOT EXISTS(SELECT 1 FROM dbo.DispatchSourceDocuments WHERE DispatchId=@Id AND DispatchSourceDocumentId=@DocumentId) THROW 51000,'The order contains a foreign document.',1;INSERT dbo.DispatchDocumentSequences(DispatchId,DispatchSourceDocumentId,Sequence,UpdatedBy,UpdatedAt) VALUES(@Id,@DocumentId,@Sequence,@UserId,SYSUTCDATETIME());",connection,tx);insert.Parameters.AddWithValue("@Id",dispatchId);insert.Parameters.AddWithValue("@DocumentId",documentId);insert.Parameters.AddWithValue("@Sequence",++sequence);insert.Parameters.AddWithValue("@UserId",actor.UserId);await insert.ExecuteNonQueryAsync(ct);}await tx.CommitAsync(ct);return(await GetAsync(actor,dispatchId,ct))!;}catch{await SafeRollbackAsync(tx,ct);throw;}
    }

    public async Task<DispatchExecutionDetail> RecordExpenseAsync(DispatchActorIdentity actor, Guid dispatchId, DispatchExpenseInput request, CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);await using var command=new SqlCommand("""
          IF EXISTS(SELECT 1 FROM dbo.DispatchExpenses WHERE BusinessId=@BusinessId AND IdempotencyKey=@Key) RETURN;
          IF NOT EXISTS(SELECT 1 FROM dbo.Dispatches WHERE DispatchId=@Id AND BusinessId=@BusinessId AND DriverUserId=@UserId AND Status IN(N'Released',N'InDelivery')) THROW 51000,'The dispatch is not open for expenses.',1;
          INSERT dbo.DispatchExpenses(DispatchExpenseId,BusinessId,DispatchId,Category,Amount,Description,EvidenceUrl,ApprovalStatus,RecordedBy,OccurredAt,IdempotencyKey,CreatedAt)
          VALUES(NEWID(),@BusinessId,@Id,@Category,@Amount,@Description,@Evidence,N'Pending',@UserId,@Occurred,@Key,SYSUTCDATETIME());
        """,connection);Scope(command,actor,dispatchId);command.Parameters.AddWithValue("@Category",request.Category);Money(command,"@Amount",request.Amount);command.Parameters.AddWithValue("@Description",(object?)request.Description??DBNull.Value);command.Parameters.AddWithValue("@Evidence",(object?)request.EvidenceUrl??DBNull.Value);command.Parameters.AddWithValue("@Occurred",request.OccurredAt);command.Parameters.AddWithValue("@Key",request.IdempotencyKey);try{await command.ExecuteNonQueryAsync(ct);}catch(SqlException ex){throw new DispatchConflictException(ex.Message);}return(await GetAsync(actor,dispatchId,ct))!;
    }

    public async Task<DispatchExecutionDetail> ReviewExpenseAsync(DispatchActorIdentity actor, Guid dispatchId, Guid expenseId, ReviewDispatchExpenseRequest request, CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);await using var command=new SqlCommand("""
          SET XACT_ABORT ON;
          IF EXISTS(SELECT 1 FROM dbo.DispatchExpenses WHERE BusinessId=@BusinessId AND ReviewIdempotencyKey=@Key) RETURN;
          BEGIN TRAN;
          BEGIN TRY
            DECLARE @Requested decimal(19,4),@Current nvarchar(16);
            SELECT @Requested=Amount,@Current=ApprovalStatus FROM dbo.DispatchExpenses WITH(UPDLOCK,HOLDLOCK) WHERE DispatchExpenseId=@ExpenseId AND DispatchId=@Id AND BusinessId=@BusinessId;
            IF @Requested IS NULL THROW 51000,'The dispatch expense does not exist.',1;
            IF @Current<>N'Pending' THROW 51000,'The dispatch expense was already reviewed.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.Dispatches WHERE DispatchId=@Id AND BusinessId=@BusinessId AND Status=N'PendingSettlement') THROW 51000,'Expenses can only be reviewed during settlement.',1;
            IF @Decision=N'Approved' AND @Approved>@Requested THROW 51000,'The approved expense cannot exceed the requested amount.',1;
            UPDATE dbo.DispatchExpenses SET ApprovalStatus=@Decision,ApprovedAmount=CASE WHEN @Decision=N'Approved' THEN @Approved ELSE 0 END,ReviewedBy=@UserId,ReviewedAt=SYSUTCDATETIME(),ReviewNotes=@Notes,ReviewIdempotencyKey=@Key WHERE DispatchExpenseId=@ExpenseId;
            DECLARE @GrossCash decimal(19,4)=(SELECT COALESCE(SUM(Amount),0) FROM dbo.DispatchDeliveryPayments WHERE DispatchId=@Id AND PaymentMethod=N'Cash');
            DECLARE @ApprovedExpenses decimal(19,4)=(SELECT COALESCE(SUM(ApprovedAmount),0) FROM dbo.DispatchExpenses WHERE DispatchId=@Id AND ApprovalStatus=N'Approved');
            IF @ApprovedExpenses>@GrossCash THROW 51000,'Approved expenses cannot exceed cash collected.',1;
            UPDATE dbo.DispatchSettlements SET ExpectedCash=@GrossCash-@ApprovedExpenses WHERE DispatchId=@Id AND Status=N'PendingReview';
            COMMIT;
          END TRY
          BEGIN CATCH
            IF @@TRANCOUNT>0 ROLLBACK;
            THROW;
          END CATCH
        """,connection);Scope(command,actor,dispatchId);command.Parameters.AddWithValue("@ExpenseId",expenseId);command.Parameters.AddWithValue("@Decision",request.Decision);Money(command,"@Approved",request.ApprovedAmount??0);command.Parameters.AddWithValue("@Notes",(object?)request.Notes??DBNull.Value);command.Parameters.AddWithValue("@Key",request.IdempotencyKey);try{await command.ExecuteNonQueryAsync(ct);}catch(SqlException ex){throw new DispatchConflictException(ex.Message);}return(await GetAsync(actor,dispatchId,ct))!;
    }

    public async Task<DispatchExecutionDetail> CloseRouteAsync(DispatchActorIdentity actor, Guid dispatchId, CloseDispatchRouteRequest request, CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);await using var command=new SqlCommand("""
          SET XACT_ABORT ON;
          IF EXISTS(SELECT 1 FROM dbo.DispatchSettlements WHERE BusinessId=@BusinessId AND IdempotencyKey=@Key) RETURN;
          BEGIN TRAN;
          BEGIN TRY
            IF NOT EXISTS(SELECT 1 FROM dbo.Dispatches WITH(UPDLOCK,HOLDLOCK) WHERE DispatchId=@Id AND BusinessId=@BusinessId AND @Settle=1 AND Status=N'InDelivery') THROW 51000,'The dispatch is not ready to be received and closed.',1;
            IF EXISTS(SELECT 1 FROM dbo.DispatchSourceDocuments source WHERE source.DispatchId=@Id AND NOT EXISTS(SELECT 1 FROM dbo.DispatchDeliveryEvents delivery WHERE delivery.DispatchSourceDocumentId=source.DispatchSourceDocumentId)) THROW 51000,'Every invoice requires a delivery result.',1;
            DECLARE @Cash decimal(19,4)=(SELECT COALESCE(SUM(Amount),0) FROM dbo.DispatchDeliveryPayments WHERE DispatchId=@Id AND PaymentMethod=N'Cash');
            DECLARE @Deposit decimal(19,4)=(SELECT COALESCE(SUM(Amount),0) FROM dbo.DispatchDeliveryPayments WHERE DispatchId=@Id AND PaymentMethod=N'Deposit');
            DECLARE @Advance decimal(19,4)=(SELECT COALESCE(SUM(Amount),0) FROM dbo.DispatchDeliveryPayments WHERE DispatchId=@Id AND ApplicationType=N'CreditAdvance');
            DECLARE @Returns decimal(19,4)=(SELECT COALESCE(SUM(ROUND((line.LineTotal/NULLIF(line.Quantity,0))*returned.Quantity,2)),0) FROM dbo.DispatchDeliveryReturns returned INNER JOIN dbo.DispatchSourceDocuments source ON source.DispatchSourceDocumentId=returned.DispatchSourceDocumentId INNER JOIN dbo.SalesDocumentLines line ON line.DocumentId=source.SourceDocumentId AND line.LineNumber=returned.OriginalLineNumber WHERE returned.DispatchId=@Id);
            DECLARE @CreditGross decimal(19,4)=(SELECT COALESCE(SUM(source.DocumentTotalSnapshot),0) FROM dbo.DispatchSourceDocuments source WHERE source.DispatchId=@Id AND EXISTS(SELECT 1 FROM dbo.DispatchDeliveryPayments payment WHERE payment.DispatchSourceDocumentId=source.DispatchSourceDocumentId AND payment.ApplicationType=N'CreditDocument'));
            DECLARE @CreditReturns decimal(19,4)=(SELECT COALESCE(SUM(ROUND((line.LineTotal/NULLIF(line.Quantity,0))*returned.Quantity,2)),0) FROM dbo.DispatchDeliveryReturns returned INNER JOIN dbo.DispatchSourceDocuments source ON source.DispatchSourceDocumentId=returned.DispatchSourceDocumentId INNER JOIN dbo.SalesDocumentLines line ON line.DocumentId=source.SourceDocumentId AND line.LineNumber=returned.OriginalLineNumber WHERE returned.DispatchId=@Id AND EXISTS(SELECT 1 FROM dbo.DispatchDeliveryPayments payment WHERE payment.DispatchSourceDocumentId=source.DispatchSourceDocumentId AND payment.ApplicationType=N'CreditDocument'));
            DECLARE @RemainingCredit decimal(19,4)=@CreditGross-@CreditReturns-@Advance;
            DECLARE @Undelivered decimal(19,4)=(SELECT COALESCE(SUM(source.DocumentTotalSnapshot),0) FROM dbo.DispatchSourceDocuments source INNER JOIN dbo.DispatchDeliveryEvents delivery ON delivery.DispatchSourceDocumentId=source.DispatchSourceDocumentId WHERE source.DispatchId=@Id AND delivery.DeliveryStatus=N'NotDelivered');
            DECLARE @DispatchTotal decimal(19,4)=(SELECT COALESCE(SUM(DocumentTotalSnapshot),0) FROM dbo.DispatchSourceDocuments WHERE DispatchId=@Id);
            DECLARE @Balance decimal(19,4)=@DispatchTotal-(@Cash+@Deposit+@RemainingCredit+@Returns+@Undelivered);
            IF @RemainingCredit<0 THROW 51000,'Credit advances exceed the net credit balance.',1;
            IF @Balance<>0 THROW 51000,'The dispatch does not balance exactly. Review payments, credit, returns and undelivered invoices.',1;
            IF @Cash<>@Declared AND @Reason IS NULL THROW 51000,'A cash difference requires its explanation.',1;
            INSERT dbo.DispatchSettlements(DispatchSettlementId,BusinessId,DispatchId,ExpectedCash,DeclaredCash,DepositTotal,CreditDocumentTotal,CreditAdvanceTotal,ReturnTotal,DifferenceReason,TransporterClosedBy,TransporterClosedAt,Status,IdempotencyKey)
            VALUES(NEWID(),@BusinessId,@Id,@Cash,@Declared,@Deposit,@RemainingCredit,@Advance,@Returns,@Reason,@UserId,SYSUTCDATETIME(),N'PendingReview',@Key);
            UPDATE dbo.Dispatches SET Status=N'PendingSettlement',UpdatedBy=@UserId,UpdatedAt=SYSUTCDATETIME() WHERE DispatchId=@Id;
            COMMIT;
          END TRY
          BEGIN CATCH
            IF @@TRANCOUNT>0 ROLLBACK;
            THROW;
          END CATCH
        """,connection);Scope(command,actor,dispatchId);Money(command,"@Declared",request.DeclaredCash);command.Parameters.AddWithValue("@Reason",(object?)request.DifferenceReason??DBNull.Value);command.Parameters.AddWithValue("@Key",request.IdempotencyKey);try{await command.ExecuteNonQueryAsync(ct);}catch(SqlException ex){throw new DispatchConflictException(ex.Message);}return(await GetAsync(actor,dispatchId,ct))!;
    }

    public async Task<DispatchExecutionDetail> SettleAsync(DispatchActorIdentity actor, Guid dispatchId, SettleDispatchRequest request, CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);await using var command=new SqlCommand("""
          SET XACT_ABORT ON;
          IF EXISTS(SELECT 1 FROM dbo.DispatchSettlementOperations WHERE BusinessId=@BusinessId AND IdempotencyKey=@Key) RETURN;
          BEGIN TRAN;
          BEGIN TRY
            IF NOT EXISTS(SELECT 1 FROM dbo.DispatchSettlements WITH(UPDLOCK,HOLDLOCK) WHERE DispatchId=@Id AND BusinessId=@BusinessId AND Status=N'PendingReview') THROW 51000,'The dispatch is not pending settlement.',1;
            IF EXISTS(SELECT 1 FROM dbo.DispatchExpenses WHERE DispatchId=@Id AND ApprovalStatus=N'Pending') THROW 51000,'Every dispatch expense must be approved or rejected before settlement.',1;
            DECLARE @GrossCash decimal(19,4)=(SELECT COALESCE(SUM(Amount),0) FROM dbo.DispatchDeliveryPayments WHERE DispatchId=@Id AND PaymentMethod=N'Cash');
            DECLARE @Deposit decimal(19,4)=(SELECT COALESCE(SUM(Amount),0) FROM dbo.DispatchDeliveryPayments WHERE DispatchId=@Id AND PaymentMethod=N'Deposit');
            DECLARE @Advance decimal(19,4)=(SELECT COALESCE(SUM(Amount),0) FROM dbo.DispatchDeliveryPayments WHERE DispatchId=@Id AND ApplicationType=N'CreditAdvance');
            DECLARE @Returns decimal(19,4)=(SELECT COALESCE(SUM(ROUND((line.LineTotal/NULLIF(line.Quantity,0))*returned.Quantity,2)),0) FROM dbo.DispatchDeliveryReturns returned INNER JOIN dbo.DispatchSourceDocuments source ON source.DispatchSourceDocumentId=returned.DispatchSourceDocumentId INNER JOIN dbo.SalesDocumentLines line ON line.DocumentId=source.SourceDocumentId AND line.LineNumber=returned.OriginalLineNumber WHERE returned.DispatchId=@Id);
            DECLARE @CreditGross decimal(19,4)=(SELECT COALESCE(SUM(source.DocumentTotalSnapshot),0) FROM dbo.DispatchSourceDocuments source WHERE source.DispatchId=@Id AND EXISTS(SELECT 1 FROM dbo.DispatchDeliveryPayments payment WHERE payment.DispatchSourceDocumentId=source.DispatchSourceDocumentId AND payment.ApplicationType=N'CreditDocument'));
            DECLARE @CreditReturns decimal(19,4)=(SELECT COALESCE(SUM(ROUND((line.LineTotal/NULLIF(line.Quantity,0))*returned.Quantity,2)),0) FROM dbo.DispatchDeliveryReturns returned INNER JOIN dbo.DispatchSourceDocuments source ON source.DispatchSourceDocumentId=returned.DispatchSourceDocumentId INNER JOIN dbo.SalesDocumentLines line ON line.DocumentId=source.SourceDocumentId AND line.LineNumber=returned.OriginalLineNumber WHERE returned.DispatchId=@Id AND EXISTS(SELECT 1 FROM dbo.DispatchDeliveryPayments payment WHERE payment.DispatchSourceDocumentId=source.DispatchSourceDocumentId AND payment.ApplicationType=N'CreditDocument'));
            DECLARE @RemainingCredit decimal(19,4)=@CreditGross-@CreditReturns-@Advance;
            DECLARE @Undelivered decimal(19,4)=(SELECT COALESCE(SUM(source.DocumentTotalSnapshot),0) FROM dbo.DispatchSourceDocuments source INNER JOIN dbo.DispatchDeliveryEvents delivery ON delivery.DispatchSourceDocumentId=source.DispatchSourceDocumentId WHERE source.DispatchId=@Id AND delivery.DeliveryStatus=N'NotDelivered');
            DECLARE @DispatchTotal decimal(19,4)=(SELECT COALESCE(SUM(DocumentTotalSnapshot),0) FROM dbo.DispatchSourceDocuments WHERE DispatchId=@Id);
            DECLARE @Balance decimal(19,4)=@DispatchTotal-(@GrossCash+@Deposit+@RemainingCredit+@Returns+@Undelivered);
            DECLARE @ApprovedExpenses decimal(19,4)=(SELECT COALESCE(SUM(ApprovedAmount),0) FROM dbo.DispatchExpenses WHERE DispatchId=@Id AND ApprovalStatus=N'Approved');
            DECLARE @Expected decimal(19,4)=@GrossCash-@ApprovedExpenses;
            IF @RemainingCredit<0 OR @Balance<>0 THROW 51000,'The commercial dispatch balance must be exactly zero before settlement.',1;
            IF @Expected<0 THROW 51000,'Approved expenses exceed cash collected.',1;
            IF @Cash<>@Expected AND NULLIF(LTRIM(RTRIM(@Notes)),N'') IS NULL THROW 51000,'A cash surplus or shortage requires a supervisor explanation.',1;
            UPDATE dbo.DispatchSettlements SET ExpectedCash=@Expected,DepositTotal=@Deposit,CreditDocumentTotal=@RemainingCredit,CreditAdvanceTotal=@Advance,ReturnTotal=@Returns,CashReceived=@Cash,ReceivedBy=@UserId,ReceivedAt=SYSUTCDATETIME(),Notes=@Notes,Status=N'Processing' WHERE DispatchId=@Id;
            INSERT dbo.DispatchSettlementOperations(DispatchSettlementOperationId,BusinessId,DispatchId,CashReceived,Notes,RequestedBy,RequestedAt,Status,Attempts,NextAttemptAt,IdempotencyKey)
            VALUES(NEWID(),@BusinessId,@Id,@Cash,@Notes,@UserId,SYSUTCDATETIME(),N'Pending',0,SYSUTCDATETIME(),@Key);
            UPDATE dbo.Dispatches SET Status=N'SettlementProcessing',UpdatedBy=@UserId,UpdatedAt=SYSUTCDATETIME() WHERE DispatchId=@Id;
            COMMIT;
          END TRY
          BEGIN CATCH
            IF @@TRANCOUNT>0 ROLLBACK;
            THROW;
          END CATCH
        """,connection);Scope(command,actor,dispatchId);Money(command,"@Cash",request.CashReceived);command.Parameters.AddWithValue("@Notes",(object?)request.Notes??DBNull.Value);command.Parameters.AddWithValue("@Key",request.IdempotencyKey);try{await command.ExecuteNonQueryAsync(ct);}catch(SqlException ex){throw new DispatchConflictException(ex.Message);}return(await GetAsync(actor,dispatchId,ct))!;
    }

    private static void Scope(SqlCommand command,DispatchActorIdentity actor,Guid id){command.Parameters.AddWithValue("@TenantId",actor.TenantId);command.Parameters.AddWithValue("@BusinessId",actor.BusinessId);command.Parameters.AddWithValue("@UserId",actor.UserId);command.Parameters.AddWithValue("@ReadAll",actor.Permissions.Contains(DispatchPermissionCodes.ReadAll));command.Parameters.AddWithValue("@Settle",actor.Permissions.Contains(DispatchPermissionCodes.Settle));command.Parameters.AddWithValue("@Id",id);}
    private static void Money(SqlCommand command,string name,decimal value){var parameter=command.Parameters.Add(name,SqlDbType.Decimal);parameter.Precision=19;parameter.Scale=4;parameter.Value=value;}
    private static void Quantity(SqlCommand command,string name,decimal value){var parameter=command.Parameters.Add(name,SqlDbType.Decimal);parameter.Precision=19;parameter.Scale=6;parameter.Value=value;}
    private static void Coordinate(SqlCommand command,string name,decimal? value){var parameter=command.Parameters.Add(name,SqlDbType.Decimal);parameter.Precision=9;parameter.Scale=6;parameter.Value=(object?)value??DBNull.Value;}
    private static string? NullableString(SqlDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetString(ordinal);
    private static Guid? NullableGuid(SqlDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetGuid(ordinal);
    private static decimal? NullableDecimal(SqlDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetDecimal(ordinal);
    private static async Task SafeRollbackAsync(SqlTransaction transaction,CancellationToken ct){try{await transaction.RollbackAsync(ct);}catch(InvalidOperationException){}}
}
