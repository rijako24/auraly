using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Authentication;

namespace Auraly.Application.Sales;

public sealed record RestaurantAttentionRequester(
    Guid UserId, Guid TenantId, Guid BusinessId, Guid AuthenticationSessionId,
    IReadOnlySet<string> Permissions);

public sealed record RestaurantWaiterOption(Guid UserId, string Name, string? ShortName);
public sealed record RestaurantWaiterPage(
    bool RequireCredential, bool RestrictTableToWaiter,
    IReadOnlyList<RestaurantWaiterOption> Items, int Page, int PageSize,
    int TotalCount);

public sealed record RestaurantAttentionCandidate(
    Guid UserId, Guid TableId, bool RequireCredential,
    bool RestrictTableToWaiter, Guid? ResponsibleUserId,
    string? PrimaryPasswordHash, byte[]? SecondarySalt,
    byte[]? SecondaryHash, int? SecondaryIterations,
    byte[]? SupervisorSalt, byte[]? SupervisorHash,
    int? SupervisorIterations, int FailedCount,
    DateTimeOffset? LockedUntil);

public sealed record RestaurantAttentionTicket(
    Guid AttentionSessionId, Guid TenantId, Guid BusinessId, Guid TableId,
    Guid ConnectedUserId, Guid AuthenticationSessionId, Guid WaiterUserId,
    byte[] TokenHash, string Method, DateTimeOffset IssuedAt,
    Guid? ExpectedResponsibleUserId = null);

public sealed record RestaurantAttentionGrant(
    Guid TableId, Guid WaiterUserId, Guid ConnectedUserId,
    string Method, bool RestrictTableToWaiter, Guid? ResponsibleUserId);

public sealed record RestaurantAttentionStarted(
    string Token, Guid TableId, Guid WaiterUserId);

public interface IRestaurantAttentionStore
{
    Task<RestaurantWaiterPage> WaitersAsync(RestaurantAttentionRequester requester,
        string? search, int page, int pageSize, CancellationToken ct);
    Task<RestaurantAttentionCandidate?> CandidateAsync(
        RestaurantAttentionRequester requester, Guid tableId,
        Guid waiterUserId, CancellationToken ct);
    Task RecordFailedCredentialAsync(Guid tenantId, Guid waiterUserId,
        DateTimeOffset now, CancellationToken ct);
    Task ClearFailedCredentialAsync(Guid tenantId, Guid waiterUserId,
        DateTimeOffset now, CancellationToken ct);
    Task SaveAsync(RestaurantAttentionTicket ticket, CancellationToken ct);
    Task<RestaurantAttentionGrant?> ResolveAsync(
        RestaurantAttentionRequester requester, Guid attentionSessionId,
        byte[] tokenHash, DateTimeOffset now, CancellationToken ct);
    Task EndAsync(RestaurantAttentionRequester requester,
        Guid attentionSessionId, byte[] tokenHash, DateTimeOffset now,
        CancellationToken ct);
    Task ConfigureSecondaryAsync(RestaurantAttentionRequester requester,
        Guid waiterUserId, byte[] salt, byte[] hash, int iterations,
        DateTimeOffset now, CancellationToken ct);
    Task<bool> HasSecondaryAsync(RestaurantAttentionRequester requester,
        Guid waiterUserId, CancellationToken ct);
    Task RevokeSecondaryAsync(RestaurantAttentionRequester requester,
        Guid waiterUserId, CancellationToken ct);
}

