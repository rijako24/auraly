using System.Security.Claims;
using Auraly.Application.Dispatching;
using Auraly.Contracts.Dispatching;

namespace Auraly.Api;

public static class DispatchingApi
{
    public static IEndpointRouteBuilder MapDispatchingApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/dispatches")
            .RequireAuthorization(DispatchPermissionCodes.Read);

        group.MapGet("", async (ClaimsPrincipal principal, int page, int pageSize,
            string? search, string? status, DateOnly? from, DateOnly? to,
            DispatchService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.PageAsync(principal.ToDispatchIdentity(),
                new(page == 0 ? 1 : page, pageSize == 0 ? 25 : pageSize, search, status, from, to), ct), Results.Ok));

        group.MapGet("/options", async (ClaimsPrincipal principal, DispatchService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.OptionsAsync(principal.ToDispatchIdentity(), ct), Results.Ok));

        group.MapGet("/candidates", async (ClaimsPrincipal principal, int page, int pageSize,
            string? search, string? documentType, DateOnly? from, DateOnly? to, Guid? warehouseId,
            DispatchService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.CandidatesAsync(principal.ToDispatchIdentity(),
                new(page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize, search, documentType, from, to, warehouseId), ct), Results.Ok));

        group.MapGet("/{dispatchId:guid}", async (ClaimsPrincipal principal, Guid dispatchId,
            DispatchService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.GetAsync(principal.ToDispatchIdentity(), dispatchId, ct), Results.Ok));

        group.MapGet("/{dispatchId:guid}/report", async (ClaimsPrincipal principal, Guid dispatchId,
            bool includePrices, DispatchService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.ReportAsync(principal.ToDispatchIdentity(), dispatchId, includePrices, ct), Results.Ok));

        group.MapPost("", async (ClaimsPrincipal principal, CreateDispatchRequest request,
            DispatchService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.CreateAsync(principal.ToDispatchIdentity(), request, ct),
                result => Results.Created($"/api/commerce/v1/dispatches/{result.DispatchId:D}", result)));

        group.MapPost("/{dispatchId:guid}/documents", async (ClaimsPrincipal principal, Guid dispatchId,
            AddDispatchDocumentsRequest request, DispatchService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.AddDocumentsAsync(principal.ToDispatchIdentity(), dispatchId, request, ct), Results.Ok));

        group.MapDelete("/{dispatchId:guid}/documents/{sourceDocumentId:guid}", async (
            ClaimsPrincipal principal, Guid dispatchId, Guid sourceDocumentId, string rowVersion,
            DispatchService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.RemoveDocumentAsync(principal.ToDispatchIdentity(), dispatchId, sourceDocumentId, rowVersion, ct), Results.Ok));

        group.MapPost("/{dispatchId:guid}/prepare", Transition((service, actor, id, request, ct) => service.PrepareAsync(actor, id, request, ct)));
        group.MapPost("/{dispatchId:guid}/start-verification", Transition((service, actor, id, request, ct) => service.StartVerificationAsync(actor, id, request, ct)));
        group.MapPost("/{dispatchId:guid}/complete-verification", Transition((service, actor, id, request, ct) => service.CompleteVerificationAsync(actor, id, request, ct)));
        group.MapPost("/{dispatchId:guid}/release", Transition((service, actor, id, request, ct) => service.ReleaseAsync(actor, id, request, ct)));
        group.MapPost("/{dispatchId:guid}/cancel", Transition((service, actor, id, request, ct) => service.CancelAsync(actor, id, request, ct)));
        group.MapPost("/{dispatchId:guid}/reopen", Transition((service, actor, id, request, ct) => service.ReopenAsync(actor, id, request, ct)));

        group.MapPost("/{dispatchId:guid}/verification-events", async (ClaimsPrincipal principal, Guid dispatchId,
            DispatchVerificationRequest request, DispatchService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.VerifyQuantityAsync(principal.ToDispatchIdentity(), dispatchId, request, ct), Results.Ok));

        group.MapPost("/{dispatchId:guid}/shortages", async (ClaimsPrincipal principal, Guid dispatchId,
            DeclareDispatchShortageRequest request, DispatchService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.DeclareShortageAsync(principal.ToDispatchIdentity(), dispatchId, request, ct), Results.Ok));

        return endpoints;
    }

    private delegate Task<DispatchMutationResult> TransitionAction(DispatchService service,
        DispatchActorIdentity actor, Guid id, DispatchTransitionRequest request, CancellationToken ct);

    private static Delegate Transition(TransitionAction action) => async (
        ClaimsPrincipal principal, Guid dispatchId, DispatchTransitionRequest request,
        DispatchService service, CancellationToken ct) =>
        await ExecuteAsync(() => action(service, principal.ToDispatchIdentity(), dispatchId, request, ct), Results.Ok);

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action, Func<T, IResult> success)
    {
        try { return success(await action()); }
        catch (DispatchForbiddenException ex) { return Results.Problem(ex.Message, statusCode: 403); }
        catch (DispatchNotFoundException ex) { return Results.Problem(ex.Message, statusCode: 404); }
        catch (DispatchValidationException ex) { return Results.Problem(ex.Message, statusCode: 400); }
        catch (DispatchConflictException ex) { return Results.Problem(ex.Message, statusCode: 409); }
    }
}

public static class DispatchClaimsPrincipalExtensions
{
    public static DispatchActorIdentity ToDispatchIdentity(this ClaimsPrincipal principal) => new(
        RequiredGuid(principal, ClaimTypes.NameIdentifier), RequiredGuid(principal, "tenant_id"),
        RequiredGuid(principal, "business_id"),
        principal.FindAll("permission").Select(value => value.Value).ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new DispatchForbiddenException($"The authenticated identity lacks claim '{claimType}'.");
}
