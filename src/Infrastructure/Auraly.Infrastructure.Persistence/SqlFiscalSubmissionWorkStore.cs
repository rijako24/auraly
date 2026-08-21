using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Fiscal;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlFiscalSubmissionWorkStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids) : IFiscalSubmissionWorkStore
{
    public async Task<FiscalSubmissionWorkItem?> AcquireAsync(
        Guid businessId,
        Guid documentId,
        string workerId,
        DateTimeOffset acquiredAt,
        TimeSpan lease,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @Document TABLE(DocumentId uniqueidentifier NOT NULL);
            ;WITH candidate AS
            (
                SELECT p.DocumentId
                FROM dbo.FiscalDocumentProcesses p WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE p.DocumentId=@DocumentId AND p.BusinessId=@BusinessId
                  AND
                  (
                    p.Status=@PendingSubmission OR
                    (p.Status=@PendingResult AND p.TrackId IS NOT NULL) OR
                    p.Status=@RetryScheduled
                  )
                  AND (p.NextAttemptAt IS NULL OR p.NextAttemptAt<=@AcquiredAt)
                  AND (p.LockedAt IS NULL OR p.LockedAt<@LeaseExpiredAt)
            )
            UPDATE p
            SET LockedAt=@AcquiredAt,LockedBy=@WorkerId,
                AttemptCount=AttemptCount+1,UpdatedAt=@AcquiredAt
            OUTPUT inserted.DocumentId INTO @Document
            FROM dbo.FiscalDocumentProcesses p
            INNER JOIN candidate c ON c.DocumentId=p.DocumentId;

            SELECT p.DocumentId,p.BusinessId,d.FiscalNumber,c.TestSetId,a.Content,p.TrackId,
                   CONVERT(bit,CASE WHEN EXISTS(
                     SELECT 1 FROM dbo.FiscalTransmissionAttempts x
                     WHERE x.DocumentId=p.DocumentId
                       AND x.Operation IN (@SendTestOperation,@SendProductionOperation)
                       AND x.CompletedAt IS NULL
                   ) THEN 1 ELSE 0 END)
            FROM @Document selected
            INNER JOIN dbo.FiscalDocumentProcesses p ON p.DocumentId=selected.DocumentId
            INNER JOIN dbo.FiscalDocuments d ON d.DocumentId=p.DocumentId
            INNER JOIN dbo.FiscalIssuerConfigurations c
              ON c.FiscalIssuerConfigurationId=p.FiscalIssuerConfigurationId
            INNER JOIN dbo.FiscalArtifacts a
              ON a.DocumentId=p.DocumentId AND a.ArtifactType=@SignedXml
            WHERE p.LockedBy=@WorkerId;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@PendingSubmission", FiscalDocumentStatusCodes.PendingSubmission);
        command.Parameters.AddWithValue("@PendingResult", FiscalDocumentStatusCodes.PendingDianResult);
        command.Parameters.AddWithValue("@RetryScheduled", FiscalDocumentStatusCodes.RetryScheduled);
        command.Parameters.AddWithValue("@AcquiredAt", acquiredAt);
        command.Parameters.AddWithValue("@LeaseExpiredAt", acquiredAt - lease);
        command.Parameters.AddWithValue("@WorkerId", workerId);
        command.Parameters.AddWithValue("@SendTestOperation", DianOperationCodes.SendTestSet);
        command.Parameters.AddWithValue("@SendProductionOperation", DianOperationCodes.SendBillSync);
        command.Parameters.AddWithValue("@SignedXml", FiscalArtifactTypeCodes.SignedXml);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        FiscalSubmissionWorkItem? work = null;
        if (await reader.ReadAsync(cancellationToken))
        {
            work = new FiscalSubmissionWorkItem(
                reader.GetGuid(0),
                reader.GetGuid(1),
                workerId,
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                (byte[])reader[4],
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetBoolean(6));
        }
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return work;
    }

    public async Task<DateTimeOffset?> GetResumeAtAsync(
        Guid businessId,
        Guid documentId,
        DateTimeOffset checkedAt,
        TimeSpan lease,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.Status,p.TrackId,p.NextAttemptAt,p.LockedAt
            FROM dbo.FiscalDocumentProcesses p
            WHERE p.DocumentId=@DocumentId AND p.BusinessId=@BusinessId;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var status = reader.GetString(0);
        var hasTrackId = !reader.IsDBNull(1);
        var remainsEligible =
            status == FiscalDocumentStatusCodes.PendingSubmission ||
            status == FiscalDocumentStatusCodes.RetryScheduled ||
            (status == FiscalDocumentStatusCodes.PendingDianResult && hasTrackId);
        if (!remainsEligible) return null;

        var resumeAt = checkedAt.AddSeconds(1);
        if (!reader.IsDBNull(2))
        {
            var nextAttemptAt = reader.GetFieldValue<DateTimeOffset>(2);
            if (nextAttemptAt > resumeAt) resumeAt = nextAttemptAt;
        }
        if (!reader.IsDBNull(3))
        {
            var leaseExpiresAt = reader.GetFieldValue<DateTimeOffset>(3).Add(lease);
            if (leaseExpiresAt > resumeAt) resumeAt = leaseExpiresAt.AddSeconds(1);
        }
        return resumeAt;
    }

    public async Task<FiscalSubmissionAttempt> StartAttemptAsync(
        FiscalSubmissionWorkItem work,
        string operation,
        byte[]? submissionZip,
        byte[] sanitizedRequest,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        if (operation is not (DianOperationCodes.SendTestSet or
            DianOperationCodes.GetStatusZip or DianOperationCodes.SendBillSync))
            throw new ArgumentOutOfRangeException(nameof(operation));
        if (sanitizedRequest.Length == 0)
            throw new ArgumentException("A sanitized request record is required.", nameof(sanitizedRequest));

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await AssertLeaseAsync(connection, transaction, work, cancellationToken);

        var attemptNumber = await NextAttemptNumberAsync(
            connection, transaction, work.DocumentId, cancellationToken);
        byte[] zip;
        if (submissionZip is { Length: > 0 })
        {
            zip = submissionZip;
            await EnsureSubmissionZipAsync(
                connection, transaction, work, zip, startedAt, cancellationToken);
        }
        else
        {
            zip = await LoadSubmissionZipAsync(
                connection, transaction, work.DocumentId, cancellationToken);
        }

        var requestArtifactId = await InsertArtifactAsync(
            connection,
            transaction,
            work.DocumentId,
            FiscalArtifactTypeCodes.SanitizedSoapRequest,
            attemptNumber,
            sanitizedRequest,
            "application/json",
            $"{work.FiscalNumber}-{attemptNumber}-request.json",
            startedAt,
            cancellationToken);
        var attemptId = ids.NewId();
        var correlationId = $"{work.DocumentId:N}-{attemptId:N}";
        const string insert = """
            INSERT INTO dbo.FiscalTransmissionAttempts
            (FiscalTransmissionAttemptId,DocumentId,AttemptNumber,Operation,CorrelationId,
             TrackId,StartedAt,MayHaveReachedDian,RequestArtifactId)
            VALUES
            (@AttemptId,@DocumentId,@AttemptNumber,@Operation,@CorrelationId,
             @TrackId,@StartedAt,0,@RequestArtifactId);
            UPDATE dbo.FiscalDocumentProcesses
            SET CorrelationId=@CorrelationId,UpdatedAt=@StartedAt
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId AND LockedBy=@WorkerId;
            """;
        await using (var command = new SqlCommand(insert, connection, transaction))
        {
            command.Parameters.AddWithValue("@AttemptId", attemptId);
            command.Parameters.AddWithValue("@DocumentId", work.DocumentId);
            command.Parameters.AddWithValue("@AttemptNumber", attemptNumber);
            command.Parameters.AddWithValue("@Operation", operation);
            command.Parameters.AddWithValue("@CorrelationId", correlationId);
            command.Parameters.AddWithValue("@TrackId", Db(work.TrackId));
            command.Parameters.AddWithValue("@StartedAt", startedAt);
            command.Parameters.AddWithValue("@RequestArtifactId", requestArtifactId);
            command.Parameters.AddWithValue("@BusinessId", work.BusinessId);
            command.Parameters.AddWithValue("@WorkerId", work.WorkerId);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 2)
                throw new InvalidOperationException("The fiscal transmission attempt was not started durably.");
        }
        await transaction.CommitAsync(cancellationToken);
        var request = new DianSubmissionRequest(
            work.BusinessId,
            work.DocumentId,
            $"{work.FiscalNumber}.zip",
            zip,
            work.TestSetId!.Value.ToString("D"),
            work.TrackId,
            correlationId);
        return new FiscalSubmissionAttempt(
            attemptId,
            attemptNumber,
            operation,
            correlationId,
            request);
    }

    public async Task CompleteAttemptAsync(
        FiscalSubmissionWorkItem work,
        FiscalSubmissionAttempt attempt,
        DianSubmissionResult result,
        DateTimeOffset completedAt,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await AssertLeaseAsync(connection, transaction, work, cancellationToken);

        var responseContent = result.SanitizedResponse.Length == 0
            ? Encoding.UTF8.GetBytes("{}")
            : result.SanitizedResponse;
        var responseArtifactId = await InsertArtifactAsync(
            connection,
            transaction,
            work.DocumentId,
            FiscalArtifactTypeCodes.SanitizedSoapResponse,
            attempt.AttemptNumber,
            responseContent,
            "application/json",
            $"{work.FiscalNumber}-{attempt.AttemptNumber}-response.json",
            completedAt,
            cancellationToken);
        if (result.ApplicationResponse is { Length: > 0 })
        {
            await InsertArtifactAsync(
                connection,
                transaction,
                work.DocumentId,
                FiscalArtifactTypeCodes.DianApplicationResponse,
                attempt.AttemptNumber,
                result.ApplicationResponse,
                "application/xml",
                $"{work.FiscalNumber}-{attempt.AttemptNumber}-application-response.xml",
                completedAt,
                cancellationToken);
        }

        var status = Status(result, work.TrackId);
        var trackId = string.IsNullOrWhiteSpace(result.TrackId) ? work.TrackId : result.TrackId;
        var terminal = status is FiscalDocumentStatusCodes.DianAccepted
            or FiscalDocumentStatusCodes.DianRejected
            or FiscalDocumentStatusCodes.PermanentFailure;
        if (status == FiscalDocumentStatusCodes.PendingDianResult && string.IsNullOrWhiteSpace(trackId))
            nextAttemptAt = null;

        const string sql = """
            UPDATE dbo.FiscalTransmissionAttempts
            SET TrackId=@TrackId,CompletedAt=@CompletedAt,Disposition=@Disposition,
                StatusCode=@StatusCode,StatusDescription=@StatusDescription,
                MayHaveReachedDian=@MayHaveReachedDian,ResponseArtifactId=@ResponseArtifactId
            WHERE FiscalTransmissionAttemptId=@AttemptId AND DocumentId=@DocumentId
              AND CompletedAt IS NULL;

            UPDATE dbo.FiscalDocumentProcesses
            SET Status=@Status,TrackId=@TrackId,LastStatusCode=@StatusCode,
                LastStatusDescription=@StatusDescription,
                LastErrorCode=CASE WHEN @IsFailure=1 THEN @StatusCode ELSE NULL END,
                LastErrorMessage=CASE WHEN @IsFailure=1 THEN @StatusDescription ELSE NULL END,
                NextAttemptAt=@NextAttemptAt,LockedAt=NULL,LockedBy=NULL,
                SubmittedAt=CASE WHEN @WasSubmitted=1 THEN COALESCE(SubmittedAt,@CompletedAt) ELSE SubmittedAt END,
                CompletedAt=CASE WHEN @IsTerminal=1 THEN @CompletedAt ELSE NULL END,
                UpdatedAt=@CompletedAt
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId AND LockedBy=@WorkerId;

            UPDATE dbo.SalesDocuments
            SET FiscalStatus=@Status
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId;

            UPDATE dbo.FiscalDocuments
            SET FiscalStatus=@Status,UpdatedAt=@CompletedAt
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId;

            UPDATE dbo.SalesReturns SET FiscalStatus=@Status
            WHERE ReturnId=@DocumentId AND BusinessId=@BusinessId;
            """;
        await using (var command = new SqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("@AttemptId", attempt.AttemptId);
            command.Parameters.AddWithValue("@DocumentId", work.DocumentId);
            command.Parameters.AddWithValue("@BusinessId", work.BusinessId);
            command.Parameters.AddWithValue("@WorkerId", work.WorkerId);
            command.Parameters.AddWithValue("@TrackId", Db(trackId));
            command.Parameters.AddWithValue("@CompletedAt", completedAt);
            command.Parameters.AddWithValue("@Disposition", result.Disposition.ToString());
            command.Parameters.AddWithValue("@StatusCode", Db(Limit(result.StatusCode, 64)));
            command.Parameters.AddWithValue("@StatusDescription", Db(Limit(result.StatusDescription, 2000)));
            command.Parameters.AddWithValue("@MayHaveReachedDian", result.MayHaveReachedDian);
            command.Parameters.AddWithValue("@ResponseArtifactId", responseArtifactId);
            command.Parameters.AddWithValue("@Status", status);
            command.Parameters.AddWithValue("@NextAttemptAt", (object?)nextAttemptAt ?? DBNull.Value);
            command.Parameters.AddWithValue("@IsFailure", result.Disposition is
                DianSubmissionDisposition.TransientFailure or DianSubmissionDisposition.PermanentFailure);
            command.Parameters.AddWithValue("@WasSubmitted",
                (attempt.Operation is DianOperationCodes.SendTestSet or DianOperationCodes.SendBillSync) &&
                result.MayHaveReachedDian);
            command.Parameters.AddWithValue("@IsTerminal", terminal);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 4)
                throw new InvalidOperationException("The fiscal transmission result could not be committed.");
        }

        if (status is FiscalDocumentStatusCodes.DianAccepted or FiscalDocumentStatusCodes.DianRejected)
            await InsertStatusEventAsync(connection, transaction, work, status, result, completedAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task MarkSubmissionOutcomeUnknownAsync(
        FiscalSubmissionWorkItem work,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        ReleaseAsync(
            work,
            FiscalDocumentStatusCodes.PendingDianResult,
            "SubmissionOutcomeUnknown",
            "A previous DIAN send attempt ended without a durable response or TrackId. Automatic retransmission is blocked.",
            occurredAt,
            cancellationToken);

    public Task FailConfigurationAsync(
        FiscalSubmissionWorkItem work,
        string errorCode,
        string errorMessage,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        ReleaseAsync(
            work,
            FiscalDocumentStatusCodes.PermanentFailure,
            errorCode,
            errorMessage,
            occurredAt,
            cancellationToken);

    private async Task ReleaseAsync(
        FiscalSubmissionWorkItem work,
        string status,
        string errorCode,
        string errorMessage,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.FiscalDocumentProcesses
            SET Status=@Status,LastErrorCode=@ErrorCode,LastErrorMessage=@ErrorMessage,
                NextAttemptAt=NULL,LockedAt=NULL,LockedBy=NULL,
                CompletedAt=CASE WHEN @Status=@PermanentFailure THEN @OccurredAt ELSE NULL END,
                UpdatedAt=@OccurredAt
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId AND LockedBy=@WorkerId;
            UPDATE dbo.SalesDocuments SET FiscalStatus=@Status
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId;
            UPDATE dbo.FiscalDocuments SET FiscalStatus=@Status,UpdatedAt=@OccurredAt
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId;
            UPDATE dbo.SalesReturns SET FiscalStatus=@Status
            WHERE ReturnId=@DocumentId AND BusinessId=@BusinessId;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@ErrorCode", Limit(errorCode, 128));
        command.Parameters.AddWithValue("@ErrorMessage", Limit(errorMessage, 2000));
        command.Parameters.AddWithValue("@OccurredAt", occurredAt);
        command.Parameters.AddWithValue("@DocumentId", work.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", work.BusinessId);
        command.Parameters.AddWithValue("@WorkerId", work.WorkerId);
        command.Parameters.AddWithValue("@PermanentFailure", FiscalDocumentStatusCodes.PermanentFailure);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 3)
            throw new InvalidOperationException("The fiscal submission lease could not be released.");
    }

    private static string Status(DianSubmissionResult result, string? existingTrackId) =>
        result.Disposition switch
        {
            DianSubmissionDisposition.Received => FiscalDocumentStatusCodes.PendingDianResult,
            DianSubmissionDisposition.Pending => FiscalDocumentStatusCodes.PendingDianResult,
            DianSubmissionDisposition.Accepted => FiscalDocumentStatusCodes.DianAccepted,
            DianSubmissionDisposition.Rejected => FiscalDocumentStatusCodes.DianRejected,
            DianSubmissionDisposition.TransientFailure when
                result.MayHaveReachedDian &&
                string.IsNullOrWhiteSpace(result.TrackId) &&
                string.IsNullOrWhiteSpace(existingTrackId) =>
                FiscalDocumentStatusCodes.PendingDianResult,
            DianSubmissionDisposition.TransientFailure => FiscalDocumentStatusCodes.RetryScheduled,
            DianSubmissionDisposition.PermanentFailure => FiscalDocumentStatusCodes.PermanentFailure,
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };

    private async Task EnsureSubmissionZipAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        FiscalSubmissionWorkItem work,
        byte[] content,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        const string exists = """
            SELECT COUNT(*) FROM dbo.FiscalArtifacts WITH (UPDLOCK,HOLDLOCK)
            WHERE DocumentId=@DocumentId AND ArtifactType=@Type AND ArtifactVersion=1;
            """;
        await using var command = new SqlCommand(exists, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", work.DocumentId);
        command.Parameters.AddWithValue("@Type", FiscalArtifactTypeCodes.SubmissionZip);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 0)
            await InsertArtifactAsync(connection, transaction, work.DocumentId,
                FiscalArtifactTypeCodes.SubmissionZip, 1, content, "application/zip",
                $"{work.FiscalNumber}.zip", createdAt, cancellationToken);
    }

    private static async Task<byte[]> LoadSubmissionZipAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Content FROM dbo.FiscalArtifacts
            WHERE DocumentId=@DocumentId AND ArtifactType=@Type AND ArtifactVersion=1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@Type", FiscalArtifactTypeCodes.SubmissionZip);
        return await command.ExecuteScalarAsync(cancellationToken) as byte[]
            ?? throw new InvalidOperationException("The durable DIAN submission ZIP is missing.");
    }

    private async Task<Guid> InsertArtifactAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid documentId,
        string type,
        int version,
        byte[] content,
        string contentType,
        string fileName,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.FiscalArtifacts
            (FiscalArtifactId,DocumentId,ArtifactType,ArtifactVersion,Content,ContentHash,
             ContentType,FileName,CreatedAt)
            VALUES(@Id,@DocumentId,@Type,@Version,@Content,@Hash,@ContentType,@FileName,@CreatedAt);
            """;
        var id = ids.NewId();
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@Type", type);
        command.Parameters.AddWithValue("@Version", version);
        command.Parameters.Add("@Content", SqlDbType.VarBinary, -1).Value = content;
        command.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = SHA256.HashData(content);
        command.Parameters.AddWithValue("@ContentType", contentType);
        command.Parameters.AddWithValue("@FileName", fileName);
        command.Parameters.AddWithValue("@CreatedAt", createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    private async Task InsertStatusEventAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        FiscalSubmissionWorkItem work,
        string status,
        DianSubmissionResult result,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.ServerOutboxMessages
            (MessageId,DocumentId,DocumentType,Type,Payload,OccurredAt)
            SELECT @MessageId,@DocumentId,f.SourceDocumentType,@Type,@Payload,@OccurredAt
            FROM dbo.FiscalDocuments f
            WHERE f.DocumentId=@DocumentId AND f.BusinessId=@BusinessId
              AND NOT EXISTS(
              SELECT 1 FROM dbo.ServerOutboxMessages
              WHERE DocumentId=@DocumentId
                AND DocumentType=f.SourceDocumentType AND Type=@Type);
            """;
        var payload = JsonSerializer.Serialize(new
        {
            work.DocumentId,
            work.BusinessId,
            status,
            trackId = result.TrackId ?? work.TrackId,
            result.StatusCode,
            result.StatusDescription,
            occurredAt
        });
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@MessageId", ids.NewId());
        command.Parameters.AddWithValue("@DocumentId", work.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", work.BusinessId);
        command.Parameters.AddWithValue("@Type", $"FiscalDocument.{status}");
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.AddWithValue("@OccurredAt", occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AssertLeaseAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        FiscalSubmissionWorkItem work,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*) FROM dbo.FiscalDocumentProcesses WITH (UPDLOCK,HOLDLOCK)
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId AND LockedBy=@WorkerId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", work.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", work.BusinessId);
        command.Parameters.AddWithValue("@WorkerId", work.WorkerId);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new InvalidOperationException("The fiscal submission lease is no longer owned by this worker.");
    }

    private static async Task<int> NextAttemptNumberAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(MAX(AttemptNumber),0)+1
            FROM dbo.FiscalTransmissionAttempts WITH (UPDLOCK,HOLDLOCK)
            WHERE DocumentId=@DocumentId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static object Db(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static string? Limit(string? value, int length) =>
        value is null || value.Length <= length ? value : value[..length];
}
