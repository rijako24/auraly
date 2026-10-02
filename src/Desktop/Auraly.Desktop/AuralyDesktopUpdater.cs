using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.WinForms;

namespace Auraly.Desktop;

internal sealed record AuralyUpdateRequest(
    string Type,
    string? DownloadUrl = null,
    string? Version = null,
    string? Sha256 = null);

internal sealed record AuralyUpdateStatus(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("progress")] int? Progress,
    [property: JsonPropertyName("message")] string Message);

internal sealed partial class AuralyDesktopUpdater(
    WebView2 browser,
    string webOrigin,
    DesktopConfiguration configuration,
    string dataDirectory,
    CancellationTokenSource shutdown)
{
    private const string DiscoveryMessageType = "auraly-pos-update-discovered";
    private const string DownloadMessageType = "auraly-pos-update-download";
    private const string RestartMessageType = "auraly-pos-update-restart";
    private const string ExitMessageType = "auraly-pos-exit";
    private const string StatusMessageType = "auraly-pos-update-status";
    private const string InstallerDownloadPath = "/api/commerce/v1/pos/installer/download";
    private int downloading;
    private AuralyUpdateRequest? availableUpdate;
    private AuralyPendingUpdate? pendingUpdate;
    private AuralyUpdateStatus? lastStatus;

    public void Start()
    {
        browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
    }

    public void Stop()
    {
        if (browser.CoreWebView2 is not null)
            browser.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
    }

    private async void OnWebMessageReceived(
        object? sender,
        Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!IsTrustedSource(args.Source, webOrigin)) return;
        AuralyUpdateRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<AuralyUpdateRequest>(
                args.WebMessageAsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return;
        }

        if (request is null) return;
        switch (request.Type)
        {
            case "auraly-pos-update-check":
                await RestorePendingUpdateAsync();
                break;
            case DiscoveryMessageType:
                HandleDiscovery(request);
                break;
            case DownloadMessageType:
                await DownloadAvailableUpdateAsync();
                break;
            case RestartMessageType:
                await RestartWithPendingUpdateAsync();
                break;
            case ExitMessageType:
                browser.FindForm()?.BeginInvoke(browser.FindForm()!.Close);
                break;
        }
    }

    internal static bool IsTrustedSource(string source, string origin) =>
        Uri.TryCreate(source, UriKind.Absolute, out var sourceUri) &&
        Uri.TryCreate(origin, UriKind.Absolute, out var originUri) &&
        string.Equals(sourceUri.GetLeftPart(UriPartial.Authority),
            originUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);

    private async Task RestorePendingUpdateAsync()
    {
        if (lastStatus is not null)
        {
            browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(lastStatus));
            return;
        }

        try
        {
            pendingUpdate = await Task.Run(() =>
                AuralyPendingUpdateStore.TryLoad(dataDirectory, configuration));
        }
        catch (Exception exception)
        {
            await LogFailureAsync(exception);
            PostStatus("check-error", null, null,
                "No fue posible recuperar la actualización descargada. Puedes consultar una nueva descarga.");
            return;
        }
        if (pendingUpdate is null)
        {
            PostStatus("idle", null, null, "No hay una actualización descargada.");
            return;
        }

        availableUpdate = new AuralyUpdateRequest(DiscoveryMessageType,
            InstallerDownloadPath, pendingUpdate.Version, pendingUpdate.Sha256);
        PostStatus("ready", pendingUpdate.Version, 100,
            "La actualización descargada está verificada y lista para instalar.");
    }

    private void HandleDiscovery(AuralyUpdateRequest request)
    {
        if (pendingUpdate is not null || downloading != 0) return;
        if (!ShouldOffer(request, configuration.Version))
        {
            availableUpdate = null;
            PostStatus("idle", null, null, "Auraly ya está actualizada.");
            return;
        }
        availableUpdate = request;
        PostStatus(
            "available",
            request.Version,
            null,
            $"La versión {request.Version} está lista para descargar.");
    }

    internal static bool ShouldOffer(AuralyUpdateRequest update, string installedVersion)
    {
        if (!string.Equals(update.Type, DiscoveryMessageType, StringComparison.Ordinal) ||
            !string.Equals(update.DownloadUrl, InstallerDownloadPath, StringComparison.Ordinal) ||
            update.Sha256 is null ||
            update.Sha256.Length != 64 ||
            !update.Sha256.All(Uri.IsHexDigit) ||
            update.Version is null ||
            !AuralyReleaseVersion.TryParse(installedVersion, out var current) ||
            !AuralyReleaseVersion.TryParse(update.Version, out var available))
        {
            return false;
        }

        return available.CompareTo(current) > 0;
    }

    private async Task DownloadAvailableUpdateAsync()
    {
        var update = availableUpdate;
        if (update is null || pendingUpdate is not null || Interlocked.Exchange(ref downloading, 1) != 0) return;

        try
        {
            var downloadedUpdate = await DownloadAsync(update, shutdown.Token);
            await AuralyPendingUpdateStore.SaveAsync(
                dataDirectory,
                downloadedUpdate,
                shutdown.Token);
            pendingUpdate = downloadedUpdate;
            PostStatus(
                "ready",
                pendingUpdate.Version,
                100,
                "Descarga verificada. Iniciando la instalación…");
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await LogFailureAsync(exception);
            PostStatus(
                "error",
                update.Version,
                null,
                "No fue posible descargar o verificar la actualización. Puedes intentarlo nuevamente.");
        }
        finally
        {
            Interlocked.Exchange(ref downloading, 0);
        }
    }

    private async Task<AuralyPendingUpdate> DownloadAsync(
        AuralyUpdateRequest update,
        CancellationToken cancellationToken)
    {
        PostStatus("downloading", update.Version, 0, "Descargando actualización…");
        var updateDirectory = Path.Combine(dataDirectory, "updates", update.Version!);
        Directory.CreateDirectory(updateDirectory);
        var installerPath = Path.Combine(updateDirectory, "Auraly-Setup.exe");
        var temporaryPath = installerPath + ".download";

        var cookies = await browser.CoreWebView2.CookieManager.GetCookiesAsync(webOrigin);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(webOrigin), update.DownloadUrl));
        if (cookies.Count > 0)
        {
            request.Headers.Add(
                "Cookie",
                string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}")));
        }

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var length = response.Content.Headers.ContentLength;
        var lastPercent = -1;
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[1024 * 128];
            long downloaded = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;
                var percent = length is > 0
                    ? (int)Math.Min(95, downloaded * 95 / length.Value)
                    : 35;
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    PostStatus("downloading", update.Version, percent,
                        $"Descargando actualización… {percent}%");
                }
            }
        }

        PostStatus("verifying", update.Version, 96, "Verificando integridad y firma…");
        await using (var installer = File.OpenRead(temporaryPath))
        {
            var actualHash = Convert.ToHexString(
                await SHA256.HashDataAsync(installer, cancellationToken));
            if (!string.Equals(actualHash, update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException("The downloaded installer hash is invalid.");
        }

        if (!string.IsNullOrWhiteSpace(configuration.PublisherCertificateThumbprint) &&
            !AuralyAuthenticodeVerifier.IsValid(
                temporaryPath,
                configuration.PublisherCertificateThumbprint))
        {
            throw new CryptographicException(
                "The downloaded installer does not have the expected Authenticode signature.");
        }

        File.Move(temporaryPath, installerPath, overwrite: true);
        return new AuralyPendingUpdate(
            update.Version!,
            update.Sha256!,
            installerPath,
            configuration.PublisherCertificateThumbprint,
            DateTimeOffset.UtcNow);
    }

    private async Task RestartWithPendingUpdateAsync()
    {
        var update = pendingUpdate;
        if (update is null || lastStatus?.Status == "restarting") return;
        PostStatus("restarting", update.Version, null, "Abriendo el instalador de la actualización…");
        try
        {
            if (!await Task.Run(() => AuralyPendingUpdateStore.IsValid(update, dataDirectory, configuration)))
            {
                pendingUpdate = null;
                PostStatus("error", update.Version, null,
                    "La descarga ya no es válida. Descarga nuevamente la actualización.");
                return;
            }
            AuralyPendingUpdateStore.StartInstaller(update.InstallerPath);
            shutdown.Cancel();
            System.Windows.Forms.Application.Exit();
        }
        catch (Exception exception)
        {
            await LogFailureAsync(exception);
            PostStatus("restart-error", update.Version, null,
                "No fue posible abrir el instalador. La descarga se conserva; puedes reintentar la instalación.");
        }
    }

    private void PostStatus(
        string status,
        string? version,
        int? progress,
        string message)
    {
        lastStatus = new AuralyUpdateStatus(
            StatusMessageType,
            status,
            version,
            progress,
            message);
        if (shutdown.IsCancellationRequested || browser.IsDisposed) return;
        browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(lastStatus));
    }

    private async Task LogFailureAsync(Exception exception)
    {
        var logDirectory = Path.Combine(dataDirectory, "logs");
        Directory.CreateDirectory(logDirectory);
        await File.AppendAllTextAsync(
            Path.Combine(logDirectory, "desktop-update-error.log"),
            $"{DateTimeOffset.Now:O} {exception}{Environment.NewLine}");
    }
}

