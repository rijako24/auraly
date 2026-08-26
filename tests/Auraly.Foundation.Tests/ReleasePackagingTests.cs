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

    [Fact]
    public void Pos_installer_hash_tolerates_the_transient_iexpress_file_lock()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "scripts",
            "Build-AuralyPosInstaller.ps1"));

        Assert.Contains("for ($attempt = 1; $attempt -le 60; $attempt++)", script,
            StringComparison.Ordinal);
        Assert.Contains("catch [IO.IOException]", script, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash -LiteralPath $setup -Algorithm SHA256 -ErrorAction Stop",
            script, StringComparison.Ordinal);
    }

    [Fact]
    public void Pos_installer_supports_graphical_installation_and_silent_updates()
    {
        var repositoryRoot = FindRepositoryRoot();
        var installer = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "Build-AuralyPosInstaller.ps1"));
        var release = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "infrastructure",
            "azure",
            "New-AuralyRelease.ps1"));
        var updater = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Desktop",
            "Auraly.Desktop",
            "AuralyDesktopUpdater.cs"));

        Assert.Contains("[Windows.Forms.ProgressBar]::new()", installer,
            StringComparison.Ordinal);
        Assert.Contains("-WindowStyle Hidden", installer, StringComparison.Ordinal);
        Assert.Contains("UserQuietInstCmd=", installer, StringComparison.Ordinal);
        Assert.Contains("-Version $Version", release, StringComparison.Ordinal);
        Assert.Contains("SHA256.HashDataAsync", updater, StringComparison.Ordinal);
        Assert.Contains("ProcessStartInfo(installerPath, \"/Q\")", updater,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Environment_verification_retries_during_post_deploy_warmup()
    {
        var workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "deploy-auraly-release.yml"));

        Assert.Contains("for ($attempt = 1; $attempt -le 6; $attempt++)", workflow,
            StringComparison.Ordinal);
        Assert.Contains("Start-Sleep -Seconds 10", workflow, StringComparison.Ordinal);
        Assert.Contains("if (-not $healthy)", workflow, StringComparison.Ordinal);
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
