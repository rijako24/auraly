using Auraly.Application.Sales;
using Auraly.Application.Organization;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Contracts.Authentication;
using Auraly.Contracts.Sales;
using Auraly.Application.Authorization;
using Auraly.Contracts.Authorization;
using Auraly.Commerce.Accounting.Application;

namespace Auraly.Api;

public static class RestaurantAccountApi
{
    public static IEndpointRouteBuilder MapRestaurantAccountApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/restaurant")
            .RequireAuthorization("pos.user");
        group.MapPost("/command-cards/synchronization/negotiate", async (
            HttpContext context, SalesWorkspaceService workspaces,
            IPosSynchronizationPushGateway gateway, CancellationToken ct) =>
            await Handle(async () =>
            {
                var actor = Actor(context);
                var identity = new SalesWorkspaceUserIdentity(
                    actor.UserId, actor.TenantId, actor.Permissions);
                if (!await workspaces.CanReadRestaurantCommandsAsync(
                    identity, actor.BusinessId, ct))
                    throw new RestaurantAccountForbiddenException(
                        "No tienes permiso para recibir comandas de esta sede.");
                return Results.Ok(new PosSynchronizationNegotiationResponse(
                    gateway.CreateRestaurantCommandsClientAccessUri(
                        actor.TenantId, actor.BusinessId, actor.UserId, ct),
                    DateTimeOffset.UtcNow.AddMinutes(15),
                    [PosSynchronizationGroups.RestaurantCommands(
                        actor.TenantId, actor.BusinessId)]));
            }));
        group.MapGet("/waiters", async (
            HttpContext context, RestaurantAttentionService attention,
            string? search, int? page, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await attention.WaitersAsync(
                Requester(context), search, page ?? 1, ct))));
        group.MapPost("/attention", async (
            HttpContext context, RestaurantAttentionService attention,
            StartRestaurantAttentionRequest request, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await attention.StartAsync(
                Requester(context), request.TableId, request.WaiterUserId,
                request.Secret, ct))));
        group.MapDelete("/attention", async (
            HttpContext context, RestaurantAttentionService attention,
            CancellationToken ct) => await Handle(async () =>
            {
                await attention.EndAsync(Requester(context), AttentionToken(context), ct);
                return Results.NoContent();
            }));
        group.MapGet("/waiters/{waiterUserId:guid}/secondary-credential", async (
            HttpContext context, RestaurantAttentionService attention,
            Guid waiterUserId, CancellationToken ct) =>
            await Handle(async () => Results.Ok(new {
                configured = await attention.HasSecondaryAsync(
                    Requester(context), waiterUserId, ct)
            })));
        group.MapPut("/waiters/{waiterUserId:guid}/secondary-credential", async (
            HttpContext context, RestaurantAttentionService attention,
            Guid waiterUserId, SetRestaurantWaiterCredentialRequest request,
            CancellationToken ct) => await Handle(async () =>
            {
                await attention.ConfigureSecondaryAsync(Requester(context),
                    waiterUserId, request.Secret, ct);
                return Results.NoContent();
            }));
        group.MapDelete("/waiters/{waiterUserId:guid}/secondary-credential", async (
            HttpContext context, RestaurantAttentionService attention,
            Guid waiterUserId, CancellationToken ct) => await Handle(async () =>
            {
                await attention.RevokeSecondaryAsync(Requester(context),
                    waiterUserId, ct);
                return Results.NoContent();
            }));
        group.MapGet("/occupancy", async (
            HttpContext context, RestaurantAccountService service,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(
                await service.OccupancyAsync(Actor(context), ct))));
        group.MapGet("/price-channels", async (
            HttpContext context, RestaurantAccountService service,
            CancellationToken ct) => await Handle(async () => Results.Ok(
                await service.PriceChannelsAsync(Actor(context), ct))));
        group.MapGet("/command-cards", async (
            HttpContext context, RestaurantAccountService service,
            Guid? stationId, string? state, string? tableCode, int? page,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.CommandCardsAsync(
                Actor(context), stationId, state, tableCode, page ?? 1, ct))));
        group.MapPut("/command-cards/{cardId:guid}/state", async (
            HttpContext context, RestaurantAccountService service,
            Guid cardId, ChangeRestaurantCommandStateRequest request,
            CancellationToken ct) => await Handle(async () =>
            {
                await service.ChangeCommandStateAsync(Actor(context), cardId, request, ct);
                return Results.NoContent();
            }));
        group.MapPost("/command-cards/{cardId:guid}/reprint", async (
            HttpContext context, RestaurantAccountService service,
            Guid cardId, CancellationToken ct) => await Handle(async () =>
            {
                await service.ReprintCommandAsync(Actor(context), cardId, ct);
                return Results.NoContent();
            }));
        group.MapPut("/cancellations/{cancellationId:guid}/confirmation", async (
            HttpContext context, RestaurantAccountService service,
            Guid cancellationId, ConfirmRestaurantCancellation request,
            CancellationToken ct) => await Handle(async () => Results.Ok(
                await service.ConfirmCancellationAsync(Actor(context),
                    cancellationId, request, ct))));
        group.MapGet("/cancellations/{cancellationId:guid}/components", async (
            HttpContext context, RestaurantAccountService service,
            Guid cancellationId, CancellationToken ct) => await Handle(
                async () => Results.Ok(await service.CancellationComponentsAsync(
                    Actor(context), cancellationId, ct))));
        group.MapPut("/cancellations/{cancellationId:guid}/resolution", async (
            HttpContext context, RestaurantAccountService service,
            Guid cancellationId, ResolveRestaurantCancellation request,
            CancellationToken ct) => await Handle(async () => Results.Ok(
                await service.ResolveCancellationAsync(Actor(context),
                    cancellationId, request, ct))));
        group.MapPost("/accounts", async (
            HttpContext context, RestaurantAccountService service,
            RestaurantAttentionService attention,
            OpenRestaurantAccountRequest request, CancellationToken ct) =>
            await Handle(async () => Results.Created(
                $"/api/commerce/v1/restaurant/accounts/{request.AccountId}",
                await service.OpenAsync(await ActorAsync(context, attention, ct), request, ct))));
        group.MapGet("/accounts/{accountId:guid}", async (
            HttpContext context, RestaurantAccountService service,
            RestaurantAttentionService attention,
            Guid accountId, CancellationToken ct) =>
            await Handle(async () =>
            {
                var account = await service.GetAsync(
                    await ActorAsync(context, attention, ct), accountId, ct);
                return account is null ? Results.NotFound() : Results.Ok(account);
            }));
        group.MapGet("/accounts/{accountId:guid}/draft", async (
            HttpContext context, RestaurantAccountService service,
            RestaurantAttentionService attention,
            Guid accountId, CancellationToken ct) =>
            await Handle(async () =>
            {
                var draft = await service.GetDraftAsync(
                    await ActorAsync(context, attention, ct),
                    accountId, ct);
                return draft is null ? Results.NotFound() : Results.Ok(draft);
            }));
        group.MapPut("/accounts/{accountId:guid}/price-channel", async (
            HttpContext context, RestaurantAccountService service,
            Guid accountId, ChangeRestaurantPriceChannelRequest request,
            CancellationToken ct) => await Handle(async () => Results.Ok(
                await service.ChangePriceChannelAsync(Actor(context),
                    accountId, request, ct))));
        group.MapPut("/accounts/{accountId:guid}/customer", async (
            HttpContext context, RestaurantAccountService service,
            RestaurantAttentionService attention, Guid accountId,
            ChangeRestaurantCustomerRequest request, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.ChangeCustomerAsync(
                await ActorAsync(context, attention, ct), accountId, request, ct))));
        group.MapGet("/accounts/{accountId:guid}/cancellations", async (
            HttpContext context, RestaurantAccountService service,
            RestaurantAttentionService attention, Guid accountId,
            CancellationToken ct) => await Handle(async () => Results.Ok(
                await service.CancellationsAsync(
                    await ActorAsync(context, attention, ct), accountId, ct))));
        group.MapPost("/accounts/{accountId:guid}/additions/{additionId:guid}/cancellations", async (
            HttpContext context, RestaurantAccountService service,
            RestaurantAttentionService attention, Guid accountId,
            Guid additionId, RequestRestaurantCancellation request,
            CancellationToken ct) => await Handle(async () => Results.Ok(
                await service.RequestCancellationAsync(
                    await ActorAsync(context, attention, ct), accountId,
                    additionId, request, ct))));
        group.MapPost("/accounts/{accountId:guid}/prebill", async (
            HttpContext context, RestaurantAccountService service,
            RestaurantAttentionService attention, Guid accountId,
            PrintRestaurantPrebillRequest request, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.PrintPrebillAsync(
                await ActorAsync(context, attention, ct), accountId,
                request, ct))));
        group.MapPost("/accounts/{accountId:guid}/additions", async (
            HttpContext context, RestaurantAccountService service,
            RestaurantAttentionService attention,
            Guid accountId, AddRestaurantAccountItemRequest request,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.AddAsync(
                await ActorAsync(context, attention, ct), accountId, request, ct))));
        group.MapPut("/accounts/{accountId:guid}/additions/{additionId:guid}/quantity", async (
            HttpContext context, RestaurantAccountService service,
            RestaurantAttentionService attention,
            Guid accountId, Guid additionId,
            ChangeRestaurantAccountQuantityRequest request,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.ChangeQuantityAsync(
                await ActorAsync(context, attention, ct), accountId, additionId, request, ct))));
        group.MapPut("/accounts/{accountId:guid}/additions/{additionId:guid}/note", async (
            HttpContext context, RestaurantAccountService service,
            RestaurantAttentionService attention, Guid accountId, Guid additionId,
            ChangeRestaurantAccountNoteRequest request, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.ChangeNoteAsync(
                await ActorAsync(context, attention, ct), accountId, additionId, request, ct))));
        group.MapPost("/accounts/{accountId:guid}/commands", async (
            HttpContext context, RestaurantAccountService service,
            RestaurantAttentionService attention, Guid accountId,
            SendRestaurantPendingRequest request, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.SendPendingAsync(
                await ActorAsync(context, attention, ct), accountId,
                request, ct))));
        group.MapGet("/accounts/{accountId:guid}/collections", async (
            HttpContext context, RestaurantCollectionService service,
            Guid accountId, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.GetAsync(
                Actor(context), accountId, ct))));
        group.MapGet("/tips/pending", async (
            HttpContext context, RestaurantCollectionService service,
            CancellationToken ct) => await Handle(async () =>
                Results.Ok(await service.PendingTipsAsync(Actor(context), ct))));
        group.MapGet("/tips/settlements", async (
            HttpContext context, RestaurantCollectionService service,
            CancellationToken ct) => await Handle(async () =>
                Results.Ok(await service.RecentTipSettlementsAsync(Actor(context), ct))));
        group.MapPost("/tips/settlements", async (
            HttpContext context, RestaurantCollectionService service,
            AccountingProcessingCoordinator accounting,
            SettleRestaurantTipsRequest request, CancellationToken ct) =>
            await Handle(async () =>
            {
                var actor = Actor(context);
                var settled = await service.SettleTipsAsync(actor, request, ct);
                await accounting.RequestPostingAsync(actor.BusinessId,
                    settled.SettlementId, "RestaurantTipSettlement", ct);
                return Results.Ok(settled);
            }));
        group.MapPost("/accounts/{accountId:guid}/collections", async (
            HttpContext context, RestaurantCollectionService service,
            AccountingProcessingCoordinator accounting,
            Guid accountId, CaptureRestaurantCollectionRequest request,
            CancellationToken ct) => await Handle(async () =>
            {
                var actor = Actor(context);
                var result = await service.CaptureAsync(actor, accountId, request,
                    context.Request.Headers["Idempotency-Key"].ToString(), ct);
                if (request.TipPayment is not null)
                    await accounting.RequestPostingAsync(actor.BusinessId,
                        request.CollectionId, "RestaurantTip", ct);
                return Results.Ok(result);
            }));
        group.MapPost("/accounts/{accountId:guid}/collections/{collectionId:guid}/reverse", async (
            HttpContext context, RestaurantCollectionService service,
            AccountingProcessingCoordinator accounting,
            Guid accountId, Guid collectionId,
            ReverseRestaurantCollectionRequest request,
            CancellationToken ct) => await Handle(async () =>
            {
                var actor = Actor(context);
                var result = await service.ReverseAsync(actor, accountId,
                    collectionId, request.ExpectedAccountVersion, ct);
                await accounting.RequestPostingAsync(actor.BusinessId,
                    collectionId, "RestaurantTipReversal", ct);
                return Results.Ok(result);
            }));
        group.MapPost("/accounts/{accountId:guid}/complete", async (
            HttpContext context, OnlineSalesCheckoutService service,
            PosApprovalService approvals, Guid accountId,
            CompleteRestaurantAccountRequest request, CancellationToken ct) =>
            await OnlineSalesDraftApi.Handle(() =>
            {
                var user = context.User.ToOnlineSalesUserIdentity();
                var key = context.Request.Headers["Idempotency-Key"].ToString();
                if (string.IsNullOrWhiteSpace(
                        context.Request.Headers["X-Auraly-Operation-Id"]))
                    return service.CompleteRestaurantAsync(
                        user, accountId, request.WorkSessionId,
                        request.Checkout, key, ct);
                var authorized = user with
                {
                    Permissions = user.Permissions
                        .Append(CommercePermissionCodes.SalesBelowCost)
                        .ToHashSet(StringComparer.Ordinal)
                };
                return OnlineSalesDraftApi.ExecuteSensitiveAsync(
                    context, approvals, accountId, null,
                    CommercePermissionCodes.SalesBelowCost,
                    () => service.CompleteRestaurantAsync(
                        authorized, accountId, request.WorkSessionId,
                        request.Checkout, key, ct), ct);
            }));
        group.MapGet("/accounts/{accountId:guid}/settlement-preview", async (
            HttpContext context, OnlineSalesCheckoutService service,
            Guid accountId, Guid workSessionId, CancellationToken ct) =>
            await OnlineSalesDraftApi.Handle(() =>
                service.PreviewRestaurantSettlementAsync(
                    context.User.ToOnlineSalesUserIdentity(), accountId,
                    workSessionId, ct)));
        group.MapGet("/accounts/{accountId:guid}/products", async (
            HttpContext context, RestaurantAccountService service,
            RestaurantAttentionService attention,
            Guid accountId, string? search, int? skip, int? take,
            Guid? categoryId,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.ProductsAsync(
                await ActorAsync(context, attention, ct), accountId, search, skip ?? 0, take ?? 50,
                categoryId, ct))));
        group.MapGet("/accounts/{accountId:guid}/categories", async (
            HttpContext context, RestaurantAccountService service,
            RestaurantAttentionService attention,
            Guid accountId, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.CategoriesAsync(
                await ActorAsync(context, attention, ct), accountId, ct))));
        return endpoints;
    }

    private static RestaurantAccountActor Actor(HttpContext context)
    {
        var identity = context.User.ToPartyUserIdentity();
        return new(identity.ActorId, identity.TenantId, identity.BusinessId,
            identity.Permissions);
    }

    private static async Task<RestaurantAccountActor> ActorAsync(
        HttpContext context, RestaurantAttentionService attention,
        CancellationToken ct)
    {
        var token = context.Request.Headers["X-Restaurant-Attention"].ToString();
        return string.IsNullOrWhiteSpace(token)
            ? Actor(context)
            : await attention.ResolveActorAsync(Requester(context), token, ct);
    }

    private static string AttentionToken(HttpContext context) =>
        context.Request.Headers["X-Restaurant-Attention"].ToString();

    private static RestaurantAttentionRequester Requester(HttpContext context)
    {
        var identity = context.User.ToPartyUserIdentity();
        var session = context.User.FindFirst(AuthenticationDefaults.SessionIdClaim)?.Value;
        if (!Guid.TryParse(session, out var sessionId))
            throw new RestaurantAccountForbiddenException(
                "La sesión conectada no está disponible.");
        return new(identity.ActorId, identity.TenantId, identity.BusinessId,
            sessionId, identity.Permissions);
    }

    public sealed record StartRestaurantAttentionRequest(
        Guid TableId, Guid WaiterUserId, string? Secret);
    public sealed record SetRestaurantWaiterCredentialRequest(string Secret);

    internal static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (RestaurantAccountForbiddenException error)
        { return Results.Problem(error.Message, statusCode: 403); }
        catch (RestaurantAccountValidationException error)
        { return Results.Problem(error.Message, statusCode: 400); }
        catch (RestaurantAccountConflictException error)
        { return Results.Problem(error.Message, statusCode: 409); }
    }
}
