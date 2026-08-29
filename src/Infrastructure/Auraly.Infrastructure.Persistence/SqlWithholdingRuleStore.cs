using System.Data;
using System.Text.Json;
using Auraly.BuildingBlocks.Infrastructure;
using Auraly.Commerce.Taxation.Application;
using Auraly.Commerce.Taxation.Domain;
using Auraly.Commerce.Taxation.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlWithholdingRuleStore(
    SqlServerConnectionFactory connections, TimeProvider timeProvider) : IWithholdingRuleStore
{
    public async Task<IReadOnlyList<WithholdingRule>> ListAsync(
        Guid tenantId, Guid businessId, bool includeInactive, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            WITH versions AS (
              SELECT r.*,ROW_NUMBER() OVER(PARTITION BY r.RuleId ORDER BY r.Version DESC) AS rn
              FROM dbo.WithholdingRules r
              JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId
              WHERE r.BusinessId=@BusinessId AND b.TenantId=@TenantId
            )
            SELECT RuleId,BusinessId,Version,Code,Name,Kind,Direction,Moment,BaseKind,
                   ConceptCode,JurisdictionCode,Rate,MinimumBase,RequiredResponsibilities,
                   EffectiveFrom,EffectiveTo,IsActive
            FROM versions WHERE rn=1 AND (@IncludeInactive=1 OR IsActive=1)
            ORDER BY Kind,Code;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@IncludeInactive", includeInactive);
        var rules = new List<WithholdingRule>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rules.Add(Read(reader));
        return rules;
    }

    public async Task<WithholdingRule> SaveVersionAsync(
        Guid tenantId, Guid userId, Guid? ruleId, WithholdingRule proposed, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        try
        {
            await EnsureScopeAsync(connection, transaction, tenantId, proposed.BusinessId, userId, ct);
            var id = ruleId ?? proposed.RuleId;
            var nextVersion = await GetNextVersionAsync(connection, transaction, proposed.BusinessId, id, ct);
            var rule = WithholdingRule.Create(id, proposed.BusinessId, nextVersion, proposed.Code,
                proposed.Name, proposed.Kind, proposed.Direction, proposed.Moment, proposed.BaseKind,
                proposed.ConceptCode, proposed.JurisdictionCode, proposed.Rate, proposed.MinimumBase,
                proposed.RequiredResponsibilities, proposed.EffectiveFrom, proposed.EffectiveTo, proposed.IsActive);
            await InsertAsync(connection, transaction, rule, userId, ct);
            await EnqueueCustomerSynchronizationAsync(
                connection, transaction, proposed.BusinessId, ct);
            await transaction.CommitAsync(ct);
            return rule;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }


    public async Task<CounterpartyTaxProfileView?> GetProfileAsync(
        Guid tenantId, Guid businessId, Guid counterpartyId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
              THROW 51300,'The business is outside the tenant.',1;
            SELECT BusinessId,CounterpartyId,AppliesWithholding,Responsibilities,JurisdictionCode,UpdatedAt
            FROM dbo.CounterpartyTaxProfiles
            WHERE BusinessId=@BusinessId AND CounterpartyId=@CounterpartyId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@CounterpartyId", counterpartyId);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            return new CounterpartyTaxProfileView(
                reader.GetGuid(0), reader.GetGuid(1),
                reader.GetBoolean(2), DeserializeResponsibilities(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetDateTimeOffset(5));
        }
        catch (SqlException exception) when (exception.Number == 51300)
        {
            throw new TaxationForbiddenException(exception.Message);
        }
    }

    public async Task<CounterpartyTaxProfileView> SaveProfileAsync(
        Guid tenantId, Guid userId, SaveCounterpartyTaxProfileRequest request,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        try
        {
            await EnsureScopeAsync(connection, transaction, tenantId, request.BusinessId, userId, ct);
            await EnsureCounterpartyAsync(connection, transaction, request.BusinessId, request.CounterpartyId, ct);
            var now = timeProvider.GetUtcNow();
            var responsibilities = request.Responsibilities
                .Select(value => value.Trim().ToUpperInvariant())
                .Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
            await using var command = new SqlCommand("""
                UPDATE dbo.CounterpartyTaxProfiles WITH(UPDLOCK,HOLDLOCK)
                SET AppliesWithholding=@AppliesWithholding,Responsibilities=@Responsibilities,JurisdictionCode=@Jurisdiction,
                    UpdatedAt=@Now,UpdatedByUserId=@UserId
                WHERE BusinessId=@BusinessId AND CounterpartyId=@CounterpartyId;
                IF @@ROWCOUNT=0
                  INSERT dbo.CounterpartyTaxProfiles
                    (BusinessId,CounterpartyId,AppliesWithholding,Responsibilities,JurisdictionCode,UpdatedAt,UpdatedByUserId)
                  VALUES(@BusinessId,@CounterpartyId,@AppliesWithholding,@Responsibilities,@Jurisdiction,@Now,@UserId);
                """, connection, transaction);
            command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
            command.Parameters.AddWithValue("@CounterpartyId", request.CounterpartyId);
            command.Parameters.AddWithValue("@AppliesWithholding", request.AppliesWithholding);
            command.Parameters.AddWithValue("@Responsibilities", JsonSerializer.Serialize(responsibilities));
            command.Parameters.AddWithValue("@Jurisdiction", (object?)request.JurisdictionCode ?? DBNull.Value);
            command.Parameters.AddWithValue("@Now", now);
            command.Parameters.AddWithValue("@UserId", userId);
            await command.ExecuteNonQueryAsync(ct);
            await EnqueueCustomerSynchronizationAsync(
                connection, transaction, request.BusinessId, ct);
            await transaction.CommitAsync(ct);
            return new CounterpartyTaxProfileView(
                request.BusinessId, request.CounterpartyId, request.AppliesWithholding, responsibilities,
                request.JurisdictionCode, now);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
    private static async Task EnsureScopeAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, Guid businessId,
        Guid userId, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
              THROW 51301,'The business is outside the tenant.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.AppUsers WHERE UserId=@UserId AND TenantId=@TenantId)
              THROW 51302,'The user is outside the tenant.',1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@UserId", userId);
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException exception) when (exception.Number is 51301 or 51302)
        { throw new TaxationForbiddenException(exception.Message); }
    }

    private static async Task EnsureCounterpartyAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        Guid counterpartyId, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            IF NOT EXISTS(
              SELECT 1 FROM dbo.Suppliers
              WHERE SupplierId=@CounterpartyId AND BusinessId=@BusinessId
              UNION ALL
              SELECT 1 FROM dbo.Customers
              WHERE CustomerId=@CounterpartyId AND BusinessId=@BusinessId)
              THROW 51303,'The counterparty is outside the business.',1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@CounterpartyId", counterpartyId);
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException exception) when (exception.Number == 51303)
        { throw new TaxationForbiddenException(exception.Message); }
    }


    private static async Task<int> GetNextVersionAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId, Guid ruleId,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT COALESCE(MAX(Version),0)+1 FROM dbo.WithholdingRules WITH(UPDLOCK,HOLDLOCK)
            WHERE RuleId=@RuleId AND BusinessId=@BusinessId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@RuleId", ruleId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    private static async Task EnqueueCustomerSynchronizationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            DECLARE @Cursor BIGINT;
            SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
            FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND Stream=N'Customers';
            INSERT dbo.PosSynchronizationOutboxMessages
              (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
            VALUES(NEWID(),@BusinessId,N'Customers',@Cursor,SYSDATETIMEOFFSET());
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task InsertAsync(
        SqlConnection connection, SqlTransaction transaction, WithholdingRule rule, Guid userId,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.WithholdingRules
              (RuleId,Version,BusinessId,Code,Name,Kind,Direction,Moment,BaseKind,ConceptCode,
               JurisdictionCode,Rate,MinimumBase,RequiredResponsibilities,EffectiveFrom,
               EffectiveTo,IsActive,CreatedAt,CreatedByUserId)
            VALUES(@RuleId,@Version,@BusinessId,@Code,@Name,@Kind,@Direction,@Moment,@BaseKind,@Concept,
               @Jurisdiction,@Rate,@Minimum,@Responsibilities,@From,@To,@Active,@Now,@UserId);
            """, connection, transaction);
        command.Parameters.AddWithValue("@RuleId", rule.RuleId);
        command.Parameters.AddWithValue("@Version", rule.Version);
        command.Parameters.AddWithValue("@BusinessId", rule.BusinessId);
        command.Parameters.AddWithValue("@Code", rule.Code);
        command.Parameters.AddWithValue("@Name", rule.Name);
        command.Parameters.AddWithValue("@Kind", rule.Kind.ToString());
        command.Parameters.AddWithValue("@Direction", rule.Direction.ToString());
        command.Parameters.AddWithValue("@Moment", rule.Moment.ToString());
        command.Parameters.AddWithValue("@BaseKind", rule.BaseKind.ToString());
        command.Parameters.AddWithValue("@Concept", (object?)rule.ConceptCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@Jurisdiction", (object?)rule.JurisdictionCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@Rate", rule.Rate);
        command.Parameters.AddWithValue("@Minimum", rule.MinimumBase);
        command.Parameters.AddWithValue("@Responsibilities", JsonSerializer.Serialize(rule.RequiredResponsibilities));
        command.Parameters.AddWithValue("@From", rule.EffectiveFrom.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@To", rule.EffectiveTo is null ? DBNull.Value :
            rule.EffectiveTo.Value.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@Active", rule.IsActive);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        command.Parameters.AddWithValue("@UserId", userId);
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        { throw new TaxationConflictException("A withholding rule with this code and version already exists."); }
    }

    private static WithholdingRule Read(SqlDataReader reader) => WithholdingRule.Create(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetString(3),
        reader.GetString(4), Enum.Parse<WithholdingKind>(reader.GetString(5)),
        Enum.Parse<WithholdingDirection>(reader.GetString(6)),
        Enum.Parse<WithholdingRecognitionMoment>(reader.GetString(7)),
        Enum.Parse<WithholdingBaseKind>(reader.GetString(8)),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetDecimal(11),
        reader.GetDecimal(12), DeserializeResponsibilities(reader.GetString(13)),
        DateOnly.FromDateTime(reader.GetDateTime(14)),
        reader.IsDBNull(15) ? null : DateOnly.FromDateTime(reader.GetDateTime(15)),
        reader.GetBoolean(16));

    private static IReadOnlyList<string> DeserializeResponsibilities(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<string[]>(json) ?? [];
}
