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
        var inventoryWarehouseId = await ResolveInventoryWarehouseAsync(
            session, request, cancellationToken);
        foreach (var line in request.Lines.OrderBy(line => line.LineNumber))
        {
            await InsertLineAsync(session, request, line, cancellationToken);
            await InsertInventoryMovementAsync(
                session, request, inventoryWarehouseId, line, cancellationToken);
        }

        await LinkSourceOrderAsync(session, request, cancellationToken);

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
                Quantity, UnitPrice, DiscountAmount, TaxAmount,
                UntaxedAmount, LineTotal
            )
            VALUES
            (
                @DocumentId, @LineNumber, @ProductId, @Description, @TaxCode, @TaxRate,
                @Quantity, @UnitPrice, @DiscountAmount, @TaxAmount,
                @UntaxedAmount, @LineTotal
            );
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@LineNumber", line.LineNumber);
        command.Parameters.AddWithValue("@ProductId", line.ProductId);
        command.Parameters.AddWithValue("@Description", line.Description);
        command.Parameters.AddWithValue("@TaxCode", line.TaxCode);
        AddDecimal(command, "@TaxRate", line.TaxRate, 9, 6);
        AddDecimal(command, "@Quantity", line.Quantity, 19, 6);
        AddDecimal(command, "@UnitPrice", line.UnitPrice, 19, 4);
        AddDecimal(command, "@DiscountAmount", line.DiscountAmount, 19, 4);
        AddDecimal(command, "@TaxAmount", line.TaxAmount, 19, 4);
        AddDecimal(command, "@UntaxedAmount", line.UntaxedAmount, 19, 4);
        AddDecimal(command, "@LineTotal", line.LineTotal, 19, 4);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertInventoryMovementAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        Guid sourceWarehouseId,
        PosSaleLineContract line,
        CancellationToken cancellationToken)
    {
        if (sourceWarehouseId != request.WarehouseId)
        {
            var unitCost = await _inventoryWriter.PostAsync(
                session,
                new InventoryLedgerPosting(
                    request.BusinessId,
                    sourceWarehouseId,
                    line.ProductId,
                    request.DocumentId,
                    request.CommercialSnapshot.DocumentType,
                    line.LineNumber,
                    "TransferOut",
                    -line.Quantity,
                    null,
                    InventoryValuationModes.AverageCost,
                    request.CommercialSnapshot.IssuedAt),
                cancellationToken);
            await _inventoryWriter.PostAsync(
                session,
                new InventoryLedgerPosting(
                    request.BusinessId,
                    request.WarehouseId,
                    line.ProductId,
                    request.DocumentId,
                    request.CommercialSnapshot.DocumentType,
                    line.LineNumber,
                    "TransferIn",
                    line.Quantity,
                    unitCost,
                    InventoryValuationModes.WeightedAverageReceipt,
                    request.CommercialSnapshot.IssuedAt),
                cancellationToken);
        }

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

    private static async Task<Guid> ResolveInventoryWarehouseAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SourceOrderId is null) return request.WarehouseId;
        const string sql = """
            SELECT ExternalStatus,
                   TRY_CONVERT(uniqueidentifier,JSON_VALUE(CustomAttributesJson,'$.ordersWarehouseId'))
            FROM dbo.Orders WITH(UPDLOCK,HOLDLOCK)
            WHERE OrderId=@OrderId AND BusinessId=@BusinessId;
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@OrderId", request.SourceOrderId.Value);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("El pedido de origen no existe en este negocio.");
        var released = !reader.IsDBNull(0) &&
            string.Equals(reader.GetString(0), "InventoryReleasedForInvoice", StringComparison.Ordinal);
        return released || reader.IsDBNull(1) ? request.WarehouseId : reader.GetGuid(1);
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
                  AND CustomerConfirmed=1 AND Status IN (2,4))
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
                Reference, RegisteredAt
            )
            VALUES
            (
                @DocumentId, @PaymentNumber, @MethodCode, @Amount,
                @Reference, @RegisteredAt
            );
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@PaymentNumber", payment.PaymentNumber);
        command.Parameters.AddWithValue("@MethodCode", payment.MethodCode);
        AddDecimal(command, "@Amount", payment.Amount, 19, 4);
        command.Parameters.AddWithValue("@Reference", (object?)payment.Reference ?? DBNull.Value);
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

