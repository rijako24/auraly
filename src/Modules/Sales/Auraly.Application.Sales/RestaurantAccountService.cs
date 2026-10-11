using Auraly.Contracts.Sales;

namespace Auraly.Application.Sales;

public sealed record RestaurantAccountActor(
    Guid UserId, Guid TenantId, Guid BusinessId,
    IReadOnlySet<string> Permissions,
    Guid? ConnectedUserId = null,
    Guid? InterventionTableId = null,
    bool CredentialVerified = false,
    bool EnforceWaiterRestriction = false);

public interface IRestaurantAccountStore
{
    Task<RestaurantCancellation> RequestCancellationAsync(
        RestaurantAccountActor actor, Guid accountId, Guid additionId,
        RequestRestaurantCancellation request, DateTimeOffset now,
        CancellationToken ct);
    Task<RestaurantCancellation> ConfirmCancellationAsync(
        RestaurantAccountActor actor, Guid cancellationId,
        ConfirmRestaurantCancellation request, DateTimeOffset now,
        CancellationToken ct);
    Task<IReadOnlyList<RestaurantCancellationComponentOption>>
        CancellationComponentsAsync(RestaurantAccountActor actor,
            Guid cancellationId, CancellationToken ct);
    Task<RestaurantCancellation> ResolveCancellationAsync(
        RestaurantAccountActor actor, Guid cancellationId,
        ResolveRestaurantCancellation request, DateTimeOffset now,
        CancellationToken ct);
    Task<IReadOnlyList<RestaurantCancellation>> CancellationsAsync(
        RestaurantAccountActor actor, Guid accountId, CancellationToken ct);
    Task<RestaurantCommandBoardPage> CommandCardsAsync(
        RestaurantAccountActor actor, Guid? stationId, string? state,
        string? tableCode, int page, CancellationToken ct);
    Task ChangeCommandStateAsync(RestaurantAccountActor actor, Guid cardId,
        string state, byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
    Task ReprintCommandAsync(RestaurantAccountActor actor, Guid cardId,
        DateTimeOffset now, CancellationToken ct);
    Task<RestaurantAccount> OpenAsync(RestaurantAccountActor actor,
        OpenRestaurantAccountRequest request, DateTimeOffset now,
        CancellationToken ct);
    Task<RestaurantAccount?> GetAsync(RestaurantAccountActor actor,
        Guid accountId, CancellationToken ct);
    Task<IReadOnlyList<RestaurantTableOccupancy>> OccupancyAsync(
        RestaurantAccountActor actor, CancellationToken ct);
    Task<RestaurantAccountDraft?> GetDraftAsync(RestaurantAccountActor actor,
        Guid accountId, CancellationToken ct);
    Task<RestaurantPrebill> PrintPrebillAsync(RestaurantAccountActor actor,
        Guid accountId, long expectedVersion, DateTimeOffset now,
        CancellationToken ct);
    Task<RestaurantAccountDraft> AddAsync(RestaurantAccountActor actor,
        Guid accountId, AddRestaurantAccountItemRequest request,
        IRestaurantProductCatalog products, DateTimeOffset now,
        CancellationToken ct);
    Task<RestaurantAccountDraft> ChangeQuantityAsync(
        RestaurantAccountActor actor, Guid accountId, Guid additionId,
        ChangeRestaurantAccountQuantityRequest request,
        DateTimeOffset now, CancellationToken ct);
    Task<RestaurantAccountDraft> ChangeNoteAsync(
        RestaurantAccountActor actor, Guid accountId, Guid additionId,
        ChangeRestaurantAccountNoteRequest request,
        DateTimeOffset now, CancellationToken ct);
    Task<SendRestaurantPendingResult> SendPendingAsync(
        RestaurantAccountActor actor, Guid accountId,
        SendRestaurantPendingRequest request, DateTimeOffset now,
        CancellationToken ct);
}

public interface IRestaurantProductCatalog
{
    Task<OnlineSalesProductPage> SearchAsync(RestaurantAccountActor actor,
        Guid accountId, Guid warehouseId, string? search,
        int skip, int take, Guid? productId, Guid? categoryId,
        CancellationToken ct);
    Task<IReadOnlyList<RestaurantMenuCategory>> CategoriesAsync(
        RestaurantAccountActor actor, Guid accountId,
        Guid warehouseId, CancellationToken ct);
}

public sealed class RestaurantAccountForbiddenException(string message) : Exception(message);
public sealed class RestaurantAccountValidationException(string message) : Exception(message);
public sealed class RestaurantAccountConflictException(string message) : Exception(message);

public sealed class RestaurantAccountService(
    IRestaurantAccountStore store, IRestaurantProductCatalog products,
    TimeProvider time)
{
    private const string OpenWithoutWaiterPermission = "restaurant.tables.open_without_waiter";
    public Task<RestaurantCancellation> RequestCancellationAsync(
        RestaurantAccountActor actor, Guid accountId, Guid additionId,
        RequestRestaurantCancellation request, CancellationToken ct)
    {
        DemandAttention(actor);
        if (accountId == Guid.Empty || additionId == Guid.Empty ||
            request.CancellationId == Guid.Empty ||
            request.ExpectedAccountVersion < 1 ||
            request.Quantity is <= 0 or > 9999 ||
            decimal.Round(request.Quantity, 4) != request.Quantity ||
            string.IsNullOrWhiteSpace(request.ReasonCode) ||
            request.ReasonCode.Trim().Length > 40 ||
            string.IsNullOrWhiteSpace(request.Explanation) ||
            request.Explanation.Trim().Length > 500)
            throw new RestaurantAccountValidationException(
                "La cantidad y el motivo de cancelación son obligatorios.");
        return store.RequestCancellationAsync(
            ForAttention(actor), accountId,
            additionId, request with {
                ReasonCode = request.ReasonCode.Trim().ToUpperInvariant(),
                Explanation = request.Explanation.Trim() },
            time.GetUtcNow(), ct);
    }

    public Task<RestaurantCancellation> ConfirmCancellationAsync(
        RestaurantAccountActor actor, Guid cancellationId,
        ConfirmRestaurantCancellation request, CancellationToken ct)
    {
        DemandIdentity(actor);
        if (!actor.Permissions.Contains("restaurant.commands.manage"))
            throw new RestaurantAccountForbiddenException(
                "No tienes permiso para confirmar la preparación.");
        if (cancellationId == Guid.Empty ||
            request.PreparationState is not ("NotPrepared" or "Prepared" or "Partial") ||
            !ValidVersion(request.RowVersion) ||
            (request.PreparationState != "Partial" &&
             request.ConsumedComponents is { Count: > 0 }) ||
            (request.PreparationState == "Partial" &&
             request.ConsumedComponents is not { Count: > 0 }) ||
            request.ConsumedComponents is { Count: > 100 } ||
            request.ConsumedComponents?.Any(item => item.ProductId == Guid.Empty ||
                item.Quantity <= 0 || item.Quantity > 9999999999999.999999m ||
                decimal.Round(item.Quantity, 6) != item.Quantity) == true ||
            request.ConsumedComponents?.Select(item => item.ProductId)
                .Distinct().Count() != request.ConsumedComponents?.Count)
            throw new RestaurantAccountValidationException(
                "La confirmación de cocina o barra no es válida.");
        return store.ConfirmCancellationAsync(actor, cancellationId,
            request, time.GetUtcNow(), ct);
    }

    public Task<IReadOnlyList<RestaurantCancellationComponentOption>>
        CancellationComponentsAsync(RestaurantAccountActor actor,
            Guid cancellationId, CancellationToken ct)
    {
        DemandIdentity(actor);
        if (!actor.Permissions.Contains("restaurant.commands.manage") &&
            !actor.Permissions.Contains("restaurant.cancellations.approve"))
            throw new RestaurantAccountForbiddenException(
                "No tienes permiso para consultar el consumo de la comanda.");
        if (cancellationId == Guid.Empty)
            throw new RestaurantAccountValidationException(
                "La solicitud de cancelación no es válida.");
        return store.CancellationComponentsAsync(actor, cancellationId, ct);
    }

    public Task<RestaurantCancellation> ResolveCancellationAsync(
        RestaurantAccountActor actor, Guid cancellationId,
        ResolveRestaurantCancellation request, CancellationToken ct)
    {
        DemandIdentity(actor);
        if (!actor.Permissions.Contains("restaurant.cancellations.approve"))
            throw new RestaurantAccountForbiddenException(
                "No tienes permiso para resolver cancelaciones.");
        if (cancellationId == Guid.Empty || !ValidVersion(request.RowVersion))
            throw new RestaurantAccountValidationException(
                "La solicitud de cancelación no es válida.");
        return store.ResolveCancellationAsync(actor, cancellationId,
            request, time.GetUtcNow(), ct);
    }

    public Task<IReadOnlyList<RestaurantCancellation>> CancellationsAsync(
        RestaurantAccountActor actor, Guid accountId, CancellationToken ct)
    {
        DemandRead(actor);
        if (accountId == Guid.Empty)
            throw new RestaurantAccountValidationException("La cuenta es obligatoria.");
        return store.CancellationsAsync(ForReading(actor), accountId, ct);
    }

    private static bool ValidVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { return Convert.FromBase64String(value).Length == 8; }
        catch (FormatException) { return false; }
    }