internal sealed partial record AuralyReleaseVersion(
    int Major,
    int Minor,
    int Patch,
    string? Prerelease) : IComparable<AuralyReleaseVersion>
{
    [GeneratedRegex("^(?<major>0|[1-9]\\d*)\\.(?<minor>0|[1-9]\\d*)\\.(?<patch>0|[1-9]\\d*)(?:-(?<pre>[0-9A-Za-z.-]+))?$")]
    private static partial Regex Pattern();

    [GeneratedRegex("^(?<name>[A-Za-z-]+)(?<number>\\d+)$")]
    private static partial Regex NamedNumberPattern();

    public static bool TryParse(string value, out AuralyReleaseVersion version)
    {
        var match = Pattern().Match(value.Trim());
        if (!match.Success ||
            !int.TryParse(match.Groups["major"].Value, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, out var minor) ||
            !int.TryParse(match.Groups["patch"].Value, out var patch))
        {
            version = null!;
            return false;
        }

        version = new AuralyReleaseVersion(
            major,
            minor,
            patch,
            match.Groups["pre"].Success ? match.Groups["pre"].Value : null);
        return true;
    }

    public int CompareTo(AuralyReleaseVersion? other)
    {
        if (other is null) return 1;
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;
        if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null) return -1;

        var left = Prerelease.Split('.');
        var right = other.Prerelease.Split('.');
        for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            if (index >= left.Length) return -1;
            if (index >= right.Length) return 1;
            result = CompareIdentifier(left[index], right[index]);
            if (result != 0) return result;
        }
        return 0;
    }

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = int.TryParse(left, out var leftNumber);
        var rightNumeric = int.TryParse(right, out var rightNumber);
        if (leftNumeric && rightNumeric) return leftNumber.CompareTo(rightNumber);
        if (leftNumeric != rightNumeric) return leftNumeric ? -1 : 1;

        var leftNamed = NamedNumberPattern().Match(left);
        var rightNamed = NamedNumberPattern().Match(right);
        if (leftNamed.Success && rightNamed.Success &&
            string.Equals(
                leftNamed.Groups["name"].Value,
                rightNamed.Groups["name"].Value,
                StringComparison.OrdinalIgnoreCase))
        {
            return int.Parse(leftNamed.Groups["number"].Value)
                .CompareTo(int.Parse(rightNamed.Groups["number"].Value));
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
