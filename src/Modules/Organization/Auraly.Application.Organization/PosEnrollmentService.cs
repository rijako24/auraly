using System.Security.Cryptography;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Organization;
using Auraly.Contracts.Parties;

namespace Auraly.Application.Organization;

public sealed record PosEnrollmentUserIdentity(
    Guid UserId,
    Guid TenantId,
    string DisplayName,
    IReadOnlySet<string> Permissions);

public sealed record PosEnrollmentAuthorizationCommand(
    Guid EnrollmentSessionId,
    byte[] RedemptionCodeHash,
    string RedemptionCode,
    DateTimeOffset ExpiresAt,
    PosEnrollmentUserIdentity User,
    CreatePosEnrollmentRequest Request);

public interface IPosEnrollmentStore
{
    Task<OnlineRegisterContext?> ResolveRegisterAsync(
        Guid tenantId,
        CreatePosEnrollmentRequest request,
        CancellationToken cancellationToken);

    Task CreateAuthorizationAsync(
        PosEnrollmentAuthorizationCommand command,
        OnlineRegisterContext register,
        CancellationToken cancellationToken);

    Task<PosEnrollmentPackage> RedeemAsync(
        RedeemPosEnrollmentRequest request,
        byte[] redemptionCodeHash,
        IReadOnlyCollection<string> devicePermissions,
        CancellationToken cancellationToken);
}

public sealed class PosEnrollmentForbiddenException(string message) : Exception(message);
public sealed class PosEnrollmentValidationException(string message) : Exception(message);
public sealed class PosEnrollmentConflictException(string message) : Exception(message);

public sealed class PosEnrollmentService(
    IPosEnrollmentStore store,
    TimeProvider timeProvider,
    IAuralyIdGenerator idGenerator)
{
    private static readonly string[] DevicePermissions =
    [
        CommercePermissionCodes.SalesCreate,
        CommercePermissionCodes.SalesDiscount,
        CommercePermissionCodes.SalesReprint,
        CommercePermissionCodes.SalesVoid,
        CatalogPermissionCodes.Sync,
        FiscalPermissionCodes.PosStatusSync,
        PartyPermissionCodes.PosCustomerCreate,
        CommercePermissionCodes.PosIdentitySync
    ];

    public async Task<PosEnrollmentAuthorization> AuthorizeAsync(
        PosEnrollmentUserIdentity user,
        CreatePosEnrollmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.Permissions.Contains(CommercePermissionCodes.PosDevicesEnroll))
            throw new PosEnrollmentForbiddenException(
                $"Permission '{CommercePermissionCodes.PosDevicesEnroll}' is required.");
        if (request.BusinessId == Guid.Empty ||
            request.RegisterId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.DeviceName))
            throw new PosEnrollmentValidationException(
                "Negocio, sede, caja y nombre del equipo son obligatorios.");
        var deviceName = request.DeviceName.Trim();
        if (deviceName.Length > 160)
            throw new PosEnrollmentValidationException(
                "El nombre del equipo no puede superar 160 caracteres.");

        var register = await store.ResolveRegisterAsync(
            user.TenantId, request with { DeviceName = deviceName }, cancellationToken)
            ?? throw new PosEnrollmentForbiddenException(
                "La caja no pertenece al tenant autenticado o no coincide con la sede.");
        var codeBytes = RandomNumberGenerator.GetBytes(32);
        var code = Convert.ToBase64String(codeBytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = timeProvider.GetUtcNow();
        var command = new PosEnrollmentAuthorizationCommand(
            idGenerator.NewId(),
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)),
            code,
            now.AddMinutes(10),
            user,
            request with { DeviceName = deviceName });
        await store.CreateAuthorizationAsync(command, register, cancellationToken);
        return new PosEnrollmentAuthorization(
            command.EnrollmentSessionId, code, command.ExpiresAt, register);
    }

    public Task<PosEnrollmentPackage> RedeemAsync(
        RedeemPosEnrollmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.EnrollmentSessionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RedemptionCode) ||
            string.IsNullOrWhiteSpace(request.InstallationId))
            throw new PosEnrollmentValidationException(
                "La sesión, el código y la identificación de instalación son obligatorios.");
        if (request.InstallationId.Trim().Length > 160)
            throw new PosEnrollmentValidationException(
                "La identificación de instalación no puede superar 160 caracteres.");
        var hash = SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(request.RedemptionCode.Trim()));
        return store.RedeemAsync(
            request with { InstallationId = request.InstallationId.Trim() },
            hash,
            DevicePermissions,
            cancellationToken);
    }
}