    public Task<RestaurantCommandBoardPage> CommandCardsAsync(
        RestaurantAccountActor actor, Guid? stationId, string? state,
        string? tableCode, int page, CancellationToken ct)
    {
        DemandIdentity(actor);
        if (!actor.Permissions.Contains("restaurant.commands.read"))
            throw new RestaurantAccountForbiddenException(
                "No tienes permiso para consultar comandas.");
        if (stationId == Guid.Empty || page is < 1 or > 1000)
            throw new RestaurantAccountValidationException("La estación no es válida.");
        if (state is not null && state is not ("Pending" or "Preparing" or "Ready" or "ToDeliver" or "Cancelled"))
            throw new RestaurantAccountValidationException("El estado de la comanda no es válido.");
        tableCode = tableCode?.Trim();
        if (tableCode is { Length: > 32 })
            throw new RestaurantAccountValidationException("El código de mesa no es válido.");
        return store.CommandCardsAsync(actor, stationId, state,
            string.IsNullOrEmpty(tableCode) ? null : tableCode, page, ct);
    }

    public Task ChangeCommandStateAsync(RestaurantAccountActor actor,
        Guid cardId, ChangeRestaurantCommandStateRequest request,
        CancellationToken ct)
    {
        DemandIdentity(actor);
        if (!actor.Permissions.Contains("restaurant.commands.manage"))
            throw new RestaurantAccountForbiddenException(
                "No tienes permiso para actualizar comandas.");
        if (cardId == Guid.Empty || request.State is not
            ("Preparing" or "Ready" or "ToDeliver" or "Delivered"))
            throw new RestaurantAccountValidationException(
                "La comanda o el estado no son válidos.");
        byte[] version;
        try { version = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new RestaurantAccountValidationException(
            "La versión de la comanda no es válida."); }
        if (version.Length != 8)
            throw new RestaurantAccountValidationException(
                "La versión de la comanda no es válida.");
        return store.ChangeCommandStateAsync(actor, cardId, request.State,
            version, time.GetUtcNow(), ct);
    }

