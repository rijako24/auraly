using System.Security.Claims;
using Auraly.Application.Fiscal;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Contracts.Fiscal;
using Microsoft.AspNetCore.Mvc;

namespace Auraly.Api;

public static class FiscalConfigurationApi
{
    public static IEndpointRouteBuilder MapFiscalConfigurationApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/fiscal/configuration")
            .RequireAuthorization("fiscal.user");

        group.MapGet("/", async (HttpContext context, Guid businessId,
            FiscalConfigurationService service, CancellationToken ct) =>
            await Handle(() => service.GetAsync(context.User.ToFiscalConfigurationUser(), businessId, ct)));

        group.MapGet("/devices", async (HttpContext context, Guid businessId,
            FiscalDeviceSeriesService service, CancellationToken ct) =>
            await Handle(() => service.ListAsync(
                context.User.ToFiscalConfigurationUser(), businessId, ct)));

        group.MapPost("/devices/assign", async (HttpContext context, Guid businessId,
            AssignFiscalDeviceSeriesRequest request,
            FiscalDeviceSeriesService service, CancellationToken ct) =>
            await Handle(() => service.AssignAsync(
                context.User.ToFiscalConfigurationUser(), businessId, request, ct)));

        endpoints.MapGet("/api/pos/v1/fiscal/provisioning", async (
                HttpContext context, Guid businessId,
                FiscalDeviceSeriesService service, CancellationToken ct) =>
            {
                var tenantId = RequiredDeviceGuid(
                    context.User, PosAuthenticationDefaults.TenantIdClaim);
                var deviceId = RequiredDeviceGuid(
                    context.User, PosAuthenticationDefaults.DeviceIdClaim);
                var result = await service.GetProvisioningAsync(
                    tenantId, businessId, deviceId, ct);
                return result is null ? Results.NoContent() : Results.Ok(result);
            })
            .RequireAuthorization("pos.synchronization");

        group.MapGet("/onboarding", async (HttpContext context, Guid businessId,
            FiscalOnboardingService service, CancellationToken ct) =>
            await Handle(() => service.GetAsync(
                context.User.ToFiscalConfigurationUser(), businessId, ct)));

        group.MapPost("/onboarding/synchronization/negotiate", async (
            HttpContext context,
            Guid businessId,
            FiscalOnboardingService service,
            IPosSynchronizationPushGateway gateway,
            CancellationToken ct) => await Handle(async () =>
        {
            var user = context.User.ToFiscalConfigurationUser();
            await service.GetAsync(user, businessId, ct);
            var uri = gateway.CreateUserClientAccessUri(
                user.TenantId, businessId, user.UserId, ct);
            return new PosSynchronizationNegotiationResponse(
                uri, DateTimeOffset.UtcNow.AddMinutes(15));
        }));

        group.MapPost("/onboarding/habilitation", async (
            HttpContext context,
            Guid businessId,
            FiscalOnboardingService service,
            CancellationToken ct) => await Handle(async () =>
        {
            var form = await context.Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("certificate")
                ?? throw new FiscalConfigurationValidationException(
                    "Selecciona un certificado PFX/P12.");
            if (file.Length > 2 * 1024 * 1024)
                throw new FiscalConfigurationValidationException(
                    "El certificado no puede superar 2 MB.");
            var extension = Path.GetExtension(file.FileName);
            if (extension is not (".pfx" or ".PFX" or ".p12" or ".P12"))
                throw new FiscalConfigurationValidationException(
                    "El certificado debe ser un archivo PFX o P12.");
            await using var stream = new MemoryStream((int)file.Length);
            await file.CopyToAsync(stream, ct);
            if (!Guid.TryParse(form["testSetId"], out var testSetId))
                throw new FiscalConfigurationValidationException("El TestSetId no es válido.");
            var request = new SaveDianHabilitationConfiguration(
                form["softwareIdentificationCode"].ToString(),
                form["softwarePin"].ToString(),
                testSetId,
                form["certificatePassword"].ToString(),
                stream.ToArray());
            return await service.ConfigureHabilitationAsync(
                context.User.ToFiscalConfigurationUser(), businessId, request, ct);
        })).DisableAntiforgery()
            .WithMetadata(
                new RequestSizeLimitAttribute(3 * 1024 * 1024),
                new RequestFormLimitsAttribute
                {
                    MultipartBodyLengthLimit = 3 * 1024 * 1024
                });

        group.MapPost("/onboarding/numbering-ranges/synchronize", async (
            HttpContext context,
            Guid businessId,
            FiscalOnboardingService service,
            CancellationToken ct) =>
            await Handle(() => service.SynchronizeNumberingRangesAsync(
                context.User.ToFiscalConfigurationUser(), businessId, ct)));

        group.MapPost("/onboarding/activate-production", async (
            HttpContext context,
            Guid businessId,
            ActivateFiscalProductionRequest request,
            FiscalOnboardingService service,
            CancellationToken ct) =>
            await Handle(() => service.ActivateProductionAsync(
                context.User.ToFiscalConfigurationUser(), businessId,
                request.DianNumberingRangeId, ct)));

        group.MapPost("/onboarding/activate-support-document", async (
            HttpContext context,
            Guid businessId,
            ActivateFiscalProductionRequest request,
            FiscalOnboardingService service,
            CancellationToken ct) =>
            await Handle(() => service.ActivateSupportDocumentAsync(
                context.User.ToFiscalConfigurationUser(), businessId,
                request.DianNumberingRangeId, ct)));

        return endpoints;
    }

    private static async Task<IResult> Handle<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (FiscalConfigurationForbiddenException exception)
        { return Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden); }
        catch (FiscalConfigurationValidationException exception)
        { return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest); }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number is 51020 or 51021)
        { return Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden); }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number == 51022)
        { return Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict); }
    }

    private static FiscalConfigurationUser ToFiscalConfigurationUser(this ClaimsPrincipal principal)
    {
        var userId = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var user)
            ? user : throw new FiscalConfigurationForbiddenException("La identidad no contiene un usuario válido.");
        var tenantId = Guid.TryParse(principal.FindFirstValue("tenant_id"), out var tenant)
            ? tenant : throw new FiscalConfigurationForbiddenException("La identidad no contiene una empresa válida.");
        return new FiscalConfigurationUser(userId, tenantId,
            principal.FindAll("permission").Select(x => x.Value).ToHashSet(StringComparer.Ordinal));
    }

    private static Guid RequiredDeviceGuid(ClaimsPrincipal principal, string type) =>
        Guid.TryParse(principal.FindFirstValue(type), out var value) && value != Guid.Empty
            ? value
            : throw new FiscalConfigurationForbiddenException(
                $"La identidad del equipo no contiene '{type}'.");
}

public sealed record ActivateFiscalProductionRequest(Guid DianNumberingRangeId);
