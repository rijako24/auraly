using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Auraly.Contracts.Sales;

namespace Auraly.Pos.Edge.Infrastructure;

public enum PosSaleUploadDisposition
{
    Uploaded,
    FiscalIntegrityConflict,
    RetryableFailure,
    PermanentFailure
}

public sealed record PosSaleUploadAttempt(
    PosSaleUploadDisposition Disposition,
    PosSaleUploadResponse? Response,
    string? Error);

public interface IPosSaleUploadClient
{
    Task<PosSaleUploadAttempt> UploadAsync(
        PosSaleUploadRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public sealed class HttpPosSaleUploadClient(
    HttpClient httpClient,
    string deviceSecret)
    : IPosSaleUploadClient
{
    public async Task<PosSaleUploadAttempt> UploadAsync(
        PosSaleUploadRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceSecret))
        {
            throw new InvalidOperationException("The POS device secret is not configured.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/pos/v1/sales");
        message.Headers.Add("X-Auraly-Device-Id", request.DeviceId.ToString("D"));
        message.Headers.Add("X-Auraly-Device-Secret", deviceSecret);
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        message.Content = new StringContent(
            PosSaleContractSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");
        try
        {
            using var response = await httpClient.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            PosSaleUploadResponse? receipt = null;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    receipt = JsonSerializer.Deserialize<PosSaleUploadResponse>(
                        body,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));
                }
                catch (JsonException)
                {
                    receipt = null;
                }
            }

            if (response.IsSuccessStatusCode &&
                receipt is not null &&
                receipt.DocumentId == request.DocumentId &&
                receipt.Status is PosSaleRemoteStatuses.FiscalVerified
                    or PosSaleRemoteStatuses.CommercialAccepted
                    or PosSaleRemoteStatuses.AlreadyProcessed)
            {
                return new PosSaleUploadAttempt(
                    PosSaleUploadDisposition.Uploaded,
                    receipt,
                    null);
            }

            if (response.StatusCode == HttpStatusCode.Conflict &&
                receipt?.Status == PosSaleRemoteStatuses.FiscalIntegrityConflict)
            {
                return new PosSaleUploadAttempt(
                    PosSaleUploadDisposition.FiscalIntegrityConflict,
                    receipt,
                    receipt.Detail);
            }

            if (response.StatusCode is HttpStatusCode.RequestTimeout ||
                (int)response.StatusCode == 429 ||
                (int)response.StatusCode >= 500 ||
                (response.StatusCode == HttpStatusCode.Conflict &&
                 body.Contains("DocumentProcessingBusy", StringComparison.Ordinal)))
            {
                return new PosSaleUploadAttempt(
                    PosSaleUploadDisposition.RetryableFailure,
                    receipt,
                    $"Server returned HTTP {(int)response.StatusCode}.");
            }

            return new PosSaleUploadAttempt(
                PosSaleUploadDisposition.PermanentFailure,
                receipt,
                $"Server rejected the upload with HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PosSaleUploadAttempt(
                PosSaleUploadDisposition.RetryableFailure,
                null,
                "The upload timed out.");
        }
        catch (HttpRequestException exception)
        {
            return new PosSaleUploadAttempt(
                PosSaleUploadDisposition.RetryableFailure,
                null,
                exception.Message);
        }
    }
}

public sealed class PosEdgeOutboxUploader(
    PosEdgeSaleStore store,
    IPosSaleUploadClient client,
    TimeProvider timeProvider,
    IPosSynchronizationEventSink? events = null)
{
    private static readonly TimeSpan LeaseTimeout = TimeSpan.FromMinutes(2);

    public async Task<bool> UploadNextAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var item = await store.ClaimNextOutboxAsync(now, LeaseTimeout, cancellationToken);
        if (item is null)
        {
            return false;
        }

        PosSaleUploadRequest request;
        try
        {
            request = PosSaleContractSerializer.Deserialize(item.Payload);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            await store.MarkFailedPermanentAsync(
                item.MessageId,
                $"The durable payload cannot be read: {exception.Message}",
                cancellationToken);
            events?.Record("Error", "Sale", "Venta local rechazada antes de subir",
                $"{item.DocumentId.Value:D} · {exception.Message}");
            return true;
        }

        var attempt = await client.UploadAsync(
            request,
            request.DocumentId.ToString("D"),
            cancellationToken);
        switch (attempt.Disposition)
        {
            case PosSaleUploadDisposition.Uploaded:
                await store.MarkUploadedAsync(
                    item.MessageId,
                    attempt.Response
                        ?? throw new InvalidOperationException("A durable server receipt is required."),
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                break;
            case PosSaleUploadDisposition.FiscalIntegrityConflict:
                await store.MarkFiscalIntegrityConflictAsync(
                    item.MessageId,
                    attempt.Response
                        ?? throw new InvalidOperationException("A fiscal-conflict receipt is required."),
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                break;
            case PosSaleUploadDisposition.RetryableFailure:
                await store.ScheduleRetryAsync(
                    item.MessageId,
                    timeProvider.GetUtcNow() + Backoff(item.AttemptCount),
                    attempt.Error ?? "Transient upload failure.",
                    cancellationToken);
                break;
            case PosSaleUploadDisposition.PermanentFailure:
                await store.MarkFailedPermanentAsync(
                    item.MessageId,
                    attempt.Error ?? "Permanent upload failure.",
                    cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        events?.Record(
            attempt.Disposition is PosSaleUploadDisposition.Uploaded
                ? "Success"
                : attempt.Disposition is PosSaleUploadDisposition.RetryableFailure
                    ? "Warning"
                    : "Error",
            "Sale",
            attempt.Disposition is PosSaleUploadDisposition.Uploaded
                ? "Venta local subida"
                : "Venta local pendiente de sincronización",
            $"{request.DocumentNumber} · {request.DocumentId:D} · {attempt.Disposition}");

        return true;
    }

    private static TimeSpan Backoff(int attemptCount)
    {
        var exponent = Math.Clamp(attemptCount - 1, 0, 6);
        return TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, exponent)));
    }
}