    public Task ReprintCommandAsync(RestaurantAccountActor actor,
        Guid cardId, CancellationToken ct)
    {
        DemandIdentity(actor);
        if (!actor.Permissions.Contains("restaurant.commands.manage"))
            throw new RestaurantAccountForbiddenException(
                "No tienes permiso para reimprimir comandas.");
        if (cardId == Guid.Empty)
            throw new RestaurantAccountValidationException("La comanda no es válida.");
        return store.ReprintCommandAsync(actor, cardId, time.GetUtcNow(), ct);
    }

    public Task<RestaurantAccount> OpenAsync(RestaurantAccountActor actor,
        OpenRestaurantAccountRequest request, CancellationToken ct)
    {
        DemandAttention(actor);
        if (request.AccountId == Guid.Empty || request.TableId == Guid.Empty ||
            request.WarehouseId == Guid.Empty)
            throw new RestaurantAccountValidationException(
                "La cuenta, la mesa y la bodega son obligatorias.");
        if (actor.InterventionTableId is { } selectedTable &&
            selectedTable != request.TableId)
            throw new RestaurantAccountForbiddenException(
                "La intervención pertenece a otra mesa.");
        return store.OpenAsync(actor, request, time.GetUtcNow(), ct);
    }

    public Task<RestaurantAccount?> GetAsync(RestaurantAccountActor actor,
        Guid accountId, CancellationToken ct)
    {
        DemandRead(actor);
        if (accountId == Guid.Empty)
            throw new RestaurantAccountValidationException("La cuenta es obligatoria.");
        return store.GetAsync(ForReading(actor), accountId, ct);
    }

