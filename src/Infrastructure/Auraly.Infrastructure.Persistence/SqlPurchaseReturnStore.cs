using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Purchasing;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
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
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await AcceptAttemptAsync(
                    user, idempotencyKey, request, requestHash, cancellationToken);
            }
            catch (SqlException exception) when (exception.Number == 1205 && attempt < 4)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(25 * attempt), timeProvider, cancellationToken);
            }
        }
    }

    private async Task<PurchaseReturnAcceptance> AcceptAttemptAsync(
        PurchasingUserIdentity user, string idempotencyKey,
        ConfirmPurchaseReturnRequest request, byte[] requestHash,
        CancellationToken cancellationToken)
    {
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
            var lines = await AllocateLinesAsync(connection, transaction, request, original,
                cancellationToken);
            var net=lines.Sum(line=>line.NetAmount);
            var tax=lines.Sum(line=>line.TaxAmount);
            var total=lines.Sum(line=>line.LineTotal);
            var now=timeProvider.GetUtcNow();
            SqlGoodsReceiptStore.SupportFiscalAllocation? supportAdjustment = null;
            if (original.Support is not null &&
                !await SqlDianDocumentQuota.TryReserveAsync(connection, transaction,
                    user.BusinessId, request.ReturnId, "SupportDocument", now,
                    cancellationToken))
                throw new PurchasingValidationException(
                    "No hay cupo de documentos DIAN para generar la nota de ajuste del documento soporte.");
            if (original.Support is not null)
                supportAdjustment = await SqlGoodsReceiptStore.AllocateSupportFiscalAsync(
                    connection, transaction, user.BusinessId, original.SupplierId,
                    request.ReturnedAt, now, cancellationToken);
            var number=await AllocateNumberAsync(connection,transaction,user.TenantId,user.BusinessId,now,cancellationToken);
            var sequence=await SqlOperationalDocumentAllocator.AllocateSequenceAsync(
                connection,transaction,user.BusinessId,now,cancellationToken);
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
            if (original.Support is not null)
                await InsertSupportAdjustmentFiscalAsync(connection, transaction,
                    payload, original.Support, supportAdjustment!, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PurchaseReturnAcceptance(request.ReturnId,jobId,number.FullNumber,
                "Accepted",sequence,false);
        }
        catch (Exception exception)
        {
            if (transaction.Connection?.State == ConnectionState.Open)
                await transaction.RollbackAsync(CancellationToken.None);
            if (exception is SqlException { Number: 2601 or 2627 })
                throw new PurchasingConflictException(
                    "The purchase return number, DocumentId or idempotency key is already in use.", exception);
            throw;
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
            SELECT r.WarehouseId,r.SupplierId,r.ReceivedAt,r.CurrencyCode,
                   fiscal.FiscalNumber,fiscal.UniqueCode,fiscal.IssuedAt,
                   fiscal.FiscalStatus,snapshot.SnapshotJson,payload.PayloadJson,
                   discountApplication.FinancialDiscountEarned,discountPayment.Status
            FROM dbo.GoodsReceipts r WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId AND b.TenantId=@TenantId
            LEFT JOIN dbo.Payables originalBalance WITH(UPDLOCK,HOLDLOCK)
              ON originalBalance.SourceDocumentId=r.GoodsReceiptId
                AND originalBalance.SourceDocumentType=N'GoodsReceipt'
                AND originalBalance.SourceLineId IS NULL AND originalBalance.BusinessId=@BusinessId
            LEFT JOIN dbo.SupplierPaymentApplications discountApplication WITH(UPDLOCK,HOLDLOCK)
              ON discountApplication.PayableId=originalBalance.PayableId
                AND discountApplication.FinancialDiscountEarned IS NOT NULL
            LEFT JOIN dbo.SupplierPayments discountPayment
              ON discountPayment.PaymentId=discountApplication.PaymentId
                AND discountPayment.BusinessId=@BusinessId
            LEFT JOIN dbo.FiscalDocuments fiscal
              ON fiscal.DocumentId=r.GoodsReceiptId
             AND fiscal.BusinessId=r.BusinessId
             AND fiscal.FiscalDocumentType=N'SupportDocument'
            LEFT JOIN fiscal.PurchaseSupportFiscalSnapshots snapshot
              ON snapshot.DocumentId=fiscal.DocumentId
            LEFT JOIN dbo.DocumentProcessingPayloads payload
              ON payload.DocumentId=r.GoodsReceiptId AND payload.DocumentType=N'GoodsReceipt'
                AND payload.BusinessId=r.BusinessId
            WHERE r.GoodsReceiptId=@Id AND r.BusinessId=@BusinessId AND r.Status=N'Processed';
            """,connection,transaction);
        command.Parameters.AddWithValue("@Id",request.OriginalGoodsReceiptId);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        command.Parameters.AddWithValue("@TenantId",user.TenantId);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        if(!await reader.ReadAsync(cancellationToken))
            throw new PurchasingValidationException(
                "The original goods receipt is not processed or is outside the authenticated business.");
        SupportDocumentReference? support = null;
        if (!reader.IsDBNull(4))
        {
            if (reader.IsDBNull(5) || reader.IsDBNull(8))
                throw new PurchasingValidationException(
                    "El documento soporte original todavía no termina su generación fiscal. Reintenta cuando tenga CUDS.");
            var status = reader.GetString(7);
            if (status is "DianRejected" or "PermanentFailure")
                throw new PurchasingValidationException(
                    "El documento soporte original fue rechazado o falló y no puede recibir una nota de ajuste.");
            support = new SupportDocumentReference(reader.GetString(4),
                reader.GetString(5), Auraly.Fiscal.Core.DianFiscalDateTime.DateInColombia(
                    reader.GetDateTimeOffset(6)),
                PurchaseSupportFiscalSnapshotSerializer.Deserialize(reader.GetString(8)));
        }
        var discount = reader.IsDBNull(9) ? null :
            GoodsReceiptContractSerializer.Deserialize(reader.GetString(9)).FinancialDiscount;
        if (!reader.IsDBNull(10) && discount is null)
            throw new PurchasingConflictException(
                "La devolución no se aceptó: falta el descuento congelado de la compra pagada.");
        if (!reader.IsDBNull(10) && (reader.IsDBNull(11) || reader.GetString(11)!="Processed"))
            throw new PurchasingConflictException(
                "La devolución no se aceptó: termina primero el pago que decidió el descuento financiero.");
        var value=new OriginalReceipt(reader.GetGuid(0),reader.GetGuid(1),
            reader.GetDateTimeOffset(2),reader.GetString(3),support,
            discount,reader.IsDBNull(10) ? null : reader.GetBoolean(10));
        if(value.CurrencyCode!="COP")
            throw new PurchasingValidationException(
                "Purchase returns currently require an original receipt in COP.");
        return value;
    }

    private static async Task<IReadOnlyList<PurchaseReturnLineSnapshot>> AllocateLinesAsync(
        SqlConnection connection, SqlTransaction transaction,
        ConfirmPurchaseReturnRequest request, OriginalReceipt original,
        CancellationToken cancellationToken)
    {
        var requestedLines = request.Lines.ToDictionary(line => line.OriginalLineNumber);
        var discountByLine = original.FinancialDiscount?.Lines.ToDictionary(
            line => line.ReceiptLineNumber) ?? [];
        var result=new List<PurchaseReturnLineSnapshot>(requestedLines.Count); var lineNumber=0;
        await using var command=new SqlCommand("""
                DECLARE @Requested TABLE(OriginalLineNumber int PRIMARY KEY);
                INSERT @Requested SELECT OriginalLineNumber FROM OPENJSON(@Lines)
                  WITH(OriginalLineNumber int);
                WITH revised AS
                (
                  SELECT allocation.ReceiptLineNumber,SUM(allocation.FunctionalAmount) Amount
                  FROM purchasing.GoodsReceiptCostDocuments cost
                  JOIN purchasing.GoodsReceiptCostRevisions revision
                    ON revision.RevisionId=cost.CostRevisionId
                  JOIN purchasing.GoodsReceiptCostAllocations allocation
                    ON allocation.CostDocumentId=cost.CostDocumentId
                  JOIN @Requested requested ON requested.OriginalLineNumber=allocation.ReceiptLineNumber
                  WHERE cost.GoodsReceiptId=@ReceiptId
                    AND revision.Status IN(N'Accepted',N'Processed')
                  GROUP BY allocation.ReceiptLineNumber
                )
                SELECT l.ProductId,l.DescriptionSnapshot,l.Quantity,l.UnitCost,
                       l.DiscountAmount,l.TaxCode,l.TaxRate,l.TaxTreatment,
                       l.NetAmount,l.TaxAmount,l.LineTotal,
                       COALESCE(SUM(prl.Quantity),0),COALESCE(SUM(prl.DiscountAmount),0),
                       COALESCE(SUM(prl.NetAmount),0),COALESCE(SUM(prl.TaxAmount),0),
                       COALESCE(SUM(prl.LineTotal),0),
                       l.RecognizedInventoryCostAmount+COALESCE(revised.Amount,0),
                       COALESCE(SUM(prl.FinancialDiscountAmount),0),requested.OriginalLineNumber
                FROM @Requested requested
                LEFT JOIN dbo.GoodsReceiptLines l WITH (UPDLOCK,HOLDLOCK)
                  ON l.GoodsReceiptId=@ReceiptId AND l.LineNumber=requested.OriginalLineNumber
                LEFT JOIN dbo.PurchaseReturnLines prl WITH (UPDLOCK,HOLDLOCK)
                  ON prl.OriginalGoodsReceiptId=l.GoodsReceiptId
                 AND prl.OriginalLineNumber=l.LineNumber
                LEFT JOIN revised ON revised.ReceiptLineNumber=l.LineNumber
                GROUP BY requested.OriginalLineNumber,l.ProductId,l.DescriptionSnapshot,l.Quantity,l.UnitCost,
                         l.DiscountAmount,l.TaxCode,l.TaxRate,l.TaxTreatment,
                         l.NetAmount,l.TaxAmount,l.LineTotal,l.RecognizedInventoryCostAmount,revised.Amount
                ORDER BY requested.OriginalLineNumber OPTION(RECOMPILE);
                """,connection,transaction);
        command.Parameters.AddWithValue("@ReceiptId",request.OriginalGoodsReceiptId);
        command.Parameters.Add("@Lines",SqlDbType.NVarChar,-1).Value = JsonSerializer.Serialize(
            request.Lines.Select(line => new { line.OriginalLineNumber }));
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))
        {
            var requested = requestedLines[reader.GetInt32(18)];
            if(reader.IsDBNull(0))
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
            var recognizedCost=decimal.Round(reader.GetDecimal(16)/reader.GetDecimal(2),6,
                MidpointRounding.AwayFromZero);
            var financialDiscount = 0m;
            var financialAdjustment = 0m;
            if (discountByLine.TryGetValue(requested.OriginalLineNumber, out var originalDiscount))
            {
                var originalNet = reader.GetDecimal(8);
                var alreadyReturned = reader.GetDecimal(13);
                var alreadyDiscounted = reader.GetDecimal(17);
                if (originalNet <= 0 || alreadyDiscounted > originalDiscount.Amount)
                    throw new PurchasingConflictException(
                        "El descuento financiero de la compra requiere revisión antes de devolver mercancía.");
                var completesLine = reader.GetDecimal(11) + allocation.Quantity == reader.GetDecimal(2);
                var targetDiscount = completesLine ? originalDiscount.Amount :
                    decimal.Round(originalDiscount.Amount *
                        (alreadyReturned + allocation.NetAmount) / originalNet, 4,
                        MidpointRounding.AwayFromZero);
                var previousTarget = decimal.Round(originalDiscount.Amount * alreadyReturned / originalNet,
                    4, MidpointRounding.AwayFromZero);
                var priorDiscount = original.FinancialDiscount!.AnticipatedInCost
                    ? alreadyDiscounted : previousTarget;
                var currentDiscount = decimal.Min(originalDiscount.Amount, targetDiscount) - priorDiscount;
                if (currentDiscount < 0 || currentDiscount > allocation.NetAmount)
                    throw new PurchasingConflictException(
                        "El descuento financiero de la devolución no concilia con la compra.");
                financialDiscount = original.FinancialDiscount.AnticipatedInCost ? currentDiscount : 0m;
                if (original.FinancialDiscountEarned is { } earned)
                    financialAdjustment = (earned ? currentDiscount : 0m) - financialDiscount;
            }
            result.Add(new PurchaseReturnLineSnapshot(++lineNumber,
                requested.OriginalLineNumber,reader.GetGuid(0),reader.GetString(1),
                allocation.Quantity,reader.GetDecimal(3),allocation.DiscountAmount,
                reader.GetString(5),reader.GetDecimal(6),reader.GetString(7),
                allocation.NetAmount,allocation.TaxAmount,allocation.LineTotal,recognizedCost,
                financialDiscount,financialAdjustment));
        }
        return result;
    }

    private static async Task<AuralyDocumentNumberAssignment> AllocateNumberAsync(
        SqlConnection connection,SqlTransaction transaction,Guid tenantId,Guid businessId,
        DateTimeOffset now,CancellationToken cancellationToken)
    {
        try
        {
            return await SqlOperationalDocumentAllocator.AllocateExistingNumberAsync(
                connection,transaction,tenantId,businessId,AuralyDocumentTypes.PurchaseReturn,now,cancellationToken);
        }
        catch(SqlException exception) when(exception.Number==51730)
        {
            throw new PurchasingValidationException(
                "No active PurchaseReturn document series is configured for the business.", exception);
        }
        catch(SqlException exception) when(exception.Number==51731)
        {
            throw new PurchasingValidationException("The PurchaseReturn document series is exhausted.", exception);
        }
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
            INSERT dbo.PurchaseReturnLines
              (PurchaseReturnId,LineNumber,OriginalGoodsReceiptId,OriginalLineNumber,
               ProductId,DescriptionSnapshot,Quantity,UnitCost,DiscountAmount,TaxCode,
               TaxRate,TaxTreatment,NetAmount,TaxAmount,LineTotal,RecognizedUnitCost,FinancialDiscountAmount)
            SELECT @Id,LineNumber,@OriginalId,OriginalLineNumber,ProductId,Description,
              Quantity,UnitCost,DiscountAmount,TaxCode,TaxRate,TaxTreatment,
              NetAmount,TaxAmount,LineTotal,RecognizedUnitCost,COALESCE(FinancialDiscountAmount,0)
            FROM OPENJSON(@Lines) WITH(LineNumber int,OriginalLineNumber int,ProductId uniqueidentifier,
              Description nvarchar(250),Quantity decimal(19,6),UnitCost decimal(19,6),DiscountAmount decimal(19,4),
              TaxCode nvarchar(32),TaxRate decimal(9,6),TaxTreatment nvarchar(32),NetAmount decimal(19,4),
              TaxAmount decimal(19,4),LineTotal decimal(19,4),RecognizedUnitCost decimal(19,6),
              FinancialDiscountAmount decimal(19,4));
            IF @@ROWCOUNT<>@LineCount THROW 50000,N'La devolución de compra quedó incompleta.',1;
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
            command.Parameters.Add("@Lines",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(lines);
            command.Parameters.AddWithValue("@LineCount",lines.Count);
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

    private static async Task InsertSupportAdjustmentFiscalAsync(
        SqlConnection connection, SqlTransaction transaction,
        PurchaseReturnDocumentPayload adjustment,
        SupportDocumentReference original,
        SqlGoodsReceiptStore.SupportFiscalAllocation allocation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var originalMetadata = original.Snapshot.Lines.ToDictionary(line => line.LineNumber);
        var metadata = adjustment.Lines.Select(line =>
        {
            if (!originalMetadata.TryGetValue(line.OriginalLineNumber, out var item))
                throw new PurchasingValidationException(
                    $"La línea original {line.OriginalLineNumber} no tiene metadatos fiscales inmutables.");
            return item with { LineNumber = line.LineNumber };
        }).ToArray();
        var snapshot = new PurchaseSupportFiscalSnapshot(null,
            allocation.IssuerConfigurationId, allocation.FiscalNumber,
            allocation.Environment, allocation.QrValidationUrl, allocation.Seller,
            allocation.Authorization, metadata, Adjustment: adjustment,
            OriginalSupportNumber: original.FiscalNumber,
            OriginalSupportCuds: original.Cuds,
            OriginalSupportIssuedOn: original.IssuedOn,
            SellerPostalZone: allocation.SellerPostalZone);
        await SqlFiscalDocumentStore.InsertPurchaseSupportSourcesAsync(connection, transaction, adjustment.TenantId,
            [new(adjustment.ReturnId, adjustment.BusinessId, PurchasingDocumentTypes.PurchaseReturn,
                FiscalDocumentTypeCodes.SupportDocumentAdjustment, adjustment.DocumentNumber, allocation.FiscalNumber,
                adjustment.ReturnedAt, snapshot.FiscalIssuerConfigurationId, snapshot.Environment,
                PurchaseSupportFiscalSnapshotSerializer.Serialize(snapshot))], now, cancellationToken);
    }

    private static void AddMoney(SqlCommand command,string name,decimal value)=>
        AddDecimal(command,name,value,19,4);
    private static void AddDecimal(SqlCommand command,string name,decimal value,byte precision,byte scale)
    {
        var parameter=command.Parameters.Add(name,SqlDbType.Decimal);
        parameter.Precision=precision;parameter.Scale=scale;parameter.Value=value;
    }
    private sealed record OriginalReceipt(Guid WarehouseId,Guid SupplierId,
        DateTimeOffset ReceivedAt,string CurrencyCode,SupportDocumentReference? Support,
        GoodsReceiptFinancialDiscountSnapshot? FinancialDiscount,bool? FinancialDiscountEarned);
    private sealed record SupportDocumentReference(string FiscalNumber,string Cuds,
        DateOnly IssuedOn,PurchaseSupportFiscalSnapshot Snapshot);
}
