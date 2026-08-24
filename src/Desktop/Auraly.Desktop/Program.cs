using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Auraly.Desktop;

internal sealed record DesktopConfiguration(
    string ApiUrl,
    int WebPort = 47830,
    int EdgePort = 47831);

internal static class Program
{
    private static readonly List<Process> Children = [];
    private static readonly CancellationTokenSource Shutdown = new();

    [STAThread]
    private static async Task Main()
    {
        using var mutex = new Mutex(true, "Local\\Auraly.Desktop", out var first);
        if (!first) return;

        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopChildren();
        Console.CancelKeyPress += (_, args) =>
        {
            args.Cancel = true;
            Shutdown.Cancel();
        };

        var root = AppContext.BaseDirectory;
        var configuration = LoadConfiguration(root);
        var data = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Auraly",
            "PosEdge");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(Path.Combine(data, "logs"));

        var sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var webOrigin = $"http://127.0.0.1:{configuration.WebPort}";
        var edgeOrigin = $"http://127.0.0.1:{configuration.EdgePort}";

        var web = StartWeb(root, configuration, webOrigin);
        Children.Add(web);
        var edge = StartEdge(root, configuration, data, sessionToken, webOrigin, edgeOrigin);
        Children.Add(edge);

        try
        {
            await WaitUntilReadyAsync($"{webOrigin}/pos", TimeSpan.FromSeconds(45), Shutdown.Token);
            await WaitUntilReadyAsync($"{edgeOrigin}/edge/v1/health", TimeSpan.FromSeconds(45), Shutdown.Token);

            var tokenFragment = Uri.EscapeDataString(sessionToken);
            var target = $"{webOrigin}/pos-launch#edgeToken={tokenFragment}";
            using var browser = LaunchApplicationWindow(target, data);

            while (!Shutdown.IsCancellationRequested && !browser.HasExited)
            {
                if (edge.HasExited)
                {
                    Children.Remove(edge);
                    await Task.Delay(1200, Shutdown.Token);
                    edge = StartEdge(
                        root, configuration, data, sessionToken, webOrigin, edgeOrigin);
                    Children.Add(edge);
                    await WaitUntilReadyAsync(
                        $"{edgeOrigin}/edge/v1/health",
                        TimeSpan.FromSeconds(30),
                        Shutdown.Token);
                }

                await Task.Delay(500, Shutdown.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            var log = Path.Combine(data, "logs", "desktop-error.log");
            await File.AppendAllTextAsync(
                log,
                $"{DateTimeOffset.Now:O} {exception}{Environment.NewLine}");
            ShowError($"Auraly no pudo iniciar. Revisa {log}");
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

    private static DesktopConfiguration LoadConfiguration(string root)
    {
        var path = Path.Combine(root, "desktopsettings.json");
        var value = JsonSerializer.Deserialize<DesktopConfiguration>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (value is null ||
            !Uri.TryCreate(value.ApiUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                "desktopsettings.json must contain a valid ApiUrl.");
        }

        return value;
    }

    private static Process StartWeb(
        string root,
        DesktopConfiguration configuration,
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
        info.Environment["NEXT_PUBLIC_API_URL"] =
            $"{configuration.ApiUrl.TrimEnd('/')}/api";
        return StartLogged(info, "web", origin);
    }

    private static Process StartEdge(
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

    private static async Task WaitUntilReadyAsync(
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

    private static Process LaunchApplicationWindow(string url, string data)
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft",
                "Edge",
                "Application",
                "msedge.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft",
                "Edge",
                "Application",
                "msedge.exe")
        };
        var edge = candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Microsoft Edge is required to open Auraly.");
        var profile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Auraly",
            "Desktop",
            "Browser");
        Directory.CreateDirectory(profile);
        return Process.Start(new ProcessStartInfo(edge)
        {
            UseShellExecute = false,
            Arguments =
                $"--user-data-dir=\"{profile}\" --no-first-run --no-default-browser-check " +
                $"--app=\"{url}\" --start-maximized"
        }) ?? throw new InvalidOperationException("Could not open Auraly.");
    }

    private static void StopChildren()
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
