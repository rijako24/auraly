using Auraly.BuildingBlocks.Domain.Documents;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal static class SqlOperationalDocumentAllocator
{
    public static async Task<AuralyDocumentNumberAssignment> AllocateNumberAsync(SqlConnection connection,
        SqlTransaction transaction, Guid businessId, string documentType, DateTimeOffset now, CancellationToken ct)
    {
        var prefix = AuralyDocumentTypes.DefaultPrefix(documentType);
        await using (var ensure = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.DocumentSeries WITH(UPDLOCK,HOLDLOCK)
              WHERE BusinessId=@BusinessId AND DocumentType=@Type AND DeviceId IS NULL AND IsActive=1)
              INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
                Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
              VALUES(NEWID(),@BusinessId,NULL,@Type,@Prefix,N'00',8,1,99999999,0,1,@Now);
            """, connection, transaction))
        {
            ensure.Parameters.AddWithValue("@BusinessId", businessId); ensure.Parameters.AddWithValue("@Type", documentType);
            ensure.Parameters.AddWithValue("@Prefix", prefix); ensure.Parameters.AddWithValue("@Now", now);
            await ensure.ExecuteNonQueryAsync(ct);
        }
        Guid seriesId; string seriesCode; byte padding; long consecutive; long end;
        await using (var select = new SqlCommand("""
            SELECT TOP(1) ds.DocumentSeriesId,ds.SeriesCode,ds.Padding,
              COALESCE(c.NextConsecutive,ds.RangeStart),ds.RangeEnd
            FROM dbo.DocumentSeries ds WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.DocumentSeriesCursors c WITH(UPDLOCK,HOLDLOCK) ON c.DocumentSeriesId=ds.DocumentSeriesId
            WHERE ds.BusinessId=@BusinessId AND ds.DocumentType=@Type AND ds.DeviceId IS NULL AND ds.IsActive=1
            ORDER BY ds.DocumentSeriesId;
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("@BusinessId", businessId); select.Parameters.AddWithValue("@Type", documentType);
            await using var reader = await select.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("No existe una serie activa para el documento.");
            seriesId = reader.GetGuid(0); seriesCode = reader.GetString(1); padding = reader.GetByte(2);
            consecutive = reader.GetInt64(3); end = reader.GetInt64(4);
        }
        if (consecutive > end) throw new InvalidOperationException("La numeración del documento está agotada.");
        await using var update = new SqlCommand("""
            UPDATE dbo.DocumentSeriesCursors SET NextConsecutive=@Next,UpdatedAt=@Now WHERE DocumentSeriesId=@Id;
            IF @@ROWCOUNT=0 INSERT dbo.DocumentSeriesCursors(DocumentSeriesId,NextConsecutive,UpdatedAt) VALUES(@Id,@Next,@Now);
            """, connection, transaction);
        update.Parameters.AddWithValue("@Id", seriesId); update.Parameters.AddWithValue("@Next", consecutive + 1); update.Parameters.AddWithValue("@Now", now);
        await update.ExecuteNonQueryAsync(ct);
        return AuralyDocumentNumberAssignment.Create(seriesId, documentType, prefix, seriesCode, consecutive, padding);
    }

    public static async Task<long> AllocateSequenceAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, DateTimeOffset now, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.BusinessProcessingCursors WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId)
              INSERT dbo.BusinessProcessingCursors(BusinessId,LastAssignedSequence,LastCompletedSequence,UpdatedAt) VALUES(@BusinessId,0,0,@Now);
            UPDATE dbo.BusinessProcessingCursors WITH(UPDLOCK,HOLDLOCK)
              SET LastAssignedSequence=LastAssignedSequence+1,UpdatedAt=@Now
              OUTPUT inserted.LastAssignedSequence WHERE BusinessId=@BusinessId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId); command.Parameters.AddWithValue("@Now", now);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }
}
