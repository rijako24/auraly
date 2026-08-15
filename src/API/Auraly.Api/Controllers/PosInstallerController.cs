using Azure;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/pos/installer")]
[Authorize]
public sealed class PosInstallerController(
    BlobServiceClient blobs,
    IConfiguration configuration,
    ILogger<PosInstallerController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Download(CancellationToken cancellationToken)
    {
        var containerName = configuration["Auraly:PosInstaller:Container"] ?? "downloads";
        var blobName = configuration["Auraly:PosInstaller:BlobName"] ?? "Auraly-POS-Setup.exe";
        var blob = blobs.GetBlobContainerClient(containerName).GetBlobClient(blobName);

        try
        {
            var download = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
            Response.Headers.CacheControl = "private, no-store";
            return File(
                download.Value.Content,
                download.Value.Details.ContentType ?? "application/vnd.microsoft.portable-executable",
                "Auraly-POS-Setup.exe",
                enableRangeProcessing: true);
        }
        catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status404NotFound)
        {
            logger.LogWarning("The POS installer blob {Container}/{Blob} was not found.", containerName, blobName);
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "El instalador todavía no está disponible",
                detail: "Auraly está preparando la versión más reciente. Intenta nuevamente en unos minutos.");
        }
    }
}