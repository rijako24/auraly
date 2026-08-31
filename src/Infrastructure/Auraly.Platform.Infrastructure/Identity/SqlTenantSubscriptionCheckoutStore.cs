using System.Data;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Auraly.Platform.Infrastructure.Identity;

public sealed class SqlTenantSubscriptionCheckoutStore(ApplicationDbContext db)
    : ITenantSubscriptionCheckoutStore
{
    public async Task<Guid> GetBillingBusinessIdAsync(CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT BillingBusinessId FROM billing.PlatformBillingSettings WHERE PlatformBillingSettingId=1;",
            connection);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid id
            ? id
            : throw new InvalidOperationException(
                "Configura la sede facturadora de Auraly antes de recibir pagos.");
    }

    public async Task CreatePaymentAsync(
        Guid tenantId,
        Guid paymentTransactionId,
        Guid renewalOrderId,
        string reference,
        long amountInCents,
        DateTimeOffset expiresAt,
        int merchantConfigurationVersion,
        CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using var command = new SqlCommand("""
                DECLARE @BillingBusinessId uniqueidentifier,@ExpectedAmount decimal(19,4),
                        @OrderStatus nvarchar(24),@ExpectedCents bigint;
                SELECT @BillingBusinessId=settings.BillingBusinessId
                FROM billing.PlatformBillingSettings settings WITH(UPDLOCK,HOLDLOCK)
                WHERE settings.PlatformBillingSettingId=1;
                SELECT @ExpectedAmount=renewal.PayableAmount,@OrderStatus=renewal.Status
                FROM billing.TenantSubscriptionRenewalOrders renewal WITH(UPDLOCK,HOLDLOCK)
                JOIN billing.TenantSubscriptions subscription
                  ON subscription.TenantSubscriptionId=renewal.TenantSubscriptionId
                WHERE renewal.TenantSubscriptionRenewalOrderId=@OrderId
                  AND subscription.TenantId=@TenantId AND renewal.IsCurrent=1;
                IF @ExpectedAmount IS NULL THROW 51120,N'La orden de renovación no existe o ya no es vigente.',1;
                IF @OrderStatus<>N'Draft' THROW 51121,N'La orden de renovación no admite un nuevo pago.',1;
                SET @ExpectedCents=CONVERT(bigint,ROUND(@ExpectedAmount*100,0));
                IF @ExpectedCents<>@AmountInCents THROW 51122,N'El valor del pago no coincide con la orden vigente.',1;

                INSERT dbo.PaymentTransactions
                  (PaymentTransactionId,BusinessId,ConversationId,PaymentReferenceId,
                   AmountInCents,Currency,Status,Source,ExpiresAt,CreatedAt,CheckoutKind,
                   MerchantConfigurationVersion,ConfirmationOutcome,SubjectType,SubjectId)
                VALUES(@PaymentId,@BillingBusinessId,NULL,@Reference,@AmountInCents,N'COP',
                   @PaymentStatus,0,@ExpiresAt,@Now,@CheckoutKind,@MerchantConfigurationVersion,
                   N'TenantSubscriptionPending',N'TenantSubscription',@OrderId);
                UPDATE billing.TenantSubscriptionRenewalOrders
                SET PaymentTransactionId=@PaymentId,Status=N'PendingPayment',
                    UpdatedAt=@Now
                WHERE TenantSubscriptionRenewalOrderId=@OrderId;
                """, connection, transaction);
            Add(command, "@TenantId", tenantId);
            Add(command, "@PaymentId", paymentTransactionId);
            Add(command, "@OrderId", renewalOrderId);
            Add(command, "@Reference", reference);
            Add(command, "@AmountInCents", amountInCents);
            Add(command, "@PaymentStatus", (int)PaymentTransactionStatus.Created);
            Add(command, "@CheckoutKind", (int)CheckoutKind.TenantSubscription);
            Add(command, "@MerchantConfigurationVersion", merchantConfigurationVersion);
            Add(command, "@ExpiresAt", expiresAt.UtcDateTime);
            Add(command, "@Now", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is >= 51120 and <= 51122)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ArgumentException(exception.Message, exception);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<TenantSubscriptionPaymentVerification?> GetPaymentForVerificationAsync(
        Guid tenantId, Guid renewalOrderId, CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT renewal.TenantSubscriptionRenewalOrderId,payment.PaymentTransactionId,
                   payment.BusinessId,payment.PaymentReferenceId,payment.AmountInCents,
                   payment.ExpiresAt,payment.Status,payment.MerchantConfigurationVersion
            FROM billing.TenantSubscriptionRenewalOrders renewal
            JOIN billing.TenantSubscriptions subscription
              ON subscription.TenantSubscriptionId=renewal.TenantSubscriptionId
            JOIN dbo.PaymentTransactions payment
              ON payment.PaymentTransactionId=renewal.PaymentTransactionId
            WHERE renewal.TenantSubscriptionRenewalOrderId=@OrderId
              AND subscription.TenantId=@TenantId;
            """, connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@OrderId", renewalOrderId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetString(3), reader.GetInt64(4),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)),
                reader.GetInt32(6), reader.GetInt32(7))
            : null;
    }

    public async Task<TenantSubscriptionManualPaymentPreparation> CreateManualPaymentAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid paymentTransactionId,
        Guid renewalOrderId,
        RecordTenantSubscriptionPaymentRequest request,
        string checkoutSnapshotJson,
        CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var paymentReference = $"TSM-{renewalOrderId:N}-{paymentTransactionId:N}";
        try
        {
            await using var command = new SqlCommand("""
                DECLARE @BillingBusinessId uniqueidentifier,@ExpectedAmount decimal(19,4),
                        @OrderStatus nvarchar(24),@ExistingPaymentId uniqueidentifier,
                        @ExistingPaymentStatus int,@AmountInCents bigint;
                SELECT @BillingBusinessId=settings.BillingBusinessId
                FROM billing.PlatformBillingSettings settings WITH(UPDLOCK,HOLDLOCK)
                WHERE settings.PlatformBillingSettingId=1;
                SELECT @ExpectedAmount=renewal.PayableAmount,@OrderStatus=renewal.Status,
                       @ExistingPaymentId=renewal.PaymentTransactionId
                FROM billing.TenantSubscriptionRenewalOrders renewal WITH(UPDLOCK,HOLDLOCK)
                JOIN billing.TenantSubscriptions subscription WITH(UPDLOCK,HOLDLOCK)
                  ON subscription.TenantSubscriptionId=renewal.TenantSubscriptionId
                WHERE renewal.TenantSubscriptionRenewalOrderId=@OrderId
                  AND subscription.TenantId=@TenantId AND renewal.IsCurrent=1;
                IF @ExpectedAmount IS NULL THROW 51130,N'La orden de renovación no existe o ya no es vigente.',1;
                IF @OrderStatus NOT IN(N'Draft',N'PendingPayment')
                  THROW 51131,N'La orden de renovación no admite registrar otro recaudo.',1;
                IF @BillingBusinessId IS NULL
                  THROW 51132,N'Configura la sede facturadora de Auraly antes de registrar recaudos.',1;
                IF EXISTS(SELECT 1 FROM dbo.PaymentTransactions WITH(UPDLOCK,HOLDLOCK)
                          WHERE Source=1 AND ProviderTransactionId=@ExternalReference)
                  THROW 51133,N'La referencia externa ya fue registrada.',1;

                IF @ExistingPaymentId IS NOT NULL
                BEGIN
                    SELECT @ExistingPaymentStatus=Status
                    FROM dbo.PaymentTransactions WITH(UPDLOCK,HOLDLOCK)
                    WHERE PaymentTransactionId=@ExistingPaymentId;
                    IF @ExistingPaymentStatus=1
                      THROW 51134,N'La orden ya tiene un pago confirmado.',1;
                END;

                SET @AmountInCents=CONVERT(bigint,ROUND(@ExpectedAmount*100,0));
                INSERT dbo.PaymentTransactions
                  (PaymentTransactionId,BusinessId,ConversationId,PaymentReferenceId,
                   ProviderTransactionId,AmountInCents,Currency,Status,Source,CreatedAt,
                   CheckoutKind,CheckoutSnapshotJson,ConfirmationOutcome,SubjectType,SubjectId)
                VALUES(@PaymentId,@BillingBusinessId,NULL,@PaymentReference,
                   @ExternalReference,@AmountInCents,N'COP',0,1,@Now,
                   4,@Snapshot,N'TenantSubscriptionManualPending',N'TenantSubscription',@OrderId);

                IF @ExistingPaymentId IS NOT NULL
                  UPDATE dbo.PaymentTransactions
                  SET Status=50,SupersededAt=@Now,SupersededByPaymentTransactionId=@PaymentId,
                      ConfirmationOutcome=N'SupersededByManualReceipt'
                  WHERE PaymentTransactionId=@ExistingPaymentId AND Status<>1;

                UPDATE billing.TenantSubscriptionRenewalOrders
                SET PaymentTransactionId=@PaymentId,Status=N'PendingPayment',UpdatedAt=@Now
                WHERE TenantSubscriptionRenewalOrderId=@OrderId AND IsCurrent=1;
                IF @@ROWCOUNT<>1 THROW 51135,N'La orden cambió mientras se registraba el recaudo.',1;

                INSERT dbo.AuditLogs
                  (AuditLogId,UserId,TenantId,Action,EntityType,EntityId,OldValues,NewValues,Timestamp)
                VALUES(NEWID(),@ActorUserId,@ActorTenantId,N'TenantSubscription.PaymentRecorded',
                       N'TenantSubscriptionRenewalOrder',CONVERT(nvarchar(36),@OrderId),NULL,
                       @Snapshot,@Now);

                SELECT @PaymentReference,@AmountInCents,@ExternalReference;
                """, connection, transaction);
            Add(command, "@TenantId", tenantId);
            Add(command, "@ActorUserId", actorUserId);
            Add(command, "@ActorTenantId", await ActorTenantIdAsync(connection, transaction, actorUserId, cancellationToken));
            Add(command, "@PaymentId", paymentTransactionId);
            Add(command, "@OrderId", renewalOrderId);
            Add(command, "@PaymentReference", paymentReference);
            Add(command, "@ExternalReference", request.Reference);
            Add(command, "@Snapshot", checkoutSnapshotJson);
            Add(command, "@Now", DateTimeOffset.UtcNow);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("No fue posible preparar el recaudo externo.");
            var result = new TenantSubscriptionManualPaymentPreparation(
                reader.GetString(0), reader.GetInt64(1), reader.GetString(2));
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqlException exception) when (exception.Number is >= 51130 and <= 51135)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new ArgumentException(exception.Message, exception);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<TenantSubscriptionReceiptDto?> GetReceiptAsync(
        Guid tenantId, Guid renewalOrderId, CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT document.DocumentId,document.DocumentNumber,document.IssuedAt,
                   renewal.TargetPeriodStart,renewal.TargetPeriodEnd,renewal.BillingPeriod,
                   renewal.CurrencyCode,document.UntaxedAmount,document.TaxAmount,
                   document.PayableAmount,
                   CASE WHEN payment.Source=1
                        THEN COALESCE(JSON_VALUE(payment.CheckoutSnapshotJson,N'$.paymentMethodCode'),N'Manual')
                        ELSE N'Wompi' END,
                   payment.ProviderTransactionId,
                   COALESCE(document.CufeCalculated,document.CufeReceived),document.FiscalStatus
            FROM billing.TenantSubscriptionInvoiceLinks link
            JOIN billing.TenantSubscriptionRenewalOrders renewal
              ON renewal.TenantSubscriptionRenewalOrderId=link.TenantSubscriptionRenewalOrderId
            JOIN billing.TenantSubscriptions subscription
              ON subscription.TenantSubscriptionId=link.TenantSubscriptionId
            JOIN dbo.SalesDocuments document ON document.DocumentId=link.SalesDocumentId
            JOIN dbo.PaymentTransactions payment
              ON payment.PaymentTransactionId=link.PaymentTransactionId
            WHERE renewal.TenantSubscriptionRenewalOrderId=@OrderId
              AND subscription.TenantId=@TenantId;

            SELECT line.ServiceCode,line.Description,line.Quantity,line.UnitPrice,
                   line.UntaxedAmount,line.TaxRate,line.TaxAmount,line.LineTotal
            FROM billing.TenantSubscriptionInvoiceLinks link
            JOIN billing.TenantSubscriptions subscription
              ON subscription.TenantSubscriptionId=link.TenantSubscriptionId
            JOIN sales.SalesDocumentServiceLines line
              ON line.DocumentId=link.SalesDocumentId
            WHERE link.TenantSubscriptionRenewalOrderId=@OrderId
              AND subscription.TenantId=@TenantId
            ORDER BY line.LineNumber;
            """, connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@OrderId", renewalOrderId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var header = new
        {
            DocumentId = reader.GetGuid(0), DocumentNumber = reader.GetString(1),
            IssuedAt = reader.GetFieldValue<DateTimeOffset>(2),
            Start = reader.GetFieldValue<DateTimeOffset>(3),
            End = reader.GetFieldValue<DateTimeOffset>(4), Billing = reader.GetString(5),
            Currency = reader.GetString(6), Subtotal = reader.GetDecimal(7),
            Tax = reader.GetDecimal(8), Total = reader.GetDecimal(9),
            Method = reader.GetString(10), Reference = reader.IsDBNull(11) ? "" : reader.GetString(11),
            Cufe = reader.IsDBNull(12) ? null : reader.GetString(12),
            FiscalStatus = reader.IsDBNull(13) ? "Pending" : reader.GetString(13)
        };
        var lines = new List<TenantSubscriptionReceiptLineDto>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            lines.Add(new(reader.GetString(0), reader.GetString(1), reader.GetDecimal(2),
                reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5),
                reader.GetDecimal(6), reader.GetDecimal(7)));
        return new(header.DocumentId, header.DocumentNumber, header.IssuedAt,
            header.Start, header.End, header.Billing, header.Currency,
            header.Subtotal, header.Tax, header.Total, header.Method,
            header.Reference, header.Cufe, header.FiscalStatus, lines);
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<Guid> ActorTenantIdAsync(
        SqlConnection connection, SqlTransaction transaction, Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT TenantId FROM dbo.AppUsers WHERE UserId=@UserId AND IsActive=1;",
            connection, transaction);
        Add(command, "@UserId", actorUserId);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid tenantId
            ? tenantId
            : throw new UnauthorizedAccessException("El usuario que registra el recaudo no está activo.");
    }

    private static void Add(SqlCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);
}
