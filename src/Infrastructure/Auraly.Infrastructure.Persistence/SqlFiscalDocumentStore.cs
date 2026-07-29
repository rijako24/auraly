using System.Data;
using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlFiscalDocumentStore(SqlServerConnectionFactory connections) : IFiscalDocumentStore
{
    public async Task<FiscalDocumentView?> GetAsync(
        Guid businessId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(SelectColumns + " WHERE d.BusinessId = @BusinessId AND d.DocumentId = @DocumentId;", connection);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<FiscalDocumentPage> PageAsync(
        Guid businessId,
        FiscalDocumentQuery query,
        CancellationToken cancellationToken)
    {
        const string filters = """
            WHERE d.BusinessId = @BusinessId
              AND (@Status IS NULL OR p.Status = @Status)
              AND (@AuralyNumber IS NULL OR d.DocumentNumber = @AuralyNumber)
              AND (@DianNumber IS NULL OR d.FiscalNumber = @DianNumber)
              AND (@Cufe IS NULL OR d.CufeReceived = @Cufe)
              AND (@RegisterId IS NULL OR d.RegisterId = @RegisterId)
              AND (@IssuedFrom IS NULL OR d.IssuedAt >= @IssuedFrom)
              AND (@IssuedTo IS NULL OR d.IssuedAt <= @IssuedTo)
            """;
        var offset = checked((query.Page - 1) * query.PageSize);
        var pageSql = SelectColumns + " " + filters + " ORDER BY d.IssuedAt DESC, d.DocumentId DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
        var countSql = "SELECT COUNT_BIG(1) FROM dbo.SalesDocuments d INNER JOIN dbo.FiscalDocumentProcesses p ON p.DocumentId = d.DocumentId " + filters + ";";
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var items = new List<FiscalDocumentView>();
        await using (var command = new SqlCommand(pageSql, connection))
        {
            AddFilters(command, businessId, query);
            command.Parameters.AddWithValue("@Offset", offset);
            command.Parameters.AddWithValue("@PageSize", query.PageSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) items.Add(Read(reader));
        }
        long total;
        await using (var command = new SqlCommand(countSql, connection))
        {
            AddFilters(command, businessId, query);
            total = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }
        return new FiscalDocumentPage(items, query.Page, query.PageSize, total);
    }

    public async Task<FiscalDocumentView?> RetryAsync(
        Guid businessId,
        Guid documentId,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE p WITH (UPDLOCK, ROWLOCK)
            SET Status = CASE WHEN p.TrackId IS NULL THEN @PendingGeneration ELSE @PendingResult END,
                NextAttemptAt = @RequestedAt,
                LockedAt = NULL,
                LockedBy = NULL,
                LastErrorCode = NULL,
                LastErrorMessage = NULL,
                UpdatedAt = @RequestedAt
            FROM dbo.FiscalDocumentProcesses p
            INNER JOIN dbo.SalesDocuments d ON d.DocumentId = p.DocumentId
            WHERE p.DocumentId = @DocumentId
              AND p.BusinessId = @BusinessId
              AND d.BusinessId = @BusinessId
              AND p.Status IN (@SchemaFailed, @SignatureFailed, @RetryScheduled, @PermanentFailure);
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using (var command = new SqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("@DocumentId", documentId);
            command.Parameters.AddWithValue("@BusinessId", businessId);
            command.Parameters.AddWithValue("@RequestedAt", requestedAt);
            command.Parameters.AddWithValue("@PendingGeneration", FiscalDocumentStatusCodes.PendingGeneration);
            command.Parameters.AddWithValue("@PendingResult", FiscalDocumentStatusCodes.PendingDianResult);
            command.Parameters.AddWithValue("@SchemaFailed", FiscalDocumentStatusCodes.SchemaValidationFailed);
            command.Parameters.AddWithValue("@SignatureFailed", FiscalDocumentStatusCodes.SignatureFailed);
            command.Parameters.AddWithValue("@RetryScheduled", FiscalDocumentStatusCodes.RetryScheduled);
            command.Parameters.AddWithValue("@PermanentFailure", FiscalDocumentStatusCodes.PermanentFailure);
            var changed = await command.ExecuteNonQueryAsync(cancellationToken);
            if (changed == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                var existing = await GetAsync(businessId, documentId, cancellationToken);
                if (existing is null) return null;
                throw new FiscalOperationException($"Fiscal document in status '{existing.Status}' cannot be retried.");
            }
        }
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(businessId, documentId, cancellationToken);
    }

    private const string SelectColumns = """
        SELECT d.DocumentId, d.BusinessId, d.DocumentNumber, d.FiscalNumber,
               d.CufeReceived, p.Status, d.RegisterId, d.IssuedAt,
               p.AttemptCount, p.TrackId, p.LastStatusCode,
               p.LastStatusDescription, p.UpdatedAt
        FROM dbo.SalesDocuments d
        INNER JOIN dbo.FiscalDocumentProcesses p ON p.DocumentId = d.DocumentId
        """;

    private static void AddFilters(SqlCommand command, Guid businessId, FiscalDocumentQuery query)
    {
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Status", Db(query.Status));
        command.Parameters.AddWithValue("@AuralyNumber", Db(query.AuralyNumber));
        command.Parameters.AddWithValue("@DianNumber", Db(query.DianNumber));
        command.Parameters.AddWithValue("@Cufe", Db(query.Cufe));
        command.Parameters.AddWithValue("@RegisterId", (object?)query.RegisterId ?? DBNull.Value);
        command.Parameters.AddWithValue("@IssuedFrom", (object?)query.IssuedFrom ?? DBNull.Value);
        command.Parameters.AddWithValue("@IssuedTo", (object?)query.IssuedTo ?? DBNull.Value);
    }

    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static FiscalDocumentView Read(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
        reader.GetString(4), reader.GetString(5), reader.GetGuid(6), reader.GetDateTimeOffset(7),
        reader.GetInt32(8), reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.IsDBNull(11) ? null : reader.GetString(11), reader.GetDateTimeOffset(12));
}