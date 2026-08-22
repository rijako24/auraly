using Auraly.Application.WorkSessions;
using System.Data;
using Auraly.Contracts.WorkSessions;
using Auraly.Domain.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlWorkSessionStore
{
    public async Task<IReadOnlyList<CashMovementReasonView>> ListCashReasonsAsync(
        WorkSessionIdentity identity, Guid businessId, string? direction,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await ValidateBusinessScopeAsync(
            connection, transaction, identity, businessId, cancellationToken);
        await EnsureDefaultCashReasonsAsync(
            connection, transaction, businessId, cancellationToken);
        var values = new List<CashMovementReasonView>();
        await using var command = new SqlCommand(ReasonViewSql + " " + """
            WHERE r.BusinessId=@BusinessId
              AND r.ReasonType IN (N'CashIn',N'CashOut')
              AND (@Direction IS NULL OR r.Direction=@Direction)
            ORDER BY r.Direction,r.Name,r.Code;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Direction", (object?)direction ?? DBNull.Value);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                values.Add(MapReasonView(reader));
        }
        await transaction.CommitAsync(cancellationToken);
        return values;
    }

    public async Task<CashMovementReasonView> UpsertCashReasonAsync(
        WorkSessionIdentity identity, CashMovementReasonDefinition reason,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await ValidateBusinessScopeAsync(
                connection, transaction, identity, reason.BusinessId, cancellationToken);
            if (reason.DefaultCostCenterId is { } costCenterId)
                await ValidateCostCenterAsync(
                    connection, transaction, reason.BusinessId, costCenterId,
                    cancellationToken);
            var now = timeProvider.GetUtcNow();
            await using var command = new SqlCommand("""
                IF EXISTS(SELECT 1 FROM dbo.BusinessReasons WITH(UPDLOCK,HOLDLOCK)
                          WHERE ReasonId=@ReasonId AND BusinessId=@BusinessId)
                  UPDATE dbo.BusinessReasons
                  SET ReasonType=CASE WHEN @Direction=N'In' THEN N'CashIn' ELSE N'CashOut' END,
                      Code=@Code,Name=@Name,Direction=@Direction,
                      CounterpartAccountingCategory=@Category,
                      DefaultCostCenterId=@CostCenterId,
                      RequiresReference=@RequiresReference,IsActive=@IsActive,
                      UpdatedAt=@Now
                  WHERE ReasonId=@ReasonId AND BusinessId=@BusinessId;
                ELSE
                  INSERT dbo.BusinessReasons
                    (ReasonId,BusinessId,ReasonType,Code,Name,Direction,
                     CounterpartAccountingCategory,DefaultCostCenterId,
                     RequiresReference,IsSystem,IsActive,DisplayOrder,CreatedAt,UpdatedAt)
                  VALUES(@ReasonId,@BusinessId,CASE WHEN @Direction=N'In' THEN N'CashIn' ELSE N'CashOut' END,
                         @Code,@Name,@Direction,@Category,@CostCenterId,@RequiresReference,0,
                         @IsActive,0,@Now,@Now);
                """, connection, transaction);
            AddReasonParameters(command, reason, now);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await SyncCashReasonCompatibilityAsync(connection, transaction, reason, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await ReadCashReasonViewAsync(
                connection, identity, reason.BusinessId, reason.ReasonId,
                cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new WorkSessionConflictException(
                "Another cash movement reason already uses this code.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<CashMovementReasonDefinition?> FindCashReasonAsync(
        WorkSessionIdentity identity, Guid businessId, Guid reasonId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await ValidateBusinessScopeAsync(
            connection, transaction, identity, businessId, cancellationToken);
        await EnsureDefaultCashReasonsAsync(
            connection, transaction, businessId, cancellationToken);
        var reason = await ReadCashReasonDefinitionAsync(
            connection, transaction, businessId, reasonId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return reason;
    }

    private async Task EnsureDefaultCashReasonsAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.BusinessReasons
              (ReasonId,BusinessId,ReasonType,Code,Name,Direction,
               CounterpartAccountingCategory,DefaultCostCenterId,
               RequiresReference,IsSystem,IsActive,DisplayOrder,CreatedAt,UpdatedAt)
            SELECT NEWID(),@BusinessId,t.ReasonType,t.Code,t.Name,t.Direction,
                   t.CounterpartAccountingCategory,NULL,t.RequiresReference,1,1,t.DisplayOrder,@Now,@Now
            FROM dbo.AccountingConfigurationProfiles p
            INNER JOIN dbo.ReasonTemplates t
              ON t.ProfileCode=p.ProfileCode
            WHERE p.IsDefault=1 AND p.IsActive=1 AND t.IsActive=1
              AND t.ReasonType IN (N'CashIn',N'CashOut')
              AND NOT EXISTS(
                SELECT 1 FROM dbo.BusinessReasons r WITH(UPDLOCK,HOLDLOCK)
                WHERE r.BusinessId=@BusinessId AND r.ReasonType=t.ReasonType AND r.Code=t.Code);
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
        await using var compatibility = new SqlCommand("""
            INSERT dbo.CashMovementReasons(
              ReasonId,BusinessId,Code,Name,Direction,CounterpartAccountingCategory,
              DefaultCostCenterId,RequiresReference,IsActive,CreatedAt,UpdatedAt)
            SELECT r.ReasonId,r.BusinessId,r.Code,r.Name,r.Direction,r.CounterpartAccountingCategory,
                   r.DefaultCostCenterId,r.RequiresReference,r.IsActive,r.CreatedAt,r.UpdatedAt
            FROM dbo.BusinessReasons r
            WHERE r.BusinessId=@BusinessId AND r.ReasonType IN (N'CashIn',N'CashOut')
              AND NOT EXISTS(SELECT 1 FROM dbo.CashMovementReasons c
                WHERE c.BusinessId=r.BusinessId AND c.ReasonId=r.ReasonId);
            """, connection, transaction);
        compatibility.Parameters.AddWithValue("@BusinessId", businessId);
        await compatibility.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SyncCashReasonCompatibilityAsync(
        SqlConnection connection, SqlTransaction transaction,
        CashMovementReasonDefinition reason, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            IF EXISTS(SELECT 1 FROM dbo.CashMovementReasons WHERE BusinessId=@BusinessId AND ReasonId=@ReasonId)
              UPDATE dbo.CashMovementReasons SET Code=@Code,Name=@Name,Direction=@Direction,
                CounterpartAccountingCategory=@Category,DefaultCostCenterId=@CostCenterId,
                RequiresReference=@RequiresReference,IsActive=@IsActive,UpdatedAt=@Now
              WHERE BusinessId=@BusinessId AND ReasonId=@ReasonId;
            ELSE
              INSERT dbo.CashMovementReasons(ReasonId,BusinessId,Code,Name,Direction,
                CounterpartAccountingCategory,DefaultCostCenterId,RequiresReference,IsActive,CreatedAt,UpdatedAt)
              VALUES(@ReasonId,@BusinessId,@Code,@Name,@Direction,@Category,@CostCenterId,
                @RequiresReference,@IsActive,@Now,@Now);
            """, connection, transaction);
        AddReasonParameters(command, reason, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ValidateBusinessScopeAsync(
        SqlConnection connection, SqlTransaction transaction,
        WorkSessionIdentity identity, Guid businessId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT COUNT_BIG(*) FROM dbo.Businesses
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND IsActive=1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new WorkSessionForbiddenException(
                "The business is outside the authenticated tenant.");
    }

    private static async Task ValidateCostCenterAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid businessId, Guid costCenterId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT COUNT_BIG(*) FROM dbo.AccountingCostCenters
            WHERE CostCenterId=@CostCenterId AND BusinessId=@BusinessId AND IsActive=1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@CostCenterId", costCenterId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new WorkSessionValidationException(
                "The cost center is not active for this business.");
    }

    private static async Task<CashMovementReasonDefinition?>
        ReadCashReasonDefinitionAsync(
            SqlConnection connection, SqlTransaction transaction,
            Guid businessId, Guid reasonId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT ReasonId,BusinessId,Code,Name,Direction,
                   CounterpartAccountingCategory,DefaultCostCenterId,
                   RequiresReference,IsActive
            FROM dbo.BusinessReasons WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND ReasonId=@ReasonId
              AND ReasonType IN (N'CashIn',N'CashOut') AND IsActive=1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@ReasonId", reasonId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return CashMovementReasonDefinition.Create(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
            reader.GetString(3),
            Enum.Parse<CashMovementDirection>(reader.GetString(4)),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetGuid(6),
            reader.GetBoolean(7), reader.GetBoolean(8));
    }

    private static async Task<CashMovementReasonView> ReadCashReasonViewAsync(
        SqlConnection connection, WorkSessionIdentity identity, Guid businessId,
        Guid reasonId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(ReasonViewSql + " " + """
            WHERE r.BusinessId=@BusinessId AND r.ReasonId=@ReasonId
              AND r.ReasonType IN (N'CashIn',N'CashOut');
            """, connection);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@ReasonId", reasonId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new WorkSessionNotFoundException(
                "The cash movement reason was not found.");
        return MapReasonView(reader);
    }

    private static CashMovementReasonView MapReasonView(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
        reader.GetString(3), reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetGuid(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.GetBoolean(10), reader.GetBoolean(11), reader.GetBoolean(12));

    private static void AddReasonParameters(
        SqlCommand command, CashMovementReasonDefinition reason, DateTimeOffset now)
    {
        command.Parameters.AddWithValue("@ReasonId", reason.ReasonId);
        command.Parameters.AddWithValue("@BusinessId", reason.BusinessId);
        command.Parameters.AddWithValue("@Code", reason.Code);
        command.Parameters.AddWithValue("@Name", reason.Name);
        command.Parameters.AddWithValue("@Direction", reason.Direction.ToString());
        command.Parameters.AddWithValue("@Category",
            (object?)reason.CounterpartAccountingCategory ?? DBNull.Value);
        command.Parameters.AddWithValue("@CostCenterId",
            (object?)reason.DefaultCostCenterId ?? DBNull.Value);
        command.Parameters.AddWithValue("@RequiresReference", reason.RequiresReference);
        command.Parameters.AddWithValue("@IsActive", reason.IsActive);
        command.Parameters.AddWithValue("@Now", now);
    }

    private const string ReasonViewSql = """
        SELECT r.ReasonId,r.BusinessId,r.Code,r.Name,r.Direction,
               r.CounterpartAccountingCategory,r.DefaultCostCenterId,cc.Name,
               a.Code,a.Name,
               CAST(CASE WHEN r.CounterpartAccountingCategory IS NOT NULL
                              AND a.AccountId IS NOT NULL THEN 1 ELSE 0 END AS bit),
               r.RequiresReference,r.IsActive
        FROM dbo.BusinessReasons r
        INNER JOIN dbo.Businesses b
          ON b.BusinessId=r.BusinessId AND b.TenantId=@TenantId
        LEFT JOIN dbo.AccountingCostCenters cc
          ON cc.CostCenterId=r.DefaultCostCenterId
        OUTER APPLY
        (
            SELECT TOP(1) aa.AccountId,aa.Code,aa.Name
            FROM dbo.AccountingAccountMappings m
            INNER JOIN dbo.AccountingAccounts aa
              ON aa.TenantId=m.TenantId AND aa.AccountId=m.AccountId
             AND aa.IsActive=1 AND aa.AllowsPosting=1
            WHERE m.TenantId=b.TenantId
              AND m.Category=r.CounterpartAccountingCategory
              AND (m.BusinessId=r.BusinessId OR m.BusinessId IS NULL)
              AND m.EffectiveFrom<=CAST(SYSUTCDATETIME() AS date)
              AND (m.EffectiveTo IS NULL OR m.EffectiveTo>=CAST(SYSUTCDATETIME() AS date))
            ORDER BY CASE WHEN m.BusinessId=r.BusinessId THEN 0 ELSE 1 END,
                     m.EffectiveFrom DESC
        ) a
        """;
}
