using System.Security.Claims;
using Auraly.Application.Authorization;
using Auraly.Application.Sales;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;
using QRCoder;

namespace Auraly.Api;

public static class OnlineSalesDraftApi
{
    public static IEndpointRouteBuilder MapOnlineSalesDraftApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/pos/drafts")
            .RequireAuthorization("pos.user");

        group.MapPost("/active", async (
            HttpContext context,
            OpenOnlineSalesDraftRequest request,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.OpenAsync(
                context.User.ToOnlineSalesUserIdentity(), request, ct)));

        group.MapPost("/products/search", async (
            HttpContext context,
            SearchOnlineSalesRequest request,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.SearchProductsAsync(
                context.User.ToOnlineSalesUserIdentity(), request, ct)));

        group.MapPost("/customers/search", async (
            HttpContext context,
            SearchOnlineSalesRequest request,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.SearchCustomersAsync(
                context.User.ToOnlineSalesUserIdentity(), request, ct)));

        group.MapPost("/customers/get", async (
            HttpContext context,
            GetOnlineSalesCustomerRequest request,
            OnlineSalesHistoryService service,
            CancellationToken ct) =>
            await HandleNullable(() => service.GetCustomerAsync(
                context.User.ToOnlineSalesUserIdentity(), request, ct)));

        group.MapPost("/sales/search", async (
            HttpContext context,
            SearchOnlineSalesIssuedSalesRequest request,
            OnlineSalesHistoryService service,
            CancellationToken ct) =>
            await Handle(() => service.SearchAsync(
                context.User.ToOnlineSalesUserIdentity(), request, ct)));

        group.MapPost("/sales/{documentId:guid}/receipt", async (
            HttpContext context,
            Guid documentId,
            OnlineSalesDraftContext request,
            OnlineSalesHistoryService service,
            CancellationToken ct) =>
            await HandleNullable(() => service.GetReceiptAsync(
                context.User.ToOnlineSalesUserIdentity(),
                request,
                documentId,
                ct)));

