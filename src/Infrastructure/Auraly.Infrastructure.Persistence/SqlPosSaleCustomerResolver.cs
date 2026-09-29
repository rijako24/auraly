using Auraly.Application.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPosSaleCustomerResolver(
    SqlServerConnectionFactory connections) : IPosSaleCustomerResolver
{
    public async Task<Guid?> ResolveForBusinessAsync(
        Guid tenantId,
        Guid businessId,
        Guid sourceCustomerId,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT customer.CustomerId
            FROM dbo.Customers customer
            JOIN dbo.Businesses business ON business.TenantId=customer.TenantId
            WHERE customer.CustomerId=@CustomerId
              AND customer.TenantId=@TenantId
              AND business.BusinessId=@BusinessId AND business.IsActive=1;
            """;
        command.Parameters.AddRange([
            new SqlParameter("@CustomerId", sourceCustomerId),
            new SqlParameter("@TenantId", tenantId),
            new SqlParameter("@BusinessId", businessId)
        ]);
        return await command.ExecuteScalarAsync(ct) is Guid customerId
            ? customerId
            : null;
    }
}
