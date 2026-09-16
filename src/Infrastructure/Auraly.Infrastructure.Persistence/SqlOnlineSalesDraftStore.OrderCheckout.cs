using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Core;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore
{
    public async Task<OnlineSalesFiscalKeyContext> ResolveOrderFiscalKeyContextAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesOrderCheckoutSource source,
        bool fiscalHabilitationOnly,
        CancellationToken cancellationToken)
    {
        if (fiscalHabilitationOnly)
            throw new OnlineSalesDraftValidationException(
                "La facturación de pedidos no admite numeración de habilitación.");
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP(2) fiscalAuthorization.FiscalAuthorizationId,
                   fiscalAuthorization.AuthorizationNumber,
                   fiscalAuthorization.TechnicalKeyVersion,fiscalAuthorization.Environment,
                   orderRow.BusinessId
            FROM dbo.Orders orderRow
            JOIN dbo.Businesses business
              ON business.BusinessId=orderRow.BusinessId
             AND business.TenantId=@TenantId AND business.IsActive=1
            JOIN dbo.WorkSessions sessionRow
              ON sessionRow.WorkSessionId=@WorkSessionId
             AND sessionRow.BusinessId=orderRow.BusinessId
             AND sessionRow.UserId=@UserId AND sessionRow.TenantId=@TenantId
             AND sessionRow.Status=N'Open'
            JOIN dbo.FiscalSeries series
              ON series.BusinessId=orderRow.BusinessId AND series.DeviceId IS NULL
             AND series.EmitterKind=N'Server'
             AND series.DocumentType=@DocumentType AND series.IsActive=1
            JOIN dbo.FiscalAuthorizations fiscalAuthorization
              ON fiscalAuthorization.FiscalAuthorizationId=series.FiscalAuthorizationId
             AND fiscalAuthorization.BusinessId=orderRow.BusinessId
             AND fiscalAuthorization.IsActive=1
            WHERE orderRow.OrderId=@OrderId AND orderRow.BusinessId=@BusinessId
              AND CONVERT(date,@Now) BETWEEN fiscalAuthorization.ValidFrom AND fiscalAuthorization.ValidUntil
            ORDER BY series.SeriesId;
            """;
        command.Parameters.AddRange([
            P("@TenantId", user.TenantId), P("@UserId", user.UserId),
            P("@WorkSessionId", source.WorkSessionId), P("@OrderId", source.OrderId),
            P("@BusinessId", source.BusinessId),
            P("@DocumentType", PosSaleDocumentTypes.Invoice), P("@Now", time.GetUtcNow())
        ]);
        var rows = new List<OnlineSalesFiscalKeyContext>(2);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(new FiscalKeyReference(
                user.TenantId, reader.GetGuid(4), reader.GetGuid(0),
                reader.GetString(1), reader.GetString(2),
                (FiscalEnvironment)reader.GetByte(3))));
        if (rows.Count != 1)
            throw new OnlineSalesDraftValidationException(
                rows.Count == 0
                    ? "La sede no tiene una resolución fiscal activa y vigente."
                    : "La sede tiene más de una serie fiscal activa.");
        return rows[0];
    }

    public async Task<PreparedOnlineOrderCheckout> PrepareOrderAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesOrderCheckoutSource source,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey,
        FiscalVerificationMaterial? fiscalMaterial,
        PreparedOnlineSaleSettlement settlement,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await PrepareOrderAttemptAsync(
                    user, source, request, idempotencyKey, fiscalMaterial,
                    settlement, cancellationToken);
            }
            catch (SqlException exception) when (exception.Number == 1205 && attempt < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
        }
    }

    private async Task<PreparedOnlineOrderCheckout> PrepareOrderAttemptAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesOrderCheckoutSource source,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey,
        FiscalVerificationMaterial? fiscalMaterial,
        PreparedOnlineSaleSettlement settlement,
        CancellationToken ct)
    {
        var requestHash = OrderCheckoutHash(source, request);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        var replay = await ReadOrderCheckoutReceiptAsync(
            connection, transaction, user, source, idempotencyKey, requestHash, ct);
        if (replay is not null)
        {
            await transaction.CommitAsync(ct);
            return replay;
        }

        await DemandCurrentOrderSourceAsync(connection, transaction, user, source, ct);
        var lines = BuildOrderSaleLines(source.Lines);
        var untaxed = lines.Sum(line => line.UntaxedAmount);
        var taxAmount = lines.Sum(line => line.TaxAmount);
        var payable = lines.Sum(line => line.LineTotal);
        if (settlement.Context.BusinessId != source.BusinessId ||
            settlement.Context.CustomerId != source.CustomerId ||
            settlement.Context.TaxExclusiveAmount != untaxed ||
            settlement.Context.VatAmount != taxAmount ||
            settlement.Withholding.GrossAmount != payable ||
            settlement.Withholding.NetAmount + settlement.Withholding.WithholdingTotal != payable)
            throw new OnlineSalesDraftConcurrencyException(
                "El pedido cambió mientras se calculaban sus valores de facturación.");
        if (request.Payments.Sum(payment => payment.Amount) +
            (request.Credit?.Amount ?? 0m) != settlement.Withholding.NetAmount)
            throw new OnlineSalesDraftValidationException(
                "Los pagos reales y el saldo financiado deben ser iguales al total de la venta.");

        var now = settlement.Context.OccurredAt;
        var creditValidation = await ValidateCreditAsync(
            connection, transaction, source.BusinessId, source.CustomerId,
            source.CustomerPartySiteId, request.Credit, now, ct);
        var payments = request.Payments.Select((payment, index) =>
            new PosSalePaymentContract(
                index + 1, payment.MethodCode, payment.Amount,
                string.IsNullOrWhiteSpace(payment.Reference) ? null : payment.Reference.Trim(),
                string.IsNullOrWhiteSpace(payment.CardFranchiseCode) ? null : payment.CardFranchiseCode.Trim(),
                string.IsNullOrWhiteSpace(payment.ApprovalNumber) ? null : payment.ApprovalNumber.Trim(),
                payment.BankAccountId,
                string.IsNullOrWhiteSpace(payment.Notes) ? null : payment.Notes.Trim(),
                payment.TenderedAmount)).ToArray();
        var taxes = lines.GroupBy(line => line.TaxCode, StringComparer.Ordinal)
            .Select(group => new PosSaleTaxContract(
                group.Key, group.Sum(line => line.TaxAmount)))
            .OrderBy(value => value.Code, StringComparer.Ordinal).ToArray();

        PosSaleUploadRequest upload;
        if (request.DocumentType == PosSaleDocumentTypes.Invoice)
        {
            if (fiscalMaterial is null)
                throw new OnlineSalesDraftValidationException(
                    "La factura electrónica requiere material fiscal activo.");
            var configuration = await ReadCheckoutConfigurationAsync(
                connection, transaction, source.BusinessId,
                PosSaleDocumentTypes.Invoice, PosSaleDocumentTypes.Invoice,
                now, ct);
            if (configuration.SupplierTaxId != fiscalMaterial.SupplierTaxId ||
                configuration.Environment != fiscalMaterial.Environment)
                throw new OnlineSalesDraftValidationException(
                    "La clave técnica no corresponde al emisor y ambiente de la resolución.");
            var documentConsecutive = await ConsumeDocumentNumberAsync(
                connection, transaction, configuration, now, ct);
            var fiscalConsecutive = await ConsumeFiscalNumberAsync(
                connection, transaction, configuration, now, ct);
            var documentNumber = AuralyDocumentNumberAssignment.Create(
                configuration.DocumentSeriesId, PosSaleDocumentTypes.Invoice,
                configuration.DocumentPrefix, configuration.SeriesCode,
                documentConsecutive, configuration.Padding);
            var fiscalNumber = $"{configuration.FiscalPrefix}{fiscalConsecutive}";
            var customer = await ReadCustomerPartyAsync(
                connection, transaction, source.BusinessId, source.CustomerId,
                source.CustomerPartySiteId, configuration, ct);
            var cufe = CufeCalculator.Calculate(new CufeInput(
                fiscalNumber, now, untaxed, payable,
                configuration.SupplierTaxId, customer.Identification,
                fiscalMaterial.TechnicalKey, fiscalMaterial.Environment,
                taxes.Select(value => new FiscalTaxAmount(value.Code, value.Amount))),
                fiscalMaterial.QrValidationUrl);
            upload = new PosSaleUploadRequest(
                user.TenantId, source.BusinessId, source.WarehouseId, Guid.Empty,
                source.WorkSessionId, user.UserId, ids.NewId(),
                new PosSaleDocumentNumberContract(
                    documentNumber.SeriesId, documentNumber.DocumentType,
                    documentNumber.Prefix, documentNumber.SeriesCode,
                    documentNumber.Consecutive, documentNumber.Padding,
                    documentNumber.FullNumber),
                new PosSaleCommercialSnapshotContract(
                    PosSaleDocumentTypes.Invoice, now, customer.Identification,
                    taxes, untaxed, taxAmount, payable, settlement.Withholding),
                new PosSaleFiscalSnapshotContract(
                    configuration.FiscalSeriesId, configuration.FiscalAuthorizationId,
                    configuration.AuthorizationNumber, PosSaleDocumentTypes.Invoice,
                    fiscalNumber, configuration.FiscalPrefix, fiscalConsecutive, now,
                    configuration.SupplierTaxId, customer.Identification,
                    (int)configuration.Environment, configuration.TechnicalKeyVersion,
                    taxes, untaxed, taxAmount, payable, cufe.Cufe, cufe.QrPayload),
                lines, payments,
                new PosSaleUblSnapshotContract(
                    configuration.FiscalIssuerConfigurationId,
                    source.Lines.Select(line => line.CurrencyCode)
                        .Distinct(StringComparer.Ordinal).Single(),
                    "01", configuration.Supplier, customer,
                    new PosSaleUblAuthorizationContract(
                        configuration.AuthorizationNumber, configuration.ValidFrom,
                        configuration.ValidUntil, configuration.FiscalPrefix,
                        configuration.AuthorizationRangeStart,
                        configuration.AuthorizationRangeEnd),
                    configuration.SoftwareIdentificationCode,
                    source.Lines.Select((line, index) => new PosSaleUblLineContract(
                        index + 1, line.ProductCode, "999", line.UnitCode,
                        PosSaleFiscalMappings.TaxName(line.TaxCode), line.TaxRate)).ToArray(),
                    request.Credit is null ? "1" : "2",
                    payments.Length == 0 ? "ZZZ" :
                        PosSaleFiscalMappings.PaymentMeansCode(payments[0].MethodCode)
                        ?? throw new OnlineSalesDraftValidationException(
                            "El medio de pago no tiene equivalencia fiscal configurada."),
                    DateOnly.FromDateTime((creditValidation?.DueDate ?? now).Date),
                    payments.Select(payment => payment.Reference)
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))),
                source.CustomerId, SaleSourceModes.Online, source.OrderId,
                BuildOrderCredit(user, source, request, creditValidation),
                CustomerPartySiteId: source.CustomerPartySiteId);
        }
        else
        {
            await DemandCustomerAllowsSalesReceiptAsync(
                connection, transaction, source.BusinessId, source.CustomerId, ct);
            var series = await ReadSalesReceiptSeriesAsync(
                connection, transaction, source.BusinessId, ct);
            var consecutive = await ConsumeSalesReceiptNumberAsync(
                connection, transaction, series, now, ct);
            var number = AuralyDocumentNumberAssignment.Create(
                series.SeriesId, PosSaleDocumentTypes.Receipt, series.Prefix,
                series.SeriesCode, consecutive, series.Padding);
            var identification = await ResolveCustomerIdentificationAsync(
                connection, transaction, source.BusinessId, source.CustomerId, ct);
            upload = new PosSaleUploadRequest(
                user.TenantId, source.BusinessId, source.WarehouseId, Guid.Empty,
                source.WorkSessionId, user.UserId, ids.NewId(),
                new PosSaleDocumentNumberContract(
                    number.SeriesId, number.DocumentType, number.Prefix,
                    number.SeriesCode, number.Consecutive, number.Padding,
                    number.FullNumber),
                new PosSaleCommercialSnapshotContract(
                    PosSaleDocumentTypes.Receipt, now, identification,
                    taxes, untaxed, taxAmount, payable, settlement.Withholding),
                null, lines, payments, null, source.CustomerId,
                SaleSourceModes.Online, source.OrderId,
                BuildOrderCredit(user, source, request, creditValidation),
                CustomerPartySiteId: source.CustomerPartySiteId);
        }

        await ExecuteAsync(connection, transaction, """
            INSERT dbo.OnlineSalesCheckoutReceipts(
              OnlineSalesCheckoutReceiptId,BusinessId,SalesDraftId,NextSalesDraftId,
              SourceOrderId,OperationId,IdempotencyKey,RequestHash,DocumentId,
              PayloadJson,Status,CreatedAt)
            VALUES(
              @ReceiptId,@BusinessId,NULL,NULL,@OrderId,@OperationId,@Key,@Hash,
              @DocumentId,@Payload,N'Prepared',@Now);
            """, [
                P("@ReceiptId", ids.NewId()), P("@BusinessId", source.BusinessId),
                P("@OrderId", source.OrderId), P("@OperationId", source.OperationId),
                P("@Key", idempotencyKey), P("@Hash", requestHash),
                P("@DocumentId", upload.DocumentId),
                P("@Payload", PosSaleContractSerializer.Serialize(upload)), P("@Now", now)
            ], ct);
        await transaction.CommitAsync(ct);
        return new(upload, false);
    }

    public async Task MarkOrderResultAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesOrderCheckoutSource source,
        Guid documentId,
        string status,
        CancellationToken cancellationToken)
    {
        if (status is not ("Completed" or "FiscalConflict"))
            throw new ArgumentOutOfRangeException(nameof(status));
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE receipt
            SET Status=@Status,
                CompletedAt=CASE WHEN @Status=N'Completed' THEN @Now ELSE NULL END
            FROM dbo.OnlineSalesCheckoutReceipts receipt
            JOIN dbo.OrderInvoiceBatchReceipts operation
              ON operation.OperationId=receipt.OperationId
            WHERE receipt.SourceOrderId=@OrderId AND receipt.OperationId=@OperationId
              AND receipt.DocumentId=@DocumentId AND receipt.BusinessId=@BusinessId
              AND operation.UserId=@UserId AND operation.BusinessId=@BusinessId
              AND receipt.Status IN(N'Prepared',@Status);
            IF @@ROWCOUNT=0
                THROW 51022,'La emisión del pedido no pertenece al usuario autenticado.',1;

            INSERT dbo.OrderInvoiceLinks(
              OrderInvoiceLinkId,BusinessId,OrderId,DocumentId,OperationId,CreatedAt)
            SELECT @LinkId,@BusinessId,@OrderId,@DocumentId,@OperationId,@Now
            WHERE @Status=N'Completed' AND NOT EXISTS(
              SELECT 1 FROM dbo.OrderInvoiceLinks WHERE OrderId=@OrderId);

            UPDATE dbo.OrderClaims SET ReleasedAt=COALESCE(ReleasedAt,@Now)
            WHERE OrderId=@OrderId AND BusinessId=@BusinessId AND ReleasedAt IS NULL;
            """;
        command.Parameters.AddRange([
            P("@Status", status), P("@Now", time.GetUtcNow()),
            P("@LinkId", ids.NewId()), P("@OrderId", source.OrderId),
            P("@OperationId", source.OperationId), P("@DocumentId", documentId),
            P("@BusinessId", source.BusinessId), P("@UserId", user.UserId)
        ]);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
            throw new OnlineSalesDraftForbiddenException(
                "La emisión del pedido no pertenece al usuario autenticado.");
        await transaction.CommitAsync(cancellationToken);
    }

    private static PosSaleLineContract[] BuildOrderSaleLines(
        IReadOnlyList<OnlineSalesOrderCheckoutLine> source) =>
        OnlineSalesOrderCheckoutLineMapper.Normalize(source).Select((line, index) =>
        {
            var fiscal = Fiscalize(line);
            return new PosSaleLineContract(
                index + 1, line.ProductId, line.Description, line.TaxCode,
                line.Quantity, fiscal.UnitPrice, fiscal.Discount,
                line.Tax, line.Net, line.Total,
                line.TaxRate, line.DocumentUnitCost, fiscal.PromotionDiscount);
        }).ToArray();

    private static PosSaleCreditContract? BuildOrderCredit(
        OnlineSalesUserIdentity user,
        OnlineSalesOrderCheckoutSource source,
        CompleteOnlineSalesDraftRequest request,
        PosCreditValidationResult? validation) =>
        request.Credit is null || source.CustomerId is null
            ? null
            : new PosSaleCreditContract(
                source.CustomerId.Value, request.Credit.Amount,
                validation!.DueDate!.Value,
                validation.AvailableCredit is null ? null : Math.Max(
                    0m, validation.AvailableCredit.Value - request.Credit.Amount),
                user.UserName, source.CustomerPartySiteId);

    private static async Task DemandCurrentOrderSourceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OnlineSalesUserIdentity user,
        OnlineSalesOrderCheckoutSource source,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT orderRow.Status,orderRow.CustomerConfirmed,
                   COALESCE(orderRow.WarehouseId,TRY_CONVERT(uniqueidentifier,JSON_VALUE(orderRow.CustomAttributesJson,'$.WarehouseId'))),
                   orderRow.CustomerId,orderRow.PartySiteId,orderRow.RowVersion,
                   CASE WHEN link.OrderId IS NULL THEN 0 ELSE 1 END
            FROM dbo.Orders orderRow WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Businesses business
              ON business.BusinessId=orderRow.BusinessId
             AND business.TenantId=@TenantId AND business.IsActive=1
            JOIN dbo.WorkSessions sessionRow
              ON sessionRow.WorkSessionId=@WorkSessionId
             AND sessionRow.BusinessId=orderRow.BusinessId
             AND sessionRow.UserId=@UserId AND sessionRow.TenantId=@TenantId
             AND sessionRow.Status=N'Open'
            LEFT JOIN dbo.OrderInvoiceLinks link ON link.OrderId=orderRow.OrderId
            WHERE orderRow.OrderId=@OrderId AND orderRow.BusinessId=@BusinessId;
            """;
        command.Parameters.AddRange([
            P("@TenantId", user.TenantId), P("@UserId", user.UserId),
            P("@WorkSessionId", source.WorkSessionId), P("@OrderId", source.OrderId),
            P("@BusinessId", source.BusinessId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftForbiddenException(
                "El pedido o la sesión de trabajo no pertenecen al usuario autenticado.");
        var status = reader.GetInt32(0);
        var storedWarehouse = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
        var storedCustomer = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3);
        var storedSite = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4);
        var version = (byte[])reader.GetValue(5);
        if (!reader.GetBoolean(1) || status is not (2 or 4 or 5) ||
            reader.GetInt32(6) != 0 || storedWarehouse != source.WarehouseId ||
            storedCustomer != source.CustomerId || storedSite != source.CustomerPartySiteId)
            throw new OnlineSalesDraftConcurrencyException(
                "El pedido ya no está disponible o cambió antes de facturarse.");
        if (!version.AsSpan().SequenceEqual(source.SnapshotVersion))
            throw new OnlineSalesDraftConcurrencyException(
                "El pedido fue editado antes de facturarse. Actualiza la lista e inténtalo nuevamente.");
    }

    private static async Task<PreparedOnlineOrderCheckout?> ReadOrderCheckoutReceiptAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OnlineSalesUserIdentity user,
        OnlineSalesOrderCheckoutSource source,
        string idempotencyKey,
        string requestHash,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT receipt.IdempotencyKey,receipt.RequestHash,receipt.PayloadJson
            FROM dbo.OnlineSalesCheckoutReceipts receipt WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.OrderInvoiceBatchReceipts operation
              ON operation.OperationId=receipt.OperationId
            WHERE receipt.SourceOrderId=@OrderId AND receipt.BusinessId=@BusinessId
              AND operation.UserId=@UserId;
            """;
        command.Parameters.AddRange([
            P("@OrderId", source.OrderId), P("@BusinessId", source.BusinessId),
            P("@UserId", user.UserId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        if (!string.Equals(reader.GetString(0), idempotencyKey, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(1), requestHash, StringComparison.Ordinal))
            throw new OnlineSalesDraftConcurrencyException(
                "El pedido ya tiene una emisión preparada con otra solicitud.");
        return new(
            PosSaleContractSerializer.Deserialize(reader.GetString(2)), true);
    }

    private static string OrderCheckoutHash(
        OnlineSalesOrderCheckoutSource source,
        CompleteOnlineSalesDraftRequest request)
    {
        var value = new StringBuilder()
            .Append(source.OperationId.ToString("D")).Append('|')
            .Append(source.OrderId.ToString("D")).Append('|')
            .Append(Convert.ToHexString(source.SnapshotVersion)).Append('|')
            .Append(request.DocumentType);
        foreach (var payment in request.Payments)
            value.Append('|').Append(payment.MethodCode).Append(':')
                .Append(payment.Amount.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(payment.Reference?.Trim()).Append(':')
                .Append(payment.BankAccountId?.ToString("D")).Append(':')
                .Append(payment.Notes?.Trim());
        if (request.Credit is not null)
            value.Append("|credit:")
                .Append(request.Credit.Amount.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }
}
