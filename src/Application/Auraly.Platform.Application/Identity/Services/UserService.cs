using Auraly.Contracts.Authorization;
using Auraly.BuildingBlocks.Application.Synchronization;
using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Auth.Interfaces;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Common.Interfaces;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

public class UserService : IUserService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ILogger<UserService> _logger;
    private readonly IPosSecuritySynchronizationWriter _securitySynchronization;
    private readonly IPosSynchronizationOutboxDispatcher _synchronization;

    public UserService(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ICorrelationIdProvider correlationIdProvider,
        ILogger<UserService> logger,
        IPosSecuritySynchronizationWriter securitySynchronization,
        IPosSynchronizationOutboxDispatcher synchronization)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _correlationIdProvider = correlationIdProvider;
        _logger = logger;
        _securitySynchronization = securitySynchronization;
        _synchronization = synchronization;
    }

    public async Task<UserDto> GetByIdAsync(Guid userId, CancellationToken ct)
    {
        var user = await _unitOfWork.AppUsers.GetWithRolesAndPermissionsAsync(userId, ct)
            ?? throw new NotFoundException(nameof(AppUser), userId);

        return MapToDto(user);
    }

    public async Task<PagedResponse<UserDto>> GetPagedAsync(Guid tenantId, PagedRequest request, CancellationToken ct)
    {
        var (items, totalCount) = await _unitOfWork.AppUsers.GetPagedByTenantAsync(
            tenantId, request.Page, request.PageSize, request.Search, ct);

        return new PagedResponse<UserDto>(
            items.Select(MapToDto).ToList(),
            totalCount, request.Page, request.PageSize);
    }

    public async Task<UserDto> CreateAsync(Guid tenantId, CreateUserRequest request, Guid createdByUserId, CancellationToken ct)
    {
        var result = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var normalizedUsername = request.Username.ToUpperInvariant();
            var normalizedEmail = request.Email.ToUpperInvariant();
            var requestedRoles = request.Roles ?? [];
            await EnsureTenantCanAddActiveUserAsync(tenantId, ct);

            if (await _unitOfWork.AppUsers.ExistsWithUsernameAsync(tenantId, normalizedUsername, ct: ct))
                throw new ConflictException($"El nombre de usuario '{request.Username}' ya está en uso.");
            if (await _unitOfWork.AppUsers.ExistsWithEmailAsync(tenantId, normalizedEmail, ct: ct))
                throw new ConflictException($"El email '{request.Email}' ya está registrado.");

            if (requestedRoles
                .GroupBy(item => new { item.RoleId, item.BusinessId })
                .Any(group => group.Count() > 1))
                throw new ConflictException("No se puede asignar dos veces el mismo rol en el mismo alcance.");

            var offlinePassword = PosOfflinePasswordHasher.Hash(request.Password, DateTimeOffset.UtcNow);
            var user = new AppUser
            {
                UserId = Guid.NewGuid(),
                TenantId = tenantId,
                Username = request.Username,
                PartyId = request.PartyId,
                NormalizedUsername = normalizedUsername,
                Email = request.Email,
                NormalizedEmail = normalizedEmail,
                PasswordHash = _passwordHasher.Hash(request.Password),
                PosOfflinePasswordSalt = offlinePassword.Salt,
                PosOfflinePasswordHash = offlinePassword.Hash,
                PosOfflinePasswordIterations = offlinePassword.Iterations,
                PosOfflinePasswordChangedAt = offlinePassword.ChangedAt,
                FirstName = request.FirstName,
                LastName = request.LastName,
                PhoneNumber = request.PhoneNumber,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = createdByUserId
            };

            await _unitOfWork.AppUsers.AddAsync(user, ct);
            foreach (var assignment in requestedRoles)
            {
                var role = await AuthorizeRoleDelegationAsync(user, assignment.RoleId, createdByUserId, ct);
                if (!role.IsActive) throw new ConflictException("El rol está inactivo.");
                if (assignment.BusinessId.HasValue)
                {
                    var business = await _unitOfWork.Businesses.GetByIdAsync(assignment.BusinessId.Value)
                        ?? throw new NotFoundException(nameof(Business), assignment.BusinessId.Value);
                    if (business.TenantId != tenantId)
                        throw new ForbiddenException("El negocio, el rol y el usuario deben pertenecer a la misma organización.");
                }

                await _unitOfWork.UserRoles.AddAsync(new UserRole
                {
                    UserRoleId = Guid.NewGuid(),
                    UserId = user.UserId,
                    RoleId = role.RoleId,
                    BusinessId = assignment.BusinessId,
                    AssignedAt = DateTime.UtcNow,
                    AssignedByUserId = createdByUserId
                }, ct);
            }
            await _unitOfWork.SaveChangesAsync(ct);
            await _securitySynchronization.EnqueueTenantAsync(tenantId, ct);
            _logger.LogInformation(
                "User {Username} created by {CreatedBy} [CorrelationId: {CorrelationId}]",
                user.Username, createdByUserId, _correlationIdProvider.CorrelationId);
            var created = await _unitOfWork.AppUsers.GetWithRolesAndPermissionsAsync(user.UserId, ct)
                ?? throw new NotFoundException(nameof(AppUser), user.UserId);
            return MapToDto(created);
        }, ct);
        await DispatchSecurityAsync(tenantId, ct);
        return result;
    }

    public async Task<UserDto> UpdateAsync(Guid userId, UpdateUserRequest request, CancellationToken ct)
    {
        var result = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var user = await _unitOfWork.AppUsers.GetByIdAsync(userId, ct)
                ?? throw new NotFoundException(nameof(AppUser), userId);
            if (request.FirstName is not null) user.FirstName = request.FirstName;
            if (request.LastName is not null) user.LastName = request.LastName;
            if (request.PhoneNumber is not null) user.PhoneNumber = request.PhoneNumber;
            if (request.AvatarUrl is not null) user.AvatarUrl = request.AvatarUrl;
            user.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.AppUsers.Update(user);
            await _unitOfWork.SaveChangesAsync(ct);
            await _securitySynchronization.EnqueueTenantAsync(user.TenantId, ct);
            return (User: MapToDto(user), user.TenantId);
        }, ct);
        await DispatchSecurityAsync(result.TenantId, ct);
        return result.User;
    }

    public async Task ResetPasswordAsync(Guid userId, ResetUserPasswordRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 10)
            throw new DomainValidationException("password", "La contraseña debe tener al menos 10 caracteres.");

        var tenantId = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var user = await _unitOfWork.AppUsers.GetByIdAsync(userId, ct)
                ?? throw new NotFoundException(nameof(AppUser), userId);
            var offlinePassword = PosOfflinePasswordHasher.Hash(request.Password, DateTimeOffset.UtcNow);
            user.PasswordHash = _passwordHasher.Hash(request.Password);
            user.PosOfflinePasswordSalt = offlinePassword.Salt;
            user.PosOfflinePasswordHash = offlinePassword.Hash;
            user.PosOfflinePasswordIterations = offlinePassword.Iterations;
            user.PosOfflinePasswordChangedAt = offlinePassword.ChangedAt;
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
            user.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.AppUsers.Update(user);
            await _unitOfWork.RefreshTokens.RevokeAllByUserIdAsync(userId, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await _securitySynchronization.EnqueueTenantAsync(user.TenantId, ct);
            return user.TenantId;
        }, ct);
        await DispatchSecurityAsync(tenantId, ct);
    }
    public async Task DeactivateAsync(Guid userId, CancellationToken ct)
    {
        var tenantId = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var user = await _unitOfWork.AppUsers.GetByIdAsync(userId, ct)
                ?? throw new NotFoundException(nameof(AppUser), userId);
            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.AppUsers.Update(user);
            await _unitOfWork.RefreshTokens.RevokeAllByUserIdAsync(userId, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await _securitySynchronization.EnqueueTenantAsync(user.TenantId, ct);
            return user.TenantId;
        }, ct);
        await DispatchSecurityAsync(tenantId, ct);
    }

    public async Task ActivateAsync(Guid userId, CancellationToken ct)
    {
        var result = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var user = await _unitOfWork.AppUsers.GetByIdAsync(userId, ct)
                ?? throw new NotFoundException(nameof(AppUser), userId);
            if (user.IsActive) return (user.TenantId, Changed: false);

            await EnsureTenantCanAddActiveUserAsync(user.TenantId, ct);
            user.IsActive = true;
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
            user.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.AppUsers.Update(user);
            await _unitOfWork.SaveChangesAsync(ct);
            await _securitySynchronization.EnqueueTenantAsync(user.TenantId, ct);
            return (user.TenantId, Changed: true);
        }, ct);
        if (result.Changed) await DispatchSecurityAsync(result.TenantId, ct);
    }

    public async Task AssignRoleAsync(Guid userId, AssignRoleRequest request, Guid assignedByUserId, CancellationToken ct)
    {
        var tenantId = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var user = await _unitOfWork.AppUsers.GetByIdAsync(userId, ct)
                ?? throw new NotFoundException(nameof(AppUser), userId);
            var role = await AuthorizeRoleDelegationAsync(user, request.RoleId, assignedByUserId, ct);
            if (!role.IsActive) throw new ConflictException("El rol está inactivo.");

            if (request.BusinessId.HasValue)
            {
                var business = await _unitOfWork.Businesses.GetByIdAsync(request.BusinessId.Value)
                    ?? throw new NotFoundException(nameof(Business), request.BusinessId.Value);
                if (business.TenantId != user.TenantId)
                    throw new ForbiddenException("El negocio y el usuario deben pertenecer a la misma organización.");
            }

            if (await _unitOfWork.UserRoles.ExistsAsync(userId, request.RoleId, request.BusinessId, ct))
                throw new ConflictException("El usuario ya tiene asignado este rol en el scope indicado.");

            await _unitOfWork.UserRoles.AddAsync(new UserRole
            {
                UserRoleId = Guid.NewGuid(), UserId = userId, RoleId = request.RoleId,
                BusinessId = request.BusinessId, AssignedAt = DateTime.UtcNow,
                AssignedByUserId = assignedByUserId
            }, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await _securitySynchronization.EnqueueTenantAsync(user.TenantId, ct);
            return user.TenantId;
        }, ct);
        await DispatchSecurityAsync(tenantId, ct);
    }

    public async Task RemoveRoleAsync(Guid userId, Guid roleId, Guid? businessId, Guid actorUserId, CancellationToken ct)
    {
        var tenantId = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var user = await _unitOfWork.AppUsers.GetByIdAsync(userId, ct)
                ?? throw new NotFoundException(nameof(AppUser), userId);
            await AuthorizeRoleDelegationAsync(user, roleId, actorUserId, ct);
            var userRole = await _unitOfWork.UserRoles.GetAsync(userId, roleId, businessId, ct)
                ?? throw new NotFoundException("UserRole", $"{userId}/{roleId}/{businessId}");
            _unitOfWork.UserRoles.Delete(userRole);
            await _unitOfWork.SaveChangesAsync(ct);
            await _securitySynchronization.EnqueueTenantAsync(user.TenantId, ct);
            return user.TenantId;
        }, ct);
        await DispatchSecurityAsync(tenantId, ct);
    }

    private async Task<AppRole> AuthorizeRoleDelegationAsync(AppUser targetUser, Guid roleId, Guid actorUserId, CancellationToken ct)
    {
        var role = await _unitOfWork.AppRoles.GetWithPermissionsAsync(roleId, ct)
            ?? throw new NotFoundException(nameof(AppRole), roleId);
        var actor = await _unitOfWork.AppUsers.GetByIdAsync(actorUserId, ct)
            ?? throw new NotFoundException(nameof(AppUser), actorUserId);
        if (targetUser.TenantId != actor.TenantId || role.TenantId != targetUser.TenantId)
            throw new ForbiddenException("El usuario, el rol y quien lo asigna deben pertenecer a la misma organización.");

        var actorPermissions = (await _unitOfWork.Permissions.GetResourcesByUserIdAsync(actorUserId, null, ct)).ToHashSet(StringComparer.Ordinal);
        var rolePermissions = role.RolePermissions.Select(item => item.Permission.Resource).ToArray();
        var unauthorized = rolePermissions.FirstOrDefault(resource => !actorPermissions.Contains(resource));
        if (unauthorized is not null)
            throw new ForbiddenException($"No puede delegar un rol con el permiso '{unauthorized}' porque no lo posee.");

        if (rolePermissions.Any(PlatformPermissions.IsPlatformPermission))
        {
            if (!string.Equals(actor.Tenant.TenantKey, PlatformPermissions.PlatformTenantKey, StringComparison.OrdinalIgnoreCase)
                || !actorPermissions.Contains(PlatformPermissions.Assign))
                throw new ForbiddenException("Los roles con permisos de plataforma solo se pueden delegar dentro de @auraly por un usuario autorizado.");
        }
        return role;
    }
    public async Task<IReadOnlyList<string>> GetUserPermissionsAsync(Guid userId, Guid? businessId, CancellationToken ct)
    {
        return await _unitOfWork.Permissions.GetResourcesByUserIdAsync(userId, businessId, ct);
    }

    private async Task EnsureTenantCanAddActiveUserAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await _unitOfWork.Tenants.GetByIdForCapacityUpdateAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);
        if (!tenant.IsActive)
            throw new ConflictException("La organizaci\u00f3n est\u00e1 inactiva. Act\u00edvala antes de crear o reactivar usuarios.");

        var activeUsers = await _unitOfWork.AppUsers.CountActiveByTenantAsync(tenantId, ct);
        if (activeUsers >= tenant.MaximumUsers)
            throw new ConflictException($"La organizaci\u00f3n alcanz\u00f3 su capacidad de {tenant.MaximumUsers} usuarios activos. Inactiva un usuario o solicita a un administrador de Auraly ampliar el cupo.");
    }

    private async Task DispatchSecurityAsync(Guid tenantId, CancellationToken ct)
    {
        var businesses = await _unitOfWork.Businesses.GetByTenantIdAsync(tenantId, ct);
        foreach (var business in businesses.Where(item => item.IsActive))
            await _synchronization.DispatchPendingAsync(
                tenantId, business.BusinessId, CancellationToken.None);
    }

    private static UserDto MapToDto(AppUser user) => new(
        user.UserId, user.TenantId, user.PartyId, user.Username, user.Email,
        user.FirstName, user.LastName, user.PhoneNumber, user.AvatarUrl,
        user.IsActive, user.EmailConfirmed, user.LastLoginAt, user.CreatedAt,
        user.UserRoles.Select(ur => new UserRoleDto(
            ur.RoleId, ur.Role.Name, ur.BusinessId,
            ur.Business?.Name, ur.AssignedAt)).ToList());
}
