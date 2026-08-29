using Auraly.Contracts.Authorization;
using Auraly.Contracts.Organization;

namespace Auraly.Application.Organization;

public sealed record SalesWorkspaceUserIdentity(
    Guid UserId,
    Guid TenantId,
    IReadOnlySet<string> Permissions);

public interface ISalesWorkspaceDirectory
{
    Task<string?> ResolveTenantNameAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesWorkspaceOption>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<SalesWorkspaceContext?> ResolveAsync(
        Guid tenantId,
        SalesWorkspaceSelection selection,
        CancellationToken cancellationToken);
}

public sealed class SalesWorkspaceForbiddenException(string message) : Exception(message);
public sealed class SalesWorkspaceValidationException(string message) : Exception(message);

public sealed class SalesWorkspaceService(
    ISalesWorkspaceDirectory directory,
    IPosEnrollmentStore enrollments)
{
    private const string SellerOrderCreatePermission = "orders.create";
    public async Task<string> TenantNameAsync(
        SalesWorkspaceUserIdentity user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return await directory.ResolveTenantNameAsync(user.TenantId, cancellationToken)
            ?? throw new SalesWorkspaceForbiddenException(
                "La empresa autenticada no existe o está inactiva.");
    }

    public Task<IReadOnlyList<SalesWorkspaceOption>> ListAsync(
        SalesWorkspaceUserIdentity user,
        CancellationToken cancellationToken = default)
    {
        DemandSalesPermission(user);
        return directory.ListAsync(user.TenantId, cancellationToken);
    }

    public Task<PosEnrollmentCapacity> EnrollmentCapacityAsync(
        SalesWorkspaceUserIdentity user,
        CancellationToken cancellationToken = default)
    {
        DemandSalesPermission(user);
        return enrollments.ReadCapacityAsync(user.TenantId, cancellationToken);
    }

    public async Task<SalesWorkspaceContext> SelectAsync(
        SalesWorkspaceUserIdentity user,
        SalesWorkspaceSelection selection,
        CancellationToken cancellationToken = default)
    {
        DemandSalesPermission(user);
        if (selection.BusinessId == Guid.Empty || selection.WarehouseId == Guid.Empty)
            throw new SalesWorkspaceValidationException(
                "La sede y la bodega son obligatorias.");

        return await directory.ResolveAsync(user.TenantId, selection, cancellationToken)
            ?? throw new SalesWorkspaceForbiddenException(
                "La sede o bodega no pertenece a la empresa autenticada o está inactiva.");
    }

    public Task<SalesWorkspaceContext> ChangeAsync(
        SalesWorkspaceUserIdentity user,
        SalesWorkspaceSelection selection,
        CancellationToken cancellationToken = default)
    {
        if (!user.Permissions.Contains(CommercePermissionCodes.PosWorkspaceChange))
            throw new SalesWorkspaceForbiddenException(
                $"Permission '{CommercePermissionCodes.PosWorkspaceChange}' is required.");
        return SelectAsync(user, selection, cancellationToken);
    }

    private static void DemandSalesPermission(SalesWorkspaceUserIdentity user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.Permissions.Contains(CommercePermissionCodes.SalesCreate)
            && !user.Permissions.Contains(SellerOrderCreatePermission))
            throw new SalesWorkspaceForbiddenException(
                $"Permission '{CommercePermissionCodes.SalesCreate}' or '{SellerOrderCreatePermission}' is required.");
    }
}
