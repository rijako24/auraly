using System.Data;
using System.Text.Json;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Auraly.Platform.Infrastructure.Identity;

public sealed class SqlTenantProvisioningCheckoutStore(ApplicationDbContext db)
    : ITenantProvisioningCheckoutStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Guid> GetBillingBusinessIdAsync(CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = new SqlCommand("""
            SELECT BillingBusinessId FROM billing.PlatformBillingSettings WHERE PlatformBillingSettingId=1;
            """, connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid businessId
            ? businessId
            : throw new InvalidOperationException("Configura la sede facturadora de Auraly antes de recibir pagos.");
    }

    public async Task CreateAsync(
        Guid draftId,
        Guid paymentTransactionId,
        byte[] accessTokenHash,
        string ownerEmail,
        TenantProvisioningCheckoutSnapshot snapshot,
        byte[] quoteHash,
        DateTimeOffset expiresAt,
        int merchantConfigurationVersion,
        CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var billingBusinessId = await ReadBillingBusinessIdAsync(connection, transaction, cancellationToken);
            var planId = await ReadPlanIdAsync(connection, transaction, snapshot.Quote.PlanCode, cancellationToken);
            var payload = JsonSerializer.Serialize(snapshot, Json);
            var lines = JsonSerializer.Serialize(snapshot.Quote.Lines, Json);
            var reference = $"TP-{draftId:N}";
            var amountInCents = checked((long)decimal.Round(snapshot.Quote.PayableAmountCop * 100m, 0,
                MidpointRounding.AwayFromZero));
            var now = DateTimeOffset.UtcNow;

            await using var command = new SqlCommand("""
                INSERT dbo.PaymentTransactions
                  (PaymentTransactionId,BusinessId,ConversationId,PaymentReferenceId,AmountInCents,
                   Currency,Status,Source,ExpiresAt,CreatedAt,CheckoutKind,CheckoutSnapshotJson,
                   MerchantConfigurationVersion,QuoteHash,ConfirmationOutcome,SubjectType,SubjectId)
                VALUES
                  (@PaymentId,@BusinessId,NULL,@Reference,@Amount,N'COP',@PaymentStatus,0,@ExpiresAt,@Now,
                   @CheckoutKind,@Snapshot,@MerchantConfigurationVersion,@QuoteHashText,N'TenantProvisioningPending',N'TenantProvisioning',@DraftId);

                INSERT billing.TenantProvisioningDrafts
                  (TenantProvisioningDraftId,OwnerUserId,OwnerEmail,AccessTokenHash,PayloadJson,Status,
                   ExpiresAt,CreatedAt,UpdatedAt,PaymentTransactionId)
                VALUES(@DraftId,NULL,@OwnerEmail,@AccessTokenHash,@Snapshot,N'PaymentPending',
                   @ExpiresAt,@Now,@Now,@PaymentId);

                INSERT billing.TenantProvisioningQuotes
                  (TenantProvisioningQuoteId,TenantProvisioningDraftId,TenantCommercialPlanId,
                   BillingPeriod,CurrencyCode,MonthlySubtotal,Periods,DiscountRate,DiscountAmount,
                   TaxAmount,PayableAmount,LinesJson,QuoteHash,ExpiresAt,CreatedAt)
                VALUES(NEWID(),@DraftId,@PlanId,@BillingPeriod,'COP',@MonthlySubtotal,@Periods,
                   @DiscountRate,@DiscountAmount,@TaxAmount,@PayableAmount,@LinesJson,@QuoteHash,@ExpiresAt,@Now);
                """, connection, transaction);
            Add(command, "@PaymentId", paymentTransactionId);
            Add(command, "@BusinessId", billingBusinessId);
            Add(command, "@Reference", reference);
            Add(command, "@Amount", amountInCents);
            Add(command, "@PaymentStatus", (int)PaymentTransactionStatus.Created);
            Add(command, "@CheckoutKind", (int)CheckoutKind.TenantProvisioning);
            Add(command, "@Snapshot", payload);
            Add(command, "@MerchantConfigurationVersion", merchantConfigurationVersion);
            Add(command, "@QuoteHashText", Convert.ToHexString(quoteHash).ToLowerInvariant());
            Add(command, "@DraftId", draftId);
            Add(command, "@OwnerEmail", ownerEmail);
            command.Parameters.Add("@AccessTokenHash", SqlDbType.Binary, 32).Value = accessTokenHash;
            Add(command, "@ExpiresAt", expiresAt);
            Add(command, "@Now", now);
            Add(command, "@PlanId", planId);
            Add(command, "@BillingPeriod", snapshot.Quote.BillingPeriod);
            Add(command, "@MonthlySubtotal", snapshot.Quote.MonthlySubtotalCop);
            Add(command, "@Periods", snapshot.Quote.Periods);
            Add(command, "@DiscountRate", snapshot.Quote.DiscountRate);
            Add(command, "@DiscountAmount", snapshot.Quote.DiscountAmountCop);
            Add(command, "@TaxAmount", snapshot.Quote.TaxAmountCop);
            Add(command, "@PayableAmount", snapshot.Quote.PayableAmountCop);
            Add(command, "@LinesJson", lines);
            command.Parameters.Add("@QuoteHash", SqlDbType.Binary, 32).Value = quoteHash;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<TenantProvisioningCheckoutStatusDto?> GetStatusAsync(
        Guid draftId,
        byte[] accessTokenHash,
        CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = new SqlCommand("""
            SELECT d.Status,p.Status,d.ProvisionedTenantId,t.TenantKey,d.ErrorMessage
            FROM billing.TenantProvisioningDrafts d
            JOIN dbo.PaymentTransactions p ON p.PaymentTransactionId=d.PaymentTransactionId
            LEFT JOIN dbo.Tenants t ON t.TenantId=d.ProvisionedTenantId
            WHERE d.TenantProvisioningDraftId=@DraftId AND d.AccessTokenHash=@AccessTokenHash;
            """, connection);
        Add(command, "@DraftId", draftId);
        command.Parameters.Add("@AccessTokenHash", SqlDbType.Binary, 32).Value = accessTokenHash;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(draftId, reader.GetString(0),
            ((PaymentTransactionStatus)reader.GetInt32(1)).ToString(),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    public async Task<TenantProvisioningFulfillment?> GetForFulfillmentAsync(
        Guid draftId,
        CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = new SqlCommand("""
            SELECT d.PaymentTransactionId,d.PayloadJson,d.Status
            FROM billing.TenantProvisioningDrafts d
            WHERE d.TenantProvisioningDraftId=@DraftId;
            """, connection);
        Add(command, "@DraftId", draftId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var snapshot = JsonSerializer.Deserialize<TenantProvisioningCheckoutSnapshot>(reader.GetString(1), Json)
            ?? throw new InvalidOperationException("El snapshot de aprovisionamiento no es válido.");
        return new(draftId, reader.GetGuid(0), snapshot, reader.GetString(2));
    }

    public async Task<TenantProvisioningPaymentVerification?> GetPaymentForVerificationAsync(
        Guid draftId, byte[] accessTokenHash, CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = new SqlCommand("""
            SELECT p.PaymentTransactionId,p.BusinessId,p.PaymentReferenceId,p.AmountInCents,
                   p.MerchantConfigurationVersion
            FROM billing.TenantProvisioningDrafts d
            JOIN dbo.PaymentTransactions p ON p.PaymentTransactionId=d.PaymentTransactionId
            WHERE d.TenantProvisioningDraftId=@DraftId AND d.AccessTokenHash=@AccessTokenHash;
            """, connection);
        Add(command, "@DraftId", draftId);
        command.Parameters.Add("@AccessTokenHash", SqlDbType.Binary, 32).Value = accessTokenHash;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(draftId, reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetInt64(3), reader.GetInt32(4))
            : null;
    }

    public Task MarkProvisionedAsync(Guid draftId, Guid tenantId,
        CancellationToken cancellationToken) =>
        UpdateAsync(draftId, "Provisioned", tenantId, null, cancellationToken);

    public Task MarkFailedAsync(Guid draftId, string error, CancellationToken cancellationToken) =>
        UpdateAsync(draftId, "Failed", null, error, cancellationToken);

    private async Task UpdateAsync(Guid draftId, string status, Guid? tenantId, string? error,
        CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = new SqlCommand("""
            UPDATE billing.TenantProvisioningDrafts
            SET Status=@Status,ProvisionedTenantId=COALESCE(@TenantId,ProvisionedTenantId),
                ErrorMessage=@Error,UpdatedAt=SYSDATETIMEOFFSET()
            WHERE TenantProvisioningDraftId=@DraftId AND Status<>N'Provisioned';
            """, connection);
        Add(command, "@Status", status);
        Add(command, "@TenantId", tenantId);
        Add(command, "@Error", error);
        Add(command, "@DraftId", draftId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Guid> ReadBillingBusinessIdAsync(SqlConnection connection,
        SqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT BillingBusinessId FROM billing.PlatformBillingSettings WITH (UPDLOCK,HOLDLOCK)
            WHERE PlatformBillingSettingId=1;
            """, connection, transaction);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? id : throw new InvalidOperationException(
            "Configura la sede facturadora de Auraly antes de recibir pagos.");
    }

    private static async Task<Guid> ReadPlanIdAsync(SqlConnection connection,
        SqlTransaction transaction, string code, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT p.TenantCommercialPlanId
            FROM billing.TenantCommercialPlans p
            INNER JOIN billing.BillableServices service
              ON service.BillableServiceId=p.BillableServiceId
            WHERE service.Code=@Code AND p.IsActive=1 AND service.IsActive=1;
            """, connection, transaction);
        Add(command, "@Code", code);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? id : throw new InvalidOperationException("El plan comercial ya no está disponible.");
    }

    private static async Task EnsureOpenAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
    }

    private static void Add(SqlCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
