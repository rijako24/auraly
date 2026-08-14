using System.Security.Claims;
using Auraly.Commerce.Taxation.Application;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Commerce.Taxation.Domain;

namespace Auraly.Api;

public static class TaxationApi
{
    public static IEndpointRouteBuilder MapTaxationApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/taxation").RequireAuthorization();
        group.MapGet("/withholding-rules", async (
            HttpContext context, bool includeInactive, WithholdingService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.ListAsync(context.User.ToTaxationIdentity(), includeInactive, ct), Results.Ok));
        group.MapPost("/withholding-rules", async (
            HttpContext context, SaveWithholdingRuleRequest request, WithholdingService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.SaveAsync(context.User.ToTaxationIdentity(), null, request, ct),
                value => Results.Created($"/api/commerce/v1/taxation/withholding-rules/{value.RuleId:D}", value)));
        group.MapPut("/withholding-rules/{ruleId:guid}", async (
            HttpContext context, Guid ruleId, SaveWithholdingRuleRequest request,
            WithholdingService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.SaveAsync(context.User.ToTaxationIdentity(), ruleId, request, ct), Results.Ok));
        group.MapGet("/counterparty-profiles/{counterpartyId:guid}", async (
            HttpContext context, Guid counterpartyId, WithholdingService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.GetProfileAsync(context.User.ToTaxationIdentity(), counterpartyId, ct),
                value => value is null ? Results.NotFound() : Results.Ok(value)));
        group.MapPut("/counterparty-profiles/{counterpartyId:guid}", async (
            HttpContext context, Guid counterpartyId, SaveCounterpartyTaxProfileRequest request,
            WithholdingService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.SaveProfileAsync(context.User.ToTaxationIdentity(),
                request with { CounterpartyId = counterpartyId }, ct), Results.Ok));
        group.MapPost("/withholdings/preview", async (
            HttpContext context, WithholdingPreviewRequest request, WithholdingService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.PreviewAsync(context.User.ToTaxationIdentity(), request, ct), Results.Ok));
        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action, Func<T, IResult> success)
    {
        try { return success(await action()); }
        catch (TaxationForbiddenException exception) { return Results.Problem(exception.Message, statusCode: 403); }
        catch (TaxationValidationException exception) { return Results.Problem(exception.Message, statusCode: 400); }
        catch (WithholdingRuleException exception) { return Results.Problem(exception.Message, statusCode: 400); }
        catch (TaxationConflictException exception) { return Results.Problem(exception.Message, statusCode: 409); }
    }
}

public static class TaxationClaimsPrincipalExtensions
{
    public static TaxationUserIdentity ToTaxationIdentity(this ClaimsPrincipal principal) => new(
        RequiredGuid(principal, "tenant_id"), RequiredGuid(principal, "business_id"),
        RequiredGuid(principal, ClaimTypes.NameIdentifier),
        principal.FindAll("permission").Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal));
    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value) ? value :
            throw new TaxationForbiddenException($"The authenticated identity lacks claim '{claimType}'.");
}
