using Auraly.BuildingBlocks.Domain.Time;
using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Auraly.Domain.Receivables;
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
        var now = time.GetUtcNow();
        var result = await ValidateWithinTransactionAsync(
            connection,
            transaction,
            tenantId,
            deviceId,
            request.BusinessId,
            request.CustomerId,
            request.Amount,
            now,
            now,
            cancellationToken,
            request.PartySiteId);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    internal static async Task<PosCreditValidationResult> ValidateWithinTransactionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        Guid? deviceId,
        Guid businessId,
        Guid customerId,
        decimal amount,
        DateTimeOffset issuedAt,
        DateTimeOffset evaluationAt,
        CancellationToken cancellationToken,
        Guid? partySiteId = null)
    {
        var zone = await SqlBusinessLocalDates.ReadTimeZoneAsync(connection, tenantId, businessId, cancellationToken, transaction);
        if (zone is null)
            return new(customerId, amount, null, false, "La sede no está disponible para validar el crédito.");
        var overdueBefore = SqlBusinessLocalDates.StartOfDay(
            BusinessCalendar.DateAt(evaluationAt, zone), zone);
        await using var command = new SqlCommand("""
            SELECT cp.IsCreditEnabled,cp.CreditLimit,COALESCE(cp.DefaultDueDays,0),
                   COALESCE(portfolio.OutstandingAmount,0)+COALESCE(pending.Amount,0),
                   CAST(CASE WHEN @PartySiteId IS NOT NULL AND EXISTS(
                       SELECT 1 FROM dbo.PartySites site
                       WHERE site.PartySiteId=@PartySiteId AND site.PartyId=c.PartyId AND site.IsActive=1)
                       THEN 1 ELSE 0 END AS bit),cp.MaximumOverdueInvoices,portfolio.OverdueInvoices
            FROM dbo.Customers c WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.CustomerCreditProfiles cp WITH(UPDLOCK,HOLDLOCK)
              ON cp.CustomerId=c.CustomerId AND cp.BusinessId=@BusinessId
            JOIN dbo.Businesses b ON b.BusinessId=@BusinessId AND b.TenantId=c.TenantId AND b.IsActive=1
            OUTER APPLY(
              SELECT SUM(r.OutstandingAmount) OutstandingAmount,
                COUNT(CASE WHEN r.OutstandingAmount>0 AND r.DueDate<@OverdueBefore THEN 1 END) OverdueInvoices
              FROM dbo.Receivables r WITH(UPDLOCK,HOLDLOCK)
              WHERE r.CustomerId=c.CustomerId AND r.BusinessId=@BusinessId AND r.Status IN(N'Open',N'PartiallyPaid')) portfolio
            OUTER APPLY(
              SELECT SUM(d.CreditAmount) Amount FROM dbo.SalesDocuments d WITH(UPDLOCK,HOLDLOCK)
              WHERE d.CustomerId=c.CustomerId AND d.BusinessId=@BusinessId AND d.CreditAmount>0
                AND d.ProcessingStatus IN(N'Received',N'Processing')) pending
            WHERE c.CustomerId=@CustomerId AND c.IsActive=1
              AND c.TenantId=@TenantId
              AND (@DeviceId IS NULL OR EXISTS(
                    SELECT 1 FROM dbo.DocumentSeries ds
                    WHERE ds.DeviceId=@DeviceId AND ds.BusinessId=@BusinessId
                      AND ds.IsActive=1))
            """, connection, transaction);
        command.Parameters.AddWithValue("@CustomerId", customerId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.Add("@PartySiteId", SqlDbType.UniqueIdentifier).Value =
            (object?)partySiteId ?? DBNull.Value;
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@OverdueBefore", overdueBefore);
        command.Parameters.Add("@DeviceId", SqlDbType.UniqueIdentifier).Value =
            (object?)deviceId ?? DBNull.Value;
        bool enabled;
        decimal? limit;
        int defaultDueDays;
        decimal outstanding;
        bool validSite;
        int? maximumOverdueInvoices;
        int overdueInvoices;
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
            maximumOverdueInvoices = reader.IsDBNull(5) ? null : reader.GetInt32(5);
            overdueInvoices = reader.GetInt32(6);
        }
        var decision = CustomerCreditPolicy.Evaluate(enabled, limit, outstanding,
            overdueInvoices, maximumOverdueInvoices, amount);
        if (!decision.IsAllowed)
            return new(customerId, amount, decision.AvailableCredit, false, decision.RejectionReason,
                OverdueInvoiceCount: overdueInvoices, MaximumOverdueInvoices: maximumOverdueInvoices);
        if (partySiteId is null)
            return new PosCreditValidationResult(
                customerId, amount, null, IsAllowed: false,
                "Selecciona la sede del cliente para la venta a crédito.");
        if (!validSite)
            return new PosCreditValidationResult(
                customerId, amount, null, IsAllowed: false,
                "La sede seleccionada no pertenece al cliente o está inactiva.");
        return new(customerId, amount, decision.AvailableCredit, true, null,
            DueDate: issuedAt.AddDays(defaultDueDays), OverdueInvoiceCount: overdueInvoices,
            MaximumOverdueInvoices: maximumOverdueInvoices);
    }
}
