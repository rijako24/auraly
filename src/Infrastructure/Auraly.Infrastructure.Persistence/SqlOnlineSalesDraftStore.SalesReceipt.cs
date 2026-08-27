using System.Data;
using System.Globalization;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore
{
    public Task<PreparedOnlineSalesCheckout> PrepareAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey,
        FiscalVerificationMaterial? fiscalMaterial,
        CancellationToken cancellationToken) => request.DocumentType switch
    {
        PosSaleDocumentTypes.Invoice => PrepareInvoiceAsync(
            user, draftId, request, idempotencyKey,
            fiscalMaterial ?? throw new OnlineSalesDraftValidationException(
                "La factura electronica requiere material fiscal activo."),
            cancellationToken),
        PosSaleDocumentTypes.Receipt => PrepareSalesReceiptAsync(
            user, draftId, request, idempotencyKey, cancellationToken),
        _ => throw new OnlineSalesDraftValidationException(
            "El tipo de documento de venta no es valido.")
    };

    private async Task<PreparedOnlineSalesCheckout> PrepareSalesReceiptAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey,
        CancellationToken ct)
    {
        var requestHash = CheckoutHash(draftId, request);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        var state = await LockDraftAsync(connection, transaction, user, draftId, ct);
        var replay = await ReadCheckoutReceiptAsync(
            connection, transaction, user, draftId, idempotencyKey, requestHash, ct);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct);
            return replay;
        }

        DemandActiveVersion(state, request.ExpectedVersion);
        await DemandCustomerAllowsSalesReceiptAsync(
            connection, transaction, state.BusinessId, state.CustomerId, ct);
        var draft = await ReadDraftAsync(connection, transaction, draftId, ct);
        if (draft.Lines.Count == 0)
            throw new OnlineSalesDraftValidationException(
                "La venta debe tener al menos un producto.");
        var inventoryValidation = await ValidateInventoryAsync(
            connection, transaction, state, draftId, ct);
        if (!inventoryValidation.IsValid)
            throw new OnlineSalesDraftValidationException(
                "El inventario cambió y uno o más productos ya no tienen existencias suficientes. Ajusta sus cantidades o elimínalos antes de cobrar.");
        if (request.Payments.Sum(payment => payment.Amount) + (request.Credit?.Amount ?? 0m) != draft.PayableAmount)
            throw new OnlineSalesDraftValidationException(
                "Los pagos reales y el saldo financiado deben ser iguales al total de la venta.");
        await ValidateCreditAsync(
            connection, transaction, state.BusinessId, state.CustomerId, request.Credit, ct);

        var now = time.GetUtcNow();
        var series = await ReadSalesReceiptSeriesAsync(
            connection, transaction, state.BusinessId, ct);
        var consecutive = await ConsumeSalesReceiptNumberAsync(
            connection, transaction, series, now, ct);
        var number = AuralyDocumentNumberAssignment.Create(
            series.SeriesId, PosSaleDocumentTypes.Receipt, series.Prefix,
            series.SeriesCode, consecutive, series.Padding);
        var customerIdentification = await ResolveCustomerIdentificationAsync(
            connection, transaction, state.BusinessId, state.CustomerId, ct);
        var lines = draft.Lines.Select((line, index) => new PosSaleLineContract(
            index + 1, line.ProductId, line.Description, line.TaxCode,
            line.Quantity, line.UnitPrice, line.Discount, line.Tax,
            line.Net, line.Total, line.TaxRate,
            line.AllowsDocumentCostOverride ? line.DocumentUnitCost : null)).ToArray();
        var payments = request.Payments.Select((payment, index) => new PosSalePaymentContract(
            index + 1, payment.MethodCode, payment.Amount,
            string.IsNullOrWhiteSpace(payment.Reference) ? null : payment.Reference.Trim(),
            string.IsNullOrWhiteSpace(payment.CardFranchiseCode) ? null : payment.CardFranchiseCode.Trim(),
            string.IsNullOrWhiteSpace(payment.ApprovalNumber) ? null : payment.ApprovalNumber.Trim())).ToArray();
        var taxes = lines.GroupBy(line => line.TaxCode, StringComparer.Ordinal)
            .Select(group => new PosSaleTaxContract(
                group.Key, group.Sum(line => line.TaxAmount)))
            .OrderBy(tax => tax.Code, StringComparer.Ordinal).ToArray();
        var upload = new PosSaleUploadRequest(
            user.TenantId, state.BusinessId, state.WarehouseId, Guid.Empty,
            state.WorkSessionId, user.UserId, ids.NewId(),
            new PosSaleDocumentNumberContract(
                number.SeriesId, number.DocumentType, number.Prefix,
                number.SeriesCode, number.Consecutive, number.Padding, number.FullNumber),
            new PosSaleCommercialSnapshotContract(
                PosSaleDocumentTypes.Receipt, now, customerIdentification, taxes,
                draft.UntaxedAmount, draft.TaxAmount, draft.PayableAmount),
            null,
            lines,
            payments,
            null,
            state.CustomerId,
            SaleSourceModes.Online,
            draft.SourceOrderId,
            request.Credit is null || state.CustomerId is null ? null :
                new PosSaleCreditContract(
                    state.CustomerId.Value, request.Credit.Amount, request.Credit.DueDate));

        await ReleaseOrderInventoryAsync(connection, transaction, user, state, ct);

        var nextDraftId = ids.NewId();
        var acquired = await ExecuteAsync(connection, transaction, """
            UPDATE dbo.SalesDrafts
            SET Status=N'Issuing',Version=Version+1,UpdatedAt=@Now
            WHERE SalesDraftId=@DraftId AND Status=N'Active'
              AND Version=@ExpectedVersion;
            """, [
                P("@Now", now), P("@DraftId", draftId),
                P("@ExpectedVersion", request.ExpectedVersion)
            ], ct);
        if (acquired != 1)
            throw new OnlineSalesDraftConcurrencyException(
                "La venta cambio mientras se preparaba la emision.");
        await ExecuteAsync(connection, transaction, """
            INSERT dbo.SalesDrafts(
              SalesDraftId,BusinessId,WarehouseId,WorkSessionId,UserId,
              Status,Version,CreatedAt,UpdatedAt)
            VALUES(
              @NextDraftId,@BusinessId,@WarehouseId,@WorkSessionId,@UserId,
              N'Active',1,@Now,@Now);
            """, [
                P("@NextDraftId", nextDraftId), P("@BusinessId", state.BusinessId),
                P("@WarehouseId", state.WarehouseId), P("@WorkSessionId", state.WorkSessionId),
                P("@UserId", user.UserId), P("@Now", now)
            ], ct);
        await ExecuteAsync(connection, transaction, """
            INSERT dbo.OnlineSalesCheckoutReceipts(
              OnlineSalesCheckoutReceiptId,BusinessId,SalesDraftId,NextSalesDraftId,
              IdempotencyKey,RequestHash,DocumentId,PayloadJson,Status,CreatedAt)
            VALUES(
              @ReceiptId,@BusinessId,@DraftId,@NextDraftId,
              @Key,@Hash,@DocumentId,@Payload,N'Prepared',@Now);
            """, [
                P("@ReceiptId", ids.NewId()), P("@BusinessId", state.BusinessId),
                P("@DraftId", draftId), P("@NextDraftId", nextDraftId),
                P("@Key", idempotencyKey), P("@Hash", requestHash),
                P("@DocumentId", upload.DocumentId),
                P("@Payload", PosSaleContractSerializer.Serialize(upload)), P("@Now", now)
            ], ct);
        var nextDraft = await ReadDraftAsync(connection, transaction, nextDraftId, ct);
        await transaction.CommitAsync(ct);
        return new PreparedOnlineSalesCheckout(upload, nextDraft, false);
    }

    private static async Task DemandCustomerAllowsSalesReceiptAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        Guid? customerId, CancellationToken ct)
    {
        if (customerId is null) return;
        await using var command=connection.CreateCommand(); command.Transaction=transaction;
        command.CommandText="""
            SELECT RequiresElectronicInvoice FROM dbo.Customers
            WHERE CustomerId=@CustomerId AND BusinessId=@BusinessId AND IsActive=1;
            """;
        command.Parameters.AddRange([P("@CustomerId",customerId),P("@BusinessId",businessId)]);
        if (await command.ExecuteScalarAsync(ct) is true)
            throw new OnlineSalesDraftValidationException(
                "Este cliente esta configurado para recibir siempre factura electronica.");
    }

    private static async Task<SalesReceiptSeries> ReadSalesReceiptSeriesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT TOP(2) DocumentSeriesId,Prefix,SeriesCode,Padding,RangeStart,RangeEnd
            FROM dbo.DocumentSeries WITH (UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND DeviceId IS NULL
              AND DocumentType=N'SalesReceipt' AND SeriesCode=N'00'
              AND IsOfflineCapable=0 AND IsActive=1
            ORDER BY DocumentSeriesId;
            """;
        command.Parameters.AddWithValue("@BusinessId", businessId);
        var rows = new List<SalesReceiptSeries>(2);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new SalesReceiptSeries(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetByte(3), reader.GetInt64(4), reader.GetInt64(5)));
        return rows.Count == 1
            ? rows[0]
            : throw new OnlineSalesDraftValidationException(
                rows.Count == 0
                    ? "La sede no tiene numeracion CVI activa."
                    : "La sede tiene numeracion CVI ambigua.");
    }

    private static async Task<long> ConsumeSalesReceiptNumberAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SalesReceiptSeries series,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @Value BIGINT;
            SELECT @Value=NextConsecutive FROM dbo.DocumentSeriesCursors WITH (UPDLOCK,HOLDLOCK)
            WHERE DocumentSeriesId=@SeriesId;
            IF @Value IS NULL
            BEGIN
              SELECT @Value=CASE WHEN COALESCE(MAX(DocumentConsecutive)+1,@RangeStart)<@RangeStart
                THEN @RangeStart ELSE COALESCE(MAX(DocumentConsecutive)+1,@RangeStart) END
              FROM dbo.SalesDocuments WITH (UPDLOCK,HOLDLOCK) WHERE DocumentSeriesId=@SeriesId;
              INSERT dbo.DocumentSeriesCursors(DocumentSeriesId,NextConsecutive,UpdatedAt)
              VALUES(@SeriesId,@Value+1,@Now);
            END
            ELSE UPDATE dbo.DocumentSeriesCursors SET NextConsecutive=@Value+1,UpdatedAt=@Now
              WHERE DocumentSeriesId=@SeriesId;
            SELECT @Value;
            """;
        command.Parameters.AddRange([
            P("@SeriesId", series.SeriesId), P("@RangeStart", series.RangeStart), P("@Now", now)
        ]);
        var value = Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        if (value > series.RangeEnd)
            throw new OnlineSalesDraftValidationException("La numeracion CVI esta agotada.");
        return value;
    }

    private sealed record SalesReceiptSeries(
        Guid SeriesId, string Prefix, string SeriesCode, byte Padding,
        long RangeStart, long RangeEnd);
}

