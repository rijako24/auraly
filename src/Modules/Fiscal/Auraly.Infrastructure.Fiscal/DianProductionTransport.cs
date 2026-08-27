using System.ServiceModel;
using System.Text.Json;
using Auraly.Contracts.Fiscal;

namespace Auraly.Infrastructure.Fiscal;

public sealed class DianProductionTransport(
    IDianProductionConfigurationProvider configurations,
    IDianWcfClientFactory clients) : IDianProductionTransport
{
    public async Task<DianSubmissionResult> SubmitBillSyncAsync(
        DianSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ZipContent.Length == 0 ||
            !request.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A DIAN ZIP payload is required.", nameof(request));
        try
        {
            var configuration = await configurations.ResolveAsync(
                request.BusinessId, cancellationToken);
            await using var client = await clients.CreateAsync(configuration, cancellationToken);
            var response = await client.SendBillSyncAsync(
                request.FileName, request.ZipContent, cancellationToken);
            var disposition = response.IsValid
                ? DianSubmissionDisposition.Accepted
                : DianSubmissionDisposition.Rejected;
            var applicationResponse = response.XmlBytes is { Length: > 0 }
                ? response.XmlBytes
                : response.XmlBase64Bytes;
            return new DianSubmissionResult(
                disposition,
                response.XmlDocumentKey,
                response.StatusCode,
                response.StatusDescription ?? response.StatusMessage,
                applicationResponse,
                JsonSerializer.SerializeToUtf8Bytes(response),
                MayHaveReachedDian: true);
        }
        catch (TimeoutException exception)
        {
            return Failure(exception, mayHaveReachedDian: true);
        }
        catch (CommunicationException exception)
        {
            return Failure(exception, mayHaveReachedDian: false);
        }
    }

    public async Task<DianSubmissionResult> SubmitPayrollSyncAsync(
        DianSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ZipContent.Length == 0)
            throw new ArgumentException("A DIAN payroll ZIP payload is required.", nameof(request));
        try
        {
            var configuration = await configurations.ResolveAsync(
                request.BusinessId, cancellationToken);
            await using var client = await clients.CreateAsync(configuration, cancellationToken);
            var response = await client.SendPayrollSyncAsync(request.ZipContent, cancellationToken);
            var disposition = response.IsValid
                ? DianSubmissionDisposition.Accepted
                : DianSubmissionDisposition.Rejected;
            var applicationResponse = response.XmlBytes is { Length: > 0 }
                ? response.XmlBytes
                : response.XmlBase64Bytes;
            return new DianSubmissionResult(
                disposition, response.XmlDocumentKey, response.StatusCode,
                response.StatusDescription ?? response.StatusMessage,
                applicationResponse, JsonSerializer.SerializeToUtf8Bytes(response),
                MayHaveReachedDian: true);
        }
        catch (TimeoutException exception)
        {
            return Failure(exception, mayHaveReachedDian: true);
        }
        catch (CommunicationException exception)
        {
            return Failure(exception, mayHaveReachedDian: false);
        }
    }

    private static DianSubmissionResult Failure(Exception exception, bool mayHaveReachedDian) =>
        new(DianSubmissionDisposition.TransientFailure, null, exception.GetType().Name,
            "The DIAN production transport failed transiently. The document number and unique code must be preserved.",
            null, [], mayHaveReachedDian);
}
