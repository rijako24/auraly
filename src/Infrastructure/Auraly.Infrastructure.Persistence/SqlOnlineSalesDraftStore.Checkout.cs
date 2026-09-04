using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Fiscal.Core;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore
{
    public async Task<OnlineSaleSettlementContext> ReadSettlementContextAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var state = await LockDraftAsync(
            connection, transaction, user, draftId, cancellationToken);
        var draft = await ReadDraftAsync(
            connection, transaction, draftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OnlineSaleSettlementContext(
            state.BusinessId,
            state.CustomerId,
            draft.UntaxedAmount,
            draft.TaxAmount,
            time.GetUtcNow());
    }

    public async Task<OnlineSalesFiscalKeyContext> ResolveFiscalKeyContextAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP(2) a.AuthorizationNumber,a.TechnicalKeyVersion,a.Environment
            FROM dbo.SalesDrafts d
            JOIN dbo.Businesses b ON b.BusinessId=d.BusinessId
            JOIN dbo.WorkSessions ws
              ON ws.WorkSessionId=d.WorkSessionId AND ws.BusinessId=d.BusinessId
             AND ws.UserId=d.UserId
             AND ws.TenantId=b.TenantId
             AND ws.Status=N'Open'
            JOIN dbo.FiscalSeries s
              ON s.BusinessId=d.BusinessId AND s.DeviceId IS NULL
             AND s.EmitterKind=N'Server'
             AND s.DocumentType=@DocumentType AND s.IsActive=1
            JOIN dbo.FiscalAuthorizations a
              ON a.FiscalAuthorizationId=s.FiscalAuthorizationId
             AND a.BusinessId=d.BusinessId AND a.IsActive=1
            WHERE d.SalesDraftId=@DraftId AND d.UserId=@UserId
              AND d.Status IN (N'Active',N'Issuing',N'Consumed')
              AND b.TenantId=@TenantId AND b.IsActive=1
              AND CONVERT(date,@Now) BETWEEN a.ValidFrom AND a.ValidUntil
            ORDER BY s.SeriesId;
            """;
        command.Parameters.AddRange([
            P("@DraftId", draftId),
            P("@UserId", user.UserId),
            P("@TenantId", user.TenantId),
            P("@DocumentType", PosSaleDocumentTypes.Invoice),
            P("@Now", time.GetUtcNow())
        ]);
        var rows = new List<OnlineSalesFiscalKeyContext>(2);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new OnlineSalesFiscalKeyContext(
                new FiscalKeyReference(
                    user.TenantId,
                    Guid.Empty,
                    reader.GetString(0),
                    reader.GetString(1),
                    (FiscalEnvironment)reader.GetByte(2))));
        }
        if (rows.Count != 1)
            throw new OnlineSalesDraftValidationException(
                rows.Count == 0
                    ? "La sede no tiene una resolución fiscal activa y vigente."
                    : "La sede tiene más de una serie fiscal activa.");

        await reader.DisposeAsync();
        await using var business = connection.CreateCommand();
        business.CommandText = """
            SELECT BusinessId FROM dbo.SalesDrafts
            WHERE SalesDraftId=@DraftId AND UserId=@UserId;
            """;
        business.Parameters.AddRange([
            P("@DraftId", draftId),
            P("@UserId", user.UserId)
        ]);
        var businessId = await business.ExecuteScalarAsync(cancellationToken) is Guid value
            ? value
            : throw new OnlineSalesDraftForbiddenException(
                "El borrador no pertenece al usuario autenticado.");
        return rows[0] with
        {
            Reference = rows[0].Reference with { BusinessId = businessId }
        };
    }

    private async Task<PreparedOnlineSalesCheckout> PrepareInvoiceAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey,
        FiscalVerificationMaterial fiscalMaterial,
        PreparedOnlineSaleSettlement settlement,
        CancellationToken cancellationToken)
    {
        var requestHash = CheckoutHash(draftId, request);
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        var state = await LockDraftAsync(
            connection, transaction, user, draftId, cancellationToken);
        var replay = await ReadCheckoutReceiptAsync(
            connection,
            transaction,
            user,
            draftId,
            idempotencyKey,
            requestHash,
            cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        DemandActiveVersion(state, request.ExpectedVersion);
        var draft = await ReadDraftAsync(
            connection, transaction, draftId, cancellationToken);
        if (draft.Lines.Count == 0)
            throw new OnlineSalesDraftValidationException(
                "La venta debe tener al menos un producto.");
        var inventoryValidation = await ValidateInventoryAsync(
            connection, transaction, state, draftId, cancellationToken);
        if (!inventoryValidation.IsValid)
            throw new OnlineSalesDraftValidationException(
                "El inventario cambió y uno o más productos ya no tienen existencias suficientes. Ajusta sus cantidades o elimínalos antes de cobrar.");
        ValidateSettlementContext(state, draft, settlement);
        var withholding = settlement.Withholding;
        if (request.Payments.Sum(payment => payment.Amount) + (request.Credit?.Amount ?? 0m) != withholding.NetAmount)
            throw new OnlineSalesDraftValidationException(
                "Los pagos reales y el saldo financiado deben ser iguales al total de la venta.");

        await ValidateCreditAsync(connection, transaction, state.BusinessId,
            state.CustomerId, request.Credit, cancellationToken);

        var now = settlement.Context.OccurredAt;
        var configuration = await ReadCheckoutConfigurationAsync(
            connection, transaction, state.BusinessId,
            PosSaleDocumentTypes.Invoice, PosSaleDocumentTypes.Invoice,
            now, cancellationToken);
        if (configuration.SupplierTaxId != fiscalMaterial.SupplierTaxId ||
            configuration.Environment != fiscalMaterial.Environment)
            throw new OnlineSalesDraftValidationException(
                "La clave técnica no corresponde al emisor y ambiente de la resolución.");

        var documentConsecutive = await ConsumeDocumentNumberAsync(
            connection, transaction, configuration, now, cancellationToken);
        var fiscalConsecutive = await ConsumeFiscalNumberAsync(
            connection, transaction, configuration, now, cancellationToken);
        var documentNumber = AuralyDocumentNumberAssignment.Create(
            configuration.DocumentSeriesId,
            PosSaleDocumentTypes.Invoice,
            configuration.DocumentPrefix,
            configuration.SeriesCode,
            documentConsecutive,
            configuration.Padding);
        var fiscalNumber = $"{configuration.FiscalPrefix}{fiscalConsecutive}";
        var taxes = draft.Lines
            .GroupBy(line => line.TaxCode, StringComparer.Ordinal)
            .Select(group => new PosSaleTaxContract(
                group.Key,
                group.Sum(line => line.Tax)))
            .OrderBy(tax => tax.Code, StringComparer.Ordinal)
            .ToArray();
        var cufe = CufeCalculator.Calculate(
            new CufeInput(
                fiscalNumber,
                now,
                draft.UntaxedAmount,
                draft.PayableAmount,
                configuration.SupplierTaxId,
                await ResolveCustomerIdentificationAsync(
                    connection,
                    transaction,
                    state.BusinessId,
                    state.CustomerId,
                    cancellationToken),
                fiscalMaterial.TechnicalKey,
                fiscalMaterial.Environment,
                taxes.Select(tax => new FiscalTaxAmount(tax.Code, tax.Amount))),
            fiscalMaterial.QrValidationUrl);
        var customer = await ReadCustomerPartyAsync(
            connection,
            transaction,
            state.BusinessId,
            state.CustomerId,
            configuration,
            cancellationToken);
        var lines = draft.Lines.Select((line, index) =>
            new PosSaleLineContract(
                index + 1,
                line.ProductId,
                line.Description,
                line.TaxCode,
                line.Quantity,
                line.UnitPrice,
                line.Discount,
                line.Tax,
                line.Net,
                line.Total,
                line.TaxRate,
                line.AllowsDocumentCostOverride ? line.DocumentUnitCost : null)).ToArray();
        var payments = request.Payments.Select((payment, index) =>
            new PosSalePaymentContract(
                index + 1,
                payment.MethodCode,
                payment.Amount,
                string.IsNullOrWhiteSpace(payment.Reference)
                    ? null
                    : payment.Reference.Trim(),
                string.IsNullOrWhiteSpace(payment.CardFranchiseCode) ? null : payment.CardFranchiseCode.Trim(),
                string.IsNullOrWhiteSpace(payment.ApprovalNumber) ? null : payment.ApprovalNumber.Trim(),
                payment.BankAccountId,
                string.IsNullOrWhiteSpace(payment.Notes) ? null : payment.Notes.Trim())).ToArray();
        var upload = new PosSaleUploadRequest(
            user.TenantId,
            state.BusinessId,
            state.WarehouseId,
            Guid.Empty,
            state.WorkSessionId,
            user.UserId,
            ids.NewId(),
            new PosSaleDocumentNumberContract(
                documentNumber.SeriesId,
                documentNumber.DocumentType,
                documentNumber.Prefix,
                documentNumber.SeriesCode,
                documentNumber.Consecutive,
                documentNumber.Padding,
                documentNumber.FullNumber),
            new PosSaleCommercialSnapshotContract(
                PosSaleDocumentTypes.Invoice,
                now,
                customer.Identification,
                taxes,
                draft.UntaxedAmount,
                draft.TaxAmount,
                draft.PayableAmount,
                withholding),
            new PosSaleFiscalSnapshotContract(
                configuration.FiscalSeriesId,
                configuration.FiscalAuthorizationId,
                configuration.AuthorizationNumber,
                PosSaleDocumentTypes.Invoice,
                fiscalNumber,
                configuration.FiscalPrefix,
                fiscalConsecutive,
                now,
                configuration.SupplierTaxId,
                customer.Identification,
                (int)configuration.Environment,
                configuration.TechnicalKeyVersion,
                taxes,
                draft.UntaxedAmount,
                draft.TaxAmount,
                draft.PayableAmount,
                cufe.Cufe,
                cufe.QrPayload),
            lines,
            payments,
            new PosSaleUblSnapshotContract(
                configuration.FiscalIssuerConfigurationId,
                draft.Lines.Select(line => line.CurrencyCode)
                    .Distinct(StringComparer.Ordinal)
                    .Single(),
                "01",
                configuration.Supplier,
                customer,
                new PosSaleUblAuthorizationContract(
                    configuration.AuthorizationNumber,
                    configuration.ValidFrom,
                    configuration.ValidUntil,
                    configuration.FiscalPrefix,
                    configuration.AuthorizationRangeStart,
                    configuration.AuthorizationRangeEnd),
                configuration.SoftwareIdentificationCode,
                draft.Lines.Select((line, index) =>
                    new PosSaleUblLineContract(
                        index + 1,
                        line.ProductCode,
                        "999",
                        line.UnitCode,
                        TaxName(line.TaxCode),
                        line.TaxRate)).ToArray(),
                request.Credit is null ? "1" : "2",
                payments.Length == 0 ? "ZZZ" : PaymentMeansCode(payments[0].MethodCode),
                DateOnly.FromDateTime((request.Credit?.DueDate ?? now).Date),
                payments.Select(payment => payment.Reference)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))),
            state.CustomerId,
            SaleSourceModes.Online,
            state.SourceOrderId,
            Credit: request.Credit is null || state.CustomerId is null ? null :
                new PosSaleCreditContract(state.CustomerId.Value, request.Credit.Amount, request.Credit.DueDate),
            FiscalHabilitationOnly: request.FiscalHabilitationOnly);

        await ReleaseOrderInventoryAsync(connection, transaction, user, state, cancellationToken);

        var nextDraftId = ids.NewId();
        var acquired = await ExecuteAsync(connection, transaction, """
            UPDATE dbo.SalesDrafts
            SET Status=N'Issuing',Version=Version+1,UpdatedAt=@Now
            WHERE SalesDraftId=@DraftId AND Status=N'Active'
              AND Version=@ExpectedVersion;
            """,
            [
                P("@Now", now),
                P("@DraftId", draftId),
                P("@ExpectedVersion", request.ExpectedVersion)
            ],
            cancellationToken);
        if (acquired != 1)
            throw new OnlineSalesDraftConcurrencyException(
                "La venta cambió mientras se preparaba la emisión.");
        await ExecuteAsync(connection, transaction, """
            INSERT dbo.SalesDrafts(
              SalesDraftId,BusinessId,WarehouseId,WorkSessionId,UserId,
              Status,Version,CreatedAt,UpdatedAt)
            VALUES(
              @NextDraftId,@BusinessId,@WarehouseId,@WorkSessionId,@UserId,
              N'Active',1,@Now,@Now);
            """,
            [
                P("@NextDraftId", nextDraftId),
                P("@BusinessId", state.BusinessId),
                P("@WarehouseId", state.WarehouseId),
                P("@WorkSessionId", state.WorkSessionId),
                P("@UserId", user.UserId),
                P("@Now", now)
            ],
            cancellationToken);
        await ExecuteAsync(connection, transaction, """
            INSERT dbo.OnlineSalesCheckoutReceipts(
              OnlineSalesCheckoutReceiptId,BusinessId,SalesDraftId,NextSalesDraftId,
              IdempotencyKey,RequestHash,DocumentId,PayloadJson,Status,CreatedAt)
            VALUES(
              @ReceiptId,@BusinessId,@DraftId,@NextDraftId,
              @Key,@Hash,@DocumentId,@Payload,N'Prepared',@Now);
            """,
            [
                P("@ReceiptId", ids.NewId()),
                P("@BusinessId", state.BusinessId),
                P("@DraftId", draftId),
                P("@NextDraftId", nextDraftId),
                P("@Key", idempotencyKey),
                P("@Hash", requestHash),
                P("@DocumentId", upload.DocumentId),
                P("@Payload", PosSaleContractSerializer.Serialize(upload)),
                P("@Now", now)
            ],
            cancellationToken);
        var nextDraft = await ReadDraftAsync(
            connection, transaction, nextDraftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PreparedOnlineSalesCheckout(upload, nextDraft, false);
    }

    public async Task MarkResultAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid documentId,
        string status,
        CancellationToken cancellationToken)
    {
        if (status is not ("Completed" or "FiscalConflict"))
            throw new ArgumentOutOfRangeException(nameof(status));
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE receipt
            SET Status=@Status,
                CompletedAt=CASE WHEN @Status=N'Completed' THEN @Now ELSE NULL END
            FROM dbo.OnlineSalesCheckoutReceipts receipt
            JOIN dbo.SalesDrafts draft
              ON draft.SalesDraftId=receipt.SalesDraftId
            JOIN dbo.Businesses business
              ON business.BusinessId=receipt.BusinessId
            WHERE receipt.SalesDraftId=@DraftId
              AND receipt.DocumentId=@DocumentId
              AND draft.UserId=@UserId
              AND business.TenantId=@TenantId
              AND receipt.Status IN (N'Prepared',@Status);

            UPDATE draft
            SET Status=N'Consumed',ConsumedAt=@Now,UpdatedAt=@Now
            FROM dbo.SalesDrafts draft
            JOIN dbo.Businesses business ON business.BusinessId=draft.BusinessId
            WHERE draft.SalesDraftId=@DraftId AND draft.UserId=@UserId
              AND business.TenantId=@TenantId AND @Status=N'Completed'
              AND draft.Status IN (N'Issuing',N'Consumed');

            INSERT dbo.OrderInvoiceLinks(
              OrderInvoiceLinkId,BusinessId,OrderId,DocumentId,OperationId,CreatedAt)
            SELECT
              @OrderInvoiceLinkId,draft.BusinessId,draft.SourceOrderId,
              @DocumentId,NULL,@Now
            FROM dbo.SalesDrafts draft
            WHERE draft.SalesDraftId=@DraftId
              AND draft.SourceOrderId IS NOT NULL
              AND @Status=N'Completed'
              AND NOT EXISTS(
                SELECT 1 FROM dbo.OrderInvoiceLinks link
                WHERE link.OrderId=draft.SourceOrderId);

            UPDATE claim
            SET ReleasedAt=@Now
            FROM dbo.OrderClaims claim
            JOIN dbo.SalesDrafts draft ON draft.SourceOrderId=claim.OrderId
            WHERE draft.SalesDraftId=@DraftId AND @Status=N'Completed'
              AND claim.ReleasedAt IS NULL;
            """;
        command.Parameters.AddRange([
            P("@Status", status),
            P("@OrderInvoiceLinkId", ids.NewId()),
            P("@Now", time.GetUtcNow()),
            P("@DraftId", draftId),
            P("@DocumentId", documentId),
            P("@UserId", user.UserId),
            P("@TenantId", user.TenantId)
        ]);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
            throw new OnlineSalesDraftForbiddenException(
                "El checkout no pertenece al usuario autenticado.");
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<PreparedOnlineSalesCheckout?> ReadCheckoutReceiptAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OnlineSalesUserIdentity user,
        Guid draftId,
        string idempotencyKey,
        string requestHash,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT receipt.IdempotencyKey,receipt.RequestHash,
                   receipt.PayloadJson,receipt.NextSalesDraftId
            FROM dbo.OnlineSalesCheckoutReceipts receipt WITH (UPDLOCK,HOLDLOCK)
            JOIN dbo.SalesDrafts draft
              ON draft.SalesDraftId=receipt.SalesDraftId
            JOIN dbo.Businesses business
              ON business.BusinessId=receipt.BusinessId
            WHERE receipt.SalesDraftId=@DraftId AND draft.UserId=@UserId
              AND business.TenantId=@TenantId;
            """;
        command.Parameters.AddRange([
            P("@DraftId", draftId),
            P("@UserId", user.UserId),
            P("@TenantId", user.TenantId)
        ]);
        string? storedKey = null;
        string? storedHash = null;
        string? payload = null;
        Guid nextDraftId = default;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                storedKey = reader.GetString(0);
                storedHash = reader.GetString(1);
                payload = reader.GetString(2);
                nextDraftId = reader.GetGuid(3);
            }
        }
        if (payload is null)
            return null;
        if (!string.Equals(storedKey, idempotencyKey, StringComparison.Ordinal) ||
            !string.Equals(storedHash, requestHash, StringComparison.Ordinal))
            throw new OnlineSalesDraftIdempotencyException(
                "La venta ya fue emitida con otro contenido o clave de idempotencia.");
        return new PreparedOnlineSalesCheckout(
            PosSaleContractSerializer.Deserialize(payload),
            await ReadDraftAsync(connection, transaction, nextDraftId, ct),
            true);
    }

    internal static async Task<CheckoutConfiguration> ReadCheckoutConfigurationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        string documentType,
        string fiscalSeriesDocumentType,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT TOP(2)
              ds.DocumentSeriesId,ds.Prefix,ds.SeriesCode,ds.Padding,
              ds.RangeStart,ds.RangeEnd,
              fs.SeriesId,fs.Prefix,fs.RangeStart,fs.RangeEnd,
              a.FiscalAuthorizationId,a.AuthorizationNumber,a.SupplierTaxId,
              a.Environment,a.QrValidationUrl,a.TechnicalKeyVersion,
              a.ValidFrom,a.ValidUntil,
              issuer.FiscalIssuerConfigurationId,issuer.SupplierCheckDigit,
              issuer.LegalName,issuer.TradeName,issuer.TaxLevelCode,
              issuer.TaxSchemeId,issuer.TaxSchemeName,issuer.IdentificationTypeCode,
              issuer.AddressLine,issuer.CityCode,issuer.CityName,
              issuer.DepartmentCode,issuer.DepartmentName,
              issuer.CountryCode,issuer.CountryName,
              issuer.SoftwareIdentificationCode,
              a.AuthorizedRangeStart,a.AuthorizedRangeEnd
            FROM dbo.DocumentSeries ds WITH (UPDLOCK,HOLDLOCK)
            JOIN dbo.FiscalSeries fs WITH (UPDLOCK,HOLDLOCK)
              ON fs.BusinessId=ds.BusinessId AND fs.DeviceId IS NULL
             AND fs.EmitterKind=N'Server'
             AND fs.DocumentType=@FiscalSeriesDocumentType AND fs.IsActive=1
            JOIN dbo.FiscalAuthorizations a
              ON a.FiscalAuthorizationId=fs.FiscalAuthorizationId
             AND a.BusinessId=fs.BusinessId AND a.IsActive=1
            JOIN dbo.FiscalIssuerConfigurations issuer
              ON issuer.BusinessId=ds.BusinessId AND issuer.IsActive=1
             AND issuer.Environment=a.Environment
            WHERE ds.BusinessId=@BusinessId
              AND ds.DeviceId IS NULL AND ds.SeriesCode=N'00'
              AND ds.IsOfflineCapable=0 AND ds.DocumentType=@DocumentType
              AND ds.IsActive=1 AND CONVERT(date,@Now) BETWEEN a.ValidFrom AND a.ValidUntil
            ORDER BY ds.DocumentSeriesId,fs.SeriesId;
            """;
        command.Parameters.AddRange([
            P("@BusinessId", businessId),
            P("@DocumentType", documentType),
            P("@FiscalSeriesDocumentType", fiscalSeriesDocumentType),
            P("@Now", now)
        ]);
        var rows = new List<CheckoutConfiguration>(2);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var supplier = new PosSaleUblPartyContract(
                reader.GetString(12),
                reader.GetString(19),
                reader.GetString(25),
                "1",
                reader.GetString(20),
                reader.IsDBNull(21) ? reader.GetString(20) : reader.GetString(21),
                reader.GetString(22),
                reader.GetString(23),
                reader.GetString(24),
                new PosSaleUblAddressContract(
                    reader.GetString(27),
                    reader.GetString(28),
                    reader.GetString(30),
                    reader.GetString(29),
                    reader.GetString(26),
                    reader.GetString(31),
                    reader.GetString(32)));
            rows.Add(new CheckoutConfiguration(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetByte(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetGuid(6),
                reader.GetString(7),
                reader.GetInt64(8),
                reader.GetInt64(9),
                reader.GetGuid(10),
                reader.GetString(11),
                reader.GetString(12),
                (FiscalEnvironment)reader.GetByte(13),
                reader.GetString(14),
                reader.GetString(15),
                DateOnly.FromDateTime(reader.GetDateTime(16)),
                DateOnly.FromDateTime(reader.GetDateTime(17)),
                reader.GetGuid(18),
                reader.GetString(33),
                supplier,
                reader.GetInt64(34),
                reader.GetInt64(35)));
        }
        if (rows.Count != 1)
            throw new OnlineSalesDraftValidationException(
                rows.Count == 0
                    ? "La sede no tiene series operativa y fiscal activas."
                    : "La sede tiene una configuración de numeración ambigua.");
        return rows[0];
    }

    internal static async Task<long> ConsumeDocumentNumberAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CheckoutConfiguration configuration,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @Value BIGINT;
            SELECT @Value=NextConsecutive
            FROM dbo.DocumentSeriesCursors WITH (UPDLOCK,HOLDLOCK)
            WHERE DocumentSeriesId=@SeriesId;
            IF @Value IS NULL
            BEGIN
              SELECT @Value=
                CASE WHEN COALESCE(MAX(DocumentConsecutive)+1,@RangeStart)<@RangeStart
                     THEN @RangeStart
                     ELSE COALESCE(MAX(DocumentConsecutive)+1,@RangeStart) END
              FROM dbo.SalesDocuments WITH (UPDLOCK,HOLDLOCK)
              WHERE DocumentSeriesId=@SeriesId;
              INSERT dbo.DocumentSeriesCursors(DocumentSeriesId,NextConsecutive,UpdatedAt)
              VALUES(@SeriesId,@Value+1,@Now);
            END
            ELSE
              UPDATE dbo.DocumentSeriesCursors
              SET NextConsecutive=@Value+1,UpdatedAt=@Now
              WHERE DocumentSeriesId=@SeriesId;
            SELECT @Value;
            """;
        command.Parameters.AddRange([
            P("@SeriesId", configuration.DocumentSeriesId),
            P("@RangeStart", configuration.DocumentRangeStart),
            P("@Now", now)
        ]);
        var value = Convert.ToInt64(
            await command.ExecuteScalarAsync(ct),
            CultureInfo.InvariantCulture);
        if (value > configuration.DocumentRangeEnd)
            throw new OnlineSalesDraftValidationException(
                "La numeración Auraly de la sede está agotada.");
        return value;
    }

    internal static async Task<long> ConsumeFiscalNumberAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CheckoutConfiguration configuration,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @Value BIGINT;
            SELECT @Value=NextConsecutive
            FROM dbo.FiscalSeriesCursors WITH (UPDLOCK,HOLDLOCK)
            WHERE SeriesId=@SeriesId;
            IF @Value IS NULL
            BEGIN
              SELECT @Value=
                CASE WHEN COALESCE(MAX(FiscalConsecutive)+1,@RangeStart)<@RangeStart
                     THEN @RangeStart
                     ELSE COALESCE(MAX(FiscalConsecutive)+1,@RangeStart) END
              FROM dbo.SalesDocuments WITH (UPDLOCK,HOLDLOCK)
              WHERE FiscalSeriesId=@SeriesId;
              INSERT dbo.FiscalSeriesCursors(SeriesId,NextConsecutive,UpdatedAt)
              VALUES(@SeriesId,@Value+1,@Now);
            END
            ELSE
              UPDATE dbo.FiscalSeriesCursors
              SET NextConsecutive=@Value+1,UpdatedAt=@Now
              WHERE SeriesId=@SeriesId;
            SELECT @Value;
            """;
        command.Parameters.AddRange([
            P("@SeriesId", configuration.FiscalSeriesId),
            P("@RangeStart", configuration.FiscalRangeStart),
            P("@Now", now)
        ]);
        var value = Convert.ToInt64(
            await command.ExecuteScalarAsync(ct),
            CultureInfo.InvariantCulture);
        if (value > configuration.FiscalRangeEnd)
            throw new OnlineSalesDraftValidationException(
                "La numeración DIAN de la sede está agotada.");
        return value;
    }

    private static async Task<string> ResolveCustomerIdentificationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid? customerId,
        CancellationToken ct)
    {
        if (customerId is null)
            return "222222222222";
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT NULLIF(p.Identification,N'')
            FROM dbo.Customers c
            JOIN dbo.Parties p ON p.PartyId=c.PartyId
            WHERE c.CustomerId=@CustomerId AND c.BusinessId=@BusinessId
              AND c.IsActive=1 AND p.IsActive=1;
            """;
        command.Parameters.AddRange([
            P("@CustomerId", customerId),
            P("@BusinessId", businessId)
        ]);
        return await command.ExecuteScalarAsync(ct) as string
            ?? "222222222222";
    }

    internal static async Task<PosSaleUblPartyContract> ReadCustomerPartyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid? customerId,
        CheckoutConfiguration configuration,
        CancellationToken ct)
    {
        if (customerId is null)
            return FinalConsumer(configuration);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT p.PartyType,p.Identification,p.VerificationDigit,
                   p.IdentificationTypeCode,
                   COALESCE(p.LegalName,p.DisplayName,
                     NULLIF(LTRIM(RTRIM(CONCAT(p.FirstName,N' ',p.LastName))),N'')),
                   COALESCE(p.DisplayName,p.LegalName),
                   country.Code,country.Name,division.Code,division.Name,
                   city.Code,city.Name,site.AddressLine,
                   email.Value,phone.Value
            FROM dbo.Customers c
            JOIN dbo.Parties p ON p.PartyId=c.PartyId
            OUTER APPLY(
              SELECT TOP(1) value.* FROM dbo.PartySites value
              WHERE value.PartyId=p.PartyId AND value.IsActive=1
              ORDER BY value.IsPrimary DESC,value.CreatedAt,value.PartySiteId) site
            LEFT JOIN dbo.Countries country ON country.CountryId=site.CountryId
            LEFT JOIN dbo.AdministrativeDivisions division
              ON division.AdministrativeDivisionId=site.AdministrativeDivisionId
            LEFT JOIN dbo.Cities city ON city.CityId=site.CityId
            OUTER APPLY(
              SELECT TOP(1) value.Value FROM dbo.PartyContacts value
              WHERE value.PartyId=p.PartyId AND value.ContactType=N'Email'
                AND value.IsActive=1 ORDER BY value.IsPrimary DESC,value.CreatedAt) email
            OUTER APPLY(
              SELECT TOP(1) value.Value FROM dbo.PartyContacts value
              WHERE value.PartyId=p.PartyId AND value.ContactType=N'Phone'
                AND value.IsActive=1 ORDER BY value.IsPrimary DESC,value.CreatedAt) phone
            WHERE c.CustomerId=@CustomerId AND c.BusinessId=@BusinessId
              AND c.IsActive=1 AND p.IsActive=1;
            """;
        command.Parameters.AddRange([
            P("@CustomerId", customerId),
            P("@BusinessId", businessId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(1))
            return FinalConsumer(configuration);
        var fallback = configuration.Supplier.Address;
        return new PosSaleUblPartyContract(
            reader.GetString(1),
            reader.IsDBNull(2) ? "0" : reader.GetString(2),
            reader.IsDBNull(3) ? "13" : reader.GetString(3),
            reader.GetString(0) == "Organization" ? "1" : "2",
            reader.IsDBNull(4) ? "Consumidor final" : reader.GetString(4),
            reader.IsDBNull(5)
                ? reader.IsDBNull(4) ? "Consumidor final" : reader.GetString(4)
                : reader.GetString(5),
            "R-99-PN",
            "01",
            "IVA",
            new PosSaleUblAddressContract(
                reader.IsDBNull(10) ? fallback.MunicipalityCode : reader.GetString(10),
                reader.IsDBNull(11) ? fallback.CityName : reader.GetString(11),
                reader.IsDBNull(9) ? fallback.DepartmentName : reader.GetString(9),
                reader.IsDBNull(8) ? fallback.DepartmentCode : reader.GetString(8),
                reader.IsDBNull(12) ? fallback.AddressLine : reader.GetString(12),
                reader.IsDBNull(6) ? fallback.CountryCode : reader.GetString(6),
                reader.IsDBNull(7) ? fallback.CountryName : reader.GetString(7)),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14));
    }

    private static PosSaleUblPartyContract FinalConsumer(
        CheckoutConfiguration configuration) =>
        new(
            "222222222222",
            "0",
            "13",
            "2",
            "Consumidor final",
            "Consumidor final",
            "R-99-PN",
            "01",
            "IVA",
            configuration.Supplier.Address);

    private static string PaymentMeansCode(string methodCode) => methodCode switch
    {
        "Cash" => "10",
        "DebitCard" => "49",
        "CreditCard" => "48",
        "Transfer" => "42",
        _ => throw new OnlineSalesDraftValidationException(
            "El medio de pago no tiene equivalencia fiscal configurada.")
    };

    private static string TaxName(string taxCode) => taxCode switch
    {
        "01" => "IVA",
        "04" => "INC",
        _ => "Impuesto"
    };

    private static string CheckoutHash(
        Guid draftId,
        CompleteOnlineSalesDraftRequest request)
    {
        var value = new StringBuilder()
            .Append(draftId.ToString("D"))
            .Append('|')
            .Append(request.ExpectedVersion.ToString(CultureInfo.InvariantCulture))
            .Append('|').Append(request.DocumentType)
            .Append('|').Append(request.FiscalHabilitationOnly ? "habilitation" : "economic");
        foreach (var payment in request.Payments)
        {
            value.Append('|')
                .Append(payment.MethodCode)
                .Append(':')
                .Append(payment.Amount.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(payment.Reference?.Trim())
                .Append(':')
                .Append(payment.CardFranchiseCode?.Trim())
                .Append(':')
                .Append(payment.ApprovalNumber?.Trim())
                .Append(':')
                .Append(payment.BankAccountId?.ToString("D"))
                .Append(':')
                .Append(payment.Notes?.Trim());
        }
        if (request.Credit is not null)
            value.Append("|credit:").Append(request.Credit.Amount.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(request.Credit.DueDate.ToString("O", CultureInfo.InvariantCulture));

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }

    internal sealed record CheckoutConfiguration(
        Guid DocumentSeriesId,
        string DocumentPrefix,
        string SeriesCode,
        byte Padding,
        long DocumentRangeStart,
        long DocumentRangeEnd,
        Guid FiscalSeriesId,
        string FiscalPrefix,
        long FiscalRangeStart,
        long FiscalRangeEnd,
        Guid FiscalAuthorizationId,
        string AuthorizationNumber,
        string SupplierTaxId,
        FiscalEnvironment Environment,
        string QrValidationUrl,
        string TechnicalKeyVersion,
        DateOnly ValidFrom,
        DateOnly ValidUntil,
        Guid FiscalIssuerConfigurationId,
        string SoftwareIdentificationCode,
        PosSaleUblPartyContract Supplier,
        long AuthorizationRangeStart,
        long AuthorizationRangeEnd);
}
