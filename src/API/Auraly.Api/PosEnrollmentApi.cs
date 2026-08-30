using System.Security.Claims;
using Auraly.Application.Authentication;
using Auraly.Application.Authorization;
using Auraly.Application.Organization;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Organization;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;

namespace Auraly.Api;

public static class PosEnrollmentApi
{
    public static IEndpointRouteBuilder MapPosEnrollmentApi(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/commerce/v1/pos/enrollments",
                async (
                    HttpContext context,
                    CreatePosEnrollmentRequest request,
                    PosEnrollmentService service,
                    PosApprovalService approvals,
                    CancellationToken ct) =>
                    await Handle(async () =>
                    {
                        var identity = context.User.ToPosEnrollmentUserIdentity();
                        if (identity.Permissions.Contains(CommercePermissionCodes.EnrolledDevicesEnroll))
                            return await service.AuthorizeAsync(identity, request, ct);

                        if (!Guid.TryParse(context.Request.Headers["Idempotency-Key"].ToString(), out var operationId) ||
                            operationId == Guid.Empty)
                            throw new PosEnrollmentValidationException(
                                "La preparación sin conexión requiere un identificador de operación válido.");
                        if (!Guid.TryParse(context.Request.Headers["X-Auraly-Draft-Id"].ToString(), out var draftId) ||
                            draftId == Guid.Empty)
                            throw new PosEnrollmentValidationException(
                                "No fue posible identificar la venta activa para solicitar autorización.");
                        var approvalId = Guid.TryParse(
                            context.Request.Headers["X-Auraly-Approval-Id"].ToString(),
                            out var parsedApprovalId)
                            ? parsedApprovalId
                            : Guid.Empty;
                        var approvalIdentity = context.User.ToPosApprovalIdentity();
                        var authorizedIdentity = identity with
                        {
                            Permissions = identity.Permissions
                                .Append(CommercePermissionCodes.EnrolledDevicesEnroll)
                                .ToHashSet(StringComparer.Ordinal)
                        };
                        return await approvals.ExecuteSensitiveAsync(
                            approvalIdentity,
                            approvalId,
                            request.BusinessId,
                            draftId,
                            null,
                            CommercePermissionCodes.EnrolledDevicesEnroll,
                            operationId,
                            () => service.AuthorizeAsync(authorizedIdentity, request, ct),
                            ct);
                    }))
            .RequireAuthorization("pos.user");

        endpoints.MapPost(
                "/api/pos/v1/enrollments/redeem",
                async (
                    RedeemPosEnrollmentRequest request,
                    PosEnrollmentService service,
                    IServiceProvider services,
                    ILoggerFactory loggerFactory,
                    CancellationToken ct) =>
                    await Handle(async () =>
                    {
                        var package = await service.RedeemAsync(request, ct);
                        return await EnrichBrandingAsync(
                            package,
                            cancellationToken => services
                                .GetRequiredService<ITenantService>()
                                .GetBrandingAsync(package.TenantId, cancellationToken),
                            loggerFactory.CreateLogger("PosEnrollmentBranding"),
                            ct);
                    }))
            .AllowAnonymous();
        return endpoints;
    }

    internal static async Task<PosEnrollmentPackage> EnrichBrandingAsync(
        PosEnrollmentPackage package,
        Func<CancellationToken, Task<TenantBrandingDto>> loadBranding,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var branding = await loadBranding(cancellationToken);
            return package with
            {
                CompanyName = string.IsNullOrWhiteSpace(branding.DisplayName)
                    ? package.BusinessName
                    : branding.DisplayName,
                CompanyLogoSource = branding.LogoUrl
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Device identity and numbering were already committed. Optional
            // visual branding cannot turn that success into a failed redemption.
            logger.LogWarning(
                exception,
                "POS enrollment {DeviceId} completed without optional tenant branding.",
                package.DeviceId);
            return package with
            {
                CompanyName = string.IsNullOrWhiteSpace(package.CompanyName)
                    ? package.BusinessName
                    : package.CompanyName,
                CompanyLogoSource = null
            };
        }
    }

    private static async Task<IResult> Handle<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (PosEnrollmentForbiddenException exception)
        {
            return Results.Problem(exception.Message, statusCode: 403);
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
        catch (PosEnrollmentValidationException exception)
        {
            return Results.Problem(exception.Message, statusCode: 400);
        }
        catch (PosEnrollmentConflictException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: 409,
                title: "PosEnrollmentConflict");
        }
        catch (OfflineAuthenticationLeaseConfigurationException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "PosOfflineAccessUnavailable");
        }
    }
}

public static class PosEnrollmentClaimsExtensions
{
    public static PosEnrollmentUserIdentity ToPosEnrollmentUserIdentity(
        this ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, "tenant_id"),
            principal.PosUserDisplayName(),
            principal.FindAll("permission").Select(x => x.Value)
                .ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(ClaimsPrincipal principal, string type) =>
        Guid.TryParse(principal.FindFirstValue(type), out var value)
            ? value
            : throw new PosEnrollmentForbiddenException(
                $"The authenticated identity lacks claim '{type}'.");
}
