using System.Security.Claims;
using Auraly.Application.Dispatching;
using Auraly.Contracts.Dispatching;
using Auraly.Platform.Application.Time;
using Auraly.Platform.Application.Services;
using Auraly.Pos.Printing;

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

        group.MapGet("/drivers", async (ClaimsPrincipal principal, int? page, int? pageSize,
            string? search, Guid? userId, DispatchService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.DriversAsync(principal.ToDispatchIdentity(),
                page ?? 1, pageSize ?? 30, search, userId, ct), Results.Ok));

        group.MapGet("/delivery-reasons", async (ClaimsPrincipal principal, string type,
            DispatchDeliveryService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.ReasonsAsync(principal.ToDispatchIdentity(), type, ct), Results.Ok));

        group.MapGet("/candidates", async (ClaimsPrincipal principal, int page, int pageSize,
            string? search, string? documentType, DateOnly? from, DateOnly? to, Guid? warehouseId, int? dateRangeVersion,
            DispatchService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.CandidatesAsync(principal.ToDispatchIdentity(),
                new(page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize, search, documentType, from, to, warehouseId, dateRangeVersion ?? 1), ct), Results.Ok));

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
            DispatchDeliveryService service, IMediaUrlResolver media, CancellationToken ct) =>
            await ExecuteAsync(() => service.GetAsync(principal.ToDispatchIdentity(), dispatchId, ct), Results.Ok,
                result => WithEvidenceAsync(result, principal.ToDispatchIdentity().BusinessId, media, ct)));

        group.MapPut("/{dispatchId:guid}/delivery-results", async (ClaimsPrincipal principal, Guid dispatchId,
            RecordDispatchDeliveryRequest request, DispatchDeliveryService service, IMediaUrlResolver media, CancellationToken ct) =>
            await ExecuteAsync(() => service.RecordAsync(principal.ToDispatchIdentity(), dispatchId, request, ct), Results.Ok,
                result => WithEvidenceAsync(result, principal.ToDispatchIdentity().BusinessId, media, ct)));

        group.MapPut("/{dispatchId:guid}/delivery-order", async (ClaimsPrincipal principal, Guid dispatchId,
            ReorderDispatchDocumentsRequest request, DispatchDeliveryService service, IMediaUrlResolver media, CancellationToken ct) =>
            await ExecuteAsync(() => service.ReorderAsync(principal.ToDispatchIdentity(), dispatchId, request, ct), Results.Ok,
                result => WithEvidenceAsync(result, principal.ToDispatchIdentity().BusinessId, media, ct)));

        group.MapPost("/{dispatchId:guid}/expenses", async (ClaimsPrincipal principal, Guid dispatchId,
            DispatchExpenseInput request, DispatchDeliveryService service, IMediaUrlResolver media, CancellationToken ct) =>
            await ExecuteAsync(() => service.RecordExpenseAsync(principal.ToDispatchIdentity(), dispatchId, request, ct), Results.Ok,
                result => WithEvidenceAsync(result, principal.ToDispatchIdentity().BusinessId, media, ct)));

        group.MapPut("/{dispatchId:guid}/expenses/{expenseId:guid}/review", async (ClaimsPrincipal principal, Guid dispatchId,
            Guid expenseId, ReviewDispatchExpenseRequest request, DispatchDeliveryService service, IMediaUrlResolver media, CancellationToken ct) =>
            await ExecuteAsync(() => service.ReviewExpenseAsync(principal.ToDispatchIdentity(), dispatchId, expenseId, request, ct), Results.Ok,
                result => WithEvidenceAsync(result, principal.ToDispatchIdentity().BusinessId, media, ct)));
        group.MapPost("/{dispatchId:guid}/evidence", async (ClaimsPrincipal principal, Guid dispatchId,
            IFormFile file, DispatchDeliveryService service, IBlobStorageService blobs, IMediaUrlResolver media, CancellationToken ct) =>
            await ExecuteAsync(async () =>
        {
            var actor = principal.ToDispatchIdentity();
            await service.GetAsync(actor, dispatchId, ct);
            if (file.Length is <= 0 or > 8_388_608 || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                throw new DispatchValidationException("El soporte debe ser una imagen de máximo 8 MB.");
            var extension = Path.GetExtension(file.FileName);
            if (extension.Length > 10) extension = ".jpg";
            await using var stream = file.OpenReadStream();
            var reference = await blobs.UploadImageAsync(actor.BusinessId, stream, $"dispatch-evidence/{dispatchId:D}/{Guid.NewGuid():N}{extension}", ct);
            var signed = await media.ResolveBusinessBatchAsync(actor.BusinessId, [reference], ct);
            var readUrl = signed[reference];
            // Only the stable unsigned identity belongs in documents and hashes.
            var url = new Uri(readUrl, UriKind.Absolute).GetLeftPart(UriPartial.Path);
            return new { url, readUrl };
        }, Results.Ok)).RequireAuthorization(policy => policy.RequireClaim("permission", DispatchPermissionCodes.ExecuteDeliveries)).DisableAntiforgery();


        group.MapPut("/{dispatchId:guid}/payments/{paymentId:guid}/verification", async (ClaimsPrincipal principal,
            Guid dispatchId, Guid paymentId, VerifyDispatchPaymentRequest request,
            DispatchDeliveryService service, IMediaUrlResolver media, CancellationToken ct) =>
            await ExecuteAsync(() => service.VerifyPaymentAsync(principal.ToDispatchIdentity(), dispatchId, paymentId, request, ct), Results.Ok,
                result => WithEvidenceAsync(result, principal.ToDispatchIdentity().BusinessId, media, ct)));

        group.MapPut("/{dispatchId:guid}/payments/{paymentId:guid}/correction", async (ClaimsPrincipal principal,
            Guid dispatchId, Guid paymentId, CorrectDispatchPaymentRequest request,
            DispatchDeliveryService service, IMediaUrlResolver media, CancellationToken ct) =>
            await ExecuteAsync(() => service.CorrectPaymentAsync(principal.ToDispatchIdentity(), dispatchId, paymentId, request, ct), Results.Ok,
                result => WithEvidenceAsync(result, principal.ToDispatchIdentity().BusinessId, media, ct)));

        group.MapPost("/{dispatchId:guid}/close-route", async (ClaimsPrincipal principal, Guid dispatchId,
            CloseDispatchRouteRequest request, DispatchDeliveryService service, IMediaUrlResolver media, CancellationToken ct) =>
            await ExecuteAsync(() => service.CloseRouteAsync(principal.ToDispatchIdentity(), dispatchId, request, ct), Results.Ok,
                result => WithEvidenceAsync(result, principal.ToDispatchIdentity().BusinessId, media, ct)));

        group.MapPost("/{dispatchId:guid}/closure-receipt", async (ClaimsPrincipal principal, Guid dispatchId,
            DispatchClosureReceiptRequest request, DispatchDeliveryService service, IBusinessClock clock, CancellationToken ct) =>
            await ExecuteAsync(async () => {
                if (request.PaperWidthMillimeters is not (58 or 80) || request.CompanyName?.Length > 200)
                    throw new DispatchValidationException("Revisa el nombre y el ancho de la tirilla (58 u 80 mm).");
                var actor = principal.ToDispatchIdentity();
                var dispatch = await service.GetAsync(actor, dispatchId, ct);
                if (dispatch.Settlement is null) throw new DispatchConflictException("El despacho todavía no está cerrado por el transportador.");
                var businessClock = await clock.GetSnapshotAsync(actor.BusinessId, ct);
                return new DispatchClosureReceiptView(DispatchClosureReceiptRenderer.RenderHtml(dispatch,
                    businessClock.TimeZone, request.CompanyName, request.PaperWidthMillimeters));
            }, Results.Ok));

        group.MapPost("/{dispatchId:guid}/settle", async (ClaimsPrincipal principal, Guid dispatchId,
            SettleDispatchRequest request, DispatchDeliveryService service,
            DispatchSettlementCoordinator coordinator, IMediaUrlResolver media, CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                var actor=principal.ToDispatchIdentity();
                var result = await service.SettleAsync(actor, dispatchId, request, ct);
                coordinator.Signal();
                return result;
            }, Results.Ok, result => WithEvidenceAsync(result, principal.ToDispatchIdentity().BusinessId, media, ct)))
            .RequireAuthorization(policy=>policy.RequireClaim("permission",DispatchPermissionCodes.Settle));

        return endpoints;
    }

    private delegate Task<DispatchMutationResult> TransitionAction(DispatchService service,
        DispatchActorIdentity actor, Guid id, DispatchTransitionRequest request, CancellationToken ct);

    private static Delegate Transition(TransitionAction action) => async (
        ClaimsPrincipal principal, Guid dispatchId, DispatchTransitionRequest request,
        DispatchService service, CancellationToken ct) =>
        await ExecuteAsync(() => action(service, principal.ToDispatchIdentity(), dispatchId, request, ct), Results.Ok);

    private static async Task<DispatchExecutionDetail> WithEvidenceAsync(DispatchExecutionDetail detail,
        Guid businessId, IMediaUrlResolver media, CancellationToken ct)
    {
        var references = detail.Documents.SelectMany(document => document.Payments).Select(payment => payment.EvidenceUrl)
            .Concat(detail.Expenses.Select(expense => expense.EvidenceUrl)).OfType<string>()
            .Where(reference => !string.IsNullOrWhiteSpace(reference)).Distinct(StringComparer.Ordinal).ToArray();
        if (references.Length == 0) return detail;
        var urls = await media.ResolveBusinessBatchAsync(businessId, references, ct);
        return detail with {
            Documents = detail.Documents.Select(document => document with {
                Payments = document.Payments.Select(payment => payment with { EvidenceReadUrl = ReadUrl(urls, payment.EvidenceUrl) }).ToArray()
            }).ToArray(),
            Expenses = detail.Expenses.Select(expense => expense with { EvidenceReadUrl = ReadUrl(urls, expense.EvidenceUrl) }).ToArray()
        };
    }

    private static async Task<DispatchPaymentReviewResult> WithEvidenceAsync(DispatchPaymentReviewResult detail,
        Guid businessId, IMediaUrlResolver media, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(detail.Payment.EvidenceUrl)) return detail;
        var urls = await media.ResolveBusinessBatchAsync(businessId, [detail.Payment.EvidenceUrl], ct);
        return detail with { Payment = detail.Payment with { EvidenceReadUrl = urls[detail.Payment.EvidenceUrl] } };
    }

    private static string? ReadUrl(IReadOnlyDictionary<string,string> urls, string? reference) =>
        string.IsNullOrWhiteSpace(reference) ? null : urls[reference];

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action, Func<T, IResult> success,
        Func<T, Task<T>>? present = null)
    {
        try {
            var result = await action();
            return success(present is null ? result : await present(result));
        }
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
        principal.FindAll("permission").Select(value => value.Value).ToHashSet(StringComparer.Ordinal),
        principal.FindAll(ClaimTypes.Role).Any(value => IsAdministratorRole(value.Value)));

    private static bool IsAdministratorRole(string role) =>
        role.Equals("Administrador", StringComparison.OrdinalIgnoreCase)
        || role.Equals("Administrator", StringComparison.OrdinalIgnoreCase)
        || role.Equals("TenantAdministrator", StringComparison.OrdinalIgnoreCase)
        || role.Equals("Administrador de plataforma", StringComparison.OrdinalIgnoreCase);

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new DispatchForbiddenException("No se pudo validar el contexto de acceso del usuario.");
}
