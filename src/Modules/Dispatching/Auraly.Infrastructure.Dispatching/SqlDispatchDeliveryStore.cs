using System.Data;
using Auraly.Domain.Returns;
using System.Text.Json;
using System.Security.Cryptography;
using Auraly.Application.Dispatching;
using Auraly.Contracts.Dispatching;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Dispatching;

public sealed partial class SqlDispatchDeliveryStore(DispatchingSqlConnectionFactory connections) : IDispatchDeliveryStore
{
    public async Task<IReadOnlyList<DispatchReasonOption>> ReasonsAsync(DispatchActorIdentity actor, string reasonType, CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);
        await using var command=new SqlCommand("SELECT ReasonId,ReasonType,Code,Name FROM dbo.BusinessReasons WHERE BusinessId=@BusinessId AND ReasonType=@Type AND IsActive=1 ORDER BY DisplayOrder,Name",connection);
        command.Parameters.AddWithValue("@BusinessId",actor.BusinessId);command.Parameters.AddWithValue("@Type",reasonType);
        var result=new List<DispatchReasonOption>();await using var reader=await command.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))result.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3)));
        return result;
    }

    public async Task<DispatchExecutionDetail?> GetAsync(DispatchActorIdentity actor, Guid dispatchId, CancellationToken ct)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(ct);
        await using var header=new SqlCommand("""
          SELECT DispatchId,DispatchNumber,ScheduledDate,DriverName,VehiclePlate,Status,RowVersion
          FROM dbo.Dispatches WHERE DispatchId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId
            AND (@ReadAll=1 OR @Settle=1 OR DriverUserId=@UserId);
        """,connection); Scope(header,actor,dispatchId);
        Guid id;string number,driver,status,rowVersion;DateOnly date;string? plate;
        await using(var reader=await header.ExecuteReaderAsync(ct))
        { if(!await reader.ReadAsync(ct))return null;id=reader.GetGuid(0);number=reader.GetString(1);date=DateOnly.FromDateTime(reader.GetDateTime(2));driver=reader.GetString(3);plate=NullableString(reader,4);status=reader.GetString(5);rowVersion=Convert.ToBase64String((byte[])reader[6]); }

        var payments=new Dictionary<Guid,List<DispatchDeliveryPaymentDetail>>();
        await using(var command=new SqlCommand($"SELECT DispatchSourceDocumentId,{PaymentColumns} FROM dispatch.EffectivePayments(@Id) ORDER BY PaymentId",connection))
        { command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct)){var documentId=reader.GetGuid(0);if(!payments.TryGetValue(documentId,out var values))payments[documentId]=values=[];values.Add(ReadPayment(reader,1));} }

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
        await using(var command=new SqlCommand("""
          SELECT line.DispatchSourceDocumentId,line.SourceLineNumber,line.ProductId,line.ProductCodeSnapshot,line.DescriptionSnapshot,
            original.Quantity,line.UnitPriceSnapshot,original.LineTotal,COALESCE(prior.Quantity,0),COALESCE(prior.Total,0)
          FROM dbo.DispatchLines line JOIN dbo.DispatchSourceDocuments source ON source.DispatchSourceDocumentId=line.DispatchSourceDocumentId
          JOIN dbo.SalesDocumentLines original ON original.DocumentId=source.SourceDocumentId AND original.LineNumber=line.SourceLineNumber
          OUTER APPLY(SELECT SUM(returned.Quantity) Quantity,SUM(returned.LineTotal) Total FROM dbo.SalesReturnLines returned
            WHERE returned.OriginalDocumentId=source.SourceDocumentId AND returned.OriginalLineNumber=line.SourceLineNumber) prior
          WHERE line.DispatchId=@Id ORDER BY line.SourceLineNumber;
          """,connection))
        { command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct)){var documentId=reader.GetGuid(0);if(!lines.TryGetValue(documentId,out var values))lines[documentId]=values=[];values.Add(new(reader.GetInt32(1),reader.GetGuid(2),reader.GetString(3),reader.GetString(4),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetDecimal(7),reader.GetDecimal(8),reader.GetDecimal(9)));} }

        var documents=new List<DispatchDeliveryDocument>();
        var linkedReturns=new List<DispatchLinkedReturn>();
        await using(var command=new SqlCommand("""
          SELECT source.DispatchSourceDocumentId,source.SourceDocumentId,source.SourceDocumentType,source.DocumentNumberSnapshot,
            COALESCE(custom.Sequence,routeStop.Sequence,ROW_NUMBER() OVER(ORDER BY source.CustomerNameSnapshot,source.DocumentNumberSnapshot)),
            source.CustomerId,source.CustomerNameSnapshot,source.DeliveryAddressSnapshot,source.DocumentTotalSnapshot,source.CreditAmountSnapshot,
            destination.Latitude,destination.Longitude,COALESCE(delivery.DeliveryStatus,N'Pending'),delivery.Reason,delivery.Notes,delivery.Latitude,delivery.Longitude,delivery.OccurredAt,
            COALESCE(delivery.ReturnTotal,0),sale.UntaxedAmount+sale.TaxAmount,sale.PayableAmount-sale.UntaxedAmount-sale.TaxAmount,
            sale.UntaxedAmount+sale.TaxAmount-COALESCE(prior.Unrounded,0),sale.PayableAmount-sale.UntaxedAmount-sale.TaxAmount-COALESCE(prior.Rounding,0),sale.CollectionMode
          FROM dbo.DispatchSourceDocuments source INNER JOIN dbo.Dispatches dispatch ON dispatch.DispatchId=source.DispatchId
          INNER JOIN dbo.SalesDocuments sale ON sale.DocumentId=source.SourceDocumentId
          LEFT JOIN dbo.DispatchDocumentSequences custom ON custom.DispatchId=source.DispatchId AND custom.DispatchSourceDocumentId=source.DispatchSourceDocumentId
          OUTER APPLY(SELECT TOP(1) stop.Sequence,stop.PartySiteId FROM dbo.SalesRouteStops stop LEFT JOIN dbo.PartySites stopSite ON stopSite.PartySiteId=stop.PartySiteId WHERE stop.RouteId=dispatch.RouteId AND stop.CustomerId=source.CustomerId AND stop.IsActive=1 ORDER BY CASE WHEN stopSite.AddressLine=source.DeliveryAddressSnapshot THEN 0 ELSE 1 END,stop.Sequence,stop.RouteStopId) routeStop
          OUTER APPLY(SELECT TOP(1) site.Latitude,site.Longitude FROM dbo.Customers customer INNER JOIN dbo.PartySites site ON site.PartyId=customer.PartyId AND site.IsActive=1 WHERE customer.CustomerId=source.CustomerId ORDER BY CASE WHEN site.PartySiteId=routeStop.PartySiteId THEN 0 WHEN site.IsPrimary=1 THEN 1 ELSE 2 END,site.CreatedAt) destination
          LEFT JOIN dbo.DispatchDeliveryEvents delivery ON delivery.DispatchSourceDocumentId=source.DispatchSourceDocumentId AND delivery.IsCurrent=1
          OUTER APPLY(SELECT SUM(returned.UntaxedAmount+returned.TaxAmount) Unrounded,
            SUM(returned.TotalAmount-returned.UntaxedAmount-returned.TaxAmount) Rounding
            FROM dbo.SalesReturns returned WHERE returned.OriginalDocumentId=source.SourceDocumentId) prior
          WHERE source.DispatchId=@Id ORDER BY COALESCE(custom.Sequence,routeStop.Sequence,2147483647),source.CustomerNameSnapshot,source.DocumentNumberSnapshot;
          SELECT returned.ReturnId,returned.OriginalDocumentId,returned.DocumentNumber,returned.TotalAmount,returned.EconomicResolution
          FROM dbo.DispatchSettlementOperations operation
          CROSS APPLY OPENJSON(operation.ReturnIdsJson) WITH(ReturnId uniqueidentifier '$') selected
          JOIN dbo.SalesReturns returned ON returned.ReturnId=selected.ReturnId AND returned.BusinessId=operation.BusinessId
          WHERE operation.DispatchId=@Id ORDER BY returned.DocumentNumber,returned.ReturnId;
        """,connection))
        { command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct)){var documentId=reader.GetGuid(0);documents.Add(new(documentId,reader.GetGuid(1),reader.GetString(2),reader.GetString(3),Convert.ToInt32(reader.GetValue(4)),NullableGuid(reader,5),reader.GetString(6),NullableString(reader,7),reader.GetDecimal(8),reader.GetDecimal(9),NullableDecimal(reader,10),NullableDecimal(reader,11),reader.GetString(12),NullableString(reader,13),NullableString(reader,14),NullableDecimal(reader,15),NullableDecimal(reader,16),reader.IsDBNull(17)?null:reader.GetDateTimeOffset(17),lines.GetValueOrDefault(documentId)??[],payments.GetValueOrDefault(documentId)??[],returns.GetValueOrDefault(documentId)??[],reader.GetDecimal(18),new(reader.GetDecimal(19),reader.GetDecimal(20),reader.GetDecimal(21),reader.GetDecimal(22)),reader.GetString(23)));}
          await reader.NextResultAsync(ct);
          while(await reader.ReadAsync(ct)) linkedReturns.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetDecimal(3),reader.GetString(4)));
        }

        var expenses=new List<DispatchExpenseDetail>();
        await using(var command=new SqlCommand("SELECT report.DispatchExpenseId,report.Category,report.Amount,report.Description,report.EvidenceUrl,report.ApprovalStatus,report.ApprovedAmount,report.ExpenseConceptId,report.AcceptedExpenseId,report.OccurredAt,expense.Status FROM dbo.DispatchExpenses report LEFT JOIN dbo.Expenses expense ON expense.ExpenseId=report.AcceptedExpenseId AND expense.BusinessId=report.BusinessId WHERE report.DispatchId=@Id ORDER BY report.OccurredAt,report.DispatchExpenseId",connection))
        { command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))expenses.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetDecimal(2),NullableString(reader,3),NullableString(reader,4),reader.GetString(5),NullableDecimal(reader,6),NullableGuid(reader,7),NullableGuid(reader,8),reader.GetDateTimeOffset(9),NullableString(reader,10))); }
        var dispatchTotal=documents.Sum(document=>document.DocumentTotal);
        var grossCash=payments.Values.SelectMany(value=>value).Where(value=>value.PaymentMethod=="Cash").Sum(value=>value.Amount);
        var approvedExpenses=expenses.Where(value=>value.ApprovalStatus=="Approved").Sum(value=>value.ApprovedAmount??0);

        DispatchSettlementSummary? settlement=null;
        await using(var command=new SqlCommand("""
          SELECT ExpectedCash,DeclaredCash,CashDifference,DepositTotal,CreditDocumentTotal,CreditAdvanceTotal,ReturnTotal,Status,ReceivedBy,ReceivedAt,CashReceived,DeclaredCard,DeclaredTransfer,TransporterClosedBy,TransporterClosedAt,(SELECT PreviouslyCollectedTotal FROM dispatch.PaymentTotals(@Id))
          FROM dispatch.Closures WHERE DispatchId=@Id;
        """,connection))
        { command.Parameters.AddWithValue("@Id",id);await using var reader=await command.ExecuteReaderAsync(ct);if(await reader.ReadAsync(ct)){var expected=reader.GetDecimal(0);var deposits=reader.GetDecimal(3);var remainingCredit=reader.GetDecimal(4);var advances=reader.GetDecimal(5);var returnTotal=reader.GetDecimal(6);var undelivered=documents.Where(value=>value.DeliveryStatus==DeliveryStatuses.NotDelivered).Sum(value=>value.DocumentTotal);var cards=payments.Values.SelectMany(value=>value).Where(value=>value.PaymentMethod is "DebitCard" or "CreditCard").Sum(value=>value.Amount);var balance=dispatchTotal-(grossCash+deposits+cards+remainingCredit+returnTotal+undelivered+reader.GetDecimal(15));settlement=new(grossCash,approvedExpenses,expected,reader.GetDecimal(1),reader.GetDecimal(2),deposits,remainingCredit,advances,returnTotal,undelivered,dispatchTotal,balance,reader.GetString(7),NullableGuid(reader,8),reader.IsDBNull(9)?null:reader.GetDateTimeOffset(9),NullableDecimal(reader,10),cards,NullableDecimal(reader,11),NullableDecimal(reader,12),NullableGuid(reader,13),reader.GetDateTimeOffset(14),reader.GetDecimal(15)); } }
        return new(id,number,date,driver,plate,status,documents,expenses,settlement,rowVersion,linkedReturns);
    }

    public async Task<DispatchExecutionDetail> RecordAsync(DispatchActorIdentity actor, Guid dispatchId, RecordDispatchDeliveryRequest request, CancellationToken ct)
    {
        var requestHash=SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request));
        await using var connection=connections.Create();await connection.OpenAsync(ct);await using var tx=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            await using(var replay=new SqlCommand("SELECT DispatchId,DispatchSourceDocumentId,PayloadHash FROM dbo.DispatchDeliveryEvents WHERE BusinessId=@BusinessId AND IdempotencyKey=@Key",connection,tx))
            {
                replay.Parameters.AddWithValue("@BusinessId",actor.BusinessId);replay.Parameters.AddWithValue("@Key",request.IdempotencyKey);
                await using var reader=await replay.ExecuteReaderAsync(ct);
                if(await reader.ReadAsync(ct))
                {
                    if(reader.GetGuid(0)!=dispatchId||reader.GetGuid(1)!=request.DispatchSourceDocumentId
                        ||!reader.IsDBNull(2)&&!requestHash.SequenceEqual((byte[])reader.GetValue(2)))
                        throw new DispatchConflictException("La solicitud ya se utilizó con otro resultado de entrega.");
                    await reader.CloseAsync();await tx.CommitAsync(ct);return(await GetAsync(actor,dispatchId,ct))!;
                }
            }
            Guid sourceDocumentId;decimal total,credit,originalUnrounded,originalRounding,returnedUnrounded,returnedRounding;string collectionMode;
            await using(var document=new SqlCommand("""
              SELECT source.SourceDocumentId,source.DocumentTotalSnapshot,source.CreditAmountSnapshot,sale.CollectionMode,
                sale.UntaxedAmount+sale.TaxAmount,sale.PayableAmount-sale.UntaxedAmount-sale.TaxAmount,
                COALESCE(prior.Unrounded,0),COALESCE(prior.Rounding,0)
              FROM dbo.DispatchSourceDocuments source INNER JOIN dbo.Dispatches dispatch WITH(UPDLOCK,HOLDLOCK) ON dispatch.DispatchId=source.DispatchId
              INNER JOIN dbo.SalesDocuments sale WITH(UPDLOCK,HOLDLOCK) ON sale.DocumentId=source.SourceDocumentId
              OUTER APPLY(SELECT SUM(returned.UntaxedAmount+returned.TaxAmount) Unrounded,
                SUM(returned.TotalAmount-returned.UntaxedAmount-returned.TaxAmount) Rounding
                FROM dbo.SalesReturns returned WHERE returned.OriginalDocumentId=sale.DocumentId) prior
              WHERE source.DispatchSourceDocumentId=@DocumentId AND source.DispatchId=@Id AND dispatch.TenantId=@TenantId AND dispatch.BusinessId=@BusinessId
                AND (@IsAdministrator=1 OR dispatch.DriverUserId=@UserId)
                AND dispatch.Status IN(N'Released',N'InDelivery',N'PendingSettlement')
                AND NOT EXISTS(SELECT 1 FROM dispatch.Closures WHERE DispatchId=dispatch.DispatchId);
            """,connection,tx))
            { Scope(document,actor,dispatchId);document.Parameters.AddWithValue("@DocumentId",request.DispatchSourceDocumentId);await using var reader=await document.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))throw new DispatchConflictException("El documento no está disponible para este transportador.");sourceDocumentId=reader.GetGuid(0);total=reader.GetDecimal(1);credit=reader.GetDecimal(2);collectionMode=reader.GetString(3);originalUnrounded=reader.GetDecimal(4);originalRounding=reader.GetDecimal(5);returnedUnrounded=reader.GetDecimal(6);returnedRounding=reader.GetDecimal(7); }

            var lineData=new Dictionary<int,DispatchReturnLine>();
            await using(var lines=new SqlCommand("""
              IF EXISTS(SELECT 1 FROM OPENJSON(@ReturnInputs) WITH(OriginalLineNumber int,Quantity decimal(19,6),
                  InventoryDisposition nvarchar(24),ReasonCode nvarchar(32),ReasonDescription nvarchar(300)) requested
                WHERE NOT EXISTS(SELECT 1 FROM dbo.BusinessReasons reason WHERE reason.BusinessId=@BusinessId
                  AND reason.ReasonType=N'DeliveryReturn' AND reason.Code=requested.ReasonCode AND reason.IsActive=1)
                AND NOT EXISTS(SELECT 1 FROM dbo.DispatchDeliveryReturns saved WHERE saved.DispatchSourceDocumentId=@SourceId
                  AND saved.OriginalLineNumber=requested.OriginalLineNumber AND saved.ReasonCode=requested.ReasonCode
                  AND saved.ReasonDescription=requested.ReasonDescription AND saved.Quantity=requested.Quantity
                  AND saved.InventoryDisposition=requested.InventoryDisposition))
                THROW 51000,N'Selecciona un motivo activo para la devolución de entrega.',1;
              SELECT line.LineNumber,line.Quantity,line.DiscountAmount,line.UntaxedAmount,line.TaxAmount,line.LineTotal,
                COALESCE(prior.Quantity,0),COALESCE(prior.DiscountAmount,0),COALESCE(prior.UntaxedAmount,0),
                COALESCE(prior.TaxAmount,0),COALESCE(prior.LineTotal,0)
              FROM dbo.SalesDocumentLines line
              OUTER APPLY(SELECT SUM(returned.Quantity) Quantity,SUM(returned.DiscountAmount) DiscountAmount,
                SUM(returned.UntaxedAmount) UntaxedAmount,SUM(returned.TaxAmount) TaxAmount,SUM(returned.LineTotal) LineTotal
                FROM dbo.SalesReturnLines returned WHERE returned.OriginalDocumentId=line.DocumentId AND returned.OriginalLineNumber=line.LineNumber) prior
              WHERE line.DocumentId=@DocumentId;
              """,connection,tx))
            { lines.Parameters.AddWithValue("@DocumentId",sourceDocumentId);lines.Parameters.AddWithValue("@BusinessId",actor.BusinessId);lines.Parameters.AddWithValue("@SourceId",request.DispatchSourceDocumentId);lines.Parameters.Add("@ReturnInputs",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(request.Returns);await using var reader=await lines.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))lineData.Add(reader.GetInt32(0),new(reader.GetDecimal(1),reader.GetDecimal(2),reader.GetDecimal(3),reader.GetDecimal(4),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetDecimal(7),reader.GetDecimal(8),reader.GetDecimal(9),reader.GetDecimal(10))); }
            decimal returnTotal=0;
            try
            {
                foreach(var value in request.Returns)
                {
                    if(!lineData.TryGetValue(value.OriginalLineNumber,out var line))
                        throw new DispatchValidationException("La línea devuelta no pertenece a la factura.");
                    returnTotal+=SalesReturnAmountCalculator.Calculate(line.Quantity,line.Discount,line.Untaxed,line.Tax,line.Total,
                        line.ReturnedQuantity,line.ReturnedDiscount,line.ReturnedUntaxed,line.ReturnedTax,line.ReturnedTotal,value.Quantity).LineTotal;
                }
                if(returnTotal>0) returnTotal+=SalesReturnAmountCalculator.AllocatePaymentRounding(originalUnrounded,
                    originalRounding,returnedUnrounded,returnedRounding,returnTotal);
            }
            catch(ArgumentException error){throw new DispatchValidationException(error.Message);}
            var due=Math.Max(0,total-returnTotal);var money=request.Payments.Where(x=>x.ApplicationType!=DeliveryPaymentApplications.CreditDocument).Sum(x=>x.Amount);
            var signed=request.Payments.Count(x=>x.ApplicationType==DeliveryPaymentApplications.CreditDocument);
            if(credit>0)
            {
                var uncollectedPaidPortion=collectionMode=="Dispatch"?decimal.Min(total-credit,due):0m;
                if(request.Payments.Any(value=>value.ApplicationType==DeliveryPaymentApplications.CreditAdvance) ||
                    request.DeliveryStatus!=DeliveryStatuses.NotDelivered && (signed!=1 || money!=uncollectedPaidPortion))
                    throw new DispatchValidationException("La venta conserva su condición de crédito. Confirma su soporte y recauda únicamente la porción de contado pendiente del pedido. La deuda se paga desde cartera o recibo de caja.");
            }
            else if(collectionMode=="Session")
            {
                if(signed>0 || money!=0) throw new DispatchValidationException("La venta ya fue pagada en su sesión original. Confirma la entrega sin registrar otro recaudo.");
            }
            else
            { if(signed>0||request.Payments.Any(x=>x.ApplicationType==DeliveryPaymentApplications.CreditAdvance))throw new DispatchValidationException("Una factura de contado no puede registrarse como una venta a crédito.");if(request.DeliveryStatus!=DeliveryStatuses.NotDelivered&&money!=due)throw new DispatchValidationException("Los pagos y la mercancía devuelta deben cubrir exactamente el valor de la factura entregada.");if(money>due)throw new DispatchValidationException("Los pagos superan el valor neto de la factura entregada."); }

            if(collectionMode=="Dispatch" && request.Payments.Any(value=>value.ApplicationType!=DeliveryPaymentApplications.CreditDocument && value.PaymentMethodId is null))
                throw new DispatchValidationException("Selecciona la forma de pago configurada para cada recaudo del pedido.");

            await using(var clear=new SqlCommand("DELETE dbo.DispatchDeliveryPayments WHERE DispatchSourceDocumentId=@DocumentId;DELETE dbo.DispatchDeliveryReturns WHERE DispatchSourceDocumentId=@DocumentId;UPDATE dbo.DispatchDeliveryEvents SET IsCurrent=0 WHERE DispatchSourceDocumentId=@DocumentId AND IsCurrent=1;",connection,tx))
            {clear.Parameters.AddWithValue("@DocumentId",request.DispatchSourceDocumentId);await clear.ExecuteNonQueryAsync(ct);}
            await using(var insert=new SqlCommand("""
              IF EXISTS(SELECT 1 FROM OPENJSON(@Payments) WITH(PaymentMethodId uniqueidentifier,PaymentMethodVersion int,
                PaymentMethod nvarchar(24),CardFranchiseCode nvarchar(40)) input
                LEFT JOIN reference.PaymentMethodDetails form ON form.PaymentMethodId=input.PaymentMethodId AND form.TenantId=@TenantId
                WHERE input.PaymentMethodId IS NOT NULL AND (form.PaymentMethodId IS NULL OR form.Version<>input.PaymentMethodVersion
                  OR form.MethodCode<>input.PaymentMethod OR form.IsActive=0 OR form.IsReady=0 OR form.UseForReceipts=0))
                THROW 51000,N'Una forma de pago cambió o no está habilitada para recaudos.',1;
              IF EXISTS(SELECT 1 FROM OPENJSON(@Payments) WITH(PaymentMethod nvarchar(24),CardFranchiseCode nvarchar(40)) input
                WHERE input.PaymentMethod IN(N'DebitCard',N'CreditCard') AND NOT EXISTS(
                  SELECT 1 FROM reference.Options WHERE CatalogCode=N'card-franchise' AND Code=input.CardFranchiseCode AND IsActive=1))
                THROW 51000,N'La franquicia de la tarjeta no está habilitada.',1;
              UPDATE form SET TypeLockedAt=SYSUTCDATETIME() FROM reference.PaymentMethods form
              WHERE form.TenantId=@TenantId AND form.TypeLockedAt IS NULL AND EXISTS(
                SELECT 1 FROM OPENJSON(@Payments) WITH(PaymentMethodId uniqueidentifier) input WHERE input.PaymentMethodId=form.PaymentMethodId);
              INSERT dbo.DispatchDeliveryPayments(DispatchDeliveryPaymentId,BusinessId,DispatchId,DispatchSourceDocumentId,
                ApplicationType,PaymentMethod,Amount,Reference,EvidenceUrl,PaymentMethodId,PaymentMethodVersion,
                CardFranchiseCode,ApprovalNumber,RecordedBy,OccurredAt,CreatedAt)
              SELECT NEWID(),@BusinessId,@Id,@DocumentId,ApplicationType,PaymentMethod,Amount,Reference,EvidenceUrl,
                PaymentMethodId,PaymentMethodVersion,CardFranchiseCode,ApprovalNumber,@UserId,@Occurred,SYSUTCDATETIME()
              FROM OPENJSON(@Payments) WITH(ApplicationType nvarchar(24),PaymentMethod nvarchar(24),Amount decimal(19,4),
                Reference nvarchar(120),EvidenceUrl nvarchar(1000),PaymentMethodId uniqueidentifier,PaymentMethodVersion int,
                CardFranchiseCode nvarchar(40),ApprovalNumber nvarchar(80));
              INSERT dbo.DispatchDeliveryReturns(DispatchDeliveryReturnId,BusinessId,DispatchId,DispatchSourceDocumentId,
                OriginalLineNumber,ProductId,Quantity,InventoryDisposition,ReasonCode,ReasonDescription,CreatedBy,CreatedAt)
              SELECT NEWID(),@BusinessId,@Id,@DocumentId,input.OriginalLineNumber,line.ProductId,input.Quantity,
                input.InventoryDisposition,input.ReasonCode,input.ReasonDescription,@UserId,SYSUTCDATETIME()
              FROM OPENJSON(@Returns) WITH(OriginalLineNumber int,Quantity decimal(19,6),InventoryDisposition nvarchar(24),
                ReasonCode nvarchar(32),ReasonDescription nvarchar(300)) input
              INNER JOIN dbo.SalesDocumentLines line ON line.DocumentId=@SourceDocumentId AND line.LineNumber=input.OriginalLineNumber;
              """,connection,tx))
            {
                Scope(insert,actor,dispatchId);insert.Parameters.AddWithValue("@DocumentId",request.DispatchSourceDocumentId);
                insert.Parameters.AddWithValue("@SourceDocumentId",sourceDocumentId);insert.Parameters.AddWithValue("@Occurred",request.OccurredAt);
                insert.Parameters.Add("@Payments",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(request.Payments);
                insert.Parameters.Add("@Returns",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(request.Returns);
                await insert.ExecuteNonQueryAsync(ct);
            }
            await using(var insert=new SqlCommand("""
              DECLARE @CollectionSession uniqueidentifier;
              EXEC worksessions.DispatchSessionEnsure @TenantId,@BusinessId,@Id,@UserId,@IsAdministrator,@CollectionSession OUTPUT;
              INSERT dbo.DispatchDeliveryEvents(DispatchDeliveryEventId,BusinessId,DispatchId,DispatchSourceDocumentId,DeliveryStatus,Reason,Notes,Latitude,Longitude,OccurredAt,RecordedBy,ReceivedAt,IdempotencyKey,PayloadHash,ReturnTotal)
              VALUES(NEWID(),@BusinessId,@Id,@DocumentId,@Status,@Reason,@Notes,@Latitude,@Longitude,@Occurred,@UserId,SYSUTCDATETIME(),@Key,@Hash,@ReturnTotal);
              UPDATE dbo.DispatchSourceDocuments SET Status=@Status WHERE DispatchSourceDocumentId=@DocumentId;
              UPDATE dispatch SET Status=CASE WHEN EXISTS(
                SELECT 1 FROM dbo.DispatchSourceDocuments pending
                WHERE pending.DispatchId=@Id AND NOT EXISTS(
                  SELECT 1 FROM dbo.DispatchDeliveryEvents completed
                  WHERE completed.DispatchSourceDocumentId=pending.DispatchSourceDocumentId AND completed.IsCurrent=1)
              ) THEN N'InDelivery' ELSE N'PendingSettlement' END,
                UpdatedBy=@UserId,UpdatedAt=SYSUTCDATETIME()
              FROM dbo.Dispatches dispatch WHERE dispatch.DispatchId=@Id;
            """,connection,tx))
            {Scope(insert,actor,dispatchId);insert.Parameters.AddWithValue("@DocumentId",request.DispatchSourceDocumentId);insert.Parameters.AddWithValue("@Status",request.DeliveryStatus);insert.Parameters.AddWithValue("@Reason",(object?)request.Reason??DBNull.Value);insert.Parameters.AddWithValue("@Notes",(object?)request.Notes??DBNull.Value);Coordinate(insert,"@Latitude",request.Latitude);Coordinate(insert,"@Longitude",request.Longitude);insert.Parameters.AddWithValue("@Occurred",request.OccurredAt);insert.Parameters.AddWithValue("@Key",request.IdempotencyKey);insert.Parameters.Add("@Hash",SqlDbType.Binary,32).Value=requestHash;Money(insert,"@ReturnTotal",returnTotal);await insert.ExecuteNonQueryAsync(ct);}
            await tx.CommitAsync(ct);return (await GetAsync(actor,dispatchId,ct))!;
        }
        catch(SqlException ex) when(ex.Number==51000){await SafeRollbackAsync(tx,ct);throw new DispatchConflictException(ex.Message);}
        catch{await SafeRollbackAsync(tx,ct);throw;}
    }

    public async Task<DispatchExecutionDetail> ReorderAsync(DispatchActorIdentity actor, Guid dispatchId, ReorderDispatchDocumentsRequest request, byte[] rowVersion, CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);await using var tx=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            await using var command=new SqlCommand("""
                DECLARE @CurrentVersion varbinary(8);
                SELECT @CurrentVersion=RowVersion FROM dbo.Dispatches WITH(UPDLOCK,HOLDLOCK)
                WHERE DispatchId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId
                  AND (@IsAdministrator=1 OR DriverUserId=@UserId) AND Status IN(N'Released',N'InDelivery');
                IF @CurrentVersion IS NULL THROW 51000,N'El despacho no está disponible para cambiar el recorrido.',1;
                DECLARE @Rows TABLE(DocumentId uniqueidentifier PRIMARY KEY,Sequence int UNIQUE);
                INSERT @Rows SELECT CONVERT(uniqueidentifier,value),CONVERT(int,[key])+1 FROM OPENJSON(@Documents);
                IF EXISTS(SELECT DocumentId FROM @Rows EXCEPT SELECT DispatchSourceDocumentId FROM dbo.DispatchSourceDocuments WHERE DispatchId=@Id)
                  OR EXISTS(SELECT DispatchSourceDocumentId FROM dbo.DispatchSourceDocuments WHERE DispatchId=@Id EXCEPT SELECT DocumentId FROM @Rows)
                  THROW 51000,N'El orden debe contener exactamente los documentos de este despacho.',1;
                IF NOT EXISTS(SELECT DocumentId,Sequence FROM @Rows EXCEPT SELECT DispatchSourceDocumentId,Sequence FROM dbo.DispatchDocumentSequences WHERE DispatchId=@Id)
                  AND NOT EXISTS(SELECT DispatchSourceDocumentId,Sequence FROM dbo.DispatchDocumentSequences WHERE DispatchId=@Id EXCEPT SELECT DocumentId,Sequence FROM @Rows)
                  RETURN;
                IF @CurrentVersion<>@Version THROW 51000,N'El despacho cambió. Actualiza antes de ordenar las entregas.',1;
                DELETE dbo.DispatchDocumentSequences WHERE DispatchId=@Id;
                INSERT dbo.DispatchDocumentSequences(DispatchId,DispatchSourceDocumentId,Sequence,UpdatedBy,UpdatedAt)
                SELECT @Id,DocumentId,Sequence,@UserId,SYSUTCDATETIME() FROM @Rows;
                UPDATE dbo.Dispatches SET UpdatedBy=@UserId,UpdatedAt=SYSUTCDATETIME() WHERE DispatchId=@Id;
                """,connection,tx);
            Scope(command,actor,dispatchId);
            command.Parameters.Add("@Version",SqlDbType.Timestamp).Value=rowVersion;
            command.Parameters.Add("@Documents",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(request.OrderedDocumentIds);
            await command.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return(await GetAsync(actor,dispatchId,ct))!;
        }
        catch(SqlException error) when(error.Number==51000)
        {
            await SafeRollbackAsync(tx,ct);
            throw new DispatchConflictException(error.Message);
        }
        catch{await SafeRollbackAsync(tx,ct);throw;}
    }

    public async Task<DispatchExecutionDetail> RecordExpenseAsync(DispatchActorIdentity actor, Guid dispatchId, DispatchExpenseInput request, CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);await using var command=new SqlCommand("""
          SET XACT_ABORT ON;
          BEGIN TRAN;
          BEGIN TRY
            IF NOT EXISTS(SELECT 1 FROM dbo.Dispatches WITH(UPDLOCK,HOLDLOCK) WHERE DispatchId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId AND DriverUserId=@UserId)
              THROW 51000,N'El despacho no está asignado a este transportador.',1;
            IF EXISTS(SELECT 1 FROM dbo.DispatchExpenses WHERE BusinessId=@BusinessId AND IdempotencyKey=@Key)
            BEGIN
              IF NOT EXISTS(SELECT 1 FROM dbo.DispatchExpenses WHERE DispatchId=@Id AND BusinessId=@BusinessId AND IdempotencyKey=@Key AND RecordedBy=@UserId
                AND Amount=@Amount AND OccurredAt=@Occurred AND ((ExpenseConceptId=@Concept) OR (ExpenseConceptId IS NULL AND @Concept IS NULL)) AND (@Concept IS NOT NULL OR Category=@Category)
                AND ISNULL(Description,N'')=ISNULL(@Description,N'') AND ISNULL(EvidenceUrl,N'')=ISNULL(@Evidence,N''))
                THROW 51000,N'La clave del gasto ya fue usada con otros datos.',1;
              COMMIT; RETURN;
            END;
            IF NOT EXISTS(SELECT 1 FROM dbo.Dispatches WHERE DispatchId=@Id AND Status IN(N'Released',N'InDelivery',N'PendingSettlement'))
              OR EXISTS(SELECT 1 FROM dispatch.Closures WHERE DispatchId=@Id)
              THROW 51000,N'El despacho no está abierto para reportar gastos.',1;
            DECLARE @Name nvarchar(120)=@Category;
            IF @Concept IS NOT NULL
            BEGIN
              SET @Name=(SELECT Name FROM dbo.ExpenseConcepts WHERE ExpenseConceptId=@Concept AND BusinessId=@BusinessId AND IsActive=1);
              IF @Name IS NULL THROW 51000,N'El concepto de gasto no está habilitado en esta sede.',1;
            END;
            INSERT dbo.DispatchExpenses(DispatchExpenseId,BusinessId,DispatchId,Category,ExpenseConceptId,Amount,Description,EvidenceUrl,ApprovalStatus,RecordedBy,OccurredAt,IdempotencyKey,CreatedAt)
            VALUES(NEWID(),@BusinessId,@Id,@Name,@Concept,@Amount,@Description,@Evidence,N'Pending',@UserId,@Occurred,@Key,SYSUTCDATETIME());
            COMMIT;
          END TRY
          BEGIN CATCH
            IF @@TRANCOUNT>0 ROLLBACK;
            THROW;
          END CATCH
        """,connection);Scope(command,actor,dispatchId);command.Parameters.AddWithValue("@Category",request.Category);command.Parameters.Add("@Concept",SqlDbType.UniqueIdentifier).Value=(object?)request.ConceptId??DBNull.Value;Money(command,"@Amount",request.Amount);command.Parameters.AddWithValue("@Description",(object?)request.Description??DBNull.Value);command.Parameters.AddWithValue("@Evidence",(object?)request.EvidenceUrl??DBNull.Value);command.Parameters.AddWithValue("@Occurred",request.OccurredAt);command.Parameters.AddWithValue("@Key",request.IdempotencyKey);try{await command.ExecuteNonQueryAsync(ct);}catch(SqlException ex) when(ex.Number==51000){throw new DispatchConflictException(ex.Message);}return(await GetAsync(actor,dispatchId,ct))!;
    }

    public async Task<DispatchExecutionDetail> ReviewExpenseAsync(DispatchActorIdentity actor, Guid dispatchId, Guid expenseId, ReviewDispatchExpenseRequest request, CancellationToken ct)
    {
        if(request.Decision!="Rejected")throw new DispatchValidationException("La aprobación se registra mediante el formulario de Gastos.");
        await using var connection=connections.Create();await connection.OpenAsync(ct);await using var command=new SqlCommand("""
          SET XACT_ABORT ON;
          BEGIN TRAN;
          BEGIN TRY
            IF NOT EXISTS(SELECT 1 FROM dbo.Dispatches WITH(UPDLOCK,HOLDLOCK) WHERE DispatchId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId)
              THROW 51000,N'El despacho no está disponible en esta sede.',1;
            IF EXISTS(SELECT 1 FROM dbo.DispatchExpenses WHERE BusinessId=@BusinessId AND ReviewIdempotencyKey=@Key)
            BEGIN
              IF NOT EXISTS(SELECT 1 FROM dbo.DispatchExpenses WHERE DispatchExpenseId=@ExpenseId AND DispatchId=@Id AND BusinessId=@BusinessId
                AND ReviewIdempotencyKey=@Key AND ApprovalStatus=N'Rejected' AND ReviewedBy=@UserId AND ISNULL(ReviewNotes,N'')=ISNULL(@Notes,N''))
                THROW 51000,N'La clave de revisión ya fue usada con otros datos.',1;
              COMMIT; RETURN;
            END;
            IF NOT EXISTS(SELECT 1 FROM dispatch.Closures WITH(UPDLOCK,HOLDLOCK) WHERE DispatchId=@Id AND BusinessId=@BusinessId AND Status=N'PendingReview')
              THROW 51000,N'El transportador debe cerrar el despacho antes de revisar sus gastos.',1;
            DECLARE @Requested decimal(19,4),@Current nvarchar(16);
            SELECT @Requested=Amount,@Current=ApprovalStatus FROM dbo.DispatchExpenses WITH(UPDLOCK,HOLDLOCK) WHERE DispatchExpenseId=@ExpenseId AND DispatchId=@Id AND BusinessId=@BusinessId;
            IF @Requested IS NULL THROW 51000,N'El gasto del despacho no está disponible.',1;
            IF @Current<>N'Pending' THROW 51000,N'El gasto del despacho ya fue revisado.',1;
            UPDATE dbo.DispatchExpenses SET ApprovalStatus=N'Rejected',ApprovedAmount=0,ReviewedBy=@UserId,ReviewedAt=SYSUTCDATETIME(),ReviewNotes=@Notes,ReviewIdempotencyKey=@Key WHERE DispatchExpenseId=@ExpenseId;
            COMMIT;
          END TRY
          BEGIN CATCH
            IF @@TRANCOUNT>0 ROLLBACK;
            THROW;
          END CATCH
        """,connection);Scope(command,actor,dispatchId);command.Parameters.AddWithValue("@ExpenseId",expenseId);command.Parameters.AddWithValue("@Notes",(object?)request.Notes??DBNull.Value);command.Parameters.AddWithValue("@Key",request.IdempotencyKey);try{await command.ExecuteNonQueryAsync(ct);}catch(SqlException ex) when(ex.Number==51000){throw new DispatchConflictException(ex.Message);}return(await GetAsync(actor,dispatchId,ct))!;
    }

    public async Task<DispatchExecutionDetail> CloseRouteAsync(DispatchActorIdentity actor, Guid dispatchId, CloseDispatchRouteRequest request, CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);await using var command=new SqlCommand("""
          SET XACT_ABORT ON;
          BEGIN TRAN;
          BEGIN TRY
            IF NOT EXISTS(SELECT 1 FROM dbo.Dispatches WITH(UPDLOCK,HOLDLOCK) WHERE DispatchId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId AND DriverUserId=@UserId)
              THROW 51000,N'El despacho no está asignado a este transportador.',1;
            IF EXISTS(SELECT 1 FROM dispatch.Closures WHERE BusinessId=@BusinessId AND IdempotencyKey=@Key)
            BEGIN
              IF NOT EXISTS(SELECT 1 FROM dispatch.Closures WHERE DispatchId=@Id AND BusinessId=@BusinessId AND IdempotencyKey=@Key
                AND TransporterClosedBy=@UserId AND DeclaredCash=@Declared AND DeclaredCard=@CardDeclared AND DeclaredTransfer=@TransferDeclared)
                THROW 51000,N'La clave del cierre ya fue usada con otros datos.',1;
              COMMIT;
              RETURN;
            END;
            IF EXISTS(SELECT 1 FROM dispatch.Closures WHERE DispatchId=@Id)
              THROW 51000,N'El transportador ya cerró este despacho. Consulta su declaración guardada.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.Dispatches WHERE DispatchId=@Id AND Status IN(N'InDelivery',N'PendingSettlement'))
              THROW 51000,N'El despacho no está disponible para cerrar.',1;
            IF EXISTS(SELECT 1 FROM dbo.DispatchSourceDocuments source WHERE source.DispatchId=@Id AND NOT EXISTS(SELECT 1 FROM dbo.DispatchDeliveryEvents delivery WHERE delivery.DispatchSourceDocumentId=source.DispatchSourceDocumentId AND delivery.IsCurrent=1)) THROW 51000,'Registra el resultado de todas las entregas antes de cerrar el despacho.',1;
            DECLARE @Cash decimal(19,4),@Deposit decimal(19,4),@Advance decimal(19,4),@Returns decimal(19,4),
              @RemainingCredit decimal(19,4),@Undelivered decimal(19,4),@DispatchTotal decimal(19,4),@PreviouslyCollected decimal(19,4),@Cards decimal(19,4);
            SELECT @Cash=GrossCash,@Deposit=TransferTotal,@Advance=AdvanceTotal,@Returns=ReturnTotal,
              @RemainingCredit=RemainingCreditTotal,@Undelivered=UndeliveredTotal,@DispatchTotal=DispatchTotal,
              @PreviouslyCollected=PreviouslyCollectedTotal,@Cards=CardTotal FROM dispatch.PaymentTotals(@Id);
            DECLARE @Balance decimal(19,4)=@DispatchTotal-(@Cash+@Deposit+@Cards+@RemainingCredit+@Returns+@Undelivered+@PreviouslyCollected);
            IF @RemainingCredit<0 THROW 51000,'Los abonos superan el saldo neto de las facturas a crédito.',1;
            IF @Balance<>0 THROW 51000,'Revisa los pagos, documentos a crédito, devoluciones y entregas: el despacho no cuadra.',1;
            DECLARE @Session uniqueidentifier,@Closure uniqueidentifier=NEWID(),@ClosedAt datetimeoffset(7)=SYSUTCDATETIME();
            EXEC worksessions.DispatchSessionEnsure @TenantId,@BusinessId,@Id,@UserId,0,@Session OUTPUT;
            DECLARE @Snapshot nvarchar(max)=(SELECT @Closure workSessionClosureId,@Session workSessionId,
              @Id dispatchId,@UserId closedByUserId,@ClosedAt closedAt,@Declared declaredCash,
              @CardDeclared declaredCard,@TransferDeclared declaredTransfer FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
            DECLARE @Totals nvarchar(max)=(SELECT method paymentMethodCode,amount salesAmount,
              CONVERT(decimal(19,4),0) refundAmount,CONVERT(decimal(19,4),0) otherAmount,
              amount netAmount,counted countedAmount,counted-amount difference
              FROM (VALUES(N'Cash',@Cash,@Declared),(N'Card',@Cards,@CardDeclared),
                (N'Transfer',@Deposit,@TransferDeclared),(N'Credit',@RemainingCredit,CONVERT(decimal(19,4),NULL))) valueset(method,amount,counted)
              FOR JSON PATH);
            DECLARE @Hash varbinary(32)=HASHBYTES('SHA2_256',CONVERT(varchar(max),@Snapshot COLLATE Latin1_General_100_CI_AS_SC_UTF8));
            DECLARE @SalesCount bigint=(SELECT COUNT_BIG(*) FROM dbo.DispatchSourceDocuments WHERE DispatchId=@Id);
            DECLARE @CreditCount int=(SELECT COUNT(*) FROM dbo.DispatchDeliveryPayments WHERE DispatchId=@Id AND ApplicationType=N'CreditDocument');
            DECLARE @ReturnCount bigint=(SELECT COUNT_BIG(DISTINCT DispatchSourceDocumentId) FROM dbo.DispatchDeliveryReturns WHERE DispatchId=@Id);
            DECLARE @Other decimal(19,4)=-@Undelivered,@Net decimal(19,4)=@DispatchTotal-@Returns-@Undelivered;
            EXEC worksessions.ClosureAccept @Closure,@Session,@TenantId,@BusinessId,@UserId,@Key,
              @DispatchTotal,@Returns,@Other,@Net,@Cash,@Declared,@SalesCount,@CreditCount,@RemainingCredit,
              @ReturnCount,NULL,@Snapshot,@Hash,@ClosedAt,@Totals,@Deposit,@Advance;
            UPDATE dbo.Dispatches SET Status=N'PendingSettlement',UpdatedBy=@UserId,UpdatedAt=SYSUTCDATETIME() WHERE DispatchId=@Id;
            COMMIT;
          END TRY
          BEGIN CATCH
            IF @@TRANCOUNT>0 ROLLBACK;
            THROW;
          END CATCH
        """,connection);Scope(command,actor,dispatchId);Money(command,"@Declared",request.DeclaredCash);Money(command,"@CardDeclared",request.DeclaredCard!.Value);Money(command,"@TransferDeclared",request.DeclaredTransfer!.Value);command.Parameters.AddWithValue("@Key",request.IdempotencyKey);try{await command.ExecuteNonQueryAsync(ct);}catch(SqlException ex) when(ex.Number==51000){throw new DispatchConflictException(ex.Message);}return(await GetAsync(actor,dispatchId,ct))!;
    }

    public async Task<DispatchExecutionDetail> SettleAsync(DispatchActorIdentity actor, Guid dispatchId, SettleDispatchRequest request, CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);await using var command=new SqlCommand("""
          SET XACT_ABORT ON;
          BEGIN TRAN;
          BEGIN TRY
            IF NOT EXISTS(SELECT 1 FROM dbo.Dispatches WITH(UPDLOCK,HOLDLOCK) WHERE DispatchId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId)
              THROW 51000,N'El despacho no está disponible en esta sede.',1;
            IF EXISTS(SELECT 1 FROM dbo.DispatchSettlementOperations WHERE BusinessId=@BusinessId AND IdempotencyKey=@Key)
            BEGIN
              IF NOT EXISTS(SELECT 1 FROM dbo.DispatchSettlementOperations WHERE BusinessId=@BusinessId AND DispatchId=@Id AND IdempotencyKey=@Key
                AND RequestedBy=@UserId AND CashReceived=@Cash AND ISNULL(Notes,N'')=ISNULL(@Notes,N'')
                AND ISNULL(ReturnIdsJson,N'[]')=ISNULL(@ReturnIds,N'[]')
                AND (CashDifferencePaymentMethodId=@DifferenceForm OR CashDifferencePaymentMethodId IS NULL AND @DifferenceForm IS NULL))
                THROW 51000,N'La clave de recepción ya fue usada con otros datos.',1;
              COMMIT; RETURN;
            END;
            IF NOT EXISTS(SELECT 1 FROM dispatch.Closures WITH(UPDLOCK,HOLDLOCK) WHERE DispatchId=@Id AND BusinessId=@BusinessId AND Status=N'PendingReview') THROW 51000,'El despacho no está pendiente de validación.',1;
            IF EXISTS(SELECT 1 FROM dispatch.EffectivePayments(@Id)
              WHERE MethodCode IN(N'Transfer',N'DebitCard',N'CreditCard') AND VerifiedAt IS NULL)
              THROW 51000,N'Chequea cada tarjeta y transferencia antes de aceptar la liquidación.',1;
            IF EXISTS(SELECT 1 FROM dbo.DispatchExpenses WHERE DispatchId=@Id AND ApprovalStatus=N'Pending') THROW 51000,'Aprueba o rechaza todos los gastos antes de aceptar la liquidación.',1;
            IF EXISTS(SELECT 1 FROM dbo.DispatchExpenses report LEFT JOIN dbo.Expenses expense
              ON expense.ExpenseId=report.AcceptedExpenseId AND expense.BusinessId=report.BusinessId
              WHERE report.DispatchId=@Id AND report.ApprovalStatus=N'Approved'
                AND (report.AcceptedExpenseId IS NULL OR expense.ExpenseId IS NULL OR expense.Status<>N'Processed'))
              THROW 51000,N'Los gastos aprobados deben tener su documento financiero procesado antes de aceptar la liquidación.',1;
            IF EXISTS(SELECT 1 FROM dispatch.EffectivePayments(@Id) payment
              JOIN dbo.DispatchSourceDocuments source ON source.DispatchSourceDocumentId=payment.DispatchSourceDocumentId
              JOIN dbo.SalesDocuments sale ON sale.DocumentId=source.SourceDocumentId AND sale.BusinessId=@BusinessId
              WHERE sale.CollectionMode=N'Dispatch' AND payment.ApplicationType=N'InvoicePayment' AND payment.PaymentMethodId IS NULL)
              THROW 51000,N'Completa las formas de pago de los recaudos del pedido antes de aceptar la liquidación.',1;
            -- Link documents already accepted by Returns. No refund, credit or
            -- inventory effect is generated by linking them to the delivery.
            DECLARE @LockedSales int;
            SELECT @LockedSales=COUNT(*) FROM dbo.SalesDocuments sale WITH(UPDLOCK,HOLDLOCK)
              JOIN dbo.DispatchSourceDocuments source ON source.SourceDocumentId=sale.DocumentId AND source.DispatchId=@Id;
            DECLARE @LinkedReturns TABLE(ReturnId uniqueidentifier PRIMARY KEY,OriginalDocumentId uniqueidentifier,
              TotalAmount decimal(19,4),EconomicResolution nvarchar(24));
            INSERT @LinkedReturns
            SELECT returned.ReturnId,returned.OriginalDocumentId,returned.TotalAmount,returned.EconomicResolution
            FROM OPENJSON(@ReturnIds) WITH(ReturnId uniqueidentifier '$') selected
            JOIN dbo.SalesReturns returned ON returned.ReturnId=selected.ReturnId AND returned.BusinessId=@BusinessId
              AND returned.Status=N'Processed' AND returned.EconomicResolution IN(N'Refund',N'CustomerCredit')
            JOIN dbo.DispatchSourceDocuments source ON source.SourceDocumentId=returned.OriginalDocumentId
              AND source.DispatchId=@Id AND returned.AcceptedAt>=source.CreatedAt
            JOIN dbo.SalesDocuments sale ON sale.DocumentId=source.SourceDocumentId AND sale.CollectionMode=N'Session'
            JOIN dbo.DispatchDeliveryEvents delivery ON delivery.DispatchSourceDocumentId=source.DispatchSourceDocumentId AND delivery.IsCurrent=1
              AND (delivery.DeliveryStatus=N'NotDelivered' OR delivery.ReturnTotal>0)
            JOIN dbo.AccountingPostingJobs posting ON posting.SourceDocumentId=returned.ReturnId AND posting.SourceDocumentType=N'SalesReturn'
              AND posting.TenantId=@TenantId AND posting.BusinessId=@BusinessId AND posting.Status IN(N'Posted',N'CommercialEffectsApplied');
            IF (SELECT COUNT(*) FROM @LinkedReturns)<>(SELECT COUNT(*) FROM OPENJSON(@ReturnIds))
              THROW 51000,N'Vincula devoluciones de estas ventas con su reembolso o aplicación a cartera terminado, registradas después de asignarlas al despacho.',1;
            IF EXISTS(SELECT 1 FROM dbo.DispatchSourceDocuments source
              JOIN dbo.SalesDocuments sale ON sale.DocumentId=source.SourceDocumentId AND sale.CollectionMode=N'Session'
              JOIN dbo.DispatchDeliveryEvents delivery ON delivery.DispatchSourceDocumentId=source.DispatchSourceDocumentId AND delivery.IsCurrent=1
              OUTER APPLY(SELECT SUM(TotalAmount) Amount FROM @LinkedReturns WHERE OriginalDocumentId=source.SourceDocumentId) linked
              WHERE source.DispatchId=@Id AND (
                CASE WHEN delivery.DeliveryStatus=N'NotDelivered' THEN source.DocumentTotalSnapshot ELSE delivery.ReturnTotal END>source.CreditAmountSnapshot AND linked.Amount IS NULL
                OR linked.Amount IS NOT NULL AND linked.Amount<>CASE WHEN delivery.DeliveryStatus=N'NotDelivered' THEN source.DocumentTotalSnapshot ELSE delivery.ReturnTotal END))
              THROW 51000,N'Registra el reembolso real desde Devoluciones y vincula los documentos que cubren exactamente la mercancía devuelta.',1;
            DECLARE @ExpectedReturns TABLE(OriginalDocumentId uniqueidentifier,LineNumber int,Disposition nvarchar(24),Quantity decimal(19,6));
            INSERT @ExpectedReturns
            SELECT source.SourceDocumentId,line.LineNumber,
              CASE WHEN delivery.DeliveryStatus=N'NotDelivered' THEN N'Sellable' ELSE captured.InventoryDisposition END,
              CASE WHEN delivery.DeliveryStatus=N'NotDelivered' THEN assigned.AssignedQuantity ELSE captured.Quantity END
            FROM dbo.DispatchSourceDocuments source
            JOIN dbo.DispatchDeliveryEvents delivery ON delivery.DispatchSourceDocumentId=source.DispatchSourceDocumentId AND delivery.IsCurrent=1
            JOIN dbo.SalesDocumentLines line ON line.DocumentId=source.SourceDocumentId
            JOIN dbo.DispatchLines assigned ON assigned.DispatchSourceDocumentId=source.DispatchSourceDocumentId AND assigned.SourceLineNumber=line.LineNumber
            LEFT JOIN dbo.DispatchDeliveryReturns captured ON captured.DispatchSourceDocumentId=source.DispatchSourceDocumentId AND captured.OriginalLineNumber=line.LineNumber
            WHERE source.DispatchId=@Id AND EXISTS(SELECT 1 FROM @LinkedReturns WHERE OriginalDocumentId=source.SourceDocumentId)
              AND (delivery.DeliveryStatus=N'NotDelivered' OR captured.DispatchDeliveryReturnId IS NOT NULL);
            DECLARE @ActualReturns TABLE(OriginalDocumentId uniqueidentifier,LineNumber int,Disposition nvarchar(24),Quantity decimal(19,6));
            INSERT @ActualReturns
            SELECT line.OriginalDocumentId,line.OriginalLineNumber,line.InventoryDisposition,SUM(line.Quantity)
            FROM dbo.SalesReturnLines line JOIN @LinkedReturns linked ON linked.ReturnId=line.ReturnId
            GROUP BY line.OriginalDocumentId,line.OriginalLineNumber,line.InventoryDisposition;
            IF EXISTS(SELECT * FROM @ExpectedReturns EXCEPT SELECT * FROM @ActualReturns)
              OR EXISTS(SELECT * FROM @ActualReturns EXCEPT SELECT * FROM @ExpectedReturns)
              THROW 51000,N'Las devoluciones vinculadas deben coincidir con los productos, cantidades y tratamiento registrados en la entrega.',1;
            INSERT dbo.DispatchSettlementOperations(DispatchSettlementOperationId,BusinessId,DispatchId,WorkSessionId,CashReceived,Notes,CashDifferencePaymentMethodId,ReturnIdsJson,RequestedBy,RequestedAt,Status,Attempts,NextAttemptAt,IdempotencyKey)
            SELECT NEWID(),@BusinessId,@Id,session.WorkSessionId,@Cash,@Notes,@DifferenceForm,@ReturnIds,@UserId,SYSUTCDATETIME(),N'Pending',0,SYSUTCDATETIME(),@Key
            FROM dbo.WorkSessions session WHERE session.DispatchId=@Id AND session.Kind=N'Dispatch' AND session.BusinessId=@BusinessId;
            IF @@ROWCOUNT<>1 THROW 51000,N'El cierre debe conservar una única sesión de dinero del despacho.',1;
            DECLARE @GrossCash decimal(19,4),@Deposit decimal(19,4),@Advance decimal(19,4),@Returns decimal(19,4),
              @RemainingCredit decimal(19,4),@Undelivered decimal(19,4),@DispatchTotal decimal(19,4),@PreviouslyCollected decimal(19,4),@Cards decimal(19,4);
            SELECT @GrossCash=GrossCash,@Deposit=TransferTotal,@Advance=AdvanceTotal,@Returns=ReturnTotal,
              @RemainingCredit=RemainingCreditTotal,@Undelivered=UndeliveredTotal,@DispatchTotal=DispatchTotal,
              @PreviouslyCollected=PreviouslyCollectedTotal,@Cards=CardTotal FROM dispatch.PaymentTotals(@Id);
            DECLARE @Balance decimal(19,4)=@DispatchTotal-(@GrossCash+@Deposit+@Cards+@RemainingCredit+@Returns+@Undelivered+@PreviouslyCollected);
            DECLARE @ApprovedExpenses decimal(19,4)=(SELECT COALESCE(SUM(ApprovedAmount),0) FROM dbo.DispatchExpenses WHERE DispatchId=@Id AND ApprovalStatus=N'Approved');
            DECLARE @Expected decimal(19,4)=@GrossCash-@ApprovedExpenses;
            IF @RemainingCredit<0 OR @Balance<>0 THROW 51000,'Los recaudos, devoluciones y documentos del despacho no cuadran.',1;
            IF @Expected<0 THROW 51000,'Los gastos aprobados superan el efectivo recaudado.',1;
            IF @Cash<>@Expected AND NULLIF(LTRIM(RTRIM(@Notes)),N'') IS NULL THROW 51000,'Explica el faltante o sobrante de efectivo recibido.',1;
            IF @Cash<>@Expected
            BEGIN
              IF @DifferenceForm IS NULL AND (SELECT COUNT(DISTINCT PaymentMethodId) FROM dispatch.EffectivePayments(@Id) WHERE MethodCode=N'Cash')<>1
                THROW 51000,N'Selecciona la forma de efectivo que asumirá el faltante o sobrante.',1;
              DECLARE @CashForm uniqueidentifier=COALESCE(@DifferenceForm,
                (SELECT TOP(1) PaymentMethodId FROM dispatch.EffectivePayments(@Id) WHERE MethodCode=N'Cash' ORDER BY PaymentMethodId));
              IF NOT EXISTS(SELECT 1 FROM reference.PaymentMethods form WITH(HOLDLOCK)
                JOIN reference.Options type ON type.OptionId=form.PaymentTypeId AND type.CatalogCode=N'payment-method' AND type.Code=N'Cash' AND type.IsActive=1
                CROSS APPLY accounting.PaymentMethodAccountReadiness(@TenantId,type.Code,form.AccountId) ready
                WHERE form.PaymentMethodId=@CashForm AND form.TenantId=@TenantId AND form.IsActive=1 AND form.UseForReceipts=1 AND ready.SourceType=N'CustomerPaymentMethod')
                THROW 51000,N'La forma de efectivo de la diferencia no está habilitada para recibir pagos.',1;
              UPDATE reference.PaymentMethods SET TypeLockedAt=SYSUTCDATETIME()
              WHERE TenantId=@TenantId AND PaymentMethodId=@CashForm AND TypeLockedAt IS NULL;
            END;
            ELSE IF @DifferenceForm IS NOT NULL THROW 51000,N'No hay diferencia de efectivo que atribuir.',1;
            UPDATE closure SET DispatchExpectedCash=@Expected,DispatchTransferTotal=@Deposit,
              DispatchCreditDocumentTotal=@RemainingCredit,DispatchCreditAdvanceTotal=@Advance,DispatchReturnTotal=@Returns,
              ReceivedCash=@Cash,ReceivedByUserId=@UserId,ReceivedAt=SYSUTCDATETIME(),DispatchReviewNotes=@Notes,DispatchStatus=N'Processing'
            FROM dbo.WorkSessionClosures closure JOIN dbo.WorkSessions session ON session.WorkSessionId=closure.WorkSessionId
            WHERE session.DispatchId=@Id AND session.BusinessId=@BusinessId AND session.Kind=N'Dispatch';
            UPDATE dbo.Dispatches SET Status=N'SettlementProcessing',UpdatedBy=@UserId,UpdatedAt=SYSUTCDATETIME() WHERE DispatchId=@Id;
            COMMIT;
          END TRY
          BEGIN CATCH
            IF @@TRANCOUNT>0 ROLLBACK;
            THROW;
          END CATCH
        """,connection);Scope(command,actor,dispatchId);Money(command,"@Cash",request.CashReceived);command.Parameters.Add("@ReturnIds",SqlDbType.NVarChar,-1).Value=request.ReturnIds is {Count:>0} ? JsonSerializer.Serialize(request.ReturnIds.Order()) : DBNull.Value;command.Parameters.AddWithValue("@DifferenceForm",(object?)request.CashDifferencePaymentMethodId??DBNull.Value);command.Parameters.AddWithValue("@Notes",(object?)request.Notes??DBNull.Value);command.Parameters.AddWithValue("@Key",request.IdempotencyKey);try{await command.ExecuteNonQueryAsync(ct);}catch(SqlException ex) when(ex.Number==51000){throw new DispatchConflictException(ex.Message);}return(await GetAsync(actor,dispatchId,ct))!;
    }

    private static void Scope(SqlCommand command,DispatchActorIdentity actor,Guid id){command.Parameters.AddWithValue("@TenantId",actor.TenantId);command.Parameters.AddWithValue("@BusinessId",actor.BusinessId);command.Parameters.AddWithValue("@UserId",actor.UserId);command.Parameters.AddWithValue("@ReadAll",actor.Permissions.Contains(DispatchPermissionCodes.ReadAll));command.Parameters.AddWithValue("@Settle",actor.Permissions.Contains(DispatchPermissionCodes.Settle));command.Parameters.AddWithValue("@IsAdministrator",actor.IsAdministrator);command.Parameters.AddWithValue("@Id",id);}
    private static void Money(SqlCommand command,string name,decimal value){var parameter=command.Parameters.Add(name,SqlDbType.Decimal);parameter.Precision=19;parameter.Scale=4;parameter.Value=value;}
    private static void Quantity(SqlCommand command,string name,decimal value){var parameter=command.Parameters.Add(name,SqlDbType.Decimal);parameter.Precision=19;parameter.Scale=6;parameter.Value=value;}
    private static void Coordinate(SqlCommand command,string name,decimal? value){var parameter=command.Parameters.Add(name,SqlDbType.Decimal);parameter.Precision=9;parameter.Scale=6;parameter.Value=(object?)value??DBNull.Value;}
    private static string? NullableString(SqlDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetString(ordinal);
    private static Guid? NullableGuid(SqlDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetGuid(ordinal);
    private static decimal? NullableDecimal(SqlDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetDecimal(ordinal);
    private static async Task SafeRollbackAsync(SqlTransaction transaction,CancellationToken ct){try{await transaction.RollbackAsync(ct);}catch(InvalidOperationException){}}
    private sealed record DispatchReturnLine(decimal Quantity,decimal Discount,decimal Untaxed,decimal Tax,decimal Total,
        decimal ReturnedQuantity,decimal ReturnedDiscount,decimal ReturnedUntaxed,decimal ReturnedTax,decimal ReturnedTotal);

}