        group.MapGet("/sales/{documentId:guid}/qr", async (
            HttpContext context,
            Guid documentId,
            Guid businessId,
            Guid warehouseId,
            Guid workSessionId,
            OnlineSalesHistoryService service,
            CancellationToken ct) =>
        {
            try
            {
                var receipt = await service.GetReceiptAsync(
                    context.User.ToOnlineSalesUserIdentity(),
                    new OnlineSalesDraftContext(
                        businessId,
                        warehouseId,
                        workSessionId),
                    documentId,
                    ct);
                if (receipt is null || string.IsNullOrWhiteSpace(receipt.QrPayload))
                    return Results.NotFound();
                using var data = QRCodeGenerator.GenerateQrCode(
                    receipt.QrPayload,
                    QRCodeGenerator.ECCLevel.Q);
                using var qr = new SvgQRCode(data);
                var svg = qr.GetGraphic(
                    pixelsPerModule: 4,
                    darkColorHex: "#061f22",
                    lightColorHex: "#ffffff",
                    drawQuietZones: true,
                    sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);
                return Results.Content(
                    svg,
                    "image/svg+xml; charset=utf-8");
            }
            catch (PosApprovalException exception)
        {
            var statusCode = exception.Code is "Forbidden" or "SelfApprovalForbidden"
                ? StatusCodes.Status403Forbidden
                : exception.Code is "InvalidApproval" or "AlreadyDecidedOrExpired"
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest;
            return Results.Problem(exception.Message, statusCode: statusCode, title: exception.Code);
        }
        catch (OnlineSalesDraftForbiddenException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (OnlineSalesDraftValidationException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapPost("/temporaries/search", async (
            HttpContext context,
            SearchOnlineSalesRequest request,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.ListTemporariesAsync(
                context.User.ToOnlineSalesUserIdentity(), request, ct)));

group.MapPost("/{draftId:guid}/items", async (
            HttpContext context,
            Guid draftId,
            AddOnlineSalesDraftItemRequest request,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.AddProductAsync(
                context.User.ToOnlineSalesUserIdentity(),
                draftId, request, IdempotencyKey(context), ct)));

        group.MapPut("/{draftId:guid}/lines/{lineId:guid}/quantity", async (
            HttpContext context,
            Guid draftId,
            Guid lineId,
            ChangeOnlineSalesDraftQuantityRequest request,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.ChangeQuantityAsync(
                context.User.ToOnlineSalesUserIdentity(),
                draftId, lineId, request, IdempotencyKey(context), ct)));

        group.MapPut("/{draftId:guid}/lines/{lineId:guid}/discount", async (
            HttpContext context,
            Guid draftId,
            Guid lineId,
            SetOnlineSalesDraftDiscountRequest request,
            OnlineSalesDraftService service,
            PosApprovalService approvals,
            CancellationToken ct) =>
            await Handle(() => ExecuteSensitiveAsync(
                context,
                approvals,
                draftId,
                lineId,
                CommercePermissionCodes.SalesDiscount,
                () => service.SetDiscountAsync(
                    context.User.ToOnlineSalesUserIdentity(),
                    draftId, lineId, request, IdempotencyKey(context), ct),
                ct)));

        group.MapPut("/{draftId:guid}/lines", async (
            HttpContext context,
            Guid draftId,
            UpdateOnlineSalesDraftLinesRequest request,
            OnlineSalesDraftService service,
            PosApprovalService approvals,
            CancellationToken ct) =>
            await Handle(() => ExecuteSensitiveAsync(
                context,
                approvals,
                draftId,
                null,
                CommercePermissionCodes.SalesChangePrice,
                () => service.UpdateLinesAsync(
                    context.User.ToOnlineSalesUserIdentity(),
                    draftId, request, IdempotencyKey(context), ct),
                ct)));

        group.MapPost("/{draftId:guid}/lines/{lineId:guid}/remove", async (
            HttpContext context,
            Guid draftId,
            Guid lineId,
            RemoveOnlineSalesDraftLineRequest request,
            OnlineSalesDraftService service,
            PosApprovalService approvals,
            CancellationToken ct) =>
            await Handle(() => ExecuteSensitiveAsync(
                context,
                approvals,
                draftId,
                lineId,
                CommercePermissionCodes.SalesRemoveLine,
                () => service.RemoveLineAsync(
                    context.User.ToOnlineSalesUserIdentity(),
                    draftId, lineId, request, IdempotencyKey(context), ct),
                ct)));

        group.MapPut("/{draftId:guid}/customer", async (
            HttpContext context,
            Guid draftId,
            SelectOnlineSalesDraftCustomerRequest request,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.SelectCustomerAsync(
                context.User.ToOnlineSalesUserIdentity(),
                draftId, request, IdempotencyKey(context), ct)));

        group.MapPost("/{draftId:guid}/pause", async (
            HttpContext context,
            Guid draftId,
            PauseOnlineSalesDraftRequest request,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.PauseAsync(
                context.User.ToOnlineSalesUserIdentity(),
                draftId, request, IdempotencyKey(context), ct)));

        group.MapPost("/temporaries/{draftId:guid}/recover", async (
            HttpContext context,
            Guid draftId,
            RecoverOnlineSalesDraftRequest request,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.RecoverTemporaryAsync(
                context.User.ToOnlineSalesUserIdentity(),
                draftId, request, IdempotencyKey(context), ct)));

        group.MapGet("/{draftId:guid}/inventory-validation", async (
            HttpContext context,
            Guid draftId,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.ValidateInventoryAsync(
                context.User.ToOnlineSalesUserIdentity(), draftId, ct)));

        group.MapPost("/temporaries/{draftId:guid}/remove", async (
            HttpContext context,
            Guid draftId,
            RemoveOnlineSalesTemporaryRequest request,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.RemoveTemporaryAsync(
                context.User.ToOnlineSalesUserIdentity(),
                draftId, request, IdempotencyKey(context), ct)));


        group.MapPost("/{draftId:guid}/complete", async (
            HttpContext context,
            Guid draftId,
            CompleteOnlineSalesDraftRequest request,
            OnlineSalesCheckoutService service,
            CancellationToken ct) =>
            await Handle(() => service.CompleteAsync(
                context.User.ToOnlineSalesUserIdentity(),
                draftId,
                request,
                IdempotencyKey(context),
                ct)));

        group.MapPost("/{draftId:guid}/reset", async (
            HttpContext context,
            Guid draftId,
            ResetOnlineSalesDraftRequest request,
            OnlineSalesDraftService service,
            PosApprovalService approvals,
            CancellationToken ct) =>
            await Handle(() => ExecuteSensitiveAsync(
                context,
                approvals,
                draftId,
                null,
                CommercePermissionCodes.SalesRestartDraft,
                () => service.ResetAsync(
                    context.User.ToOnlineSalesUserIdentity(),
                    draftId, request, IdempotencyKey(context), ct),
                ct)));

        return endpoints;
    }

    private static string IdempotencyKey(HttpContext context) =>
        context.Request.Headers["Idempotency-Key"].ToString();

    private static async Task<T> ExecuteSensitiveAsync<T>(
        HttpContext context,
        PosApprovalService approvals,
        Guid draftId,
        Guid? lineId,
        string permission,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = IdempotencyKey(context);
        if (!Guid.TryParse(idempotencyKey, out var operationId) || operationId == Guid.Empty)
            throw new OnlineSalesDraftValidationException(
                "Idempotency-Key must be a non-empty UUID for sensitive POS actions.");
        var approvalHeader = context.Request.Headers["X-Auraly-Approval-Id"].ToString();
        var approvalId = Guid.TryParse(approvalHeader, out var parsedApprovalId)
            ? parsedApprovalId
            : Guid.Empty;
        var identity = context.User.ToPosApprovalIdentity();
        return await approvals.ExecuteSensitiveAsync(
            identity,
            approvalId,
            identity.BusinessId,
            draftId,
            lineId,
            permission,
            operationId,
            action,
            cancellationToken);
    }

    private static async Task<IResult> Handle<T>(
        Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (PosApprovalException exception)
        {
            var statusCode = exception.Code is "Forbidden" or "SelfApprovalForbidden"
                ? StatusCodes.Status403Forbidden
                : exception.Code is "InvalidApproval" or "AlreadyDecidedOrExpired"
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest;
            return Results.Problem(exception.Message, statusCode: statusCode, title: exception.Code);
        }
        catch (OnlineSalesDraftForbiddenException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (OnlineSalesDraftValidationException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (OnlineSalesDraftConcurrencyException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "SalesDraftVersionConflict");
        }
        catch (OnlineSalesDraftIdempotencyException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "SalesDraftIdempotencyConflict");
        }
        catch (PosSaleForbiddenException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (PosSaleInvalidException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (PosSaleIdempotencyConflictException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "DocumentIdempotencyConflict");
        }
        catch (PosSaleProcessingBusyException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "DocumentProcessingBusy");
        }
    }

    private static async Task<IResult> HandleNullable<T>(
        Func<Task<T?>> action)
        where T : class
    {
        try
        {
            var result = await action();
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (PosApprovalException exception)
        {
            var statusCode = exception.Code is "Forbidden" or "SelfApprovalForbidden"
                ? StatusCodes.Status403Forbidden
                : exception.Code is "InvalidApproval" or "AlreadyDecidedOrExpired"
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest;
            return Results.Problem(exception.Message, statusCode: statusCode, title: exception.Code);
        }
        catch (OnlineSalesDraftForbiddenException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (OnlineSalesDraftValidationException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (OnlineSalesDraftConcurrencyException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "SalesDraftVersionConflict");
        }
        catch (OnlineSalesDraftIdempotencyException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "SalesDraftIdempotencyConflict");
        }
        catch (PosSaleForbiddenException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (PosSaleInvalidException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}

public static class OnlineSalesDraftClaimsPrincipalExtensions
{
    public static OnlineSalesUserIdentity ToOnlineSalesUserIdentity(
        this ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, "tenant_id"),
            principal.FindAll("permission")
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(
        ClaimsPrincipal principal,
        string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new OnlineSalesDraftForbiddenException(
                $"The authenticated identity lacks claim '{claimType}'.");
}
