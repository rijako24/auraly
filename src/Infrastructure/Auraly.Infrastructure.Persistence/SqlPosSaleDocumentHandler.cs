using System.Data;
using System.Text.Json;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Sales;
using Auraly.Domain.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPosSaleDocumentHandler : IConfirmedDocumentHandler
{
    private readonly SqlDocumentProcessingSessionAccessor _sessions;
    private readonly IAuralyIdGenerator _idGenerator;
    private readonly SqlInventoryLedgerWriter _inventoryWriter;
    private readonly TimeProvider _timeProvider;
    private readonly Auraly.Commerce.Taxation.Application.WithholdingService _taxation;

    public SqlPosSaleDocumentHandler(
        SqlDocumentProcessingSessionAccessor sessions,
        IAuralyIdGenerator idGenerator,
        SqlInventoryLedgerWriter inventoryWriter,
        TimeProvider timeProvider,
        Auraly.Commerce.Taxation.Application.WithholdingService taxation)
    {
        _sessions = sessions;
        _idGenerator = idGenerator;
        _inventoryWriter = inventoryWriter;
        _timeProvider = timeProvider;
        _taxation = taxation;
    }

    public string DocumentType => PosSaleDocumentTypes.Invoice;

    public async Task HandleAsync(
        ConfirmedDocument document,
        CancellationToken cancellationToken)
    {
        var legacyHabilitation = IsLegacyFiscalHabilitation(document.Payload);
        var request = PosSaleContractSerializer.Deserialize(document.Payload);
        if (request.DocumentId != document.DocumentId.Value ||
            request.TenantId != document.TenantId.Value ||
            request.BusinessId != document.BusinessId.Value)
        {
            throw new InvalidOperationException("The confirmed document envelope does not match its payload.");
        }

        var session = _sessions.Current;
        // Safety tombstone for already persisted payloads from the retired sales-based
        // habilitation route. They must never be reinterpreted as economic sales.
        if (legacyHabilitation)
        {
            await MarkDocumentProcessedAsync(session, request, cancellationToken);
            return;
        }
        await ValidateWorkSessionAsync(session, request, cancellationToken);
        var inventoryWarehouseId = await ResolveInventoryWarehouseAsync(
            session, request, cancellationToken);
        await _inventoryWriter.PostBatchAsync(session, request.Lines.OrderBy(line => line.LineNumber)
            .Where(line => !line.IsGenericProductSnapshot).Select(line => new InventoryLedgerPosting(
                request.BusinessId, inventoryWarehouseId, line.ProductId, request.DocumentId,
                request.CommercialSnapshot.DocumentType, line.LineNumber, "Sale", -line.Quantity, null,
                InventoryValuationMode.AverageCost, request.CommercialSnapshot.IssuedAt)).ToArray(), cancellationToken);
        await InsertLinesAsync(session, request, cancellationToken);

        await SqlExpenseStore.AcceptInvoiceChargesAsync(session.Connection, session.Transaction,
            request, _taxation, _idGenerator, _timeProvider.GetUtcNow(), cancellationToken);
        await LinkSourceOrderAsync(session, request, cancellationToken);

        await PersistWithholdingSnapshotAsync(session, request, cancellationToken);

        await InsertPaymentsAsync(session, request, cancellationToken);

        await SqlAccountingPostingJobWriter.InsertAsync(
            session, document, request.CommercialSnapshot.IssuedAt,
            _idGenerator, _timeProvider, cancellationToken,
            AccountingJobRequirement.PreserveCommercialEffects);
        await SqlSalesReportingJobWriter.InsertAsync(
            session, document, _idGenerator, _timeProvider, cancellationToken);
        await InsertOutboxAsync(session, request, document.Payload, cancellationToken);
        await MarkDocumentProcessedAsync(session, request, cancellationToken);
    }

    private static bool IsLegacyFiscalHabilitation(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        return (json.RootElement.TryGetProperty("fiscalHabilitationOnly", out var camel) &&
                camel.ValueKind == JsonValueKind.True) ||
               (json.RootElement.TryGetProperty("FiscalHabilitationOnly", out var pascal) &&
                pascal.ValueKind == JsonValueKind.True);
    }

    private static async Task PersistWithholdingSnapshotAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        CancellationToken cancellationToken)
    {
        var withholding = request.CommercialSnapshot.Withholding;
        if (withholding is null) return;
        await using (var command = new SqlCommand("""
            INSERT dbo.DocumentWithholdingSnapshots
              (DocumentId,DocumentType,BusinessId,GrossAmount,WithholdingTotal,NetAmount,RecognizedAt)
            VALUES
              (@DocumentId,@DocumentType,@BusinessId,@Gross,@Withholding,@Net,@RecognizedAt);
            """, session.Connection, session.Transaction))
        {
            command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
            command.Parameters.AddWithValue("@DocumentType", request.CommercialSnapshot.DocumentType);
            command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
            AddDecimal(command, "@Gross", withholding.GrossAmount, 19, 4);
            AddDecimal(command, "@Withholding", withholding.WithholdingTotal, 19, 4);
            AddDecimal(command, "@Net", withholding.NetAmount, 19, 4);
            command.Parameters.AddWithValue("@RecognizedAt", request.CommercialSnapshot.IssuedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (withholding.Lines.Count > 0)
        {
            await using var command = new SqlCommand("""
                INSERT dbo.DocumentWithholdingLines
                  (DocumentId,DocumentType,LineNumber,RuleId,RuleVersion,RuleCode,Name,
                   Kind,BaseKind,TaxableBase,Rate,Amount,JurisdictionCode)
                SELECT @DocumentId,@DocumentType,CONVERT(int,j.[key])+1,line.RuleId,line.RuleVersion,line.RuleCode,line.Name,
                   line.Kind,line.BaseKind,line.TaxableBase,line.Rate,line.Amount,line.JurisdictionCode
                FROM OPENJSON(@Lines) j CROSS APPLY OPENJSON(j.value) WITH(
                  RuleId uniqueidentifier,RuleVersion int,RuleCode nvarchar(64),Name nvarchar(200),
                  Kind nvarchar(32),BaseKind nvarchar(32),TaxableBase decimal(19,4),Rate decimal(9,6),
                  Amount decimal(19,4),JurisdictionCode nvarchar(16)) line;
                """, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
            command.Parameters.AddWithValue("@DocumentType", request.CommercialSnapshot.DocumentType);
            command.Parameters.Add("@Lines", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(withholding.Lines);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != withholding.Lines.Count)
                throw new DBConcurrencyException("The complete sale withholding snapshot was not persisted.");
        }
    }

    private static async Task InsertLinesAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.SalesDocumentLines
            (
                DocumentId, LineNumber, ProductId, Description, TaxCode, TaxRate,
                Quantity, UnitPrice, UnitCostSnapshot, DiscountAmount, PromotionDiscountAmount, TaxAmount,
                UntaxedAmount, LineTotal,ProductCodeSnapshot,ProductNameSnapshot,
                CategoryIdSnapshot,CategoryNameSnapshot,SupplierIdSnapshot,SupplierNameSnapshot,
                AttributionSnapshotVersion,IsGenericProductSnapshot
            )
            SELECT
                @DocumentId,input.LineNumber,input.ProductId,input.Description,input.TaxCode,input.TaxRate,
                input.Quantity,input.UnitPrice,input.DocumentUnitCost,
                input.DiscountAmount,input.PromotionDiscountAmount,input.TaxAmount,
                input.UntaxedAmount,input.LineTotal,
                COALESCE(NULLIF(input.ProductCodeSnapshot,N''),p.ProductCode,p.Sku,p.Reference,N''),input.Description,
                p.ProductCategoryId,COALESCE(category.Name,p.CategoryName),supplier.SupplierId,supplier.Name,
                1,input.IsGenericProductSnapshot
            FROM OPENJSON(@LinesJson) WITH(
              LineNumber int '$.LineNumber',ProductId uniqueidentifier '$.ProductId',
              Description nvarchar(300) '$.Description',TaxCode nvarchar(16) '$.TaxCode',
              TaxRate decimal(9,6) '$.TaxRate',Quantity decimal(19,6) '$.Quantity',
              UnitPrice decimal(19,4) '$.UnitPrice',DocumentUnitCost decimal(19,6) '$.DocumentUnitCost',
              DiscountAmount decimal(19,4) '$.DiscountAmount',
              PromotionDiscountAmount decimal(19,4) '$.PromotionDiscountAmount',
              TaxAmount decimal(19,4) '$.TaxAmount',UntaxedAmount decimal(19,4) '$.UntaxedAmount',
              LineTotal decimal(19,4) '$.LineTotal',
              IsGenericProductSnapshot bit '$.IsGenericProductSnapshot',
              ProductCodeSnapshot nvarchar(80) '$.ProductCodeSnapshot') input
            INNER JOIN dbo.Products p ON p.ProductId=input.ProductId
            LEFT JOIN dbo.ProductCategories category
              ON category.ProductCategoryId=p.ProductCategoryId
            OUTER APPLY
            (
              SELECT TOP(1) s.SupplierId,s.Name
              FROM dbo.SupplierProducts sp
              INNER JOIN dbo.Suppliers s
                ON s.SupplierId=sp.SupplierId AND s.BusinessId=sp.BusinessId AND s.IsActive=1
              WHERE sp.BusinessId=@BusinessId AND sp.ProductId=input.ProductId AND sp.IsActive=1
              ORDER BY sp.IsPrimary DESC,sp.CreatedAt,sp.SupplierProductId
            ) supplier
            WHERE (p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
                   OR (p.TenantId IS NULL AND p.BusinessId=@BusinessId));
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@LinesJson", JsonSerializer.Serialize(request.Lines));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != request.Lines.Count)
            throw new InvalidOperationException(
                "The immutable attribution for every sale line could not be captured.");
    }

    private static async Task<Guid> ResolveInventoryWarehouseAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SourceOrderId is null)
            return request.WarehouseId;

        const string sql = """
            SELECT orders.OrdersWarehouseId
            FROM dbo.Orders orders WITH(UPDLOCK,HOLDLOCK)
            WHERE orders.OrderId=@OrderId
              AND orders.BusinessId=@BusinessId
              AND orders.CustomerConfirmed=1
              AND orders.Status IN (2,4)
              AND orders.OrdersWarehouseId IS NOT NULL;
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@OrderId", request.SourceOrderId.Value);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        if (await command.ExecuteScalarAsync(cancellationToken) is not Guid warehouseId)
            throw new InvalidOperationException(
                "El pedido de origen no tiene una reserva de inventario válida en la bodega PED.");
        return warehouseId;
    }

    private async Task LinkSourceOrderAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SourceOrderId is null) return;
        const string sql = """
            IF EXISTS (
                SELECT 1 FROM dbo.OrderInvoiceLinks WITH (UPDLOCK,HOLDLOCK)
                WHERE OrderId=@OrderId
                  AND (BusinessId<>@BusinessId OR DocumentId<>@DocumentId))
                THROW 51000, 'El pedido de origen ya esta vinculado a otro documento.', 1;

            IF NOT EXISTS (
                SELECT 1 FROM dbo.OrderInvoiceLinks WITH (UPDLOCK,HOLDLOCK)
                WHERE OrderId=@OrderId AND BusinessId=@BusinessId AND DocumentId=@DocumentId)
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM dbo.Orders WITH (UPDLOCK,HOLDLOCK)
                    WHERE OrderId=@OrderId AND BusinessId=@BusinessId
                      AND CustomerConfirmed=1 AND Status IN (2,4)
                      AND OrdersWarehouseId IS NOT NULL)
                    THROW 51000, 'El pedido de origen no esta disponible en este negocio.', 1;

                INSERT INTO dbo.OrderInvoiceLinks
                    (OrderInvoiceLinkId,BusinessId,OrderId,DocumentId,OperationId,CreatedAt)
                VALUES
                    (@LinkId,@BusinessId,@OrderId,@DocumentId,NULL,@CreatedAt);
            END

            UPDATE dbo.OrderClaims
            SET ReleasedAt=COALESCE(ReleasedAt,@CreatedAt)
            WHERE OrderId=@OrderId AND ReleasedAt IS NULL;

            UPDATE dbo.Orders
            SET ExternalStatus=N'InventoryConsumedByInvoice',UpdatedAt=@CreatedAt
            WHERE OrderId=@OrderId AND BusinessId=@BusinessId;
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@LinkId", _idGenerator.NewId());
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@OrderId", request.SourceOrderId.Value);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@CreatedAt", _timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertPaymentsAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Payments.Count == 0) return;
        const string sql = """
            DECLARE @AccountingEnabled bit=CASE WHEN EXISTS(
              SELECT 1 FROM dbo.AccountingTenantSettings settings
              INNER JOIN dbo.Businesses business ON business.TenantId=settings.TenantId
              WHERE business.BusinessId=@BusinessId AND settings.Status=N'Ready') THEN 1 ELSE 0 END;
            INSERT INTO dbo.SalesPayments
            (
                DocumentId, PaymentNumber, MethodCode, Amount, RoundingAdjustment, TenderedAmount,
                Reference, Notes, CardFranchiseCode, ApprovalNumber, BankAccountId, RegisteredAt
            )
            SELECT
                @DocumentId, input.PaymentNumber, input.MethodCode, input.Amount, input.RoundingAdjustment, input.TenderedAmount,
                input.Reference, input.Notes, input.CardFranchiseCode, input.ApprovalNumber, input.BankAccountId, @RegisteredAt
            FROM OPENJSON(@Payments) WITH(
              PaymentNumber int,MethodCode nvarchar(32),Amount decimal(19,4),RoundingAdjustment decimal(19,4),TenderedAmount decimal(19,4),
              Reference nvarchar(160),Notes nvarchar(500),CardFranchiseCode nvarchar(64),
              ApprovalNumber nvarchar(100),BankAccountId uniqueidentifier) input
            WHERE input.MethodCode<>N'Transfer' OR
              (@AccountingEnabled=0 AND input.BankAccountId IS NULL) OR
              (@AccountingEnabled=1 AND EXISTS(
                  SELECT 1 FROM accounting.BankAccounts bank
                  INNER JOIN dbo.Businesses business ON business.TenantId=bank.TenantId
                  INNER JOIN dbo.AccountingAccounts account
                    ON account.AccountId=bank.AccountingAccountId AND account.TenantId=bank.TenantId
                  WHERE bank.BankAccountId=input.BankAccountId
                    AND business.BusinessId=@BusinessId AND bank.IsActive=1
                    AND account.IsActive=1 AND account.AllowsPosting=1));
            IF @@ROWCOUNT<>@PaymentCount
                THROW 51000,N'The transfer bank account is not active for the sale tenant.',1;
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.Add("@Payments", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(request.Payments);
        command.Parameters.AddWithValue("@PaymentCount", request.Payments.Count);
        command.Parameters.AddWithValue("@RegisteredAt", request.CommercialSnapshot.IssuedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertOutboxAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        string payload,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.ServerOutboxMessages
            (
                MessageId, DocumentId, DocumentType, Type, Payload, OccurredAt
            )
            VALUES
            (
                @MessageId, @DocumentId, @DocumentType, @Type, @Payload, @OccurredAt
            );
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@MessageId", _idGenerator.NewId());
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", request.CommercialSnapshot.DocumentType);
        command.Parameters.AddWithValue("@Type", "sales.document.processed");
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.AddWithValue("@OccurredAt", _timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MarkDocumentProcessedAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.SalesDocuments
            SET ProcessingStatus = 'Completed',
                ProcessedAt = @ProcessedAt
            WHERE DocumentId = @DocumentId
              AND BusinessId = @BusinessId
              AND ((DocumentType = 'SalesInvoice' AND FiscalStatus IN ('FiscalVerified','DianAccepted'))
                   OR (DocumentType = 'SalesReceipt' AND FiscalStatus IS NULL))
              AND ProcessingStatus IN ('Received', 'Failed');
            IF @@ROWCOUNT<>1
                THROW 51022,N'La venta no pudo marcarse como procesada.',1;

            UPDATE draft
            SET Status=N'Consumed',ConsumedAt=@ProcessedAt,
                DeletedAt=NULL,UpdatedAt=@ProcessedAt
            FROM dbo.SalesDrafts draft
            JOIN dbo.OnlineSalesCheckoutReceipts receipt
              ON receipt.SalesDraftId=draft.SalesDraftId
             AND receipt.BusinessId=draft.BusinessId
            WHERE receipt.DocumentId=@DocumentId
              AND receipt.BusinessId=@BusinessId
              AND receipt.Status=N'FiscalConflict'
              AND draft.Status=N'Deleted';

            UPDATE dbo.OnlineSalesCheckoutReceipts
            SET Status=N'Completed',CompletedAt=@ProcessedAt
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId
              AND Status=N'FiscalConflict';
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@ProcessedAt", _timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddDecimal(
        SqlCommand command,
        string name,
        decimal value,
        byte precision,
        byte scale)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = value;
    }

}
public sealed class SqlSalesReceiptDocumentHandler(
    SqlPosSaleDocumentHandler sales) : IConfirmedDocumentHandler
{
    public string DocumentType => PosSaleDocumentTypes.Receipt;

    public Task HandleAsync(ConfirmedDocument document, CancellationToken cancellationToken) =>
        sales.HandleAsync(document, cancellationToken);
}

