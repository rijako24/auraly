using System.Data;
using System.Text.Json;
using Auraly.Contracts.Sales;
using Auraly.Application.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore
{
    public async Task<IReadOnlyList<OnlineOrderCreditValidationIssue>> ValidateOrderCreditBatchAsync(
        OnlineSalesUserIdentity user,
        Guid businessId,
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH Requested AS (
              SELECT DISTINCT TRY_CONVERT(uniqueidentifier,[value]) OrderId
              FROM OPENJSON(@OrderIds)
              WHERE TRY_CONVERT(uniqueidentifier,[value]) IS NOT NULL
            ), Eligible AS (
              SELECT o.CustomerId,
                     COALESCE(NULLIF(MAX(o.CustomerNameSnapshot),N''),N'Cliente sin nombre') CustomerName,
                     NULLIF(MAX(o.CustomerDocumentSnapshot),N'') CustomerIdentification,
                     SUM(o.Total) RequestedAmount
              FROM Requested requested
              JOIN dbo.Orders o ON o.OrderId=requested.OrderId AND o.BusinessId=@BusinessId
              JOIN dbo.Businesses business ON business.BusinessId=o.BusinessId
                                        AND business.TenantId=@TenantId AND business.IsActive=1
              LEFT JOIN dbo.PaymentTransactions payment ON payment.PaymentTransactionId=o.PaymentTransactionId
              LEFT JOIN dbo.OrderInvoiceLinks invoiceLink ON invoiceLink.OrderId=o.OrderId
              WHERE o.CustomerConfirmed=1 AND o.Status IN(2,4)
                AND invoiceLink.OrderId IS NULL
                AND ISNULL(payment.Status,-1)<>2
              GROUP BY o.CustomerId
            ), Outstanding AS (
              SELECT eligible.CustomerId,
                     COALESCE(SUM(receivable.OutstandingAmount),0) OutstandingAmount
              FROM Eligible eligible
              LEFT JOIN dbo.Receivables receivable
                ON receivable.CustomerId=eligible.CustomerId
               AND receivable.BusinessId=@BusinessId
               AND receivable.Status IN(N'Open',N'PartiallyPaid')
              GROUP BY eligible.CustomerId
            ), PendingCredit AS (
              SELECT eligible.CustomerId,
                     COALESCE(SUM(document.CreditAmount),0) PendingAmount
              FROM Eligible eligible
              LEFT JOIN dbo.SalesDocuments document
                ON document.CustomerId=eligible.CustomerId
               AND document.BusinessId=@BusinessId
               AND document.CreditAmount>0
               AND document.ProcessingStatus IN(N'Received',N'Processing')
              GROUP BY eligible.CustomerId
            )
            SELECT eligible.CustomerId,eligible.CustomerName,eligible.CustomerIdentification,
                   eligible.RequestedAmount,
                   CASE WHEN eligible.CustomerId IS NULL OR customer.CustomerId IS NULL OR profile.CustomerId IS NULL
                                  OR profile.IsCreditEnabled=0 OR profile.CreditLimit IS NULL THEN NULL
                        WHEN profile.CreditLimit-COALESCE(outstanding.OutstandingAmount,0)-COALESCE(pending.PendingAmount,0)<0
                          THEN CONVERT(decimal(19,4),0)
                        ELSE profile.CreditLimit-COALESCE(outstanding.OutstandingAmount,0)-COALESCE(pending.PendingAmount,0) END AvailableCredit,
                   CASE WHEN eligible.CustomerId IS NULL OR customer.CustomerId IS NULL
                                  OR profile.CustomerId IS NULL OR profile.IsCreditEnabled=0
                          THEN N'El cliente no tiene habilitada la venta a crédito.'
                        ELSE N'El valor seleccionado supera el cupo disponible del cliente.' END Reason
            FROM Eligible eligible
            LEFT JOIN dbo.Customers customer
              ON customer.CustomerId=eligible.CustomerId AND customer.BusinessId=@BusinessId
             AND customer.IsActive=1
            LEFT JOIN dbo.CustomerCreditProfiles profile
              ON profile.CustomerId=eligible.CustomerId AND profile.BusinessId=@BusinessId
            LEFT JOIN Outstanding outstanding ON outstanding.CustomerId=eligible.CustomerId
            LEFT JOIN PendingCredit pending ON pending.CustomerId=eligible.CustomerId
            WHERE eligible.CustomerId IS NULL
               OR customer.CustomerId IS NULL
               OR profile.CustomerId IS NULL
               OR profile.IsCreditEnabled=0
               OR (profile.CreditLimit IS NOT NULL
                   AND eligible.RequestedAmount>
                       profile.CreditLimit-COALESCE(outstanding.OutstandingAmount,0)-COALESCE(pending.PendingAmount,0))
            ORDER BY eligible.CustomerName;
            """;
        command.Parameters.AddRange([
            P("@OrderIds", JsonSerializer.Serialize(orderIds)),
            P("@BusinessId", businessId),
            P("@TenantId", user.TenantId)
        ]);
        var issues = new List<OnlineOrderCreditValidationIssue>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            issues.Add(new(
                reader.IsDBNull(0) ? null : reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                reader.GetString(5)));
        return issues;
    }

    private static async Task<PosCreditValidationResult?> ValidateCreditAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid? customerId,
        Guid? partySiteId,
        OnlineSalesCreditTerms? credit,
        DateTimeOffset issuedAt,
        CancellationToken cancellationToken)
    {
        if (credit is null) return null;
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
            issuedAt,
            cancellationToken,
            partySiteId);
        if (!validation.IsAllowed)
            throw new OnlineSalesDraftValidationException(
                validation.RejectionReason ?? "No fue posible validar el cupo del cliente.");
        _ = validation.DueDate
            ?? throw new OnlineSalesDraftValidationException(
                "No fue posible calcular el vencimiento de la cartera.");
        return validation;
    }
}
