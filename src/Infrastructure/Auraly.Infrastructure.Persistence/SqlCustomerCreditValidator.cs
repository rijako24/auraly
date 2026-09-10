using System.Data;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlCustomerCreditValidator(SqlServerConnectionFactory connections)
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
            cancellationToken);
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
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT cp.IsCreditEnabled,cp.CreditLimit,
                   COALESCE(SUM(CASE WHEN r.Status IN(N'Open',N'PartiallyPaid')
                                     THEN r.OutstandingAmount ELSE 0 END),0)
            FROM dbo.Customers c WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.CustomerCreditProfiles cp WITH(UPDLOCK,HOLDLOCK)
              ON cp.CustomerId=c.CustomerId AND cp.BusinessId=c.BusinessId
            LEFT JOIN dbo.Receivables r WITH(UPDLOCK,HOLDLOCK)
              ON r.CustomerId=c.CustomerId AND r.BusinessId=c.BusinessId
            WHERE c.CustomerId=@CustomerId AND c.BusinessId=@BusinessId AND c.IsActive=1
              AND (@TenantId IS NULL OR EXISTS(
                    SELECT 1 FROM dbo.Businesses b
                    WHERE b.BusinessId=c.BusinessId AND b.TenantId=@TenantId
                      AND b.IsActive=1))
              AND (@DeviceId IS NULL OR EXISTS(
                    SELECT 1 FROM dbo.DocumentSeries ds
                    WHERE ds.DeviceId=@DeviceId AND ds.BusinessId=c.BusinessId
                      AND ds.IsActive=1))
            GROUP BY cp.IsCreditEnabled,cp.CreditLimit;
            """, connection, transaction);
        command.Parameters.AddWithValue("@CustomerId", customerId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value =
            (object?)tenantId ?? DBNull.Value;
        command.Parameters.Add("@DeviceId", SqlDbType.UniqueIdentifier).Value =
            (object?)deviceId ?? DBNull.Value;
        bool enabled;
        decimal? limit;
        decimal outstanding;
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
            outstanding = reader.GetDecimal(2);
        }
        if (!enabled)
            return new PosCreditValidationResult(
                customerId,
                amount,
                null,
                IsAllowed: false,
                "El cliente no tiene habilitada la venta a crédito.");
        await using var pending = new SqlCommand("""
            SELECT COALESCE(SUM(CreditAmount),0)
            FROM dbo.SalesDocuments WITH(UPDLOCK,HOLDLOCK)
            WHERE CustomerId=@CustomerId AND BusinessId=@BusinessId
              AND CreditAmount>0
              AND ProcessingStatus IN(N'Received',N'Processing');
            """, connection, transaction);
        pending.Parameters.AddWithValue("@CustomerId", customerId);
        pending.Parameters.AddWithValue("@BusinessId", businessId);
        outstanding += (decimal)(await pending.ExecuteScalarAsync(cancellationToken) ?? 0m);
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
                null);
    }
}
