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
        Assert.Contains("\"ar-$PID-$temporaryToken\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("auraly-release-$Version-", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Pos_release_requires_an_authenticode_certificate()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "scripts",
            "Build-AuralyPosInstaller.ps1"));

        Assert.Contains("SigningCertificateThumbprint es obligatorio", script,
            StringComparison.Ordinal);
        Assert.Contains("Sign-AuralyWindowsArtifact.ps1", script, StringComparison.Ordinal);
        Assert.Contains("if (-not $isSelfSigned)", File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "scripts", "Sign-AuralyWindowsArtifact.ps1")),
            StringComparison.Ordinal);
        Assert.Contains("Get-AuthenticodeSignature", File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "scripts", "Sign-AuralyWindowsArtifact.ps1")),
            StringComparison.Ordinal);

        var workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), ".github", "workflows", "deploy-auraly-release.yml"));
        Assert.DoesNotContain("TrustedPublisher", workflow,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Import-Certificate", workflow, StringComparison.Ordinal);
        Assert.Contains("@('NotTrusted', 'UnknownError')", script,
            StringComparison.Ordinal);
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

        Assert.Contains("Auraly.Pos.Bundle.wixproj", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("IExpress", installer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-WindowStyle Hidden", installer, StringComparison.Ordinal);
        Assert.Contains("-Version $Version", release, StringComparison.Ordinal);
        Assert.Contains("-RequireSignature", release, StringComparison.Ordinal);
        Assert.Contains("SHA256.HashDataAsync", updater, StringComparison.Ordinal);
        Assert.Contains("auraly-pos-update-download", updater,
            StringComparison.Ordinal);
        Assert.Contains("/passive /norestart AuralyRelaunch=1",
            File.ReadAllText(Path.Combine(
                repositoryRoot,
                "src",
                "Desktop",
                "Auraly.Desktop",
                "AuralyPendingUpdateStore.cs")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Immutable_release_contains_environment_specific_signed_pos_installers()
    {
        var repositoryRoot = FindRepositoryRoot();
        var release = File.ReadAllText(Path.Combine(
            repositoryRoot, "infrastructure", "azure", "New-AuralyRelease.ps1"));
        var pipeline = File.ReadAllText(Path.Combine(
            repositoryRoot, "infrastructure", "azure", "Publish-AuralyReleasePipeline.ps1"));
        var workflow = File.ReadAllText(Path.Combine(
            repositoryRoot, ".github", "workflows", "deploy-auraly-release.yml"));

        Assert.Contains("[string]$ProdPosApiUrl", release, StringComparison.Ordinal);
        Assert.Contains("auraly-pos-prod-$Version.exe", release, StringComparison.Ordinal);
        Assert.Contains("$Environment -eq 'prod'", pipeline, StringComparison.Ordinal);
        Assert.Contains("auraly-pos-prod-$ReleaseVersion.exe", pipeline, StringComparison.Ordinal);
        Assert.Contains("-ProdPosApiUrl 'https://api-auraly-prod-7sov4nxc.azurewebsites.net'", workflow,
            StringComparison.Ordinal);
        var installerBuild = File.ReadAllText(Path.Combine(
            repositoryRoot, "scripts", "Build-AuralyPosInstaller.ps1"));
        Assert.Contains("-p:IntermediateOutputPath=$msiIntermediate\\", installerBuild,
            StringComparison.Ordinal);
        Assert.Contains("-p:IntermediateOutputPath=$bundleIntermediate\\", installerBuild,
            StringComparison.Ordinal);
        Assert.Contains("Join-Path $msiBuild 'Auraly.Pos.Setup.msi'", installerBuild,
            StringComparison.Ordinal);
        Assert.Contains("Join-Path $bundleBuild 'Auraly.Pos.Bundle.exe'", installerBuild,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Get-ChildItem -LiteralPath $msiBuild -Recurse", installerBuild,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Get-ChildItem -LiteralPath $bundleBuild -Recurse", installerBuild,
            StringComparison.Ordinal);
        Assert.Contains("$posInstallerHashes['dev'] -eq $posInstallerHashes['prod']", release,
            StringComparison.Ordinal);
        Assert.Contains("Los instaladores DEV y PROD resultaron idénticos", release,
            StringComparison.Ordinal);
        Assert.Contains("posInstallers = if ($PosApiUrl)", release,
            StringComparison.Ordinal);

        var infrastructureDeployment = File.ReadAllText(Path.Combine(
            repositoryRoot, "infrastructure", "azure", "Deploy-Auraly.ps1"));
        Assert.Contains("$posInstallerSha256ByEnvironment[$environment]", infrastructureDeployment,
            StringComparison.Ordinal);
        Assert.Contains("DEV y PROD usan instaladores distintos", infrastructureDeployment,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Pos_installer_retries_only_the_known_transient_WiX_pipe_failure()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "scripts",
            "Build-AuralyPosInstaller.ps1"));

        Assert.Contains("$maximumAttempts = 2", script, StringComparison.Ordinal);
        Assert.Contains(
            "WIX0001:\\s+System\\.IO\\.IOException:\\s+The pipe is being closed\\.",
            script,
            StringComparison.Ordinal);
        Assert.Contains("throw $failureMessage", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Select-Object -Single", script, StringComparison.Ordinal);

        var workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), ".github", "workflows", "deploy-auraly-release.yml"));
        Assert.Contains("runs-on: windows-2022", workflow, StringComparison.Ordinal);
        Assert.Contains("wixtoolset/wix#701", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Desktop_children_are_bound_to_a_kill_on_close_windows_job()
    {
        var repositoryRoot = FindRepositoryRoot();
        var desktopDirectory = Path.Combine(
            repositoryRoot, "src", "Desktop", "Auraly.Desktop");
        var program = File.ReadAllText(Path.Combine(desktopDirectory, "Program.cs"));
        var processJob = File.ReadAllText(Path.Combine(
            desktopDirectory, "AuralyChildProcessJob.cs"));
        var applicationContext = File.ReadAllText(Path.Combine(
            desktopDirectory, "AuralyDesktopApplicationContext.cs"));

        Assert.Contains("AuralyChildProcessJob ChildProcessJob", program,
            StringComparison.Ordinal);
        Assert.Contains("ChildProcessJob.Add(process)", program,
            StringComparison.Ordinal);
        Assert.Contains("ChildProcessJob.Dispose()", program,
            StringComparison.Ordinal);
        Assert.Contains("JobObjectLimitKillOnJobClose", processJob,
            StringComparison.Ordinal);
        Assert.Contains("AssignProcessToJobObject", processJob,
            StringComparison.Ordinal);
        Assert.Contains("process.SafeHandle", processJob,
            StringComparison.Ordinal);
        Assert.Contains("Program.StopStaleLocalComponents(root)", applicationContext,
            StringComparison.Ordinal);
        Assert.Contains("Path.Combine(root, \"runtime\", \"node.exe\")", program,
            StringComparison.Ordinal);
        Assert.Contains("Path.GetFullPath(actualPath)", program,
            StringComparison.Ordinal);
        Assert.Contains("process.Kill(entireProcessTree: true)", program,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_projects_are_visible_but_only_built_by_the_packaging_pipeline()
    {
        var solution = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Auraly.Commerce.sln"));

        Assert.Contains("Auraly.Pos.Setup.wixproj", solution, StringComparison.Ordinal);
        Assert.Contains("Auraly.Pos.Bundle.wixproj", solution, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "{8FC786E1-BBF9-4C18-9655-00A80624A4B8}.Release|Any CPU.Build.0",
            solution,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "{00981DD1-1677-4718-82B5-7C34541FADF2}.Release|Any CPU.Build.0",
            solution,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Pos_installer_uses_the_single_Auraly_name_and_official_icon()
    {
        var repositoryRoot = FindRepositoryRoot();
        var package = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "Installer", "Auraly.Pos.Setup", "Package.wxs"));
        var bundle = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "Installer", "Auraly.Pos.Bundle", "Bundle.wxs"));
        var bundleTheme = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "Installer", "Auraly.Pos.Bundle", "AuralyTheme.xml"));
        var desktopProject = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "Desktop", "Auraly.Desktop", "Auraly.Desktop.csproj"));

        Assert.Contains("Package Name=\"Auraly\"", package, StringComparison.Ordinal);
        Assert.Contains("Shortcut Id=\"AuralyPosDesktopShortcut\"", package,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            package.Split("Icon=\"AuralyIcon.ico\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("Bundle Name=\"Auraly\"", bundle, StringComparison.Ordinal);
        Assert.Contains("Theme=\"hyperlinkSidebarLicense\"", bundle,
            StringComparison.Ordinal);
        Assert.Contains("ThemeFile=\"AuralyTheme.xml\"", bundle,
            StringComparison.Ordinal);
        Assert.Contains("LocalizationFile=\"AuralyTheme.wxl\"", bundle,
            StringComparison.Ordinal);
        Assert.DoesNotContain("LogoSideFile=", bundle, StringComparison.Ordinal);
        Assert.Contains("admin\\public\\brand\\auraly-mark.png", bundle,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AuralyIcon-v5.png", bundle, StringComparison.Ordinal);
        Assert.Contains("<Window Width=\"520\"", bundleTheme, StringComparison.Ordinal);
        Assert.Contains("Auraly.ico", bundle, StringComparison.Ordinal);
        Assert.Contains("<ApplicationIcon>Assets\\Auraly.ico</ApplicationIcon>",
            desktopProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"Auraly POS\"", package, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"Auraly Commerce\"", bundle, StringComparison.Ordinal);
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

    [Fact]
    public void Release_deploys_only_the_components_recorded_in_the_immutable_manifest()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(
            repositoryRoot, ".github", "workflows", "deploy-auraly-release.yml"));
        var deployment = File.ReadAllText(Path.Combine(
            repositoryRoot, "infrastructure", "azure", "Publish-AuralyReleasePipeline.ps1"));
        var scopeResolver = File.ReadAllText(Path.Combine(
            repositoryRoot, "infrastructure", "azure", "Resolve-AuralyDeploymentComponents.ps1"));

        Assert.Contains("Record immutable deployment scope in DEV", workflow,
            StringComparison.Ordinal);
        Assert.Contains("needs.release.outputs.deploy_cloud == 'true'", workflow,
            StringComparison.Ordinal);
        Assert.Contains("needs.release.outputs.deploy_admin == 'true'", workflow,
            StringComparison.Ordinal);
        Assert.Contains("-Components $components", workflow, StringComparison.Ordinal);
        Assert.Contains("deploymentComponents", scopeResolver, StringComparison.Ordinal);
        Assert.Contains("Los componentes solicitados no coinciden", scopeResolver,
            StringComparison.Ordinal);
        Assert.Contains("if ($selectedComponents -contains 'database')", deployment,
            StringComparison.Ordinal);
        Assert.Contains("if ($selectedComponents -contains 'function')", deployment,
            StringComparison.Ordinal);
        Assert.Contains("if ($selectedComponents -contains 'api')", deployment,
            StringComparison.Ordinal);
        Assert.Contains("if ($selectedComponents -contains 'pos-installer')", deployment,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Assert-OfflineLeaseSigningConfiguration\r\nPublish-Database",
            deployment,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Function_key_configuration_retries_while_the_new_host_discovers_functions()
    {
        var deployment = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "infrastructure",
            "azure",
            "Publish-AuralyReleasePipeline.ps1"));

        Assert.Contains("function Set-FunctionKeyWithRetry", deployment,
            StringComparison.Ordinal);
        Assert.Contains("for ($attempt = 1; $attempt -le $Attempts; $attempt++)", deployment,
            StringComparison.Ordinal);
        Assert.Contains("Set-FunctionKeyWithRetry `", deployment,
            StringComparison.Ordinal);
        Assert.Contains("-FunctionName 'WhatsAppWebhook'", deployment,
            StringComparison.Ordinal);
        Assert.Contains("-KeyName 'meta-cj'", deployment,
            StringComparison.Ordinal);
    }

    [Fact]
    public void App_configuration_writes_are_retried_and_verified_after_timeouts()
    {
        var deployment = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "infrastructure",
            "azure",
            "Publish-AuralyReleasePipeline.ps1"));

        Assert.Contains("function Set-AppConfigurationValueWithRetry", deployment,
            StringComparison.Ordinal);
        Assert.Contains("az appconfig kv show", deployment, StringComparison.Ordinal);
        Assert.Contains("[string]::Equals($currentValue, $Value", deployment,
            StringComparison.Ordinal);
        Assert.Contains("-Key 'WhatsApp:Webhook:VerifyToken'", deployment,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Function_receives_the_same_processing_queue_settings_owned_by_the_api()
    {
        var repositoryRoot = FindRepositoryRoot();
        var deployment = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "infrastructure",
            "azure",
            "Publish-AuralyReleasePipeline.ps1"));
        var infrastructure = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "infrastructure",
            "azure",
            "main.bicep"));

        Assert.Contains("function Sync-FunctionRuntimeSettingsFromApi", deployment,
            StringComparison.Ordinal);
        Assert.Contains("'webapp', 'config', 'appsettings', 'list'", deployment,
            StringComparison.Ordinal);
        foreach (var name in new[]
                 {
                     "Auraly__Accounting__ServiceBus__QueueName",
                     "Auraly__Fiscal__ServiceBus__QueueName",
                     "Auraly__SalesReporting__ServiceBus__QueueName",
                 })
        {
            Assert.Contains(name, deployment, StringComparison.Ordinal);
            Assert.Contains(name, infrastructure, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Release_keeps_runtime_sql_access_and_probes_the_database()
    {
        var repositoryRoot = FindRepositoryRoot();
        var deployment = File.ReadAllText(Path.Combine(
            repositoryRoot, "infrastructure", "azure", "Publish-AuralyReleasePipeline.ps1"));
        var firewall = File.ReadAllText(Path.Combine(
            repositoryRoot, "infrastructure", "azure", "sql-app-firewall.bicep"));
        var infrastructure = File.ReadAllText(Path.Combine(
            repositoryRoot, "infrastructure", "azure", "main.bicep"));
        var api = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "API", "Auraly.Api", "Program.cs"));
        var workerHealth = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "API", "Auraly.Platform.Worker", "Functions", "HealthFunction.cs"));

        Assert.Contains("Sync-AuralySqlFirewall.ps1", deployment, StringComparison.Ordinal);
        Assert.Contains("AllowAllWindowsAzureIps", firewall, StringComparison.Ordinal);
        Assert.Contains("startIpAddress: '0.0.0.0'", firewall, StringComparison.Ordinal);
        Assert.Contains("AllowAllWindowsAzureIps", infrastructure, StringComparison.Ordinal);
        Assert.Contains("github-$Environment-*", File.ReadAllText(Path.Combine(
            repositoryRoot, "infrastructure", "azure", "Sync-AuralySqlFirewall.ps1")),
            StringComparison.Ordinal);
        Assert.Contains("database.CheckAsync", api, StringComparison.Ordinal);
        Assert.Contains("database.CheckAsync", workerHealth, StringComparison.Ordinal);
        Assert.Contains("ServiceUnavailable", workerHealth, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_rejects_a_truncated_offline_signing_key_before_deployment()
    {
        var repositoryRoot = FindRepositoryRoot();
        var deployment = File.ReadAllText(Path.Combine(
            repositoryRoot, "infrastructure", "azure", "Publish-AuralyReleasePipeline.ps1"));
        var api = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "API", "Auraly.Api", "Program.cs"));
        var installer = File.ReadAllText(Path.Combine(
            repositoryRoot, "scripts", "Build-AuralyPosInstaller.ps1"));

        Assert.Contains("Assert-OfflineLeaseSigningConfiguration", deployment,
            StringComparison.Ordinal);
        Assert.Contains("ImportFromPem($privateKeyPem)", deployment, StringComparison.Ordinal);
        Assert.Contains("ValidateConfiguration();", api, StringComparison.Ordinal);
        Assert.Contains("AURALY_DESKTOP_BUILD", installer, StringComparison.Ordinal);
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
