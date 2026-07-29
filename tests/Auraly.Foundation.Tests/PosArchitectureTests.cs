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

    [Fact]
    public void Pos_scanner_remains_editable_while_the_local_edge_reconnects()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pagePath = Path.Combine(
            repositoryRoot,
            "admin",
            "src",
            "app",
            "(pos)",
            "pos",
            "page.tsx");
        var page = File.ReadAllText(pagePath);
        var scannerStart = page.IndexOf("id=\"pos-scanner\"", StringComparison.Ordinal);
        var scannerEnd = page.IndexOf("/>", scannerStart, StringComparison.Ordinal);

        Assert.True(scannerStart >= 0, "The POS scanner input was not found.");
        Assert.True(scannerEnd > scannerStart, "The POS scanner input is malformed.");

        var scanner = page[scannerStart..scannerEnd];
        Assert.Contains("disabled={busy}", scanner, StringComparison.Ordinal);
        Assert.DoesNotContain("edgeReady", scanner, StringComparison.Ordinal);
        Assert.Contains(
            "POS Edge no est\\u00e1 conectado. El c\\u00f3digo se conservar\\u00e1 para reintentar.",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "window.setInterval(() => void connect(), 3_000)",
            page,
            StringComparison.Ordinal);
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
