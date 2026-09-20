using Auraly.BuildingBlocks.Domain.Documents;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public static class SqlOperationalDocumentAllocator
{
    public static async Task<AuralyDocumentNumberAssignment> AllocateNumberAsync(SqlConnection connection,
        SqlTransaction transaction, Guid businessId, string documentType, DateTimeOffset now, CancellationToken ct) =>
        (await AllocateNumbersAsync(connection, transaction, businessId, documentType, 1, now, ct))[0];

    public static async Task<IReadOnlyList<AuralyDocumentNumberAssignment>> AllocateNumbersAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId, string documentType,
        int count, DateTimeOffset now, CancellationToken ct)
    {
        if (count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(count));
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.DocumentSeries WITH(UPDLOCK,HOLDLOCK)
              WHERE BusinessId=@BusinessId AND DocumentType=@Type AND DeviceId IS NULL AND IsActive=1)
              INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
                Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
              VALUES(NEWID(),@BusinessId,NULL,@Type,@Prefix,N'00',8,1,99999999,0,1,@Now);
            DECLARE @Id uniqueidentifier,@SeriesCode nvarchar(8),@ActualPrefix nvarchar(8),
              @Padding tinyint,@Start bigint,@End bigint;
            SELECT TOP(1) @Id=ds.DocumentSeriesId,@SeriesCode=ds.SeriesCode,@ActualPrefix=ds.Prefix,
              @Padding=ds.Padding,@Start=COALESCE(c.NextConsecutive,ds.RangeStart),@End=ds.RangeEnd
            FROM dbo.DocumentSeries ds WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.DocumentSeriesCursors c WITH(UPDLOCK,HOLDLOCK) ON c.DocumentSeriesId=ds.DocumentSeriesId
            WHERE ds.BusinessId=@BusinessId AND ds.DocumentType=@Type AND ds.DeviceId IS NULL AND ds.IsActive=1
            ORDER BY ds.DocumentSeriesId;
            IF @Id IS NULL THROW 51730,N'No existe una serie activa para el documento.',1;
            IF @Start>@End-@Count+1 THROW 51731,N'La numeración del documento está agotada.',1;
            UPDATE dbo.DocumentSeriesCursors SET NextConsecutive=@Start+@Count,UpdatedAt=@Now WHERE DocumentSeriesId=@Id;
            IF @@ROWCOUNT=0 INSERT dbo.DocumentSeriesCursors(DocumentSeriesId,NextConsecutive,UpdatedAt)
              VALUES(@Id,@Start+@Count,@Now);
            SELECT @Id,@ActualPrefix,@SeriesCode,@Padding,@Start;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Type", documentType);
        command.Parameters.AddWithValue("@Prefix", AuralyDocumentTypes.DefaultPrefix(documentType));
        command.Parameters.AddWithValue("@Count", count);
        command.Parameters.AddWithValue("@Now", now);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Document numbering was not allocated.");
        var seriesId = reader.GetGuid(0); var prefix = reader.GetString(1); var seriesCode = reader.GetString(2);
        var padding = reader.GetByte(3); var start = reader.GetInt64(4);
        return Enumerable.Range(0, count).Select(offset => AuralyDocumentNumberAssignment.Create(
            seriesId, documentType, prefix, seriesCode, start + offset, padding)).ToArray();
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
