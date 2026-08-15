using Microsoft.AspNetCore.Authorization;

namespace Auraly.Api.Authorization;

public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var userPermissions = context.User
            .FindAll("permission")
            .Select(c => c.Value);

        if (userPermissions.Contains(requirement.Permission))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
