namespace Auraly.Foundation.Tests;

public sealed class PosArchitectureTests
{
    [Fact]
    public void Pos_edge_is_canonical_and_part_of_the_solution()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "Pos",
            "Auraly.Pos.Edge.Infrastructure",
            "Auraly.Pos.Edge.Infrastructure.csproj");
        var solution = File.ReadAllText(Path.Combine(repositoryRoot, "Auraly.Commerce.sln"));
        var forbidden = new[]
        {
            string.Concat("Talk", "io"),
            string.Concat("Mi", "mos"),
            string.Concat("Xi", "on")
        };
        var sourceFiles = Directory.GetFiles(
            Path.GetDirectoryName(projectPath)!,
            "*",
            SearchOption.AllDirectories)
            .Where(file =>
                file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));

        Assert.True(File.Exists(projectPath));
        Assert.Contains(Path.GetFileName(projectPath), solution, StringComparison.Ordinal);
        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain(
                forbidden,
                token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
        }
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
