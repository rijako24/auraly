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
        await using var command = new SqlCommand(SelectColumns + " WHERE fd.BusinessId=@BusinessId AND fd.DocumentId=@DocumentId;", connection);
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
            WHERE fd.BusinessId = @BusinessId
              AND (@Status IS NULL OR p.Status = @Status)
              AND (@AuralyNumber IS NULL OR fd.AuralyDocumentNumber = @AuralyNumber)
              AND (@DianNumber IS NULL OR fd.FiscalNumber = @DianNumber)
              AND (@UniqueCode IS NULL OR fd.UniqueCode = @UniqueCode)
              AND (@DeviceId IS NULL OR sale.DeviceId = @DeviceId)
              AND (@IssuedFrom IS NULL OR fd.IssuedAt >= @IssuedFrom)
              AND (@IssuedTo IS NULL OR fd.IssuedAt <= @IssuedTo)
              AND (@QuotaOnly=0 OR p.QuotaBlockedAt IS NOT NULL)
            """;
        var offset = checked((query.Page - 1) * query.PageSize);
        var pageSql = SelectColumns + " " + filters + " ORDER BY fd.IssuedAt DESC,fd.DocumentId DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
        var countSql = "SELECT COUNT_BIG(1) FROM dbo.FiscalDocuments fd INNER JOIN dbo.FiscalDocumentProcesses p ON p.DocumentId=fd.DocumentId LEFT JOIN dbo.SalesDocuments sale ON sale.DocumentId=fd.DocumentId " + filters + ";";
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
            INNER JOIN dbo.FiscalDocuments fd ON fd.DocumentId = p.DocumentId
            WHERE p.DocumentId = @DocumentId
              AND p.BusinessId = @BusinessId
              AND fd.BusinessId = @BusinessId
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
                if (existing.Status is
                    FiscalDocumentStatusCodes.PendingGeneration or
                    FiscalDocumentStatusCodes.PendingSubmission or
                    FiscalDocumentStatusCodes.PendingDianResult or
                    FiscalDocumentStatusCodes.RetryScheduled)
                    return existing;
                throw new FiscalOperationException($"Fiscal document in status '{existing.Status}' cannot be retried.");
            }
        }
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(businessId, documentId, cancellationToken);
    }

    private const string SelectColumns = """
        SELECT fd.DocumentId,fd.BusinessId,fd.SourceDocumentType,fd.FiscalDocumentType,
               fd.AuralyDocumentNumber,fd.FiscalNumber,fd.UniqueCodeType,fd.UniqueCode,
               p.Status,sale.DeviceId,fd.IssuedAt,
               p.AttemptCount, p.TrackId, p.LastStatusCode,
               p.LastStatusDescription, p.UpdatedAt,p.QuotaBlockedAt
        FROM dbo.FiscalDocuments fd
        INNER JOIN dbo.FiscalDocumentProcesses p ON p.DocumentId=fd.DocumentId
        LEFT JOIN dbo.SalesDocuments sale ON sale.DocumentId=fd.DocumentId
        """;

    private static void AddFilters(SqlCommand command, Guid businessId, FiscalDocumentQuery query)
    {
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Status", Db(query.Status));
        command.Parameters.AddWithValue("@AuralyNumber", Db(query.AuralyNumber));
        command.Parameters.AddWithValue("@DianNumber", Db(query.DianNumber));
        command.Parameters.AddWithValue("@UniqueCode", Db(query.UniqueCode));
        command.Parameters.AddWithValue("@DeviceId", (object?)query.DeviceId ?? DBNull.Value);
        command.Parameters.AddWithValue("@IssuedFrom", (object?)query.IssuedFrom ?? DBNull.Value);
        command.Parameters.AddWithValue("@IssuedTo", (object?)query.IssuedTo ?? DBNull.Value);
        command.Parameters.AddWithValue("@QuotaOnly", query.QuotaOnly);
    }

    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static FiscalDocumentView Read(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
        reader.GetString(4), reader.GetString(5), reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetGuid(9), reader.GetDateTimeOffset(10),
        reader.GetInt32(11), reader.IsDBNull(12) ? null : reader.GetString(12),
        reader.IsDBNull(13) ? null : reader.GetString(13),
        reader.IsDBNull(14) ? null : reader.GetString(14), reader.GetDateTimeOffset(15),
        reader.IsDBNull(16) ? null : reader.GetDateTimeOffset(16));
}
