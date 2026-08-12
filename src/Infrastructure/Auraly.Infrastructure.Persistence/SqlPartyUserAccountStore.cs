using Auraly.Application.Parties;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPartyStore
{
    public async Task<PartyUserAccountLink?> GetUserAccountAsync(
        Guid tenantId,
        Guid partyId,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.UserId,u.PartyId,u.Username,u.Email,u.IsActive
            FROM dbo.Parties p
            JOIN dbo.AppUsers u ON u.PartyId=p.PartyId AND u.TenantId=p.TenantId
            WHERE p.TenantId=@TenantId AND p.PartyId=@PartyId;
            """;
        command.Parameters.AddRange([P("@TenantId", tenantId), P("@PartyId", partyId)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadUserAccount(reader) : null;
    }

    public async Task<PartyUserAccountLink> LinkUserAccountAsync(
        Guid tenantId,
        Guid partyId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                IF NOT EXISTS (
                  SELECT 1 FROM dbo.Parties WITH (UPDLOCK,HOLDLOCK)
                  WHERE PartyId=@PartyId AND TenantId=@TenantId)
                  THROW 51040,'Party is outside the authenticated tenant.',1;
                IF NOT EXISTS (
                  SELECT 1 FROM dbo.AppUsers WITH (UPDLOCK,HOLDLOCK)
                  WHERE UserId=@UserId AND TenantId=@TenantId)
                  THROW 51041,'User account is outside the authenticated tenant.',1;
                IF EXISTS (
                  SELECT 1 FROM dbo.AppUsers
                  WHERE UserId=@UserId AND PartyId IS NOT NULL AND PartyId<>@PartyId)
                  THROW 51042,'The user account is already linked to another Party.',1;
                IF EXISTS (
                  SELECT 1 FROM dbo.AppUsers
                  WHERE PartyId=@PartyId AND UserId<>@UserId)
                  THROW 51043,'The Party is already linked to another user account.',1;

                UPDATE dbo.AppUsers
                SET PartyId=@PartyId,UpdatedAt=@Now
                WHERE UserId=@UserId AND TenantId=@TenantId;

                SELECT UserId,PartyId,Username,Email,IsActive
                FROM dbo.AppUsers
                WHERE UserId=@UserId AND TenantId=@TenantId;
                """;
            command.Parameters.AddRange(
            [
                P("@TenantId", tenantId),
                P("@PartyId", partyId),
                P("@UserId", userId),
                P("@Now", now.UtcDateTime)
            ]);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new PartyConflictException("The user account link was not persisted.");
            var result = ReadUserAccount(reader);
            await reader.CloseAsync();
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (SqlException exception) when (exception.Number is 51040 or 51041)
        {
            await transaction.RollbackAsync(ct);
            throw new PartyForbiddenException(exception.Message);
        }
        catch (SqlException exception) when (exception.Number is 51042 or 51043 or 2601 or 2627)
        {
            await transaction.RollbackAsync(ct);
            throw new PartyConflictException(exception.Message);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task UnlinkUserAccountAsync(
        Guid tenantId,
        Guid partyId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (
              SELECT 1 FROM dbo.Parties
              WHERE PartyId=@PartyId AND TenantId=@TenantId)
              THROW 51040,'Party is outside the authenticated tenant.',1;

            UPDATE dbo.AppUsers
            SET PartyId=NULL,UpdatedAt=@Now
            WHERE PartyId=@PartyId AND TenantId=@TenantId;
            """;
        command.Parameters.AddRange(
        [
            P("@TenantId", tenantId),
            P("@PartyId", partyId),
            P("@Now", now.UtcDateTime)
        ]);
        try
        {
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException exception) when (exception.Number is 51040)
        {
            throw new PartyForbiddenException(exception.Message);
        }
    }

    private static PartyUserAccountLink ReadUserAccount(SqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetBoolean(4));
}