public sealed class RestaurantAttentionService(
    IRestaurantAttentionStore store,
    IAuthenticationPasswordVerifier primaryPasswordVerifier,
    TimeProvider time)
{
    private const int SecondaryIterations = 210_000;

    public Task<RestaurantWaiterPage> WaitersAsync(
        RestaurantAttentionRequester requester, string? search, int page,
        CancellationToken ct)
    {
        DemandConnectedAccess(requester);
        if (page < 1 || search?.Length > 100)
            throw new RestaurantAccountValidationException("Página o búsqueda inválida.");
        return store.WaitersAsync(requester, search?.Trim(), page, 40, ct);
    }

    public async Task<RestaurantAttentionStarted> StartAsync(
        RestaurantAttentionRequester requester, Guid tableId,
        Guid waiterUserId, string? secret, CancellationToken ct)
    {
        DemandConnectedAccess(requester);
        if (tableId == Guid.Empty || waiterUserId == Guid.Empty)
            throw new RestaurantAccountValidationException(
                "Selecciona una mesa y un mesero válidos.");
        var candidate = await store.CandidateAsync(requester, tableId,
            waiterUserId, ct) ?? throw new RestaurantAccountForbiddenException(
                "La mesa o el mesero no está disponible para atender.");
        var now = time.GetUtcNow();
        var canTransfer = requester.Permissions.Contains(
            "restaurant.tables.open_without_waiter");
        if (candidate.RestrictTableToWaiter && !canTransfer &&
            candidate.ResponsibleUserId is { } responsible &&
            responsible != waiterUserId)
            throw new RestaurantAccountForbiddenException(
                "Esta mesa pertenece a otro mesero. Se requiere aprobación supervisora.");

        var method = "Selection";
        if (candidate.RequireCredential)
        {
            if (string.IsNullOrEmpty(secret) || secret.Length > 128 ||
                candidate.LockedUntil is { } locked && locked > now)
                throw new RestaurantAccountForbiddenException(
                    "No fue posible validar la clave del mesero.");
            if (candidate.PrimaryPasswordHash is { } primary &&
                primaryPasswordVerifier.Verify(secret, primary))
                method = "Primary";
            else if (MatchesSecondary(secret, candidate.SecondarySalt,
                candidate.SecondaryHash, candidate.SecondaryIterations) ||
                MatchesSecondary(secret, candidate.SupervisorSalt,
                    candidate.SupervisorHash, candidate.SupervisorIterations))
                method = "Secondary";
            else
            {
                await store.RecordFailedCredentialAsync(requester.TenantId,
                    waiterUserId, now, ct);
                throw new RestaurantAccountForbiddenException(
                    "No fue posible validar la clave del mesero.");
            }
            await store.ClearFailedCredentialAsync(requester.TenantId,
                waiterUserId, now, ct);
        }

        var id = Guid.NewGuid();
        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var token = $"{id:N}.{Convert.ToHexString(secretBytes)}";
        await store.SaveAsync(new(id, requester.TenantId, requester.BusinessId,
            tableId, requester.UserId, requester.AuthenticationSessionId,
            waiterUserId, SHA256.HashData(secretBytes), method, now,
            canTransfer && candidate.ResponsibleUserId != waiterUserId
                ? candidate.ResponsibleUserId : null), ct);
        return new(token, tableId, waiterUserId);
    }

    public async Task<RestaurantAccountActor> ResolveActorAsync(
        RestaurantAttentionRequester requester, string token,
        CancellationToken ct)
    {
        DemandConnectedAccess(requester);
        var (id, hash) = Parse(token);
        var grant = await store.ResolveAsync(requester, id, hash,
            time.GetUtcNow(), ct) ?? throw new RestaurantAccountForbiddenException(
                "La atención de esta mesa terminó. Identifica nuevamente al mesero.");
        if (grant.RestrictTableToWaiter &&
            grant.ResponsibleUserId is { } responsible &&
            responsible != grant.WaiterUserId)
            throw new RestaurantAccountForbiddenException(
                "Esta mesa pertenece a otro mesero.");
        return new(grant.WaiterUserId, requester.TenantId,
            requester.BusinessId, new HashSet<string>(StringComparer.Ordinal)
            { "restaurant.attend" }, requester.UserId, grant.TableId,
            grant.Method != "Selection");
    }

    public async Task EndAsync(RestaurantAttentionRequester requester,
        string token, CancellationToken ct)
    {
        var (id, hash) = Parse(token);
        await store.EndAsync(requester, id, hash, time.GetUtcNow(), ct);
    }

    public async Task ConfigureSecondaryAsync(
        RestaurantAttentionRequester requester, Guid waiterUserId,
        string secret, CancellationToken ct)
    {
        DemandManageCredential(requester);
        if (waiterUserId == Guid.Empty || secret is null || secret.Length != 4 ||
            !secret.All(char.IsAsciiDigit))
            throw new RestaurantAccountValidationException(
                "La clave secundaria de atención debe tener cuatro dígitos.");
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt,
            SecondaryIterations, HashAlgorithmName.SHA256, 32);
        await store.ConfigureSecondaryAsync(requester, waiterUserId,
            salt, hash, SecondaryIterations, time.GetUtcNow(), ct);
    }

    public Task<bool> HasSecondaryAsync(RestaurantAttentionRequester requester,
        Guid waiterUserId, CancellationToken ct)
    {
        DemandManageCredential(requester);
        return store.HasSecondaryAsync(requester, waiterUserId, ct);
    }

    public Task RevokeSecondaryAsync(RestaurantAttentionRequester requester,
        Guid waiterUserId, CancellationToken ct)
    {
        DemandManageCredential(requester);
        return store.RevokeSecondaryAsync(requester, waiterUserId, ct);
    }

    private static bool MatchesSecondary(string secret, byte[]? salt,
        byte[]? hash, int? iterations)
    {
        if (salt is null || hash is null || iterations is null) return false;
        var derived = Rfc2898DeriveBytes.Pbkdf2(secret, salt,
            iterations.Value, HashAlgorithmName.SHA256, hash.Length);
        return CryptographicOperations.FixedTimeEquals(derived, hash);
    }

    private static (Guid Id, byte[] Hash) Parse(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new RestaurantAccountForbiddenException("La atención de mesa no es válida.");
        var parts = token.Split('.', 2);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var id) ||
            parts[1].Length != 64)
            throw new RestaurantAccountForbiddenException("La atención de mesa no es válida.");
        try { return (id, SHA256.HashData(Convert.FromHexString(parts[1]))); }
        catch (FormatException)
        { throw new RestaurantAccountForbiddenException("La atención de mesa no es válida."); }
    }

    private static void DemandConnectedAccess(RestaurantAttentionRequester requester)
    {
        if (requester.UserId == Guid.Empty || requester.TenantId == Guid.Empty ||
            requester.BusinessId == Guid.Empty ||
            requester.AuthenticationSessionId == Guid.Empty ||
            !requester.Permissions.Contains("sales.create") &&
            !requester.Permissions.Contains("restaurant.attend") &&
            !requester.Permissions.Contains("restaurant.tables.open_without_waiter"))
            throw new RestaurantAccountForbiddenException(
                "El usuario conectado no puede atender mesas.");
    }

    private static void DemandManageCredential(RestaurantAttentionRequester requester)
    {
        if (!requester.Permissions.Contains("pos.approvals.manage_credential"))
            throw new RestaurantAccountForbiddenException(
                "No puedes configurar la clave secundaria del mesero.");
    }
}
