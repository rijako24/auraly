using Microsoft.Extensions.Options;

namespace Auraly.Api;

public sealed class PosInstallerOptions
{
    public const string SectionName = "PosInstaller";
    public string DownloadUrl { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;

    public bool TryCreateView(out PosInstallerView? view)
    {
        var url = DownloadUrl.Trim();
        var version = Version.Trim();
        var sha256 = Sha256.Trim().ToUpperInvariant();
        var valid = Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
                    parsed.Scheme == Uri.UriSchemeHttps &&
                    version is { Length: > 0 and <= 64 } &&
                    sha256.Length == 64 &&
                    sha256.All(Uri.IsHexDigit);
        view = valid
            ? new PosInstallerView(
                url,
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
    public static IEndpointRouteBuilder MapPosInstallerApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/pos/installer")
            .RequireAuthorization("pos.user");

        group.MapGet("", (IOptions<PosInstallerOptions> configured) =>
        {
            var options = configured.Value;
            if (!options.TryCreateView(out var installer))
                return Results.Problem(
                    "El instalador generico del POS aun no ha sido publicado.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "PosInstallerUnavailable");

            return Results.Ok(installer);
        });

        group.MapGet("/download", (IOptions<PosInstallerOptions> configured) =>
        {
            var options = configured.Value;
            return !options.TryCreateView(out var installer)
                ? Results.Problem(
                    "El instalador generico del POS aun no ha sido publicado.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "PosInstallerUnavailable")
                : Results.Redirect(installer!.DownloadUrl, permanent: false, preserveMethod: false);
        });

        return endpoints;
    }
}
