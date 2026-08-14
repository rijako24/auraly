using Auraly.Contracts.Authorization;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Auth.Interfaces;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Common.Interfaces;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public class UserService : IUserService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ICorrelationIdProvider correlationIdProvider,
        ILogger<UserService> logger)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _correlationIdProvider = correlationIdProvider;
        _logger = logger;
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
        var normalizedUsername = request.Username.ToUpperInvariant();
        var normalizedEmail = request.Email.ToUpperInvariant();

        if (await _unitOfWork.AppUsers.ExistsWithUsernameAsync(tenantId, normalizedUsername, ct: ct))
            throw new ConflictException($"El nombre de usuario '{request.Username}' ya está en uso.");

        if (await _unitOfWork.AppUsers.ExistsWithEmailAsync(tenantId, normalizedEmail, ct: ct))
            throw new ConflictException($"El email '{request.Email}' ya está registrado.");

        var offlinePassword = PosOfflinePasswordHasher.Hash(
            request.Password, DateTimeOffset.UtcNow);
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
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "User {Username} created by {CreatedBy} [CorrelationId: {CorrelationId}]",
            user.Username, createdByUserId, _correlationIdProvider.CorrelationId);

        return MapToDto(user);
    }

    public async Task<UserDto> UpdateAsync(Guid userId, UpdateUserRequest request, CancellationToken ct)
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

        return MapToDto(user);
    }

    public async Task DeactivateAsync(Guid userId, CancellationToken ct)
    {
        var user = await _unitOfWork.AppUsers.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(AppUser), userId);

        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.AppUsers.Update(user);

        await _unitOfWork.RefreshTokens.RevokeAllByUserIdAsync(userId, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task ActivateAsync(Guid userId, CancellationToken ct)
    {
        var user = await _unitOfWork.AppUsers.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(AppUser), userId);

        user.IsActive = true;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        user.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.AppUsers.Update(user);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task AssignRoleAsync(Guid userId, AssignRoleRequest request, Guid assignedByUserId, CancellationToken ct)
    {
        var user = await _unitOfWork.AppUsers.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(AppUser), userId);

        var role = await _unitOfWork.AppRoles.GetByIdAsync(request.RoleId, ct)
            ?? throw new NotFoundException(nameof(AppRole), request.RoleId);

        if (await _unitOfWork.UserRoles.ExistsAsync(userId, request.RoleId, request.BusinessId, ct))
            throw new ConflictException("El usuario ya tiene asignado este rol en el scope indicado.");

        await _unitOfWork.UserRoles.AddAsync(new UserRole
        {
            UserRoleId = Guid.NewGuid(),
            UserId = userId,
            RoleId = request.RoleId,
            BusinessId = request.BusinessId,
            AssignedAt = DateTime.UtcNow,
            AssignedByUserId = assignedByUserId
        }, ct);

        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task RemoveRoleAsync(Guid userId, Guid roleId, Guid? businessId, CancellationToken ct)
    {
        var userRole = await _unitOfWork.UserRoles.GetAsync(userId, roleId, businessId, ct)
            ?? throw new NotFoundException("UserRole", $"{userId}/{roleId}/{businessId}");

        _unitOfWork.UserRoles.Delete(userRole);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetUserPermissionsAsync(Guid userId, Guid? businessId, CancellationToken ct)
    {
        return await _unitOfWork.Permissions.GetResourcesByUserIdAsync(userId, businessId, ct);
    }

    private static UserDto MapToDto(AppUser user) => new(
        user.UserId, user.TenantId, user.Username, user.Email,
        user.FirstName, user.LastName, user.PhoneNumber, user.AvatarUrl,
        user.IsActive, user.EmailConfirmed, user.LastLoginAt, user.CreatedAt,
        user.UserRoles.Select(ur => new UserRoleDto(
            ur.RoleId, ur.Role.Name, ur.BusinessId,
            ur.Business?.Name, ur.AssignedAt)).ToList());
}
