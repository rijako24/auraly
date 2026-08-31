using System.Data;
using System.Globalization;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Contracts.TenantBilling;
using Auraly.Fiscal.Core;
using Auraly.Platform.Application.Identity.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlTenantSubscriptionSettlementService(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider time,
    IFiscalTechnicalKeyProvider technicalKeys) : ITenantSubscriptionSettlementService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<bool> IsSettledAsync(
        Guid paymentTransactionId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM billing.TenantSubscriptionInvoiceLinks
            WHERE PaymentTransactionId=@PaymentId AND EnginesDispatchedAt IS NOT NULL;
            """, connection);
        command.Parameters.AddWithValue("@PaymentId", paymentTransactionId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) == 1;
    }

    public async Task MarkDispatchedAsync(
        Guid paymentTransactionId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            UPDATE billing.TenantSubscriptionInvoiceLinks
            SET EnginesDispatchedAt=COALESCE(EnginesDispatchedAt,SYSDATETIMEOFFSET())
            WHERE PaymentTransactionId=@PaymentId;
            """, connection);
        command.Parameters.AddWithValue("@PaymentId", paymentTransactionId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException(
                "La factura de suscripción no existe para marcar su despacho.");
    }

    public async Task<TenantSubscriptionSettlementResult> SettleAsync(
        PaymentTransaction payment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        if (payment.Status != PaymentTransactionStatus.Confirmed ||
            payment.SubjectType != "TenantSubscription" ||
            payment.SubjectId is not Guid orderId)
            throw new InvalidOperationException(
                "El pago confirmado no corresponde a una renovación de tenant.");

        var keyReference = await ReadFiscalKeyReferenceAsync(
            payment.PaymentTransactionId, orderId, cancellationToken);
        var fiscalMaterial = await technicalKeys.ResolveAsync(
            keyReference, cancellationToken)
            ?? throw new InvalidOperationException(
                "La clave técnica de la resolución fiscal de Auraly no está disponible.");

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var replay = await ReadExistingAsync(
                connection, transaction, payment.PaymentTransactionId, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay with { IsReplay = true };
            }

            var source = await ReadSourceAsync(
                connection, transaction, payment.PaymentTransactionId, orderId,
                cancellationToken);
            if (source.AmountInCents != payment.AmountInCents ||
                !string.Equals(source.PaymentReference, payment.PaymentReferenceId,
                    StringComparison.Ordinal) ||
                source.AmountInCents != ToCents(source.PayableAmount))
                throw new InvalidOperationException(
                    "El pago no coincide exactamente con la orden de renovación vigente.");

            var now = time.GetUtcNow();
            var configuration = await SqlOnlineSalesDraftStore.ReadCheckoutConfigurationAsync(
                connection, transaction, source.BillingBusinessId,
                ServiceInvoiceDocumentTypes.ServiceInvoice,
                PosSaleDocumentTypes.Invoice, now, cancellationToken);
            if (configuration.SupplierTaxId != fiscalMaterial.SupplierTaxId ||
                configuration.Environment != fiscalMaterial.Environment ||
                configuration.AuthorizationNumber != keyReference.AuthorizationNumber)
                throw new InvalidOperationException(
                    "La resolución fiscal cambió mientras se confirmaba el pago.");

            var documentConsecutive = await SqlOnlineSalesDraftStore.ConsumeDocumentNumberAsync(
                connection, transaction, configuration, now, cancellationToken);
            var fiscalConsecutive = await SqlOnlineSalesDraftStore.ConsumeFiscalNumberAsync(
                connection, transaction, configuration, now, cancellationToken);
            var documentNumber = AuralyDocumentNumberAssignment.Create(
                configuration.DocumentSeriesId,
                ServiceInvoiceDocumentTypes.ServiceInvoice,
                configuration.DocumentPrefix,
                configuration.SeriesCode,
                documentConsecutive,
                configuration.Padding);
            var fiscalNumber = $"{configuration.FiscalPrefix}{fiscalConsecutive}";
            var customer = await SqlOnlineSalesDraftStore.ReadCustomerPartyAsync(
                connection, transaction, source.BillingBusinessId,
                source.BillingCustomerId, configuration, cancellationToken);
            var quoteLines = JsonSerializer.Deserialize<IReadOnlyList<TenantQuoteLineDto>>(
                source.LinesJson, Json) ?? throw new InvalidOperationException(
                    "La orden pagada no contiene líneas comerciales válidas.");
            var lines = await BuildLinesAsync(
                connection, transaction, source, quoteLines, cancellationToken);
            var taxes = lines.GroupBy(line => line.TaxCode, StringComparer.Ordinal)
                .Select(group => new PosSaleTaxContract(
                    group.Key, group.Sum(line => line.TaxAmount)))
                .OrderBy(tax => tax.Code, StringComparer.Ordinal)
                .ToArray();
            var cufe = CufeCalculator.Calculate(new CufeInput(
                fiscalNumber, now, source.UntaxedAmount, source.PayableAmount,
                configuration.SupplierTaxId, customer.Identification,
                fiscalMaterial.TechnicalKey, fiscalMaterial.Environment,
                taxes.Select(tax => new FiscalTaxAmount(tax.Code, tax.Amount))),
                fiscalMaterial.QrValidationUrl);
            var documentId = ids.NewId();
            var snapshot = new ServiceInvoiceSnapshot(
                source.BillingTenantId, source.BillingBusinessId,
                source.BillingCustomerId, documentId, payment.PaymentTransactionId,
                orderId, null,
                new PosSaleDocumentNumberContract(documentNumber.SeriesId,
                    documentNumber.DocumentType, documentNumber.Prefix,
                    documentNumber.SeriesCode, documentNumber.Consecutive,
                    documentNumber.Padding, documentNumber.FullNumber),
                new PosSaleCommercialSnapshotContract(
                    ServiceInvoiceDocumentTypes.ServiceInvoice, now,
                    customer.Identification, taxes, source.UntaxedAmount,
                    source.TaxAmount, source.PayableAmount),
                new PosSaleFiscalSnapshotContract(
                    configuration.FiscalSeriesId, configuration.FiscalAuthorizationId,
                    configuration.AuthorizationNumber,
                    ServiceInvoiceDocumentTypes.ServiceInvoice, fiscalNumber,
                    configuration.FiscalPrefix, fiscalConsecutive, now,
                    configuration.SupplierTaxId, customer.Identification,
                    (int)configuration.Environment, configuration.TechnicalKeyVersion,
                    taxes, source.UntaxedAmount, source.TaxAmount,
                    source.PayableAmount, cufe.Cufe, cufe.QrPayload),
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
                    "1", "42", DateOnly.FromDateTime(now.Date),
                    source.ExternalPaymentReference),
                lines,
                new PosSalePaymentContract(1, source.PaymentMethodCode, source.PayableAmount,
                    source.ExternalPaymentReference));
            await PersistAsync(
                connection, transaction, source, snapshot, configuration,
                payment, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(documentId, source.BillingBusinessId, false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<FiscalKeyReference> ReadFiscalKeyReferenceAsync(
        Guid paymentId, Guid orderId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT TOP(2) billingTenant.TenantId,billing.BusinessId,
                   fiscalAuthorization.AuthorizationNumber,
                   fiscalAuthorization.TechnicalKeyVersion,fiscalAuthorization.Environment
            FROM dbo.PaymentTransactions payment
            JOIN billing.TenantSubscriptionRenewalOrders renewal
              ON renewal.TenantSubscriptionRenewalOrderId=payment.SubjectId
             AND renewal.PaymentTransactionId=payment.PaymentTransactionId
            JOIN billing.PlatformBillingSettings settings
              ON settings.PlatformBillingSettingId=1
            JOIN dbo.Businesses billing ON billing.BusinessId=settings.BillingBusinessId
            JOIN dbo.Tenants billingTenant ON billingTenant.TenantId=billing.TenantId
            JOIN dbo.FiscalSeries series ON series.BusinessId=billing.BusinessId
             AND series.DocumentType=N'SalesInvoice' AND series.EmitterKind=N'Server'
             AND series.DeviceId IS NULL AND series.IsActive=1
            JOIN dbo.FiscalAuthorizations fiscalAuthorization
              ON fiscalAuthorization.FiscalAuthorizationId=series.FiscalAuthorizationId
             AND fiscalAuthorization.BusinessId=billing.BusinessId
             AND fiscalAuthorization.IsActive=1
            WHERE payment.PaymentTransactionId=@PaymentId
              AND payment.SubjectType=N'TenantSubscription' AND payment.SubjectId=@OrderId
              AND payment.Status=@Confirmed
              AND CONVERT(date,SYSDATETIMEOFFSET()) BETWEEN fiscalAuthorization.ValidFrom AND fiscalAuthorization.ValidUntil
            ORDER BY series.SeriesId;
            """, connection);
        command.Parameters.AddWithValue("@PaymentId", paymentId);
        command.Parameters.AddWithValue("@OrderId", orderId);
        command.Parameters.AddWithValue("@Confirmed", (int)PaymentTransactionStatus.Confirmed);
        var rows = new List<FiscalKeyReference>(2);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetString(3), (FiscalEnvironment)reader.GetByte(4)));
        return rows.Count == 1 ? rows[0] : throw new InvalidOperationException(
            rows.Count == 0
                ? "Auraly no tiene una resolución online activa y vigente para facturar el pago."
                : "Auraly tiene más de una resolución online activa para facturación.");
    }

    private static async Task<TenantSubscriptionSettlementResult?> ReadExistingAsync(
        SqlConnection connection, SqlTransaction transaction, Guid paymentId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT link.SalesDocumentId,document.BusinessId
            FROM billing.TenantSubscriptionInvoiceLinks link WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.SalesDocuments document ON document.DocumentId=link.SalesDocumentId
            WHERE link.PaymentTransactionId=@PaymentId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@PaymentId", paymentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetGuid(0), reader.GetGuid(1), true)
            : null;
    }

    private static async Task<SettlementSource> ReadSourceAsync(
        SqlConnection connection, SqlTransaction transaction, Guid paymentId,
        Guid orderId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT payment.AmountInCents,payment.PaymentReferenceId,
                   billingTenant.TenantId,billing.BusinessId,
                   subscription.TenantSubscriptionId,subscription.BillingCustomerId,
                   renewal.TenantCommercialPlanId,renewal.BillingPeriod,
                   renewal.TargetPeriodStart,renewal.TargetPeriodEnd,
                   renewal.PayableAmount-renewal.TaxAmount,renewal.TaxAmount,
                   renewal.PayableAmount,renewal.DiscountRate,renewal.Periods,
                   renewal.FullUserLimit,renewal.SellerUserLimit,renewal.PosDeviceLimit,
                   renewal.DianDocumentMonthlyLimit,renewal.PayrollEmployeeLimit,
                   renewal.LinesJson,renewal.Status,renewal.IsCurrent,
                   COALESCE(JSON_VALUE(payment.CheckoutSnapshotJson,N'$.paymentMethodCode'),N'Transfer'),
                   COALESCE(TRY_CONVERT(datetimeoffset(7),JSON_VALUE(payment.CheckoutSnapshotJson,N'$.paidAt')),
                            CONVERT(datetimeoffset(7),payment.ConfirmedAt),SYSDATETIMEOFFSET()),
                   COALESCE(payment.ProviderTransactionId,payment.PaymentReferenceId)
            FROM dbo.PaymentTransactions payment WITH(UPDLOCK,HOLDLOCK)
            JOIN billing.TenantSubscriptionRenewalOrders renewal WITH(UPDLOCK,HOLDLOCK)
              ON renewal.TenantSubscriptionRenewalOrderId=payment.SubjectId
             AND renewal.PaymentTransactionId=payment.PaymentTransactionId
            JOIN billing.TenantSubscriptions subscription WITH(UPDLOCK,HOLDLOCK)
              ON subscription.TenantSubscriptionId=renewal.TenantSubscriptionId
            JOIN billing.PlatformBillingSettings settings WITH(UPDLOCK,HOLDLOCK)
              ON settings.PlatformBillingSettingId=1
            JOIN dbo.Businesses billing ON billing.BusinessId=settings.BillingBusinessId
            JOIN dbo.Tenants billingTenant ON billingTenant.TenantId=billing.TenantId
            WHERE payment.PaymentTransactionId=@PaymentId
              AND payment.SubjectType=N'TenantSubscription' AND payment.SubjectId=@OrderId
              AND payment.Status=@Confirmed;
            """, connection, transaction);
        command.Parameters.AddWithValue("@PaymentId", paymentId);
        command.Parameters.AddWithValue("@OrderId", orderId);
        command.Parameters.AddWithValue("@Confirmed", (int)PaymentTransactionStatus.Confirmed);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException(
                "La orden de renovación confirmada no existe o cambió.");
        if (!reader.GetBoolean(22) || reader.GetString(21) is not ("PendingPayment" or "PaymentConfirmed" or "Invoicing"))
            throw new InvalidOperationException(
                "La orden pagada ya no es la revisión vigente liquidable.");
        return new(reader.GetInt64(0), reader.GetString(1), reader.GetGuid(2),
            reader.GetGuid(3), reader.GetGuid(4), reader.GetGuid(5), reader.GetGuid(6),
            reader.GetString(7), reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetFieldValue<DateTimeOffset>(9), reader.GetDecimal(10),
            reader.GetDecimal(11), reader.GetDecimal(12), reader.GetDecimal(13),
            reader.GetInt32(14), reader.GetInt32(15), reader.GetInt32(16),
            reader.GetInt32(17), reader.GetInt32(18), reader.GetInt32(19),
            reader.GetString(20), reader.GetString(23),
            reader.GetFieldValue<DateTimeOffset>(24), reader.GetString(25));
    }

    private static async Task<IReadOnlyList<ServiceInvoiceLineContract>> BuildLinesAsync(
        SqlConnection connection, SqlTransaction transaction, SettlementSource source,
        IReadOnlyList<TenantQuoteLineDto> quoteLines,
        CancellationToken cancellationToken)
    {
        if (quoteLines.Count == 0)
            throw new InvalidOperationException("La orden pagada no tiene servicios.");
        var lines = new List<ServiceInvoiceLineContract>(quoteLines.Count);
        for (var index = 0; index < quoteLines.Count; index++)
        {
            var quote = quoteLines[index];
            await using var command = new SqlCommand("""
                SELECT service.BillableServiceId,service.Code,service.Name,
                       service.UblUnitCode,tax.DianTaxCode,tax.Name,tax.Rate
                FROM billing.BillableServices service
                JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=service.SalesTaxProfileId
                WHERE service.BusinessId=@BusinessId AND service.Code=@Code
                  AND service.IsActive=1 AND tax.IsActive=1;
                """, connection, transaction);
            command.Parameters.AddWithValue("@BusinessId", source.BillingBusinessId);
            command.Parameters.AddWithValue("@Code", quote.Code);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException(
                    $"El servicio '{quote.Code}' de la orden ya no está disponible.");
            var serviceId = reader.GetGuid(0);
            var code = reader.GetString(1);
            var name = reader.GetString(2);
            var unitCode = reader.GetString(3);
            var taxCode = reader.GetString(4);
            var taxName = reader.GetString(5);
            var taxRate = reader.GetDecimal(6);
            await reader.DisposeAsync();
            if (taxRate != quote.SalesTaxRate)
                throw new InvalidOperationException(
                    $"El impuesto del servicio '{quote.Code}' cambió después de crear la orden.");
            var gross = Money(quote.MonthlyTotalCop * source.Periods);
            var untaxed = Money(gross * (1m - source.DiscountRate));
            var tax = Money(untaxed * taxRate / 100m);
            var description = index == 0
                ? $"{name} · {source.Periods} mes(es) · {source.FullUserLimit} usuarios · {source.SellerUserLimit} vendedores · {source.PosDeviceLimit} cajas · {source.DianDocumentMonthlyLimit} documentos DIAN/mes · {source.PayrollEmployeeLimit} empleados nómina"
                : name;
            lines.Add(new(index + 1, serviceId, code, description, unitCode,
                taxCode, taxName, taxRate, quote.Quantity,
                Money(quote.MonthlyUnitPriceCop * source.Periods),
                Money(gross - untaxed), untaxed, tax, Money(untaxed + tax)));
        }
        var last = lines[^1];
        var untaxedDelta = source.UntaxedAmount - lines.Sum(line => line.UntaxedAmount);
        var taxDelta = source.TaxAmount - lines.Sum(line => line.TaxAmount);
        if (untaxedDelta != 0 || taxDelta != 0)
        {
            var gross = last.UnitPrice * last.Quantity;
            var adjustedUntaxed = last.UntaxedAmount + untaxedDelta;
            var adjustedTax = last.TaxAmount + taxDelta;
            lines[^1] = last with
            {
                DiscountAmount = Money(gross - adjustedUntaxed),
                UntaxedAmount = Money(adjustedUntaxed),
                TaxAmount = Money(adjustedTax),
                LineTotal = Money(adjustedUntaxed + adjustedTax)
            };
        }
        if (lines.Sum(line => line.UntaxedAmount) != source.UntaxedAmount ||
            lines.Sum(line => line.TaxAmount) != source.TaxAmount ||
            lines.Sum(line => line.LineTotal) != source.PayableAmount)
            throw new InvalidOperationException(
                "Las líneas de la orden no concilian con el total pagado.");
        return lines;
    }

    private async Task PersistAsync(
        SqlConnection connection, SqlTransaction transaction, SettlementSource source,
        ServiceInvoiceSnapshot snapshot,
        SqlOnlineSalesDraftStore.CheckoutConfiguration configuration,
        PaymentTransaction payment, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await SqlServiceInvoiceDocumentWriter.PersistAsync(
            connection, transaction, ids,
            new ServiceInvoiceDocumentWrite(
                source.BillingTenantId,
                source.BillingCustomerId,
                snapshot,
                configuration,
                $"tenant-subscription:{payment.PaymentTransactionId:D}",
                null,
                0,
                null,
                source.PaymentMethodCode,
                source.ExternalPaymentReference,
                source.PaidAt),
            now,
            cancellationToken);
        await using var command = new SqlCommand("""
            SET XACT_ABORT ON;
            INSERT billing.TenantSubscriptionInvoiceLinks
              (SalesDocumentId,TenantSubscriptionId,TenantSubscriptionRenewalOrderId,
               PaymentTransactionId,CreatedAt)
            VALUES(@DocumentId,@SubscriptionId,@OrderId,@PaymentId,@Now);

            UPDATE billing.TenantSubscriptionRenewalOrders
            SET Status=N'Activated',UpdatedAt=@Now
            WHERE TenantSubscriptionRenewalOrderId=@OrderId AND IsCurrent=1
              AND PaymentTransactionId=@PaymentId
              AND Status IN(N'PendingPayment',N'PaymentConfirmed',N'Invoicing');
            IF @@ROWCOUNT<>1 THROW 51090,N'La orden pagada cambió durante la liquidación.',1;

            UPDATE billing.TenantSubscriptions
            SET TenantCommercialPlanId=@PlanId,BillingPeriod=@BillingPeriod,
                Status=N'Active',CurrentPeriodStart=@PeriodStart,CurrentPeriodEnd=@PeriodEnd,
                BillingAnchorDay=DAY(@PeriodStart),FullUserLimit=@FullUsers,
                SellerUserLimit=@SellerUsers,PosDeviceLimit=@PosDevices,
                DianDocumentMonthlyLimit=@DianDocuments,
                PayrollEmployeeLimit=@PayrollEmployees,UpdatedAt=@Now
            WHERE TenantSubscriptionId=@SubscriptionId;
            IF @@ROWCOUNT<>1 THROW 51091,N'La suscripción pagada ya no existe.',1;

            UPDATE scheduled
            SET Status=0,ScheduledAtUtc=CONVERT(datetime2,SWITCHOFFSET(
                  DATEADD(day,-settings.PreDueReminderDays,@PeriodEnd),'+00:00')),
                Attempts=0,LockedUntilUtc=NULL,SentAtUtc=NULL,LastError=NULL,UpdatedAt=@Now
            FROM dbo.ScheduledAutomationJobs scheduled
            CROSS JOIN billing.PlatformBillingSettings settings
            WHERE scheduled.TenantSubscriptionId=@SubscriptionId AND scheduled.JobType=2
              AND settings.PlatformBillingSettingId=1;
            IF @@ROWCOUNT=0
              INSERT dbo.ScheduledAutomationJobs
                (ScheduledAutomationJobId,BusinessId,ReservationId,AgentId,TenantSubscriptionId,
                 JobType,ScheduledAtUtc,Status,DeduplicationKey,Attempts,PayloadJson,CreatedAt)
              SELECT NEWID(),NULL,NULL,NULL,@SubscriptionId,2,
                     CONVERT(datetime2,SWITCHOFFSET(
                       DATEADD(day,-settings.PreDueReminderDays,@PeriodEnd),'+00:00')),
                     0,CONCAT(N'tenant-subscription-lifecycle:',
                       LOWER(CONVERT(nvarchar(36),@SubscriptionId))),0,N'{}',@Now
              FROM billing.PlatformBillingSettings settings
              WHERE settings.PlatformBillingSettingId=1;

            IF NOT EXISTS(SELECT 1 FROM billing.TenantSubscriptionUsagePeriods WITH(UPDLOCK,HOLDLOCK)
                          WHERE TenantSubscriptionId=@SubscriptionId AND PeriodStart=@PeriodStart)
              INSERT billing.TenantSubscriptionUsagePeriods
                (TenantSubscriptionUsagePeriodId,TenantSubscriptionId,PeriodStart,PeriodEnd,
                 DianDocumentsUsed,CreatedAt,UpdatedAt)
              VALUES(@UsagePeriodId,@SubscriptionId,@PeriodStart,
                 CASE WHEN DATEADD(month,1,@PeriodStart)<@PeriodEnd
                      THEN DATEADD(month,1,@PeriodStart) ELSE @PeriodEnd END,
                 0,@Now,@Now);
            """, connection, transaction);
        Add(command, "@DocumentId", snapshot.DocumentId);
        Add(command, "@Now", now);
        Add(command, "@SubscriptionId", source.SubscriptionId);
        Add(command, "@OrderId", snapshot.RenewalOrderId);
        Add(command, "@PaymentId", payment.PaymentTransactionId);
        Add(command, "@PlanId", source.PlanId);
        Add(command, "@BillingPeriod", source.BillingPeriod);
        Add(command, "@PeriodStart", source.PeriodStart);
        Add(command, "@PeriodEnd", source.PeriodEnd);
        Add(command, "@FullUsers", source.FullUserLimit);
        Add(command, "@SellerUsers", source.SellerUserLimit);
        Add(command, "@PosDevices", source.PosDeviceLimit);
        Add(command, "@DianDocuments", source.DianDocumentMonthlyLimit);
        Add(command, "@PayrollEmployees", source.PayrollEmployeeLimit);
        Add(command, "@UsagePeriodId", ids.NewId());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static long ToCents(decimal amount) => checked((long)decimal.Round(
        amount * 100m, 0, MidpointRounding.AwayFromZero));
    private static decimal Money(decimal value) => decimal.Round(
        value, 2, MidpointRounding.AwayFromZero);
    private static void Add(SqlCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private sealed record SettlementSource(
        long AmountInCents, string PaymentReference, Guid BillingTenantId,
        Guid BillingBusinessId, Guid SubscriptionId, Guid BillingCustomerId,
        Guid PlanId, string BillingPeriod, DateTimeOffset PeriodStart,
        DateTimeOffset PeriodEnd, decimal UntaxedAmount, decimal TaxAmount,
        decimal PayableAmount, decimal DiscountRate, int Periods,
        int FullUserLimit, int SellerUserLimit, int PosDeviceLimit,
        int DianDocumentMonthlyLimit, int PayrollEmployeeLimit, string LinesJson,
        string PaymentMethodCode, DateTimeOffset PaidAt, string ExternalPaymentReference);
}
