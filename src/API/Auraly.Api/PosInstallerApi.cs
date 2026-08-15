using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace Auraly.Api;

public sealed class PosInstallerOptions
{
    public const string SectionName = "PosInstaller";
    public string ContainerName { get; init; } = "downloads";
    public string BlobName { get; init; } = "Auraly-POS-Setup.exe";
    public string Version { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;

    public bool TryCreateView(out PosInstallerView? view)
    {
        var containerName = ContainerName.Trim();
        var blobName = BlobName.Trim();
        var version = Version.Trim();
        var sha256 = Sha256.Trim().ToUpperInvariant();
        var valid = containerName is { Length: > 0 and <= 63 } &&
                    blobName is { Length: > 0 and <= 1024 } &&
                    version is { Length: > 0 and <= 64 } &&
                    sha256.Length == 64 &&
                    sha256.All(Uri.IsHexDigit);
        view = valid
            ? new PosInstallerView(
                "/api/commerce/v1/pos/installer/download",
                version,
                sha256,
                TenantPreconfigured: false)
            : null;
        return valid;
    }
}

public sealed record PosInstallerView(
    string DownloadUrl,
    string Version,
    string Sha256,
    bool TenantPreconfigured);

public static class PosInstallerApi
{
    public static IEndpointRouteBuilder MapPosInstallerApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/pos/installer")
            .RequireAuthorization();

        group.MapGet("", (IOptions<PosInstallerOptions> configured) =>
        {
            return configured.Value.TryCreateView(out var installer)
                ? Results.Ok(installer)
                : Unavailable();
        });

        group.MapGet("/download", async (
            IOptions<PosInstallerOptions> configured,
            BlobServiceClient blobs,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var options = configured.Value;
            if (!options.TryCreateView(out _)) return Unavailable();

            var logger = loggerFactory.CreateLogger("PosInstaller");
            var blob = blobs.GetBlobContainerClient(options.ContainerName.Trim())
                .GetBlobClient(options.BlobName.Trim());
            try
            {
                var download = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
                return Results.Stream(
                    download.Value.Content,
                    download.Value.Details.ContentType ?? "application/vnd.microsoft.portable-executable",
                    "Auraly-POS-Setup.exe");
            }
            catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status404NotFound)
            {
                logger.LogWarning(
                    "The POS installer blob {Container}/{Blob} was not found.",
                    options.ContainerName,
                    options.BlobName);
                return Results.Problem(
                    "Auraly está preparando la versión más reciente. Intenta nuevamente en unos minutos.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "El instalador todavía no está disponible");
            }
        });

        return endpoints;
    }

    private static IResult Unavailable() => Results.Problem(
        "El instalador genérico del POS aún no ha sido publicado.",
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "PosInstallerUnavailable");
}