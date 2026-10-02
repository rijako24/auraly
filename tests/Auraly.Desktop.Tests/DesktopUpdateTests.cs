using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using System.Text.Json;
using Auraly.Desktop;
using Xunit;

namespace Auraly.Desktop.Tests;

public sealed class DesktopUpdateTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "auraly-update-test-" + Guid.NewGuid().ToString("N"));
    private readonly DesktopConfiguration configuration = new("https://example.test", "0.1.0-rc9");

    [Theory]
    [InlineData("available")]
    [InlineData("downloading")]
    [InlineData("verifying")]
    [InlineData("ready")]
    [InlineData("restarting")]
    [InlineData("restart-error")]
    [InlineData("error")]
    public void Native_status_uses_the_exact_web_contract(string status)
    {
        var message = new AuralyUpdateStatus("auraly-pos-update-status", status, "0.1.0-rc10", 50, "Prueba");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(message));
        Assert.Equal(new[] { "type", "status", "version", "progress", "message" },
            json.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("auraly-pos-update-status", json.RootElement.GetProperty("type").GetString());
        Assert.Equal(status, json.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("0.1.0-rc10", true)]
    [InlineData("0.1.0-rc9", false)]
    [InlineData("0.1.0-rc8", false)]
    [InlineData("0.1.0", true)]
    public void Offers_only_a_newer_installer(string version, bool expected)
    {
        var request = new AuralyUpdateRequest("auraly-pos-update-discovered",
            "/api/commerce/v1/pos/installer/download", version, new string('A', 64));
        Assert.Equal(expected, AuralyDesktopUpdater.ShouldOffer(request, configuration.Version));
        Assert.False(AuralyDesktopUpdater.ShouldOffer(request with { DownloadUrl = "https://example.test/setup.exe" }, configuration.Version));
        Assert.False(AuralyDesktopUpdater.ShouldOffer(request with { Sha256 = "invalid" }, configuration.Version));
    }

    [Theory]
    [InlineData("http://127.0.0.1:47830/pos", true)]
    [InlineData("http://127.0.0.1:47830/dashboard", true)]
    [InlineData("http://127.0.0.1:47831/pos", false)]
    [InlineData("https://example.test/pos", false)]
    public void Native_commands_require_the_local_web_origin(string source, bool expected) =>
        Assert.Equal(expected, AuralyDesktopUpdater.IsTrustedSource(source, "http://127.0.0.1:47830"));

    [Fact]
    public async Task Pending_download_can_be_restored_without_redownloading_or_executing_it_from_the_store()
    {
        var pending = await SaveDownload();
        Assert.Equal(pending, AuralyPendingUpdateStore.TryLoad(directory, configuration));
        Assert.Equal(pending, AuralyPendingUpdateStore.TryLoad(directory, configuration));
        Assert.True(File.Exists(pending.InstallerPath));
        Assert.True(File.Exists(Path.Combine(directory, "updates", "pending-update.json")));
    }

    [Fact]
    public async Task Successfully_installed_version_is_not_offered_again()
    {
        var pending = await SaveDownload();
        Assert.Null(AuralyPendingUpdateStore.TryLoad(directory, configuration with { Version = pending.Version }));
        Assert.False(File.Exists(Path.Combine(directory, "updates", "pending-update.json")));
    }

    [Fact]
    public async Task A_modified_or_missing_installer_is_rejected()
    {
        var pending = await SaveDownload();
        await File.WriteAllTextAsync(pending.InstallerPath, "changed");
        Assert.False(AuralyPendingUpdateStore.IsValid(pending, directory, configuration));
        Assert.Null(AuralyPendingUpdateStore.TryLoad(directory, configuration));
        File.Delete(pending.InstallerPath);
        Assert.False(AuralyPendingUpdateStore.IsValid(pending, directory, configuration));
    }

    [Fact]
    public async Task Installer_must_be_inside_the_update_directory_and_signed_by_the_configured_publisher()
    {
        var pending = await SaveDownload();
        var outside = Path.Combine(directory, "Auraly-Setup.exe");
        File.Copy(pending.InstallerPath, outside);
        Assert.False(AuralyPendingUpdateStore.IsValid(pending with { InstallerPath = outside }, directory, configuration));
        Assert.False(AuralyPendingUpdateStore.IsValid(pending, directory,
            configuration with { PublisherCertificateThumbprint = new string('A', 40) }));
        Assert.False(AuralyPendingUpdateStore.IsValid(
            pending with { PublisherCertificateThumbprint = new string('A', 40) }, directory,
            configuration with { PublisherCertificateThumbprint = new string('A', 40) }));
    }

    [Fact]
    public void Windows_accepts_a_signed_runtime_only_for_its_actual_publisher()
    {
        var runtime = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        using var signed = X509Certificate.CreateFromSignedFile(runtime);
        using var certificate = new X509Certificate2(signed);
        Assert.True(AuralyAuthenticodeVerifier.IsValid(runtime, certificate.Thumbprint));
        Assert.False(AuralyAuthenticodeVerifier.IsValid(runtime, new string('A', 40)));
    }

    private async Task<AuralyPendingUpdate> SaveDownload()
    {
        var folder = Path.Combine(directory, "updates", "0.1.0-rc10");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Auraly-Setup.exe");
        await File.WriteAllTextAsync(path, "An inert test fixture, never executable.");
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        var pending = new AuralyPendingUpdate("0.1.0-rc10", hash, path, "", DateTimeOffset.UtcNow);
        await AuralyPendingUpdateStore.SaveAsync(directory, pending, CancellationToken.None);
        return pending;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
