using Auraly.Pos.Edge.Host;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosStartupModeTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        $"auraly-pos-startup-mode-{Guid.NewGuid():N}");

    [Fact]
    public void New_installation_starts_online()
    {
        var store = new PosStartupModeStore(_path);

        Assert.Equal(PosStartupModes.Online, store.Load(hasEnrollment: false));
    }

    [Fact]
    public void Existing_enrollment_defaults_to_local_login_during_upgrade()
    {
        var store = new PosStartupModeStore(_path);

        Assert.Equal(PosStartupModes.Enrolled, store.Load(hasEnrollment: true));
    }

    [Theory]
    [InlineData(PosStartupModes.Online)]
    [InlineData(PosStartupModes.Enrolled)]
    public void Explicit_mode_survives_restart(string mode)
    {
        new PosStartupModeStore(_path).Save(mode);

        var reopened = new PosStartupModeStore(_path);

        Assert.Equal(mode, reopened.Load(hasEnrollment: mode == PosStartupModes.Enrolled));
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        if (File.Exists(_path + ".new")) File.Delete(_path + ".new");
    }
}
