using System.Data;
using Auraly.Contracts.Authorization;
using Auraly.Infrastructure.Persistence;
using Auraly.Platform.Application.Auth.Interfaces;
using Microsoft.Data.SqlClient;

namespace Auraly.Api;

public static class SellerUserAccessApi
{
    public sealed record CreateSellerUserAccessRequest(
        string Username,
        string Email,
        string Password,
        string FirstName,
        string LastName,
        string? PhoneNumber);

    public sealed record SellerUserAccessResult(
        Guid UserId,
        Guid PartyId,
        string Username,
        string Email,
        bool IsActive,
        string RoleName,
        Guid BusinessId);

    public static IEndpointRouteBuilder MapSellerUserAccessApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/parties")
            .RequireAuthorization();

        group.MapGet("/{partyId:guid}/seller-access", async (
            HttpContext context,
            SellerUserAccessService service,
            Guid partyId,
            CancellationToken ct) =>
            await Execute(() => service.GetAsync(
                context.User.ToPartyUserIdentity(), partyId, ct)));

        group.MapPost("/{partyId:guid}/seller-access", async (
            HttpContext context,
            SellerUserAccessService service,
            Guid partyId,
            CreateSellerUserAccessRequest request,
            CancellationToken ct) =>
            await Execute(async () => (SellerUserAccessResult?)await service.CreateAsync(
                context.User.ToPartyUserIdentity(), partyId, request, ct)));

        return endpoints;
    }

    private static async Task<IResult> Execute<T>(Func<Task<T?>> operation)
    {
        try { return Results.Ok(await operation()); }
        catch (SellerUserAccessForbiddenException error) { return Results.Problem(error.Message, statusCode: 403); }
        catch (SellerUserAccessValidationException error) { return Results.Problem(error.Message, statusCode: 400); }
        catch (SellerUserAccessConflictException error) { return Results.Problem(error.Message, statusCode: 409); }
    }
}

public sealed class SellerUserAccessService(
    SqlServerConnectionFactory connections,
    IPasswordHasher passwordHasher)
{
    private static readonly string[] RequiredPermissions =
        ["users.create", "users.assign_role", "security.users.link-party"];

    public async Task<SellerUserAccessApi.SellerUserAccessResult?> GetAsync(
        Auraly.Contracts.Parties.PartyActorIdentity actor,
        Guid partyId,
        CancellationToken ct)
    {
        Demand(actor);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = Procedure("dbo.SellerUserAccessGet", connection);
        AddScope(command, actor, partyId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public async Task<SellerUserAccessApi.SellerUserAccessResult> CreateAsync(
        Auraly.Contracts.Parties.PartyActorIdentity actor,
        Guid partyId,
        SellerUserAccessApi.CreateSellerUserAccessRequest request,
        CancellationToken ct)
    {
        Demand(actor);
        var username = Required(request.Username, "El usuario", 100);
        var email = Required(request.Email, "El correo", 256);
        var firstName = Required(request.FirstName, "Los nombres", 100);
        var lastName = Required(request.LastName, "Los apellidos", 100);
        var password = request.Password ?? string.Empty;
        if (password.Length < 8 || password.Length > 128)
            throw new SellerUserAccessValidationException("La contraseña debe tener entre 8 y 128 caracteres.");
        var phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        if (phone?.Length > 20)
            throw new SellerUserAccessValidationException("El teléfono supera 20 caracteres.");

        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var offline = PosOfflinePasswordHasher.Hash(password, now);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using var command = Procedure("dbo.SellerUserAccessCreate", connection, transaction);
            AddScope(command, actor, partyId);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@Username", username);
            command.Parameters.AddWithValue("@NormalizedUsername", username.ToUpperInvariant());
            command.Parameters.AddWithValue("@Email", email);
            command.Parameters.AddWithValue("@NormalizedEmail", email.ToUpperInvariant());
            command.Parameters.AddWithValue("@PasswordHash", passwordHasher.Hash(password));
            command.Parameters.AddWithValue("@OfflineSalt", offline.Salt);
            command.Parameters.AddWithValue("@OfflineHash", offline.Hash);
            command.Parameters.AddWithValue("@OfflineIterations", offline.Iterations);
            command.Parameters.AddWithValue("@OfflineChangedAt", offline.ChangedAt);
            command.Parameters.AddWithValue("@FirstName", firstName);
            command.Parameters.AddWithValue("@LastName", lastName);
            command.Parameters.AddWithValue("@PhoneNumber", (object?)phone ?? DBNull.Value);
            command.Parameters.AddWithValue("@Now", now);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new SellerUserAccessConflictException("No fue posible crear el acceso del vendedor.");
            var result = Read(reader);
            await reader.CloseAsync();
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (SqlException error) when (error.Number == 51910)
        {
            await transaction.RollbackAsync(ct);
            throw new SellerUserAccessForbiddenException(error.Message);
        }
        catch (SqlException error) when (error.Number is 51911 or 51912 or 51913 or 2601 or 2627)
        {
            await transaction.RollbackAsync(ct);
            throw new SellerUserAccessConflictException(error.Message);
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void Demand(Auraly.Contracts.Parties.PartyActorIdentity actor)
    {
        if (RequiredPermissions.Any(permission => !actor.Permissions.Contains(permission)))
            throw new SellerUserAccessForbiddenException(
                "Se requieren permisos para crear usuarios, asignar roles y enlazar terceros.");
    }

    private static void AddScope(
        SqlCommand command,
        Auraly.Contracts.Parties.PartyActorIdentity actor,
        Guid partyId)
    {
        command.Parameters.AddWithValue("@TenantId", actor.TenantId);
        command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
        command.Parameters.AddWithValue("@ActorUserId", actor.ActorId);
        command.Parameters.AddWithValue("@PartyId", partyId);
    }

    private static string Required(string? value, string label, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new SellerUserAccessValidationException($"{label} es obligatorio.");
        if (normalized.Length > maxLength)
            throw new SellerUserAccessValidationException($"{label} supera {maxLength} caracteres.");
        return normalized;
    }

    private static SellerUserAccessApi.SellerUserAccessResult Read(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
        reader.GetBoolean(4), reader.GetString(5), reader.GetGuid(6));

    private static SqlCommand Procedure(string name, SqlConnection connection, SqlTransaction? transaction = null) =>
        new(name, connection, transaction) { CommandType = CommandType.StoredProcedure };
}

public sealed class SellerUserAccessForbiddenException(string message) : Exception(message);
public sealed class SellerUserAccessValidationException(string message) : Exception(message);
public sealed class SellerUserAccessConflictException(string message) : Exception(message);
