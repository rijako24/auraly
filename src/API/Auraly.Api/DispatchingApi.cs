using System.Security.Claims;
using Auraly.Application.Dispatching;
using Auraly.Contracts.Dispatching;
using Auraly.Infrastructure.Persistence;

namespace Auraly.Api;

public static class DispatchingApi
{
    public static IEndpointRouteBuilder MapDispatchingApi(this IEndpointRouteBuilder endpoints)
    {
        // The services enforce each operation's exact permission. Keeping only authentication
        // here lets transporter-only users execute an assigned dispatch without admin read access.
        var group = endpoints.MapGroup("/api/commerce/v1/dispatches")
            .RequireAuthorization();

        group.MapGet("", async (ClaimsPrincipal principal, int page, int pageSize,
            string? search, string? status, DateOnly? from, DateOnly? to,
            DispatchService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.PageAsync(principal.ToDispatchIdentity(),
                new(page == 0 ? 1 : page, pageSize == 0 ? 25 : pageSize, search, status, from, to), ct), Results.Ok));

        group.MapGet("/options", async (ClaimsPrincipal principal, DispatchService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.OptionsAsync(principal.ToDispatchIdentity(), ct), Results.Ok));

        group.MapGet("/delivery-reasons", async (ClaimsPrincipal principal, string type,
            DispatchDeliveryService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.ReasonsAsync(principal.ToDispatchIdentity(), type, ct), Results.Ok));

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

        group.MapGet("/{dispatchId:guid}/execution", async (ClaimsPrincipal principal, Guid dispatchId,
            DispatchDeliveryService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.GetAsync(principal.ToDispatchIdentity(), dispatchId, ct), Results.Ok));

        group.MapPut("/{dispatchId:guid}/delivery-results", async (ClaimsPrincipal principal, Guid dispatchId,
            RecordDispatchDeliveryRequest request, DispatchDeliveryService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.RecordAsync(principal.ToDispatchIdentity(), dispatchId, request, ct), Results.Ok));

        group.MapPut("/{dispatchId:guid}/delivery-order", async (ClaimsPrincipal principal, Guid dispatchId,
            ReorderDispatchDocumentsRequest request, DispatchDeliveryService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.ReorderAsync(principal.ToDispatchIdentity(), dispatchId, request, ct), Results.Ok));

        group.MapPost("/{dispatchId:guid}/expenses", async (ClaimsPrincipal principal, Guid dispatchId,
            DispatchExpenseInput request, DispatchDeliveryService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.RecordExpenseAsync(principal.ToDispatchIdentity(), dispatchId, request, ct), Results.Ok));

        group.MapPut("/{dispatchId:guid}/expenses/{expenseId:guid}/review", async (ClaimsPrincipal principal, Guid dispatchId,
            Guid expenseId, ReviewDispatchExpenseRequest request, DispatchDeliveryService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.ReviewExpenseAsync(principal.ToDispatchIdentity(), dispatchId, expenseId, request, ct), Results.Ok));
        group.MapPost("/{dispatchId:guid}/evidence", async (ClaimsPrincipal principal, Guid dispatchId,
            IFormFile file, DispatchDeliveryService service, AzureBlobObjectStorage blobs, CancellationToken ct) =>
        {
            var actor = principal.ToDispatchIdentity();
            await service.GetAsync(actor, dispatchId, ct);
            if (file.Length is <= 0 or > 8_388_608 || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return Results.Problem("Evidence must be an image of at most 8 MB.", statusCode: 400);
            var extension = Path.GetExtension(file.FileName);
            if (extension.Length > 10) extension = ".jpg";
            await using var stream = file.OpenReadStream();
            var url = await blobs.UploadImageAsync(actor.BusinessId, stream, $"dispatch-evidence/{dispatchId:D}/{Guid.NewGuid():N}{extension}", file.ContentType, ct);
            return Results.Ok(new { url });
        }).DisableAntiforgery();


        group.MapPost("/{dispatchId:guid}/close-route", async (ClaimsPrincipal principal, Guid dispatchId,
            CloseDispatchRouteRequest request, DispatchDeliveryService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.CloseRouteAsync(principal.ToDispatchIdentity(), dispatchId, request, ct), Results.Ok));

        group.MapPost("/{dispatchId:guid}/settle", async (ClaimsPrincipal principal, Guid dispatchId,
            SettleDispatchRequest request, DispatchDeliveryService service,
            DispatchSettlementCoordinator coordinator, CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                var result = await service.SettleAsync(principal.ToDispatchIdentity(), dispatchId, request, ct);
                coordinator.Signal();
                return result;
            }, Results.Ok));

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
