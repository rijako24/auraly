using Auraly.Platform.Application.Identity.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Identity;

public sealed class PermissionServiceTests
{
    [Fact]
    public async Task SeedPermissionsAsync_DoesNotGrantFullCatalogToOperationalSystemRoles()
    {
        var permission = new Permission
        {
            PermissionId = Guid.NewGuid(),
            Module = "Inventory",
            Action = "Read",
            Resource = "inventory.read"
        };
        var administrator = new AppRole
        {
            RoleId = Guid.NewGuid(),
            Name = "Administrador",
            NormalizedName = "ADMINISTRATOR",
            IsActive = true,
            IsSystemRole = true
        };
        var transporter = new AppRole
        {
            RoleId = Guid.NewGuid(),
            Name = "Transportador",
            NormalizedName = "TRANSPORTADOR",
            IsActive = true,
            IsSystemRole = true
        };

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(x => x.Permissions).Returns(Mock.Of<IPermissionRepository>());
        unitOfWork.SetupGet(x => x.AppRoles).Returns(Mock.Of<IAppRoleRepository>());
        unitOfWork.SetupGet(x => x.RolePermissions).Returns(Mock.Of<IRolePermissionRepository>());
        unitOfWork.Setup(x => x.Permissions.ExistsByResourceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        unitOfWork.Setup(x => x.Permissions.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([permission]);
        unitOfWork.Setup(x => x.AppRoles.GetActiveSystemRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([administrator, transporter]);

        IReadOnlyList<RolePermission>? assignments = null;
        unitOfWork.Setup(x => x.RolePermissions.AddRangeAsync(It.IsAny<IEnumerable<RolePermission>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<RolePermission>, CancellationToken>((items, _) => assignments = items.ToList())
            .Returns(Task.CompletedTask);

        var service = new PermissionService(unitOfWork.Object, Mock.Of<ILogger<PermissionService>>());

        await service.SeedPermissionsAsync(CancellationToken.None);

        Assert.NotNull(assignments);
        Assert.Single(assignments!);
        Assert.Equal(administrator.RoleId, assignments![0].RoleId);
        Assert.DoesNotContain(assignments!, item => item.RoleId == transporter.RoleId);
    }
}
