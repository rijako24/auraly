using System.Data;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPosSaleDocumentHandler(
    SqlDocumentProcessingSessionAccessor sessions,
    IAuralyIdGenerator idGenerator,
    TimeProvider timeProvider)
    : IConfirmedDocumentHandler
{
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

        var session = sessions.Current;
        foreach (var line in request.Lines.OrderBy(line => line.LineNumber))
        {
            await InsertLineAsync(session, request, line, cancellationToken);
            await InsertInventoryMovementAsync(session, request, line, cancellationToken);
        }

        foreach (var payment in request.Payments.OrderBy(payment => payment.PaymentNumber))
        {
            await InsertPaymentAsync(session, request, payment, cancellationToken);
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
                DocumentId, LineNumber, ProductId, Description, TaxCode,
                Quantity, UnitPrice, DiscountAmount, TaxAmount,
                UntaxedAmount, LineTotal
            )
            VALUES
            (
                @DocumentId, @LineNumber, @ProductId, @Description, @TaxCode,
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
        PosSaleLineContract line,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.InventoryMovements
            (
                InventoryMovementId, TenantId, BusinessId, WarehouseId,
                DocumentId, LineNumber, ProductId, MovementType,
                QuantityChange, OccurredAt, CreatedAt
            )
            VALUES
            (
                @InventoryMovementId, @TenantId, @BusinessId, @WarehouseId,
                @DocumentId, @LineNumber, @ProductId, 'Sale',
                @QuantityChange, @OccurredAt, @CreatedAt
            );
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@InventoryMovementId", idGenerator.NewId());
        command.Parameters.AddWithValue("@TenantId", request.TenantId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@LineNumber", line.LineNumber);
        command.Parameters.AddWithValue("@ProductId", line.ProductId);
        AddDecimal(command, "@QuantityChange", -line.Quantity, 19, 6);
        command.Parameters.AddWithValue("@OccurredAt", request.FiscalSnapshot.IssuedAt);
        command.Parameters.AddWithValue("@CreatedAt", timeProvider.GetUtcNow());
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
                MessageId, TenantId, DocumentId, Type, Payload, OccurredAt
            )
            VALUES
            (
                @MessageId, @TenantId, @DocumentId, @Type, @Payload, @OccurredAt
            );
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@MessageId", idGenerator.NewId());
        command.Parameters.AddWithValue("@TenantId", request.TenantId);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@Type", "sales.invoice.processed");
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.AddWithValue("@OccurredAt", timeProvider.GetUtcNow());
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
              AND TenantId = @TenantId
              AND FiscalStatus = 'FiscalVerified'
              AND ProcessingStatus IN ('Received', 'Failed');
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@TenantId", request.TenantId);
        command.Parameters.AddWithValue("@ProcessedAt", timeProvider.GetUtcNow());
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

