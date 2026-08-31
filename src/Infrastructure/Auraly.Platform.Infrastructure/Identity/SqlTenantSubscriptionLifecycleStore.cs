using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Contracts.TenantBilling;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Auraly.Platform.Infrastructure.Identity;

public sealed class SqlTenantSubscriptionLifecycleStore(ApplicationDbContext db)
    : ITenantSubscriptionLifecycleStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task ReconcileSchedulesAsync(
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            INSERT dbo.ScheduledAutomationJobs
              (ScheduledAutomationJobId,BusinessId,ReservationId,AgentId,TenantSubscriptionId,
               JobType,ScheduledAtUtc,Status,DeduplicationKey,Attempts,PayloadJson,CreatedAt)
            SELECT NEWID(),NULL,NULL,NULL,subscription.TenantSubscriptionId,2,
                   CONVERT(datetime2,SWITCHOFFSET(
                     DATEADD(day,-settings.PreDueReminderDays,subscription.CurrentPeriodEnd),'+00:00')),
                   0,CONCAT(N'tenant-subscription-lifecycle:',
                     LOWER(CONVERT(nvarchar(36),subscription.TenantSubscriptionId))),0,N'{}',@Now
            FROM billing.TenantSubscriptions subscription
            CROSS JOIN billing.PlatformBillingSettings settings
            WHERE settings.PlatformBillingSettingId=1
              AND subscription.Status IN(N'Active',N'PastDue',N'Suspended')
              AND NOT EXISTS(
                SELECT 1 FROM dbo.ScheduledAutomationJobs scheduled WITH(UPDLOCK,HOLDLOCK)
                WHERE scheduled.TenantSubscriptionId=subscription.TenantSubscriptionId
                  AND scheduled.JobType=2);
            """, connection);
        command.Parameters.AddWithValue("@Now", now.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TenantSubscriptionLifecycleCandidate>> GetDueAsync(
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        await using var command = new SqlCommand("""
            DECLARE @Claimed TABLE(ScheduledAutomationJobId uniqueidentifier,TenantSubscriptionId uniqueidentifier);
            ;WITH due AS(
              SELECT TOP(200) scheduled.ScheduledAutomationJobId
              FROM dbo.ScheduledAutomationJobs scheduled WITH(UPDLOCK,READPAST,ROWLOCK)
              JOIN billing.TenantSubscriptions eligible
                ON eligible.TenantSubscriptionId=scheduled.TenantSubscriptionId
              WHERE scheduled.JobType=2 AND scheduled.ScheduledAtUtc<=@Now
                AND eligible.Status IN(N'Active',N'PastDue',N'Suspended')
                AND (scheduled.Status=0 OR
                     (scheduled.Status=1 AND scheduled.LockedUntilUtc<=@Now))
              ORDER BY scheduled.ScheduledAtUtc,scheduled.ScheduledAutomationJobId)
            UPDATE scheduled
            SET Status=1,LockedUntilUtc=DATEADD(minute,5,@Now),Attempts=Attempts+1,
                UpdatedAt=@Now,LastError=NULL
            OUTPUT inserted.ScheduledAutomationJobId,inserted.TenantSubscriptionId INTO @Claimed
            FROM dbo.ScheduledAutomationJobs scheduled
            JOIN due ON due.ScheduledAutomationJobId=scheduled.ScheduledAutomationJobId;

            SELECT claimed.ScheduledAutomationJobId,subscription.TenantSubscriptionId,subscription.TenantId,
                   subscription.CurrentPeriodEnd,planService.Code,subscription.BillingPeriod,
                   subscription.FullUserLimit,subscription.SellerUserLimit,subscription.PosDeviceLimit,
                   subscription.DianDocumentMonthlyLimit,subscription.PayrollEmployeeLimit,
                   pricingPlan.IncludedFullUsers,pricingPlan.IncludedSellerUsers,
                   pricingPlan.IncludedPosDevices,pricingPlan.IncludedDianDocuments,
                   pricingPlan.IncludedPayrollEmployees,
                   dianPack.UnitSize,payrollPack.UnitSize,
                   settings.EmailRemindersEnabled,settings.PreDueReminderDays,
                   settings.OverdueReminderIntervalDays,settings.GracePeriodDays,
                   settings.BillingTimeZoneId
            FROM @Claimed claimed
            JOIN billing.TenantSubscriptions subscription
              ON subscription.TenantSubscriptionId=claimed.TenantSubscriptionId
            JOIN billing.TenantCommercialPlans selectedPlan
              ON selectedPlan.TenantCommercialPlanId=subscription.TenantCommercialPlanId
            JOIN billing.BillableServices planService
              ON planService.BillableServiceId=selectedPlan.BillableServiceId
            JOIN billing.BillableServices pricingService
              ON pricingService.Code=CASE WHEN planService.Code=N'custom' THEN N'company' ELSE planService.Code END
             AND pricingService.IsActive=1
            JOIN billing.TenantCommercialPlans pricingPlan
              ON pricingPlan.BillableServiceId=pricingService.BillableServiceId AND pricingPlan.IsActive=1
            CROSS APPLY(
              SELECT TOP(1) addOn.UnitSize
              FROM billing.TenantCommercialAddOns addOn
              JOIN billing.BillableServices serviceValue ON serviceValue.BillableServiceId=addOn.BillableServiceId
              WHERE serviceValue.Code=N'dian_document_pack' AND serviceValue.IsActive=1 AND addOn.IsActive=1) dianPack
            CROSS APPLY(
              SELECT TOP(1) addOn.UnitSize
              FROM billing.TenantCommercialAddOns addOn
              JOIN billing.BillableServices serviceValue ON serviceValue.BillableServiceId=addOn.BillableServiceId
              WHERE serviceValue.Code=N'payroll_employee_pack' AND serviceValue.IsActive=1 AND addOn.IsActive=1) payrollPack
            CROSS JOIN billing.PlatformBillingSettings settings
            WHERE settings.PlatformBillingSettingId=1
              AND subscription.Status IN(N'Active',N'PastDue',N'Suspended');
            """, connection, transaction);
        command.Parameters.AddWithValue("@Now", now.UtcDateTime);
        var result = new List<TenantSubscriptionLifecycleCandidate>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetFieldValue<DateTimeOffset>(3),
                    reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetInt32(7),
                    reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10), reader.GetInt32(11),
                    reader.GetInt32(12), reader.GetInt32(13), reader.GetInt32(14), reader.GetInt32(15),
                    reader.GetInt32(16), reader.GetInt32(17), reader.GetBoolean(18),
                    reader.GetInt32(19), reader.GetInt32(20), reader.GetInt32(21), reader.GetString(22)));
            }
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        return result;
    }

    public async Task ApplyAsync(
        TenantSubscriptionLifecycleCandidate candidate,
        TenantQuoteDto quote,
        TenantSubscriptionLifecycleDecision decision,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var orderId = Guid.NewGuid();
        var lines = JsonSerializer.Serialize(quote.Lines, Json);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(quote, Json)));
        try
        {
            await using var command = new SqlCommand("""
                DECLARE @CurrentEnd datetimeoffset(7),@Status nvarchar(24),@PlanId uniqueidentifier,
                        @ExistingOrderId uniqueidentifier,@DueAt datetimeoffset(7),@DaysOverdue int,
                        @ConfiguredPreDue int,@ConfiguredInterval int,@ConfiguredGrace int,
                        @ConfiguredEmail bit,@ConfiguredTimeZone nvarchar(100),
                        @JobSubscriptionId uniqueidentifier;

                SELECT @JobSubscriptionId=TenantSubscriptionId
                FROM dbo.ScheduledAutomationJobs WITH(UPDLOCK,HOLDLOCK)
                WHERE ScheduledAutomationJobId=@ScheduledJobId AND JobType=2 AND Status=1;
                IF @JobSubscriptionId<>@SubscriptionId RETURN;

                SELECT @CurrentEnd=CurrentPeriodEnd,@Status=Status
                FROM billing.TenantSubscriptions WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantSubscriptionId=@SubscriptionId AND TenantId=@TenantId
                  AND Status IN(N'Active',N'PastDue',N'Suspended');
                SELECT @ConfiguredPreDue=PreDueReminderDays,@ConfiguredInterval=OverdueReminderIntervalDays,
                       @ConfiguredGrace=GracePeriodDays,@ConfiguredEmail=EmailRemindersEnabled,
                       @ConfiguredTimeZone=BillingTimeZoneId
                FROM billing.PlatformBillingSettings WITH(UPDLOCK,HOLDLOCK)
                WHERE PlatformBillingSettingId=1;
                IF @ConfiguredGrace IS NULL THROW 51090,N'La política global de cobranza no está configurada.',1;
                IF @CurrentEnd IS NULL
                BEGIN
                    UPDATE dbo.ScheduledAutomationJobs
                    SET Status=3,LockedUntilUtc=NULL,LastError=N'La suscripción ya no es evaluable',UpdatedAt=@Now
                    WHERE ScheduledAutomationJobId=@ScheduledJobId;
                    RETURN;
                END;
                IF @CurrentEnd<>@ExpectedPeriodEnd
                BEGIN
                    UPDATE dbo.ScheduledAutomationJobs
                    SET Status=0,ScheduledAtUtc=CONVERT(datetime2,SWITCHOFFSET(
                          DATEADD(day,-@ConfiguredPreDue,@CurrentEnd),'+00:00')),
                        LockedUntilUtc=NULL,LastError=NULL,UpdatedAt=@Now
                    WHERE ScheduledAutomationJobId=@ScheduledJobId;
                    RETURN;
                END;
                IF @ConfiguredPreDue<>@ExpectedPreDue OR @ConfiguredInterval<>@ExpectedInterval
                   OR @ConfiguredGrace<>@ExpectedGrace OR @ConfiguredEmail<>@ExpectedEmail
                   OR @ConfiguredTimeZone<>@ExpectedTimeZone
                BEGIN
                    UPDATE dbo.ScheduledAutomationJobs
                    SET Status=0,ScheduledAtUtc=@Now,LockedUntilUtc=NULL,
                        LastError=NULL,UpdatedAt=@Now
                    WHERE ScheduledAutomationJobId=@ScheduledJobId;
                    RETURN;
                END;

                SELECT @ExistingOrderId=TenantSubscriptionRenewalOrderId,@DueAt=DueAt
                FROM billing.TenantSubscriptionRenewalOrders WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantSubscriptionId=@SubscriptionId AND TargetPeriodStart=@CurrentEnd AND IsCurrent=1;

                IF @ExistingOrderId IS NULL
                BEGIN
                    SELECT @PlanId=planValue.TenantCommercialPlanId
                    FROM billing.TenantCommercialPlans planValue
                    JOIN billing.BillableServices serviceValue ON serviceValue.BillableServiceId=planValue.BillableServiceId
                    WHERE serviceValue.Code=@PlanCode AND planValue.IsActive=1 AND serviceValue.IsActive=1;
                    IF @PlanId IS NULL THROW 51091,N'El plan de la renovación ya no está disponible.',1;

                    INSERT billing.TenantSubscriptionRenewalOrders
                      (TenantSubscriptionRenewalOrderId,TenantSubscriptionId,Revision,IsCurrent,Status,
                       TargetPeriodStart,TargetPeriodEnd,DueAt,TenantCommercialPlanId,BillingPeriod,CurrencyCode,
                       MonthlySubtotal,Periods,DiscountRate,DiscountAmount,TaxAmount,PayableAmount,
                       FullUserLimit,SellerUserLimit,PosDeviceLimit,DianDocumentMonthlyLimit,PayrollEmployeeLimit,
                       LinesJson,OrderHash,CreatedByUserId,CreatedAt,UpdatedAt)
                    VALUES(@OrderId,@SubscriptionId,1,1,N'Draft',@CurrentEnd,DATEADD(month,@Periods,@CurrentEnd),
                       @CurrentEnd,@PlanId,@BillingPeriod,N'COP',@MonthlySubtotal,@Periods,@DiscountRate,
                       @DiscountAmount,@TaxAmount,@PayableAmount,@FullUsers,@SellerUsers,@PosDevices,
                       @DianDocuments,@PayrollEmployees,@LinesJson,@OrderHash,NULL,@Now,@Now);
                    SET @ExistingOrderId=@OrderId;
                    SET @DueAt=@CurrentEnd;
                END;

                IF @TargetStatus IS NOT NULL
                    UPDATE billing.TenantSubscriptions SET Status=@TargetStatus,UpdatedAt=@Now
                    WHERE TenantSubscriptionId=@SubscriptionId AND Status<>@TargetStatus;

                IF @EventKey IS NOT NULL
                BEGIN
                    DECLARE @Created TABLE(NotificationId uniqueidentifier,UserId uniqueidentifier);
                    INSERT billing.TenantBillingNotifications
                      (TenantBillingNotificationId,TenantId,UserId,TenantSubscriptionRenewalOrderId,
                       EventKey,Title,Message,ActionUrl,CreatedAt)
                    OUTPUT inserted.TenantBillingNotificationId,inserted.UserId INTO @Created
                    SELECT NEWID(),@TenantId,userValue.UserId,@ExistingOrderId,@EventKey,@Title,@Message,
                           CONCAT(N'/dashboard/subscription?order=',CONVERT(nvarchar(36),@ExistingOrderId)),@Now
                    FROM dbo.AppUsers userValue
                    WHERE userValue.TenantId=@TenantId AND userValue.IsActive=1
                      AND EXISTS(
                        SELECT 1 FROM dbo.UserRoles assignment
                        JOIN dbo.AppRoles roleValue ON roleValue.RoleId=assignment.RoleId
                          AND roleValue.TenantId=@TenantId AND roleValue.IsActive=1
                        JOIN dbo.RolePermissions grantValue ON grantValue.RoleId=roleValue.RoleId
                        JOIN dbo.Permissions permissionValue ON permissionValue.PermissionId=grantValue.PermissionId
                        WHERE assignment.UserId=userValue.UserId
                          AND permissionValue.Resource=N'subscription.manage')
                      AND NOT EXISTS(
                        SELECT 1 FROM billing.TenantBillingNotifications existing
                        WHERE existing.UserId=userValue.UserId
                          AND existing.TenantSubscriptionRenewalOrderId=@ExistingOrderId
                          AND existing.EventKey=@EventKey);

                    IF @SendEmail=1
                    BEGIN
                        DECLARE @Emails TABLE(NotificationId uniqueidentifier,MessageId uniqueidentifier,TenantId uniqueidentifier);
                        INSERT @Emails SELECT NotificationId,NEWID(),@TenantId FROM @Created;
                        INSERT dbo.TenantProvisioningOutboxMessages
                          (MessageId,TenantId,Type,Payload,OccurredAt,AvailableAt,AttemptCount)
                        SELECT item.MessageId,item.TenantId,N'SubscriptionPaymentReminder',
                               (SELECT item.NotificationId AS notificationId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
                               @Now,@Now,0
                        FROM @Emails item;
                        UPDATE notificationValue SET EmailOutboxMessageId=item.MessageId
                        FROM billing.TenantBillingNotifications notificationValue
                        JOIN @Emails item ON item.NotificationId=notificationValue.TenantBillingNotificationId;
                    END;
                END;

                UPDATE dbo.ScheduledAutomationJobs
                SET Status=CASE WHEN @NextEvaluationAt IS NULL THEN 2 ELSE 0 END,
                    ScheduledAtUtc=COALESCE(CONVERT(datetime2,SWITCHOFFSET(@NextEvaluationAt,'+00:00')),ScheduledAtUtc),
                    LockedUntilUtc=NULL,SentAtUtc=CASE WHEN @NextEvaluationAt IS NULL THEN @Now ELSE NULL END,
                    LastError=NULL,UpdatedAt=@Now
                WHERE ScheduledAutomationJobId=@ScheduledJobId AND TenantSubscriptionId=@SubscriptionId;
                """, connection, transaction);
            Add(command, "@ScheduledJobId", candidate.ScheduledJobId);
            Add(command, "@SubscriptionId", candidate.SubscriptionId);
            Add(command, "@TenantId", candidate.TenantId);
            Add(command, "@ExpectedPeriodEnd", candidate.CurrentPeriodEnd);
            Add(command, "@ExpectedPreDue", candidate.PreDueReminderDays);
            Add(command, "@ExpectedInterval", candidate.OverdueReminderIntervalDays);
            Add(command, "@ExpectedGrace", candidate.GracePeriodDays);
            Add(command, "@ExpectedEmail", candidate.EmailRemindersEnabled);
            Add(command, "@ExpectedTimeZone", candidate.BillingTimeZoneId);
            Add(command, "@OrderId", orderId);
            Add(command, "@PlanCode", quote.PlanCode);
            Add(command, "@BillingPeriod", quote.BillingPeriod);
            Add(command, "@MonthlySubtotal", quote.MonthlySubtotalCop);
            Add(command, "@Periods", quote.Periods);
            Add(command, "@DiscountRate", quote.DiscountRate);
            Add(command, "@DiscountAmount", quote.DiscountAmountCop);
            Add(command, "@TaxAmount", quote.TaxAmountCop);
            Add(command, "@PayableAmount", quote.PayableAmountCop);
            Add(command, "@FullUsers", quote.FullUserLimit);
            Add(command, "@SellerUsers", quote.SellerUserLimit);
            Add(command, "@PosDevices", quote.PosDeviceLimit);
            Add(command, "@DianDocuments", quote.DianDocumentMonthlyLimit);
            Add(command, "@PayrollEmployees", quote.PayrollEmployeeLimit);
            Add(command, "@LinesJson", lines);
            Add(command, "@TargetStatus", (object?)decision.SubscriptionStatus ?? DBNull.Value);
            Add(command, "@EventKey", (object?)decision.EventKey ?? DBNull.Value);
            Add(command, "@Title", (object?)decision.Title ?? DBNull.Value);
            Add(command, "@Message", (object?)decision.Message ?? DBNull.Value);
            Add(command, "@SendEmail", decision.SendEmail);
            Add(command, "@NextEvaluationAt", (object?)decision.NextEvaluationAt ?? DBNull.Value);
            command.Parameters.Add("@OrderHash", SqlDbType.VarBinary, 32).Value = hash;
            Add(command, "@Now", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static void Add(SqlCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);
}
