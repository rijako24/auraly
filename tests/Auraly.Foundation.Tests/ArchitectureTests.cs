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
