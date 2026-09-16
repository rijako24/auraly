using Auraly.Contracts.Authorization;

namespace Auraly.Foundation.Tests;

public sealed class PosDelegatedPermissionPolicyTests
{
    [Theory]
    [InlineData("sales.below-cost")]
    [InlineData("future-module.future_action")]
    [InlineData("work-sessions.close-with-paused-sales")]
    public void Any_well_formed_backend_permission_can_use_the_generic_approval_flow(
        string permission)
    {
        Assert.True(PosDelegatedPermissionPolicy.IsValid(permission));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" sales.create")]
    [InlineData("SALES.CREATE")]
    [InlineData("sales/create")]
    public void Malformed_permission_resources_are_rejected(string permission)
    {
        Assert.False(PosDelegatedPermissionPolicy.IsValid(permission));
    }
}
