using System.Security.Claims;
using Auraly.Application.Authorization;
using Auraly.Application.Sales;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;
using Auraly.Pos.Printing;
using QRCoder;

namespace Auraly.Api;

public static class OnlineSalesDraftApi
{
    public static IEndpointRouteBuilder MapOnlineSalesDraftApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/pos/drafts")
            .RequireAuthorization("pos.user");

        group.MapPut("/{draftId:guid}/charges/{chargeId:guid}", (HttpContext context, Guid draftId,
            Guid chargeId, InvoiceChargeDraftRequest request, OnlineSalesDraftService service, CancellationToken ct) =>
            chargeId != request.AppliedChargeId ? Task.FromResult<IResult>(Results.BadRequest()) :
            Handle(() => service.SaveChargeAsync(context.User.ToOnlineSalesUserIdentity(), draftId, request,
                context.Request.Headers["Idempotency-Key"].ToString(), ct)));
        group.MapPost("/{draftId:guid}/charges/{chargeId:guid}/remove", (HttpContext context, Guid draftId,
            Guid chargeId, RemoveOnlineSalesDraftLineRequest request, OnlineSalesDraftService service, CancellationToken ct) =>
            Handle(() => service.RemoveChargeAsync(context.User.ToOnlineSalesUserIdentity(), draftId, chargeId,
                request.ExpectedVersion, context.Request.Headers["Idempotency-Key"].ToString(), ct)));

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

        group.MapPost("/history/customers/search", async (
            HttpContext context,
            SearchOnlineSalesHistoryOptionsRequest request,
            OnlineSalesHistoryService service,
            CancellationToken ct) =>
            await Handle(() => service.SearchCustomersAsync(
                context.User.ToOnlineSalesUserIdentity(), request, ct)));

