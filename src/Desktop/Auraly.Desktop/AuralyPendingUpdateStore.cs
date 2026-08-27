using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Auraly.Desktop;

internal sealed record AuralyPendingUpdate(
    string Version,
    string Sha256,
    string InstallerPath,
    string PublisherCertificateThumbprint,
    DateTimeOffset DownloadedAtUtc);

internal static class AuralyPendingUpdateStore
{
    private const string MarkerFileName = "pending-update.json";

    public static async Task SaveAsync(
        string dataDirectory,
        AuralyPendingUpdate update,
        CancellationToken cancellationToken)
    {
        var updatesDirectory = Path.Combine(dataDirectory, "updates");
        Directory.CreateDirectory(updatesDirectory);
        var markerPath = Path.Combine(updatesDirectory, MarkerFileName);
        var temporaryPath = markerPath + ".tmp";
        var json = JsonSerializer.Serialize(update);
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, markerPath, overwrite: true);
    }

    public static bool TryStartAtStartup(
        string dataDirectory,
        DesktopConfiguration configuration)
    {
        var markerPath = Path.Combine(dataDirectory, "updates", MarkerFileName);
        if (!File.Exists(markerPath)) return false;

        try
        {
            var update = JsonSerializer.Deserialize<AuralyPendingUpdate>(
                File.ReadAllText(markerPath));
            if (!IsValid(update, dataDirectory, configuration))
                throw new InvalidDataException("The pending update marker is invalid.");

            File.Delete(markerPath);
            StartInstaller(update!.InstallerPath);
            return true;
        }
        catch (Exception exception)
        {
            var logDirectory = Path.Combine(dataDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "desktop-update-error.log"),
                $"{DateTimeOffset.Now:O} Pending update rejected: {exception}{Environment.NewLine}");
            File.Delete(markerPath);
            return false;
        }
    }

    internal static bool IsValid(
        AuralyPendingUpdate? update,
        string dataDirectory,
        DesktopConfiguration configuration)
    {
        if (update is null ||
            update.Sha256.Length != 64 ||
            !update.Sha256.All(Uri.IsHexDigit) ||
            !AuralyReleaseVersion.TryParse(update.Version, out var pendingVersion) ||
            !AuralyReleaseVersion.TryParse(configuration.Version, out var currentVersion) ||
            pendingVersion.CompareTo(currentVersion) <= 0)
        {
            return false;
        }

        var updatesRoot = Path.GetFullPath(Path.Combine(dataDirectory, "updates"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var installerPath = Path.GetFullPath(update.InstallerPath);
        if (!installerPath.StartsWith(updatesRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                Path.GetFileName(installerPath),
                "Auraly-Setup.exe",
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(installerPath))
        {
            return false;
        }

        using var installer = File.OpenRead(installerPath);
        var actualHash = Convert.ToHexString(SHA256.HashData(installer));
        if (!string.Equals(actualHash, update.Sha256, StringComparison.OrdinalIgnoreCase))
            return false;

        var expectedThumbprint = configuration.PublisherCertificateThumbprint;
        if (string.IsNullOrWhiteSpace(expectedThumbprint)) return true;
        return string.Equals(
                   update.PublisherCertificateThumbprint,
                   expectedThumbprint,
                   StringComparison.OrdinalIgnoreCase) &&
               AuralyAuthenticodeVerifier.IsValid(installerPath, expectedThumbprint);
    }

    public static void StartInstaller(string installerPath)
    {
        _ = Process.Start(new ProcessStartInfo(
            installerPath,
            "/passive /norestart AuralyRelaunch=1")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath)
        }) ?? throw new InvalidOperationException("The updater could not start the installer.");
    }
}
