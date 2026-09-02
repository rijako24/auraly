using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlReferenceOptionStore(SqlServerConnectionFactory connections)
    : IReferenceOptionStore
{
    public async Task<IReadOnlyList<ReferenceOption>> ListAsync(
        string catalogCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT OptionId,Code,Label,Description,SortOrder
            FROM reference.Options
            WHERE CatalogCode=@CatalogCode AND IsActive=1
            ORDER BY SortOrder,Label,Code;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@CatalogCode", catalogCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ReferenceOption>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ReferenceOption(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4)));
        }

        return result;
    }

    public async Task<ReferenceOption> CreateAsync(
        string catalogCode,
        CreateReferenceOptionRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sql = """
                IF NOT EXISTS (
                    SELECT 1 FROM reference.Options WITH (UPDLOCK,HOLDLOCK)
                    WHERE CatalogCode=@CatalogCode)
                  THROW 51220,'The reference catalog does not exist.',1;

                DECLARE @SortOrder INT=(
                    SELECT ISNULL(MAX(SortOrder),-10)+10
                    FROM reference.Options WITH (UPDLOCK,HOLDLOCK)
                    WHERE CatalogCode=@CatalogCode);
                DECLARE @Now DATETIMEOFFSET(7)=SYSUTCDATETIME();
                INSERT reference.Options(
                    OptionId,CatalogCode,Code,Label,Description,IsActive,SortOrder,CreatedAt,UpdatedAt)
                VALUES(
                    @OptionId,@CatalogCode,@Code,@Label,@Description,1,@SortOrder,@Now,@Now);
                SELECT @SortOrder;
                """;
            var optionId = Guid.NewGuid();
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@OptionId", optionId);
            command.Parameters.AddWithValue("@CatalogCode", catalogCode);
            command.Parameters.AddWithValue("@Code", request.Code);
            command.Parameters.AddWithValue("@Label", request.Label);
            command.Parameters.AddWithValue("@Description", (object?)request.Description ?? DBNull.Value);
            var sortOrder = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return new ReferenceOption(optionId, request.Code, request.Label, request.Description, sortOrder);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new CatalogConflictException(
                "An option with that code already exists in the reference catalog.");
        }
        catch (SqlException exception) when (exception.Number == 51220)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new CatalogValidationException("The reference catalog does not exist.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
