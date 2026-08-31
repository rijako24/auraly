using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Contracts.TenantBilling;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Auraly.Platform.Infrastructure.Identity;

public sealed class SqlTenantRenewalOrderStore(ApplicationDbContext db) : ITenantRenewalOrderStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<TenantRenewalOrderDto?> GetCurrentAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(ReadSql + " WHERE subscription.TenantId=@TenantId AND renewal.IsCurrent=1;", connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        return await ReadAsync(command, cancellationToken);
    }

    public async Task<TenantRenewalOrderDto> CreateRevisionAsync(
        Guid tenantId, Guid userId, TenantQuoteDto quote, CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var id = Guid.NewGuid();
        var lines = JsonSerializer.Serialize(quote.Lines, Json);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(quote, Json)));
        try
        {
            await using var command = new SqlCommand("""
                DECLARE @SubscriptionId uniqueidentifier,@CurrentEnd datetimeoffset(7),@PlanId uniqueidentifier,
                        @Revision int,@FullUsed int,@SellerUsed int,@PosUsed int,@PayrollUsed int;
                SELECT @SubscriptionId=TenantSubscriptionId,@CurrentEnd=CurrentPeriodEnd
                FROM billing.TenantSubscriptions WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantId=@TenantId AND Status IN(N'Active',N'PastDue',N'Suspended');
                IF @SubscriptionId IS NULL THROW 51080,N'El tenant no tiene una suscripción renovable.',1;
                SELECT @PlanId=planValue.TenantCommercialPlanId
                FROM billing.TenantCommercialPlans planValue
                JOIN billing.BillableServices serviceValue ON serviceValue.BillableServiceId=planValue.BillableServiceId
                WHERE serviceValue.Code=@PlanCode AND planValue.IsActive=1 AND serviceValue.IsActive=1;
                IF @PlanId IS NULL THROW 51081,N'El plan cotizado ya no está disponible.',1;

                SELECT @FullUsed=COUNT(*) FROM dbo.AppUsers app
                WHERE app.TenantId=@TenantId AND app.IsActive=1
                  AND NOT EXISTS(
                    SELECT 1 FROM dbo.CommerceSellers seller
                    JOIN dbo.Businesses businessValue ON businessValue.BusinessId=seller.BusinessId
                    WHERE businessValue.TenantId=@TenantId AND seller.PartyId=app.PartyId AND seller.IsActive=1);
                SELECT @SellerUsed=COUNT(*) FROM dbo.CommerceSellers seller
                JOIN dbo.Businesses businessValue ON businessValue.BusinessId=seller.BusinessId
                WHERE businessValue.TenantId=@TenantId AND seller.IsActive=1;
                SELECT @PosUsed=COUNT(*) FROM dbo.EnrolledDevices WHERE TenantId=@TenantId AND IsActive=1;
                SELECT @PayrollUsed=COUNT(*) FROM payroll.Employments WHERE TenantId=@TenantId AND IsActive=1;

                IF @FullUsers<@FullUsed THROW 51082,N'La nueva capacidad de usuarios completos es menor que el uso activo.',1;
                IF @SellerUsers<@SellerUsed THROW 51083,N'La nueva capacidad de vendedores es menor que el uso activo.',1;
                IF @PosDevices<@PosUsed THROW 51084,N'La nueva capacidad de cajas es menor que el uso activo.',1;
                IF @PayrollEmployees<@PayrollUsed THROW 51085,N'La nueva capacidad de empleados de nómina es menor que el uso activo.',1;
                IF EXISTS(SELECT 1 FROM billing.TenantSubscriptionRenewalOrders WITH(UPDLOCK,HOLDLOCK)
                          WHERE TenantSubscriptionId=@SubscriptionId AND IsCurrent=1
                            AND PaymentTransactionId IS NOT NULL AND Status=N'PendingPayment')
                    THROW 51086,N'El pago ya fue iniciado; cancélalo antes de modificar la orden.',1;

                UPDATE billing.TenantSubscriptionRenewalOrders
                SET IsCurrent=0,Status=CASE WHEN Status IN(N'Draft',N'PendingPayment') THEN N'Cancelled' ELSE Status END,
                    UpdatedAt=@Now
                WHERE TenantSubscriptionId=@SubscriptionId AND TargetPeriodStart=@CurrentEnd AND IsCurrent=1;
                SELECT @Revision=ISNULL(MAX(Revision),0)+1
                FROM billing.TenantSubscriptionRenewalOrders WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantSubscriptionId=@SubscriptionId AND TargetPeriodStart=@CurrentEnd;

                INSERT billing.TenantSubscriptionRenewalOrders
                  (TenantSubscriptionRenewalOrderId,TenantSubscriptionId,Revision,IsCurrent,Status,
                   TargetPeriodStart,TargetPeriodEnd,DueAt,TenantCommercialPlanId,BillingPeriod,CurrencyCode,
                   MonthlySubtotal,Periods,DiscountRate,DiscountAmount,TaxAmount,PayableAmount,
                   FullUserLimit,SellerUserLimit,PosDeviceLimit,DianDocumentMonthlyLimit,PayrollEmployeeLimit,
                   LinesJson,OrderHash,CreatedByUserId,CreatedAt,UpdatedAt)
                VALUES(@OrderId,@SubscriptionId,@Revision,1,N'Draft',@CurrentEnd,DATEADD(month,@Periods,@CurrentEnd),
                   @CurrentEnd,@PlanId,@BillingPeriod,N'COP',@MonthlySubtotal,@Periods,@DiscountRate,
                   @DiscountAmount,@TaxAmount,@PayableAmount,@FullUsers,@SellerUsers,@PosDevices,@DianDocuments,
                   @PayrollEmployees,@LinesJson,@OrderHash,@UserId,@Now,@Now);
                """, connection, transaction);
            command.Parameters.AddWithValue("@TenantId", tenantId);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@OrderId", id);
            command.Parameters.AddWithValue("@PlanCode", quote.PlanCode);
            command.Parameters.AddWithValue("@BillingPeriod", quote.BillingPeriod);
            command.Parameters.AddWithValue("@MonthlySubtotal", quote.MonthlySubtotalCop);
            command.Parameters.AddWithValue("@Periods", quote.Periods);
            command.Parameters.AddWithValue("@DiscountRate", quote.DiscountRate);
            command.Parameters.AddWithValue("@DiscountAmount", quote.DiscountAmountCop);
            command.Parameters.AddWithValue("@TaxAmount", quote.TaxAmountCop);
            command.Parameters.AddWithValue("@PayableAmount", quote.PayableAmountCop);
            command.Parameters.AddWithValue("@FullUsers", quote.FullUserLimit);
            command.Parameters.AddWithValue("@SellerUsers", quote.SellerUserLimit);
            command.Parameters.AddWithValue("@PosDevices", quote.PosDeviceLimit);
            command.Parameters.AddWithValue("@DianDocuments", quote.DianDocumentMonthlyLimit);
            command.Parameters.AddWithValue("@PayrollEmployees", quote.PayrollEmployeeLimit);
            command.Parameters.AddWithValue("@LinesJson", lines);
            command.Parameters.Add("@OrderHash", SqlDbType.VarBinary, 32).Value = hash;
            command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is >= 51080 and <= 51087)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ArgumentException(exception.Message, exception);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        var result = await GetCurrentAsync(tenantId, cancellationToken);
        return result ?? throw new InvalidOperationException("No fue posible recuperar la orden creada.");
    }

    private const string ReadSql = """
        SELECT renewal.TenantSubscriptionRenewalOrderId,renewal.Revision,renewal.Status,renewal.IsCurrent,
               renewal.TargetPeriodStart,renewal.TargetPeriodEnd,renewal.DueAt,
               serviceValue.Code,serviceValue.Name,renewal.BillingPeriod,renewal.MonthlySubtotal,
               renewal.Periods,(renewal.MonthlySubtotal*renewal.Periods),renewal.DiscountRate,
               renewal.DiscountAmount,renewal.TaxAmount,renewal.PayableAmount,
               renewal.FullUserLimit,renewal.SellerUserLimit,renewal.PosDeviceLimit,
               renewal.DianDocumentMonthlyLimit,renewal.PayrollEmployeeLimit,renewal.LinesJson,
               (SELECT COUNT(*) FROM dbo.AppUsers app WHERE app.TenantId=subscription.TenantId AND app.IsActive=1
                  AND NOT EXISTS(SELECT 1 FROM dbo.CommerceSellers seller JOIN dbo.Businesses businessValue ON businessValue.BusinessId=seller.BusinessId
                    WHERE businessValue.TenantId=subscription.TenantId AND seller.PartyId=app.PartyId AND seller.IsActive=1)),
               (SELECT COUNT(*) FROM dbo.CommerceSellers seller JOIN dbo.Businesses businessValue ON businessValue.BusinessId=seller.BusinessId
                  WHERE businessValue.TenantId=subscription.TenantId AND seller.IsActive=1),
               (SELECT COUNT(*) FROM dbo.EnrolledDevices deviceValue WHERE deviceValue.TenantId=subscription.TenantId AND deviceValue.IsActive=1),
               (SELECT COUNT(*) FROM payroll.Employments employment WHERE employment.TenantId=subscription.TenantId AND employment.IsActive=1)
        FROM billing.TenantSubscriptionRenewalOrders renewal
        JOIN billing.TenantSubscriptions subscription ON subscription.TenantSubscriptionId=renewal.TenantSubscriptionId
        JOIN billing.TenantCommercialPlans planValue ON planValue.TenantCommercialPlanId=renewal.TenantCommercialPlanId
        JOIN billing.BillableServices serviceValue ON serviceValue.BillableServiceId=planValue.BillableServiceId
        """;

    private static async Task<TenantRenewalOrderDto?> ReadAsync(SqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var lines = JsonSerializer.Deserialize<IReadOnlyList<TenantQuoteLineDto>>(reader.GetString(22), Json) ?? [];
        var quote = new TenantQuoteDto(reader.GetString(7), reader.GetString(8), reader.GetString(9),
            reader.GetDecimal(10), reader.GetInt32(11), reader.GetDecimal(12), reader.GetDecimal(13),
            reader.GetDecimal(14), reader.GetDecimal(15), reader.GetDecimal(16),
            decimal.Round(reader.GetDecimal(16)/reader.GetInt32(11),2,MidpointRounding.AwayFromZero),
            reader.GetInt32(17),reader.GetInt32(18),reader.GetInt32(19),reader.GetInt32(20),reader.GetInt32(21),lines);
        return new(reader.GetGuid(0),reader.GetInt32(1),reader.GetString(2),reader.GetBoolean(3),
            reader.GetFieldValue<DateTimeOffset>(4),reader.GetFieldValue<DateTimeOffset>(5),reader.GetFieldValue<DateTimeOffset>(6),
            quote,new(reader.GetInt32(23),reader.GetInt32(24),reader.GetInt32(25),reader.GetInt32(26)));
    }
}
