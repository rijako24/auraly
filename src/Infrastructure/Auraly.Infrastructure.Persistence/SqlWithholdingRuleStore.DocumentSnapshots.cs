using System.Data;
using System.Text.Json;
using Auraly.Commerce.Taxation.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlWithholdingRuleStore
{
    internal sealed record DocumentSnapshotSource(Guid DocumentId, string DocumentType,
        DateTimeOffset RecognizedAt, WithholdingCalculationSnapshot Snapshot);

    // Producers own calculation, authorization and their existing transaction.
    // This participant only persists the immutable tax facts in bounded batches.
    internal static async Task InsertDocumentSnapshotsAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, IReadOnlyList<DocumentSnapshotSource> sources, CancellationToken token)
    {
        if (transaction.Connection != connection || businessId == Guid.Empty || sources.Count == 0)
            throw new InvalidOperationException("Document tax snapshots require their business transaction.");
        foreach (var batch in sources.Chunk(100))
        {
            foreach (var source in batch)
            {
                if (source.DocumentId == Guid.Empty || source.DocumentType.Length is < 1 or > 32 ||
                    source.Snapshot.Lines.Any(line => line.RuleCode.Length > 32 || line.Name.Length > 120 ||
                        line.Kind.Length > 32 || line.BaseKind.Length > 32 || line.JurisdictionCode?.Length > 16))
                    throw new InvalidOperationException("A document tax snapshot exceeds its persisted identity or text limits.");
            }
            // Flatten once, before SQL. Nested applies over document/line JSON
            // caused oversized grants in the receipt path even for empty lines.
            var lines = batch.SelectMany(source => source.Snapshot.Lines.Select((line, index) => new
            {
                source.DocumentId, source.DocumentType, LineNumber = index + 1,
                line.RuleId, line.RuleVersion, line.RuleCode, line.Name, line.Kind, line.BaseKind,
                line.TaxableBase, line.Rate, line.Amount, line.JurisdictionCode, line.AccountId, line.ManualLineId
            })).ToArray();
            await using var command = new SqlCommand("""
                SET NOCOUNT ON;
                DECLARE @Headers TABLE(DocumentId uniqueidentifier,DocumentType nvarchar(32),
                  RecognizedAt datetimeoffset(7),GrossAmount decimal(19,4),WithholdingTotal decimal(19,4),
                  NetAmount decimal(19,4),Adjustments nvarchar(max),PRIMARY KEY(DocumentId,DocumentType));
                INSERT @Headers
                SELECT DocumentId,DocumentType,RecognizedAt,GrossAmount,WithholdingTotal,NetAmount,Adjustments
                FROM OPENJSON(@Snapshots) WITH(DocumentId uniqueidentifier,DocumentType nvarchar(32),
                  RecognizedAt datetimeoffset(7),GrossAmount decimal(19,4),WithholdingTotal decimal(19,4),
                  NetAmount decimal(19,4),Adjustments nvarchar(max) AS JSON);
                IF @@ROWCOUNT<>@SourceCount THROW 51735,'The document tax header batch is incomplete.',1;
                DECLARE @SnapshotLines TABLE(DocumentId uniqueidentifier,DocumentType nvarchar(32),LineNumber int,
                  RuleId uniqueidentifier,RuleVersion int,RuleCode nvarchar(32),Name nvarchar(120),
                  Kind nvarchar(32),BaseKind nvarchar(32),TaxableBase decimal(19,4),Rate decimal(9,6),
                  Amount decimal(19,4),JurisdictionCode nvarchar(16),AccountId uniqueidentifier,ManualLineId uniqueidentifier);
                INSERT @SnapshotLines
                SELECT DocumentId,DocumentType,LineNumber,RuleId,RuleVersion,RuleCode,Name,Kind,BaseKind,
                  TaxableBase,Rate,Amount,JurisdictionCode,AccountId,ManualLineId
                FROM OPENJSON(@Lines) WITH(DocumentId uniqueidentifier,DocumentType nvarchar(32),LineNumber int,
                  RuleId uniqueidentifier,RuleVersion int,RuleCode nvarchar(32),Name nvarchar(120),
                  Kind nvarchar(32),BaseKind nvarchar(32),TaxableBase decimal(19,4),Rate decimal(9,6),
                  Amount decimal(19,4),JurisdictionCode nvarchar(16),AccountId uniqueidentifier,ManualLineId uniqueidentifier);
                IF @@ROWCOUNT<>@LineCount OR EXISTS(SELECT 1 FROM @SnapshotLines line
                  WHERE NOT EXISTS(SELECT 1 FROM @Headers header
                    WHERE header.DocumentId=line.DocumentId AND header.DocumentType=line.DocumentType))
                  THROW 51735,'The document tax line batch is incomplete.',1;
                INSERT dbo.DocumentWithholdingSnapshots
                  (DocumentId,DocumentType,BusinessId,GrossAmount,WithholdingTotal,NetAmount,RecognizedAt,AdjustmentsJson)
                SELECT DocumentId,DocumentType,@BusinessId,GrossAmount,WithholdingTotal,NetAmount,RecognizedAt,Adjustments
                FROM @Headers OPTION(RECOMPILE);
                DECLARE @HeaderCount int=@@ROWCOUNT;
                INSERT dbo.DocumentWithholdingLines
                  (DocumentId,DocumentType,LineNumber,RuleId,RuleVersion,RuleCode,Name,Kind,
                   BaseKind,TaxableBase,Rate,Amount,JurisdictionCode,AccountId,ManualLineId)
                SELECT DocumentId,DocumentType,LineNumber,
                  NULLIF(RuleId,'00000000-0000-0000-0000-000000000000'),NULLIF(RuleVersion,0),
                  RuleCode,Name,Kind,BaseKind,TaxableBase,Rate,Amount,JurisdictionCode,AccountId,ManualLineId
                FROM @SnapshotLines OPTION(RECOMPILE);
                SELECT @HeaderCount,@@ROWCOUNT;
                """, connection, transaction);
            command.Parameters.AddWithValue("@BusinessId", businessId);
            command.Parameters.AddWithValue("@SourceCount", batch.Length);
            command.Parameters.AddWithValue("@LineCount", lines.Length);
            command.Parameters.Add("@Snapshots", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(batch.Select(source => new
            {
                source.DocumentId, source.DocumentType, source.RecognizedAt, source.Snapshot.GrossAmount,
                source.Snapshot.WithholdingTotal, source.Snapshot.NetAmount, source.Snapshot.Adjustments
            }));
            command.Parameters.Add("@Lines", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(lines);
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token) || reader.GetInt32(0) != batch.Length || reader.GetInt32(1) != lines.Length)
                throw new DBConcurrencyException("The complete document tax snapshot batch was not persisted.");
        }
    }
}
