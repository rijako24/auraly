using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Contracts.Catalog;
using Auraly.Pos.Edge.Host;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosEdgeHostTests : IAsyncLifetime
{
    private const string Token = "test-session-token-with-at-least-32-bytes";
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"auraly-edge-host-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private readonly List<string> _environmentKeys = [];
    private HttpClient Client =>
        _client ?? throw new InvalidOperationException("The test host has not started.");

    [Fact]
    public async Task Loopback_api_requires_the_local_session_token()
    {
        using var anonymous = _factory!.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/edge/v1/health")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await _client!.GetAsync("/edge/v1/health")).StatusCode);
    }

    [Fact]
    public async Task Scanner_and_temporaries_flow_through_the_protected_http_api()
    {
        var active = await Client.GetFromJsonAsync<PosDraft>("/edge/v1/drafts/active");
        Assert.NotNull(active);
        Assert.Empty(active!.Lines);

        var capture = await Client.PostAsJsonAsync(
            "/edge/v1/capture",
            new CaptureRequest("770123", null));
        capture.EnsureSuccessStatusCode();
        var captured = await capture.Content.ReadFromJsonAsync<PosCaptureResult>();
        Assert.Single(captured!.Draft!.Lines);

        var saved = await Client.PostAsJsonAsync(
            $"/edge/v1/drafts/{captured.Draft.DraftId.Value:D}/temporary",
            new SaveTemporaryRequest("Cliente espera", "REF-1", null));
        saved.EnsureSuccessStatusCode();
        var temporaries = await Client.GetFromJsonAsync<PosDraft[]>(
            "/edge/v1/temporaries?search=REF-1");
        Assert.Single(temporaries!);

        var recovered = await Client.PostAsync(
            $"/edge/v1/temporaries/{temporaries![0].DraftId.Value:D}/recover",
            null);
        recovered.EnsureSuccessStatusCode();
        var restored = await recovered.Content.ReadFromJsonAsync<PosDraft>();
        Assert.Single(restored!.Lines);
    }

    public async Task InitializeAsync()
    {
        var ids = new Dictionary<string, string?>
        {
            ["PosEdge:DatabasePath"] = _path,
            ["PosEdge:SessionToken"] = Token,
            ["PosEdge:AllowedOrigin"] = "http://127.0.0.1:47830",
            ["PosEdge:ServerUrl"] = "http://127.0.0.1:59999",
            ["PosEdge:DeviceId"] = Guid.NewGuid().ToString("D"),
            ["PosEdge:DeviceSecret"] = "test-device-secret",
            ["PosEdge:BusinessId"] = Guid.NewGuid().ToString("D"),
            ["PosEdge:WarehouseId"] = Guid.NewGuid().ToString("D"),
            ["PosEdge:RegisterId"] = Guid.NewGuid().ToString("D"),
            ["PosEdge:UserId"] = Guid.NewGuid().ToString("D"),
            ["PosEdge:WarehouseAllowsNegativeStock"] = "true"
        };
        foreach (var setting in ids)
        {
            var key = setting.Key.Replace(":", "__", StringComparison.Ordinal);
            Environment.SetEnvironmentVariable(key, setting.Value);
            _environmentKeys.Add(key);
        }
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Auraly-Edge-Session", Token);

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<PosCatalogStore>();
        var product = new PosCatalogItem(
            Guid.NewGuid(), "P-1", "REF-1", "Product", "EA", "VAT19", 19m,
            100m, "COP", true, null, ["770123"], []);
        var sessionId = Guid.NewGuid();
        var items = new[] { product };
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(items))))
            .ToLowerInvariant();
        await store.BeginBootstrapAsync(
            new CatalogSyncSessionResponse(sessionId, 0, 1, DateTimeOffset.UtcNow.AddHours(1)));
        await store.ApplyBootstrapPageAsync(
            new CatalogBootstrapPage(sessionId, 0, null, false, hash, items));
        await store.PromoteBootstrapAsync();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _path, $"{_path}-wal", $"{_path}-shm" })
            if (File.Exists(path)) File.Delete(path);
        foreach (var key in _environmentKeys)
            Environment.SetEnvironmentVariable(key, null);
    }
}
