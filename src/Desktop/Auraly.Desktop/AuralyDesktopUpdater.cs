using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.WinForms;

namespace Auraly.Desktop;

internal sealed record AuralyUpdateMessage(
    string Type,
    string DownloadUrl,
    string Version,
    string Sha256);

internal sealed partial class AuralyDesktopUpdater(
    WebView2 browser,
    string webOrigin,
    string currentVersion,
    string dataDirectory,
    CancellationTokenSource shutdown)
{
    private const string UpdateMessageType = "auraly-pos-update";
    private const string InstallerDownloadPath = "/api/commerce/v1/pos/installer/download";
    private int updating;
    private string? pendingInstallerPath;

    public void Start()
    {
        browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
    }

    public void Stop()
    {
        if (browser.CoreWebView2 is not null)
            browser.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
    }

    public void InstallPendingWhenClosing()
    {
        InstallPendingUpdate();
    }

    private async void OnWebMessageReceived(
        object? sender,
        Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs args)
    {
        AuralyUpdateMessage? update;
        try
        {
            update = JsonSerializer.Deserialize<AuralyUpdateMessage>(
                args.WebMessageAsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return;
        }

        if (!ShouldInstall(update) || Interlocked.Exchange(ref updating, 1) != 0)
            return;

        try
        {
            await DownloadAndInstallAsync(update!, shutdown.Token);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await LogFailureAsync(exception);
            MessageBox.Show(
                browser.FindForm(),
                "No fue posible aplicar la actualización. Auraly POS seguirá funcionando y lo intentará de nuevo al abrirse.",
                "Actualización de Auraly POS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            Interlocked.Exchange(ref updating, 0);
        }
    }

    private bool ShouldInstall(AuralyUpdateMessage? update)
    {
        if (update is null ||
            !string.Equals(update.Type, UpdateMessageType, StringComparison.Ordinal) ||
            !string.Equals(update.DownloadUrl, InstallerDownloadPath, StringComparison.Ordinal) ||
            update.Sha256.Length != 64 ||
            !update.Sha256.All(Uri.IsHexDigit) ||
            !AuralyReleaseVersion.TryParse(currentVersion, out var current) ||
            !AuralyReleaseVersion.TryParse(update.Version, out var available))
        {
            return false;
        }

        return available.CompareTo(current) > 0;
    }

    private async Task DownloadAndInstallAsync(
        AuralyUpdateMessage update,
        CancellationToken cancellationToken)
    {
        using var progress = new AuralyUpdateProgressForm();
        progress.Show(browser.FindForm());
        progress.SetProgress("Descargando la nueva versión...", 2);

        var updateDirectory = Path.Combine(dataDirectory, "updates", update.Version);
        Directory.CreateDirectory(updateDirectory);
        var installerPath = Path.Combine(updateDirectory, "Auraly-POS-Setup.exe");
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
                    ? 5 + (int)Math.Min(78, downloaded * 78 / length.Value)
                    : 35;
                progress.SetProgress("Descargando la nueva versión...", percent);
            }
        }

        progress.SetProgress("Verificando la actualización...", 86);
        await using (var installer = File.OpenRead(temporaryPath))
        {
            var actualHash = Convert.ToHexString(
                await SHA256.HashDataAsync(installer, cancellationToken));
            if (!string.Equals(actualHash, update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException("The downloaded installer hash is invalid.");
        }

        File.Move(temporaryPath, installerPath, overwrite: true);
        progress.SetProgress("Actualización lista", 100);
        pendingInstallerPath = installerPath;
        progress.Close();

        var restart = MessageBox.Show(
            browser.FindForm(),
            "La nueva versión ya está lista. ¿Quieres reiniciar Auraly POS ahora? Si eliges Más tarde, se aplicará cuando cierres la aplicación.",
            "Actualización de Auraly POS",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button2);
        if (restart == DialogResult.Yes)
        {
            InstallPendingUpdate();
            shutdown.Cancel();
            Application.Exit();
        }
    }

    private void InstallPendingUpdate()
    {
        var installerPath = pendingInstallerPath;
        pendingInstallerPath = null;
        if (installerPath is not null)
            StartInstaller(installerPath);
    }

    private static void StartInstaller(string installerPath)
    {
        _ = Process.Start(new ProcessStartInfo(installerPath, "/Q")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath)
        }) ?? throw new InvalidOperationException("The updater could not start the installer.");
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

internal sealed class AuralyUpdateProgressForm : Form
{
    private readonly Label status = new();
    private readonly ProgressBar progress = new();

    public AuralyUpdateProgressForm()
    {
        Text = "Actualizando Auraly POS";
        ClientSize = new Size(520, 205);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ControlBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        BackColor = Color.FromArgb(7, 26, 29);

        var title = new Label
        {
            Text = "Auraly POS",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(28, 24)
        };
        status.Text = "Preparando la actualización...";
        status.ForeColor = Color.FromArgb(205, 228, 230);
        status.Font = new Font("Segoe UI", 10);
        status.AutoSize = true;
        status.Location = new Point(31, 82);
        progress.Style = ProgressBarStyle.Continuous;
        progress.Minimum = 0;
        progress.Maximum = 100;
        progress.Value = 2;
        progress.Size = new Size(456, 25);
        progress.Location = new Point(31, 119);
        Controls.AddRange([title, status, progress]);
    }

    public void SetProgress(string message, int percent)
    {
        status.Text = message;
        progress.Value = Math.Clamp(percent, 0, 100);
        Application.DoEvents();
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
