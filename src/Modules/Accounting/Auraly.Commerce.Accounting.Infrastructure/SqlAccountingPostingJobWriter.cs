using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Auraly.Commerce.Accounting.Infrastructure;

public enum AccountingJobRequirement
{
    AccountingEntryOnly = 0,
    PreserveCommercialEffects = 1,
    OpeningBalance = 2,
    ManualBookEntry = 3
}

public static class SqlAccountingPostingJobWriter
{
    public static Task InsertAsync(
        SqlConnection connection, SqlTransaction transaction,
        ConfirmedDocument document,
        DateTimeOffset occurredAt,
        IAuralyIdGenerator ids,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        AccountingJobRequirement requirement = AccountingJobRequirement.AccountingEntryOnly)
        => InsertSourcesAsync(connection, transaction,
            [new(document.TenantId.Value, document.BusinessId.Value, document.DocumentId.Value,
                document.DocumentType, null, occurredAt, ids.NewId())],
            timeProvider.GetUtcNow(), requirement, cancellationToken);

    public sealed record Source(Guid TenantId, Guid BusinessId, Guid DocumentId, string DocumentType,
        string? Payload, DateTimeOffset OccurredAt, Guid JobId);

    public static Task InsertSourceAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid tenantId, Guid businessId, Guid documentId, string documentType,
        string payload, DateTimeOffset occurredAt, IAuralyIdGenerator ids,
        TimeProvider timeProvider, CancellationToken cancellationToken,
        AccountingJobRequirement requirement = AccountingJobRequirement.AccountingEntryOnly,
        Guid? jobId = null) => InsertSourcesAsync(connection, transaction,
            [new(tenantId, businessId, documentId, documentType, payload, occurredAt, jobId ?? ids.NewId())],
            timeProvider.GetUtcNow(), requirement, cancellationToken);

    public static async Task<int> InsertSourcesAsync(SqlConnection connection, SqlTransaction transaction,
        IReadOnlyList<Source> sources, DateTimeOffset now, AccountingJobRequirement requirement,
        CancellationToken cancellationToken)
        => (await AcceptSourcesAsync(connection, transaction, sources, now, requirement, cancellationToken)).QueuedCount;

    internal readonly record struct AcceptanceResult(int QueuedCount, int NewSourceCount, string? SingleSourceStatus);

    internal static async Task<AcceptanceResult> AcceptSourcesAsync(SqlConnection connection, SqlTransaction transaction,
        IReadOnlyList<Source> sources, DateTimeOffset now, AccountingJobRequirement requirement,
        CancellationToken cancellationToken)
    {
        if (sources.Count is < 1 or > 100 || sources.Select(source => (source.DocumentId, source.DocumentType)).Distinct().Count() != sources.Count ||
            sources.Select(source => source.TenantId).Distinct().Count() != 1)
            throw new ArgumentException("Accounting sources must be a bounded batch from one tenant.", nameof(sources));
        await using var command = new SqlCommand("dbo.AccountingSourcesAccept", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("@TenantId", sources[0].TenantId);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.Add("@Requirement", SqlDbType.TinyInt).Value = (byte)requirement;
        command.Parameters.Add("@SourcesJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(sources.Select(source => new {
            source.DocumentId, source.DocumentType, source.BusinessId, source.Payload, source.OccurredAt, source.JobId,
            Hash = source.Payload is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.Payload))) }));
        var queuedCount = command.Parameters.Add("@QueuedCount", SqlDbType.Int);
        queuedCount.Direction = ParameterDirection.Output;
        var newSourceCount = command.Parameters.Add("@NewSourceCount", SqlDbType.Int);
        newSourceCount.Direction = ParameterDirection.Output;
        var status = command.Parameters.Add("@SingleSourceStatus", SqlDbType.NVarChar, 32);
        status.Direction = ParameterDirection.Output;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new((int)queuedCount.Value, (int)newSourceCount.Value, status.Value as string);
    }
}