    public Task<RestaurantAccountDraft?> GetDraftAsync(
        RestaurantAccountActor actor, Guid accountId, CancellationToken ct)
    {
        DemandRead(actor);
        if (accountId == Guid.Empty)
            throw new RestaurantAccountValidationException("La cuenta es obligatoria.");
        return store.GetDraftAsync(ForReading(actor), accountId, ct);
    }

    public Task<RestaurantPrebill> PrintPrebillAsync(
        RestaurantAccountActor actor, Guid accountId,
        PrintRestaurantPrebillRequest request, CancellationToken ct)
    {
        DemandRead(actor);
        if (accountId == Guid.Empty || request.ExpectedVersion < 1)
            throw new RestaurantAccountValidationException(
                "La cuenta o su versión no es válida.");
        return store.PrintPrebillAsync(ForReading(actor), accountId,
            request.ExpectedVersion, time.GetUtcNow(), ct);
    }

    public Task<RestaurantAccountDraft> AddAsync(
        RestaurantAccountActor actor, Guid accountId,
        AddRestaurantAccountItemRequest request, CancellationToken ct)
    {
        DemandAttention(actor);
        if (accountId == Guid.Empty || request.AdditionId == Guid.Empty ||
            request.ProductId == Guid.Empty || request.ExpectedVersion < 1 ||
            request.ReplacementOfCancellationId == Guid.Empty ||
            request.Quantity is <= 0 or > 9999 ||
            decimal.Round(request.Quantity, 4) != request.Quantity)
            throw new RestaurantAccountValidationException(
                "Producto, cantidad, cuenta o versión inválida.");
        return store.AddAsync(ForAttention(actor),
            accountId, request,
            products, time.GetUtcNow(), ct);
    }

    public Task<RestaurantAccountDraft> ChangeQuantityAsync(
        RestaurantAccountActor actor, Guid accountId, Guid additionId,
        ChangeRestaurantAccountQuantityRequest request, CancellationToken ct)
    {
        DemandAttention(actor);
        if (accountId == Guid.Empty || additionId == Guid.Empty ||
            request.ChangeId == Guid.Empty || request.ExpectedVersion < 1 ||
            request.Quantity is < 0 or > 9999 ||
            decimal.Round(request.Quantity, 4) != request.Quantity)
            throw new RestaurantAccountValidationException(
                "Incorporación, cantidad o versión inválida.");
        return store.ChangeQuantityAsync(
            ForAttention(actor), accountId, additionId,
            request, time.GetUtcNow(), ct);
    }

    public Task<RestaurantAccountDraft> ChangeNoteAsync(
        RestaurantAccountActor actor, Guid accountId, Guid additionId,
        ChangeRestaurantAccountNoteRequest request, CancellationToken ct)
    {
        DemandAttention(actor);
        if (accountId == Guid.Empty || additionId == Guid.Empty ||
            request.ExpectedVersion < 1 || request.PreparationNote?.Trim().Length > 500)
            throw new RestaurantAccountValidationException(
                "La observación o la versión de la cuenta no es válida.");
        var note = string.IsNullOrWhiteSpace(request.PreparationNote)
            ? null : request.PreparationNote.Trim();
        return store.ChangeNoteAsync(
            ForAttention(actor), accountId, additionId,
            request with { PreparationNote = note }, time.GetUtcNow(), ct);
    }

