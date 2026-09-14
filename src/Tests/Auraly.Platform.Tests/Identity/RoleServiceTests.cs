using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Platform.Application.Common.Interfaces;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Application.Identity.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Identity;

public sealed class RoleServiceTests
{
    [Fact]
    public async Task Permission_workspace_loads_role_assignments_and_catalog_once()
    {
        var assignedPermission = Permission("roles.read");
        var availablePermission = Permission("roles.update");
        var role = new AppRole
        {
            RoleId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Name = "Supervisor",
            NormalizedName = "SUPERVISOR",
            CreatedAt = DateTime.UtcNow,
            RolePermissions =
            [
                new RolePermission
                {
                    RolePermissionId = Guid.NewGuid(),
                    PermissionId = assignedPermission.PermissionId
                }
            ]
        };
        var roles = new Mock<IAppRoleRepository>();
        roles.Setup(repository => repository.GetWithPermissionsAsync(
                role.RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        var permissions = new Mock<IPermissionRepository>();
        permissions.Setup(repository => repository.GetAllAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([assignedPermission, availablePermission]);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.AppRoles).Returns(roles.Object);
        unitOfWork.SetupGet(value => value.Permissions).Returns(permissions.Object);
        var service = new RoleService(
            unitOfWork.Object,
            Mock.Of<ICorrelationIdProvider>(),
            Mock.Of<ILogger<RoleService>>(),
            Mock.Of<IPosSecuritySynchronizationWriter>(),
            Mock.Of<IPosSynchronizationOutboxDispatcher>());

        var workspace = await service.GetPermissionWorkspaceAsync(
            role.RoleId, CancellationToken.None);

        Assert.Equal(role.RoleId, workspace.Role.RoleId);
        Assert.Equal(2, workspace.Permissions.Count);
        Assert.Equal([assignedPermission.PermissionId], workspace.AssignedPermissionIds);
        roles.Verify(repository => repository.GetWithPermissionsAsync(
            role.RoleId, It.IsAny<CancellationToken>()), Times.Once);
        roles.Verify(repository => repository.GetByIdAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        permissions.Verify(repository => repository.GetAllAsync(
            It.IsAny<CancellationToken>()), Times.Once);
        permissions.Verify(repository => repository.GetByRoleIdAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Permission Permission(string resource) => new()
    {
        PermissionId = Guid.NewGuid(),
        Module = "Security",
        Action = "Read",
        Resource = resource
    };
}
