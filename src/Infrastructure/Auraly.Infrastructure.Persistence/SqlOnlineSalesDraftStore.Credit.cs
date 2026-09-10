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

        var validation = await SqlCustomerCreditValidator.ValidateWithinTransactionAsync(
            connection,
            transaction,
            tenantId: null,
            deviceId: null,
            businessId,
            customerId.Value,
            credit.Amount,
            cancellationToken);
        if (!validation.IsAllowed)
            throw new OnlineSalesDraftValidationException(
                validation.RejectionReason ?? "No fue posible validar el cupo del cliente.");
    }
}
