using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public Task<AccountingManualDocumentAcceptance> ConfirmAccountAdjustmentAsync(
        AccountingUserIdentity user, ConfirmAccountAdjustmentRequest request,
        CancellationToken cancellationToken) => AcceptManualDocumentAsync(
            user, request.AdjustmentId, AccountingManualDocumentTypes.AccountAdjustment,
            request.OccurredAt, request, async (connection, transaction, token) =>
            {
                _ = await ValidatePostingAccountAsync(connection, transaction, user.TenantId,
                    request.CounterpartAccountId, token);
                await ValidateCostCenterAsync(connection, transaction, user.BusinessId,
                    request.CostCenterId, token);
                var table = request.SubledgerKind == AccountingSubledgerKinds.Receivable
                    ? "Receivables" : "Payables";
                var idColumn = request.SubledgerKind == AccountingSubledgerKinds.Receivable
                    ? "ReceivableId" : "PayableId";
                await using var target = new SqlCommand($"""
                    SELECT OutstandingAmount,Status FROM dbo.[{table}] WITH(UPDLOCK,HOLDLOCK)
                    WHERE [{idColumn}]=@Id AND BusinessId=@BusinessId;
                    """, connection, transaction);
                target.Parameters.AddWithValue("@Id", request.SubledgerId);
                target.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                await using var reader = await target.ExecuteReaderAsync(token);
                if (!await reader.ReadAsync(token))
                    throw new AccountingValidationException(
                        "The account adjustment references an unknown subledger balance.");
                var outstanding = reader.GetDecimal(0);
                var status = reader.GetString(1);
                if (status is "Cancelled" ||
                    request.Direction == AccountingAdjustmentDirections.Decrease &&
                    request.Amount > outstanding)
                    throw new AccountingConflictException(
                        "The adjustment would make the subledger balance negative.");
            }, cancellationToken);

    public Task<AccountingManualDocumentAcceptance> ConfirmManualVoucherAsync(
        AccountingUserIdentity user, ConfirmManualAccountingVoucherRequest request,
        CancellationToken cancellationToken) => AcceptManualDocumentAsync(
            user, request.VoucherId, AccountingManualDocumentTypes.ManualVoucher,
            request.OccurredAt, request, async (connection, transaction, token) =>
            {
                foreach (var line in request.Lines)
                {
                    var requiresParty = await ValidatePostingAccountAsync(connection, transaction, user.TenantId,
                        line.AccountId, token);
                    if (requiresParty && line.PartyId is null)
                        throw new AccountingValidationException(
                            "A manual voucher line requires a party for its account.");
                    await ValidateCostCenterAsync(connection, transaction, user.BusinessId,
                        line.CostCenterId, token);
                    if (line.PartyId is Guid partyId)
                    {
                        await using var party = new SqlCommand("""
                            SELECT COUNT_BIG(1) FROM dbo.Parties
                            WHERE PartyId=@PartyId AND TenantId=@TenantId;
                            """, connection, transaction);
                        party.Parameters.AddWithValue("@PartyId", partyId);
                        party.Parameters.AddWithValue("@TenantId", user.TenantId);
                        if (Convert.ToInt64(await party.ExecuteScalarAsync(token)) != 1)
                            throw new AccountingValidationException(
                                "A manual voucher line references an unknown party.");
                    }
                }
            }, cancellationToken);

    private async Task<AccountingManualDocumentAcceptance> AcceptManualDocumentAsync<T>(
        AccountingUserIdentity user, Guid documentId, string documentType,
        DateTimeOffset occurredAt, T payload,
        Func<SqlConnection, SqlTransaction, CancellationToken, Task> validate,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        var now = timeProvider.GetUtcNow();
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using (var replay = new SqlCommand("""
                SELECT s.PayloadHash,a.Status
                FROM dbo.AccountingSourceDocuments s WITH(UPDLOCK,HOLDLOCK)
                INNER JOIN dbo.AccountingPostingJobs a
                  ON a.SourceDocumentId=s.SourceDocumentId
                 AND a.SourceDocumentType=s.SourceDocumentType
                 AND a.BusinessId=s.BusinessId
                WHERE s.SourceDocumentId=@DocumentId
                  AND s.SourceDocumentType=@DocumentType
                  AND s.BusinessId=@BusinessId;
                """, connection, transaction))
            {
                replay.Parameters.AddWithValue("@DocumentId", documentId);
                replay.Parameters.AddWithValue("@DocumentType", documentType);
                replay.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                await using var replayReader = await replay.ExecuteReaderAsync(cancellationToken);
                if (await replayReader.ReadAsync(cancellationToken))
                {
                    var existingHash = (byte[])replayReader[0];
                    var existingStatus = replayReader.GetString(1);
                    if (!existingHash.AsSpan().SequenceEqual(hash))
                        throw new AccountingConflictException(
                            "The manual document ID was reused with different content.");
                    await replayReader.DisposeAsync();
                    await transaction.CommitAsync(cancellationToken);
                    return new(documentId, documentType, existingStatus, true);
                }
            }

            await using (var readiness = new SqlCommand("""
                SELECT COUNT_BIG(1) FROM dbo.AccountingTenantSettings WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantId=@TenantId AND Status=N'Ready'
                  AND EffectiveFrom<=CONVERT(date,@OccurredAt);
                """, connection, transaction))
            {
                readiness.Parameters.AddWithValue("@TenantId", user.TenantId);
                readiness.Parameters.AddWithValue("@OccurredAt", occurredAt);
                if (Convert.ToInt64(await readiness.ExecuteScalarAsync(cancellationToken)) != 1)
                    throw new AccountingConflictException(
                        "Accounting must be active for the manual document date.");
            }

            await validate(connection, transaction, cancellationToken);
            await using var insert = new SqlCommand("""
                INSERT dbo.AccountingSourceDocuments
                  (SourceDocumentId,SourceDocumentType,TenantId,BusinessId,
                   PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
                VALUES(@DocumentId,@DocumentType,@TenantId,@BusinessId,
                   @Payload,@Hash,@OccurredAt,@Now,1);
                INSERT dbo.AccountingPostingJobs
                  (AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
                   SourceDocumentType,SourcePayloadHash,OccurredAt,Status,AttemptCount,CreatedAt)
                VALUES(@AccountingJobId,@TenantId,@BusinessId,@DocumentId,
                   @DocumentType,@Hash,@OccurredAt,N'Pending',0,@Now);
                """, connection, transaction);
            insert.Parameters.AddWithValue("@AccountingJobId", ids.NewId());
            insert.Parameters.AddWithValue("@TenantId", user.TenantId);
            insert.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            insert.Parameters.AddWithValue("@DocumentId", documentId);
            insert.Parameters.AddWithValue("@DocumentType", documentType);
            insert.Parameters.AddWithValue("@Payload", json);
            insert.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = hash;
            insert.Parameters.AddWithValue("@OccurredAt", occurredAt);
            insert.Parameters.AddWithValue("@Now", now);
            if (await insert.ExecuteNonQueryAsync(cancellationToken) != 2)
                throw new DBConcurrencyException(
                    "The manual accounting document was not accepted atomically.");
            await transaction.CommitAsync(cancellationToken);
            return new(documentId, documentType, AccountingPostingStatuses.Pending, false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<bool> ValidatePostingAccountAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId,
        Guid accountId, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            SELECT RequiresParty FROM dbo.AccountingAccounts
            WHERE AccountId=@AccountId AND TenantId=@TenantId
              AND IsActive=1 AND AllowsPosting=1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@AccountId", accountId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        var value = await command.ExecuteScalarAsync(token);
        if (value is null)
            throw new AccountingValidationException(
                "A manual document references an invalid posting account.");
        return Convert.ToBoolean(value);
    }

    private static async Task ValidateCostCenterAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        Guid? costCenterId, CancellationToken token)
    {
        if (costCenterId is null) return;
        await using var command = new SqlCommand("""
            SELECT COUNT_BIG(1) FROM dbo.AccountingCostCenters
            WHERE CostCenterId=@CostCenterId AND BusinessId=@BusinessId AND IsActive=1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@CostCenterId", costCenterId.Value);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        if (Convert.ToInt64(await command.ExecuteScalarAsync(token)) != 1)
            throw new AccountingValidationException(
                "A manual document references an invalid cost center.");
    }

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
            SELECT c.CostCenterId,c.BusinessId,c.Code,c.Name,c.ParentCostCenterId,c.IsDefault,c.IsActive,c.RowVersion
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
                reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetBoolean(5), reader.GetBoolean(6),
                Convert.ToBase64String((byte[])reader[7])));
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

                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingTenantSettings WITH(UPDLOCK,HOLDLOCK)
                  WHERE TenantId=@TenantId)
                  INSERT dbo.AccountingTenantSettings
                    (TenantId,Status,FunctionalCurrencyCode,UpdatedAt)
                  VALUES(@TenantId,N'Configuring',N'COP',@Now);

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
            IF @ParentCostCenterId IS NOT NULL AND NOT EXISTS(
              SELECT 1 FROM dbo.AccountingCostCenters
              WHERE CostCenterId=@ParentCostCenterId AND BusinessId=@BusinessId AND IsActive=1)
              THROW 51400,'The parent must be active in this business.',1;
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
        return (await ListCostCentersAsync(user, cancellationToken)).Single(row => row.CostCenterId == request.CostCenterId);
    }

    public async Task<AccountingCostCenterView> SetCostCenterStatusAsync(
        AccountingUserIdentity user, Guid costCenterId, SetAccountingCostCenterStatusRequest request,
        CancellationToken cancellationToken)
    {
        var current = (await ListCostCentersAsync(user, cancellationToken))
            .SingleOrDefault(row => row.CostCenterId == costCenterId)
            ?? throw new AccountingValidationException("El centro no pertenece a esta sede.");
        return await UpdateCostCenterAsync(user, costCenterId, new(
            current.Code, current.Name, current.ParentCostCenterId, current.IsDefault,
            request.IsActive, request.RowVersion ?? ""), cancellationToken);
    }

    public async Task<AccountingCostCenterView> UpdateCostCenterAsync(
        AccountingUserIdentity user, Guid costCenterId, UpdateCostCenterRequest request,
        CancellationToken cancellationToken)
    {
        var version = RequiredCostCenterVersion(request.RowVersion);
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using var command = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
                  THROW 51411,'El centro no pertenece a esta empresa.',1;
                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingCostCenters WITH(UPDLOCK,HOLDLOCK)
                  WHERE CostCenterId=@CostCenterId AND BusinessId=@BusinessId AND RowVersion=@Version)
                  THROW 51415,'El centro cambió. Actualiza la lista antes de guardar.',1;
                IF @ParentCostCenterId IS NOT NULL AND NOT EXISTS(
                  SELECT 1 FROM dbo.AccountingCostCenters WITH(UPDLOCK,HOLDLOCK)
                  WHERE CostCenterId=@ParentCostCenterId AND BusinessId=@BusinessId AND IsActive=1)
                  THROW 51411,'El centro superior debe estar activo en esta sede.',1;
                DECLARE @Ancestor uniqueidentifier=@ParentCostCenterId;
                DECLARE @Visited TABLE(Id uniqueidentifier PRIMARY KEY);
                WHILE @Ancestor IS NOT NULL
                BEGIN
                  IF @Ancestor=@CostCenterId OR EXISTS(SELECT 1 FROM @Visited WHERE Id=@Ancestor)
                    THROW 51411,'El centro superior genera un ciclo.',1;
                  INSERT @Visited VALUES(@Ancestor);
                  SELECT @Ancestor=ParentCostCenterId FROM dbo.AccountingCostCenters
                    WHERE CostCenterId=@Ancestor AND BusinessId=@BusinessId;
                END;
                IF @IsDefault=0 AND EXISTS(SELECT 1 FROM dbo.AccountingCostCenters
                  WHERE CostCenterId=@CostCenterId AND BusinessId=@BusinessId AND IsDefault=1)
                  THROW 51412,'Selecciona otro centro como predeterminado antes de sustituir este.',1;
                IF @IsActive=0
                BEGIN
                  IF @IsDefault=1
                    THROW 51412,'El centro predeterminado debe permanecer activo.',1;
                  DECLARE @Dependencies nvarchar(2048);
                  SELECT @Dependencies=STRING_AGG(CONVERT(nvarchar(max),Label),N'; ')
                  FROM (
                    SELECT N'Centro hijo: '+Code+N' · '+Name Label FROM dbo.AccountingCostCenters
                    WHERE BusinessId=@BusinessId AND ParentCostCenterId=@CostCenterId AND IsActive=1
                    UNION ALL
                    SELECT N'Regla: '+OperationKind+COALESCE(N' / '+CONVERT(nvarchar(36),WarehouseId),N' / general')
                    FROM dbo.AccountingCostCenterAssignments
                    WHERE BusinessId=@BusinessId AND CostCenterId=@CostCenterId AND IsActive=1
                    UNION ALL
                    SELECT N'Concepto de gasto: '+Code+N' · '+Name FROM dbo.ExpenseConcepts
                    WHERE BusinessId=@BusinessId AND DefaultCostCenterId=@CostCenterId AND IsActive=1
                    UNION ALL
                    SELECT N'Motivo: '+Code+N' · '+Name FROM dbo.BusinessReasons
                    WHERE BusinessId=@BusinessId AND DefaultCostCenterId=@CostCenterId AND IsActive=1
                  ) dependencies;
                  IF @Dependencies IS NOT NULL
                    THROW 51413,@Dependencies,1;
                END;
                IF @IsDefault=1
                  UPDATE dbo.AccountingCostCenters SET IsDefault=0
                  WHERE BusinessId=@BusinessId AND IsDefault=1 AND CostCenterId<>@CostCenterId;
                UPDATE dbo.AccountingCostCenters SET Code=@Code,Name=@Name,
                  ParentCostCenterId=@ParentCostCenterId,IsDefault=@IsDefault,IsActive=@IsActive
                WHERE CostCenterId=@CostCenterId AND BusinessId=@BusinessId;
                SELECT CostCenterId,BusinessId,Code,Name,ParentCostCenterId,IsDefault,IsActive,RowVersion
                FROM dbo.AccountingCostCenters WHERE CostCenterId=@CostCenterId AND BusinessId=@BusinessId;
                """, connection, transaction);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@CostCenterId", costCenterId);
            command.Parameters.AddWithValue("@Version", version);
            command.Parameters.AddWithValue("@Code", request.Code);
            command.Parameters.AddWithValue("@Name", request.Name);
            command.Parameters.AddWithValue("@ParentCostCenterId", (object?)request.ParentCostCenterId ?? DBNull.Value);
            command.Parameters.AddWithValue("@IsDefault", request.IsDefault);
            command.Parameters.AddWithValue("@IsActive", request.IsActive);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var result = new AccountingCostCenterView(reader.GetGuid(0), reader.GetGuid(1),
                reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetBoolean(5), reader.GetBoolean(6), Convert.ToBase64String((byte[])reader[7]));
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqlException error) when (error.Number == 51415 || error.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new AccountingConflictException(error.Number == 51415 ? error.Message : "El código del centro ya existe en esta sede.");
        }
        catch (SqlException error) when (error.Number is 51411 or 51412 or 51413)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new AccountingValidationException(error.Message);
        }
    }

    private static byte[] RequiredCostCenterVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new AccountingConflictException("Actualiza la lista para obtener la versión vigente.");
        try
        {
            var version = Convert.FromBase64String(value);
            if (version.Length == 8) return version;
        }
        catch (FormatException)
        {
            throw new AccountingValidationException("La versión del registro no es válida.");
        }
        throw new AccountingValidationException("La versión del registro no es válida.");
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
                if (!await reader.ReadAsync(cancellationToken)) throw new AccountingConflictException("El periodo contable no existe.");
                startsOn = reader.GetDateTime(0); endsOn = reader.GetDateTime(1); status = reader.GetString(2);
            }
            if (status == "Closed") { await transaction.CommitAsync(cancellationToken); return; }
            await using (var pending = new SqlCommand("""
                SELECT
                  (SELECT COUNT(*) FROM dbo.AccountingPostingJobs
                   WHERE TenantId=@TenantId AND CAST(OccurredAt AS date) BETWEEN @StartsOn AND @EndsOn
                     AND Status NOT IN(N'Posted',N'CommercialEffectsApplied'))
                  +
                  (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments source
                   WHERE source.TenantId=@TenantId
                     AND CAST(source.OccurredAt AS date) BETWEEN @StartsOn AND @EndsOn
                     AND NOT EXISTS(
                       SELECT 1 FROM dbo.AccountingPostingJobs job
                       WHERE job.SourceDocumentId=source.SourceDocumentId
                         AND job.SourceDocumentType=source.SourceDocumentType
                         AND job.BusinessId=source.BusinessId
                         AND job.TenantId=source.TenantId));
                """, connection, transaction))
            {
                pending.Parameters.AddWithValue("@TenantId", user.TenantId); pending.Parameters.AddWithValue("@StartsOn", startsOn); pending.Parameters.AddWithValue("@EndsOn", endsOn);
                if (Convert.ToInt32(await pending.ExecuteScalarAsync(cancellationToken)) > 0)
                    throw new AccountingConflictException("El periodo tiene documentos pendientes de configuración o contabilización.");
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
        var source = await FindPostingSourceAsync(user, documentId, cancellationToken) ??
            await RecoverMissingPostingJobAsync(user, documentId, cancellationToken);
        if (source is null) return null;
        await postingProcessor.ProcessAsync(documentId, source, user.BusinessId, cancellationToken);
        return await FindPostingAsync(user, documentId, cancellationToken);
    }

    private async Task<string?> RecoverMissingPostingJobAsync(
        AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using var command = new SqlCommand("""
                DECLARE @DocumentType nvarchar(64),@Hash binary(32),
                        @OccurredAt datetimeoffset(7),@AccountingEntryRequired bit;
                SELECT @DocumentType=source.SourceDocumentType,@Hash=source.PayloadHash,
                       @OccurredAt=source.OccurredAt,
                       @AccountingEntryRequired=source.AccountingEntryRequired
                FROM dbo.AccountingSourceDocuments source WITH(UPDLOCK,HOLDLOCK)
                WHERE source.SourceDocumentId=@DocumentId
                  AND source.BusinessId=@BusinessId AND source.TenantId=@TenantId
                  AND source.SourceDocumentType IN(SELECT value FROM OPENJSON(@AccountableTypes));
                IF @DocumentType IS NULL SELECT CAST(NULL AS nvarchar(64));
                ELSE IF @AccountingEntryRequired IS NULL
                  THROW 51405,N'The legacy accounting source has no provable frozen mode.',1;
                ELSE BEGIN
                  IF NOT EXISTS(SELECT 1 FROM dbo.AccountingPostingJobs WITH(UPDLOCK,HOLDLOCK)
                    WHERE SourceDocumentId=@DocumentId AND SourceDocumentType=@DocumentType)
                    INSERT dbo.AccountingPostingJobs
                      (AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
                       SourceDocumentType,SourcePayloadHash,OccurredAt,AccountingEntryRequired,
                       Status,AttemptCount,CreatedAt)
                    VALUES(@JobId,@TenantId,@BusinessId,@DocumentId,@DocumentType,@Hash,
                           @OccurredAt,@AccountingEntryRequired,N'Pending',0,@Now);
                  SELECT @DocumentType;
                END;
                """, connection, transaction);
            AddScope(command, user, documentId);
            AddAccountableTypes(command);
            command.Parameters.AddWithValue("@JobId", ids.NewId());
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            var source = await command.ExecuteScalarAsync(cancellationToken) as string;
            await transaction.CommitAsync(cancellationToken);
            return source;
        }
        catch (SqlException error) when (error.Number == 51405)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new AccountingConflictException(error.Message);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public Task<AccountingPostingView?> GetPostingAsync(
        AccountingUserIdentity user, Guid documentId,
        CancellationToken cancellationToken) =>
        FindPostingAsync(user, documentId, cancellationToken);

    public async Task<AccountingEntryView?> GetEntryAsync(
        AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(cancellationToken);
        Guid entryId; string number; string type; string description; DateTimeOffset occurred; DateTimeOffset posted; decimal debit; decimal credit;
        await using (var command = new SqlCommand("""
            SELECT EntryId,EntryNumber,SourceDocumentType,OccurredAt,PostedAt,Description,DebitTotal,CreditTotal
            FROM dbo.AccountingEntries WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND SourceDocumentId=@DocumentId;
            """, connection))
        {
            AddScope(command, user, documentId); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            entryId = reader.GetGuid(0); number = reader.GetString(1); type = reader.GetString(2); occurred = reader.GetDateTimeOffset(3); posted = reader.GetDateTimeOffset(4); description = reader.GetString(5); debit = reader.GetDecimal(6); credit = reader.GetDecimal(7);
        }
        var lines = new List<AccountingEntryLineView>();
        await using (var command = new SqlCommand("""
            SELECT l.LineNumber,a.Code,a.Name,l.Debit,l.Credit,l.PartyId,
                   l.PartyIdentificationSnapshot,l.PartyNameSnapshot,l.CostCenterId,
                   l.CostCenterCodeSnapshot,l.CostCenterNameSnapshot,l.Description
            FROM dbo.AccountingEntryLines l
            INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
            WHERE l.EntryId=@EntryId ORDER BY l.LineNumber;
            """, connection))
        {
            command.Parameters.AddWithValue("@EntryId", entryId); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) lines.Add(new(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetDecimal(3), reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetString(11)));
        }
        return new(entryId, number, documentId, type, occurred, posted, description, debit, credit, lines);
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
        CancellationToken cancellationToken, Guid? costCenterId = null)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            WITH AccountLines AS (
              SELECT e.EntryId,e.EntryNumber,e.SourceDocumentId,e.SourceDocumentType,e.OccurredAt,
                     l.LineNumber,l.Description,l.Debit,l.Credit,l.CostCenterId,
                     l.CostCenterCodeSnapshot,l.CostCenterNameSnapshot,
                     SUM(l.Debit-l.Credit) OVER(
                       ORDER BY e.OccurredAt,e.EntryNumber,l.LineNumber
                       ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) Balance
              FROM dbo.AccountingEntries e
              INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
              INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
              WHERE e.TenantId=@TenantId AND e.BusinessId=@BusinessId AND a.Code=@AccountCode
                AND (@CostCenterId IS NULL OR l.CostCenterId=@CostCenterId)
                AND CAST(e.OccurredAt AS date)<=@To
            )
            SELECT EntryId,EntryNumber,SourceDocumentId,SourceDocumentType,OccurredAt,
                   Description,Debit,Credit,Balance,LineNumber,CostCenterId,CostCenterCodeSnapshot,CostCenterNameSnapshot
            FROM AccountLines
            WHERE CAST(OccurredAt AS date)>=@From
            ORDER BY OccurredAt,EntryNumber,LineNumber;
            """, connection);
        command.Parameters.AddWithValue("@CostCenterId", (object?)costCenterId ?? DBNull.Value);
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
            reader.GetDecimal(7), reader.GetDecimal(8), reader.GetInt32(9),
            reader.IsDBNull(10) ? null : reader.GetGuid(10),
            reader.IsDBNull(11) ? null : reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetString(12)));
        return rows;
    }

    public async Task<IReadOnlyList<AccountingJournalRow>> GetJournalAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT e.EntryId,e.EntryNumber,e.OccurredAt,e.SourceDocumentId,
                   e.SourceDocumentType,l.LineNumber,a.Code,a.Name,l.PartyId,
                   l.CostCenterId,l.CostCenterCodeSnapshot,l.CostCenterNameSnapshot,
                   l.Description,l.Debit,l.Credit
            FROM dbo.AccountingEntries e
            INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
            INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
            WHERE e.TenantId=@TenantId AND e.BusinessId=@BusinessId
              AND CAST(e.OccurredAt AS date) BETWEEN @From AND @To
            ORDER BY e.OccurredAt,e.EntryNumber,l.LineNumber;
            """, connection);
        AddReportScope(command, user, from, to);
        var rows = new List<AccountingJournalRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetDateTimeOffset(2),
                reader.GetGuid(3), reader.GetString(4), reader.GetInt32(5), reader.GetString(6),
                reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11), reader.GetString(12),
                reader.GetDecimal(13), reader.GetDecimal(14)));
        return rows;
    }

    public async Task<IReadOnlyList<GeneralLedgerRow>> GetGeneralLedgerAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT a.Code,a.Name,a.AccountType,
              COALESCE(SUM(CASE WHEN CAST(e.OccurredAt AS date)<@From THEN l.Debit-l.Credit ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN CAST(e.OccurredAt AS date) BETWEEN @From AND @To THEN l.Debit ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN CAST(e.OccurredAt AS date) BETWEEN @From AND @To THEN l.Credit ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN CAST(e.OccurredAt AS date)<=@To THEN l.Debit-l.Credit ELSE 0 END),0)
            FROM dbo.AccountingAccounts a
            LEFT JOIN dbo.AccountingEntryLines l ON l.AccountId=a.AccountId
            LEFT JOIN dbo.AccountingEntries e ON e.EntryId=l.EntryId AND e.BusinessId=@BusinessId
            WHERE a.TenantId=@TenantId AND a.AllowsPosting=1
            GROUP BY a.Code,a.Name,a.AccountType
            HAVING COALESCE(SUM(CASE WHEN e.BusinessId=@BusinessId AND CAST(e.OccurredAt AS date)<=@To
                               THEN ABS(l.Debit)+ABS(l.Credit) ELSE 0 END),0)>0
            ORDER BY a.Code;
            """, connection);
        AddReportScope(command, user, from, to);
        var rows = new List<GeneralLedgerRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5),
                reader.GetDecimal(6)));
        return rows;
    }

    public async Task<IReadOnlyList<FinancialStatementRow>> GetBalanceSheetAsync(
        AccountingUserIdentity user, DateOnly asOf, CancellationToken cancellationToken)
    {
        var start = new DateOnly(1900, 1, 1);
        var rows = (await GetFinancialStatementAsync(user, start, asOf,
            ["Asset", "Liability", "Equity"], cancellationToken)).ToList();
        var income = await GetFinancialStatementAsync(user, start, asOf,
            ["Revenue", "ContraRevenue", "Expense"], cancellationToken);
        var netIncome = income.Sum(row => row.Section == "Revenue"
            ? row.Amount : -row.Amount);
        if (netIncome != 0)
            rows.Add(new("Equity", "RESULTADO", "Resultado acumulado del ejercicio",
                decimal.Round(netIncome, 4)));
        return rows;
    }

    public Task<IReadOnlyList<FinancialStatementRow>> GetIncomeStatementAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to,
        CancellationToken cancellationToken) => GetFinancialStatementAsync(
            user, from, to, ["Revenue", "ContraRevenue", "Expense"], cancellationToken);

    private async Task<IReadOnlyList<FinancialStatementRow>> GetFinancialStatementAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to,
        IReadOnlyList<string> accountTypes, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT a.AccountType,a.Code,a.Name,
              SUM(CASE WHEN a.AccountType IN(N'Liability',N'Equity',N'Revenue')
                       THEN l.Credit-l.Debit ELSE l.Debit-l.Credit END)
            FROM dbo.AccountingEntries e
            INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
            INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
            WHERE e.TenantId=@TenantId AND e.BusinessId=@BusinessId
              AND CAST(e.OccurredAt AS date) BETWEEN @From AND @To
              AND a.AccountType IN (SELECT value FROM STRING_SPLIT(@Types,N','))
            GROUP BY a.AccountType,a.Code,a.Name
            HAVING SUM(ABS(l.Debit)+ABS(l.Credit))>0
            ORDER BY CASE a.AccountType WHEN N'Asset' THEN 1 WHEN N'Liability' THEN 2
                     WHEN N'Equity' THEN 3 WHEN N'Revenue' THEN 4
                     WHEN N'ContraRevenue' THEN 5 ELSE 6 END,a.Code;
            """, connection);
        AddReportScope(command, user, from, to);
        command.Parameters.AddWithValue("@Types", string.Join(',', accountTypes));
        var rows = new List<FinancialStatementRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetDecimal(3)));
        return rows;
    }

    public async Task<IReadOnlyList<AccountingExceptionRow>> GetExceptionsAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT exception.SourceDocumentId,exception.SourceDocumentType,
                   exception.OccurredAt,exception.Status,
                   exception.LastErrorCode,exception.LastErrorMessage
            FROM (
              SELECT SourceDocumentId,SourceDocumentType,OccurredAt,Status,
                     LastErrorCode,LastErrorMessage
              FROM dbo.AccountingPostingJobs
              WHERE TenantId=@TenantId AND BusinessId=@BusinessId
                AND Status NOT IN(N'Posted',N'CommercialEffectsApplied')
              UNION ALL
              SELECT source.SourceDocumentId,source.SourceDocumentType,source.OccurredAt,
                     N'MissingAccountingJob',N'MissingAccountingJob',
                     N'The immutable accounting source has no durable posting job.'
              FROM dbo.AccountingSourceDocuments source
              WHERE source.TenantId=@TenantId AND source.BusinessId=@BusinessId
                AND NOT EXISTS(
                  SELECT 1 FROM dbo.AccountingPostingJobs job
                  WHERE job.SourceDocumentId=source.SourceDocumentId
                    AND job.SourceDocumentType=source.SourceDocumentType
                    AND job.BusinessId=source.BusinessId
                    AND job.TenantId=source.TenantId)
            ) exception
            WHERE CAST(exception.OccurredAt AS date) BETWEEN @From AND @To
            ORDER BY OccurredAt,SourceDocumentType,SourceDocumentId;
            """, connection);
        AddReportScope(command, user, from, to);
        var rows = new List<AccountingExceptionRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetDateTimeOffset(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        return rows;
    }

    public async Task<AccountingDocumentPage> ListDocumentsAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to, string? documentType,
        string? status, string? search, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            WITH posting AS (
              SELECT SourceDocumentId,SourceDocumentType,OccurredAt,Status,AttemptCount,
                     LastErrorCode,LastErrorMessage,TenantId,BusinessId
              FROM dbo.AccountingPostingJobs
              UNION ALL
              SELECT source.SourceDocumentId,source.SourceDocumentType,source.OccurredAt,
                     N'MissingAccountingJob',0,N'MissingAccountingJob',
                     N'The immutable accounting source has no durable posting job.',
                     source.TenantId,source.BusinessId
              FROM dbo.AccountingSourceDocuments source
              WHERE NOT EXISTS(
                  SELECT 1 FROM dbo.AccountingPostingJobs job
                  WHERE job.SourceDocumentId=source.SourceDocumentId
                    AND job.SourceDocumentType=source.SourceDocumentType
                    AND job.BusinessId=source.BusinessId
                    AND job.TenantId=source.TenantId)
            )
            SELECT job.SourceDocumentId,job.SourceDocumentType,sourceNumber.DocumentNumber,
                   job.OccurredAt,job.Status,job.AttemptCount,job.LastErrorCode,
                   job.LastErrorMessage,entry.EntryId,entry.EntryNumber,
                   entry.DebitTotal,entry.CreditTotal,entry.PostedAt,
                   fiscal.FiscalDocumentType,fiscal.FiscalNumber,fiscal.UniqueCodeType,
                   fiscal.UniqueCode,fiscal.FiscalStatus,COUNT(*) OVER()
            FROM posting job
            LEFT JOIN dbo.AccountingEntries entry
              ON entry.SourceDocumentId=job.SourceDocumentId
             AND entry.SourceDocumentType=job.SourceDocumentType
            LEFT JOIN dbo.FiscalDocuments fiscal
              ON fiscal.DocumentId=job.SourceDocumentId
             AND fiscal.BusinessId=job.BusinessId
            OUTER APPLY(SELECT TOP(1) candidate.DocumentNumber FROM (
              SELECT sale.DocumentNumber FROM dbo.SalesDocuments sale
               WHERE sale.DocumentId=job.SourceDocumentId
              UNION ALL SELECT saleReturn.DocumentNumber FROM dbo.SalesReturns saleReturn
               WHERE saleReturn.ReturnId=job.SourceDocumentId
              UNION ALL SELECT debitNote.DocumentNumber FROM dbo.SalesDebitNotes debitNote
               WHERE debitNote.DebitNoteId=job.SourceDocumentId
              UNION ALL SELECT receipt.DocumentNumber FROM dbo.GoodsReceipts receipt
               WHERE receipt.GoodsReceiptId=job.SourceDocumentId
              UNION ALL SELECT purchaseReturn.DocumentNumber FROM dbo.PurchaseReturns purchaseReturn
               WHERE purchaseReturn.PurchaseReturnId=job.SourceDocumentId
              UNION ALL SELECT expense.DocumentNumber FROM dbo.Expenses expense
               WHERE expense.ExpenseId=job.SourceDocumentId
              UNION ALL SELECT supplierPayment.DocumentNumber FROM dbo.SupplierPayments supplierPayment
               WHERE supplierPayment.PaymentId=job.SourceDocumentId
              UNION ALL SELECT customerPayment.DocumentNumber FROM dbo.CustomerPayments customerPayment
               WHERE customerPayment.PaymentId=job.SourceDocumentId
              UNION ALL SELECT cashMovement.DocumentNumber FROM dbo.CashMovementDocuments cashMovement
               WHERE cashMovement.DocumentId=job.SourceDocumentId
              UNION ALL SELECT operation.DocumentNumber FROM dbo.InventoryOperations operation
               WHERE operation.InventoryOperationId=job.SourceDocumentId
              UNION ALL SELECT cost.DocumentNumber FROM purchasing.GoodsReceiptCostDocuments cost
               WHERE cost.CostDocumentId=job.SourceDocumentId
            ) candidate) sourceNumber
            WHERE job.TenantId=@TenantId AND job.BusinessId=@BusinessId
              AND CAST(job.OccurredAt AS date) BETWEEN @From AND @To
              AND (@DocumentType IS NULL OR job.SourceDocumentType=@DocumentType)
              AND (@Status IS NULL OR job.Status=@Status)
              AND (@Search IS NULL OR sourceNumber.DocumentNumber LIKE N'%'+@Search+N'%'
                   OR entry.EntryNumber LIKE N'%'+@Search+N'%'
                   OR CONVERT(nvarchar(36),job.SourceDocumentId) LIKE N'%'+@Search+N'%')
            ORDER BY job.OccurredAt DESC,job.SourceDocumentId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@From", from.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@To", to.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@DocumentType", (object?)documentType ?? DBNull.Value);
        command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@PageSize", pageSize);
        var rows = new List<AccountingDocumentRow>();
        var total = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            total = reader.GetInt32(18);
            rows.Add(new(reader.GetGuid(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetDateTimeOffset(3),
                reader.GetString(4), reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                reader.IsDBNull(12) ? null : reader.GetDateTimeOffset(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetString(17)));
        }
        return new(rows, page, pageSize, total);
    }

    private static void AddReportScope(
        SqlCommand command, AccountingUserIdentity user, DateOnly from, DateOnly to)
    {
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@From", from.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@To", to.ToDateTime(TimeOnly.MinValue));
    }

    private static void AddAccountableTypes(SqlCommand command) =>
        command.Parameters.AddWithValue("@AccountableTypes",
            JsonSerializer.Serialize(AccountingProcessingPolicy.DocumentTypes));

    public async Task<AccountingOpeningBalanceView?> GetOpeningBalanceAsync(
        AccountingUserIdentity user, DateOnly effectiveOn,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        return await ReadOpeningBalanceAsync(connection, null, user, effectiveOn, cancellationToken);
    }

    public async Task<AccountingOpeningBalanceView> SaveOpeningBalanceAsync(
        AccountingUserIdentity user, SaveAccountingOpeningBalanceRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await EnsureOpeningBalancesEditableAsync(connection, transaction, user, cancellationToken);
            foreach (var line in request.Lines)
            {
                var requiresParty = await ValidatePostingAccountAsync(connection, transaction,
                    user.TenantId, line.AccountId, cancellationToken);
                if (requiresParty && line.PartyId is null)
                    throw new AccountingValidationException("La cuenta seleccionada exige un tercero.");
                await ValidateCostCenterAsync(connection, transaction, user.BusinessId,
                    line.CostCenterId, cancellationToken);
                if (line.PartyId is Guid partyId)
                {
                    await using var party = new SqlCommand("""
                        SELECT COUNT_BIG(1)
                        FROM dbo.Parties p
                        WHERE p.PartyId=@PartyId AND p.TenantId=@TenantId AND p.IsActive=1
                          AND (EXISTS(SELECT 1 FROM dbo.Customers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId)
                               OR EXISTS(SELECT 1 FROM dbo.Suppliers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId)
                               OR EXISTS(SELECT 1 FROM dbo.CommerceSellers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId)
                               OR EXISTS(SELECT 1 FROM dbo.Carriers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId)
                               OR EXISTS(SELECT 1 FROM dbo.Employees e WHERE e.PartyId=p.PartyId AND e.BusinessId=@BusinessId)
                               OR EXISTS(SELECT 1 FROM dbo.AppUsers u WHERE u.PartyId=p.PartyId AND u.TenantId=@TenantId));
                        """, connection, transaction);
                    party.Parameters.AddWithValue("@PartyId", partyId);
                    party.Parameters.AddWithValue("@TenantId", user.TenantId);
                    party.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                    if (Convert.ToInt64(await party.ExecuteScalarAsync(cancellationToken)) != 1)
                        throw new AccountingValidationException("El saldo inicial referencia un tercero inexistente, inactivo o ajeno al negocio.");
                }
            }

            byte[]? expectedVersion = null;
            if (!string.IsNullOrWhiteSpace(request.RowVersion))
            {
                try { expectedVersion = Convert.FromBase64String(request.RowVersion); }
                catch (FormatException) { throw new AccountingValidationException("La versión del saldo inicial no es válida."); }
            }
            var now = timeProvider.GetUtcNow();
            await using (var command = new SqlCommand("""
                DECLARE @ExistingId uniqueidentifier=(SELECT BatchId
                  FROM dbo.AccountingOpeningBalanceBatches WITH(UPDLOCK,HOLDLOCK)
                  WHERE BusinessId=@BusinessId AND EffectiveOn=@EffectiveOn);
                IF @ExistingId IS NOT NULL AND @ExistingId<>@BatchId THROW 51405,N'An opening balance already exists for this business and date.',1;

                IF EXISTS(SELECT 1 FROM dbo.AccountingOpeningBalanceBatches WHERE BatchId=@BatchId)
                BEGIN
                  UPDATE dbo.AccountingOpeningBalanceBatches
                  SET CurrencyCode=@Currency,Description=@Description,
                      UpdatedByUserId=@UserId,UpdatedAt=@Now
                  WHERE BatchId=@BatchId AND TenantId=@TenantId AND BusinessId=@BusinessId
                    AND Status=N'Draft' AND (@ExpectedVersion IS NULL OR RowVersion=@ExpectedVersion);
                  IF @@ROWCOUNT<>1 THROW 51406,N'The opening balance changed or is no longer editable.',1;
                  DELETE dbo.AccountingOpeningBalanceLines WHERE BatchId=@BatchId;
                END
                ELSE
                  INSERT dbo.AccountingOpeningBalanceBatches
                    (BatchId,TenantId,BusinessId,EffectiveOn,CurrencyCode,Description,Status,
                     CreatedByUserId,CreatedAt,UpdatedByUserId,UpdatedAt)
                  VALUES(@BatchId,@TenantId,@BusinessId,@EffectiveOn,@Currency,@Description,N'Draft',
                         @UserId,@Now,@UserId,@Now);
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("@BatchId", request.BatchId);
                command.Parameters.AddWithValue("@TenantId", user.TenantId);
                command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                command.Parameters.AddWithValue("@EffectiveOn", request.EffectiveOn.ToDateTime(TimeOnly.MinValue));
                command.Parameters.AddWithValue("@Currency", request.CurrencyCode);
                command.Parameters.AddWithValue("@Description", request.Description.Trim());
                command.Parameters.AddWithValue("@UserId", user.UserId);
                command.Parameters.AddWithValue("@Now", now);
                command.Parameters.Add("@ExpectedVersion", SqlDbType.Timestamp).Value = (object?)expectedVersion ?? DBNull.Value;
                try { await command.ExecuteNonQueryAsync(cancellationToken); }
                catch (SqlException exception) when (exception.Number is 51405 or 51406)
                { throw new AccountingConflictException(exception.Message); }
            }
            for (var index = 0; index < request.Lines.Count; index++)
            {
                var line = request.Lines[index];
                await using var insert = new SqlCommand("""
                    INSERT dbo.AccountingOpeningBalanceLines
                      (BatchId,LineNumber,AccountId,PartyId,CostCenterId,Description,Debit,Credit)
                    VALUES(@BatchId,@LineNumber,@AccountId,@PartyId,@CostCenterId,@Description,@Debit,@Credit);
                    """, connection, transaction);
                insert.Parameters.AddWithValue("@BatchId", request.BatchId);
                insert.Parameters.AddWithValue("@LineNumber", index + 1);
                insert.Parameters.AddWithValue("@AccountId", line.AccountId);
                insert.Parameters.AddWithValue("@PartyId", (object?)line.PartyId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@CostCenterId", (object?)line.CostCenterId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@Description", line.Description.Trim());
                AddMoney(insert, "@Debit", line.Debit);
                AddMoney(insert, "@Credit", line.Credit);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            var result = await ReadOpeningBalanceAsync(connection, transaction, user,
                request.EffectiveOn, cancellationToken) ?? throw new DBConcurrencyException("The opening balance was not persisted.");
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<AccountingOpeningBalanceView> ApproveOpeningBalanceAsync(
        AccountingUserIdentity user, Guid batchId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await EnsureOpeningBalancesEditableAsync(connection, transaction, user, cancellationToken);
            DateOnly effectiveOn;
            await using (var command = new SqlCommand("""
                SELECT EffectiveOn FROM dbo.AccountingOpeningBalanceBatches WITH(UPDLOCK,HOLDLOCK)
                WHERE BatchId=@BatchId AND TenantId=@TenantId AND BusinessId=@BusinessId AND Status=N'Draft';
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("@BatchId", batchId);
                command.Parameters.AddWithValue("@TenantId", user.TenantId);
                command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                var value = await command.ExecuteScalarAsync(cancellationToken);
                if (value is not DateTime date)
                    throw new AccountingConflictException("El saldo inicial no existe o ya no está en borrador.");
                effectiveOn = DateOnly.FromDateTime(date);
            }
            await using (var validate = new SqlCommand("""
                SELECT COUNT(*),COALESCE(SUM(Debit),0),COALESCE(SUM(Credit),0),
                  SUM(CASE WHEN a.AccountId IS NULL OR a.IsActive=0 OR a.AllowsPosting=0
                            OR (a.RequiresParty=1 AND l.PartyId IS NULL)
                            OR (l.PartyId IS NOT NULL AND (p.PartyId IS NULL OR partyScope.IsInBusiness=0))
                            OR (l.CostCenterId IS NOT NULL AND c.CostCenterId IS NULL)
                           THEN 1 ELSE 0 END)
                FROM dbo.AccountingOpeningBalanceLines l
                LEFT JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId AND a.TenantId=@TenantId
                LEFT JOIN dbo.Parties p ON p.PartyId=l.PartyId AND p.TenantId=@TenantId AND p.IsActive=1
                LEFT JOIN dbo.AccountingCostCenters c ON c.CostCenterId=l.CostCenterId AND c.BusinessId=@BusinessId AND c.IsActive=1
                CROSS APPLY(SELECT CONVERT(bit,CASE WHEN l.PartyId IS NULL
                    OR EXISTS(SELECT 1 FROM dbo.Customers customer WHERE customer.PartyId=l.PartyId AND customer.BusinessId=@BusinessId)
                    OR EXISTS(SELECT 1 FROM dbo.Suppliers supplier WHERE supplier.PartyId=l.PartyId AND supplier.BusinessId=@BusinessId)
                    OR EXISTS(SELECT 1 FROM dbo.CommerceSellers seller WHERE seller.PartyId=l.PartyId AND seller.BusinessId=@BusinessId)
                    OR EXISTS(SELECT 1 FROM dbo.Carriers carrier WHERE carrier.PartyId=l.PartyId AND carrier.BusinessId=@BusinessId)
                    OR EXISTS(SELECT 1 FROM dbo.Employees employee WHERE employee.PartyId=l.PartyId AND employee.BusinessId=@BusinessId)
                    OR EXISTS(SELECT 1 FROM dbo.AppUsers appUser WHERE appUser.PartyId=l.PartyId AND appUser.TenantId=@TenantId)
                    THEN 1 ELSE 0 END) IsInBusiness) partyScope
                WHERE l.BatchId=@BatchId;
                """, connection, transaction))
            {
                validate.Parameters.AddWithValue("@BatchId", batchId);
                validate.Parameters.AddWithValue("@TenantId", user.TenantId);
                validate.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                await using var reader = await validate.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);
                var count = reader.GetInt32(0);
                var debit = reader.GetDecimal(1);
                var credit = reader.GetDecimal(2);
                var invalid = reader.GetInt32(3);
                if (count < 2) throw new AccountingValidationException("El saldo inicial requiere al menos dos líneas.");
                if (invalid > 0) throw new AccountingValidationException("Hay líneas con cuentas, terceros o centros de costo inválidos.");
                if (debit <= 0 || decimal.Round(debit, 4) != decimal.Round(credit, 4))
                    throw new AccountingValidationException("El saldo inicial debe estar cuadrado: débitos y créditos deben ser iguales.");
            }
            await using (var approve = new SqlCommand("""
                UPDATE dbo.AccountingOpeningBalanceBatches
                SET Status=N'Approved',ApprovedByUserId=@UserId,ApprovedAt=@Now,
                    UpdatedByUserId=@UserId,UpdatedAt=@Now
                WHERE BatchId=@BatchId AND Status=N'Draft';
                """, connection, transaction))
            {
                approve.Parameters.AddWithValue("@BatchId", batchId);
                approve.Parameters.AddWithValue("@UserId", user.UserId);
                approve.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
                if (await approve.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new DBConcurrencyException("The opening balance was not approved.");
            }
            var result = await ReadOpeningBalanceAsync(connection, transaction, user,
                effectiveOn, cancellationToken) ?? throw new DBConcurrencyException("The approved opening balance could not be read.");
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task EnsureOpeningBalancesEditableAsync(
        SqlConnection connection, SqlTransaction transaction, AccountingUserIdentity user,
        CancellationToken cancellationToken)
    {
        if (!await CanEditOpeningBalancesAsync(
                connection, transaction, user.TenantId, true, cancellationToken))
            throw new AccountingConflictException(
                "Los saldos iniciales no se pueden modificar porque la activación está en curso o ya existen movimientos contables.");
    }

    private static async Task<bool> CanEditOpeningBalancesAsync(
        SqlConnection connection, SqlTransaction? transaction, Guid tenantId,
        bool lockSettings, CancellationToken cancellationToken)
    {
        var lockHint = lockSettings ? " WITH(UPDLOCK,HOLDLOCK)" : string.Empty;
        await using var command = new SqlCommand($"""
            DECLARE @Status nvarchar(16),@OpeningBalanceMode nvarchar(24),
                    @ActivatedAt datetimeoffset(7);
            SELECT @Status=Status,@OpeningBalanceMode=OpeningBalanceMode,@ActivatedAt=ActivatedAt
            FROM dbo.AccountingTenantSettings{lockHint} WHERE TenantId=@TenantId;
            SELECT CONVERT(bit,CASE
              WHEN @Status IS NULL OR @Status=N'Disabled' THEN 1
              WHEN @Status=N'Configuring' AND NOT EXISTS(
                SELECT 1 FROM dbo.AccountingTenantSettings
                WHERE TenantId=@TenantId AND EffectiveFrom IS NOT NULL) THEN 1
              WHEN @Status=N'Ready' AND @OpeningBalanceMode=N'ZeroDeclared'
               AND NOT EXISTS(SELECT 1 FROM dbo.AccountingSourceDocuments
                              WHERE TenantId=@TenantId AND AcceptedAt>=@ActivatedAt)
               AND NOT EXISTS(SELECT 1 FROM dbo.AccountingEntries
                              WHERE TenantId=@TenantId AND PostedAt>=@ActivatedAt) THEN 1
              ELSE 0 END);
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<AccountingOpeningBalanceView?> ReadOpeningBalanceAsync(
        SqlConnection connection, SqlTransaction? transaction, AccountingUserIdentity user,
        DateOnly effectiveOn, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT BatchId,BusinessId,EffectiveOn,CurrencyCode,Description,Status,
                   RowVersion,UpdatedAt,ApprovedAt,PostedAt
            FROM dbo.AccountingOpeningBalanceBatches
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND EffectiveOn=@EffectiveOn;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@EffectiveOn", effectiveOn.ToDateTime(TimeOnly.MinValue));
        Guid batchId;
        Guid businessId;
        string currency;
        string description;
        string status;
        string version;
        DateTimeOffset updatedAt;
        DateTimeOffset? approvedAt;
        DateTimeOffset? postedAt;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            batchId = reader.GetGuid(0); businessId = reader.GetGuid(1);
            currency = reader.GetString(3); description = reader.GetString(4); status = reader.GetString(5);
            version = Convert.ToBase64String((byte[])reader[6]);
            updatedAt = reader.GetDateTimeOffset(7);
            approvedAt = reader.IsDBNull(8) ? null : reader.GetDateTimeOffset(8);
            postedAt = reader.IsDBNull(9) ? null : reader.GetDateTimeOffset(9);
        }
        await using var linesCommand = new SqlCommand("""
            SELECT LineNumber,AccountId,PartyId,CostCenterId,Description,Debit,Credit
            FROM dbo.AccountingOpeningBalanceLines WHERE BatchId=@BatchId ORDER BY LineNumber;
            """, connection, transaction);
        linesCommand.Parameters.AddWithValue("@BatchId", batchId);
        var lines = new List<AccountingOpeningBalanceLineView>();
        await using var linesReader = await linesCommand.ExecuteReaderAsync(cancellationToken);
        while (await linesReader.ReadAsync(cancellationToken))
            lines.Add(new(linesReader.GetInt32(0), linesReader.GetGuid(1),
                linesReader.IsDBNull(2) ? null : linesReader.GetGuid(2),
                linesReader.IsDBNull(3) ? null : linesReader.GetGuid(3), linesReader.GetString(4),
                linesReader.GetDecimal(5), linesReader.GetDecimal(6)));
        return new(batchId, businessId, effectiveOn, currency, description, status,
            lines.Sum(line => line.Debit), lines.Sum(line => line.Credit), version,
            updatedAt, approvedAt, postedAt, lines);
    }

    public async Task<AccountingReadinessView> GetReadinessAsync(
        AccountingUserIdentity user, DateOnly? effectiveFrom, string? openingBalanceMode,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var issues = await ReadActivationIssuesAsync(
            connection, null, user,
            effectiveFrom ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().Date),
            openingBalanceMode,
            cancellationToken);
        await using var command = new SqlCommand("""
            SELECT Status,FunctionalCurrencyCode,EffectiveFrom,OpeningBalanceMode,ActivatedAt
            FROM dbo.AccountingTenantSettings WHERE TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new(AccountingActivationStatuses.Disabled, "COP", null, null, null, issues, true);
        var status = reader.GetString(0);
        var currency = reader.GetString(1);
        var storedEffectiveFrom = reader.IsDBNull(2) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(2));
        var storedOpeningMode = reader.IsDBNull(3) ? null : reader.GetString(3);
        var activatedAt = reader.IsDBNull(4) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(4);
        await reader.DisposeAsync();
        var canEditOpeningBalances = await CanEditOpeningBalancesAsync(
            connection, null, user.TenantId, false, cancellationToken);
        return new(status, currency,
            storedEffectiveFrom, storedOpeningMode, activatedAt, issues, canEditOpeningBalances);
    }

    public async Task<AccountingReadinessView> ActivateAsync(
        AccountingUserIdentity user, ActivateAccountingRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var issues = await ReadActivationIssuesAsync(
                connection, transaction, user, request.EffectiveFrom,
                request.OpeningBalanceMode, cancellationToken);
            if (issues.Count > 0)
                throw new AccountingConflictException(
                    $"Accounting is not ready: {string.Join("; ", issues)}");
            var wasReady = false;
            var allowReadyReconfiguration = false;
            await using (var current = new SqlCommand("""
                SELECT Status FROM dbo.AccountingTenantSettings WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantId=@TenantId;
                """, connection, transaction))
            {
                current.Parameters.AddWithValue("@TenantId", user.TenantId);
                wasReady = string.Equals(
                    await current.ExecuteScalarAsync(cancellationToken) as string,
                    AccountingActivationStatuses.Ready,
                    StringComparison.Ordinal);
            }
            if (wasReady)
            {
                allowReadyReconfiguration = request.OpeningBalanceMode == "ImportedAndApproved"
                    && await CanEditOpeningBalancesAsync(
                        connection, transaction, user.TenantId, true, cancellationToken);
                if (request.OpeningBalanceMode == "ImportedAndApproved" && !allowReadyReconfiguration)
                    throw new AccountingConflictException(
                        "No se puede reemplazar el inicio en cero porque ya existen movimientos contables.");
            }
            var now = timeProvider.GetUtcNow();
            await using var command = new SqlCommand("""
                MERGE dbo.AccountingTenantSettings WITH(HOLDLOCK) AS target
                USING(SELECT @TenantId TenantId) source ON target.TenantId=source.TenantId
                WHEN MATCHED AND (target.Status<>N'Ready' OR @AllowReadyReconfiguration=1) THEN UPDATE SET
                  Status=@ActivationStatus,FunctionalCurrencyCode=@Currency,EffectiveFrom=@EffectiveFrom,
                  OpeningBalanceMode=@OpeningMode,ActivationRequestedAt=@Now,
                  ActivationRequestedByUserId=@UserId,
                  ActivatedAt=CASE WHEN @ActivationStatus=N'Ready' THEN @Now ELSE NULL END,
                  ActivatedByUserId=CASE WHEN @ActivationStatus=N'Ready' THEN @UserId ELSE NULL END,
                  UpdatedAt=@Now
                WHEN NOT MATCHED THEN INSERT
                  (TenantId,Status,FunctionalCurrencyCode,EffectiveFrom,OpeningBalanceMode,
                   ActivationRequestedAt,ActivationRequestedByUserId,ActivatedAt,ActivatedByUserId,UpdatedAt)
                VALUES(@TenantId,@ActivationStatus,@Currency,@EffectiveFrom,@OpeningMode,@Now,@UserId,
                   CASE WHEN @ActivationStatus=N'Ready' THEN @Now ELSE NULL END,
                   CASE WHEN @ActivationStatus=N'Ready' THEN @UserId ELSE NULL END,@Now);

                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingVoucherCursors WITH(UPDLOCK,HOLDLOCK)
                              WHERE TenantId=@TenantId)
                  INSERT dbo.AccountingVoucherCursors(TenantId,LastAssignedNumber,UpdatedAt)
                  VALUES(@TenantId,0,@Now);
                """, connection, transaction);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@Currency", request.FunctionalCurrencyCode);
            command.Parameters.AddWithValue("@EffectiveFrom", request.EffectiveFrom.ToDateTime(TimeOnly.MinValue));
            command.Parameters.AddWithValue("@OpeningMode", request.OpeningBalanceMode);
            var activationStatus = request.OpeningBalanceMode == "ImportedAndApproved"
                ? AccountingActivationStatuses.Configuring : AccountingActivationStatuses.Ready;
            command.Parameters.AddWithValue("@ActivationStatus", activationStatus);
            command.Parameters.AddWithValue("@AllowReadyReconfiguration", allowReadyReconfiguration);
            command.Parameters.AddWithValue("@UserId", user.UserId);
            command.Parameters.AddWithValue("@Now", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
            if (request.OpeningBalanceMode == "ImportedAndApproved")
                await QueueOpeningBalancesAsync(connection, transaction, user,
                    request.EffectiveFrom, now, cancellationToken);
            else if (!wasReady)
                await EnqueueBankAccountSynchronizationAsync(
                    connection, transaction, user.TenantId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(activationStatus,
                request.FunctionalCurrencyCode, request.EffectiveFrom,
                request.OpeningBalanceMode, now, [], activationStatus != AccountingActivationStatuses.Configuring);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<IReadOnlyList<string>> ReadActivationIssuesAsync(
        SqlConnection connection, SqlTransaction? transaction,
        AccountingUserIdentity user, DateOnly effectiveFrom,
        string? openingBalanceMode,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            DECLARE @Profile nvarchar(32)=(SELECT TOP(1) ProfileCode
              FROM dbo.AccountingConfigurationProfiles WHERE IsDefault=1 AND IsActive=1);
            SELECT
              (SELECT COUNT(*) FROM dbo.AccountingConfigurationProfileAccounts d
               WHERE d.ProfileCode=@Profile AND d.IsRequired=1 AND NOT EXISTS
               (SELECT 1 FROM dbo.AccountingAccountMappings m
                INNER JOIN dbo.AccountingAccounts a ON a.AccountId=m.AccountId
                WHERE m.TenantId=@TenantId AND (m.BusinessId IS NULL OR m.BusinessId=@BusinessId)
                  AND m.Category=d.Category AND a.IsActive=1 AND a.AllowsPosting=1
                  AND m.EffectiveFrom<=@EffectiveFrom
                  AND (m.EffectiveTo IS NULL OR m.EffectiveTo>=@EffectiveFrom))),
              CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.AccountingPeriods
                WHERE TenantId=@TenantId AND Status=N'Open'
                  AND @EffectiveFrom BETWEEN StartsOn AND EndsOn) THEN 1 ELSE 0 END),
              (SELECT COUNT(*) FROM dbo.Businesses b
               WHERE b.TenantId=@TenantId AND b.IsActive=1 AND NOT EXISTS
                 (SELECT 1 FROM dbo.AccountingCostCenters c
                  WHERE c.BusinessId=b.BusinessId AND c.IsDefault=1 AND c.IsActive=1)),
              (SELECT COUNT(*) FROM dbo.Businesses b
               WHERE @OpeningMode=N'ImportedAndApproved'
                 AND b.TenantId=@TenantId AND b.IsActive=1 AND NOT EXISTS
                 (SELECT 1 FROM dbo.AccountingOpeningBalanceBatches o
                  WHERE o.TenantId=@TenantId AND o.BusinessId=b.BusinessId
                    AND o.EffectiveOn=@EffectiveFrom
                    AND o.Status IN (N'Approved',N'Posted')));
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@EffectiveFrom", effectiveFrom.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@OpeningMode", (object?)openingBalanceMode ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var issues = new List<string>();
        if (reader.GetInt32(0) > 0) issues.Add("Faltan cuentas automáticas obligatorias asociadas a cuentas PUC activas.");
        if (!reader.GetBoolean(1)) issues.Add("No existe un periodo abierto que incluya la fecha efectiva.");
        if (reader.GetInt32(2) > 0) issues.Add("Hay un negocio activo sin centro de costo predeterminado.");
        if (reader.GetInt32(3) > 0) issues.Add("Hay un negocio activo sin saldos iniciales aprobados para la fecha efectiva.");
        return issues;
    }

    private async Task QueueOpeningBalancesAsync(
        SqlConnection connection, SqlTransaction transaction, AccountingUserIdentity user,
        DateOnly effectiveOn, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var batches = new List<OpeningBatchPosting>();
        await using (var command = new SqlCommand("""
            SELECT b.BatchId,b.BusinessId,b.Description,l.LineNumber,l.AccountId,
                   l.PartyId,l.CostCenterId,l.Description,l.Debit,l.Credit
            FROM dbo.AccountingOpeningBalanceBatches b WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Businesses business ON business.BusinessId=b.BusinessId AND business.IsActive=1
            INNER JOIN dbo.AccountingOpeningBalanceLines l ON l.BatchId=b.BatchId
            WHERE b.TenantId=@TenantId AND b.EffectiveOn=@EffectiveOn AND b.Status=N'Approved'
            ORDER BY b.BusinessId,l.LineNumber;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@EffectiveOn", effectiveOn.ToDateTime(TimeOnly.MinValue));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var batchId = reader.GetGuid(0);
                var batch = batches.LastOrDefault(value => value.BatchId == batchId);
                if (batch is null)
                {
                    batch = new(batchId, reader.GetGuid(1), reader.GetString(2), []);
                    batches.Add(batch);
                }
                batch.Lines.Add(new(reader.GetInt32(3), reader.GetGuid(4),
                    reader.IsDBNull(5) ? null : reader.GetGuid(5),
                    reader.IsDBNull(6) ? null : reader.GetGuid(6), reader.GetString(7),
                    reader.GetDecimal(8), reader.GetDecimal(9)));
            }
        }

        foreach (var batch in batches)
        {
            var occurredAt = new DateTimeOffset(effectiveOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var payload = JsonSerializer.Serialize(new ConfirmManualAccountingVoucherRequest(
                batch.BatchId, batch.BusinessId, occurredAt, "OPENING_BALANCE",
                batch.Description, batch.Lines.Select(line => new ManualVoucherLineRequest(
                    line.AccountId, line.PartyId, line.CostCenterId, line.Description,
                    line.Debit, line.Credit)).ToArray()));
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            var jobId = ids.NewId();
            await using (var insert = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingSourceDocuments WITH(UPDLOCK,HOLDLOCK)
                              WHERE SourceDocumentId=@BatchId AND SourceDocumentType=N'AccountingOpeningBalance')
                BEGIN
                  INSERT dbo.AccountingSourceDocuments
                  (SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
                  VALUES(@BatchId,N'AccountingOpeningBalance',@TenantId,@BusinessId,@Payload,@Hash,@OccurredAt,@Now,1);
                  INSERT dbo.AccountingPostingJobs
                  (AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,
                   SourcePayloadHash,OccurredAt,Status,AttemptCount,CreatedAt)
                VALUES(@JobId,@TenantId,@BusinessId,@BatchId,N'AccountingOpeningBalance',
                       @Hash,@OccurredAt,N'Pending',0,@Now);
                END
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("@BatchId", batch.BatchId);
                insert.Parameters.AddWithValue("@TenantId", user.TenantId);
                insert.Parameters.AddWithValue("@BusinessId", batch.BusinessId);
                insert.Parameters.AddWithValue("@Payload", payload);
                insert.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = hash;
                insert.Parameters.AddWithValue("@OccurredAt", occurredAt);
                insert.Parameters.AddWithValue("@Now", now);
                insert.Parameters.AddWithValue("@JobId", jobId);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    public async Task<IReadOnlyList<AccountingOpeningBalancePosting>> ListPendingOpeningPostingsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT SourceDocumentId,BusinessId FROM dbo.AccountingPostingJobs
            WHERE TenantId=@TenantId AND SourceDocumentType=N'AccountingOpeningBalance'
              AND Status<>N'Posted' ORDER BY OccurredAt,BusinessId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        var values = new List<AccountingOpeningBalancePosting>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) values.Add(new(reader.GetGuid(0),reader.GetGuid(1)));
        return values;
    }

    public async Task<IReadOnlyList<AccountingCostCenterAssignmentView>> ListCostCenterAssignmentsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT assignment.AssignmentId,assignment.BusinessId,assignment.CostCenterId,
                   center.Code,center.Name,assignment.OperationKind,assignment.WarehouseId,
                   warehouse.Code,warehouse.Name,assignment.IsActive,assignment.RowVersion
            FROM dbo.AccountingCostCenterAssignments assignment
            INNER JOIN dbo.AccountingCostCenters center
              ON center.BusinessId=assignment.BusinessId AND center.CostCenterId=assignment.CostCenterId
            LEFT JOIN dbo.Warehouses warehouse
              ON warehouse.BusinessId=assignment.BusinessId AND warehouse.WarehouseId=assignment.WarehouseId
            WHERE assignment.TenantId=@TenantId AND assignment.BusinessId=@BusinessId
            ORDER BY assignment.OperationKind,warehouse.Code,center.Code;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        var values = new List<AccountingCostCenterAssignmentView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetBoolean(9), Convert.ToBase64String((byte[])reader[10])));
        return values;
    }

    public async Task<AccountingCostCenterAssignmentView> SaveCostCenterAssignmentAsync(
        AccountingUserIdentity user, SaveAccountingCostCenterAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using var command = new SqlCommand("""
                IF @IsActive=1 AND NOT EXISTS(SELECT 1 FROM dbo.AccountingCostCenters WITH(UPDLOCK,HOLDLOCK)
                  WHERE CostCenterId=@CostCenterId AND BusinessId=@BusinessId AND IsActive=1)
                  THROW 51000,'The cost center is not active in this business.',1;
                IF @IsActive=1 AND @WarehouseId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Warehouses WITH(UPDLOCK,HOLDLOCK)
                  WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId AND IsActive=1)
                  THROW 51000,'The warehouse is not active in this business.',1;
                DECLARE @Existing uniqueidentifier=(SELECT AssignmentId
                  FROM dbo.AccountingCostCenterAssignments WITH(UPDLOCK,HOLDLOCK)
                  WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND AssignmentId=@AssignmentId);
                IF @Existing IS NOT NULL AND (@Version IS NULL OR NOT EXISTS(
                  SELECT 1 FROM dbo.AccountingCostCenterAssignments
                  WHERE AssignmentId=@Existing AND RowVersion=@Version))
                  THROW 51000,'La regla cambió. Actualiza la lista antes de guardar.',1;
                IF @Existing IS NULL AND @Version IS NOT NULL
                  THROW 51000,'La regla no pertenece a esta sede o ya no existe.',1;
                IF @Existing IS NULL
                BEGIN
                  INSERT dbo.AccountingCostCenterAssignments
                    (AssignmentId,TenantId,BusinessId,CostCenterId,OperationKind,WarehouseId,IsActive,CreatedAt,UpdatedAt)
                  VALUES(@AssignmentId,@TenantId,@BusinessId,@CostCenterId,@OperationKind,@WarehouseId,@IsActive,@Now,@Now);
                  SET @Existing=@AssignmentId;
                END
                ELSE UPDATE dbo.AccountingCostCenterAssignments SET CostCenterId=@CostCenterId,
                  OperationKind=@OperationKind,WarehouseId=@WarehouseId,
                  IsActive=@IsActive,UpdatedAt=@Now WHERE AssignmentId=@Existing;
                SELECT assignment.AssignmentId,assignment.BusinessId,assignment.CostCenterId,
                       center.Code,center.Name,assignment.OperationKind,assignment.WarehouseId,
                       warehouse.Code,warehouse.Name,assignment.IsActive,assignment.RowVersion
                FROM dbo.AccountingCostCenterAssignments assignment
                INNER JOIN dbo.AccountingCostCenters center ON center.CostCenterId=assignment.CostCenterId
                LEFT JOIN dbo.Warehouses warehouse ON warehouse.WarehouseId=assignment.WarehouseId
                WHERE assignment.AssignmentId=@Existing;
                """, connection, transaction);
            command.Parameters.Add("@Version", SqlDbType.Timestamp).Value =
                request.RowVersion is null ? DBNull.Value : RequiredCostCenterVersion(request.RowVersion);
            command.Parameters.AddWithValue("@AssignmentId", request.AssignmentId);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@CostCenterId", request.CostCenterId);
            command.Parameters.AddWithValue("@OperationKind", request.OperationKind);
            command.Parameters.AddWithValue("@WarehouseId", (object?)request.WarehouseId ?? DBNull.Value);
            command.Parameters.AddWithValue("@IsActive", request.IsActive);
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var value = new AccountingCostCenterAssignmentView(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetBoolean(9), Convert.ToBase64String((byte[])reader[10]));
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return value;
        }
        catch (SqlException error) when (error.Number is 51000 or 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new AccountingConflictException(error.Number == 51000 ? error.Message :
                "Ya existe una regla para esa operación y bodega. Edita la regla existente.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<BankAccountView>> ListBankAccountsAsync(
        AccountingUserIdentity user, bool includeInactive, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT b.BankAccountId,b.AccountingAccountId,a.Code,a.Name,
                   b.AccountTypeOptionId,o.Code,o.Label,b.BankName,b.AccountNumber,
                   b.DisplayName,b.CurrencyCode,b.IsPrimary,b.IsActive,b.RowVersion
            FROM accounting.BankAccounts b
            INNER JOIN dbo.AccountingAccounts a
              ON a.AccountId=b.AccountingAccountId AND a.TenantId=b.TenantId
            INNER JOIN reference.Options o
              ON o.OptionId=b.AccountTypeOptionId AND o.CatalogCode=N'bank-account-type'
            WHERE b.TenantId=@TenantId AND (@IncludeInactive=1 OR b.IsActive=1)
            ORDER BY b.IsPrimary DESC,b.DisplayName,b.BankAccountId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@IncludeInactive", includeInactive);
        var values = new List<BankAccountView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetString(3), reader.GetGuid(4), reader.GetString(5), reader.GetString(6),
                reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10),
                reader.GetBoolean(11), reader.GetBoolean(12),
                Convert.ToBase64String((byte[])reader[13])));
        return values;
    }

    public Task<IReadOnlyList<BankAccountView>> ListActiveBankAccountsForTenantAsync(
        Guid tenantId, CancellationToken cancellationToken) => ListBankAccountsAsync(
            new AccountingUserIdentity(Guid.Empty, tenantId, Guid.Empty,
                new HashSet<string>(StringComparer.Ordinal)), false, cancellationToken);

    public async Task<bool> IsAccountingEnabledAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT COUNT_BIG(1) FROM dbo.AccountingTenantSettings
            WHERE TenantId=@TenantId AND Status=N'Ready';
            """, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<BankAccountView> SaveBankAccountAsync(
        AccountingUserIdentity user, SaveBankAccountRequest request,
        CancellationToken cancellationToken)
    {
        byte[]? expectedVersion = null;
        if (!string.IsNullOrWhiteSpace(request.RowVersion))
        {
            try { expectedVersion = Convert.FromBase64String(request.RowVersion); }
            catch (FormatException) { throw new AccountingValidationException("La versión de la cuenta bancaria no es válida."); }
        }

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using (var validation = new SqlCommand("""
                IF NOT EXISTS(
                    SELECT 1 FROM dbo.AccountingAccounts WITH(UPDLOCK,HOLDLOCK)
                    WHERE AccountId=@AccountingAccountId AND TenantId=@TenantId
                      AND IsActive=1 AND AllowsPosting=1 AND AccountType=N'Asset'
                      AND RequiresParty=0)
                    THROW 51400,N'La cuenta bancaria debe usar un auxiliar activo del activo, contabilizable y sin tercero obligatorio.',1;
                IF NOT EXISTS(
                    SELECT 1 FROM reference.Options
                    WHERE OptionId=@AccountTypeOptionId AND CatalogCode=N'bank-account-type' AND IsActive=1)
                    THROW 51400,N'El tipo de cuenta bancaria no es válido.',1;
                """, connection, transaction))
            {
                validation.Parameters.AddWithValue("@AccountingAccountId", request.AccountingAccountId);
                validation.Parameters.AddWithValue("@AccountTypeOptionId", request.AccountTypeOptionId);
                validation.Parameters.AddWithValue("@TenantId", user.TenantId);
                await validation.ExecuteNonQueryAsync(cancellationToken);
            }

            var exists = false;
            var wasPrimary = false;
            Guid? previousAccountingAccountId = null;
            await using (var current = new SqlCommand("""
                SELECT IsPrimary,AccountingAccountId FROM accounting.BankAccounts WITH(UPDLOCK,HOLDLOCK)
                WHERE BankAccountId=@BankAccountId AND TenantId=@TenantId;
                """, connection, transaction))
            {
                current.Parameters.AddWithValue("@BankAccountId", request.BankAccountId);
                current.Parameters.AddWithValue("@TenantId", user.TenantId);
                await using var reader = await current.ExecuteReaderAsync(cancellationToken);
                exists = await reader.ReadAsync(cancellationToken);
                if (exists) { wasPrimary = reader.GetBoolean(0); previousAccountingAccountId = reader.GetGuid(1); }
            }

            if (exists && expectedVersion is null)
                throw new AccountingConflictException("Actualiza la lista para obtener la versión vigente de la cuenta bancaria.");
            if (exists && previousAccountingAccountId != request.AccountingAccountId)
            {
                await using var used = new SqlCommand("""
                    SELECT CASE
                      WHEN EXISTS(SELECT 1 FROM dbo.SalesPayments WHERE BankAccountId=@BankAccountId)
                        OR EXISTS(SELECT 1 FROM dbo.SalesReturns WHERE BankAccountId=@BankAccountId)
                        OR EXISTS(SELECT 1 FROM dbo.SalesReturnSettlements WHERE BankAccountId=@BankAccountId)
                        OR EXISTS(SELECT 1 FROM dbo.SupplierPayments WHERE BankAccountId=@BankAccountId)
                        OR EXISTS(SELECT 1 FROM dbo.CustomerPayments WHERE BankAccountId=@BankAccountId)
                        OR EXISTS(SELECT 1 FROM accounting.BankReconciliations WHERE BankAccountId=@BankAccountId)
                      THEN 1 ELSE 0 END;
                    """, connection, transaction);
                used.Parameters.AddWithValue("@BankAccountId", request.BankAccountId);
                if (Convert.ToBoolean(await used.ExecuteScalarAsync(cancellationToken)))
                    throw new AccountingConflictException("El auxiliar PUC no puede cambiar porque la cuenta bancaria ya tiene movimientos o conciliaciones.");
            }

            var makePrimary = request.IsActive && (request.IsPrimary || !await HasActiveBankAccountAsync(
                connection, transaction, user.TenantId, request.BankAccountId, cancellationToken));
            if (wasPrimary && request.IsActive && !makePrimary)
                throw new AccountingConflictException("Selecciona otra cuenta principal antes de quitar esta condición.");
            if (wasPrimary && !request.IsActive && await HasActiveBankAccountAsync(
                    connection, transaction, user.TenantId, request.BankAccountId, cancellationToken))
                throw new AccountingConflictException("Selecciona otra cuenta principal antes de desactivar esta cuenta.");

            if (makePrimary)
            {
                await using var clear = new SqlCommand("""
                    UPDATE accounting.BankAccounts SET IsPrimary=0,UpdatedByUserId=@UserId,UpdatedAt=@Now
                    WHERE TenantId=@TenantId AND BankAccountId<>@BankAccountId AND IsPrimary=1;
                    """, connection, transaction);
                clear.Parameters.AddWithValue("@UserId", user.UserId);
                clear.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
                clear.Parameters.AddWithValue("@TenantId", user.TenantId);
                clear.Parameters.AddWithValue("@BankAccountId", request.BankAccountId);
                await clear.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var save = new SqlCommand(exists ? """
                UPDATE accounting.BankAccounts SET AccountingAccountId=@AccountingAccountId,
                    AccountTypeOptionId=@AccountTypeOptionId,BankName=@BankName,
                    AccountNumber=@AccountNumber,DisplayName=@DisplayName,
                    IsPrimary=@IsPrimary,IsActive=@IsActive,UpdatedByUserId=@UserId,UpdatedAt=@Now
                WHERE BankAccountId=@BankAccountId AND TenantId=@TenantId
                  AND (@ExpectedVersion IS NULL OR RowVersion=@ExpectedVersion);
                """ : """
                INSERT accounting.BankAccounts(BankAccountId,TenantId,AccountingAccountId,
                    AccountTypeOptionId,BankName,AccountNumber,DisplayName,CurrencyCode,
                    IsPrimary,IsActive,CreatedByUserId,CreatedAt,UpdatedByUserId,UpdatedAt)
                VALUES(@BankAccountId,@TenantId,@AccountingAccountId,@AccountTypeOptionId,
                    @BankName,@AccountNumber,@DisplayName,N'COP',@IsPrimary,@IsActive,
                    @UserId,@Now,@UserId,@Now);
                """, connection, transaction);
            save.Parameters.AddWithValue("@BankAccountId", request.BankAccountId);
            save.Parameters.AddWithValue("@TenantId", user.TenantId);
            save.Parameters.AddWithValue("@AccountingAccountId", request.AccountingAccountId);
            save.Parameters.AddWithValue("@AccountTypeOptionId", request.AccountTypeOptionId);
            save.Parameters.AddWithValue("@BankName", request.BankName);
            save.Parameters.AddWithValue("@AccountNumber", request.AccountNumber);
            save.Parameters.AddWithValue("@DisplayName", request.DisplayName);
            save.Parameters.AddWithValue("@IsPrimary", makePrimary);
            save.Parameters.AddWithValue("@IsActive", request.IsActive);
            save.Parameters.AddWithValue("@UserId", user.UserId);
            save.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            if (exists)
                save.Parameters.Add("@ExpectedVersion", SqlDbType.Timestamp).Value =
                    (object?)expectedVersion ?? DBNull.Value;
            if (await save.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new AccountingConflictException("La cuenta bancaria cambió. Actualiza la lista e inténtalo de nuevo.");

            await EnqueueBankAccountSynchronizationAsync(
                connection, transaction, user.TenantId, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
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

        return (await ListBankAccountsAsync(user, true, cancellationToken))
            .Single(value => value.BankAccountId == request.BankAccountId);
    }

    private static async Task<bool> HasActiveBankAccountAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId,
        Guid excludedId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT CASE WHEN EXISTS(SELECT 1 FROM accounting.BankAccounts WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantId=@TenantId AND IsActive=1 AND BankAccountId<>@ExcludedId)
                THEN 1 ELSE 0 END;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@ExcludedId", excludedId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task EnqueueBankAccountSynchronizationAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId,
        CancellationToken cancellationToken)
    {
        await SqlAccountingPosSynchronizationOutbox.InsertTenantConfigurationAsync(
            connection, transaction, tenantId, ids, timeProvider.GetUtcNow(),
            cancellationToken);
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
    private static bool IsConflict(SqlException exception) => exception.Number is 2601 or 2627 or 547 or 51400 or 51401 or 51402 or 51403 or 51404 or 51405;
    private static void AddMoney(SqlCommand command, string name, decimal value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = 19;
        parameter.Scale = 4;
        parameter.Value = value;
    }
    private sealed record OpeningBatchPosting(
        Guid BatchId, Guid BusinessId, string Description,
        List<AccountingOpeningBalanceLineView> Lines);
    private static void AddPeriod(SqlCommand command, Guid tenantId, DateOnly starts, DateOnly ends) { command.Parameters.AddWithValue("@TenantId", tenantId); command.Parameters.AddWithValue("@StartsOn", starts.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue("@EndsOn", ends.ToDateTime(TimeOnly.MinValue)); }
    private static void AddMapping(SqlCommand command, SetAccountMappingRequest value) { command.Parameters.AddWithValue("@TenantId", value.TenantId); command.Parameters.AddWithValue("@BusinessId", (object?)value.BusinessId ?? DBNull.Value); command.Parameters.AddWithValue("@Category", value.Category); command.Parameters.AddWithValue("@AccountId", value.AccountId); command.Parameters.AddWithValue("@EffectiveFrom", value.EffectiveFrom.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue("@EffectiveTo", value.EffectiveTo is null ? DBNull.Value : value.EffectiveTo.Value.ToDateTime(TimeOnly.MinValue)); }
    private static void AddScope(SqlCommand command, AccountingUserIdentity user, Guid documentId) { command.Parameters.AddWithValue("@TenantId", user.TenantId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId); command.Parameters.AddWithValue("@DocumentId", documentId); }
}
