using System.Security.Claims;
using Auraly.Api;
using FluentAssertions;
using Xunit;

namespace Auraly.Platform.Tests.Dispatching;

public sealed class DispatchAuthorizationArchitectureTests
{
    [Fact]
    public void Administrators_can_execute_deliveries_without_impersonating_the_assigned_transporter()
    {
        var root = FindSolutionRoot();
        var store = File.ReadAllText(Path.Combine(
            root,
            "src/Modules/Dispatching/Auraly.Infrastructure.Dispatching/SqlDispatchDeliveryStore.cs"
                .Replace('/', Path.DirectorySeparatorChar)));

        store.Should().Contain("(@IsAdministrator=1 OR dispatch.DriverUserId=@UserId)");
        store.Should().Contain("(@IsAdministrator=1 OR DriverUserId=@UserId)");
        store.Should().NotContain("AND dispatch.DriverUserId=@UserId AND dispatch.Status");
        store.Should().NotContain("AND DriverUserId=@UserId AND Status");
    }

    [Theory]
    [InlineData("Administrador")]
    [InlineData("Administrator")]
    [InlineData("TenantAdministrator")]
    [InlineData("Administrador de plataforma")]
    public void Built_in_administrator_roles_receive_the_assignment_override(string role)
    {
        Actor(role).IsAdministrator.Should().BeTrue();
    }

    [Theory]
    [InlineData("Transportador")]
    [InlineData("Supervisor")]
    [InlineData("Administrativo")]
    public void Non_administrator_roles_cannot_override_the_assigned_transporter(string role)
    {
        Actor(role).IsAdministrator.Should().BeFalse();
    }

    private static Auraly.Contracts.Dispatching.DispatchActorIdentity Actor(string role)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim("tenant_id", Guid.NewGuid().ToString("D")),
            new Claim("business_id", Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, role),
            new Claim("permission", "dispatches.delivery.execute"),
            new Claim("permission", "dispatches.read-all"),
            new Claim("permission", "dispatches.settle")
        ], "Test");
        return new ClaimsPrincipal(identity).ToDispatchIdentity();
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Auraly.Commerce.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Auraly.Commerce.sln.");
    }
}
