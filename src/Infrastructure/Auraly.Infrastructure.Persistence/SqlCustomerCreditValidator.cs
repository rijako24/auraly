using System.Data;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlCustomerCreditValidator(
    SqlServerConnectionFactory connections,
    TimeProvider time)
{
    public async Task<PosCreditValidationResult> ValidateAsync(
        Guid tenantId,
        Guid deviceId,
        PosCreditValidationRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var result = await ValidateWithinTransactionAsync(
            connection,
            transaction,
            tenantId,
            deviceId,
            request.BusinessId,
            request.CustomerId,
            request.Amount,
            time.GetUtcNow(),
            cancellationToken,
            request.PartySiteId);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    internal static async Task<PosCreditValidationResult> ValidateWithinTransactionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid? tenantId,
        Guid? deviceId,
        Guid businessId,
        Guid customerId,
        decimal amount,
        DateTimeOffset issuedAt,
        CancellationToken cancellationToken,
        Guid? partySiteId = null)
    {
        await using var command = new SqlCommand("""
            SELECT cp.IsCreditEnabled,cp.CreditLimit,COALESCE(cp.DefaultDueDays,0),
                   COALESCE((
                       SELECT SUM(r.OutstandingAmount)
                       FROM dbo.Receivables r WITH(UPDLOCK,HOLDLOCK)
                       WHERE r.CustomerId=c.CustomerId AND r.BusinessId=c.BusinessId
                         AND r.Status IN(N'Open',N'PartiallyPaid')),0) +
                   COALESCE((
                       SELECT SUM(d.CreditAmount)
                       FROM dbo.SalesDocuments d WITH(UPDLOCK,HOLDLOCK)
                       WHERE d.CustomerId=c.CustomerId AND d.BusinessId=c.BusinessId
                         AND d.CreditAmount>0
                         AND d.ProcessingStatus IN(N'Received',N'Processing')),0),
                   CAST(CASE WHEN @PartySiteId IS NOT NULL AND EXISTS(
                       SELECT 1 FROM dbo.PartySites site
                       WHERE site.PartySiteId=@PartySiteId AND site.PartyId=c.PartyId AND site.IsActive=1)
                       THEN 1 ELSE 0 END AS bit)
            FROM dbo.Customers c WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.CustomerCreditProfiles cp WITH(UPDLOCK,HOLDLOCK)
              ON cp.CustomerId=c.CustomerId AND cp.BusinessId=c.BusinessId
            WHERE c.CustomerId=@CustomerId AND c.BusinessId=@BusinessId AND c.IsActive=1
              AND (@TenantId IS NULL OR EXISTS(
                    SELECT 1 FROM dbo.Businesses b
                    WHERE b.BusinessId=c.BusinessId AND b.TenantId=@TenantId
                      AND b.IsActive=1))
              AND (@DeviceId IS NULL OR EXISTS(
                    SELECT 1 FROM dbo.DocumentSeries ds
                    WHERE ds.DeviceId=@DeviceId AND ds.BusinessId=c.BusinessId
                      AND ds.IsActive=1))
            """, connection, transaction);
        command.Parameters.AddWithValue("@CustomerId", customerId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.Add("@PartySiteId", SqlDbType.UniqueIdentifier).Value =
            (object?)partySiteId ?? DBNull.Value;
        command.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value =
            (object?)tenantId ?? DBNull.Value;
        command.Parameters.Add("@DeviceId", SqlDbType.UniqueIdentifier).Value =
            (object?)deviceId ?? DBNull.Value;
        bool enabled;
        decimal? limit;
        int defaultDueDays;
        decimal outstanding;
        bool validSite;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                return new PosCreditValidationResult(
                    customerId,
                    amount,
                    null,
                    IsAllowed: false,
                    "El cliente no tiene habilitada la venta a crédito.");
            enabled = !reader.IsDBNull(0) && reader.GetBoolean(0);
            limit = reader.IsDBNull(1) ? null : reader.GetDecimal(1);
            defaultDueDays = reader.GetInt32(2);
            outstanding = reader.GetDecimal(3);
            validSite = reader.GetBoolean(4);
        }
        if (!enabled)
            return new PosCreditValidationResult(
                customerId,
                amount,
                null,
                IsAllowed: false,
                "El cliente no tiene habilitada la venta a crédito.");
        if (partySiteId is null)
            return new PosCreditValidationResult(
                customerId, amount, null, IsAllowed: false,
                "Selecciona la sede del cliente para la venta a crédito.");
        if (!validSite)
            return new PosCreditValidationResult(
                customerId, amount, null, IsAllowed: false,
                "La sede seleccionada no pertenece al cliente o está inactiva.");
        decimal? available = limit is null ? null : decimal.Max(0, limit.Value - outstanding);
        return available is not null && amount > available.Value
            ? new PosCreditValidationResult(
                customerId,
                amount,
                available,
                IsAllowed: false,
                "La venta supera el cupo disponible del cliente.")
            : new PosCreditValidationResult(
                customerId,
                amount,
                available,
                IsAllowed: true,
                null,
                DueDate: issuedAt.AddDays(defaultDueDays));
    }
}
