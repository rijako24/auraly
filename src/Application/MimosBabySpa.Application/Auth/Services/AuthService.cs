using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Auth.DTOs;
using MimosBabySpa.Application.Auth.Interfaces;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Common.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Auth.Services;

public class AuthService : IAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly IGoogleAuthService _googleAuthService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ILogger<AuthService> _logger;

    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public AuthService(
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        IGoogleAuthService googleAuthService,
        IPasswordHasher passwordHasher,
        ICorrelationIdProvider correlationIdProvider,
        ILogger<AuthService> logger)
    {
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _googleAuthService = googleAuthService;
        _passwordHasher = passwordHasher;
        _correlationIdProvider = correlationIdProvider;
        _logger = logger;
    }

    public async Task<LoginResponse> LoginAsync(
        LoginRequest request, string? ipAddress, string? deviceInfo, CancellationToken ct)
    {
        _logger.LogInformation("Login attempt for user {Username} [CorrelationId: {CorrelationId}]",
            request.Username, _correlationIdProvider.CorrelationId);

        var user = await _unitOfWork.AppUsers.GetByUsernameAsync(
            request.Username.ToUpperInvariant(), ct);

        if (user is null || !user.IsActive)
            throw new UnauthorizedAccessException("Credenciales inválidas.");

        if (user.IsLockedOut)
            throw new UnauthorizedAccessException(
                $"Cuenta bloqueada hasta {user.LockoutEnd:yyyy-MM-dd HH:mm} UTC.");

        if (string.IsNullOrEmpty(user.PasswordHash))
            throw new UnauthorizedAccessException(
                "Esta cuenta solo permite autenticación con proveedor externo.");

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            user.RecordFailedLogin(MaxFailedAttempts, LockoutDuration);
            _unitOfWork.AppUsers.Update(user);
            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogWarning("Failed login for user {Username}. Attempts: {Attempts} [CorrelationId: {CorrelationId}]",
                request.Username, user.AccessFailedCount, _correlationIdProvider.CorrelationId);

            throw new UnauthorizedAccessException("Credenciales inválidas.");
        }

        user.RecordSuccessfulLogin();
        _unitOfWork.AppUsers.Update(user);

        var response = await BuildLoginResponseAsync(user, ipAddress, deviceInfo, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("User {Username} logged in successfully [CorrelationId: {CorrelationId}]",
            user.Username, _correlationIdProvider.CorrelationId);

        return response;
    }

    public async Task<LoginResponse> GoogleLoginAsync(
        GoogleLoginRequest request, string? ipAddress, string? deviceInfo, CancellationToken ct)
    {
        var googleInfo = await _googleAuthService.ValidateGoogleTokenAsync(request.IdToken, ct);

        var externalLogin = await _unitOfWork.UserExternalLogins.GetAsync("Google", googleInfo.GoogleId, ct);
        AppUser? user;

        if (externalLogin is not null)
        {
            user = await _unitOfWork.AppUsers.GetByIdAsync(externalLogin.UserId, ct);
        }
        else
        {
            user = await _unitOfWork.AppUsers.GetByEmailAsync(googleInfo.Email.ToUpperInvariant(), ct);
        }

        if (user is null)
        {
            if (!request.TenantId.HasValue)
                throw new UnauthorizedAccessException(
                    "No existe una cuenta asociada a este email. Contacte al administrador.");

            var baseUsername = googleInfo.Email.Split('@')[0];
            var normalizedUsername = baseUsername.ToUpperInvariant();
            if (await _unitOfWork.AppUsers.ExistsWithUsernameAsync(normalizedUsername, ct: ct))
                normalizedUsername = $"{normalizedUsername}_{Guid.NewGuid():N}".ToUpperInvariant()[..50];

            user = new AppUser
            {
                UserId = Guid.NewGuid(),
                TenantId = request.TenantId.Value,
                Username = baseUsername.Length > 50 ? baseUsername[..50] : baseUsername,
                NormalizedUsername = normalizedUsername,
                Email = googleInfo.Email,
                NormalizedEmail = googleInfo.Email.ToUpperInvariant(),
                FirstName = googleInfo.FirstName,
                LastName = googleInfo.LastName,
                AvatarUrl = googleInfo.PictureUrl,
                EmailConfirmed = googleInfo.EmailVerified,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.AppUsers.AddAsync(user, ct);
        }

        if (externalLogin is null)
        {
            await _unitOfWork.UserExternalLogins.AddAsync(new UserExternalLogin
            {
                ExternalLoginId = Guid.NewGuid(),
                UserId = user.UserId,
                Provider = "Google",
                ProviderKey = googleInfo.GoogleId,
                ProviderDisplayName = $"{googleInfo.FirstName} {googleInfo.LastName}",
                ProviderEmail = googleInfo.Email,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }

        if (!user.IsActive)
            throw new UnauthorizedAccessException("Cuenta desactivada.");

        user.RecordSuccessfulLogin();
        user.AvatarUrl ??= googleInfo.PictureUrl;
        _unitOfWork.AppUsers.Update(user);

        var response = await BuildLoginResponseAsync(user, ipAddress, deviceInfo, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return response;
    }

    public async Task<LoginResponse> RefreshTokenAsync(
        RefreshTokenRequest request, string? ipAddress, CancellationToken ct)
    {
        var principal = _tokenService.ValidateExpiredToken(request.AccessToken)
            ?? throw new UnauthorizedAccessException("Token de acceso inválido.");

        var existingToken = await _unitOfWork.RefreshTokens.GetByTokenAsync(request.RefreshToken, ct)
            ?? throw new UnauthorizedAccessException("Refresh token inválido.");

        if (!existingToken.IsActive)
        {
            _logger.LogWarning(
                "Reuse of revoked refresh token detected for UserId {UserId} [CorrelationId: {CorrelationId}]",
                existingToken.UserId, _correlationIdProvider.CorrelationId);

            await _unitOfWork.RefreshTokens.RevokeAllByUserIdAsync(existingToken.UserId, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException(
                "Token comprometido. Todas las sesiones han sido revocadas.");
        }

        existingToken.RevokedAt = DateTime.UtcNow;
        _unitOfWork.RefreshTokens.Update(existingToken);

        var user = await _unitOfWork.AppUsers.GetWithRolesAndPermissionsAsync(existingToken.UserId, ct)
            ?? throw new UnauthorizedAccessException("Usuario no encontrado.");

        var response = await BuildLoginResponseAsync(user, ipAddress, null, ct);

        var newStoredToken = await _unitOfWork.RefreshTokens.GetByTokenAsync(response.RefreshToken, ct);
        if (newStoredToken is not null)
            existingToken.ReplacedByTokenId = newStoredToken.RefreshTokenId;

        await _unitOfWork.SaveChangesAsync(ct);
        return response;
    }

    public async Task RevokeTokenAsync(string refreshToken, CancellationToken ct)
    {
        var token = await _unitOfWork.RefreshTokens.GetByTokenAsync(refreshToken, ct);
        if (token is null)
            return;

        if (token.IsActive)
        {
            token.RevokedAt = DateTime.UtcNow;
            _unitOfWork.RefreshTokens.Update(token);
            await _unitOfWork.SaveChangesAsync(ct);
        }
    }

    public async Task RevokeAllUserTokensAsync(Guid userId, CancellationToken ct)
    {
        await _unitOfWork.RefreshTokens.RevokeAllByUserIdAsync(userId, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await _unitOfWork.AppUsers.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(AppUser), userId);

        if (string.IsNullOrEmpty(user.PasswordHash))
            throw new DomainValidationException("Password",
                "Esta cuenta usa autenticación externa. Establezca una contraseña primero.");

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new DomainValidationException("CurrentPassword", "La contraseña actual es incorrecta.");

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.AppUsers.Update(user);

        await _unitOfWork.RefreshTokens.RevokeAllByUserIdAsync(userId, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<AuthUserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await _unitOfWork.AppUsers.GetWithRolesAndPermissionsAsync(userId, ct)
            ?? throw new NotFoundException(nameof(AppUser), userId);

        var roles = user.UserRoles.Select(ur => ur.Role.Name).Distinct().ToList();
        var permissions = await _unitOfWork.Permissions.GetResourcesByUserIdAsync(userId, ct: ct);

        return new AuthUserDto(
            user.UserId, user.TenantId, user.Username, user.Email,
            user.FirstName, user.LastName, user.AvatarUrl,
            roles, permissions.ToList());
    }

    private async Task<LoginResponse> BuildLoginResponseAsync(
        AppUser user, string? ipAddress, string? deviceInfo, CancellationToken ct)
    {
        var userWithRoles = await _unitOfWork.AppUsers.GetWithRolesAndPermissionsAsync(user.UserId, ct) ?? user;
        var roles = userWithRoles.UserRoles.Select(ur => ur.Role.Name).Distinct().ToList();
        var permissions = await _unitOfWork.Permissions.GetResourcesByUserIdAsync(user.UserId, ct: ct);

        var accessToken = _tokenService.GenerateAccessToken(user, roles, permissions.ToList(), user.TenantId);
        var refreshTokenValue = _tokenService.GenerateRefreshToken();

        await _unitOfWork.RefreshTokens.AddAsync(new RefreshToken
        {
            RefreshTokenId = Guid.NewGuid(),
            UserId = user.UserId,
            Token = refreshTokenValue,
            DeviceInfo = deviceInfo,
            IpAddress = ipAddress,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        }, ct);

        return new LoginResponse(
            AccessToken: accessToken,
            RefreshToken: refreshTokenValue,
            AccessTokenExpiresAt: DateTime.UtcNow.AddMinutes(30),
            User: new AuthUserDto(
                user.UserId, user.TenantId, user.Username, user.Email,
                user.FirstName, user.LastName, user.AvatarUrl,
                roles, permissions.ToList()),
            CorrelationId: _correlationIdProvider.CorrelationId);
    }
}
