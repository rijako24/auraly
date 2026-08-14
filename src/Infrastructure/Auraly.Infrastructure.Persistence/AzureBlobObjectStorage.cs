using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace Auraly.Infrastructure.Persistence;

public sealed class AzureBlobObjectStorage(
    BlobServiceClient blobServiceClient,
    ILogger<AzureBlobObjectStorage> logger)
{
    public async Task<string> UploadImageAsync(
        Guid businessId,
        Stream imageStream,
        string objectName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var containerName = $"business-{businessId:N}".ToLowerInvariant();
        var container = blobServiceClient.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(
            PublicAccessType.Blob,
            cancellationToken: cancellationToken);
        var blob = container.GetBlobClient(objectName);
        await blob.UploadAsync(
            imageStream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            },
            cancellationToken);
        logger.LogInformation(
            "Dispatch evidence {ObjectName} stored for business {BusinessId}.",
            objectName,
            businessId);
        return blob.Uri.ToString();
    }
}
