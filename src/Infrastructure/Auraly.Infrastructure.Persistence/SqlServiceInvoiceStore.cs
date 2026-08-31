using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Core;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlServiceInvoiceStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider time,
    IFiscalTechnicalKeyProvider technicalKeys) : IServiceInvoiceStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<BillableServicePage> SearchServicesAsync(
        ServiceInvoiceUserIdentity user,
        ServiceInvoiceSearchRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await EnsureBusinessAsync(connection, null, user, request.BusinessId, cancellationToken);
        var query = request.Query?.Trim();
        await using var command = new SqlCommand("""
            SELECT COUNT(*)
            FROM billing.BillableServices service
            WHERE service.BusinessId=@BusinessId AND service.IsActive=1
              AND (@Query IS NULL OR service.Code LIKE @Like OR service.Name LIKE @Like
                   OR service.Description LIKE @Like);
            SELECT service.BillableServiceId,service.Code,service.Name,service.Description,
                   service.UnitLabel,service.UblUnitCode,service.UnitPrice,
                   tax.DianTaxCode,tax.Name,tax.Rate
            FROM billing.BillableServices service
            JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=service.SalesTaxProfileId
            WHERE service.BusinessId=@BusinessId AND service.IsActive=1 AND tax.IsActive=1
              AND (@Query IS NULL OR service.Code LIKE @Like OR service.Name LIKE @Like
                   OR service.Description LIKE @Like)
            ORDER BY service.Name,service.Code
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, connection);
        AddSearch(command, request, query);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var total = reader.GetInt32(0);
        await reader.NextResultAsync(cancellationToken);
        var items = new List<BillableServiceItem>();
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetDecimal(6), reader.GetString(7),
                reader.GetString(8), reader.GetDecimal(9)));
        return new(items, request.Page, request.PageSize, total);
    }

    public async Task<ServiceInvoiceCustomerPage> SearchCustomersAsync(
        ServiceInvoiceUserIdentity user,
        ServiceInvoiceSearchRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await EnsureBusinessAsync(connection, null, user, request.BusinessId, cancellationToken);
        var query = request.Query?.Trim();
        await using var command = new SqlCommand("""
            SELECT COUNT(*)
            FROM dbo.Customers customer
            JOIN dbo.Parties party ON party.PartyId=customer.PartyId
            WHERE customer.BusinessId=@BusinessId AND customer.IsActive=1 AND party.IsActive=1
              AND (@Query IS NULL OR party.Identification LIKE @Like
                   OR COALESCE(party.DisplayName,party.LegalName,
                     LTRIM(RTRIM(CONCAT(party.FirstName,N' ',party.LastName)))) LIKE @Like);
            SELECT customer.CustomerId,party.Identification,
                   COALESCE(party.DisplayName,party.LegalName,
                     LTRIM(RTRIM(CONCAT(party.FirstName,N' ',party.LastName)))),email.Value
            FROM dbo.Customers customer
            JOIN dbo.Parties party ON party.PartyId=customer.PartyId
            OUTER APPLY(
              SELECT TOP(1) value.Value FROM dbo.PartyContacts value
              WHERE value.PartyId=party.PartyId AND value.ContactType=N'Email'
                AND value.IsActive=1
              ORDER BY value.IsPrimary DESC,value.CreatedAt,value.PartyContactId) email
            WHERE customer.BusinessId=@BusinessId AND customer.IsActive=1 AND party.IsActive=1
              AND (@Query IS NULL OR party.Identification LIKE @Like
                   OR COALESCE(party.DisplayName,party.LegalName,
                     LTRIM(RTRIM(CONCAT(party.FirstName,N' ',party.LastName)))) LIKE @Like)
            ORDER BY 3,party.Identification
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, connection);
        AddSearch(command, request, query);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var total = reader.GetInt32(0);
        await reader.NextResultAsync(cancellationToken);
        var items = new List<ServiceInvoiceCustomerItem>();
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        return new(items, request.Page, request.PageSize, total);
    }

    public async Task<ServiceInvoiceHistoryPage> SearchInvoicesAsync(
        ServiceInvoiceUserIdentity user,
        ServiceInvoiceHistoryRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await EnsureBusinessAsync(connection, null, user, request.BusinessId, cancellationToken);
        var query = request.Query?.Trim();
        await using var command = new SqlCommand("""
            SELECT COUNT(*)
            FROM dbo.SalesDocuments document
            LEFT JOIN dbo.Customers customer ON customer.CustomerId=document.CustomerId
            LEFT JOIN dbo.Parties party ON party.PartyId=customer.PartyId
            WHERE document.BusinessId=@BusinessId AND document.DocumentType=N'ServiceInvoice'
              AND (@Query IS NULL OR document.DocumentNumber LIKE @Like
                   OR document.FiscalNumber LIKE @Like
                   OR document.CustomerIdentification LIKE @Like
                   OR COALESCE(party.DisplayName,party.LegalName,
                     LTRIM(RTRIM(CONCAT(party.FirstName,N' ',party.LastName)))) LIKE @Like)
              AND (@From IS NULL OR document.IssuedAt>=@From)
              AND (@ToExclusive IS NULL OR document.IssuedAt<@ToExclusive)
              AND (@FiscalStatus IS NULL OR document.FiscalStatus=@FiscalStatus);
            SELECT document.DocumentId,document.DocumentNumber,document.FiscalNumber,
                   document.IssuedAt,document.CustomerIdentification,
                   COALESCE(party.DisplayName,party.LegalName,
                     LTRIM(RTRIM(CONCAT(party.FirstName,N' ',party.LastName))),
                     document.CustomerIdentification),
                   document.PayableAmount,document.CreditAmount,document.FiscalStatus
            FROM dbo.SalesDocuments document
            LEFT JOIN dbo.Customers customer ON customer.CustomerId=document.CustomerId
            LEFT JOIN dbo.Parties party ON party.PartyId=customer.PartyId
            WHERE document.BusinessId=@BusinessId AND document.DocumentType=N'ServiceInvoice'
              AND (@Query IS NULL OR document.DocumentNumber LIKE @Like
                   OR document.FiscalNumber LIKE @Like
                   OR document.CustomerIdentification LIKE @Like
                   OR COALESCE(party.DisplayName,party.LegalName,
                     LTRIM(RTRIM(CONCAT(party.FirstName,N' ',party.LastName)))) LIKE @Like)
              AND (@From IS NULL OR document.IssuedAt>=@From)
              AND (@ToExclusive IS NULL OR document.IssuedAt<@ToExclusive)
              AND (@FiscalStatus IS NULL OR document.FiscalStatus=@FiscalStatus)
            ORDER BY document.IssuedAt DESC,document.DocumentId DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, connection);
        Add(command, "@BusinessId", request.BusinessId);
        Add(command, "@Query", string.IsNullOrWhiteSpace(query) ? DBNull.Value : query);
        Add(command, "@Like", string.IsNullOrWhiteSpace(query) ? DBNull.Value : $"%{query}%");
        Add(command, "@From", request.From is null
            ? DBNull.Value
            : request.From.Value.ToDateTime(TimeOnly.MinValue));
        Add(command, "@ToExclusive", request.To is null
            ? DBNull.Value
            : request.To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
        Add(command, "@FiscalStatus", string.IsNullOrWhiteSpace(request.FiscalStatus)
            ? DBNull.Value
            : request.FiscalStatus.Trim());
        Add(command, "@Offset", (request.Page - 1) * request.PageSize);
        Add(command, "@PageSize", request.PageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var total = reader.GetInt32(0);
        await reader.NextResultAsync(cancellationToken);
        var items = new List<ServiceInvoiceHistoryItem>();
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4),
                reader.GetString(5), reader.GetDecimal(6), reader.GetDecimal(7),
                reader.GetString(8)));
        return new(items, request.Page, request.PageSize, total);
    }

    public async Task<ServiceInvoiceDetail?> GetInvoiceAsync(
        ServiceInvoiceUserIdentity user,
        Guid businessId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await EnsureBusinessAsync(connection, null, user, businessId, cancellationToken);
        await using var command = new SqlCommand("""
            SELECT document.DocumentId,document.BusinessId,business.Name,
                   document.DocumentNumber,document.FiscalNumber,document.IssuedAt,
                   document.CustomerIdentification,
                   COALESCE(party.DisplayName,party.LegalName,
                     LTRIM(RTRIM(CONCAT(party.FirstName,N' ',party.LastName))),
                     document.CustomerIdentification),email.Value,
                   document.UntaxedAmount,document.TaxAmount,document.PayableAmount,
                   document.CreditAmount,document.CreditDueDate,
                   COALESCE(document.CufeCalculated,document.CufeReceived),
                   document.FiscalStatus,snapshot.SnapshotJson
            FROM dbo.SalesDocuments document
            JOIN dbo.Businesses business ON business.BusinessId=document.BusinessId
            JOIN sales.SalesDocumentServiceFiscalSnapshots snapshot
              ON snapshot.DocumentId=document.DocumentId
            LEFT JOIN dbo.Customers customer ON customer.CustomerId=document.CustomerId
            LEFT JOIN dbo.Parties party ON party.PartyId=customer.PartyId
            OUTER APPLY(
              SELECT TOP(1) contact.Value FROM dbo.PartyContacts contact
              WHERE contact.PartyId=party.PartyId AND contact.ContactType=N'Email'
                AND contact.IsActive=1
              ORDER BY contact.IsPrimary DESC,contact.CreatedAt,contact.PartyContactId) email
            WHERE document.BusinessId=@BusinessId AND document.DocumentId=@DocumentId
              AND document.DocumentType=N'ServiceInvoice';
            SELECT line.LineNumber,line.ServiceCode,line.Description,line.UnitCode,
                   line.Quantity,line.UnitPrice,line.DiscountAmount,line.UntaxedAmount,
                   line.TaxName,line.TaxRate,line.TaxAmount,line.LineTotal
            FROM sales.SalesDocumentServiceLines line
            JOIN dbo.SalesDocuments document ON document.DocumentId=line.DocumentId
            WHERE document.BusinessId=@BusinessId AND document.DocumentId=@DocumentId
              AND document.DocumentType=N'ServiceInvoice'
            ORDER BY line.LineNumber;
            SELECT payment.PaymentNumber,payment.MethodCode,payment.Amount,payment.Reference
            FROM dbo.SalesPayments payment
            JOIN dbo.SalesDocuments document ON document.DocumentId=payment.DocumentId
            WHERE document.BusinessId=@BusinessId AND document.DocumentId=@DocumentId
              AND document.DocumentType=N'ServiceInvoice'
            ORDER BY payment.PaymentNumber;
            """, connection);
        Add(command, "@BusinessId", businessId);
        Add(command, "@DocumentId", documentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var snapshot = ServiceInvoiceSnapshotSerializer.Deserialize(reader.GetString(16));
        var header = new
        {
            DocumentId = reader.GetGuid(0), BusinessId = reader.GetGuid(1),
            BusinessName = reader.GetString(2), DocumentNumber = reader.GetString(3),
            FiscalNumber = reader.GetString(4), IssuedAt = reader.GetFieldValue<DateTimeOffset>(5),
            Identification = reader.GetString(6), CustomerName = reader.GetString(7),
            Email = reader.IsDBNull(8) ? null : reader.GetString(8),
            Untaxed = reader.GetDecimal(9), Tax = reader.GetDecimal(10),
            Payable = reader.GetDecimal(11), Credit = reader.GetDecimal(12),
            Due = reader.IsDBNull(13) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(13),
            Cufe = reader.GetString(14), FiscalStatus = reader.GetString(15)
        };
        await reader.NextResultAsync(cancellationToken);
        var lines = new List<ServiceInvoiceDetailLine>();
        while (await reader.ReadAsync(cancellationToken))
            lines.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetDecimal(4), reader.GetDecimal(5),
                reader.GetDecimal(6), reader.GetDecimal(7), reader.GetString(8),
                reader.GetDecimal(9), reader.GetDecimal(10), reader.GetDecimal(11)));
        await reader.NextResultAsync(cancellationToken);
        var payments = new List<ServiceInvoiceDetailPayment>();
        while (await reader.ReadAsync(cancellationToken))
            payments.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        return new(header.DocumentId, header.BusinessId, header.BusinessName,
            header.DocumentNumber, header.FiscalNumber, header.IssuedAt,
            header.Identification, header.CustomerName, header.Email,
            header.Untaxed, header.Tax, header.Payable, header.Credit, header.Due,
            header.Cufe, header.FiscalStatus, snapshot.FiscalSnapshot.QrPayload,
            lines, payments);
    }

    public async Task<IssuedServiceInvoice> IssueAsync(
        ServiceInvoiceUserIdentity user,
        IssueServiceInvoiceRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var requestHash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, Json));
        var keyReference = await ReadFiscalKeyReferenceAsync(
            user, request.BusinessId, cancellationToken);
        var fiscalMaterial = await technicalKeys.ResolveAsync(keyReference, cancellationToken)
            ?? throw new ServiceInvoiceValidationException(
                "La clave técnica de la resolución fiscal no está disponible.");

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await EnsureBusinessAsync(
                connection, transaction, user, request.BusinessId, cancellationToken);
            var replay = await ReadReplayAsync(
                connection, transaction, request.BusinessId, idempotencyKey,
                requestHash, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            var now = time.GetUtcNow();
            var configuration = await SqlOnlineSalesDraftStore.ReadCheckoutConfigurationAsync(
                connection, transaction, request.BusinessId,
                ServiceInvoiceDocumentTypes.ServiceInvoice,
                PosSaleDocumentTypes.Invoice, now, cancellationToken);
            if (configuration.SupplierTaxId != fiscalMaterial.SupplierTaxId ||
                configuration.Environment != fiscalMaterial.Environment ||
                configuration.AuthorizationNumber != keyReference.AuthorizationNumber)
                throw new ServiceInvoiceValidationException(
                    "La resolución fiscal cambió durante la emisión.");

            var customer = await ReadRequiredCustomerAsync(
                connection, transaction, request.BusinessId, request.CustomerId,
                configuration, cancellationToken);
            var lines = await BuildLinesAsync(
                connection, transaction, request.BusinessId, request.Lines,
                cancellationToken);
            var untaxed = Money(lines.Sum(line => line.UntaxedAmount));
            var tax = Money(lines.Sum(line => line.TaxAmount));
            var payable = Money(lines.Sum(line => line.LineTotal));
            if (payable <= 0 || request.CreditAmount > payable)
                throw new ServiceInvoiceValidationException(
                    "El total o el valor financiado de la factura no es válido.");
            if (request.CreditDueDate is not null && request.CreditDueDate <= now)
                throw new ServiceInvoiceValidationException(
                    "El vencimiento del crédito debe ser posterior a la emisión.");
            var paidAmount = Money(payable - request.CreditAmount);
            var paymentMethod = NormalizePaymentMethod(request.PaymentMethodCode, paidAmount);
            var documentId = ids.NewId();
            if (!await SqlDianDocumentQuota.TryReserveAsync(
                    connection, transaction, request.BusinessId, documentId,
                    "Invoice", now, cancellationToken))
                throw new ServiceInvoiceValidationException(
                    "No hay cupo de documentos DIAN. Amplía el paquete antes de emitir la factura de servicio.");

            var documentConsecutive = await SqlOnlineSalesDraftStore.ConsumeDocumentNumberAsync(
                connection, transaction, configuration, now, cancellationToken);
            var fiscalConsecutive = await SqlOnlineSalesDraftStore.ConsumeFiscalNumberAsync(
                connection, transaction, configuration, now, cancellationToken);
            var number = AuralyDocumentNumberAssignment.Create(
                configuration.DocumentSeriesId, ServiceInvoiceDocumentTypes.ServiceInvoice,
                configuration.DocumentPrefix, configuration.SeriesCode,
                documentConsecutive, configuration.Padding);
            var fiscalNumber = $"{configuration.FiscalPrefix}{fiscalConsecutive}";
            var taxes = lines.GroupBy(line => line.TaxCode, StringComparer.Ordinal)
                .Select(group => new PosSaleTaxContract(
                    group.Key, Money(group.Sum(line => line.TaxAmount))))
                .OrderBy(value => value.Code, StringComparer.Ordinal)
                .ToArray();
            var cufe = CufeCalculator.Calculate(new CufeInput(
                fiscalNumber, now, untaxed, payable, configuration.SupplierTaxId,
                customer.Identification, fiscalMaterial.TechnicalKey,
                fiscalMaterial.Environment,
                taxes.Select(value => new FiscalTaxAmount(value.Code, value.Amount))),
                fiscalMaterial.QrValidationUrl);
            var snapshot = new ServiceInvoiceSnapshot(
                user.TenantId, request.BusinessId, request.CustomerId, documentId,
                null, null, user.UserId,
                new PosSaleDocumentNumberContract(number.SeriesId, number.DocumentType,
                    number.Prefix, number.SeriesCode, number.Consecutive,
                    number.Padding, number.FullNumber),
                new PosSaleCommercialSnapshotContract(
                    ServiceInvoiceDocumentTypes.ServiceInvoice, now,
                    customer.Identification, taxes, untaxed, tax, payable),
                new PosSaleFiscalSnapshotContract(
                    configuration.FiscalSeriesId, configuration.FiscalAuthorizationId,
                    configuration.AuthorizationNumber,
                    ServiceInvoiceDocumentTypes.ServiceInvoice, fiscalNumber,
                    configuration.FiscalPrefix, fiscalConsecutive, now,
                    configuration.SupplierTaxId, customer.Identification,
                    (int)configuration.Environment, configuration.TechnicalKeyVersion,
                    taxes, untaxed, tax, payable, cufe.Cufe, cufe.QrPayload),
                new PosSaleUblSnapshotContract(
                    configuration.FiscalIssuerConfigurationId, "COP", "01",
                    configuration.Supplier, customer,
                    new PosSaleUblAuthorizationContract(
                        configuration.AuthorizationNumber, configuration.ValidFrom,
                        configuration.ValidUntil, configuration.FiscalPrefix,
                        configuration.AuthorizationRangeStart, configuration.AuthorizationRangeEnd),
                    configuration.SoftwareIdentificationCode,
                    lines.Select(line => new PosSaleUblLineContract(
                        line.LineNumber, line.ServiceCode, "999", line.UnitCode,
                        line.TaxName, line.TaxRate)).ToArray(),
                    request.CreditAmount > 0 ? "2" : "1",
                    PaymentMeansCode(paymentMethod),
                    DateOnly.FromDateTime((request.CreditDueDate ?? now).Date),
                    request.PaymentReference?.Trim()),
                lines,
                new PosSalePaymentContract(1, paymentMethod, paidAmount,
                    request.PaymentReference?.Trim()));
            await SqlServiceInvoiceDocumentWriter.PersistAsync(
                connection, transaction, ids,
                new ServiceInvoiceDocumentWrite(
                    user.TenantId, request.CustomerId, snapshot, configuration,
                    $"service-invoice:{idempotencyKey}", requestHash,
                    request.CreditAmount, request.CreditDueDate, paymentMethod,
                    request.PaymentReference?.Trim(), now),
                now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(documentId, number.FullNumber, fiscalNumber, cufe.Cufe,
                untaxed, tax, payable, request.CreditAmount,
                "PendingGeneration", false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<FiscalKeyReference> ReadFiscalKeyReferenceAsync(
        ServiceInvoiceUserIdentity user,
        Guid businessId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT TOP(2) business.TenantId,business.BusinessId,
                   fiscalAuthorization.AuthorizationNumber,
                   fiscalAuthorization.TechnicalKeyVersion,fiscalAuthorization.Environment
            FROM dbo.Businesses business
            JOIN dbo.FiscalSeries series ON series.BusinessId=business.BusinessId
             AND series.DocumentType=N'SalesInvoice' AND series.EmitterKind=N'Server'
             AND series.DeviceId IS NULL AND series.IsActive=1
            JOIN dbo.FiscalAuthorizations fiscalAuthorization
              ON fiscalAuthorization.FiscalAuthorizationId=series.FiscalAuthorizationId
             AND fiscalAuthorization.BusinessId=business.BusinessId
             AND fiscalAuthorization.IsActive=1
            WHERE business.BusinessId=@BusinessId AND business.TenantId=@TenantId
              AND business.IsActive=1
              AND CONVERT(date,SYSDATETIMEOFFSET()) BETWEEN fiscalAuthorization.ValidFrom AND fiscalAuthorization.ValidUntil
            ORDER BY series.SeriesId;
            """, connection);
        Add(command, "@BusinessId", businessId);
        Add(command, "@TenantId", user.TenantId);
        var rows = new List<FiscalKeyReference>(2);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetString(3), (FiscalEnvironment)reader.GetByte(4)));
        return rows.Count == 1 ? rows[0] : throw new ServiceInvoiceValidationException(
            rows.Count == 0
                ? "El negocio no tiene una resolución online activa y vigente."
                : "El negocio tiene una configuración fiscal online ambigua.");
    }

    private static async Task<IssuedServiceInvoice?> ReadReplayAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        string idempotencyKey,
        byte[] requestHash,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT DocumentId,DocumentNumber,FiscalNumber,CufeCalculated,
                   UntaxedAmount,TaxAmount,PayableAmount,CreditAmount,FiscalStatus,RequestHash
            FROM dbo.SalesDocuments WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND IdempotencyKey=@IdempotencyKey
              AND DocumentType=N'ServiceInvoice';
            """, connection, transaction);
        Add(command, "@BusinessId", businessId);
        Add(command, "@IdempotencyKey", $"service-invoice:{idempotencyKey}");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (reader.IsDBNull(9) || !reader.GetFieldValue<byte[]>(9).SequenceEqual(requestHash))
            throw new ServiceInvoiceIdempotencyException(
                "La clave de idempotencia ya fue utilizada con otro contenido.");
        return new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetDecimal(4), reader.GetDecimal(5),
            reader.GetDecimal(6), reader.GetDecimal(7), reader.GetString(8), true);
    }

    private static async Task<PosSaleUblPartyContract> ReadRequiredCustomerAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid customerId,
        SqlOnlineSalesDraftStore.CheckoutConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await using (var existence = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.Customers customer
            JOIN dbo.Parties party ON party.PartyId=customer.PartyId
            WHERE customer.CustomerId=@CustomerId AND customer.BusinessId=@BusinessId
              AND customer.IsActive=1 AND party.IsActive=1
              AND NULLIF(LTRIM(RTRIM(party.Identification)),N'') IS NOT NULL;
            """, connection, transaction))
        {
            Add(existence, "@CustomerId", customerId);
            Add(existence, "@BusinessId", businessId);
            if (Convert.ToInt32(await existence.ExecuteScalarAsync(cancellationToken),
                    CultureInfo.InvariantCulture) != 1)
                throw new ServiceInvoiceValidationException(
                    "El cliente no existe, está inactivo o no tiene identificación fiscal.");
        }
        return await SqlOnlineSalesDraftStore.ReadCustomerPartyAsync(
            connection, transaction, businessId, customerId, configuration,
            cancellationToken);
    }

    private static async Task<IReadOnlyList<ServiceInvoiceLineContract>> BuildLinesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        IReadOnlyList<IssueServiceInvoiceLineRequest> requested,
        CancellationToken cancellationToken)
    {
        var lines = new List<ServiceInvoiceLineContract>(requested.Count);
        for (var index = 0; index < requested.Count; index++)
        {
            var input = requested[index];
            await using var command = new SqlCommand("""
                SELECT service.BillableServiceId,service.Code,service.Name,
                       service.UblUnitCode,service.UnitPrice,
                       tax.DianTaxCode,tax.Name,tax.Rate
                FROM billing.BillableServices service WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=service.SalesTaxProfileId
                WHERE service.BillableServiceId=@ServiceId
                  AND service.BusinessId=@BusinessId
                  AND service.IsActive=1 AND tax.IsActive=1;
                """, connection, transaction);
            Add(command, "@ServiceId", input.BillableServiceId);
            Add(command, "@BusinessId", businessId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ServiceInvoiceValidationException(
                    "Uno de los servicios no existe o está inactivo.");
            var serviceId = reader.GetGuid(0);
            var code = reader.GetString(1);
            var name = reader.GetString(2);
            var unitCode = reader.GetString(3);
            var catalogPrice = reader.GetDecimal(4);
            var taxCode = reader.GetString(5);
            var taxName = reader.GetString(6);
            var taxRate = reader.GetDecimal(7);
            var unitPrice = Money(input.UnitPrice ?? catalogPrice);
            var gross = Money(unitPrice * input.Quantity);
            var discount = input.DiscountKind?.Trim() switch
            {
                null or "" or "None" when input.DiscountValue == 0 => 0,
                "Percentage" when input.DiscountValue <= 100 =>
                    Money(gross * input.DiscountValue / 100m),
                "Value" when input.DiscountValue <= gross => Money(input.DiscountValue),
                _ => throw new ServiceInvoiceValidationException(
                    "El descuento debe ser porcentaje entre 0 y 100 o un valor que no supere la línea.")
            };
            var baseAmount = Money(gross - discount);
            var taxAmount = Money(baseAmount * taxRate / 100m);
            var total = Money(baseAmount + taxAmount);
            if (total <= 0)
                throw new ServiceInvoiceValidationException(
                    "Una línea de servicio no puede quedar en cero.");
            lines.Add(new(index + 1, serviceId, code,
                string.IsNullOrWhiteSpace(input.Description) ? name : input.Description.Trim(),
                unitCode, taxCode, taxName, taxRate, input.Quantity, unitPrice,
                discount, baseAmount, taxAmount, total));
        }
        return lines;
    }

    private static async Task EnsureBusinessAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        ServiceInvoiceUserIdentity user,
        Guid businessId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.Businesses
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND IsActive=1;
            """, connection, transaction);
        Add(command, "@BusinessId", businessId);
        Add(command, "@TenantId", user.TenantId);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture) != 1)
            throw new ServiceInvoiceForbiddenException(
                "El negocio no pertenece al tenant autenticado.");
    }

    private static string NormalizePaymentMethod(string value, decimal paidAmount)
    {
        if (paidAmount == 0) return "Transfer";
        return value?.Trim() switch
        {
            "Cash" => "Cash",
            "DebitCard" => "DebitCard",
            "CreditCard" => "CreditCard",
            "Transfer" => "Transfer",
            _ => throw new ServiceInvoiceValidationException(
                "El medio de pago no es válido.")
        };
    }

    private static string PaymentMeansCode(string methodCode) => methodCode switch
    {
        "Cash" => "10",
        "DebitCard" => "49",
        "CreditCard" => "48",
        "Transfer" => "42",
        _ => throw new ServiceInvoiceValidationException(
            "El medio de pago no tiene equivalencia fiscal.")
    };

    private static void AddSearch(
        SqlCommand command,
        ServiceInvoiceSearchRequest request,
        string? query)
    {
        Add(command, "@BusinessId", request.BusinessId);
        Add(command, "@Query", string.IsNullOrWhiteSpace(query) ? null : query);
        Add(command, "@Like", string.IsNullOrWhiteSpace(query) ? null : $"%{query}%");
        Add(command, "@Offset", (request.Page - 1) * request.PageSize);
        Add(command, "@PageSize", request.PageSize);
    }

    private static decimal Money(decimal value) => decimal.Round(
        value, 2, MidpointRounding.AwayFromZero);

    private static void Add(SqlCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
