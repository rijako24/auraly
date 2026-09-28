using Auraly.Pos.Edge.Host;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosLocalPrintBrandingStoreTests
{
    [Fact]
    public void Installed_logo_survives_restart_and_remains_scoped_to_its_tenant()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"auraly-logo-{Guid.NewGuid():N}");
        try
        {
            var tenant = Guid.NewGuid();
            var otherTenant = Guid.NewGuid();
            var source = "data:image/png;base64,AQID";
            var prepared = new PosLocalPrintBrandingStore(directory, tenant);
            Assert.True(prepared.AllowsTenant(tenant));
            Assert.False(prepared.AllowsTenant(otherTenant));
            new PosLocalPrintBrandingStore(directory).Save(tenant, source);
            var restarted = new PosLocalPrintBrandingStore(directory);
            Assert.Null(restarted.Get(tenant));
            restarted.Prepare(tenant);
            Assert.Equal(source, restarted.Get(tenant));
            File.Delete(Path.Combine(directory, "print-branding", $"{tenant:N}.logo"));
            Assert.Equal(source, restarted.Get(tenant));
            Assert.Null(restarted.Get(otherTenant));
            restarted.Save(tenant, null);
            Assert.Null(new PosLocalPrintBrandingStore(directory).Get(tenant));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Rejects_remote_or_oversized_logo()
    {
        var store = new PosLocalPrintBrandingStore(Path.GetTempPath());
        Assert.Throws<ArgumentException>(() => store.Save(Guid.NewGuid(), "https://example.com/logo.png"));
        Assert.Throws<ArgumentException>(() => store.Save(Guid.NewGuid(),
            $"data:image/png;base64,{new string('A', 6 * 1024 * 1024)}"));
    }
}
