namespace Auraly.Foundation.Tests;

public sealed class ReleasePackagingTests
{
    [Fact]
    public void Release_zip_uses_a_commit_specific_reproducible_timestamp()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "infrastructure",
            "azure",
            "New-AuralyRelease.ps1"));

        Assert.Contains("git -C $repoRoot show -s --format=%cI $commit", script,
            StringComparison.Ordinal);
        Assert.Contains("$entry.LastWriteTime = $commitTimestamp", script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("$entry.LastWriteTime = $normalizedTimestamp", script,
            StringComparison.Ordinal);
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
