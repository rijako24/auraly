using System.Xml.Linq;

namespace Auraly.Foundation.Tests;

public sealed class ServerSliceArchitectureTests
{
    [Fact]
    public void Every_canonical_project_in_the_repository_is_connected_to_the_solution()
    {
        var root = FindRepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(root, "Auraly.Commerce.sln"));
        var projects = Directory
            .GetFiles(root, "Auraly.*.*proj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .ToArray();
        var missing = projects
            .Where(project => !solution.Contains(Path.GetFileName(project), StringComparison.Ordinal))
            .Select(project => Path.GetRelativePath(root, project))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"Canonical projects missing from Auraly.Commerce.sln:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    [Fact]
    public void Server_slice_projects_have_real_consumers()
    {
        var root = FindRepositoryRoot();
        var projects = Directory
            .GetFiles(root, "Auraly.*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .ToArray();
        var projectNames = projects.ToDictionary(
            path => Path.GetFileNameWithoutExtension(path),
            _ => 0,
            StringComparer.Ordinal);
        foreach (var project in projects)
        {
            foreach (var reference in XDocument.Load(project)
                         .Descendants("ProjectReference")
                         .Select(element => element.Attribute("Include")?.Value)
                         .Where(value => value is not null)
                         .Select(value => Path.GetFileNameWithoutExtension(value!)))
            {
                if (projectNames.ContainsKey(reference))
                {
                    projectNames[reference]++;
                }
            }
        }

        var desktopProject = "Auraly.Desktop";
        var packagingScript = Path.Combine(
            root,
            "scripts",
            "Build-AuralyPosInstaller.ps1");
        if (projectNames.ContainsKey(desktopProject) &&
            File.Exists(packagingScript) &&
            File.ReadAllText(packagingScript).Contains(
                @"src\Desktop\Auraly.Desktop\Auraly.Desktop.csproj",
                StringComparison.Ordinal))
        {
            projectNames[desktopProject]++;
        }
        var disconnected = projectNames
            .Where(entry =>
                entry.Value == 0 &&
                !entry.Key.EndsWith("Tests", StringComparison.Ordinal) &&
                !entry.Key.EndsWith(".Api", StringComparison.Ordinal))
            .Select(entry => entry.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            disconnected.Length == 0,
            $"Canonical runtime projects without a consumer:{Environment.NewLine}{string.Join(Environment.NewLine, disconnected)}");
    }

    [Fact]
    public void New_server_slice_contains_only_canonical_names_and_no_placeholders()
    {
        var root = FindRepositoryRoot();
        var scopes = new[]
        {
            Path.Combine(root, "src", "API", "Auraly.Api"),
            Path.Combine(root, "src", "Infrastructure", "Auraly.Infrastructure.Persistence"),
            Path.Combine(root, "src", "Pos", "Auraly.Pos.Edge.Infrastructure"),
            Path.Combine(root, "tests", "Auraly.ServerSlice.IntegrationTests")
        };
        var forbidden = new[]
        {
            string.Concat("Talk", "io"),
            string.Concat("Mi", "mos"),
            string.Concat("Xi", "on"),
            string.Concat("Pedidos", "OK")
        };
        var violations = new List<string>();
        foreach (var scope in scopes)
        {
            foreach (var file in Directory.GetFiles(scope, "*", SearchOption.AllDirectories)
                         .Where(path => !IsBuildOutput(path))
                         .Where(path =>
                             path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                             path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                             path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                         .Where(path => !path.EndsWith("packages.lock.json", StringComparison.OrdinalIgnoreCase)))
            {
                var text = File.ReadAllText(file);
                if (!IsAbsorbedPlatformSurface(file, root))
                {
                    foreach (var token in forbidden)
                    {
                        if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                        {
                            violations.Add($"{Path.GetRelativePath(root, file)}: {token}");
                        }
                    }
                }

                if (text.Contains("TODO", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("NotImplementedException", StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetRelativePath(root, file)}: placeholder");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Server-slice canonical/placeholder violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void Fiscal_processing_is_per_document_and_has_no_polling_timer()
    {
        var root = FindRepositoryRoot();
        var api = Path.Combine(root, "src", "API", "Auraly.Api");
        Assert.False(File.Exists(Path.Combine(api, "FiscalGenerationHostedService.cs")));
        Assert.False(File.Exists(Path.Combine(api, "FiscalSubmissionHostedService.cs")));
        var hosted = File.ReadAllText(Path.Combine(
            api,
            "ServiceBusFiscalProcessingHostedService.cs"));
        Assert.DoesNotContain("PeriodicTimer", hosted, StringComparison.Ordinal);
        Assert.Contains("CreateSessionProcessor", hosted, StringComparison.Ordinal);
        Assert.Contains("MaxConcurrentCallsPerSession = 1", hosted, StringComparison.Ordinal);

        var persistence = Path.Combine(
            root,
            "src",
            "Infrastructure",
            "Auraly.Infrastructure.Persistence");
        foreach (var file in new[]
                 {
                     "SqlFiscalGenerationWorkStore.cs",
                     "SqlFiscalSubmissionWorkStore.cs"
                 })
        {
            var source = File.ReadAllText(Path.Combine(persistence, file));
            Assert.DoesNotContain("TOP (1)", source, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("p.DocumentId=@DocumentId", source, StringComparison.Ordinal);
            Assert.Contains("p.BusinessId=@BusinessId", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Rabbit_transport_is_durable_ordered_and_has_no_sql_polling()
    {
        var root = FindRepositoryRoot();
        var api = Path.Combine(root, "src", "API", "Auraly.Api");
        var source = File.ReadAllText(Path.Combine(
            api, "RabbitMqProcessingTransport.cs"));
        var composition = File.ReadAllText(Path.Combine(api, "Program.cs"));

        Assert.Contains("RabbitMQ.Client", File.ReadAllText(Path.Combine(
            api, "Auraly.Api.csproj")), StringComparison.Ordinal);
        Assert.Contains("publisherConfirmationsEnabled: publisherConfirmations", source);
        Assert.Contains("publisherConfirmationTrackingEnabled: publisherConfirmations", source);
        Assert.Contains("Persistent = true", source);
        Assert.Contains("BasicQosAsync(0, 1, false", source);
        Assert.Contains("autoAck: false", source);
        Assert.Contains("MaximumAttempts = 5", source);
        Assert.Contains("BasicAckAsync", source);
        Assert.Contains("BasicNackAsync", source);
        Assert.DoesNotContain("PeriodicTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT ", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RetryDocumentAsync", source, StringComparison.Ordinal);
        Assert.Contains("includeScheduledRetries", source, StringComparison.Ordinal);
        Assert.Contains("\"RabbitMq\"", composition, StringComparison.Ordinal);
        Assert.Contains(
            "RabbitMqDocumentProcessingHostedService",
            composition,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_environment_overrides_remote_platform_configuration()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "src", "API", "Auraly.Api", "PlatformApiComposition.cs"));
        var remote = source.IndexOf(
            "AddAzureAppConfiguration", StringComparison.Ordinal);
        var environment = source.IndexOf(
            "AddEnvironmentVariables", remote, StringComparison.Ordinal);

        Assert.True(remote >= 0, "The canonical API must load Azure App Configuration.");
        Assert.True(environment > remote,
            "Runtime environment settings must override remote values so secrets cannot be shadowed.");
    }

    private static bool IsAbsorbedPlatformSurface(string path, string root)
    {
        var apiRoot = Path.Combine(root, "src", "API", "Auraly.Api");
        if (!path.StartsWith(apiRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = Path.GetRelativePath(apiRoot, path);
        return relative.Equals("Auraly.Api.csproj", StringComparison.OrdinalIgnoreCase) ||
               relative.Equals("PlatformApiComposition.cs", StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith($"Controllers{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith($"Authorization{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith($"Configuration{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith($"Extensions{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith($"Middleware{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }
    private static bool IsBuildOutput(string path) =>
        path.Contains(
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) ||
        path.Contains(
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Auraly.Commerce.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Auraly.Commerce.sln.");
    }
}

