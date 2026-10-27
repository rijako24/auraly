using System.Data;
using System.Text.Json;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlFiscalDocumentStore
{
    internal sealed record PurchaseSupportSource(Guid DocumentId, Guid BusinessId,
        string SourceDocumentType, string FiscalDocumentType, string AuralyNumber,
        string FiscalNumber, DateTimeOffset IssuedAt, Guid IssuerId, int Environment,
        string SnapshotJson, Guid? TestSetId = null);

    // Source producers keep their allocation, snapshot and transaction. Only
    // this participant inserts the support root, snapshot and existing process.
    // It neither allocates numbers nor applies financial effects or sends work.
    internal static async Task InsertPurchaseSupportSourcesAsync(SqlConnection connection,
        SqlTransaction transaction, Guid tenantId, IReadOnlyList<PurchaseSupportSource> sources,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (transaction.Connection != connection || tenantId == Guid.Empty)
            throw new InvalidOperationException("Purchase support acceptance requires its scoped transaction.");
        if (sources.Count == 0) return;
        await using var command = new SqlCommand("""
            SET NOCOUNT ON;
            DECLARE @Sources TABLE(DocumentId uniqueidentifier PRIMARY KEY,BusinessId uniqueidentifier,
              SourceDocumentType nvarchar(max),FiscalDocumentType nvarchar(max),AuralyNumber nvarchar(max),
              FiscalNumber nvarchar(max),IssuedAt datetimeoffset(7),IssuerId uniqueidentifier,
              Environment tinyint,SnapshotJson nvarchar(max),TestSetId uniqueidentifier);
            INSERT @Sources SELECT DocumentId,BusinessId,SourceDocumentType,FiscalDocumentType,AuralyNumber,
              FiscalNumber,IssuedAt,IssuerId,Environment,SnapshotJson,TestSetId
            FROM OPENJSON(@SourcesJson) WITH(DocumentId uniqueidentifier,BusinessId uniqueidentifier,
              SourceDocumentType nvarchar(max),FiscalDocumentType nvarchar(max),AuralyNumber nvarchar(max),
              FiscalNumber nvarchar(max),IssuedAt datetimeoffset(7),IssuerId uniqueidentifier,
              Environment tinyint,SnapshotJson nvarchar(max),TestSetId uniqueidentifier);
            IF (SELECT COUNT(*) FROM @Sources)<>@Count OR EXISTS(
              SELECT 1 FROM @Sources input LEFT JOIN dbo.Businesses business
                ON business.BusinessId=input.BusinessId AND business.TenantId=@TenantId
              LEFT JOIN dbo.FiscalIssuerConfigurations issuer
                ON issuer.FiscalIssuerConfigurationId=input.IssuerId AND issuer.BusinessId=input.BusinessId
              WHERE business.BusinessId IS NULL OR issuer.FiscalIssuerConfigurationId IS NULL
                OR input.FiscalDocumentType IS NULL
                OR input.FiscalDocumentType NOT IN(N'SupportDocument',N'SupportDocumentAdjustment')
                OR ISNULL(ISJSON(input.SnapshotJson),0)<>1)
              THROW 51735,'The purchase-support sources are invalid or outside their fiscal scope.',1;
            INSERT dbo.FiscalDocuments(DocumentId,BusinessId,SourceDocumentType,FiscalDocumentType,
              AuralyDocumentNumber,FiscalNumber,UniqueCodeType,UniqueCode,IssuedAt,FiscalStatus,CreatedAt,UpdatedAt)
            SELECT DocumentId,BusinessId,SourceDocumentType,FiscalDocumentType,AuralyNumber,FiscalNumber,
              N'CUDS',NULL,IssuedAt,@Status,@Now,@Now FROM @Sources;
            IF @@ROWCOUNT<>@Count THROW 51735,'The purchase-support roots were not persisted atomically.',1;
            INSERT fiscal.PurchaseSupportFiscalSnapshots(DocumentId,SnapshotJson,Environment,CreatedAt)
            SELECT DocumentId,SnapshotJson,Environment,@Now FROM @Sources;
            IF @@ROWCOUNT<>@Count THROW 51735,'The purchase-support snapshots were not persisted atomically.',1;
            INSERT dbo.FiscalDocumentProcesses(DocumentId,BusinessId,FiscalIssuerConfigurationId,TestSetId,Status,
              AttemptCount,NextAttemptAt,CreatedAt,UpdatedAt)
            SELECT DocumentId,BusinessId,IssuerId,TestSetId,@Status,0,@Now,@Now,@Now FROM @Sources;
            IF @@ROWCOUNT<>@Count THROW 51735,'The purchase-support processes were not persisted atomically.',1;
            """, connection, transaction);
        command.Parameters.Add("@SourcesJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(sources);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@Count", sources.Count);
        command.Parameters.AddWithValue("@Status", FiscalDocumentStatusCodes.PendingGeneration);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
