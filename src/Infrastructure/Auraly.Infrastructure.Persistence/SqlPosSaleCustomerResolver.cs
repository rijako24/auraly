using System.Data;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPosSaleCustomerResolver(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids) : IPosSaleCustomerResolver
{
    public async Task<Guid?> ResolveForBusinessAsync(
        Guid tenantId,
        Guid businessId,
        Guid sourceCustomerId,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var partyId = await SourcePartyAsync(
                connection, transaction, tenantId, sourceCustomerId, ct);
            if (partyId is null)
            {
                await transaction.CommitAsync(ct);
                return null;
            }

            var current = await CurrentCustomerAsync(
                connection, transaction, businessId, partyId.Value, ct);
            if (current is not null)
            {
                await transaction.CommitAsync(ct);
                return current;
            }

            var customerId = ids.NewId();
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT dbo.Customers
                  (CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
                SELECT @CustomerId,@PartyId,@BusinessId,1,@ActorId,@Now
                WHERE EXISTS (
                  SELECT 1 FROM dbo.Businesses
                  WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND IsActive=1);
                IF @@ROWCOUNT=0
                  THROW 51030,'The destination business is outside the authenticated tenant.',1;
                """;
            insert.Parameters.AddRange(
            [
                P("@CustomerId", customerId),
                P("@PartyId", partyId.Value),
                P("@BusinessId", businessId),
                P("@TenantId", tenantId),
                P("@ActorId", actorId),
                P("@Now", now)
            ]);
            await insert.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
            return customerId;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            return await ResolveExistingAsync(
                tenantId, businessId, sourceCustomerId, ct);
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<Guid?> ResolveExistingAsync(
        Guid tenantId,
        Guid businessId,
        Guid sourceCustomerId,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT destination.CustomerId
            FROM dbo.Customers source
            INNER JOIN dbo.Parties party ON party.PartyId=source.PartyId
            INNER JOIN dbo.Businesses sourceBusiness ON sourceBusiness.BusinessId=source.BusinessId
            INNER JOIN dbo.Customers destination
              ON destination.PartyId=party.PartyId AND destination.BusinessId=@BusinessId
            WHERE source.CustomerId=@SourceCustomerId
              AND sourceBusiness.TenantId=@TenantId
              AND party.TenantId=@TenantId;
            """;
        command.Parameters.AddRange(
        [
            P("@SourceCustomerId", sourceCustomerId),
            P("@BusinessId", businessId),
            P("@TenantId", tenantId)
        ]);
        return await command.ExecuteScalarAsync(ct) as Guid?;
    }

    private static async Task<Guid?> SourcePartyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        Guid sourceCustomerId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT party.PartyId
            FROM dbo.Customers customer WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Parties party ON party.PartyId=customer.PartyId
            INNER JOIN dbo.Businesses business ON business.BusinessId=customer.BusinessId
            WHERE customer.CustomerId=@CustomerId
              AND business.TenantId=@TenantId
              AND party.TenantId=@TenantId;
            """;
        command.Parameters.AddRange(
        [
            P("@CustomerId", sourceCustomerId),
            P("@TenantId", tenantId)
        ]);
        return await command.ExecuteScalarAsync(ct) as Guid?;
    }

    private static async Task<Guid?> CurrentCustomerAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid partyId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CustomerId
            FROM dbo.Customers WITH (UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND PartyId=@PartyId;
            """;
        command.Parameters.AddRange(
        [
            P("@BusinessId", businessId),
            P("@PartyId", partyId)
        ]);
        return await command.ExecuteScalarAsync(ct) as Guid?;
    }

    private static SqlParameter P(string name, object value) => new(name, value);
}
