using Auraly.Contracts.Sales;
using Auraly.Application.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore
{
    private static async Task ValidateCreditAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid? customerId,
        OnlineSalesCreditTerms? credit,
        CancellationToken cancellationToken)
    {
        if (credit is null) return;
        if (customerId is null)
            throw new OnlineSalesDraftValidationException(
                "Debe seleccionar un cliente para vender a crédito.");

        await using var command = new SqlCommand("""
            SELECT cp.IsCreditEnabled,cp.CreditLimit,
                   COALESCE(SUM(CASE WHEN r.Status IN (N'Open',N'PartiallyPaid')
                                     THEN r.OutstandingAmount ELSE 0 END),0)
            FROM dbo.Customers c WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.CustomerCreditProfiles cp WITH(UPDLOCK,HOLDLOCK)
              ON cp.CustomerId=c.CustomerId AND cp.BusinessId=c.BusinessId
            LEFT JOIN dbo.Receivables r WITH(UPDLOCK,HOLDLOCK)
              ON r.CustomerId=c.CustomerId AND r.BusinessId=c.BusinessId
            WHERE c.CustomerId=@CustomerId AND c.BusinessId=@BusinessId AND c.IsActive=1
            GROUP BY cp.IsCreditEnabled,cp.CreditLimit;
            """, connection, transaction);
        command.Parameters.AddWithValue("@CustomerId", customerId.Value);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0) || !reader.GetBoolean(0))
            throw new OnlineSalesDraftValidationException(
                "El cliente no tiene habilitada la venta a crédito.");
        var limit = reader.IsDBNull(1) ? (decimal?)null : reader.GetDecimal(1);
        var outstanding = reader.GetDecimal(2);
        if (limit is not null && outstanding + credit.Amount > limit)
            throw new OnlineSalesDraftValidationException(
                "La venta supera el cupo disponible del cliente.");
    }
}
