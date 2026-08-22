using System.Data;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed class SqlAccountingStore(
    AccountingSqlConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider,
    SqlAccountingPostingProcessor postingProcessor) : IAccountingStore
{
    public async Task<IReadOnlyList<AccountingAccountView>> ListAccountsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT AccountId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive
            FROM dbo.AccountingAccounts WHERE TenantId=@TenantId ORDER BY Code;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        var values = new List<AccountingAccountView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetBoolean(4), reader.GetBoolean(5), reader.GetBoolean(6)));
        return values;
    }

    public async Task<IReadOnlyList<AccountingCostCenterView>> ListCostCentersAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT c.CostCenterId,c.BusinessId,c.Code,c.Name,c.ParentCostCenterId,c.IsDefault,c.IsActive
            FROM dbo.AccountingCostCenters c
            JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId
            WHERE c.BusinessId=@BusinessId AND b.TenantId=@TenantId ORDER BY c.Code;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        var values = new List<AccountingCostCenterView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetBoolean(5), reader.GetBoolean(6)));
        return values;
    }

    public async Task<IReadOnlyList<AccountingPeriodView>> ListPeriodsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT PeriodId,StartsOn,EndsOn,Name,Status FROM dbo.AccountingPeriods
            WHERE TenantId=@TenantId ORDER BY StartsOn DESC;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        var values = new List<AccountingPeriodView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetGuid(0), DateOnly.FromDateTime(reader.GetDateTime(1)),
                DateOnly.FromDateTime(reader.GetDateTime(2)), reader.GetString(3), reader.GetString(4)));
        return values;
    }

    public async Task<IReadOnlyList<AccountingMappingView>> ListMappingsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo
            FROM dbo.AccountingAccountMappings
            WHERE TenantId=@TenantId AND (BusinessId IS NULL OR BusinessId=@BusinessId)
            ORDER BY Category,EffectiveFrom DESC;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        var values = new List<AccountingMappingView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetGuid(0), reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetString(3), reader.GetGuid(4),
                DateOnly.FromDateTime(reader.GetDateTime(5)),
                reader.IsDBNull(6) ? null : DateOnly.FromDateTime(reader.GetDateTime(6))));
        return values;
    }

    public async Task<IReadOnlyList<AccountingCategoryDefinition>> ListCategoryDefinitionsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT d.Category,d.DisplayName,d.AccountType,d.IsRequired,d.DisplayOrder
            FROM dbo.AccountingConfigurationProfiles p
            INNER JOIN dbo.AccountingConfigurationProfileAccounts d ON d.ProfileCode=p.ProfileCode
            WHERE p.IsDefault=1 AND p.IsActive=1
            ORDER BY d.DisplayOrder;
            """, connection);
        var values = new List<AccountingCategoryDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetBoolean(3), reader.GetInt32(4)));
        return values;
    }

    public async Task<AccountingDefaultsResult> EnsureDefaultsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using var command = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WITH(UPDLOCK,HOLDLOCK)
                  WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND IsActive=1)
                  THROW 51403,'The accounting business is outside the legal entity.',1;

                DECLARE @ProfileCode nvarchar(32)=(
                  SELECT ProfileCode FROM dbo.AccountingConfigurationProfiles WITH(UPDLOCK,HOLDLOCK)
                  WHERE IsDefault=1 AND IsActive=1);
                IF @ProfileCode IS NULL
                  THROW 51403,'No active default accounting profile is configured.',1;

                INSERT dbo.AccountingAccounts(
                  AccountId,TenantId,Code,Name,AccountType,AllowsPosting,
                  RequiresParty,IsActive,CreatedAt)
                SELECT NEWID(),@TenantId,d.AccountCode,d.AccountName,d.AccountType,d.AllowsPosting,
                       d.RequiresParty,1,@Now
                FROM dbo.AccountingConfigurationProfileAccounts d
                WHERE d.ProfileCode=@ProfileCode AND
                  NOT EXISTS(SELECT 1 FROM dbo.AccountingAccounts a WITH(UPDLOCK,HOLDLOCK)
                  WHERE a.TenantId=@TenantId AND a.Code=d.AccountCode);

                INSERT dbo.AccountingAccountMappings(
                  MappingId,TenantId,BusinessId,Category,AccountId,
                  EffectiveFrom,EffectiveTo,CreatedAt)
                SELECT NEWID(),@TenantId,NULL,d.Category,a.AccountId,
                       CONVERT(date,'20000101'),NULL,@Now
                FROM dbo.AccountingConfigurationProfileAccounts d
                INNER JOIN dbo.AccountingAccounts a
                  ON a.TenantId=@TenantId AND a.Code=d.AccountCode AND a.IsActive=1 AND a.AllowsPosting=1
                WHERE d.ProfileCode=@ProfileCode AND
                  NOT EXISTS(SELECT 1 FROM dbo.AccountingAccountMappings m WITH(UPDLOCK,HOLDLOCK)
                  WHERE m.TenantId=@TenantId AND m.BusinessId IS NULL AND m.Category=d.Category);

                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingCostCenters WITH(UPDLOCK,HOLDLOCK)
                  WHERE BusinessId=@BusinessId AND IsDefault=1 AND IsActive=1)
                  INSERT dbo.AccountingCostCenters(
                    CostCenterId,BusinessId,Code,Name,ParentCostCenterId,IsDefault,IsActive,CreatedAt)
                  VALUES(NEWID(),@BusinessId,N'GENERAL',N'Operación general',NULL,1,1,@Now);

                DECLARE @YearStart date=DATEFROMPARTS(YEAR(@Now),1,1);
                DECLARE @YearEnd date=DATEFROMPARTS(YEAR(@Now),12,31);
                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingPeriods WITH(UPDLOCK,HOLDLOCK)
                  WHERE TenantId=@TenantId AND StartsOn<=@YearEnd AND EndsOn>=@YearStart)
                  INSERT dbo.AccountingPeriods(
                    PeriodId,TenantId,Name,StartsOn,EndsOn,Status,CreatedAt)
                  VALUES(NEWID(),@TenantId,CONVERT(nvarchar(4),YEAR(@Now)),
                         @YearStart,@YearEnd,N'Open',@Now);

                DECLARE @RequiredMappingCount int=(SELECT COUNT(*)
                  FROM dbo.AccountingConfigurationProfileAccounts
                  WHERE ProfileCode=@ProfileCode AND IsRequired=1);
                SELECT
                  (SELECT COUNT(*) FROM dbo.AccountingAccounts WHERE TenantId=@TenantId AND IsActive=1),
                  (SELECT COUNT(DISTINCT Category) FROM dbo.AccountingAccountMappings
                    WHERE TenantId=@TenantId AND (BusinessId IS NULL OR BusinessId=@BusinessId)
                      AND EffectiveFrom<=CAST(@Now AS date)
                      AND (EffectiveTo IS NULL OR EffectiveTo>=CAST(@Now AS date))),
                  CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.AccountingCostCenters
                    WHERE BusinessId=@BusinessId AND IsDefault=1 AND IsActive=1) THEN 1 ELSE 0 END),
                  CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.AccountingPeriods
                    WHERE TenantId=@TenantId AND Status=N'Open'
                      AND CAST(@Now AS date) BETWEEN StartsOn AND EndsOn) THEN 1 ELSE 0 END),
                  @RequiredMappingCount;
                """, connection, transaction);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Accounting defaults did not return their status.");
            var accountCount = reader.GetInt32(0);
            var mappingCount = reader.GetInt32(1);
            var hasCenter = reader.GetBoolean(2);
            var hasPeriod = reader.GetBoolean(3);
            var requiredMappingCount = reader.GetInt32(4);
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return new(accountCount, mappingCount, hasCenter, hasPeriod,
                accountCount >= requiredMappingCount && mappingCount >= requiredMappingCount && hasCenter && hasPeriod);
        }
        catch (SqlException exception) when (IsConflict(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new AccountingConflictException(exception.Message);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<AccountingAccountView> CreateAccountAsync(
        AccountingUserIdentity user, CreateAccountingAccountRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.AccountingAccounts
            (AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,CreatedAt)
            VALUES(@AccountId,@TenantId,@Code,@Name,@AccountType,@AllowsPosting,@RequiresParty,1,@Now);
            """;
        await using var connection = connections.Create(); await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@AccountId", request.AccountId); command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@Code", request.Code); command.Parameters.AddWithValue("@Name", request.Name);
        command.Parameters.AddWithValue("@AccountType", request.AccountType); command.Parameters.AddWithValue("@AllowsPosting", request.AllowsPosting);
        command.Parameters.AddWithValue("@RequiresParty", request.RequiresParty); command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await ExecuteMutationAsync(command, cancellationToken, "An account with the same ID or code already exists.");
        return new(request.AccountId, request.Code, request.Name, request.AccountType, request.AllowsPosting, request.RequiresParty, true);
    }

    public async Task<AccountingCostCenterView> CreateCostCenterAsync(
        AccountingUserIdentity user, CreateCostCenterRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
              THROW 51400,'The cost center business is outside the legal entity.',1;
            INSERT dbo.AccountingCostCenters
            (CostCenterId,BusinessId,Code,Name,ParentCostCenterId,IsDefault,IsActive,CreatedAt)
            VALUES(@CostCenterId,@BusinessId,@Code,@Name,@ParentCostCenterId,@IsDefault,1,@Now);
            """;
        await using var connection = connections.Create(); await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@CostCenterId", request.CostCenterId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId); command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@Code", request.Code); command.Parameters.AddWithValue("@Name", request.Name); command.Parameters.AddWithValue("@ParentCostCenterId", (object?)request.ParentCostCenterId ?? DBNull.Value);
        command.Parameters.AddWithValue("@IsDefault", request.IsDefault); command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await ExecuteMutationAsync(command, cancellationToken, "The cost center conflicts with an existing code, parent or default center.");
        return new(request.CostCenterId, request.BusinessId, request.Code, request.Name, request.ParentCostCenterId, request.IsDefault, true);
    }

    public async Task<AccountingPeriodView> CreatePeriodAsync(
        AccountingUserIdentity user, CreateAccountingPeriodRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using (var overlap = new SqlCommand("""
                IF EXISTS(SELECT 1 FROM dbo.AccountingPeriods WITH(UPDLOCK,HOLDLOCK)
                  WHERE TenantId=@TenantId AND StartsOn<=@EndsOn AND EndsOn>=@StartsOn)
                  THROW 51401,'The accounting period overlaps an existing period.',1;
                """, connection, transaction))
            {
                AddPeriod(overlap, user.TenantId, request.StartsOn, request.EndsOn);
                await overlap.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var insert = new SqlCommand("""
                INSERT dbo.AccountingPeriods(PeriodId,TenantId,Name,StartsOn,EndsOn,Status,CreatedAt)
                VALUES(@PeriodId,@TenantId,@Name,@StartsOn,@EndsOn,N'Open',@Now);
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("@PeriodId", request.PeriodId); insert.Parameters.AddWithValue("@Name", request.Name); insert.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
                AddPeriod(insert, user.TenantId, request.StartsOn, request.EndsOn);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException exception) when (IsConflict(exception))
        { await transaction.RollbackAsync(CancellationToken.None); throw new AccountingConflictException(exception.Message); }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
        return new(request.PeriodId, request.StartsOn, request.EndsOn, request.Name, "Open");
    }

    public async Task SetMappingAsync(
        AccountingUserIdentity user, SetAccountMappingRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using (var validate = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingAccounts
                  WHERE AccountId=@AccountId AND TenantId=@TenantId AND IsActive=1 AND AllowsPosting=1)
                  THROW 51402,'The mapping account is not active or postable.',1;
                IF @BusinessId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Businesses
                  WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
                  THROW 51403,'The mapping business is outside the legal entity.',1;
                IF NOT EXISTS(
                  SELECT 1 FROM dbo.AccountingConfigurationProfiles p
                  INNER JOIN dbo.AccountingConfigurationProfileAccounts d ON d.ProfileCode=p.ProfileCode
                  WHERE p.IsDefault=1 AND p.IsActive=1 AND d.Category=@Category)
                  THROW 51403,'The accounting category is not configured in the active profile.',1;
                IF EXISTS(SELECT 1 FROM dbo.AccountingAccountMappings WITH(UPDLOCK,HOLDLOCK)
                  WHERE TenantId=@TenantId AND Category=@Category
                    AND ((BusinessId=@BusinessId) OR (BusinessId IS NULL AND @BusinessId IS NULL))
                    AND EffectiveFrom<>@EffectiveFrom
                    AND EffectiveFrom<=COALESCE(@EffectiveTo,'9999-12-31')
                    AND COALESCE(EffectiveTo,'9999-12-31')>=@EffectiveFrom)
                  THROW 51404,'The accounting mapping overlaps another validity range.',1;
                """, connection, transaction))
            { AddMapping(validate, request); await validate.ExecuteNonQueryAsync(cancellationToken); }
            await using (var upsert = new SqlCommand("""
                DELETE dbo.AccountingAccountMappings
                WHERE TenantId=@TenantId AND Category=@Category AND EffectiveFrom=@EffectiveFrom
                  AND ((BusinessId=@BusinessId) OR (BusinessId IS NULL AND @BusinessId IS NULL));
                INSERT dbo.AccountingAccountMappings
                (MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo,CreatedAt)
                VALUES(@MappingId,@TenantId,@BusinessId,@Category,@AccountId,@EffectiveFrom,@EffectiveTo,@Now);
                """, connection, transaction))
            {
                AddMapping(upsert, request); upsert.Parameters.AddWithValue("@MappingId", ids.NewId()); upsert.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
                await upsert.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException exception) when (IsConflict(exception))
        { await transaction.RollbackAsync(CancellationToken.None); throw new AccountingConflictException(exception.Message); }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task ClosePeriodAsync(
        AccountingUserIdentity user, Guid periodId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            DateTime startsOn; DateTime endsOn; string status;
            await using (var read = new SqlCommand("""
                SELECT StartsOn,EndsOn,Status FROM dbo.AccountingPeriods WITH(UPDLOCK,HOLDLOCK)
                WHERE PeriodId=@PeriodId AND TenantId=@TenantId;
                """, connection, transaction))
            {
                read.Parameters.AddWithValue("@PeriodId", periodId); read.Parameters.AddWithValue("@TenantId", user.TenantId);
                await using var reader = await read.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)) throw new AccountingConflictException("The accounting period does not exist.");
                startsOn = reader.GetDateTime(0); endsOn = reader.GetDateTime(1); status = reader.GetString(2);
            }
            if (status == "Closed") { await transaction.CommitAsync(cancellationToken); return; }
            await using (var pending = new SqlCommand("""
                SELECT COUNT(*) FROM dbo.AccountingPostingJobs
                WHERE TenantId=@TenantId AND CAST(OccurredAt AS date) BETWEEN @StartsOn AND @EndsOn
                  AND Status<>N'Posted';
                """, connection, transaction))
            {
                pending.Parameters.AddWithValue("@TenantId", user.TenantId); pending.Parameters.AddWithValue("@StartsOn", startsOn); pending.Parameters.AddWithValue("@EndsOn", endsOn);
                if (Convert.ToInt32(await pending.ExecuteScalarAsync(cancellationToken)) > 0)
                    throw new AccountingConflictException("The period has documents pending accounting configuration or posting.");
            }
            await using (var close = new SqlCommand("""
                UPDATE dbo.AccountingPeriods SET Status=N'Closed',ClosedAt=@Now,ClosedByUserId=@UserId
                WHERE PeriodId=@PeriodId AND TenantId=@TenantId AND Status=N'Open';
                """, connection, transaction))
            {
                close.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow()); close.Parameters.AddWithValue("@UserId", user.UserId); close.Parameters.AddWithValue("@PeriodId", periodId); close.Parameters.AddWithValue("@TenantId", user.TenantId);
                if (await close.ExecuteNonQueryAsync(cancellationToken) != 1) throw new DBConcurrencyException("The accounting period could not be closed.");
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<AccountingPostingView?> RetryPostingAsync(
        AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken)
    {
        var source = await FindPostingSourceAsync(user, documentId, cancellationToken);
        if (source is null) return null;
        await postingProcessor.ProcessAsync(documentId, source, user.BusinessId, cancellationToken);
        return await FindPostingAsync(user, documentId, cancellationToken);
    }

    public async Task<AccountingEntryView?> GetEntryAsync(
        AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(cancellationToken);
        Guid entryId; string number; string type; DateTimeOffset occurred; DateTimeOffset posted; decimal debit; decimal credit;
        await using (var command = new SqlCommand("""
            SELECT EntryId,EntryNumber,SourceDocumentType,OccurredAt,PostedAt,DebitTotal,CreditTotal
            FROM dbo.AccountingEntries WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND SourceDocumentId=@DocumentId;
            """, connection))
        {
            AddScope(command, user, documentId); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            entryId = reader.GetGuid(0); number = reader.GetString(1); type = reader.GetString(2); occurred = reader.GetDateTimeOffset(3); posted = reader.GetDateTimeOffset(4); debit = reader.GetDecimal(5); credit = reader.GetDecimal(6);
        }
        var lines = new List<AccountingEntryLineView>();
        await using (var command = new SqlCommand("""
            SELECT l.LineNumber,a.Code,a.Name,l.Debit,l.Credit,l.PartyId,l.CostCenterId,l.Description
            FROM dbo.AccountingEntryLines l INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
            WHERE l.EntryId=@EntryId ORDER BY l.LineNumber;
            """, connection))
        {
            command.Parameters.AddWithValue("@EntryId", entryId); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) lines.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3), reader.GetDecimal(4), reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.IsDBNull(6) ? null : reader.GetGuid(6), reader.GetString(7)));
        }
        return new(entryId, number, documentId, type, occurred, posted, debit, credit, lines);
    }

    public async Task<IReadOnlyList<TrialBalanceRow>> GetTrialBalanceAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT a.Code,a.Name,SUM(l.Debit),SUM(l.Credit),SUM(l.Debit-l.Credit)
            FROM dbo.AccountingEntries e
            INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
            INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
            WHERE e.TenantId=@TenantId AND e.BusinessId=@BusinessId
              AND CAST(e.OccurredAt AS date) BETWEEN @From AND @To
            GROUP BY a.Code,a.Name ORDER BY a.Code;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@From", from.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue("@To", to.ToDateTime(TimeOnly.MinValue));
        var rows = new List<TrialBalanceRow>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4)));
        return rows;
    }

    public async Task<IReadOnlyList<AccountMovementRow>> GetAccountMovementsAsync(
        AccountingUserIdentity user, string accountCode, DateOnly from, DateOnly to,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT e.EntryId,e.EntryNumber,e.SourceDocumentId,e.SourceDocumentType,e.OccurredAt,
                   l.Description,l.Debit,l.Credit,
                   SUM(l.Debit-l.Credit) OVER(
                     ORDER BY e.OccurredAt,e.EntryNumber,l.LineNumber
                     ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)
            FROM dbo.AccountingEntries e
            INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
            INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
            WHERE e.TenantId=@TenantId AND e.BusinessId=@BusinessId AND a.Code=@AccountCode
              AND CAST(e.OccurredAt AS date) BETWEEN @From AND @To
            ORDER BY e.OccurredAt,e.EntryNumber,l.LineNumber;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@AccountCode", accountCode);
        command.Parameters.AddWithValue("@From", from.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@To", to.ToDateTime(TimeOnly.MinValue));
        var rows = new List<AccountMovementRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(
            reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3),
            reader.GetDateTimeOffset(4), reader.GetString(5), reader.GetDecimal(6),
            reader.GetDecimal(7), reader.GetDecimal(8)));
        return rows;
    }

    private async Task<string?> FindPostingSourceAsync(AccountingUserIdentity user, Guid documentId, CancellationToken token)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(token); await using var command = new SqlCommand("SELECT SourceDocumentType FROM dbo.AccountingPostingJobs WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND SourceDocumentId=@DocumentId", connection); AddScope(command, user, documentId); return await command.ExecuteScalarAsync(token) as string;
    }
    private async Task<AccountingPostingView?> FindPostingAsync(AccountingUserIdentity user, Guid documentId, CancellationToken token)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(token); await using var command = new SqlCommand("""
            SELECT p.SourceDocumentType,p.Status,p.LastErrorCode,p.LastErrorMessage,e.EntryId
            FROM dbo.AccountingPostingJobs p LEFT JOIN dbo.AccountingEntries e ON e.SourceDocumentId=p.SourceDocumentId AND e.SourceDocumentType=p.SourceDocumentType
            WHERE p.TenantId=@TenantId AND p.BusinessId=@BusinessId AND p.SourceDocumentId=@DocumentId;
            """, connection); AddScope(command, user, documentId); await using var reader = await command.ExecuteReaderAsync(token); if (!await reader.ReadAsync(token)) return null; return new(documentId, reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetGuid(4));
    }
    private static async Task ExecuteMutationAsync(SqlCommand command, CancellationToken token, string conflict) { try { await command.ExecuteNonQueryAsync(token); } catch (SqlException exception) when (IsConflict(exception)) { throw new AccountingConflictException(conflict); } }
    private static bool IsConflict(SqlException exception) => exception.Number is 2601 or 2627 or 547 or 51400 or 51401 or 51402 or 51403 or 51404;
    private static void AddPeriod(SqlCommand command, Guid tenantId, DateOnly starts, DateOnly ends) { command.Parameters.AddWithValue("@TenantId", tenantId); command.Parameters.AddWithValue("@StartsOn", starts.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue("@EndsOn", ends.ToDateTime(TimeOnly.MinValue)); }
    private static void AddMapping(SqlCommand command, SetAccountMappingRequest value) { command.Parameters.AddWithValue("@TenantId", value.TenantId); command.Parameters.AddWithValue("@BusinessId", (object?)value.BusinessId ?? DBNull.Value); command.Parameters.AddWithValue("@Category", value.Category); command.Parameters.AddWithValue("@AccountId", value.AccountId); command.Parameters.AddWithValue("@EffectiveFrom", value.EffectiveFrom.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue("@EffectiveTo", value.EffectiveTo is null ? DBNull.Value : value.EffectiveTo.Value.ToDateTime(TimeOnly.MinValue)); }
    private static void AddScope(SqlCommand command, AccountingUserIdentity user, Guid documentId) { command.Parameters.AddWithValue("@TenantId", user.TenantId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId); command.Parameters.AddWithValue("@DocumentId", documentId); }
}
