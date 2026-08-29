using System.Data;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPosSaleDocumentHandler : IConfirmedDocumentHandler
{
    private readonly SqlDocumentProcessingSessionAccessor _sessions;
    private readonly IAuralyIdGenerator _idGenerator;
    private readonly SqlInventoryLedgerWriter _inventoryWriter;
    private readonly TimeProvider _timeProvider;

    public SqlPosSaleDocumentHandler(
        SqlDocumentProcessingSessionAccessor sessions,
        IAuralyIdGenerator idGenerator,
        SqlInventoryLedgerWriter inventoryWriter,
        TimeProvider timeProvider)
    {
        _sessions = sessions;
        _idGenerator = idGenerator;
        _inventoryWriter = inventoryWriter;
        _timeProvider = timeProvider;
    }

    public string DocumentType => PosSaleDocumentTypes.Invoice;

    public async Task HandleAsync(
        ConfirmedDocument document,
        CancellationToken cancellationToken)
    {
        var request = PosSaleContractSerializer.Deserialize(document.Payload);
        if (request.DocumentId != document.DocumentId.Value ||
            request.TenantId != document.TenantId.Value ||
            request.BusinessId != document.BusinessId.Value)
        {
            throw new InvalidOperationException("The confirmed document envelope does not match its payload.");
        }

        var session = _sessions.Current;
        if (request.FiscalHabilitationOnly)
        {
            await MarkDocumentProcessedAsync(session, request, cancellationToken);
            return;
        }
        await EnsureWorkSessionAsync(
            session, request, cancellationToken);
        foreach (var line in request.Lines.OrderBy(line => line.LineNumber))
        {
            await ValidateDocumentCostAsync(session, request.BusinessId, line, cancellationToken);
            await InsertInventoryMovementAsync(
                session, request, line, cancellationToken);
            await InsertLineAsync(session, request, line, cancellationToken);
        }

        await LinkSourceOrderAsync(session, request, cancellationToken);

        await PersistWithholdingSnapshotAsync(session, request, cancellationToken);

        foreach (var payment in request.Payments.OrderBy(payment => payment.PaymentNumber))
        {
            await InsertPaymentAsync(session, request, payment, cancellationToken);
        }

        await SqlAccountingPostingJobWriter.InsertAsync(
            session, document, request.CommercialSnapshot.IssuedAt,
            _idGenerator, _timeProvider, cancellationToken);
        await SqlSalesReportingJobWriter.InsertAsync(
            session, document, _idGenerator, _timeProvider, cancellationToken);
        await InsertOutboxAsync(session, request, document.Payload, cancellationToken);
        await MarkDocumentProcessedAsync(session, request, cancellationToken);
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

        foreach (var item in withholding.Lines.Select((line, index) => (line, index)))
        {
            await using var command = new SqlCommand("""
                INSERT dbo.DocumentWithholdingLines
                  (DocumentId,DocumentType,LineNumber,RuleId,RuleVersion,RuleCode,Name,
                   Kind,BaseKind,TaxableBase,Rate,Amount,JurisdictionCode)
                VALUES
                  (@DocumentId,@DocumentType,@LineNumber,@RuleId,@RuleVersion,@RuleCode,@Name,
                   @Kind,@BaseKind,@TaxableBase,@Rate,@Amount,@JurisdictionCode);
                """, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
            command.Parameters.AddWithValue("@DocumentType", request.CommercialSnapshot.DocumentType);
            command.Parameters.AddWithValue("@LineNumber", item.index + 1);
            command.Parameters.AddWithValue("@RuleId", item.line.RuleId);
            command.Parameters.AddWithValue("@RuleVersion", item.line.RuleVersion);
            command.Parameters.AddWithValue("@RuleCode", item.line.RuleCode);
            command.Parameters.AddWithValue("@Name", item.line.Name);
            command.Parameters.AddWithValue("@Kind", item.line.Kind);
            command.Parameters.AddWithValue("@BaseKind", item.line.BaseKind);
            AddDecimal(command, "@TaxableBase", item.line.TaxableBase, 19, 4);
            AddDecimal(command, "@Rate", item.line.Rate, 9, 6);
            AddDecimal(command, "@Amount", item.line.Amount, 19, 4);
            command.Parameters.AddWithValue("@JurisdictionCode", (object?)item.line.JurisdictionCode ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertLineAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        PosSaleLineContract line,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.SalesDocumentLines
            (
                DocumentId, LineNumber, ProductId, Description, TaxCode, TaxRate,
                Quantity, UnitPrice, UnitCostSnapshot, DiscountAmount, TaxAmount,
                UntaxedAmount, LineTotal,ProductCodeSnapshot,ProductNameSnapshot,
                CategoryIdSnapshot,CategoryNameSnapshot,SupplierIdSnapshot,SupplierNameSnapshot,
                AttributionSnapshotVersion
            )
            SELECT
                @DocumentId, @LineNumber, @ProductId, @Description, @TaxCode, @TaxRate,
                @Quantity, @UnitPrice,
                COALESCE(@UnitCostSnapshot,CASE WHEN @Quantity=0 THEN 0 ELSE COALESCE(ABS(movement.ValueChange)/@Quantity,0) END),
                @DiscountAmount, @TaxAmount,
                @UntaxedAmount, @LineTotal,COALESCE(p.ProductCode,p.Sku,p.Reference,N''),p.Name,
                p.ProductCategoryId,COALESCE(category.Name,p.CategoryName),supplier.SupplierId,supplier.Name,
                1
            FROM dbo.Products p
            LEFT JOIN dbo.ProductCategories category
              ON category.ProductCategoryId=p.ProductCategoryId
            OUTER APPLY
            (
              SELECT TOP(1) s.SupplierId,s.Name
              FROM dbo.SupplierProducts sp
              INNER JOIN dbo.Suppliers s
                ON s.SupplierId=sp.SupplierId AND s.BusinessId=sp.BusinessId AND s.IsActive=1
              WHERE sp.BusinessId=@BusinessId AND sp.ProductId=p.ProductId AND sp.IsActive=1
              ORDER BY sp.IsPrimary DESC,sp.CreatedAt,sp.SupplierProductId
            ) supplier
            OUTER APPLY
            (
              SELECT TOP(1) movement.ValueChange
              FROM dbo.InventoryMovements movement
              WHERE movement.DocumentId=@DocumentId AND movement.DocumentType=@DocumentType
                AND movement.LineNumber=@LineNumber AND movement.MovementType=N'Sale'
            ) movement
            WHERE p.ProductId=@ProductId
              AND (p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
                   OR (p.TenantId IS NULL AND p.BusinessId=@BusinessId));
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", request.CommercialSnapshot.DocumentType);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@LineNumber", line.LineNumber);
        command.Parameters.AddWithValue("@ProductId", line.ProductId);
        command.Parameters.AddWithValue("@Description", line.Description);
        command.Parameters.AddWithValue("@TaxCode", line.TaxCode);
        AddDecimal(command, "@TaxRate", line.TaxRate, 9, 6);
        AddDecimal(command, "@Quantity", line.Quantity, 19, 6);
        AddDecimal(command, "@UnitPrice", line.UnitPrice, 19, 4);
        var unitCost = command.Parameters.Add("@UnitCostSnapshot", SqlDbType.Decimal);
        unitCost.Precision = 19;
        unitCost.Scale = 6;
        unitCost.Value = (object?)line.DocumentUnitCost ?? DBNull.Value;
        AddDecimal(command, "@DiscountAmount", line.DiscountAmount, 19, 4);
        AddDecimal(command, "@TaxAmount", line.TaxAmount, 19, 4);
        AddDecimal(command, "@UntaxedAmount", line.UntaxedAmount, 19, 4);
        AddDecimal(command, "@LineTotal", line.LineTotal, 19, 4);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException(
                $"The immutable attribution for sale line {line.LineNumber} could not be captured.");
    }

    private async Task InsertInventoryMovementAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        PosSaleLineContract line,
        CancellationToken cancellationToken)
    {
        await _inventoryWriter.PostAsync(
            session,
            new InventoryLedgerPosting(
                request.BusinessId,
                request.WarehouseId,
                line.ProductId,
                request.DocumentId,
                request.CommercialSnapshot.DocumentType,
                line.LineNumber,
                "Sale",
                -line.Quantity,
                null,
                InventoryValuationModes.AverageCost,
                request.CommercialSnapshot.IssuedAt),
            cancellationToken);
    }

    private static async Task ValidateDocumentCostAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        Guid businessId,
        PosSaleLineContract line,
        CancellationToken cancellationToken)
    {
        if (line.DocumentUnitCost is null) return;
        if (line.DocumentUnitCost < 0)
            throw new InvalidOperationException("The document unit cost cannot be negative.");
        await using var command = new SqlCommand(
            "SELECT ManageStock FROM dbo.Products WHERE ProductId=@ProductId AND IsActive=1 AND (TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId) OR (TenantId IS NULL AND BusinessId=@BusinessId));",
            session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@ProductId", line.ProductId);
        if (await command.ExecuteScalarAsync(cancellationToken) is not bool managesStock)
            throw new InvalidOperationException("The sale product is not active in this business.");
        if (managesStock)
            throw new InvalidOperationException(
                "The cost of an inventory-managed product must come from inventory valuation.");
    }

    private async Task LinkSourceOrderAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SourceOrderId is null) return;
        const string sql = """
            IF NOT EXISTS (
                SELECT 1 FROM dbo.Orders WITH (UPDLOCK,HOLDLOCK)
                WHERE OrderId=@OrderId AND BusinessId=@BusinessId
                  AND CustomerConfirmed=1 AND Status IN (2,4)
                  AND ExternalStatus=N'InventoryReleasedForInvoice')
                THROW 51000, 'El pedido de origen no esta disponible en este negocio.', 1;

            IF EXISTS (
                SELECT 1 FROM dbo.OrderInvoiceLinks WITH (UPDLOCK,HOLDLOCK)
                WHERE OrderId=@OrderId
                  AND (BusinessId<>@BusinessId OR DocumentId<>@DocumentId))
                THROW 51000, 'El pedido de origen ya esta vinculado a otro documento.', 1;

            IF NOT EXISTS (
                SELECT 1 FROM dbo.OrderInvoiceLinks WITH (UPDLOCK,HOLDLOCK)
                WHERE OrderId=@OrderId AND BusinessId=@BusinessId AND DocumentId=@DocumentId)
                INSERT INTO dbo.OrderInvoiceLinks
                    (OrderInvoiceLinkId,BusinessId,OrderId,DocumentId,OperationId,CreatedAt)
                VALUES
                    (@LinkId,@BusinessId,@OrderId,@DocumentId,NULL,@CreatedAt);

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

    private static async Task InsertPaymentAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        PosSalePaymentContract payment,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.SalesPayments
            (
                DocumentId, PaymentNumber, MethodCode, Amount,
                Reference, CardFranchiseCode, ApprovalNumber, RegisteredAt
            )
            VALUES
            (
                @DocumentId, @PaymentNumber, @MethodCode, @Amount,
                @Reference, @CardFranchiseCode, @ApprovalNumber, @RegisteredAt
            );
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@PaymentNumber", payment.PaymentNumber);
        command.Parameters.AddWithValue("@MethodCode", payment.MethodCode);
        AddDecimal(command, "@Amount", payment.Amount, 19, 4);
        command.Parameters.AddWithValue("@Reference", (object?)payment.Reference ?? DBNull.Value);
        command.Parameters.AddWithValue("@CardFranchiseCode", (object?)payment.CardFranchiseCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@ApprovalNumber", (object?)payment.ApprovalNumber ?? DBNull.Value);
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
              AND ((DocumentType = 'SalesInvoice' AND FiscalStatus = 'FiscalVerified')
                   OR (DocumentType = 'SalesReceipt' AND FiscalStatus IS NULL))
              AND ProcessingStatus IN ('Received', 'Failed');
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@ProcessedAt", _timeProvider.GetUtcNow());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new DBConcurrencyException("The sale document could not be marked as processed.");
        }
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

