using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Purchasing;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Purchasing;
using Auraly.Domain.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPurchaseReturnStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IPurchaseReturnStore
{
    public async Task<ReturnableGoodsReceiptPage> ListReturnableReceiptsAsync(
        PurchasingUserIdentity user, string? search, DateOnly? from, DateOnly? to,
        bool? withAvailableQuantity, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH Returned AS
            (
              SELECT l.OriginalGoodsReceiptId,l.OriginalLineNumber,
                     SUM(l.Quantity) ReturnedQuantity,SUM(l.LineTotal) ReturnedTotal
              FROM dbo.PurchaseReturnLines l
              INNER JOIN dbo.PurchaseReturns r ON r.PurchaseReturnId=l.PurchaseReturnId
              WHERE r.BusinessId=@BusinessId
              GROUP BY l.OriginalGoodsReceiptId,l.OriginalLineNumber
            ), Receipts AS
            (
              SELECT r.GoodsReceiptId,r.DocumentNumber,s.Name SupplierName,w.Name WarehouseName,
                     r.SupplierInvoiceNumber,r.ReceivedAt,r.GrandTotal,
                     COALESCE(SUM(x.ReturnedTotal),0) ReturnedTotal,
                     CAST(CASE WHEN SUM(CASE WHEN l.Quantity>COALESCE(x.ReturnedQuantity,0)
                                              THEN 1 ELSE 0 END)>0 THEN 1 ELSE 0 END AS BIT) HasAvailableQuantity
              FROM dbo.GoodsReceipts r
              INNER JOIN dbo.Suppliers s ON s.SupplierId=r.SupplierId
              INNER JOIN dbo.Warehouses w ON w.WarehouseId=r.WarehouseId
              INNER JOIN dbo.GoodsReceiptLines l ON l.GoodsReceiptId=r.GoodsReceiptId
              LEFT JOIN Returned x ON x.OriginalGoodsReceiptId=l.GoodsReceiptId
                                  AND x.OriginalLineNumber=l.LineNumber
              WHERE r.BusinessId=@BusinessId AND r.Status=N'Processed'
                AND (@From IS NULL OR r.ReceivedAt>=@From)
                AND (@To IS NULL OR r.ReceivedAt<DATEADD(DAY,1,@To))
                AND (@Search IS NULL OR r.DocumentNumber LIKE N'%'+@Search+N'%'
                  OR s.Name LIKE N'%'+@Search+N'%'
                  OR r.SupplierInvoiceNumber LIKE N'%'+@Search+N'%'
                  OR w.Name LIKE N'%'+@Search+N'%'
                  OR EXISTS(SELECT 1 FROM dbo.Products product
                    WHERE product.ProductId=l.ProductId AND
                      (product.ProductCode LIKE N'%'+@Search+N'%' OR
                       product.Reference LIKE N'%'+@Search+N'%' OR
                       product.Name LIKE N'%'+@Search+N'%')))
              GROUP BY r.GoodsReceiptId,r.DocumentNumber,s.Name,w.Name,
                       r.SupplierInvoiceNumber,r.ReceivedAt,r.GrandTotal
            )
            SELECT GoodsReceiptId,DocumentNumber,SupplierName,WarehouseName,
                   SupplierInvoiceNumber,ReceivedAt,GrandTotal,ReturnedTotal,
                   HasAvailableQuantity,COUNT(*) OVER()
            FROM Receipts
            WHERE @Available IS NULL OR HasAvailableQuantity=@Available
            ORDER BY ReceivedAt DESC,GoodsReceiptId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("@From", (object?)from?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("@To", (object?)to?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("@Available", (object?)withAvailableQuantity ?? DBNull.Value);
        command.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@PageSize", pageSize);
        var items = new List<ReturnableGoodsReceiptListItem>();
        var total = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            total = reader.GetInt32(9);
            items.Add(new ReturnableGoodsReceiptListItem(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetDateTimeOffset(5), reader.GetDecimal(6), reader.GetDecimal(7),
                reader.GetBoolean(8)));
        }
        return new ReturnableGoodsReceiptPage(
            items, page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<ReturnableGoodsReceipt?> GetReturnableReceiptAsync(
        PurchasingUserIdentity user, Guid goodsReceiptId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string headerSql = """
            SELECT r.DocumentNumber,r.WarehouseId,w.Name,r.SupplierId,s.Name,
                   r.SupplierInvoiceNumber,r.ReceivedAt,r.CurrencyCode,r.GrandTotal
            FROM dbo.GoodsReceipts r
            INNER JOIN dbo.Warehouses w ON w.WarehouseId=r.WarehouseId
            INNER JOIN dbo.Suppliers s ON s.SupplierId=r.SupplierId
            INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId AND b.TenantId=@TenantId
            WHERE r.GoodsReceiptId=@Id AND r.BusinessId=@BusinessId AND r.Status=N'Processed';
            """;
        string number; Guid warehouseId; string warehouse; Guid supplierId;
        string supplier; string? supplierInvoice; DateTimeOffset receivedAt;
        string currency; decimal total;
        await using (var command = new SqlCommand(headerSql, connection))
        {
            command.Parameters.AddWithValue("@Id", goodsReceiptId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            number=reader.GetString(0); warehouseId=reader.GetGuid(1); warehouse=reader.GetString(2);
            supplierId=reader.GetGuid(3); supplier=reader.GetString(4);
            supplierInvoice=reader.IsDBNull(5)?null:reader.GetString(5);
            receivedAt=reader.GetDateTimeOffset(6); currency=reader.GetString(7); total=reader.GetDecimal(8);
        }
        const string linesSql = """
            SELECT l.LineNumber,l.ProductId,l.DescriptionSnapshot,l.Quantity,
                   COALESCE(SUM(prl.Quantity),0),l.UnitCost,l.NetAmount,l.TaxAmount,l.LineTotal
            FROM dbo.GoodsReceiptLines l
            LEFT JOIN dbo.PurchaseReturnLines prl
              ON prl.OriginalGoodsReceiptId=l.GoodsReceiptId
             AND prl.OriginalLineNumber=l.LineNumber
            LEFT JOIN dbo.PurchaseReturns pr ON pr.PurchaseReturnId=prl.PurchaseReturnId
            WHERE l.GoodsReceiptId=@Id
            GROUP BY l.LineNumber,l.ProductId,l.DescriptionSnapshot,l.Quantity,
                     l.UnitCost,l.NetAmount,l.TaxAmount,l.LineTotal
            ORDER BY l.LineNumber;
            """;
        var lines = new List<ReturnableGoodsReceiptLine>();
        await using (var command = new SqlCommand(linesSql, connection))
        {
            command.Parameters.AddWithValue("@Id", goodsReceiptId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var received=reader.GetDecimal(3); var returned=reader.GetDecimal(4);
                lines.Add(new ReturnableGoodsReceiptLine(
                    reader.GetInt32(0),reader.GetGuid(1),reader.GetString(2),received,
                    returned,received-returned,reader.GetDecimal(5),reader.GetDecimal(6),
                    reader.GetDecimal(7),reader.GetDecimal(8)));
            }
        }
        return new ReturnableGoodsReceipt(goodsReceiptId,number,warehouseId,warehouse,
            supplierId,supplier,supplierInvoice,receivedAt,currency,total,lines);
    }

    public async Task<PurchaseReturnAcceptance> AcceptAsync(
        PurchasingUserIdentity user, string idempotencyKey,
        ConfirmPurchaseReturnRequest request, CancellationToken cancellationToken)
    {
        var requestHash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request));
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var replay = await TryReplayAsync(connection, transaction, user.BusinessId,
                request.ReturnId, idempotencyKey, requestHash, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }
            await using (var reason = new SqlCommand("""
                SELECT COUNT_BIG(*) FROM dbo.BusinessReasons WITH(UPDLOCK,HOLDLOCK)
                WHERE BusinessId=@BusinessId AND ReasonType=N'PurchaseReturn'
                  AND Code=@Code AND IsActive=1;
                """, connection, transaction))
            {
                reason.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                reason.Parameters.AddWithValue("@Code", request.ReasonCode);
                if (Convert.ToInt64(await reason.ExecuteScalarAsync(cancellationToken)) != 1)
                    throw new PurchasingValidationException("The return reason is not active for this business.");
            }
            var original = await LoadOriginalAsync(connection, transaction, user, request,
                cancellationToken);
            if (request.ReturnedAt < original.ReceivedAt)
                throw new PurchasingValidationException(
                    "ReturnedAt cannot be earlier than the original receipt.");
            var lines = await AllocateLinesAsync(connection, transaction, request,
                cancellationToken);
            var net=lines.Sum(line=>line.NetAmount);
            var tax=lines.Sum(line=>line.TaxAmount);
            var total=lines.Sum(line=>line.LineTotal);
            var number=await AllocateNumberAsync(connection,transaction,user.BusinessId,cancellationToken);
            var now=timeProvider.GetUtcNow();
            var sequence=await AllocateSequenceAsync(connection,transaction,user.BusinessId,now,cancellationToken);
            var payload=new PurchaseReturnDocumentPayload(
                user.TenantId,user.BusinessId,request.ReturnId,request.OriginalGoodsReceiptId,
                original.WarehouseId,original.SupplierId,user.UserId,number.FullNumber,
                number.SeriesId,number.Prefix,number.SeriesCode,number.Consecutive,
                request.ReturnedAt,request.ReasonCode,request.Notes,original.CurrencyCode,
                net,tax,total,lines);
            var payloadJson=PurchaseReturnContractSerializer.Serialize(payload);
            var payloadHash=SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));
            var jobId=ids.NewId();
            await InsertAsync(connection,transaction,request,user,original,number,
                idempotencyKey,requestHash,net,tax,total,lines,jobId,sequence,
                payloadJson,payloadHash,now,cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PurchaseReturnAcceptance(request.ReturnId,jobId,number.FullNumber,
                "Accepted",sequence,false);
        }
        catch (PurchasingConflictException)
        {
            await transaction.RollbackAsync(CancellationToken.None); throw;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new PurchasingConflictException(
                "The purchase return number, DocumentId or idempotency key is already in use.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None); throw;
        }
    }

    private static async Task<PurchaseReturnAcceptance?> TryReplayAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        Guid returnId, string key, byte[] hash, CancellationToken cancellationToken)
    {
        await using var command=new SqlCommand("""
            SELECT r.PurchaseReturnId,r.DocumentNumber,r.Status,r.PayloadHash,
                   j.ProcessingSequence,j.JobId
            FROM dbo.PurchaseReturns r WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.DocumentProcessingJobs j
              ON j.DocumentId=r.PurchaseReturnId AND j.DocumentType=N'PurchaseReturn'
            WHERE r.BusinessId=@BusinessId
              AND (r.PurchaseReturnId=@ReturnId OR r.IdempotencyKey=@Key);
            """,connection,transaction);
        command.Parameters.AddWithValue("@BusinessId",businessId);
        command.Parameters.AddWithValue("@ReturnId",returnId);
        command.Parameters.AddWithValue("@Key",key);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        if(!await reader.ReadAsync(cancellationToken))return null;
        if(!reader.GetFieldValue<byte[]>(3).AsSpan().SequenceEqual(hash))
            throw new PurchasingConflictException(
                "The idempotency key or ReturnId was reused with another payload.");
        return new PurchaseReturnAcceptance(reader.GetGuid(0),reader.GetGuid(5),
            reader.GetString(1),reader.GetString(2),reader.GetInt64(4),true);
    }

    private static async Task<OriginalReceipt> LoadOriginalAsync(
        SqlConnection connection, SqlTransaction transaction,
        PurchasingUserIdentity user, ConfirmPurchaseReturnRequest request,
        CancellationToken cancellationToken)
    {
        await using var command=new SqlCommand("""
            SELECT r.WarehouseId,r.SupplierId,r.ReceivedAt,r.CurrencyCode
            FROM dbo.GoodsReceipts r WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId AND b.TenantId=@TenantId
            WHERE r.GoodsReceiptId=@Id AND r.BusinessId=@BusinessId AND r.Status=N'Processed';
            """,connection,transaction);
        command.Parameters.AddWithValue("@Id",request.OriginalGoodsReceiptId);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        command.Parameters.AddWithValue("@TenantId",user.TenantId);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        if(!await reader.ReadAsync(cancellationToken))
            throw new PurchasingValidationException(
                "The original goods receipt is not processed or is outside the authenticated business.");
        var value=new OriginalReceipt(reader.GetGuid(0),reader.GetGuid(1),
            reader.GetDateTimeOffset(2),reader.GetString(3));
        if(value.CurrencyCode!="COP")
            throw new PurchasingValidationException(
                "Purchase returns currently require an original receipt in COP.");
        return value;
    }

    private static async Task<IReadOnlyList<PurchaseReturnLineSnapshot>> AllocateLinesAsync(
        SqlConnection connection, SqlTransaction transaction,
        ConfirmPurchaseReturnRequest request, CancellationToken cancellationToken)
    {
        var result=new List<PurchaseReturnLineSnapshot>(); var lineNumber=0;
        foreach(var requested in request.Lines.OrderBy(line=>line.OriginalLineNumber))
        {
            await using var command=new SqlCommand("""
                SELECT l.ProductId,l.DescriptionSnapshot,l.Quantity,l.UnitCost,
                       l.DiscountAmount,l.TaxCode,l.TaxRate,l.TaxTreatment,
                       l.NetAmount,l.TaxAmount,l.LineTotal,
                       COALESCE(SUM(prl.Quantity),0),COALESCE(SUM(prl.DiscountAmount),0),
                       COALESCE(SUM(prl.NetAmount),0),COALESCE(SUM(prl.TaxAmount),0),
                       COALESCE(SUM(prl.LineTotal),0)
                FROM dbo.GoodsReceiptLines l WITH (UPDLOCK,HOLDLOCK)
                LEFT JOIN dbo.PurchaseReturnLines prl WITH (UPDLOCK,HOLDLOCK)
                  ON prl.OriginalGoodsReceiptId=l.GoodsReceiptId
                 AND prl.OriginalLineNumber=l.LineNumber
                WHERE l.GoodsReceiptId=@ReceiptId AND l.LineNumber=@Line
                GROUP BY l.ProductId,l.DescriptionSnapshot,l.Quantity,l.UnitCost,
                         l.DiscountAmount,l.TaxCode,l.TaxRate,l.TaxTreatment,
                         l.NetAmount,l.TaxAmount,l.LineTotal;
                """,connection,transaction);
            command.Parameters.AddWithValue("@ReceiptId",request.OriginalGoodsReceiptId);
            command.Parameters.AddWithValue("@Line",requested.OriginalLineNumber);
            await using var reader=await command.ExecuteReaderAsync(cancellationToken);
            if(!await reader.ReadAsync(cancellationToken))
                throw new PurchasingValidationException(
                    $"Original receipt line {requested.OriginalLineNumber} does not exist.");
            PurchaseReturnAllocation allocation;
            try
            {
                allocation=PurchaseReturnCalculator.Allocate(
                    reader.GetDecimal(2),reader.GetDecimal(11),requested.Quantity,
                    reader.GetDecimal(4),reader.GetDecimal(12),reader.GetDecimal(8),
                    reader.GetDecimal(13),reader.GetDecimal(9),reader.GetDecimal(14),
                    reader.GetDecimal(10),reader.GetDecimal(15));
            }
            catch(ArgumentOutOfRangeException exception)
            {
                throw new PurchasingConflictException(
                    $"Line {requested.OriginalLineNumber} exceeds its quantity available to return: {exception.ParamName}.");
            }
            var recognizedAmount=allocation.NetAmount+
                (reader.GetString(7)==PurchasingTaxTreatments.CapitalizedCost
                    ? allocation.TaxAmount:0m);
            var recognizedCost=decimal.Round(recognizedAmount/allocation.Quantity,6,
                MidpointRounding.AwayFromZero);
            result.Add(new PurchaseReturnLineSnapshot(++lineNumber,
                requested.OriginalLineNumber,reader.GetGuid(0),reader.GetString(1),
                allocation.Quantity,reader.GetDecimal(3),allocation.DiscountAmount,
                reader.GetString(5),reader.GetDecimal(6),reader.GetString(7),
                allocation.NetAmount,allocation.TaxAmount,allocation.LineTotal,recognizedCost));
        }
        return result;
    }

    private static async Task<AuralyDocumentNumberAssignment> AllocateNumberAsync(
        SqlConnection connection,SqlTransaction transaction,Guid businessId,
        CancellationToken cancellationToken)
    {
        await using var select=new SqlCommand("""
            SELECT TOP(1) ds.DocumentSeriesId,ds.Prefix,ds.SeriesCode,ds.Padding,
                   ds.RangeEnd,COALESCE(c.NextConsecutive,ds.RangeStart)
            FROM dbo.DocumentSeries ds WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.DocumentSeriesCursors c WITH(UPDLOCK,HOLDLOCK)
              ON c.DocumentSeriesId=ds.DocumentSeriesId
            WHERE ds.BusinessId=@BusinessId AND ds.DocumentType=N'PurchaseReturn'
              AND ds.DeviceId IS NULL AND ds.IsActive=1
            ORDER BY ds.DocumentSeriesId;
            """,connection,transaction);
        select.Parameters.AddWithValue("@BusinessId",businessId);
        Guid id;string prefix;string code;byte padding;long end;long next;
        await using(var reader=await select.ExecuteReaderAsync(cancellationToken))
        {
            if(!await reader.ReadAsync(cancellationToken))
                throw new PurchasingValidationException(
                    "No active PurchaseReturn document series is configured for the business.");
            id=reader.GetGuid(0);prefix=reader.GetString(1);code=reader.GetString(2);
            padding=reader.GetByte(3);end=reader.GetInt64(4);next=reader.GetInt64(5);
        }
        if(next>end)throw new PurchasingValidationException(
            "The PurchaseReturn document series is exhausted.");
        await using var update=new SqlCommand("""
            IF EXISTS(SELECT 1 FROM dbo.DocumentSeriesCursors WHERE DocumentSeriesId=@Id)
              UPDATE dbo.DocumentSeriesCursors SET NextConsecutive=@Next,UpdatedAt=@Now WHERE DocumentSeriesId=@Id;
            ELSE INSERT dbo.DocumentSeriesCursors(DocumentSeriesId,NextConsecutive,UpdatedAt)
              VALUES(@Id,@Next,@Now);
            """,connection,transaction);
        update.Parameters.AddWithValue("@Id",id);update.Parameters.AddWithValue("@Next",next+1);
        update.Parameters.AddWithValue("@Now",DateTimeOffset.UtcNow);
        await update.ExecuteNonQueryAsync(cancellationToken);
        return AuralyDocumentNumberAssignment.Create(id,AuralyDocumentTypes.PurchaseReturn,
            prefix,code,next,padding);
    }

    private static async Task<long> AllocateSequenceAsync(
        SqlConnection connection,SqlTransaction transaction,Guid businessId,
        DateTimeOffset now,CancellationToken cancellationToken)
    {
        await using var command=new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.BusinessProcessingCursors WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId)
              INSERT dbo.BusinessProcessingCursors(BusinessId,LastAssignedSequence,LastCompletedSequence,UpdatedAt) VALUES(@BusinessId,0,0,@Now);
            UPDATE dbo.BusinessProcessingCursors WITH(UPDLOCK,HOLDLOCK)
            SET LastAssignedSequence=LastAssignedSequence+1,UpdatedAt=@Now
            OUTPUT inserted.LastAssignedSequence WHERE BusinessId=@BusinessId;
            """,connection,transaction);
        command.Parameters.AddWithValue("@BusinessId",businessId);
        command.Parameters.AddWithValue("@Now",now);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertAsync(
        SqlConnection connection,SqlTransaction transaction,
        ConfirmPurchaseReturnRequest request,PurchasingUserIdentity user,
        OriginalReceipt original,AuralyDocumentNumberAssignment number,
        string key,byte[] requestHash,decimal net,decimal tax,decimal total,
        IReadOnlyList<PurchaseReturnLineSnapshot> lines,Guid jobId,long sequence,
        string payload,byte[] payloadHash,DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using(var command=new SqlCommand("""
            INSERT dbo.PurchaseReturns
              (PurchaseReturnId,BusinessId,OriginalGoodsReceiptId,WarehouseId,SupplierId,
               DocumentSeriesId,DocumentNumber,DocumentPrefix,DocumentSeriesCode,
               DocumentConsecutive,IdempotencyKey,PayloadHash,ReturnedAt,ReasonCode,
               Notes,CurrencyCode,NetAmount,TaxAmount,TotalAmount,Status,
               ConfirmedByUserId,AcceptedAt)
            VALUES(@Id,@BusinessId,@OriginalId,@WarehouseId,@SupplierId,@SeriesId,
               @Number,@Prefix,@SeriesCode,@Consecutive,@Key,@Hash,@ReturnedAt,@Reason,
               @Notes,@Currency,@Net,@Tax,@Total,N'Accepted',@UserId,@Now);
            """,connection,transaction))
        {
            command.Parameters.AddWithValue("@Id",request.ReturnId);
            command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
            command.Parameters.AddWithValue("@OriginalId",request.OriginalGoodsReceiptId);
            command.Parameters.AddWithValue("@WarehouseId",original.WarehouseId);
            command.Parameters.AddWithValue("@SupplierId",original.SupplierId);
            command.Parameters.AddWithValue("@SeriesId",number.SeriesId);
            command.Parameters.AddWithValue("@Number",number.FullNumber);
            command.Parameters.AddWithValue("@Prefix",number.Prefix);
            command.Parameters.AddWithValue("@SeriesCode",number.SeriesCode);
            command.Parameters.AddWithValue("@Consecutive",number.Consecutive);
            command.Parameters.AddWithValue("@Key",key);
            command.Parameters.Add("@Hash",SqlDbType.Binary,32).Value=requestHash;
            command.Parameters.AddWithValue("@ReturnedAt",request.ReturnedAt);
            command.Parameters.AddWithValue("@Reason",request.ReasonCode);
            command.Parameters.AddWithValue("@Notes",(object?)request.Notes??DBNull.Value);
            command.Parameters.AddWithValue("@Currency",original.CurrencyCode);
            AddMoney(command,"@Net",net);AddMoney(command,"@Tax",tax);AddMoney(command,"@Total",total);
            command.Parameters.AddWithValue("@UserId",user.UserId);
            command.Parameters.AddWithValue("@Now",now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach(var line in lines)
        {
            await using var command=new SqlCommand("""
                INSERT dbo.PurchaseReturnLines
                  (PurchaseReturnId,LineNumber,OriginalGoodsReceiptId,OriginalLineNumber,
                   ProductId,DescriptionSnapshot,Quantity,UnitCost,DiscountAmount,TaxCode,
                   TaxRate,TaxTreatment,NetAmount,TaxAmount,LineTotal,RecognizedUnitCost)
                VALUES(@Id,@Line,@OriginalId,@OriginalLine,@ProductId,@Description,@Quantity,
                   @UnitCost,@Discount,@TaxCode,@TaxRate,@TaxTreatment,@Net,@Tax,@Total,@RecognizedCost);
                """,connection,transaction);
            command.Parameters.AddWithValue("@Id",request.ReturnId);
            command.Parameters.AddWithValue("@Line",line.LineNumber);
            command.Parameters.AddWithValue("@OriginalId",request.OriginalGoodsReceiptId);
            command.Parameters.AddWithValue("@OriginalLine",line.OriginalLineNumber);
            command.Parameters.AddWithValue("@ProductId",line.ProductId);
            command.Parameters.AddWithValue("@Description",line.Description);
            AddDecimal(command,"@Quantity",line.Quantity,19,6);
            AddDecimal(command,"@UnitCost",line.UnitCost,19,6);
            AddMoney(command,"@Discount",line.DiscountAmount);
            command.Parameters.AddWithValue("@TaxCode",line.TaxCode);
            AddDecimal(command,"@TaxRate",line.TaxRate,9,6);
            command.Parameters.AddWithValue("@TaxTreatment",line.TaxTreatment);
            AddMoney(command,"@Net",line.NetAmount);AddMoney(command,"@Tax",line.TaxAmount);
            AddMoney(command,"@Total",line.LineTotal);
            AddDecimal(command,"@RecognizedCost",line.RecognizedUnitCost,19,6);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var job=new SqlCommand("""
            INSERT dbo.DocumentProcessingJobs
              (JobId,BusinessId,ProcessingSequence,DocumentId,DocumentType,Status,AvailableAt,CreatedAt)
            VALUES(@JobId,@BusinessId,@Sequence,@DocumentId,N'PurchaseReturn',N'Pending',@Now,@Now);
            INSERT dbo.DocumentProcessingPayloads
              (DocumentId,DocumentType,BusinessId,ContractVersion,PayloadJson,PayloadHash,AcceptedAt)
            VALUES(@DocumentId,N'PurchaseReturn',@BusinessId,1,@Payload,@PayloadHash,@Now);
            """,connection,transaction);
        job.Parameters.AddWithValue("@JobId",jobId);job.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        job.Parameters.AddWithValue("@Sequence",sequence);job.Parameters.AddWithValue("@DocumentId",request.ReturnId);
        job.Parameters.AddWithValue("@Now",now);job.Parameters.AddWithValue("@Payload",payload);
        job.Parameters.Add("@PayloadHash",SqlDbType.Binary,32).Value=payloadHash;
        await job.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddMoney(SqlCommand command,string name,decimal value)=>
        AddDecimal(command,name,value,19,4);
    private static void AddDecimal(SqlCommand command,string name,decimal value,byte precision,byte scale)
    {
        var parameter=command.Parameters.Add(name,SqlDbType.Decimal);
        parameter.Precision=precision;parameter.Scale=scale;parameter.Value=value;
    }
    private sealed record OriginalReceipt(Guid WarehouseId,Guid SupplierId,
        DateTimeOffset ReceivedAt,string CurrencyCode);
}
