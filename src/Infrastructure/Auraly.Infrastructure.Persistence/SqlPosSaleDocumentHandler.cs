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
    private readonly TimeProvider _timeProvider;

    public SqlPosSaleDocumentHandler(
        SqlDocumentProcessingSessionAccessor sessions,
        IAuralyIdGenerator idGenerator,
        TimeProvider timeProvider)
    {
        _sessions = sessions;
        _idGenerator = idGenerator;
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
        var cashResponsibility = await EnsureCashResponsibilityAsync(
            session, request, cancellationToken);
        foreach (var line in request.Lines.OrderBy(line => line.LineNumber))
        {
            await InsertLineAsync(session, request, line, cancellationToken);
            await InsertInventoryMovementAsync(session, request, line, cancellationToken);
        }

        await InsertTaxSummariesAsync(session, request, _timeProvider.GetUtcNow(), cancellationToken);
        await LinkSourceOrderAsync(session, request, cancellationToken);

        foreach (var payment in request.Payments.OrderBy(payment => payment.PaymentNumber))
        {
            await InsertPaymentAsync(session, request, payment, cancellationToken);
            await InsertCashMovementAsync(
                session, request, payment, cashResponsibility, cancellationToken);
        }

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

    private static async Task InsertTaxSummariesAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.SalesDocumentTaxSummaries
            (
                DocumentId, TaxCode, TaxRate, TaxableAmount,
                TaxAmount, TotalAmount, CreatedAt
            )
            VALUES
            (
                @DocumentId, @TaxCode, @TaxRate, @TaxableAmount,
                @TaxAmount, @TotalAmount, @CreatedAt
            );
            """;
        var summaries = request.Lines
            .GroupBy(line => new { line.TaxCode, line.TaxRate })
            .OrderBy(group => group.Key.TaxCode, StringComparer.Ordinal)
            .ThenBy(group => group.Key.TaxRate);
        foreach (var summary in summaries)
        {
            await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
            command.Parameters.AddWithValue("@TaxCode", summary.Key.TaxCode);
            AddDecimal(command, "@TaxRate", summary.Key.TaxRate, 9, 6);
            AddDecimal(command, "@TaxableAmount", summary.Sum(line => line.UntaxedAmount), 19, 4);
            AddDecimal(command, "@TaxAmount", summary.Sum(line => line.TaxAmount), 19, 4);
            AddDecimal(command, "@TotalAmount", summary.Sum(line => line.LineTotal), 19, 4);
            command.Parameters.AddWithValue("@CreatedAt", createdAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task InsertInventoryMovementAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        PosSaleLineContract line,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @ManageStock BIT;
            SELECT @ManageStock = p.ManageStock
            FROM dbo.Products p WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN dbo.Warehouses w WITH (UPDLOCK, HOLDLOCK)
                ON w.WarehouseId = @WarehouseId
               AND w.BusinessId = @BusinessId
            WHERE p.ProductId = @ProductId
              AND p.BusinessId = @BusinessId;

            IF @ManageStock IS NULL
                THROW 51000, 'El producto o la bodega no pertenecen al negocio del documento.', 1;

            DECLARE @QuantityBefore DECIMAL(19,6) = NULL;
            DECLARE @QuantityAfter DECIMAL(19,6) = NULL;
            DECLARE @AverageCost DECIMAL(19,6) = NULL;
            DECLARE @ValueBefore DECIMAL(19,4) = NULL;
            DECLARE @ValueAfter DECIMAL(19,4) = NULL;
            DECLARE @ValueChange DECIMAL(19,4) = NULL;

            IF @ManageStock = 1
            BEGIN
            IF NOT EXISTS
            (
                SELECT 1
                FROM dbo.InventoryBalances WITH (UPDLOCK, HOLDLOCK)
                WHERE BusinessId = @BusinessId
                  AND WarehouseId = @WarehouseId
                  AND ProductId = @ProductId
            )
            BEGIN
                INSERT INTO dbo.InventoryBalances
                (
                    BusinessId, WarehouseId, ProductId, QuantityOnHand,
                    AverageUnitCost, InventoryValue, LastProcessingSequence, UpdatedAt
                )
                VALUES
                (
                    @BusinessId, @WarehouseId, @ProductId, 0,
                    0, 0, @ProcessingSequence, @PostedAt
                );
            END;

            SELECT @QuantityBefore = QuantityOnHand,
                   @AverageCost = AverageUnitCost,
                   @ValueBefore = InventoryValue
            FROM dbo.InventoryBalances WITH (UPDLOCK, HOLDLOCK)
            WHERE BusinessId = @BusinessId
              AND WarehouseId = @WarehouseId
              AND ProductId = @ProductId;

            SET @QuantityAfter = @QuantityBefore + @QuantityChange;
            SET @ValueChange = CAST(@QuantityChange * @AverageCost AS DECIMAL(19,4));
            SET @ValueAfter = @ValueBefore + @ValueChange;

            UPDATE dbo.InventoryBalances
            SET QuantityOnHand = @QuantityAfter,
                InventoryValue = @ValueAfter,
                LastProcessingSequence = @ProcessingSequence,
                UpdatedAt = @PostedAt
            WHERE BusinessId = @BusinessId
              AND WarehouseId = @WarehouseId
              AND ProductId = @ProductId;
            END;

            IF @ManageStock = 0
                RETURN;

            INSERT INTO dbo.InventoryMovements
            (
                InventoryMovementId, BusinessId, WarehouseId,
                DocumentId, LineNumber, ProductId, MovementType,
                QuantityChange, ProcessingSequence, QuantityBefore, QuantityAfter,
                AverageUnitCostBefore, AverageUnitCostAfter, RecognizedUnitCost,
                ValueChange, OccurredAt, PostedAt, CreatedAt
            )
            VALUES
            (
                @InventoryMovementId, @BusinessId, @WarehouseId,
                @DocumentId, @LineNumber, @ProductId, 'Sale',
                @QuantityChange, @ProcessingSequence, @QuantityBefore, @QuantityAfter,
                @AverageCost, @AverageCost, @AverageCost,
                @ValueChange, @OccurredAt, @PostedAt, @PostedAt
            );
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@InventoryMovementId", _idGenerator.NewId());
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@LineNumber", line.LineNumber);
        command.Parameters.AddWithValue("@ProductId", line.ProductId);
        AddDecimal(command, "@QuantityChange", -line.Quantity, 19, 6);
        command.Parameters.AddWithValue("@ProcessingSequence", session.ProcessingSequence);
        command.Parameters.AddWithValue("@OccurredAt", request.FiscalSnapshot.IssuedAt);
        command.Parameters.AddWithValue("@PostedAt", _timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
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

            INSERT INTO dbo.OrderInvoiceLinks
                (OrderInvoiceLinkId,BusinessId,OrderId,DocumentId,OperationId,CreatedAt)
            VALUES
                (@LinkId,@BusinessId,@OrderId,@DocumentId,NULL,@CreatedAt);

            UPDATE dbo.OrderClaims
            SET ReleasedAt=COALESCE(ReleasedAt,@CreatedAt)
            WHERE OrderId=@OrderId AND ReleasedAt IS NULL;
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
        command.Parameters.AddWithValue("@RegisteredAt", request.FiscalSnapshot.IssuedAt);
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
                MessageId, DocumentId, Type, Payload, OccurredAt
            )
            VALUES
            (
                @MessageId, @DocumentId, @Type, @Payload, @OccurredAt
            );
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@MessageId", _idGenerator.NewId());
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@Type", "sales.invoice.processed");
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
              AND FiscalStatus = 'FiscalVerified'
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

