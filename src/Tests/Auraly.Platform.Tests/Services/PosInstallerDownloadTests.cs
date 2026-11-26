using System.Net;
using System.Security.Claims;
using Auraly.Api;
using Auraly.Platform.Application.Services;
using Azure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class PosInstallerDownloadTests
{
    [Theory]
    [InlineData(true, false, HttpStatusCode.Redirect)]
    [InlineData(false, false, HttpStatusCode.Unauthorized)]
    [InlineData(true, true, HttpStatusCode.NotFound)]
    public async Task Download_authorizes_then_redirects_to_the_configured_private_file(
        bool authenticated, bool missing, HttpStatusCode expected)
    {
        const string url = "https://testing.blob.core.windows.net/downloads/Auraly-POS-Setup.exe?sp=r&sig=test";
        var files = new Mock<IApplicationFileUrlResolver>(MockBehavior.Strict);
        var call = files.Setup(value => value.ResolveApplicationFileAsync("downloads", "Auraly-POS-Setup.exe",
            It.IsAny<CancellationToken>()));
        if (missing) call.ThrowsAsync(new RequestFailedException(404, "missing"));
        else call.ReturnsAsync(url);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(files.Object);
        builder.Services.AddSingleton<IOptions<PosInstallerOptions>>(Options.Create(new PosInstallerOptions
            { Version = "1.0.0", Sha256 = new string('A', 64) }));
        builder.Services.AddAuthentication("test").AddCookie("test", options =>
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = 401;
                return Task.CompletedTask;
            });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.ContainsKey("X-Test-User"))
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "test-user")], "test"));
            await next();
        });
        app.UseAuthorization();
        app.MapPosInstallerApi();
        await app.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single()), Timeout = TimeSpan.FromSeconds(10) };
        if (authenticated) client.DefaultRequestHeaders.Add("X-Test-User", "test");
        using var response = await client.GetAsync("/api/commerce/v1/pos/installer/download");
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Redirect)
        {
            Assert.Equal(url, response.Headers.Location!.ToString());
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Equal(0, response.Content.Headers.ContentLength);
        }
        files.Verify(value => value.ResolveApplicationFileAsync("downloads", "Auraly-POS-Setup.exe",
            It.IsAny<CancellationToken>()), authenticated ? Times.Once() : Times.Never());
        await app.StopAsync();
    }
}
