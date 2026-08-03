using Auraly.Application.Parties;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlExternalCustomerReconciliationStore(
    SqlServerConnectionFactory connections) : IExternalCustomerReconciliationStore
{
    public async Task<ExternalCustomerReconciliationPage> PageAsync(
        PartyActorIdentity actor,
        int page,
        ExternalCustomerReconciliationQuery query,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT_BIG(1)
            FROM dbo.ExternalCommerceCustomers e
            JOIN dbo.IntegrationConnections i ON i.IntegrationConnectionId=e.IntegrationConnectionId
            JOIN dbo.Businesses b ON b.BusinessId=e.BusinessId
            WHERE e.BusinessId=@BusinessId AND b.TenantId=@TenantId
              AND (@Status IS NULL OR e.ReconciliationStatus=@Status)
              AND (@IntegrationId IS NULL OR e.IntegrationConnectionId=@IntegrationId)
              AND (@Search IS NULL OR e.Name LIKE N'%'+@Search+N'%'
                   OR e.Phone LIKE N'%'+@Search+N'%'
                   OR e.PhoneNormalized LIKE N'%'+@Search+N'%'
                   OR e.ExternalAccountId LIKE N'%'+@Search+N'%'
                   OR e.ExternalCustomerId LIKE N'%'+@Search+N'%');

            SELECT e.ExternalCommerceCustomerId,e.IntegrationConnectionId,i.Name,
                   e.ExternalAccountId,e.ExternalCustomerId,e.Name,e.Phone,e.PhoneNormalized,
                   e.ReconciliationStatus,e.ReconciliationError,e.PartyId,e.CustomerId,
                   e.LastSyncedAt,e.ReconciledAt
            FROM dbo.ExternalCommerceCustomers e
            JOIN dbo.IntegrationConnections i ON i.IntegrationConnectionId=e.IntegrationConnectionId
            JOIN dbo.Businesses b ON b.BusinessId=e.BusinessId
            WHERE e.BusinessId=@BusinessId AND b.TenantId=@TenantId
              AND (@Status IS NULL OR e.ReconciliationStatus=@Status)
              AND (@IntegrationId IS NULL OR e.IntegrationConnectionId=@IntegrationId)
              AND (@Search IS NULL OR e.Name LIKE N'%'+@Search+N'%'
                   OR e.Phone LIKE N'%'+@Search+N'%'
                   OR e.PhoneNormalized LIKE N'%'+@Search+N'%'
                   OR e.ExternalAccountId LIKE N'%'+@Search+N'%'
                   OR e.ExternalCustomerId LIKE N'%'+@Search+N'%')
            ORDER BY e.LastSyncedAt DESC,e.ExternalCommerceCustomerId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        command.Parameters.AddRange([
            P("@BusinessId", actor.BusinessId),
            P("@TenantId", actor.TenantId),
            P("@Status", Empty(query.Status)),
            P("@IntegrationId", query.IntegrationConnectionId),
            P("@Search", Empty(query.Search)),
            P("@Offset", (page - 1) * query.PageSize),
            P("@PageSize", query.PageSize)
        ]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var total = checked((int)reader.GetInt64(0));
        await reader.NextResultAsync(cancellationToken);
        var items = new List<ExternalCustomerReconciliationItem>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(Read(reader));
        return new ExternalCustomerReconciliationPage(
            items,
            page,
            query.PageSize,
            total,
            (int)Math.Ceiling(total / (double)query.PageSize));
    }

    public async Task<IReadOnlyCollection<Guid>> PendingIdsAsync(
        PartyActorIdentity actor,
        int maximumItems,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP(@Maximum) e.ExternalCommerceCustomerId
            FROM dbo.ExternalCommerceCustomers e
            JOIN dbo.Businesses b ON b.BusinessId=e.BusinessId
            WHERE e.BusinessId=@BusinessId AND b.TenantId=@TenantId
              AND e.ReconciliationStatus=N'Pending'
            ORDER BY e.LastSyncedAt,e.ExternalCommerceCustomerId;
            """;
        command.Parameters.AddRange([
            P("@Maximum", maximumItems),
            P("@BusinessId", actor.BusinessId),
            P("@TenantId", actor.TenantId)
        ]);
        var result = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetGuid(0));
        return result;
    }

    public async Task<ExternalCustomerReconciliationResult> ReconcileAsync(
        ExternalCustomerReconciliationExecution execution,
        Guid externalCommerceCustomerId,
        Guid newPartyId,
        Guid newCustomerId,
        Guid newContactId,
        Guid notificationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var source = await LoadSourceAsync(
                connection,
                transaction,
                execution,
                externalCommerceCustomerId,
                cancellationToken);
            if (source.Status == ExternalCustomerReconciliationStatuses.Linked &&
                source.PartyId is Guid linkedParty && source.CustomerId is Guid linkedCustomer)
            {
                await transaction.CommitAsync(cancellationToken);
                return new ExternalCustomerReconciliationResult(
                    externalCommerceCustomerId,
                    source.Status,
                    linkedParty,
                    linkedCustomer,
                    null,
                    true);
            }

            if (!source.IsActive)
                return await ConflictAsync(
                    connection,
                    transaction,
                    execution,
                    source,
                    "The external customer is inactive.",
                    now,
                    cancellationToken);

            var candidates = string.IsNullOrWhiteSpace(source.PhoneNormalized)
                ? []
                : await CandidatePartyIdsAsync(
                    connection,
                    transaction,
                    execution.TenantId,
                    source.PhoneNormalized,
                    cancellationToken);
            if (candidates.Count > 1)
                return await ConflictAsync(
                    connection,
                    transaction,
                    execution,
                    source,
                    "The phone matches more than one Party. Manual identity review is required.",
                    now,
                    cancellationToken);

            var partyId = candidates.Count == 1 ? candidates[0] : newPartyId;
            if (candidates.Count == 0)
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT dbo.Parties
                      (PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
                    VALUES
                      (@PartyId,@TenantId,N'NaturalPerson',@DisplayName,N'Incomplete',1,@ActorId,@Now);
                    """, [
                    P("@PartyId", partyId),
                    P("@TenantId", execution.TenantId),
                    P("@DisplayName", DisplayName(source)),
                    P("@ActorId", execution.ActorId),
                    P("@Now", now)
                ], cancellationToken);
                if (!string.IsNullOrWhiteSpace(source.PhoneNormalized))
                    await ExecuteAsync(connection, transaction, """
                        INSERT dbo.PartyContacts
                          (PartyContactId,PartyId,ContactType,Value,NormalizedValue,IsPrimary,IsActive,CreatedAt)
                        VALUES(@ContactId,@PartyId,N'Phone',@Phone,@Normalized,1,1,@Now);
                        """, [
                        P("@ContactId", newContactId),
                        P("@PartyId", partyId),
                        P("@Phone", string.IsNullOrWhiteSpace(source.Phone) ? source.PhoneNormalized : source.Phone),
                        P("@Normalized", source.PhoneNormalized),
                        P("@Now", now)
                    ], cancellationToken);
            }

            var customerId = await CustomerIdAsync(
                connection,
                transaction,
                execution.BusinessId,
                partyId,
                cancellationToken);
            if (customerId is null)
            {
                customerId = newCustomerId;
                await ExecuteAsync(connection, transaction, """
                    INSERT dbo.Customers
                      (CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
                    VALUES(@CustomerId,@PartyId,@BusinessId,1,@ActorId,@Now);
                    """, [
                    P("@CustomerId", customerId),
                    P("@PartyId", partyId),
                    P("@BusinessId", execution.BusinessId),
                    P("@ActorId", execution.ActorId),
                    P("@Now", now)
                ], cancellationToken);
            }

            await ExecuteAsync(connection, transaction, """
                UPDATE dbo.ExternalCommerceCustomers
                SET PartyId=@PartyId,CustomerId=@CustomerId,ReconciliationStatus=N'Linked',
                    ReconciliationError=NULL,ReconciledAt=@Now,ReconciledBy=@ActorId,
                    ReconciliationOrigin=@Origin,UpdatedAt=@Now
                WHERE ExternalCommerceCustomerId=@ExternalId AND BusinessId=@BusinessId;

                DECLARE @Cursor BIGINT;
                SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
                FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
                WHERE BusinessId=@BusinessId AND Stream=N'Customers';
                INSERT dbo.PosSynchronizationOutboxMessages
                  (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
                VALUES(@NotificationId,@BusinessId,N'Customers',@Cursor,@Now);
                """, [
                P("@PartyId", partyId),
                P("@CustomerId", customerId),
                P("@ExternalId", externalCommerceCustomerId),
                P("@BusinessId", execution.BusinessId),
                P("@ActorId", execution.ActorId),
                P("@Origin", execution.Origin),
                P("@NotificationId", notificationId),
                P("@Now", now)
            ], cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ExternalCustomerReconciliationResult(
                externalCommerceCustomerId,
                ExternalCustomerReconciliationStatuses.Linked,
                partyId,
                customerId,
                null,
                false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ExternalCustomerReconciliationExecution> ResolveIntegrationExecutionAsync(
        Guid businessId,
        Guid externalCommerceCustomerId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.TenantId
            FROM dbo.ExternalCommerceCustomers e
            JOIN dbo.Businesses b ON b.BusinessId=e.BusinessId
            WHERE e.ExternalCommerceCustomerId=@ExternalId AND e.BusinessId=@BusinessId;
            """;
        command.Parameters.AddRange([
            P("@ExternalId", externalCommerceCustomerId),
            P("@BusinessId", businessId)
        ]);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not Guid tenantId)
            throw new InvalidOperationException(
                "The external customer does not belong to the signaled business.");
        return new ExternalCustomerReconciliationExecution(
            tenantId,
            businessId,
            null,
            "Integration");
    }

    public async Task<ExternalCustomerReconciliationReceipt?> ReceiptStatusAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ExternalCommerceCustomerId,BusinessId,ResultStatus
            FROM dbo.ExternalCustomerReconciliationReceipts
            WHERE MessageId=@MessageId;
            """;
        command.Parameters.Add(P("@MessageId", messageId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ExternalCustomerReconciliationReceipt(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2))
            : null;
    }

    public async Task RecordReceiptAsync(
        Guid messageId,
        Guid externalCommerceCustomerId,
        Guid businessId,
        string resultStatus,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            await using var lookup = connection.CreateCommand();
            lookup.Transaction = transaction;
            lookup.CommandText = """
                SELECT ExternalCommerceCustomerId,BusinessId
                FROM dbo.ExternalCustomerReconciliationReceipts WITH(UPDLOCK,HOLDLOCK)
                WHERE MessageId=@MessageId;
                """;
            lookup.Parameters.Add(P("@MessageId", messageId));
            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetGuid(0) != externalCommerceCustomerId ||
                    reader.GetGuid(1) != businessId)
                    throw new InvalidOperationException(
                        "The reconciliation message ID belongs to another source.");
                await reader.DisposeAsync();
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            await reader.DisposeAsync();
            await ExecuteAsync(connection, transaction, """
                INSERT dbo.ExternalCustomerReconciliationReceipts
                  (MessageId,ExternalCommerceCustomerId,BusinessId,ResultStatus,ProcessedAt)
                VALUES(@MessageId,@ExternalId,@BusinessId,@Status,@ProcessedAt);
                """, [
                P("@MessageId", messageId),
                P("@ExternalId", externalCommerceCustomerId),
                P("@BusinessId", businessId),
                P("@Status", resultStatus),
                P("@ProcessedAt", processedAt)
            ], cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
    private static async Task<Source> LoadSourceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ExternalCustomerReconciliationExecution execution,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT e.ExternalCommerceCustomerId,e.Name,e.Phone,e.PhoneNormalized,e.IsActive,
                   e.ReconciliationStatus,e.PartyId,e.CustomerId
            FROM dbo.ExternalCommerceCustomers e WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Businesses b ON b.BusinessId=e.BusinessId
            WHERE e.ExternalCommerceCustomerId=@Id AND e.BusinessId=@BusinessId AND b.TenantId=@TenantId;
            """;
        command.Parameters.AddRange([
            P("@Id", id),
            P("@BusinessId", execution.BusinessId),
            P("@TenantId", execution.TenantId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new PartyForbiddenException("External customer is outside the authenticated business.");
        return new Source(
            reader.GetGuid(0),
            S(reader, 1),
            S(reader, 2),
            reader.GetString(3),
            reader.GetBoolean(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetGuid(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7));
    }

    private static async Task<List<Guid>> CandidatePartyIdsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        string phone,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT DISTINCT p.PartyId
            FROM dbo.PartyContacts c WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Parties p ON p.PartyId=c.PartyId
            WHERE p.TenantId=@TenantId AND c.ContactType=N'Phone'
              AND c.NormalizedValue=@Phone AND c.IsActive=1;
            """;
        command.Parameters.AddRange([P("@TenantId", tenantId), P("@Phone", phone)]);
        var result = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetGuid(0));
        return result;
    }

    private static async Task<Guid?> CustomerIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid partyId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CustomerId FROM dbo.Customers WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND PartyId=@PartyId;
            """;
        command.Parameters.AddRange([P("@BusinessId", businessId), P("@PartyId", partyId)]);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? id : null;
    }

    private static async Task<ExternalCustomerReconciliationResult> ConflictAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ExternalCustomerReconciliationExecution execution,
        Source source,
        string error,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            UPDATE dbo.ExternalCommerceCustomers
            SET PartyId=NULL,CustomerId=NULL,ReconciliationStatus=N'Conflict',
                ReconciliationError=@Error,ReconciledAt=@Now,ReconciledBy=@ActorId,
                ReconciliationOrigin=@Origin,UpdatedAt=@Now
            WHERE ExternalCommerceCustomerId=@Id AND BusinessId=@BusinessId;
            """, [
            P("@Error", error),
            P("@Now", now),
            P("@ActorId", execution.ActorId),
            P("@Origin", execution.Origin),
            P("@Id", source.Id),
            P("@BusinessId", execution.BusinessId)
        ], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ExternalCustomerReconciliationResult(
            source.Id,
            ExternalCustomerReconciliationStatuses.Conflict,
            null,
            null,
            error,
            false);
    }

    private static ExternalCustomerReconciliationItem Read(SqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        S(reader, 5),
        S(reader, 6) ?? string.Empty,
        reader.GetString(7),
        reader.GetString(8),
        S(reader, 9),
        reader.IsDBNull(10) ? null : reader.GetGuid(10),
        reader.IsDBNull(11) ? null : reader.GetGuid(11),
        Utc(reader.GetDateTime(12)),
        reader.IsDBNull(13) ? null : Utc(reader.GetDateTime(13)));

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string DisplayName(Source source) =>
        string.IsNullOrWhiteSpace(source.Name)
            ? string.IsNullOrWhiteSpace(source.PhoneNormalized)
                ? "Cliente externo sin nombre"
                : $"Cliente externo {source.PhoneNormalized}"
            : source.Name.Trim();

    private static async Task ExecuteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        SqlParameter[] parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqlParameter P(string name, object? value) =>
        new(name, value ?? DBNull.Value);

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? S(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private sealed record Source(
        Guid Id,
        string? Name,
        string? Phone,
        string PhoneNormalized,
        bool IsActive,
        string Status,
        Guid? PartyId,
        Guid? CustomerId);
}
