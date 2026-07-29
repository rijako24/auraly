using Auraly.Contracts.Authorization;
using Auraly.Contracts.Organization;

namespace Auraly.Application.Organization;

public sealed record OnlineRegisterUserIdentity(
    Guid UserId,
    Guid TenantId,
    IReadOnlySet<string> Permissions);

public interface IOnlineRegisterDirectory
{
    Task<IReadOnlyList<OnlineRegisterOption>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<OnlineRegisterContext?> ResolveAsync(
        Guid tenantId,
        OnlineRegisterSelection selection,
        CancellationToken cancellationToken);
}

public sealed class OnlineRegisterForbiddenException(string message) : Exception(message);
public sealed class OnlineRegisterValidationException(string message) : Exception(message);

public sealed class OnlineRegisterService(IOnlineRegisterDirectory directory)
{
    public Task<IReadOnlyList<OnlineRegisterOption>> ListAsync(
        OnlineRegisterUserIdentity user,
        CancellationToken cancellationToken = default)
    {
        DemandSalesPermission(user);
        return directory.ListAsync(user.TenantId, cancellationToken);
    }

    public async Task<OnlineRegisterContext> SelectAsync(
        OnlineRegisterUserIdentity user,
        OnlineRegisterSelection selection,
        CancellationToken cancellationToken = default)
    {
        DemandSalesPermission(user);
        if (selection.BusinessId == Guid.Empty ||
            selection.LocationId == Guid.Empty ||
            selection.RegisterId == Guid.Empty)
        {
            throw new OnlineRegisterValidationException(
                "Negocio, sede y caja son obligatorios.");
        }

        return await directory.ResolveAsync(user.TenantId, selection, cancellationToken)
            ?? throw new OnlineRegisterForbiddenException(
                "La caja no pertenece al tenant autenticado, no coincide con la sede o está inactiva.");
    }

    private static void DemandSalesPermission(OnlineRegisterUserIdentity user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.Permissions.Contains(CommercePermissionCodes.SalesCreate))
        {
            throw new OnlineRegisterForbiddenException(
                $"Permission '{CommercePermissionCodes.SalesCreate}' is required.");
        }
    }
}
