using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Auraly.Foundation.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Modules_do_not_reference_other_modules_domain_or_infrastructure()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projects = Directory.GetFiles(
            Path.Combine(repositoryRoot, "src", "Modules"),
            "*.csproj",
            SearchOption.AllDirectories);
        var violations = new List<string>();

        foreach (var project in projects)
        {
            var projectName = Path.GetFileNameWithoutExtension(project);
            var ownModule = projectName.Split('.').Last();
            var references = XDocument.Load(project)
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => value is not null)
                .Select(value => Path.GetFileNameWithoutExtension(value!));

            foreach (var reference in references)
            {
                if (!reference.StartsWith("Auraly.Domain.", StringComparison.Ordinal) &&
                    !reference.StartsWith("Auraly.Infrastructure.", StringComparison.Ordinal))
                {
                    continue;
                }

                var referencedModule = reference.Split('.').Last();
                if (!string.Equals(ownModule, referencedModule, StringComparison.Ordinal))
                {
                    violations.Add($"{projectName} -> {reference}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Forbidden module references:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void Domain_projects_do_not_reference_application_or_infrastructure()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projects = Directory.GetFiles(
            Path.Combine(repositoryRoot, "src"),
            "Auraly.Domain.*.csproj",
            SearchOption.AllDirectories);
        var violations = new List<string>();

        foreach (var project in projects)
        {
            var references = XDocument.Load(project)
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
                .Where(value =>
                    value.Contains("Application", StringComparison.Ordinal) ||
                    value.Contains("Infrastructure", StringComparison.Ordinal));

            violations.AddRange(references.Select(reference =>
                $"{Path.GetFileNameWithoutExtension(project)} -> {reference}"));
        }

        Assert.True(
            violations.Count == 0,
            $"Domain dependency violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void New_foundation_has_only_canonical_auraly_names()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roots = new[]
        {
            Path.Combine(repositoryRoot, "src", "BuildingBlocks"),
            Path.Combine(repositoryRoot, "src", "Fiscal"),
            Path.Combine(repositoryRoot, "src", "Modules"),
            Path.Combine(repositoryRoot, "tests", "Auraly.Foundation.Tests")
        };
        var forbidden = new[]
        {
            string.Concat("Talk", "io"),
            string.Concat("Mi", "mos"),
            string.Concat("Xi", "on"),
            string.Concat("Pedidos", "OK")
        };
        var violations = new List<string>();

        foreach (var root in roots)
        {
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                         .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                                        file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
            {
                var relativePath = Path.GetRelativePath(repositoryRoot, file);
                var text = File.ReadAllText(file);
                foreach (var token in forbidden)
                {
                    if (relativePath.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                        text.Contains(token, StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add($"{relativePath}: {token}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Legacy names found:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void Commerce_has_no_redundant_organization_location_level()
    {
        var root = FindRepositoryRoot();
        var scopes = new[]
        {
            Path.Combine(root, "src"),
            Path.Combine(root, "database", "Auraly.Database"),
            Path.Combine(root, "admin", "src")
        };
        var forbidden = new[]
        {
            string.Concat("Business", "Locations"),
            string.Concat("Location", "Id"),
            string.Concat("location", "Id")
        };
        var violations = scopes
            .Where(Directory.Exists)
            .SelectMany(scope => Directory.GetFiles(scope, "*", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.next{Path.DirectorySeparatorChar}"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Scripts{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".sqlproj", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => forbidden
                .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetRelativePath(root, path)}: {token}"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Redundant organization location references:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void Database_schema_does_not_use_triggers()
    {
        var repositoryRoot = FindRepositoryRoot();
        var databaseRoot = Path.Combine(repositoryRoot, "database", "Auraly.Database");
        var triggerDefinition = new Regex(@"\b(?:CREATE|ALTER)\s+TRIGGER\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var violations = Directory.GetFiles(databaseRoot, "*.sql", SearchOption.AllDirectories)
            .Where(path => triggerDefinition.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(violations.Length == 0,
            $"Los triggers de base de datos están prohibidos; usa servicios transaccionales y auditoría explícita:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void Database_postdeployment_batches_do_not_redeclare_variables()
    {
        var repositoryRoot = FindRepositoryRoot();
        var postDeploymentPath = Path.Combine(
            repositoryRoot,
            "database",
            "Auraly.Database",
            "Scripts",
            "PostDeployment.sql");
        var scriptsRoot = Path.GetDirectoryName(postDeploymentPath)!;
        var includeDirective = new Regex(
            @"^\s*:r\s+(.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var batchSeparator = new Regex(
            @"^\s*GO\s*(?:--.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var declaration = new Regex(
            @"\bDECLARE\s+(@[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var expandedLines = File.ReadLines(postDeploymentPath).SelectMany(line =>
        {
            var include = includeDirective.Match(line);
            if (!include.Success)
                return new[] { line };

            var relativePath = include.Groups[1].Value.Trim()
                .Replace(@".\", string.Empty, StringComparison.Ordinal);
            return File.ReadLines(Path.Combine(scriptsRoot, relativePath));
        });
        var declarations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var violations = new List<string>();
        var batchNumber = 1;

        foreach (var line in expandedLines)
        {
            if (batchSeparator.IsMatch(line))
            {
                declarations.Clear();
                batchNumber++;
                continue;
            }

            foreach (Match match in declaration.Matches(line))
            {
                var variable = match.Groups[1].Value;
                if (declarations.TryGetValue(variable, out var previousDeclaration))
                {
                    violations.Add(
                        $"Batch {batchNumber}: {variable} is declared by both '{previousDeclaration}' and '{line.Trim()}'.");
                    continue;
                }

                declarations[variable] = line.Trim();
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Post-deployment SQL variables must be isolated with GO:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void Azure_topology_provisions_distinct_operational_accounting_and_fiscal_queues()
    {
        var root = FindRepositoryRoot();
        var bicep = File.ReadAllText(Path.Combine(root, "infrastructure", "azure", "main.bicep"));
        var readiness = File.ReadAllText(Path.Combine(root, "infrastructure", "azure", "Test-AuralyDeploymentReadiness.ps1"));
        var queues = new[]
        {
            (Name: "auraly-document-processing", Setting: "Auraly__DocumentProcessing__ServiceBus__QueueName"),
            (Name: "auraly-accounting-processing", Setting: "Auraly__Accounting__ServiceBus__QueueName"),
            (Name: "auraly-fiscal-processing", Setting: "Auraly__Fiscal__ServiceBus__QueueName")
        };

        foreach (var queue in queues)
        {
            Assert.Contains(queue.Name, bicep, StringComparison.Ordinal);
            Assert.Contains(queue.Setting, bicep, StringComparison.Ordinal);
            Assert.Contains(queue.Name, readiness, StringComparison.Ordinal);
            Assert.Contains(queue.Setting, readiness, StringComparison.Ordinal);
        }
    }
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Auraly repository root.");
    }
}
