using System.Data;
using System.Text.Json;
using Auraly.Contracts.TenantBilling;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Auraly.Platform.Infrastructure.Identity;

public sealed class SqlPlatformBillingPolicyStore(ApplicationDbContext db)
    : IPlatformBillingPolicyStore
{
    public async Task<PlatformBillingPolicyDto> GetAsync(CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(ReadSql, connection);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? Read(reader)
            : throw new InvalidOperationException("La política global de cobranza no está configurada.");
    }

    public async Task<PlatformBillingPolicyDto> UpdateAsync(
        Guid actorTenantId, Guid actorUserId, UpdatePlatformBillingPolicyRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        byte[] expected;
        try { expected = Convert.FromBase64String(request.Version); }
        catch (FormatException exception) { throw new ArgumentException("La versión de la política no es válida.", exception); }

        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            PlatformBillingPolicyDto current;
            await using (var read = new SqlCommand(LockReadSql, connection, transaction))
            await using (var reader = await read.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken))
                current = await reader.ReadAsync(cancellationToken)
                    ? Read(reader)
                    : throw new InvalidOperationException("La política global de cobranza no está configurada.");
            if (!Convert.FromBase64String(current.Version).AsSpan().SequenceEqual(expected))
                throw new InvalidOperationException("La política cambió mientras la editabas. Recárgala e inténtalo de nuevo.");

            var now = DateTimeOffset.UtcNow;
            await using var update = new SqlCommand("""
                UPDATE billing.PlatformBillingSettings
                SET EmailRemindersEnabled=@Email,PreDueReminderDays=@PreDue,
                    OverdueReminderIntervalDays=@Interval,GracePeriodDays=@Grace,
                    BillingTimeZoneId=@TimeZone,UpdatedByUserId=@Actor,UpdatedAt=@Now
                WHERE PlatformBillingSettingId=1 AND RowVersion=@Version;
                """, connection, transaction);
            Add(update, "@Email", request.EmailRemindersEnabled);
            Add(update, "@PreDue", request.PreDueReminderDays);
            Add(update, "@Interval", request.OverdueReminderIntervalDays);
            Add(update, "@Grace", request.GracePeriodDays);
            Add(update, "@TimeZone", request.BillingTimeZoneId.Trim());
            Add(update, "@Actor", actorUserId);
            Add(update, "@Now", now);
            update.Parameters.Add("@Version", SqlDbType.Timestamp, 8).Value = expected;
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("La política cambió mientras la editabas. Recárgala e inténtalo de nuevo.");

            await using var reschedule = new SqlCommand("""
                UPDATE scheduled
                SET Status=0,ScheduledAtUtc=@Now,LockedUntilUtc=NULL,SentAtUtc=NULL,
                    LastError=NULL,UpdatedAt=@Now
                FROM dbo.ScheduledAutomationJobs scheduled
                JOIN billing.TenantSubscriptions subscription
                  ON subscription.TenantSubscriptionId=scheduled.TenantSubscriptionId
                WHERE scheduled.JobType=2
                  AND subscription.Status IN(N'Active',N'PastDue',N'Suspended');
                """, connection, transaction);
            Add(reschedule, "@Now", now.UtcDateTime);
            await reschedule.ExecuteNonQueryAsync(cancellationToken);

            var nextValues = new
            {
                request.EmailRemindersEnabled, request.PreDueReminderDays,
                request.OverdueReminderIntervalDays, request.GracePeriodDays,
                BillingTimeZoneId = request.BillingTimeZoneId.Trim(),
                Reason = request.Reason.Trim()
            };
            await using var audit = new SqlCommand("""
                INSERT dbo.AuditLogs
                  (AuditLogId,UserId,TenantId,Action,EntityType,EntityId,OldValues,NewValues,Timestamp)
                VALUES(NEWID(),@Actor,@Tenant,N'PlatformBillingPolicy.Updated',N'PlatformBillingSettings',N'1',
                       @OldValues,@NewValues,@Timestamp);
                """, connection, transaction);
            Add(audit, "@Actor", actorUserId);
            Add(audit, "@Tenant", actorTenantId);
            Add(audit, "@OldValues", JsonSerializer.Serialize(current));
            Add(audit, "@NewValues", JsonSerializer.Serialize(nextValues));
            Add(audit, "@Timestamp", now.UtcDateTime);
            await audit.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private const string ReadSql = """
        SELECT EmailRemindersEnabled,PreDueReminderDays,OverdueReminderIntervalDays,
               GracePeriodDays,BillingTimeZoneId,UpdatedAt,RowVersion
        FROM billing.PlatformBillingSettings WHERE PlatformBillingSettingId=1;
        """;
    private const string LockReadSql = """
        SELECT EmailRemindersEnabled,PreDueReminderDays,OverdueReminderIntervalDays,
               GracePeriodDays,BillingTimeZoneId,UpdatedAt,RowVersion
        FROM billing.PlatformBillingSettings WITH(UPDLOCK,HOLDLOCK)
        WHERE PlatformBillingSettingId=1;
        """;

    private static PlatformBillingPolicyDto Read(SqlDataReader reader) => new(
        reader.GetBoolean(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
        reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5),
        Convert.ToBase64String((byte[])reader[6]));

    private static void Validate(UpdatePlatformBillingPolicyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PreDueReminderDays is < 0 or > 90) throw new ArgumentException("Los días previos deben estar entre 0 y 90.");
        if (request.OverdueReminderIntervalDays is < 1 or > 30) throw new ArgumentException("El intervalo de mora debe estar entre 1 y 30 días.");
        if (request.GracePeriodDays is < 1 or > 90) throw new ArgumentException("El periodo de gracia debe estar entre 1 y 90 días.");
        if (request.GracePeriodDays <= request.OverdueReminderIntervalDays) throw new ArgumentException("El periodo de gracia debe superar el intervalo de recordatorio.");
        if (string.IsNullOrWhiteSpace(request.BillingTimeZoneId)) throw new ArgumentException("La zona horaria es obligatoria.");
        _ = TimeZoneInfo.FindSystemTimeZoneById(request.BillingTimeZoneId.Trim());
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
            throw new ArgumentException("Explica brevemente el motivo del cambio.");
    }

    private static void Add(SqlCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);
}
