using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Auraly.Desktop;

internal sealed record DesktopConfiguration(
    string ApiUrl,
    string Version = "0.0.0-dev",
    int WebPort = 47830,
    int EdgePort = 47831);

internal static class Program
{
    private static readonly List<Process> Children = [];
    private static readonly CancellationTokenSource Shutdown = new();

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "Local\\Auraly.Desktop", out var first);
        if (!first) return;

        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopChildren();
        Console.CancelKeyPress += (_, args) =>
        {
            args.Cancel = true;
            Shutdown.Cancel();
        };

        try
        {
            ApplicationConfiguration.Initialize();
            using var context = new AuralyDesktopApplicationContext(
                AppContext.BaseDirectory,
                LoadConfiguration(AppContext.BaseDirectory),
                Shutdown);
            Application.Run(context);
        }
        finally
        {
            StopChildren();
        }
    }

    internal static string LoadStartupMode(string path, bool enrolled)
    {
        if (!File.Exists(path)) return enrolled ? "enrolled" : "online";
        var mode = File.ReadAllText(path).Trim().ToLowerInvariant();
        return mode is "online" or "enrolled"
            ? mode
            : enrolled ? "enrolled" : "online";
    }

    internal static DesktopConfiguration LoadConfiguration(string root)
    {
        var path = Path.Combine(root, "desktopsettings.json");
        var value = JsonSerializer.Deserialize<DesktopConfiguration>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (value is null ||
            !Uri.TryCreate(value.ApiUrl, UriKind.Absolute, out _) ||
            !AuralyReleaseVersion.TryParse(value.Version, out _))
        {
            throw new InvalidOperationException(
                "desktopsettings.json must contain a valid ApiUrl and Version.");
        }

        return value;
    }

    internal static Process StartWeb(
        string root,
        DesktopConfiguration configuration,
        string data,
        string origin)
    {
        var info = new ProcessStartInfo(
            Path.Combine(root, "runtime", "node.exe"))
        {
            WorkingDirectory = Path.Combine(root, "web"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add(Path.Combine(root, "web", "server.js"));
        info.Environment["NODE_ENV"] = "production";
        info.Environment["AURALY_DESKTOP_LOCAL"] = "true";
        info.Environment["HOSTNAME"] = "127.0.0.1";
        info.Environment["PORT"] = configuration.WebPort.ToString();
        info.Environment["NODE_EXTRA_CA_CERTS"] =
            WriteSystemCertificateBundle(data);
        var backendUrl = $"{configuration.ApiUrl.TrimEnd('/')}/api";
        info.Environment["AURALY_API_URL"] = backendUrl;
        info.Environment["NEXT_PUBLIC_API_URL"] = backendUrl;
        return StartLogged(info, "web", origin);
    }

    internal static string WriteSystemCertificateBundle(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var destination = Path.Combine(dataDirectory, "windows-trusted-roots.pem");
        var certificates = new Dictionary<string, byte[]>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.Root, location);
            store.Open(OpenFlags.ReadOnly);
            foreach (var certificate in store.Certificates)
            {
                certificates.TryAdd(certificate.Thumbprint, certificate.RawData);
            }
        }

        if (certificates.Count == 0)
        {
            throw new InvalidOperationException(
                "Windows did not provide trusted certificates for the local web runtime.");
        }

        var pem = string.Join(
            Environment.NewLine,
            certificates.Values.Select(certificate =>
                PemEncoding.WriteString("CERTIFICATE", certificate)));
        File.WriteAllText(destination, pem);
        return destination;
    }

    internal static Process StartEdge(
        string root,
        DesktopConfiguration configuration,
        string data,
        string sessionToken,
        string webOrigin,
        string edgeOrigin)
    {
        var info = new ProcessStartInfo(
            Path.Combine(root, "edge", "Auraly.Pos.Edge.Host.exe"))
        {
            WorkingDirectory = Path.Combine(root, "edge"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.Environment["PosEdge__Url"] = edgeOrigin;
        info.Environment["PosEdge__SessionToken"] = sessionToken;
        info.Environment["PosEdge__AllowedOrigin"] = webOrigin;
        info.Environment["PosEdge__ServerUrl"] = configuration.ApiUrl.TrimEnd('/');
        info.Environment["PosEdge__DatabasePath"] = Path.Combine(data, "auraly-pos.db");
        info.Environment["PosEdge__SecretKeyDirectory"] = Path.Combine(data, "keys");
        info.Environment["PosEdge__EnrollmentPackagePath"] =
            Path.Combine(data, "enrollment.protected");
        info.Environment["PosEdge__StartupModePath"] =
            Path.Combine(data, "startup-mode");
        return StartLogged(info, "edge", edgeOrigin);
    }

    private static Process StartLogged(
        ProcessStartInfo info,
        string component,
        string identity)
    {
        var process = Process.Start(info)
            ?? throw new InvalidOperationException($"Could not start {identity}.");
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Auraly",
            "PosEdge",
            "logs");
        Directory.CreateDirectory(logDirectory);
        _ = PumpAsync(
            process.StandardOutput,
            Path.Combine(logDirectory, $"{component}.log"),
            Shutdown.Token);
        _ = PumpAsync(
            process.StandardError,
            Path.Combine(logDirectory, $"{component}-error.log"),
            Shutdown.Token);
        return process;
    }

    private static async Task PumpAsync(
        StreamReader reader,
        string target,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;
            await File.AppendAllTextAsync(
                target,
                $"{DateTimeOffset.Now:O} {line}{Environment.NewLine}",
                cancellationToken);
        }
    }

    internal static async Task WaitUntilReadyAsync(
        string url,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await client.GetAsync(url, cancellationToken);
                if (response.StatusCode != HttpStatusCode.ServiceUnavailable) return;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            await Task.Delay(300, cancellationToken);
        }

        throw new TimeoutException($"The local component did not answer at {url}.");
    }

    internal static void RegisterChild(Process process) => Children.Add(process);

    internal static void RemoveChild(Process process)
    {
        Children.Remove(process);
        process.Dispose();
    }

    internal static void StopChildren()
    {
        foreach (var process in Children.ToArray())
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
        Children.Clear();
    }

    private static void ShowError(string message)
    {
        _ = Process.Start(new ProcessStartInfo(
            "powershell.exe",
            $"-NoProfile -WindowStyle Hidden -Command \"Add-Type -AssemblyName PresentationFramework; [System.Windows.MessageBox]::Show('{message.Replace("'", "''")}', 'Auraly')\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }
}
