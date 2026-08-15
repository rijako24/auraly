using Auraly.Api;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class PosInstallerOptionsTests
{
    [Fact]
    public void Published_installer_is_generic_and_exposes_integrity_metadata()
    {
        var options = new PosInstallerOptions
        {
            ContainerName = "downloads",
            BlobName = "Auraly-POS-Setup.exe",
            Version = "1.4.0",
            Sha256 = new string('a', 64)
        };

        Assert.True(options.TryCreateView(out var installer));
        Assert.NotNull(installer);
        Assert.Equal("/api/commerce/v1/pos/installer/download", installer.DownloadUrl);
        Assert.Equal("1.4.0", installer.Version);
        Assert.Equal(new string('A', 64), installer.Sha256);
        Assert.False(installer.TenantPreconfigured);
    }

    [Theory]
    [InlineData("", "Auraly-POS-Setup.exe", "1.0.0")]
    [InlineData("downloads", "", "1.0.0")]
    [InlineData("downloads", "Auraly-POS-Setup.exe", "")]
    public void Untrusted_or_incomplete_installer_metadata_is_rejected(
        string container,
        string blob,
        string version)
    {
        var options = new PosInstallerOptions
        {
            ContainerName = container,
            BlobName = blob,
            Version = version,
            Sha256 = new string('F', 64)
        };

        Assert.False(options.TryCreateView(out var installer));
        Assert.Null(installer);
    }
}