        group.MapPost("/history/products/search", async (
            HttpContext context,
            SearchOnlineSalesHistoryOptionsRequest request,
            OnlineSalesHistoryService service,
            CancellationToken ct) =>
            await Handle(() => service.SearchProductsAsync(
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
            OnlineSalesHistoryContext request,
            OnlineSalesHistoryService service,
            CancellationToken ct) =>
            await HandleNullable(() => service.GetReceiptAsync(
                context.User.ToOnlineSalesUserIdentity(),
                request,
                documentId,
                ct)));

        group.MapPost("/sales/{documentId:guid}/reprint-audit", async (
            HttpContext context,
            Guid documentId,
            OnlineSalesHistoryContext request,
            OnlineSalesHistoryService service,
            CancellationToken ct) =>
            await HandleFound(() => service.RecordReprintAsync(
                context.User.ToOnlineSalesUserIdentity(), request, documentId, ct)));

        var deviceHistory = endpoints.MapGroup("/api/pos/v1/history")
            .RequireAuthorization("pos.enrolled");

        deviceHistory.MapPost("/customers/search", async (
            HttpContext context,
            SearchOnlineSalesHistoryOptionsRequest request,
            OnlineSalesHistoryService service,
            CancellationToken ct) =>
            await Handle(() => service.SearchCustomersAsync(
                context.User.ToDeviceOnlineSalesHistoryIdentity(context),
                request,
                ct)));

        deviceHistory.MapPost("/products/search", async (
            HttpContext context,
            SearchOnlineSalesHistoryOptionsRequest request,
            OnlineSalesHistoryService service,
            CancellationToken ct) =>
            await Handle(() => service.SearchProductsAsync(
                context.User.ToDeviceOnlineSalesHistoryIdentity(context),
                request,
                ct)));

        deviceHistory.MapPost("/sales/search", async (
            HttpContext context,
            SearchOnlineSalesIssuedSalesRequest request,
            OnlineSalesHistoryService service,
            CancellationToken ct) =>
            await Handle(() => service.SearchAsync(
                context.User.ToDeviceOnlineSalesHistoryIdentity(context),
                request,
                ct)));

        deviceHistory.MapPost("/sales/{documentId:guid}/receipt", async (
            HttpContext context,
            Guid documentId,
            OnlineSalesHistoryContext request,
            OnlineSalesHistoryService service,
            CancellationToken ct) =>
            await HandleNullable(() => service.GetReceiptAsync(
                context.User.ToDeviceOnlineSalesHistoryIdentity(context),
                request,
                documentId,
                ct)));

        deviceHistory.MapPost("/sales/{documentId:guid}/reprint-audit", async (
            HttpContext context,
            Guid documentId,
            OnlineSalesHistoryContext request,
            OnlineSalesHistoryService service,
            CancellationToken ct) =>
            await HandleFound(() => service.RecordReprintAsync(
                context.User.ToDeviceOnlineSalesHistoryIdentity(context),
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
                    new OnlineSalesHistoryContext(businessId),
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
                    : exception.Code == "ApprovalRequired"
                        ? StatusCodes.Status428PreconditionRequired
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
                CommercePermissionCodes.SalesChangePrice,
                () => service.SetDiscountAsync(
                    context.User.ToOnlineSalesUserIdentity(),
                    draftId, lineId, request, IdempotencyKey(context), ct),
                ct)));

        group.MapPut("/{draftId:guid}/lines", async (
            HttpContext context,
            Guid draftId,
            UpdateOnlineSalesDraftLinesRequest request,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.UpdateLinesAsync(
                context.User.ToOnlineSalesUserIdentity(),
                draftId, request, IdempotencyKey(context), ct)));

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

        group.MapGet("/{draftId:guid}/settlement", async (
            HttpContext context,
            Guid draftId,
            OnlineSalesCheckoutService service,
            CancellationToken ct) =>
            await Handle(() => service.PreviewSettlementAsync(
                context.User.ToOnlineSalesUserIdentity(), draftId, ct)));

        group.MapPost("/temporaries/{draftId:guid}/remove", async (
            HttpContext context,
            Guid draftId,
            RemoveOnlineSalesTemporaryRequest request,
            OnlineSalesDraftService service,
            PosApprovalService approvals,
            CancellationToken ct) =>
            await Handle(() =>
            {
                var user = context.User.ToOnlineSalesUserIdentity();
                var authorizedUser = user with
                {
                    Permissions = user.Permissions
                        .Append(CommercePermissionCodes.SalesDeletePausedDraft)
                        .ToHashSet(StringComparer.Ordinal)
                };
                return ExecuteSensitiveAsync(
                    context,
                    approvals,
                    draftId,
                    null,
                    CommercePermissionCodes.SalesDeletePausedDraft,
                    () => service.RemoveTemporaryAsync(
                        authorizedUser,
                        draftId, request, IdempotencyKey(context), ct),
                    ct);
            }));


        group.MapPost("/{draftId:guid}/complete", async (
            HttpContext context,
            Guid draftId,
            CompleteOnlineSalesDraftRequest request,
            OnlineSalesCheckoutService service,
            PosApprovalService approvals,
            CancellationToken ct) =>
            await Handle(() =>
            {
                var user = context.User.ToOnlineSalesUserIdentity();
                if (string.IsNullOrWhiteSpace(
                        context.Request.Headers["X-Auraly-Operation-Id"]))
                    return service.CompleteAsync(
                        user, draftId, request, IdempotencyKey(context), ct);
                var authorizedUser = user with
                {
                    Permissions = user.Permissions
                        .Append(CommercePermissionCodes.SalesBelowCost)
                        .ToHashSet(StringComparer.Ordinal)
                };
                return ExecuteSensitiveAsync(
                    context,
                    approvals,
                    draftId,
                    null,
                    CommercePermissionCodes.SalesBelowCost,
                    () => service.CompleteAsync(
                        authorizedUser, draftId, request, IdempotencyKey(context), ct),
                    ct);
            }));

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

        group.MapPost("/{draftId:guid}/lines/{lineId:guid}/discard-unpriced-generic", async (
            HttpContext context,
            Guid draftId,
            Guid lineId,
            RemoveOnlineSalesDraftLineRequest request,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.DiscardUnpricedGenericLineAsync(
                context.User.ToOnlineSalesUserIdentity(),
                draftId, lineId, request, IdempotencyKey(context), ct)));

        // Render the response already held by the caller; no database or per-document QR request.
        group.MapPost("/sales/receipts/render", (SalesReceiptsRenderRequest request) =>
        {
            if (request.Receipts is null || request.Receipts.Count is < 1 or > 500 ||
                request.Receipts.Any(receipt => receipt is null ||
                    !PosSaleDocumentTypes.IsSupported(receipt.DocumentType) ||
                    receipt.Lines is null || receipt.Lines.Count == 0 || receipt.Lines.Any(line => line is null) ||
                    receipt.Payments is null || receipt.Payments.Any(payment => payment is null) ||
                    (PosSaleDocumentTypes.IsFiscal(receipt.DocumentType) &&
                        (string.IsNullOrWhiteSpace(receipt.Cufe) || string.IsNullOrWhiteSpace(receipt.QrPayload)))) ||
                request.Receipts.Sum(receipt => (long)receipt.Lines.Count) > 10_000 ||
                request.Format is not ("Receipt" or "HalfLetter" or "HalfLegal" or "Letter") ||
                request.PaperWidthMillimeters is not (58 or 80))
                return Results.BadRequest(new { message = "Selecciona hasta 500 documentos y un formato de impresión válido." });
            try
            {
                var html = request.Format == "Receipt"
                    ? new SalesReceiptHtmlRenderer().RenderBatch(request.Receipts,
                        request.PaperWidthMillimeters, request.BusinessName, autoPrint: request.AutoPrint)
                    : new HalfLetterDocumentRenderer().Render(request.Receipts, request.Format,
                        autoPrint: request.AutoPrint);
                return Results.Ok(new { html });
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { [nameof(request.Receipts)] = [exception.Message] });
            }
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(10 * 1024 * 1024));

        group.MapPost("/sales/credit-acknowledgement/render", (
            CreditSaleAcknowledgementRenderRequest request) =>
        {
            try
            {
                var renderer = new CreditSaleAcknowledgementRenderer();
                return Results.Ok(new CreditSaleAcknowledgementRenderResponse(
                    request.Acknowledgements.Select(acknowledgement =>
                        renderer.Render(
                            acknowledgement,
                            request.Format,
                            request.ReceiptPaperWidthMillimeters)).ToArray()));
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.Acknowledgements)] = [exception.Message]
                    });
            }
        });

        group.MapPost("/{draftId:guid}/complete-order", async (
            HttpContext context,
            Guid draftId,
            CompleteOnlineSalesOrderDraftRequest request,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.ResetAfterOrderAsync(
                context.User.ToOnlineSalesUserIdentity(),
                draftId, request, IdempotencyKey(context), ct)));

        return endpoints;
    }

    private static OnlineSalesUserIdentity ToDeviceOnlineSalesUserIdentity(
        this ClaimsPrincipal principal,
        HttpContext context,
        OnlineSalesDraftContext requested)
    {
        if (!Guid.TryParse(context.Request.Headers["X-Auraly-User-Id"], out var userId) ||
            !Guid.TryParse(context.Request.Headers["X-Auraly-Work-Session-Id"], out var workSessionId) ||
            workSessionId != requested.WorkSessionId)
            throw new OnlineSalesDraftForbiddenException(
                "El dispositivo no identificó el usuario y su sesión de trabajo.");
        if (!Guid.TryParse(
                principal.FindFirstValue(PosAuthenticationDefaults.TenantIdClaim),
                out var tenantId) ||
            !Guid.TryParse(
                principal.FindFirstValue(PosAuthenticationDefaults.DeviceIdClaim),
                out var deviceId))
            throw new OnlineSalesDraftForbiddenException(
                "El dispositivo enrolado no tiene un contexto válido.");
        return new OnlineSalesUserIdentity(
            userId,
            tenantId,
            new HashSet<string>([CommercePermissionCodes.SalesCreate], StringComparer.Ordinal),
            DeviceId: deviceId);
    }

    private static OnlineSalesUserIdentity ToDeviceOnlineSalesHistoryIdentity(
        this ClaimsPrincipal principal,
        HttpContext context)
    {
        if (!Guid.TryParse(context.Request.Headers["X-Auraly-User-Id"], out var userId))
            throw new OnlineSalesDraftForbiddenException(
                "El dispositivo no identificó el usuario local.");
        if (!Guid.TryParse(
                principal.FindFirstValue(PosAuthenticationDefaults.TenantIdClaim),
                out var tenantId) ||
            !Guid.TryParse(
                principal.FindFirstValue(PosAuthenticationDefaults.DeviceIdClaim),
                out var deviceId))
            throw new OnlineSalesDraftForbiddenException(
                "El dispositivo enrolado no tiene un contexto válido.");
        return new OnlineSalesUserIdentity(
            userId,
            tenantId,
            new HashSet<string>([CommercePermissionCodes.SalesReprint], StringComparer.Ordinal),
            DeviceId: deviceId);
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
        var operationHeader = context.Request.Headers["X-Auraly-Operation-Id"].ToString();
        var operationValue = string.IsNullOrWhiteSpace(operationHeader)
            ? IdempotencyKey(context)
            : operationHeader;
        if (!Guid.TryParse(operationValue, out var operationId) || operationId == Guid.Empty)
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
                    : exception.Code == "ApprovalRequired"
                        ? StatusCodes.Status428PreconditionRequired
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

    private static async Task<IResult> HandleFound(Func<Task<bool>> action)
    {
        try
        {
            return await action() ? Results.NoContent() : Results.NotFound();
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
    }
}

public static class OnlineSalesDraftClaimsPrincipalExtensions
{
    public static OnlineSalesUserIdentity ToOnlineSalesUserIdentity(
        this ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, Auraly.Contracts.Authentication.AuthenticationDefaults.IdentityTenantIdClaim),
            principal.FindAll("permission")
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal),
            principal.FindFirstValue(ClaimTypes.Name)
                ?? principal.Identity?.Name
                ?? "Usuario");

    private static Guid RequiredGuid(
        ClaimsPrincipal principal,
        string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new OnlineSalesDraftForbiddenException(
                $"The authenticated identity lacks claim '{claimType}'.");
}
