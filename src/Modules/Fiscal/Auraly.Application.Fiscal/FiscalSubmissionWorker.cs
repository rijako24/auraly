using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Auraly.Contracts.Fiscal;

namespace Auraly.Application.Fiscal;

public sealed record FiscalSubmissionWorkItem(
    Guid DocumentId,
    Guid BusinessId,
    string WorkerId,
    string FiscalNumber,
    Guid? TestSetId,
    byte[] SignedXml,
    string? TrackId,
    bool HasUnresolvedSendAttempt);

public sealed record FiscalSubmissionAttempt(
    Guid AttemptId,
    int AttemptNumber,
    string Operation,
    string CorrelationId,
    DianSubmissionRequest Request);

public interface IFiscalSubmissionWorkStore
{
    Task<FiscalSubmissionWorkItem?> AcquireAsync(
        Guid businessId,
        Guid documentId,
        string workerId, DateTimeOffset acquiredAt, TimeSpan lease,
        CancellationToken cancellationToken);

    Task<DateTimeOffset?> GetResumeAtAsync(
        Guid businessId, Guid documentId, DateTimeOffset checkedAt,
        TimeSpan lease, CancellationToken cancellationToken);

    Task<FiscalSubmissionAttempt> StartAttemptAsync(
        FiscalSubmissionWorkItem work,
        string operation,
        byte[]? submissionZip,
        byte[] sanitizedRequest,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    Task CompleteAttemptAsync(
        FiscalSubmissionWorkItem work,
        FiscalSubmissionAttempt attempt,
        DianSubmissionResult result,
        DateTimeOffset completedAt,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken);

    Task MarkSubmissionOutcomeUnknownAsync(
        FiscalSubmissionWorkItem work,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);

    Task FailConfigurationAsync(
        FiscalSubmissionWorkItem work,
        string errorCode,
        string errorMessage,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);
}

public sealed class FiscalSubmissionPackageBuilder
{
    public byte[] Build(string fiscalNumber, byte[] signedXml)
    {
        if (string.IsNullOrWhiteSpace(fiscalNumber))
            throw new ArgumentException("A fiscal number is required.", nameof(fiscalNumber));
        if (signedXml.Length == 0)
            throw new ArgumentException("A signed fiscal XML is required.", nameof(signedXml));

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry($"{SafeName(fiscalNumber)}.xml", CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var content = entry.Open();
            content.Write(signedXml);
        }
        return output.ToArray();
    }

    public byte[] BuildSanitizedRequest(
        FiscalSubmissionWorkItem work,
        string operation,
        string correlationId,
        string fileName,
        byte[] zip)
    {
        var value = new
        {
            operation,
            work.DocumentId,
            work.BusinessId,
            fileName,
            contentSha256 = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant(),
            testSetId = work.TestSetId,
            work.TrackId,
            correlationId
        };
        return JsonSerializer.SerializeToUtf8Bytes(value);
    }

    private static string SafeName(string value)
    {
        var normalized = new string(value.Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .ToArray());
        return normalized.Length > 0
            ? normalized
            : throw new ArgumentException("The fiscal number has no file-safe characters.", nameof(value));
    }
}

public sealed record FiscalSubmissionProcessingResult(
    bool WorkFound,
    DateTimeOffset? NextAttemptAt);

public sealed class FiscalSubmissionWorker(
    IFiscalSubmissionWorkStore store,
    IDianHabilitationTransport transport,
    FiscalSubmissionPackageBuilder packages,
    TimeProvider timeProvider)
{
    public async Task<FiscalSubmissionProcessingResult> ProcessAsync(
        Guid businessId,
        Guid documentId,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        if (businessId == Guid.Empty || documentId == Guid.Empty)
            throw new ArgumentException("Business and document identifiers are required.");
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("A worker identity is required.", nameof(workerId));

        var work = await store.AcquireAsync(
            businessId,
            documentId,
            workerId.Trim(),
            timeProvider.GetUtcNow(),
            TimeSpan.FromMinutes(2),
            cancellationToken);
        if (work is null) return new(false, null);

        if (work.HasUnresolvedSendAttempt && string.IsNullOrWhiteSpace(work.TrackId))
        {
            await store.MarkSubmissionOutcomeUnknownAsync(
                work,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return new(true, null);
        }

        if (work.TestSetId is null)
        {
            await store.FailConfigurationAsync(
                work,
                "MissingTestSetId",
                "The active fiscal issuer configuration has no DIAN habilitation TestSetId.",
                timeProvider.GetUtcNow(),
                cancellationToken);
            return new(true, null);
        }

        var zip = packages.Build(work.FiscalNumber, work.SignedXml);
        var operation = string.IsNullOrWhiteSpace(work.TrackId)
            ? DianOperationCodes.SendTestSet
            : DianOperationCodes.GetStatusZip;
        var correlationId = $"{work.DocumentId:N}-{Guid.NewGuid():N}";
        var fileName = $"{work.FiscalNumber}.zip";
        var request = new DianSubmissionRequest(
            work.BusinessId,
            work.DocumentId,
            fileName,
            zip,
            work.TestSetId.Value.ToString("D"),
            work.TrackId,
            correlationId);
        var sanitizedRequest = packages.BuildSanitizedRequest(
            work,
            operation,
            correlationId,
            fileName,
            zip);
        var attempt = await store.StartAttemptAsync(
            work,
            operation,
            operation == DianOperationCodes.SendTestSet ? zip : null,
            sanitizedRequest,
            timeProvider.GetUtcNow(),
            cancellationToken);

        var result = operation == DianOperationCodes.SendTestSet
            ? await transport.SubmitTestSetAsync(attempt.Request, cancellationToken)
            : await transport.GetStatusZipAsync(attempt.Request, cancellationToken);
        var completedAt = timeProvider.GetUtcNow();
        var nextAttemptAt = NextAttempt(result, completedAt);
        await store.CompleteAttemptAsync(
            work,
            attempt,
            result,
            completedAt,
            nextAttemptAt,
            cancellationToken);
        return new(true, nextAttemptAt);
    }

    private static DateTimeOffset? NextAttempt(
        DianSubmissionResult result,
        DateTimeOffset now) =>
        result.Disposition switch
        {
            DianSubmissionDisposition.Received or DianSubmissionDisposition.Pending =>
                now.AddSeconds(5),
            DianSubmissionDisposition.TransientFailure when !result.MayHaveReachedDian =>
                now.AddSeconds(15),
            DianSubmissionDisposition.TransientFailure when
                result.MayHaveReachedDian && !string.IsNullOrWhiteSpace(result.TrackId) =>
                now.AddSeconds(15),
            _ => null
        };
}
