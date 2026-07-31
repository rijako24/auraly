using System.Xml.Linq;

namespace Auraly.Foundation.Tests;

public sealed class ProjectConnectivityTests
{
    [Fact]
    public void Every_canonical_project_is_in_the_solution_and_has_a_consumer()
    {
        var repositoryRoot = FindRepositoryRoot();
        var solutionText = File.ReadAllText(Path.Combine(repositoryRoot, "Auraly.Commerce.sln"));
        var projectRoots = new[]
        {
            Path.Combine(repositoryRoot, "src", "BuildingBlocks"),
            Path.Combine(repositoryRoot, "src", "Fiscal"),
            Path.Combine(repositoryRoot, "src", "Modules"),
            Path.Combine(repositoryRoot, "tests", "Auraly.Foundation.Tests")
        };
        var projects = projectRoots
            .SelectMany(root => Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories))
            .ToArray();
        var projectNames = projects.ToDictionary(
            project => Path.GetFileNameWithoutExtension(project)!,
            StringComparer.Ordinal);
        var missingFromSolution = projects
            .Where(project => !solutionText.Contains(
                Path.GetFileName(project),
                StringComparison.Ordinal))
            .Select(project => Path.GetRelativePath(repositoryRoot, project))
            .ToArray();

        Assert.True(
            missingFromSolution.Length == 0,
            $"Projects missing from solution:{Environment.NewLine}{string.Join(Environment.NewLine, missingFromSolution)}");

        var incomingReferences = projectNames.Keys.ToDictionary(
            name => name,
            _ => 0,
            StringComparer.Ordinal);
        var consumerProjects = Directory
            .GetFiles(Path.Combine(repositoryRoot, "src"), "Auraly*.csproj", SearchOption.AllDirectories)
            .Concat(projects)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var project in consumerProjects)
        {
            var references = XDocument.Load(project)
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => value is not null)
                .Select(value => Path.GetFileNameWithoutExtension(value!)!);

            foreach (var reference in references.Where(incomingReferences.ContainsKey))
            {
                incomingReferences[reference]++;
            }
        }

        var disconnected = incomingReferences
            .Where(entry =>
                entry.Value == 0 &&
                !entry.Key.EndsWith(".Tests", StringComparison.Ordinal))
            .Select(entry => entry.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            disconnected.Length == 0,
            $"Canonical projects without a consumer:{Environment.NewLine}{string.Join(Environment.NewLine, disconnected)}");
    }

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
