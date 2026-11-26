using Azure;
using Auraly.Platform.Application.Services;
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
                    !Uri.TryCreate(blobName, UriKind.Absolute, out _) &&
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
            IApplicationFileUrlResolver files,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var options = configured.Value;
            if (!options.TryCreateView(out _)) return Unavailable();

            var logger = loggerFactory.CreateLogger("PosInstaller");
            try
            {
                // The web host must not relay the executable through its API response.
                // Authorization still happens here; only this configured blob receives
                // the existing storage owner's short-lived, read-only signature.
                var url = await files.ResolveApplicationFileAsync(options.ContainerName.Trim(),
                    options.BlobName.Trim(), cancellationToken);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Redirect(url);
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