    public Task<SendRestaurantPendingResult> SendPendingAsync(
        RestaurantAccountActor actor, Guid accountId,
        SendRestaurantPendingRequest request, CancellationToken ct)
    {
        DemandAttention(actor);
        if (accountId == Guid.Empty || request.OperationId == Guid.Empty ||
            request.ExpectedVersion < 1 ||
            (request.AdditionId is { } selectedAdditionId &&
             selectedAdditionId == Guid.Empty))
            throw new RestaurantAccountValidationException(
                "Cuenta, envío o versión inválida.");
        return store.SendPendingAsync(
            ForAttention(actor), accountId,
            request, time.GetUtcNow(), ct);
    }

    public Task<IReadOnlyList<RestaurantTableOccupancy>> OccupancyAsync(
        RestaurantAccountActor actor, CancellationToken ct)
    {
        DemandRead(actor);
        return store.OccupancyAsync(actor, ct);
    }

    public async Task<OnlineSalesProductPage> ProductsAsync(
        RestaurantAccountActor actor, Guid accountId, string? search,
        int skip, int take, Guid? categoryId, CancellationToken ct)
    {
        DemandAttention(actor);
        if (accountId == Guid.Empty || categoryId == Guid.Empty ||
            skip < 0 || take is < 1 or > 100 ||
            search?.Length > 160)
            throw new RestaurantAccountValidationException(
                "Cuenta, búsqueda o página inválida.");
        actor = ForAttention(actor);
        var account = await store.GetAsync(actor, accountId, ct)
            ?? throw new RestaurantAccountConflictException(
                "La cuenta ya no está disponible.");
        if (account.Status != "Open")
            throw new RestaurantAccountConflictException(
                "La cuenta no permite agregar productos.");
        return await products.SearchAsync(actor, accountId,
            account.WarehouseId, search, skip, take, null, categoryId, ct);
    }

    public async Task<IReadOnlyList<RestaurantMenuCategory>> CategoriesAsync(
        RestaurantAccountActor actor, Guid accountId, CancellationToken ct)
    {
        DemandAttention(actor);
        if (accountId == Guid.Empty)
            throw new RestaurantAccountValidationException("La cuenta es obligatoria.");
        actor = ForAttention(actor);
        var account = await store.GetAsync(actor, accountId, ct)
            ?? throw new RestaurantAccountConflictException(
                "La cuenta ya no está disponible.");
        if (account.Status != "Open")
            throw new RestaurantAccountConflictException(
                "La cuenta no permite agregar productos.");
        return await products.CategoriesAsync(actor, accountId,
            account.WarehouseId, ct);
    }

    private static void DemandAttention(RestaurantAccountActor actor)
    {
        DemandIdentity(actor);
        if (!actor.Permissions.Contains("restaurant.attend") &&
            !actor.Permissions.Contains(OpenWithoutWaiterPermission))
            throw new RestaurantAccountForbiddenException(
                "No tienes permiso para atender mesas.");
    }

    private static RestaurantAccountActor ForAttention(RestaurantAccountActor actor) =>
        actor with { EnforceWaiterRestriction =
            !actor.Permissions.Contains(OpenWithoutWaiterPermission) };

    private static RestaurantAccountActor ForReading(RestaurantAccountActor actor) =>
        actor with { EnforceWaiterRestriction =
            !actor.Permissions.Contains(OpenWithoutWaiterPermission) &&
            !actor.Permissions.Contains("sales.create") };

    private static void DemandRead(RestaurantAccountActor actor)
    {
        DemandIdentity(actor);
        if (!actor.Permissions.Contains("restaurant.attend") &&
            !actor.Permissions.Contains("sales.create") &&
            !actor.Permissions.Contains(OpenWithoutWaiterPermission))
            throw new RestaurantAccountForbiddenException(
                "No tienes permiso para consultar cuentas de mesa.");
    }

    private static void DemandIdentity(RestaurantAccountActor actor)
    {
        if (actor.UserId == Guid.Empty || actor.TenantId == Guid.Empty ||
            actor.BusinessId == Guid.Empty)
            throw new RestaurantAccountForbiddenException(
                "El contexto de usuario y sede es obligatorio.");
    }
}
