using Auraly.BuildingBlocks.Domain.Time;
using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Auraly.Domain.Receivables;
using System.Data;
using System.Text.Json;
using Auraly.Contracts.Sales;
using Auraly.Application.Sales;
using Microsoft.Data.SqlClient;
using PaymentTransactionStatus = Auraly.Platform.Domain.Enums.PaymentTransactionStatus;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore
{
    public async Task<IReadOnlyList<OnlineOrderCreditValidationIssue>> ValidateOrderCreditBatchAsync(
        OnlineSalesUserIdentity user,
        Guid businessId,
        IReadOnlyDictionary<Guid, decimal> orderAmounts,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var zone = await SqlBusinessLocalDates.ReadTimeZoneAsync(connection, user.TenantId, businessId, cancellationToken)
            ?? throw new OnlineSalesDraftForbiddenException("La sede no está disponible para validar el crédito.");
        var overdueBefore = SqlBusinessLocalDates.StartOfDay(
            BusinessCalendar.DateAt(time.GetUtcNow(), zone), zone);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH Requested AS (
              SELECT OrderId,AdditionalAmount
              FROM OPENJSON(@Orders) WITH(OrderId uniqueidentifier,AdditionalAmount decimal(19,4))
            ), OrderTotals AS (
              SELECT item.OrderId,item.BusinessId,SUM(item.LineTotal)+MAX(requested.AdditionalAmount) RequestedAmount
              FROM dbo.OrderItems item
              JOIN Requested requested ON requested.OrderId=item.OrderId
              GROUP BY item.OrderId,item.BusinessId
            ), Eligible AS (
              SELECT o.CustomerId,
                     COALESCE(NULLIF(MAX(o.CustomerNameSnapshot),N''),N'Cliente sin nombre') CustomerName,
                     NULLIF(MAX(o.CustomerDocumentSnapshot),N'') CustomerIdentification,
                     SUM(orderTotal.RequestedAmount) RequestedAmount
              FROM Requested requested
              JOIN dbo.Orders o ON o.OrderId=requested.OrderId AND o.BusinessId=@BusinessId
              JOIN dbo.Businesses business ON business.BusinessId=o.BusinessId
                                        AND business.TenantId=@TenantId AND business.IsActive=1
              JOIN OrderTotals orderTotal
                ON orderTotal.OrderId=o.OrderId AND orderTotal.BusinessId=o.BusinessId
              LEFT JOIN dbo.PaymentTransactions payment ON payment.PaymentTransactionId=o.PaymentTransactionId AND payment.BusinessId=o.BusinessId
              LEFT JOIN dbo.OrderInvoiceLinks invoiceLink ON invoiceLink.OrderId=o.OrderId
              WHERE o.CustomerConfirmed=1 AND o.Status IN(2,4)
                AND invoiceLink.OrderId IS NULL
                AND ISNULL(payment.Status,-1)<>@ConfirmedPaymentStatus
              GROUP BY o.CustomerId
            ), Outstanding AS (
              SELECT eligible.CustomerId,
                     COALESCE(SUM(receivable.OutstandingAmount),0) OutstandingAmount,
                     COUNT(CASE WHEN receivable.OutstandingAmount>0 AND receivable.DueDate<@OverdueBefore THEN 1 END) OverdueInvoices
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
                   CONVERT(bit,CASE WHEN customer.CustomerId IS NOT NULL AND profile.IsCreditEnabled=1 THEN 1 ELSE 0 END),
                   profile.CreditLimit,COALESCE(outstanding.OutstandingAmount,0)+COALESCE(pending.PendingAmount,0),
                   COALESCE(outstanding.OverdueInvoices,0),profile.MaximumOverdueInvoices
            FROM Eligible eligible
            LEFT JOIN dbo.Customers customer
              ON customer.CustomerId=eligible.CustomerId AND customer.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
             AND customer.IsActive=1
            LEFT JOIN dbo.CustomerCreditProfiles profile
              ON profile.CustomerId=eligible.CustomerId AND profile.BusinessId=@BusinessId
            LEFT JOIN Outstanding outstanding ON outstanding.CustomerId=eligible.CustomerId
            LEFT JOIN PendingCredit pending ON pending.CustomerId=eligible.CustomerId
            ORDER BY eligible.CustomerName;
            """;
        command.Parameters.AddRange([
            P("@Orders", JsonSerializer.Serialize(orderAmounts.Select(item => new
                { OrderId = item.Key, AdditionalAmount = item.Value }))),
            P("@BusinessId", businessId),
            P("@TenantId", user.TenantId),
            P("@ConfirmedPaymentStatus",(int)PaymentTransactionStatus.Confirmed),
            P("@OverdueBefore", overdueBefore)
        ]);
        var issues = new List<OnlineOrderCreditValidationIssue>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var overdue = reader.GetInt32(7);
            var maximum = reader.IsDBNull(8) ? (int?)null : reader.GetInt32(8);
            var decision = CustomerCreditPolicy.Evaluate(reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5), reader.GetDecimal(6),
                overdue, maximum, reader.GetDecimal(3));
            if (!decision.IsAllowed)
                issues.Add(new(reader.IsDBNull(0) ? null : reader.GetGuid(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetDecimal(3),
                    decision.AvailableCredit, decision.RejectionReason!, overdue, maximum));
        }
        return issues;
    }

    private async Task<PosCreditValidationResult?> ValidateCreditAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
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
            tenantId,
            deviceId: null,
            businessId,
            customerId.Value,
            credit.Amount,
            issuedAt,
            time.GetUtcNow(),
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
