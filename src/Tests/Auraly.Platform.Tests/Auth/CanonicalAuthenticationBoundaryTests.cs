using System.Reflection;
using System.Security.Claims;
using Auraly.Application.Authentication;
using Auraly.Contracts.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Auraly.Api.Authorization;
using Auraly.Api.Controllers;
using Xunit;

namespace Auraly.Platform.Tests.Auth;

public sealed class CanonicalAuthenticationBoundaryTests
{
    [Fact]
    public void Historical_controller_no_longer_exposes_token_issuance()
    {
        var routes = typeof(AuthController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(method => method.GetCustomAttributes<HttpPostAttribute>())
            .Select(attribute => attribute.Template)
            .ToArray();

        Assert.Equal(new[] { "change-password" }, routes);
    }

    [Fact]
    public async Task Administrative_host_rejects_an_inactive_session()
    {
        var validator = new Mock<IAuthenticationSessionValidator>();
        validator
            .Setup(service => service.IsActiveAsync(
                It.IsAny<ParsedAuthenticationToken>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var events = new AuthenticationSessionJwtBearerEvents(validator.Object);
        var context = Context(Principal());

        await events.TokenValidated(context);

        Assert.NotNull(context.Result?.Failure);
        validator.VerifyAll();
    }

    [Fact]
    public async Task Administrative_host_rejects_a_token_without_sid()
    {
        var validator = new Mock<IAuthenticationSessionValidator>(MockBehavior.Strict);
        var events = new AuthenticationSessionJwtBearerEvents(validator.Object);
        var context = Context(Principal(includeSession: false));

        await events.TokenValidated(context);

        Assert.NotNull(context.Result?.Failure);
        validator.VerifyNoOtherCalls();
    }

    private static TokenValidatedContext Context(ClaimsPrincipal principal)
    {
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme,
            JwtBearerDefaults.AuthenticationScheme,
            typeof(IAuthenticationHandler));
        return new TokenValidatedContext(
            new DefaultHttpContext(), scheme, new JwtBearerOptions())
        {
            Principal = principal,
        };
    }

    private static ClaimsPrincipal Principal(bool includeSession = true)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new(AuthenticationDefaults.TenantIdClaim, Guid.NewGuid().ToString("D")),
        };
        if (includeSession)
            claims.Add(new Claim(
                AuthenticationDefaults.SessionIdClaim,
                Guid.NewGuid().ToString("D")));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
