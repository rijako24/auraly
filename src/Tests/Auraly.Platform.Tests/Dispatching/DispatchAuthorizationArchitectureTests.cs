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

        store.Should().Contain("(@ReadAll=1 OR @Settle=1 OR dispatch.DriverUserId=@UserId)");
        store.Should().Contain("(@ReadAll=1 OR @Settle=1 OR DriverUserId=@UserId)");
        store.Should().NotContain("AND dispatch.DriverUserId=@UserId AND dispatch.Status");
        store.Should().NotContain("AND DriverUserId=@UserId AND Status");
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
